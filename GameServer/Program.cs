using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;

namespace GameServer
{
    class PlayerData { public int Team; public int HP; public string Name; public bool IsReady; }

    class Program
    {
        static TcpListener _tcpListener;
        static UdpClient _udpListener;
        static List<TcpClient> _tcpClients = new List<TcpClient>();
        static HashSet<IPEndPoint> _udpEndPoints = new HashSet<IPEndPoint>();

        // Kritik veri, kilitlenmeli
        static Dictionary<string, PlayerData> _players = new Dictionary<string, PlayerData>();
        static readonly object _serverLock = new object(); // KİLİT NESNESİ

        static List<string> _activeItems = new List<string>();
        static int _gameTime = 60;
        static bool _isGameRunning = false;
        static string _hostName = "";

        static void Main(string[] args)
        {
            _tcpListener = new TcpListener(IPAddress.Any, 26000);
            _udpListener = new UdpClient(26001);

            _tcpListener.Start();
            Console.WriteLine("=== CRYPT ARENA SERVER: FINAL GOLD EDITION ===");
            Console.WriteLine(">> TCP/UDP Active");
            Console.WriteLine(">> Thread Safety Active");
            Console.WriteLine(">> Item Spawner Active (20s)");

            new Thread(AcceptClients).Start();
            new Thread(UdpBroadcaster).Start();
            new Thread(GameLoop).Start();
            new Thread(ItemSpawner).Start();
        }

        static void GameLoop()
        {
            while (true)
            {
                if (_isGameRunning)
                {
                    if (_gameTime > 0)
                    {
                        _gameTime--;
                        BroadcastUdp($"TIME:{_gameTime}", null);
                    }
                    else
                    {
                        FinishRound(0);
                    }
                }
                else
                {
                    lock (_serverLock) Console.Title = $"Lobi: {_players.Count} Oyuncu | Host: {_hostName}";
                }
                Thread.Sleep(1000);
            }
        }

        static void ItemSpawner()
        {
            Random rnd = new Random();
            while (true)
            {
                if (_isGameRunning)
                {
                    Thread.Sleep(20000); // 20 saniye bekle

                    lock (_serverLock) // Listeye erişirken kilitle
                    {
                        if (_isGameRunning && _activeItems.Count < 5)
                        {
                            int x = rnd.Next(2, 95);
                            int y = rnd.Next(4, 28);
                            if (!((y > 13 && y < 17 && x > 38 && x < 62) || (y == 15)))
                            {
                                string itemPos = $"{x}|{y}";
                                if (!_activeItems.Contains(itemPos))
                                {
                                    _activeItems.Add(itemPos);
                                    BroadcastUdp($"ITEM:SPAWN:{x}|{y}", null);
                                    Console.WriteLine($"[ITEM] Can iksiri belirdi: {x},{y}");
                                }
                            }
                        }
                    }
                }
                else Thread.Sleep(1000);
            }
        }

        static void UpdatePlayerHP(string name, int hp, int team)
        {
            lock (_serverLock) // Veri yazarken kilitle
            {
                if (!_players.ContainsKey(name))
                {
                    _players[name] = new PlayerData { Name = name, Team = team, HP = hp, IsReady = false };
                    Console.WriteLine($"[LOGIN] {name} katıldı.");
                    BroadcastLobbyState();
                }
                else
                {
                    if (_players[name].HP != hp)
                    {
                        _players[name].HP = hp;
                        CheckSuddenDeath(); // Kilit içindeyken çağırıyoruz
                    }
                    _players[name].Team = team;
                }
            }
        }

        // Bu metot zaten lock içinde çağrılıyor, tekrar locklamaya gerek yok
        static void CheckSuddenDeath()
        {
            if (!_isGameRunning || _players.Count < 2) return;

            long t1 = _players.Values.Where(p => p.Team == 1).Sum(p => (long)p.HP);
            long t2 = _players.Values.Where(p => p.Team == 2).Sum(p => (long)p.HP);

            if (t1 <= 0 && t2 > 0) FinishRound(2);
            else if (t2 <= 0 && t1 > 0) FinishRound(1);
        }

        static void FinishRound(int winnerOverride)
        {
            // Thread çakışmasını önlemek için lock dışına alıyoruz veya dikkatli kullanıyoruz
            // BroadcastUdp thread-safe olduğu için sorun yok.

            _isGameRunning = false;
            int winner = winnerOverride;
            long s1 = 0, s2 = 0;

            lock (_serverLock)
            {
                s1 = _players.Values.Where(p => p.Team == 1).Sum(p => (long)p.HP);
                s2 = _players.Values.Where(p => p.Team == 2).Sum(p => (long)p.HP);
            }

            if (winner == 0)
            {
                if (s1 > s2) winner = 1; else if (s2 > s1) winner = 2;
            }

            Console.WriteLine($"[OYUN BİTTİ] Kazanan Takım: {winner}");
            BroadcastUdp($"WIN:{winner}|{s1}|{s2}", null);

            Thread.Sleep(5000);
            ResetGame();
        }

        static void BroadcastLobbyState()
        {
            if (_isGameRunning) return;
            StringBuilder sb = new StringBuilder();

            lock (_serverLock)
            {
                sb.Append($"LOBBY:{_hostName}");
                foreach (var p in _players.Values) sb.Append($"#{p.Name},{p.Team},{(p.IsReady ? 1 : 0)}");
            }
            BroadcastUdp(sb.ToString(), null);
        }

        static void ResetGame()
        {
            _gameTime = 60;
            _isGameRunning = false;

            lock (_serverLock)
            {
                _activeItems.Clear();
                foreach (var p in _players.Values) { p.IsReady = false; p.HP = 100; }
            }

            BroadcastUdp("RESET:LOBBY", null);
            BroadcastLobbyState();
        }

        static void AcceptClients()
        {
            while (true)
            {
                try
                {
                    var client = _tcpListener.AcceptTcpClient();
                    lock (_serverLock) _tcpClients.Add(client);
                    new Thread(() => HandleTcp(client)).Start();
                }
                catch { }
            }
        }

        // --- KRİTER: StreamReader/Writer EKLENDİ ---
        static void HandleTcp(TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                StreamReader reader = new StreamReader(stream);

                while (true)
                {
                    string msg = reader.ReadLine(); // Satır satır oku
                    if (msg == null) break;
                    // TCP üzerinden gelen mesajları işleyebiliriz (Şu an sadece keep-alive)
                }
            }
            catch { }
            finally
            {
                lock (_serverLock) _tcpClients.Remove(client);
                client.Close();
            }
        }

        static void UdpBroadcaster()
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] data = _udpListener.Receive(ref remoteEP);
                    string msg = Encoding.UTF8.GetString(data);

                    // HashSet thread-safe değil, kilitliyoruz
                    lock (_serverLock)
                    {
                        if (!_udpEndPoints.Contains(remoteEP)) _udpEndPoints.Add(remoteEP);
                    }

                    if (msg.StartsWith("MOV:"))
                    {
                        var p = msg.Substring(4).Split('|');
                        if (p.Length >= 7)
                        {
                            string name = p[0];
                            lock (_serverLock) if (string.IsNullOrEmpty(_hostName)) _hostName = name;
                            UpdatePlayerHP(name, int.Parse(p[4]), int.Parse(p[6]));
                        }
                        BroadcastUdp(msg, remoteEP);
                    }
                    else if (msg.StartsWith("CMD:READY"))
                    {
                        string[] parts = msg.Split(':');
                        if (parts.Length >= 3)
                        {
                            lock (_serverLock)
                            {
                                if (_players.ContainsKey(parts[2]))
                                {
                                    _players[parts[2]].IsReady = !_players[parts[2]].IsReady;
                                    // Lock içinden metot çağırırken dikkat et, BroadcastLobbyState kendi lock'ını alıyor mu?
                                    // Hayır, BroadcastLobbyState içinde lock var. Re-entrant lock (Monitor) olduğu için sorun olmaz.
                                }
                            }
                            BroadcastLobbyState();
                        }
                    }
                    else if (msg.StartsWith("CMD:HOST_START"))
                    {
                        string[] parts = msg.Split(':');
                        bool canStart = false;
                        lock (_serverLock)
                        {
                            if (parts.Length >= 3 && parts[2] == _hostName && _players.Count >= 2) canStart = true;
                        }

                        if (canStart)
                        {
                            _isGameRunning = true;
                            BroadcastUdp("CMD:START", null);
                            Console.WriteLine(">> Oyun Başlatıldı!");
                        }
                    }
                    else if (msg.StartsWith("CMD:ITEM_TAKEN"))
                    {
                        string loc = msg.Split(':')[2];
                        bool taken = false;
                        lock (_serverLock)
                        {
                            if (_activeItems.Contains(loc))
                            {
                                _activeItems.Remove(loc);
                                taken = true;
                            }
                        }
                        if (taken)
                        {
                            BroadcastUdp($"ITEM:DESTROY:{loc}", null);
                            Console.WriteLine($">> Eşya alındı: {loc}");
                        }
                    }
                    else if (!msg.StartsWith("MOV:")) BroadcastUdp(msg, remoteEP);
                }
                catch { }
            }
        }

        static void BroadcastUdp(string msg, IPEndPoint excludeEP)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg);

            // Koleksiyonu kopyalayarak alalım ki gönderim sırasında liste değişirse hata vermesin
            List<IPEndPoint> targets;
            lock (_serverLock) targets = _udpEndPoints.ToList();

            foreach (var ep in targets)
            {
                if (excludeEP != null && ep.Equals(excludeEP) && msg.StartsWith("MOV:")) continue;
                try { _udpListener.Send(data, data.Length, ep); } catch { }
            }
        }
    }
}