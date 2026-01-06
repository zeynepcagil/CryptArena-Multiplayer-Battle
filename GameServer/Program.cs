using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;
using GameCommon;

namespace GameServer
{
    class Program
    {
        // --- AĞ DEĞİŞKENLERİ ---
        // TCP: Güvenilir bağlantı (Giriş işlemleri vb. için)
        static TcpListener _tcpListener;
        // UDP: Hızlı veri transferi (Pozisyon güncelleme, anlık hareketler için)
        static UdpClient _udpListener;

        static List<TcpClient> _tcpClients = new List<TcpClient>();
        // UDP istemcilerinin IP adreslerini tutan liste (Tekrarlı kayıt olmaması için HashSet kullanıldı)
        static HashSet<IPEndPoint> _udpEndPoints = new HashSet<IPEndPoint>();

        // --- OYUN DURUM DEĞİŞKENLERİ ---
        // Oyuncu verilerini (Can, Takım, İsim) tutan ana sözlük
        static Dictionary<string, PlayerData> _players = new Dictionary<string, PlayerData>();

        // Oyuncuların en son ne zaman paket gönderdiğini tutar (AFK/Disconnect kontrolü için)
        static Dictionary<string, DateTime> _playerLastSeen = new Dictionary<string, DateTime>();

        static List<string> _activeItems = new List<string>();
        static int _gameTime = GameConstants.GAME_DURATION;
        static bool _isGameRunning = false;
        static string _hostName = ""; // Lobiyi yöneten oyuncu

        // --- THREAD SENKRONİZASYONU ---
        // Farklı thread'lerin (UDP dinleyici, Oyun Döngüsü vb.) aynı anda _players listesine
        // erişip hata (Race Condition) oluşturmasını engellemek için kullanılan kilit nesnesi.
        static readonly object _serverLock = new object();

        static void Main(string[] args)
        {
            // Portları dinlemeye başla
            _tcpListener = new TcpListener(IPAddress.Any, GameCommon.GameCommon.TCP_PORT);
            _udpListener = new UdpClient(GameCommon.GameCommon.UDP_PORT);

            _tcpListener.Start();
            Console.WriteLine($"=== SERVER ONLINE: ITEM SYSTEM ACTIVE ({GameConstants.ITEM_SPAWN_INTERVAL / 1000}s) ===");

            // --- MULTITHREADING KURULUMU ---
            // Server'ın aynı anda birden fazla işi yapabilmesi için görevler thread'lere bölündü:
            new Thread(AcceptClients).Start();  // 1. Yeni TCP bağlantılarını kabul et
            new Thread(UdpBroadcaster).Start(); // 2. Gelen UDP paketlerini işle ve dağıt
            new Thread(GameLoop).Start();       // 3. Oyunun ana mantığını (süre, disconnect) yönet
            new Thread(ItemSpawner).Start();    // 4. Haritaya belirli aralıklarla eşya oluştur
        }

        // Oyunun ana kalp atışı (Saniyede 1 kez çalışır)
        static void GameLoop()
        {
            while (true)
            {
                // Her saniye inaktif (bağlantısı kopan) oyuncuları kontrol et
                CheckDisconnects();

                if (_isGameRunning)
                {
                    if (_gameTime > 0)
                    {
                        _gameTime--;
                        // Kalan süreyi tüm oyunculara bildir
                        BroadcastUdp(MessageBuilder.BuildTimeMessage(_gameTime), null);
                    }
                    else
                    {
                        // Süre biterse berabere veya skor durumuna göre bitir
                        FinishRound(0);
                    }
                }
                else
                {
                    // Oyun başlamadıysa konsol başlığında lobi durumunu göster
                    Console.Title = $"Lobi: {_players.Count} Oyuncu";
                }
                Thread.Sleep(1000); // 1 saniye bekle
            }
        }

        // Düşen (Timeout olan) oyuncuları tespit edip listeden siler
        static void CheckDisconnects()
        {
            // Kritik Bölge: _players ve _playerLastSeen listeleri değiştirileceği için kilitlenir.
            lock (_serverLock)
            {
                List<string> timedOutPlayers = new List<string>();
                DateTime now = DateTime.Now;

                // 5 saniyeden fazla süredir haber alınamayanları bul
                foreach (var entry in _playerLastSeen)
                {
                    if ((now - entry.Value).TotalSeconds > 5)
                    {
                        timedOutPlayers.Add(entry.Key);
                    }
                }

                bool lobbyNeedsUpdate = false;
                foreach (var name in timedOutPlayers)
                {
                    if (_players.ContainsKey(name))
                    {
                        Console.WriteLine($"[DISCONNECT] Oyuncu düştü: {name}");
                        _players.Remove(name);
                        _playerLastSeen.Remove(name);

                        // Eğer düşen kişi Host ise, yetkiyi sıradaki oyuncuya devret
                        if (_hostName == name)
                        {
                            _hostName = _players.Count > 0 ? _players.Keys.First() : "";
                            Console.WriteLine($"[HOST] Yeni Host: {_hostName}");
                        }

                        lobbyNeedsUpdate = true;
                    }
                }

                // Lobi aşamasındaysak ve biri düştüyse güncel listeyi herkese tekrar gönder
                if (lobbyNeedsUpdate && !_isGameRunning)
                {
                    BroadcastLobbyState();
                }
            }
        }

        // Haritada rastgele eşya (can iksiri vb.) oluşturan thread
        static void ItemSpawner()
        {
            Random rnd = new Random();
            while (true)
            {
                if (_isGameRunning)
                {
                    // Belirlenen süre kadar bekle (Örn: 10 saniye)
                    Thread.Sleep(GameConstants.ITEM_SPAWN_INTERVAL);

                    // Oyun hala devam ediyorsa ve haritada maksimum eşya sayısına ulaşılmadıysa
                    if (_isGameRunning && _activeItems.Count < GameConstants.MAX_ITEMS_ON_MAP)
                    {
                        int x = rnd.Next(GameConstants.MIN_X, GameConstants.MAX_X);
                        int y = rnd.Next(GameConstants.MIN_Y + 1, GameConstants.MAX_Y);

                        // Duvarların içine eşya doğmaması için kontrol
                        if (MapManager.IsValidItemSpawnPosition(x, y))
                        {
                            string itemPos = GameUtils.FormatItemPosition(x, y);
                            if (!_activeItems.Contains(itemPos))
                            {
                                _activeItems.Add(itemPos);
                                // Eşyanın oluştuğunu tüm oyunculara bildir
                                BroadcastUdp(MessageBuilder.BuildItemSpawnMessage(x, y), null);
                                Console.WriteLine($"[ITEM] Can iksiri düştü: {x},{y}");
                            }
                        }
                    }
                }
                else Thread.Sleep(1000); // Oyun yoksa işlemciyi yormamak için bekle
            }
        }

        // Oyuncudan gelen veriyle sunucudaki durumu günceller
        static void UpdatePlayerHP(string name, int hp, int team)
        {
            lock (_serverLock) // Thread güvenliği için kilit
            {
                // Oyuncu hareket ettiğinde veya veri gönderdiğinde "Son Görülme" zamanını güncelle
                _playerLastSeen[name] = DateTime.Now;

                if (!_players.ContainsKey(name))
                {
                    // Yeni oyuncu bağlandıysa listeye ekle
                    _players[name] = new PlayerData(name, team, hp, hp) { IsReady = false };
                    Console.WriteLine($"[CONNECT] Yeni Oyuncu: {name}");
                    BroadcastLobbyState();
                }
                else
                {
                    // Mevcut oyuncunun canı değiştiyse güncelle
                    if (_players[name].HP != hp)
                    {
                        _players[name].HP = hp;
                        CheckSuddenDeath(); // Biri öldü mü diye kontrol et
                    }
                    _players[name].Team = team;
                }
            }
        }

        // Ani Ölüm Kontrolü: Bir takımın tüm oyuncuları öldü mü?
        static void CheckSuddenDeath()
        {
            if (!_isGameRunning || _players.Count < 2) return;

            long t1 = _players.Values.Where(p => p.Team == GameConstants.TEAM_LIGHT).Sum(p => (long)p.HP);
            long t2 = _players.Values.Where(p => p.Team == GameConstants.TEAM_SHADOW).Sum(p => (long)p.HP);

            // Bir takımın toplam canı 0 veya altına düştüyse diğer takım kazanır
            if (t1 <= 0 && t2 > 0) FinishRound(GameConstants.TEAM_SHADOW);
            else if (t2 <= 0 && t1 > 0) FinishRound(GameConstants.TEAM_LIGHT);
        }

        // Turu bitirir ve kazananı ilan eder
        static void FinishRound(int winnerOverride)
        {
            _isGameRunning = false;
            int winner = winnerOverride;

            long s1 = _players.Values.Where(p => p.Team == GameConstants.TEAM_LIGHT).Sum(p => (long)p.HP);
            long s2 = _players.Values.Where(p => p.Team == GameConstants.TEAM_SHADOW).Sum(p => (long)p.HP);

            // Eğer kazanan parametre olarak gelmediyse (süre bittiyse), kalan canlara bak
            if (winner == 0)
            {
                if (s1 > s2) winner = GameConstants.TEAM_LIGHT;
                else if (s2 > s1) winner = GameConstants.TEAM_SHADOW;
            }

            BroadcastUdp(MessageBuilder.BuildWinMessage(winner, s1, s2), null);
            Thread.Sleep(5000); // Sonuç ekranı için 5 saniye bekle
            ResetGame(); // Yeni oyun için hazırlık yap
        }

        // Lobideki tüm oyuncuların durumunu (Hazır/Değil, Takım) herkese gönderir
        static void BroadcastLobbyState()
        {
            if (_isGameRunning) return;
            StringBuilder sb = new StringBuilder();
            sb.Append($"{GameCommon.GameCommon.MSG_LOBBY}{_hostName}");

            lock (_serverLock) // Liste değişirken okuma hatası olmasın diye kilit
            {
                foreach (var p in _players.Values)
                    sb.Append($"#{p.Name},{p.Team},{(p.IsReady ? 1 : 0)}");
            }
            BroadcastUdp(sb.ToString(), null);
        }

        static void ResetGame()
        {
            _gameTime = GameConstants.GAME_DURATION;
            _isGameRunning = false;
            _activeItems.Clear();

            lock (_serverLock)
            {
                foreach (var p in _players.Values)
                {
                    p.IsReady = false;
                    p.HP = 100;
                }
            }
            BroadcastUdp(GameCommon.GameCommon.MSG_RESET, null);
            BroadcastLobbyState();
        }

        static void AcceptClients()
        {
            while (true)
            {
                try
                {
                    // TCP bağlantısı sadece handshake (el sıkışma) için kullanılıyor olabilir
                    var client = _tcpListener.AcceptTcpClient();
                    _tcpClients.Add(client);
                    new Thread(() => HandleTcp(client)).Start();
                }
                catch { }
            }
        }

        static void HandleTcp(TcpClient c)
        {
            try { c.GetStream().Read(new byte[1], 0, 1); } catch { }
        }

        // UDP Paketlerini Dinleyen ve İşleyen Ana Metot
        static void UdpBroadcaster()
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    // İstemciden gelen veriyi al
                    byte[] data = _udpListener.Receive(ref remoteEP);
                    string msg = Encoding.UTF8.GetString(data);

                    // Yeni bir uç nokta (istemci) ise listeye ekle
                    if (!_udpEndPoints.Contains(remoteEP)) _udpEndPoints.Add(remoteEP);

                    // --- MESAJ AYRIŞTIRMA (PARSING) ---

                    // 1. Hareket Mesajı mı?
                    if (MessageParser.TryParseMoveMessage(msg, out string name, out _,
                        out _, out _, out int hp, out int maxHp, out int team))
                    {
                        if (string.IsNullOrEmpty(_hostName)) _hostName = name;
                        UpdatePlayerHP(name, hp, team);
                        BroadcastUdp(msg, remoteEP); // Hareketi diğerlerine yansıt
                    }
                    // 2. Hazır Olma Komutu mu?
                    else if (msg.StartsWith(GameCommon.GameCommon.CMD_READY))
                    {
                        string[] parts = msg.Split(':');
                        if (parts.Length >= 3 && _players.ContainsKey(parts[2]))
                        {
                            _players[parts[2]].IsReady = !_players[parts[2]].IsReady;
                            BroadcastLobbyState();
                        }
                    }
                    // 3. Host Oyunu Başlattı mı?
                    else if (msg.StartsWith(GameCommon.GameCommon.CMD_HOST_START))
                    {
                        string[] parts = msg.Split(':');
                        if (parts.Length >= 3 && parts[2] == _hostName && _players.Count >= 2)
                        {
                            _isGameRunning = true;
                            BroadcastUdp(GameCommon.GameCommon.CMD_START, null);
                        }
                    }
                    // 4. Eşya Alındı mı?
                    else if (msg.StartsWith(GameCommon.GameCommon.CMD_ITEM_TAKEN))
                    {
                        string loc = msg.Split(':')[2];
                        if (_activeItems.Contains(loc))
                        {
                            _activeItems.Remove(loc); // Sunucudan sil
                            BroadcastUdp($"{GameCommon.GameCommon.MSG_ITEM_DESTROY}{loc}", null); // İstemcilerden sil
                        }
                    }
                    // Diğer mesajlar (Sohbet vb.)
                    else if (!msg.StartsWith(GameCommon.GameCommon.MSG_MOVE))
                    {
                        BroadcastUdp(msg, remoteEP);
                    }
                }
                catch { }
            }
        }

        // Mesajı bağlı olan tüm UDP istemcilerine gönderir
        static void BroadcastUdp(string msg, IPEndPoint excludeEP)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg);

            // Koleksiyon değişti hatası almamak için listeyi kopyalayarak (.ToList) döngüye sokuyoruz
            var targets = _udpEndPoints.ToList();

            foreach (var ep in targets)
            {
                // Mesajı gönderen kişiye (excludeEP) tekrar aynısını gönderme (Echo'yu engelle)
                if (excludeEP != null && ep.Equals(excludeEP) && msg.StartsWith(GameCommon.GameCommon.MSG_MOVE))
                    continue;
                try { _udpListener.Send(data, data.Length, ep); } catch { }
            }
        }
    }
}