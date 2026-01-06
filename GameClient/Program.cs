using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;
using GameCommon;

namespace GameClient
{
    class Program
    {
        // Ağ Bağlantı Değişkenleri
        private static TcpClient _tcp;          // Güvenilir bağlantı (Giriş, çıkış vb.)
        private static NetworkStream _stream;
        private static UdpClient _udp;          // Hızlı bağlantı (Pozisyon, savaş)
        private static int _tPort = GameCommon.GameCommon.TCP_PORT;
        private static int _uPort = GameCommon.GameCommon.UDP_PORT;
        private static string _serverIp = "127.0.0.1"; // Localhost

        // Oyuncu Durum Değişkenleri
        private static string _myName, _myAvatar, _myProj;
        private static int _myX, _myY, _myHP, _myMaxHP, _myMana, _myMaxMana, _myDmg, _myTeam;
        private static int _dX = 1, _dY = 0;    // Son bakılan yön
        private static bool _isDead = false;
        private static int _selectedClassIndex = 0;

        // Render Alanı (UI payı hariç oyun alanı)
        private static int _arenaWidth = 58;
        private static int _arenaHeight = 28;

        // Çevresel Veriler (Düşmanlar, Eşyalar, Duvarlar)
        private static Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();
        // Interpolasyon yapılmıyor, direkt son görülen yer işaretleniyor
        private static Dictionary<string, DateTime> _lastSeen = new Dictionary<string, DateTime>();

        private static List<(int x, int y)> _activeItems = new List<(int x, int y)>();
        private static List<(int x, int y)> _obstacles = new List<(int x, int y)>();

        // Thread Senkronizasyonu
        // UI thread'i ile Ağ thread'i aynı anda ekrana yazmaya çalışırsa konsol bozulur, o yüzden lock var.
        private static readonly object _lock = new object();
        private static bool _needsRedraw = true; // Sadece değişiklik olduğunda ekranı çiz

        // Oyun Döngüsü Kontrolleri
        private static int _gameTime = GameConstants.GAME_DURATION;
        private static bool _isGameStarted = false;
        private static List<LobbyPlayer> _lobbyPlayers = new List<LobbyPlayer>();
        private static string _hostName = "";
        private static bool _amIHost = false;
        private static string _centerMsg = ""; // Ekranda beliren uyarı mesajları (Kazandı vb.)

        private static DateTime _lastHeartbeat = DateTime.Now;

        static void Main()
        {
            // Emoji desteği için şart
            Console.OutputEncoding = Encoding.UTF8;
            Console.CursorVisible = false;

            try
            {
                // Konsol boyutunu sabitle
                Console.SetWindowSize(60, 30);
                Console.SetBufferSize(60, 30);
            }
            catch { }

            // Uygulama kapanırken sunucuya "öldüm/çıktım" mesajı at (Ghost kalmaması için)
            AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                SendUdp(MessageBuilder.BuildMoveMessage(_myName, GameConstants.EMOJI_GHOST, 0, 0, 0, _myMaxHP, _myTeam));
            };

            Console.Clear();
            DrawBox(10, 10, 40, 5, ConsoleColor.Red);
            Put(18, 12, "CRYPT ARENA", ConsoleColor.White);
            Put(16, 13, "Client Başlatılıyor...", ConsoleColor.DarkGray);
            Thread.Sleep(800);

            InitMap();
            Login();    // Kullanıcı adı ve sınıf seçimi
            Connect();  // Sunucu bağlantısı
        }

        static void InitMap()
        {
            // Common kütüphanesinden harita verisini çek
            var obstacles = MapManager.GetObstacles();
            foreach (var obs in obstacles)
            {
                if (obs.x < _arenaWidth && obs.y < _arenaHeight)
                    _obstacles.Add(obs);
            }
        }

        static void Login()
        {
            // 1. Kullanıcı Adı Girişi
            Console.Clear();
            DrawBox(5, 8, 50, 8, ConsoleColor.Yellow);
            Put(20, 10, "LOBİ GİRİŞİ", ConsoleColor.Yellow);
            Put(10, 12, "Kullanıcı Adı:", ConsoleColor.White);

            Console.ForegroundColor = ConsoleColor.Cyan;
            _myName = ReadInput(25, 12, 12);

            if (string.IsNullOrWhiteSpace(_myName))
                _myName = GameUtils.GenerateRandomName();
            _myName = _myName.Trim();

            // 2. Sınıf Seçimi
            Console.Clear();
            DrawBox(5, 5, 50, 15, ConsoleColor.Cyan);
            Put(20, 7, "SINIF SEÇİMİ", ConsoleColor.Cyan);

            for (int i = 0; i < GameConstants.CLASSES.Length; i++)
            {
                var c = GameConstants.CLASSES[i];
                Put(8, 9 + (i * 2), $"[{i + 1}] {c.Avatar} {c.Name}", ConsoleColor.White);
                Put(25, 9 + (i * 2), $"HP:{c.MHP} MP:{c.MMana} Dmg:{c.Dmg}", ConsoleColor.DarkGray);
            }

            Put(15, 18, "Seçiminiz (1-4): ", ConsoleColor.Yellow);
            int c_choice = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out int r) ? r - 1 : 0;
            _selectedClassIndex = (c_choice < 0 || c_choice >= GameConstants.CLASSES.Length) ? 0 : c_choice;

            // 3. Takım Seçimi
            Console.Clear();
            DrawBox(5, 8, 50, 10, ConsoleColor.Magenta);
            Put(20, 10, "TAKIM SEÇİMİ", ConsoleColor.Magenta);

            Put(10, 12, "[1] 💙 IŞIK (Solda Doğar)", ConsoleColor.Cyan);
            Put(10, 14, "[2] ❤️  GÖLGE (Sağda Doğar)", ConsoleColor.Red);

            Put(15, 16, "Seçiminiz (1-2): ", ConsoleColor.Yellow);
            var k = Console.ReadKey(true).Key;
            _myTeam = (k == ConsoleKey.D2 || k == ConsoleKey.NumPad2) ? GameConstants.TEAM_SHADOW : GameConstants.TEAM_LIGHT;

            // Spawn pozisyonunu ayarla
            var startPos = GameUtils.GetTeamStartPosition(_myTeam);
            Random rnd = new Random();

            if (_myTeam == GameConstants.TEAM_LIGHT)
            {
                _myX = rnd.Next(2, 15);
                _myY = Math.Min(startPos.y, _arenaHeight - 2);
            }
            else
            {
                _myX = rnd.Next(_arenaWidth - 15, _arenaWidth - 2);
                // Gölge takımı için spawn offset ayarı
                _myY = Math.Min(startPos.y, _arenaHeight - 2) - 2;
            }

            _dX = GameUtils.GetStartDirection(_myTeam);

            // Seçilen sınıfın özelliklerini yükle
            var s = GameConstants.CLASSES[_selectedClassIndex];
            _myAvatar = s.Avatar;
            _myHP = _myMaxHP = s.MHP;
            _myMaxMana = _myMana = s.MMana;
            _myDmg = s.Dmg;
            _myProj = s.Proj;

            Console.Clear();
            DrawBox(15, 12, 30, 4, ConsoleColor.Green);
            Put(22, 14, "HAZIRLANIYOR...", ConsoleColor.Green);
            Thread.Sleep(500);
        }

        // Custom Input Okuyucu: Console.ReadLine UI'ı bozduğu için karakter karakter okuyoruz
        static string ReadInput(int x, int y, int maxLength)
        {
            string input = "";
            Console.SetCursorPosition(x, y);

            while (true)
            {
                var keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input = input.Substring(0, input.Length - 1);
                        Console.SetCursorPosition(x + input.Length, y);
                        Console.Write(" "); // Karakteri sil
                        Console.SetCursorPosition(x + input.Length, y);
                    }
                }
                else if (!char.IsControl(keyInfo.KeyChar) && input.Length < maxLength)
                {
                    Console.Write(keyInfo.KeyChar);
                    input += keyInfo.KeyChar;
                }
            }
            return input;
        }

        // ASCII Kutu Çizimi
        static void DrawBox(int x, int y, int w, int h, ConsoleColor c)
        {
            Console.ForegroundColor = c;
            string top = "╔" + new string('═', w - 2) + "╗";
            string mid = "║" + new string(' ', w - 2) + "║";
            string bot = "╚" + new string('═', w - 2) + "╝";

            Put(x, y, top, c);
            for (int i = 1; i < h - 1; i++) Put(x, y + i, mid, c);
            Put(x, y + h - 1, bot, c);
        }

        static void Connect()
        {
            try
            {
                Console.Clear();
                DrawBox(10, 10, 40, 5, ConsoleColor.Yellow);
                Put(18, 12, "SUNUCUYA BAĞLANILIYOR...", ConsoleColor.Yellow);

                _tcp = new TcpClient(_serverIp, _tPort);
                _stream = _tcp.GetStream();
                _udp = new UdpClient();

                Console.Clear();
                DrawBox(10, 10, 40, 5, ConsoleColor.Green);
                Put(22, 12, "BAĞLANTI BAŞARILI!", ConsoleColor.Green);
                Thread.Sleep(200);

                // Dinleyici threadleri başlat
                new Thread(TcpIn).Start();
                new Thread(UdpIn).Start();
                new Thread(ManaRegen).Start();

                Console.Clear();
                UpdatePos(); // İlk pozisyonu gönder
                Loop();      // Oyun döngüsüne gir
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("HATA OLUŞTU!");
                Console.WriteLine($"\n{ex.Message}");
                Console.ReadKey();
            }
        }

        static void ManaRegen()
        {
            while (true)
            {
                Thread.Sleep(GameConstants.MANA_REGEN_INTERVAL);
                if (_isGameStarted && !_isDead && _myMana < _myMaxMana)
                {
                    lock (_lock)
                        _myMana = Math.Min(_myMana + GameConstants.MANA_REGEN_RATE, _myMaxMana);
                    _needsRedraw = true;
                }
            }
        }

        static void TcpIn() { } // Şu an TCP'den veri okumuyoruz, ileride eklenebilir

        // UDP Paketlerini Dinleyen Ana Thread
        static void UdpIn()
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] d = _udp.Receive(ref ep);
                    string m = Encoding.UTF8.GetString(d);

                    // --- Mesaj İşleme ---

                    // Lobi Durumu
                    if (m.StartsWith(GameCommon.GameCommon.MSG_LOBBY))
                    {
                        var parts = m.Substring(GameCommon.GameCommon.MSG_LOBBY.Length).Split('#');
                        _hostName = parts[0];
                        _amIHost = (_hostName == _myName);
                        lock (_lock)
                        {
                            _lobbyPlayers.Clear();
                            for (int i = 1; i < parts.Length; i++)
                            {
                                var info = parts[i].Split(',');
                                if (info.Length >= 3)
                                    _lobbyPlayers.Add(new LobbyPlayer(info[0], int.Parse(info[1]), info[2] == "1"));
                            }
                        }
                        _needsRedraw = true;
                    }
                    // Oyuncu Hareketi
                    else if (MessageParser.TryParseMoveMessage(m, out string name, out string avatar,
                        out int x, out int y, out int hp, out int maxHp, out int team))
                    {
                        if (name == _myName) continue; // Kendimizi tekrar güncellemeyelim
                        if (x >= _arenaWidth || y >= _arenaHeight) continue;

                        lock (_lock)
                        {
                            _lastSeen[name] = DateTime.Now;

                            if (!_enemies.ContainsKey(name))
                                _enemies[name] = new Enemy { Name = name };
                            Put(_enemies[name].X, _enemies[name].Y, "     "); // Eski çizimi temizle
                            _enemies[name].Avatar = avatar;
                            _enemies[name].X = x;
                            _enemies[name].Y = y;
                            _enemies[name].HP = hp;
                            _enemies[name].MaxHP = maxHp;
                            _enemies[name].Team = team;
                        }
                        _needsRedraw = true;
                    }
                    // Saldırı Mesajı
                    else if (MessageParser.TryParseAttackMessage(m, out int ax, out int ay, out int dx,
                        out int dy, out int dmg, out string proj, out int atkTeam))
                    {
                        // Mermi animasyonunu ayrı thread'de başlat ki oyunu dondurmasın
                        new Thread(() => Projectile(ax, ay, dx, dy, dmg, proj, atkTeam)).Start();
                    }
                    // Oyun Süresi
                    else if (m.StartsWith(GameCommon.GameCommon.MSG_TIME))
                    {
                        _gameTime = int.Parse(m.Substring(GameCommon.GameCommon.MSG_TIME.Length));
                        _needsRedraw = true;
                    }
                    // Oyun Başlangıcı
                    else if (m.StartsWith(GameCommon.GameCommon.CMD_START))
                    {
                        lock (_lock) Console.Clear();
                        _isGameStarted = true;
                        _centerMsg = "";
                        _needsRedraw = true;
                    }
                    // Oyun Bitişi
                    else if (m.StartsWith(GameCommon.GameCommon.MSG_WIN))
                    {
                        var p = m.Substring(GameCommon.GameCommon.MSG_WIN.Length).Split('|');
                        int w = int.Parse(p[0]);
                        string sc = p.Length >= 3 ? $"({p[1]}-{p[2]})" : "";
                        _centerMsg = $"{(w == 0 ? "BERABERE!" : (GameUtils.GetTeamNameTurkish(w) + " KAZANDI!"))}\n{sc}";
                        _isGameStarted = false;
                        _needsRedraw = true;
                    }
                    // Eşya Oluşumu
                    else if (m.StartsWith(GameCommon.GameCommon.MSG_ITEM_SPAWN))
                    {
                        var p = m.Split(':')[2].Split('|');
                        int ix = int.Parse(p[0]);
                        int iy = int.Parse(p[1]);

                        // Sınır kontrolü (Modulo ile güvenli alana al)
                        if (ix >= _arenaWidth) ix = ix % (_arenaWidth - 4) + 2;
                        if (iy >= _arenaHeight) iy = iy % (_arenaHeight - 4) + 2;

                        lock (_lock) _activeItems.Add((ix, iy));
                        _needsRedraw = true;
                    }
                    // Eşya Alındı/Yok Oldu
                    else if (m.StartsWith(GameCommon.GameCommon.MSG_ITEM_DESTROY))
                    {
                        var p = m.Split(':')[2].Split('|');
                        int itemX = int.Parse(p[0]);
                        int itemY = int.Parse(p[1]);

                        if (itemX >= _arenaWidth) itemX = itemX % (_arenaWidth - 4) + 2;
                        if (itemY >= _arenaHeight) itemY = itemY % (_arenaHeight - 4) + 2;

                        lock (_lock)
                        {
                            _activeItems.Remove((itemX, itemY));
                            Put(itemX, itemY, " ");
                        }
                        _needsRedraw = true;
                    }
                    // Lobiye Dönüş / Reset
                    else if (m.StartsWith(GameCommon.GameCommon.MSG_RESET))
                    {
                        lock (_lock) Console.Clear();
                        _centerMsg = "";
                        _isGameStarted = false;
                        _myHP = _myMaxHP;
                        _myMana = _myMaxMana;
                        _isDead = false;
                        _myAvatar = GameConstants.CLASSES[_selectedClassIndex].Avatar;
                        _activeItems.Clear();
                        _enemies.Clear();
                        _lastSeen.Clear();

                        // Reset sonrası tekrar pozisyon al
                        Random rnd = new Random();
                        var startPos = GameUtils.GetTeamStartPosition(_myTeam);

                        if (_myTeam == GameConstants.TEAM_LIGHT)
                        {
                            _myX = rnd.Next(2, 15);
                            _myY = Math.Min(startPos.y, _arenaHeight - 2);
                        }
                        else
                        {
                            _myX = rnd.Next(_arenaWidth - 15, _arenaWidth - 2);
                            _myY = Math.Min(startPos.y, _arenaHeight - 2) - 2;
                        }

                        _dX = GameUtils.GetStartDirection(_myTeam);

                        UpdatePos();
                        _needsRedraw = true;
                    }
                }
                catch { }
            }
        }

        // Ana Oyun Döngüsü (Input ve Mantık)
     
        static void Loop()
        {
            while (true)
            {
                // Saniyede 1 kez Heartbeat gönder (Ben buradayım sinyali)
                if ((DateTime.Now - _lastHeartbeat).TotalSeconds > 1.0)
                {
                    UpdatePos();
                    _lastHeartbeat = DateTime.Now;
                }

                // Düşen (Timeout) oyuncuları temizle
                lock (_lock)
                {
                    List<string> timedOutPlayers = new List<string>();
                    foreach (var player in _lastSeen)
                    {
                        if ((DateTime.Now - player.Value).TotalSeconds > 4)
                            timedOutPlayers.Add(player.Key);
                    }

                    foreach (var name in timedOutPlayers)
                    {
                        if (_enemies.ContainsKey(name))
                        {
                            Put(_enemies[name].X, _enemies[name].Y, "     ");
                            _enemies.Remove(name);
                            _lastSeen.Remove(name);
                            _needsRedraw = true;
                        }
                    }
                }

                if (_needsRedraw) { DrawGame(); _needsRedraw = false; }

                // Tuş Kontrolleri
                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true).Key;

                    // Lobi Kontrolleri
                    if (!_isGameStarted)
                    {
                        if (k == ConsoleKey.R)
                            SendUdp(MessageBuilder.BuildReadyCommand(_myName));
                        else if (k == ConsoleKey.Enter && _amIHost)
                        {
                            bool allReady = true;
                            lock (_lock) { foreach (var p in _lobbyPlayers) if (!p.IsReady) allReady = false; }

                            if (allReady && _lobbyPlayers.Count >= 2) SendUdp(MessageBuilder.BuildHostStartCommand(_myName));
                            else
                            {
                                _centerMsg = "Yetersiz/Hazır Değil!"; _needsRedraw = true;
                                Thread.Sleep(1500); _centerMsg = ""; _needsRedraw = true;
                            }
                        }
                    }
                    // Oyun İçi Kontrolleri
                    else
                    {
                        bool mv = false;
                        int newX = _myX, newY = _myY;

                        if (k == ConsoleKey.W && _myY > 2) { newY--; _dX = 0; _dY = -1; mv = true; }
                        else if (k == ConsoleKey.S && _myY < _arenaHeight - 1) { newY++; _dX = 0; _dY = 1; mv = true; }
                        else if (k == ConsoleKey.A && _myX > 1) { newX--; _dX = -1; _dY = 0; mv = true; }
                        else if (k == ConsoleKey.D && _myX < _arenaWidth - 1) { newX++; _dX = 1; _dY = 0; mv = true; }

                        // --- DEĞİŞİKLİK YAPILAN KISIM (SPACEBAR/ATEŞ ETME) ---
                        else if (k == ConsoleKey.Spacebar && !_isDead)
                        {
                            int manaCost = 20; // Saldırı bedeli (Bunu isteğine göre değiştirebilirsin)
                            bool canAttack = false;

                            // Thread güvenliği için lock kullanıyoruz çünkü ManaRegen thread'i de bu değişkeni elliyor
                            lock (_lock)
                            {
                                if (_myMana >= manaCost)
                                {
                                    _myMana -= manaCost; // Manayı düş
                                    canAttack = true;
                                }
                            }

                            if (canAttack)
                            {
                                _needsRedraw = true; // HUD'daki mana barını güncellemek için
                                                     // Ateş etme (Animasyon ve Sunucuya bildirme)
                                new Thread(() => Projectile(_myX + _dX, _myY + _dY, _dX, _dY, _myDmg, _myProj, _myTeam)).Start();
                                SendUdp(MessageBuilder.BuildAttackMessage(_myX + _dX, _myY + _dY, _dX, _dY, _myDmg, _myProj, _myTeam));
                            }
                        }
                        // --- DEĞİŞİKLİK SONU ---

                        if (mv)
                        {
                            // Çarpışma Kontrolü (Collision)
                            if (!_obstacles.Contains((newX, newY)) && !_obstacles.Contains((newX + 1, newY)))
                            {
                                lock (_lock)
                                {
                                    Put(_myX, _myY, "  "); // Eskiyi sil
                                    _myX = newX; _myY = newY;

                                    // Eşya toplama kontrolü
                                    if (_activeItems.Contains((_myX, _myY)))
                                    {
                                        _myHP = Math.Min(_myHP + GameConstants.ITEM_HEAL_AMOUNT, _myMaxHP);
                                        _activeItems.Remove((_myX, _myY));
                                        SendUdp(MessageBuilder.BuildItemTakenCommand(_myX, _myY));
                                    }
                                    Put(_myX, _myY, _myAvatar, _isDead ? ConsoleColor.DarkGray : GameUtils.GetTeamColor(_myTeam));
                                }
                                UpdatePos();
                            }
                        }
                    }
                }
                Thread.Sleep(33); // ~30 FPS
            }
        }

        static void DrawGame()
        {
            lock (_lock)
            {
                // Uyarı mesajları çizimi
                if (!string.IsNullOrEmpty(_centerMsg) && !_isGameStarted && !_centerMsg.Contains("KAZANDI"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Put(10, 15, $"⚠️ {_centerMsg} ⚠️");
                }

                // Oyun sonu ekranı
                if (!string.IsNullOrEmpty(_centerMsg) && _centerMsg.Contains("KAZANDI"))
                {
                    Console.Clear();
                    string[] lines = _centerMsg.Split('\n');
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    DrawBox(4, 11, 52, 6, ConsoleColor.Yellow);
                    Put(6, 13, lines[0].PadRight(48));
                    if (lines.Length > 1) Put(6, 14, lines[1].PadRight(48));
                    Put(15, 16, "Lobiye dönülüyor...", ConsoleColor.White);
                    return;
                }

                // Lobi Ekranı
                if (!_isGameStarted)
                {
                    Console.Clear();
                    DrawBox(2, 1, 56, 28, ConsoleColor.Cyan);
                    Put(20, 2, "OYUNCU LİSTESİ", ConsoleColor.Cyan);
                    Put(4, 4, $"HOST: {_hostName}");
                    Put(4, 6, "OYUNCU        TAKIM   DURUM");
                    Put(4, 7, "--------------------------");

                    int y = 8;
                    int readyCount = 0;
                    foreach (var p in _lobbyPlayers)
                    {
                        Put(4, y, p.Name.PadRight(12), ConsoleColor.White);
                        Put(17, y, GameUtils.GetTeamNameTurkish(p.Team).Substring(0, 1), GameUtils.GetTeamColor(p.Team));
                        Put(25, y, p.IsReady ? "HAZIR" : "...", p.IsReady ? ConsoleColor.Green : ConsoleColor.DarkGray);
                        y++;
                        if (p.IsReady) readyCount++;
                    }
                    Put(4, y + 2, $"HAZIR: {readyCount}/{_lobbyPlayers.Count}", ConsoleColor.Yellow);
                    if (_amIHost) Put(4, y + 4, "[ENTER] - BAŞLAT", ConsoleColor.Green);
                    else Put(4, y + 4, "HOST başlatacak...", ConsoleColor.White);
                    Put(4, y + 6, "[R] - HAZIR OL", ConsoleColor.Cyan);
                    return;
                }

                // HUD (Üst Bilgi Çubuğu)
                Console.SetCursorPosition(0, 0);
                Console.ForegroundColor = GameUtils.GetTeamColor(_myTeam);
                string tName = GameUtils.GetTeamName(_myTeam);
                string hud = $"[{tName}] HP:{_myHP} MP:{_myMana} SÜRE:{_gameTime}".PadRight(Console.WindowWidth - 1);
                if (hud.Length >= Console.WindowWidth) hud = hud.Substring(0, Console.WindowWidth - 1);
                Console.Write(hud);

                Console.SetCursorPosition(0, 1);
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(new string('=', Console.WindowWidth - 1));

                // Nesneleri Çiz
                Console.ForegroundColor = ConsoleColor.DarkGray;
                foreach (var w in _obstacles) { Put(w.x, w.y, GameConstants.EMOJI_WALL); }

                Console.ForegroundColor = ConsoleColor.Red;
                foreach (var item in _activeItems) { Put(item.x, item.y, GameConstants.EMOJI_ITEM); }

                foreach (var e in _enemies.Values)
                {
                    if (e.X < 1 || e.Y < 2) continue;

                    if (e.IsDead)
                        Put(e.X, e.Y, $"{GameConstants.EMOJI_DEAD}", ConsoleColor.DarkGray);
                    else
                        Put(e.X, e.Y, $"{e.Avatar}{e.HP}", GameUtils.GetTeamColor(e.Team));
                }

                Put(_myX, _myY, _myAvatar, _isDead ? ConsoleColor.DarkGray : GameUtils.GetTeamColor(_myTeam));
            }
        }

        // Mermi Animasyonu
        static void Projectile(int x, int y, int dx, int dy, int dmg, string ic, int shooterTeam)
        {
            int range = (dx != 0) ? GameConstants.PROJECTILE_HORIZONTAL_RANGE : GameConstants.PROJECTILE_VERTICAL_RANGE;
            int sleepTime = (dx != 0) ? GameConstants.PROJECTILE_HORIZONTAL_SPEED : GameConstants.PROJECTILE_VERTICAL_SPEED;

            for (int i = 0; i < range; i++)
            {
                if (!MapManager.IsValidPosition(x, y) || _obstacles.Contains((x, y))) break;
                if (x >= _arenaWidth || y >= _arenaHeight) break;

                // Vurulma Kontrolü (Client-side prediction, asıl hesap sunucuda olabilir ama burada görsel efekt için)
                if (shooterTeam != _myTeam && !_isDead && y == _myY && (x >= _myX - 1 && x <= _myX + 1))
                { Hit(dmg); break; }

                Put(x, y, ic, ConsoleColor.Yellow);
                Thread.Sleep(sleepTime);

                // Mermi geçtikten sonra arkasını temizle veya eski objeyi geri çiz
                lock (_lock)
                {
                    bool res = false;
                    if (_obstacles.Contains((x, y))) { Put(x, y, GameConstants.EMOJI_WALL, ConsoleColor.Gray); res = true; }
                    else if (_activeItems.Contains((x, y))) { Put(x, y, GameConstants.EMOJI_ITEM, ConsoleColor.Red); res = true; }
                    else if (y == _myY && (x == _myX || x == _myX + 1 || x == _myX - 1))
                    {
                        Put(_myX, _myY, _myAvatar, _isDead ? ConsoleColor.DarkGray : GameUtils.GetTeamColor(_myTeam));
                        res = true;
                    }
                    if (!res)
                    {
                        foreach (var e in _enemies.Values)
                            if (y == e.Y && (x == e.X || x == e.X + 1))
                            {
                                Put(e.X, e.Y, $"{e.Avatar}{e.HP}", GameUtils.GetTeamColor(e.Team));
                                res = true;
                                break;
                            }
                    }
                    if (!res) Put(x, y, "  ");
                }
                x += dx; y += dy;
            }
            _needsRedraw = true;
        }

        static void Hit(int d)
        {
            lock (_lock)
            {
                _myHP = Math.Max(0, _myHP - d);
                if (_myHP <= 0)
                {
                    _isDead = true;
                    _myAvatar = GameConstants.EMOJI_GHOST;
                }
            }
            SendStatus();
            UpdatePos();
            _needsRedraw = true;
        }

        // Thread-Safe Ekrana Yazma Metodu
        static void Put(int x, int y, string s, ConsoleColor c = ConsoleColor.White)
        {
            try
            {
                if (x < 0 || y < 0 || y >= Console.WindowHeight || x >= Console.WindowWidth) return;
                if (x + s.Length > Console.WindowWidth) s = s.Substring(0, Console.WindowWidth - x);
                lock (_lock) { Console.SetCursorPosition(x, y); Console.ForegroundColor = c; Console.Write(s); }
            }
            catch { }
        }

        static void UpdatePos()
        {
            SendUdp(MessageBuilder.BuildMoveMessage(_myName, _myAvatar, _myX, _myY, _myHP, _myMaxHP, _myTeam));
        }

        static void SendStatus()
        {
            byte[] d = Encoding.UTF8.GetBytes(MessageBuilder.BuildStatusMessage(_myName, _myHP, _myMaxHP));
            try { _stream.Write(d, 0, d.Length); } catch { }
        }

        static void SendUdp(string m)
        {
            byte[] d = Encoding.UTF8.GetBytes(m);
            try { _udp.Send(d, d.Length, _serverIp, _uPort); } catch { }
        }
    }
}