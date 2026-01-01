using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GameClient
{
    class Enemy { public string Name, Avatar; public int X, Y, HP, MaxHP, Team; public bool IsDead => HP <= 0; }
    struct PlayerClass { public string Name, Avatar, Proj; public int MHP, MMana, Dmg, Cost; }
    struct LobbyPlayer { public string Name; public int Team; public bool IsReady; }

    class Program
    {
        private static StreamWriter _sWriter; 
        private static TcpClient _tcp;
        private static NetworkStream _stream;
        private static UdpClient _udp;
        private static int _tPort = 26000, _uPort = 26001;
        private static string _serverIp = "";

        private static string _myName, _myAvatar, _myProj;
        private static int _myX, _myY, _myHP, _myMaxHP, _myMana, _myMaxMana, _myDmg, _cost, _myTeam;
        private static int _dX = 1, _dY = 0;
        private static bool _isDead = false;
        private static int _selectedClassIndex = 0;

        private static Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();
        private static List<(int x, int y)> _activeItems = new List<(int x, int y)>();

        private static readonly object _lock = new object();
        private static bool _needsRedraw = true;

        private static int _gameTime = 60;
        private static bool _isGameStarted = false;
        private static List<LobbyPlayer> _lobbyPlayers = new List<LobbyPlayer>();
        private static string _hostName = "";
        private static bool _amIHost = false;
        private static string _centerMsg = "";
        private static string _debugLastMsg = "Bekleniyor...";

        private static List<(int x, int y)> _obstacles = new List<(int x, int y)>();
        private static PlayerClass[] _classes = {
            new PlayerClass { Name="Necromancer", Avatar="🧙", MHP=80,  MMana=100, Dmg=30, Cost=20, Proj="💀" },
            new PlayerClass { Name="Paladin",     Avatar="🛡️", MHP=150, MMana=60,  Dmg=10, Cost=10, Proj="✨" },
            new PlayerClass { Name="Rogue",       Avatar="🥷", MHP=100, MMana=80,  Dmg=15, Cost=10, Proj="🗡️" },
            new PlayerClass { Name="Vampire",     Avatar="🧛", MHP=120, MMana=90,  Dmg=12, Cost=15, Proj="🩸" }
        };

        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.CursorVisible = false;
            try { Console.SetWindowSize(100, 30); Console.SetBufferSize(100, 30); } catch { }

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
   ______                  _     ___                       
  / ____/______  ______  _| |_  /   |  ________  ____  ____ 
 / /   / ___/ / / / __ \/_   _|/ /| | / ___/ _ \/ __ \/ __ \
/ /___/ /  / /_/ / /_/ /  |_| / ___ |/ /  /  __/ / / / /_/ /
\____/_/   \__, / .___/      /_/  |_/_/   \___/_/ /_/\__,_/ 
          /____/_/                                          
            ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n[1] OYUNU KUR (Host)");
            Console.WriteLine("[2] KATIL (Client)");
            Console.Write("\nSeçim: ");

            var k = Console.ReadKey(true).Key;
            if (k == ConsoleKey.D1 || k == ConsoleKey.NumPad1) { _serverIp = "127.0.0.1"; Console.WriteLine("\n\nLobi kuruluyor..."); }
            else { Console.Write("\n\nSunucu IP: "); string ip = Console.ReadLine(); _serverIp = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim(); Console.WriteLine($"\n>> {_serverIp}..."); }
            Thread.Sleep(500);

            InitMap(); Login(); Connect();
        }

        static void InitMap()
        {
            for (int x = 40; x < 60; x++) _obstacles.Add((x, 15));
            for (int y = 8; y < 12; y++) { _obstacles.Add((20, y)); _obstacles.Add((21, y)); }
            for (int y = 8; y < 12; y++) { _obstacles.Add((80, y)); _obstacles.Add((81, y)); }
            for (int x = 30; x < 35; x++) _obstacles.Add((x, 22));
            for (int x = 65; x < 70; x++) _obstacles.Add((x, 22));
        }

        static void Login()
        {
            Console.Clear();
            Console.Write("Kullanıcı Adı: "); _myName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(_myName)) _myName = "Hero" + new Random().Next(100, 999);
            _myName = _myName.Trim();

            Console.WriteLine("\nSınıf Seç:"); for (int i = 0; i < 4; i++) Console.WriteLine($"[{i + 1}] {_classes[i].Name}");
            int c = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out int r) ? r - 1 : 0;
            _selectedClassIndex = (c < 0 || c > 3) ? 0 : c;

            Console.WriteLine("\nTakım Seç:\n[1] Işık (Mavi)\n[2] Gölge (Kırmızı)");
            var k = Console.ReadKey(true).Key;
            _myTeam = (k == ConsoleKey.D2 || k == ConsoleKey.NumPad2) ? 2 : 1;

            if (_myTeam == 1) { _myX = 5; _myY = 15; _dX = 1; } else { _myX = 94; _myY = 15; _dX = -1; }
            var s = _classes[_selectedClassIndex];
            _myAvatar = s.Avatar; _myHP = _myMaxHP = s.MHP; _myMaxMana = _myMana = s.MMana; _myDmg = s.Dmg; _cost = s.Cost; _myProj = s.Proj;
        }

        static void Connect()
        {
            try
            {
                _tcp = new TcpClient(_serverIp, _tPort);
                _stream = _tcp.GetStream();

                // --- KRİTER İÇİN EKLENDİ: StreamWriter ---
                _sWriter = new StreamWriter(_stream) { AutoFlush = true };
                // -----------------------------------------

                _udp = new UdpClient();

                new Thread(TcpIn).Start();
                new Thread(UdpIn).Start();
                new Thread(ManaRegen).Start();

                Console.Clear();
                UpdatePos();
                Loop();
            }
            catch { Console.WriteLine("\nSunucu Kapalı veya IP Yanlış!"); Console.ReadKey(); }
        }

        static void ManaRegen() { while (true) { Thread.Sleep(1000); if (_isGameStarted && !_isDead && _myMana < _myMaxMana) { lock (_lock) _myMana = Math.Min(_myMana + 5, _myMaxMana); _needsRedraw = true; } } }
        static void TcpIn() { }

        static void UdpIn()
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] d = _udp.Receive(ref ep);
                    string m = Encoding.UTF8.GetString(d);
                    if (!m.StartsWith("MOV")) _debugLastMsg = m;

                    if (m.StartsWith("LOBBY:"))
                    {
                        var parts = m.Substring(6).Split('#'); _hostName = parts[0]; _amIHost = (_hostName == _myName);
                        lock (_lock)
                        {
                            _lobbyPlayers.Clear();
                            for (int i = 1; i < parts.Length; i++)
                            {
                                var info = parts[i].Split(',');
                                if (info.Length >= 3) _lobbyPlayers.Add(new LobbyPlayer { Name = info[0], Team = int.Parse(info[1]), IsReady = info[2] == "1" });
                            }
                        }
                        _needsRedraw = true;
                    }
                    else if (m.StartsWith("MOV:"))
                    {
                        var p = m.Substring(4).Split('|');
                        if (p[0] == _myName) continue;
                        lock (_lock)
                        {
                            if (!_enemies.ContainsKey(p[0])) _enemies[p[0]] = new Enemy { Name = p[0] };
                            Put(_enemies[p[0]].X, _enemies[p[0]].Y, "      ");
                            _enemies[p[0]].Avatar = p[1]; _enemies[p[0]].X = int.Parse(p[2]); _enemies[p[0]].Y = int.Parse(p[3]);
                            if (p.Length >= 6) { _enemies[p[0]].HP = int.Parse(p[4]); _enemies[p[0]].MaxHP = int.Parse(p[5]); }
                            if (p.Length >= 7) _enemies[p[0]].Team = int.Parse(p[6]);
                        }
                        _needsRedraw = true;
                    }
                    else if (m.StartsWith("ATK:"))
                    {
                        var p = m.Substring(4).Split('|');
                        new Thread(() => Projectile(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]), p[5], int.Parse(p[6]))).Start();
                    }
                    else if (m.StartsWith("TIME:")) { _gameTime = int.Parse(m.Substring(5)); _needsRedraw = true; }
                    else if (m.StartsWith("CMD:START")) { lock (_lock) Console.Clear(); _isGameStarted = true; _centerMsg = ""; _needsRedraw = true; }
                    else if (m.StartsWith("WIN:"))
                    {
                        var p = m.Substring(4).Split('|'); int w = int.Parse(p[0]);
                        string sc = p.Length >= 3 ? $"({p[1]} - {p[2]})" : "";
                        _centerMsg = $"{(w == 0 ? "BERABERE!" : (w == 1 ? "IŞIK KAZANDI!" : "GÖLGE KAZANDI!"))}\n{sc}";
                        _isGameStarted = false; _needsRedraw = true;
                    }
                    else if (m.StartsWith("ITEM:SPAWN:"))
                    {
                        var p = m.Split(':')[2].Split('|');
                        lock (_lock) _activeItems.Add((int.Parse(p[0]), int.Parse(p[1])));
                        _needsRedraw = true;
                    }
                    else if (m.StartsWith("ITEM:DESTROY:"))
                    {
                        var p = m.Split(':')[2].Split('|');
                        lock (_lock)
                        {
                            _activeItems.Remove((int.Parse(p[0]), int.Parse(p[1])));
                            Put(int.Parse(p[0]), int.Parse(p[1]), " ");
                        }
                        _needsRedraw = true;
                    }
                    else if (m.StartsWith("RESET:LOBBY"))
                    {
                        lock (_lock) Console.Clear();
                        _centerMsg = ""; _isGameStarted = false; _myHP = _myMaxHP; _myMana = _myMaxMana; _isDead = false;
                        _myAvatar = _classes[_selectedClassIndex].Avatar;
                        _activeItems.Clear();
                        if (_myTeam == 1) { _myX = 5; _myY = 15; _dX = 1; } else { _myX = 94; _myY = 15; _dX = -1; }
                        UpdatePos(); _needsRedraw = true;
                    }
                }
                catch { }
            }
        }

        static void Loop()
        {
            while (true)
            {
                if (_needsRedraw) { DrawGame(); _needsRedraw = false; }

                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true).Key;

                    if (!_isGameStarted)
                    {
                        if (k == ConsoleKey.R) SendUdp($"CMD:READY:{_myName}");
                        else if (k == ConsoleKey.Enter && _amIHost) SendUdp($"CMD:HOST_START:{_myName}");
                    }
                    else
                    {
                        bool mv = false; int newX = _myX, newY = _myY;
                        if (k == ConsoleKey.W && _myY > 3) { newY--; _dX = 0; _dY = -1; mv = true; }
                        else if (k == ConsoleKey.S && _myY < 28) { newY++; _dX = 0; _dY = 1; mv = true; }
                        else if (k == ConsoleKey.A && _myX > 2) { newX--; _dX = -1; _dY = 0; mv = true; }
                        else if (k == ConsoleKey.D && _myX < 95) { newX++; _dX = 1; _dY = 0; mv = true; }
                        else if (k == ConsoleKey.Spacebar && !_isDead && _myMana >= _cost)
                        {
                            lock (_lock) _myMana -= _cost; _needsRedraw = true;
                            new Thread(() => Projectile(_myX + _dX, _myY + _dY, _dX, _dY, _myDmg, _myProj, _myTeam)).Start();
                            SendUdp($"ATK:{_myX + _dX}|{_myY + _dY}|{_dX}|{_dY}|{_myDmg}|{_myProj}|{_myTeam}");
                        }

                        if (mv)
                        {
                            if (!_obstacles.Contains((newX, newY)) && !_obstacles.Contains((newX + 1, newY)))
                            {
                                lock (_lock)
                                {
                                    Put(_myX, _myY, "  ");
                                    _myX = newX; _myY = newY;

                                    // EŞYA TOPLAMA
                                    if (_activeItems.Contains((_myX, _myY)))
                                    {
                                        _myHP = Math.Min(_myHP + 20, _myMaxHP);
                                        _activeItems.Remove((_myX, _myY));
                                        SendUdp($"CMD:ITEM_TAKEN:{_myX}|{_myY}");
                                    }

                                    Console.SetCursorPosition(_myX, _myY);
                                    Console.ForegroundColor = _isDead ? ConsoleColor.DarkGray : (_myTeam == 1 ? ConsoleColor.Cyan : ConsoleColor.Red);
                                    Console.Write(_myAvatar);
                                }
                                UpdatePos();
                            }
                        }
                    }
                }
                Thread.Sleep(33);
            }
        }

        static void DrawGame()
        {
            lock (_lock)
            {
                // DEBUG
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.SetCursorPosition(0, 29);
                string debugInfo = $"DEBUG: {_debugLastMsg}".PadRight(99);
                Console.Write(debugInfo.Substring(0, 99));

                // KAZANMA EKRANI
                if (!string.IsNullOrEmpty(_centerMsg))
                {
                    Console.Clear(); string[] lines = _centerMsg.Split('\n');
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.SetCursorPosition(25, 12); Console.Write("╔══════════════════════════════════════════════╗");
                    Console.SetCursorPosition(25, 13); Console.Write($"║ {lines[0].PadRight(44)} ║");
                    if (lines.Length > 1) { Console.SetCursorPosition(25, 14); Console.Write("╠══════════════════════════════════════════════╣"); Console.SetCursorPosition(25, 15); Console.Write($"║ {lines[1].PadRight(44)} ║"); }
                    Console.SetCursorPosition(25, lines.Length > 1 ? 16 : 14); Console.Write("╚══════════════════════════════════════════════╝");
                    Console.SetCursorPosition(30, 18); Console.Write("Lobiye dönülüyor...");
                    return;
                }

                // LOBI
                if (!_isGameStarted)
                {
                    Console.Clear(); Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.SetCursorPosition(30, 5); Console.Write("╔══════════ LOBİ: OYUNCU LİSTESİ ══════════╗");
                    Console.SetCursorPosition(30, 6); Console.Write($"║ HOST: {_hostName.PadRight(34)} ║");
                    Console.SetCursorPosition(30, 7); Console.Write("╠════════════════╦════════════╦════════════╣");
                    Console.SetCursorPosition(30, 8); Console.Write("║ OYUNCU ADI     ║ TAKIM      ║ DURUM      ║");
                    Console.SetCursorPosition(30, 9); Console.Write("╠════════════════╬════════════╬════════════╣");
                    int y = 10;
                    foreach (var p in _lobbyPlayers)
                    {
                        Console.SetCursorPosition(30, y++);
                        Console.Write("║ "); Console.ForegroundColor = ConsoleColor.White; Console.Write(p.Name.PadRight(14));
                        Console.ForegroundColor = p.Team == 1 ? ConsoleColor.Cyan : ConsoleColor.Red; Console.Write(" ║ " + (p.Team == 1 ? "IŞIK  " : "GÖLGE "));
                        Console.ForegroundColor = p.IsReady ? ConsoleColor.Green : ConsoleColor.DarkGray; Console.Write("   ║ " + (p.IsReady ? "HAZIR   " : "BEKLİYOR") + "   ");
                        Console.ForegroundColor = ConsoleColor.Cyan; Console.Write("║");
                    }
                    Console.SetCursorPosition(30, y); Console.ForegroundColor = ConsoleColor.Cyan; Console.Write("╚════════════════╩════════════╩════════════╝");
                    Console.SetCursorPosition(30, y + 2);
                    if (_amIHost) { Console.ForegroundColor = ConsoleColor.Green; Console.Write(">> OYUNU BAŞLATMAK İÇİN [ENTER] TUŞUNA BAS <<"); }
                    else { Console.ForegroundColor = ConsoleColor.White; Console.Write(">> HAZIR OLMAK İÇİN [R] TUŞUNA BAS <<"); }
                    return;
                }

                // OYUN
                Console.SetCursorPosition(0, 0); Console.ForegroundColor = _myTeam == 1 ? ConsoleColor.Cyan : ConsoleColor.Red;
                string tName = _myTeam == 1 ? "LIGHT" : "SHADOW";
                Console.Write($"[{tName}] HP: {_myHP}/{_myMaxHP} | MP: {_myMana}/{_myMaxMana} | SÜRE: {_gameTime} | ADIN: {_myName}".PadRight(95));
                Console.SetCursorPosition(0, 1); Console.ForegroundColor = ConsoleColor.Gray; Console.Write(new string('=', 95));

                Console.ForegroundColor = ConsoleColor.DarkGray;
                foreach (var w in _obstacles) { Console.SetCursorPosition(w.x, w.y); Console.Write("▓"); }

                // --- GÖRSEL DÜZELTME: İKSİR EMOJİSİ VE RENGİ ---
                Console.ForegroundColor = ConsoleColor.Red;
                foreach (var item in _activeItems)
                {
                    Console.SetCursorPosition(item.x, item.y);
                    Console.Write("🍷"); // 2x büyük gözükür
                }
                // ------------------------------------------------

                foreach (var e in _enemies.Values)
                {
                    if (e.X < 1 || e.Y < 3) continue;
                    Console.SetCursorPosition(e.X, e.Y);
                    if (e.IsDead) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write($"💀{e.Name}"); }
                    else { Console.ForegroundColor = e.Team == 1 ? ConsoleColor.Cyan : ConsoleColor.Red; Console.Write($"{e.Avatar}{e.HP}"); }
                }
                Console.SetCursorPosition(_myX, _myY);
                Console.ForegroundColor = _isDead ? ConsoleColor.DarkGray : (_myTeam == 1 ? ConsoleColor.Cyan : ConsoleColor.Red);
                Console.Write(_myAvatar);
            }
        }

        static void Projectile(int x, int y, int dx, int dy, int dmg, string ic, int shooterTeam)
        {
            int range = (dx != 0) ? 30 : 15; int sleepTime = (dx != 0) ? 30 : 60;
            for (int i = 0; i < range; i++)
            {
                if (x < 1 || x > 95 || y < 3 || y > 29 || _obstacles.Contains((x, y))) break;
                if (shooterTeam != _myTeam && !_isDead && y == _myY && (x >= _myX - 1 && x <= _myX + 1)) { Hit(dmg); break; }
                Put(x, y, ic, ConsoleColor.Yellow); Thread.Sleep(sleepTime);
                lock (_lock)
                {
                    bool res = false;
                    if (_obstacles.Contains((x, y))) { Put(x, y, "▓", ConsoleColor.Gray); res = true; }
                    // Mermi geçince iksir geri gelsin
                    else if (_activeItems.Contains((x, y))) { Put(x, y, "🍷", ConsoleColor.Red); res = true; }
                    else if (y == _myY && (x == _myX || x == _myX + 1 || x == _myX - 1)) { Put(_myX, _myY, _myAvatar, _isDead ? ConsoleColor.DarkGray : (_myTeam == 1 ? ConsoleColor.Cyan : ConsoleColor.Red)); res = true; }
                    if (!res) { foreach (var e in _enemies.Values) if (y == e.Y && (x == e.X || x == e.X + 1)) { Put(e.X, e.Y, e.Avatar, e.Team == 1 ? ConsoleColor.Cyan : ConsoleColor.Red); res = true; break; } }
                    if (!res) Put(x, y, "  ");
                }
                x += dx; y += dy;
            }
            _needsRedraw = true;
        }

        static void Hit(int d) { lock (_lock) { _myHP = Math.Max(0, _myHP - d); if (_myHP <= 0) { _isDead = true; _myAvatar = "👻"; } } SendStatus(); UpdatePos(); _needsRedraw = true; }
        static void Put(int x, int y, string s, ConsoleColor c = ConsoleColor.White) { if (x < 0 || y < 0 || x >= 100 || y >= 30) return; lock (_lock) { Console.SetCursorPosition(x, y); Console.ForegroundColor = c; Console.Write(s); } }
        static void UpdatePos() { SendUdp($"MOV:{_myName}|{_myAvatar}|{_myX}|{_myY}|{_myHP}|{_myMaxHP}|{_myTeam}"); }
        static void SendStatus()
        {
            try
            {
                // --- ESKİSİ (Byte Array) SİLİNDİ ---
                // byte[] d = Encoding.UTF8.GetBytes($"STAT:{_myName}|{_myHP}|{_myMaxHP}"); 
                // _stream.Write(d, 0, d.Length); 

                // --- YENİSİ (StreamWriter) EKLENDİ ---
                // TCP üzerinden metin tabanlı veri yolluyoruz
                _sWriter.WriteLine($"STAT:{_myName}|{_myHP}|{_myMaxHP}");
            }
            catch { }
        }
        static void SendUdp(string m) { byte[] d = Encoding.UTF8.GetBytes(m); try { _udp.Send(d, d.Length, _serverIp, _uPort); } catch { } }
    }
}