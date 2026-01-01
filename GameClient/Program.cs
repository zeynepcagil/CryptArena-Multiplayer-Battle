using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GameClient
{
    class Enemy
    {
        public string Name, Avatar;
        public int X, Y, HP, MaxHP;
        public bool IsDead => HP <= 0;
    }

    struct PlayerClass
    {
        public string ClassName, Avatar, ProjectileIcon, Description;
        public int MaxHP, MaxMana, Damage, ManaCost;
    }

    class Program
    {
        private static TcpClient _tcpClient;
        private static NetworkStream _tcpStream;
        private static UdpClient _udpClient;
        private static int _tcpPort = 26000, _udpPort = 26001;
        private static string _serverIp = "127.0.0.1";

        private static string _myName = "Hero", _myAvatar = "😐", _myProjectile = "🔥";
        private static int _myX = 15, _myY = 10, _myHP, _myMaxHP, _myMana, _myMaxMana, _myDamage, _manaCost;
        private static int _manaRegenRate = 5;
        private static bool _isDead = false;
        private static int _lastDirX = 1, _lastDirY = 0;
        private static string _aliveAvatar = "😐";

        private static Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();
        private static readonly object _drawLock = new object();
        private static readonly object _enemyLock = new object(); // Yeni: Enemy dictionary için ayrı lock

        private static Dictionary<string, (int X, int Y, int Length)> _lastDrawnPositions = new Dictionary<string, (int, int, int)>();

        private static PlayerClass[] _classes = new PlayerClass[]
        {
            new PlayerClass { ClassName="Necromancer", Avatar="🧙", MaxHP=80,  MaxMana=100, Damage=30, ManaCost=20, ProjectileIcon="💀", Description="Yuksek Hasar / Kirilgan" },
            new PlayerClass { ClassName="Paladin",     Avatar="🛡️", MaxHP=150, MaxMana=60,  Damage=10, ManaCost=10, ProjectileIcon="✨", Description="Tank / Dayanikli" },
            new PlayerClass { ClassName="Rogue",       Avatar="🥷", MaxHP=100, MaxMana=80,  Damage=15, ManaCost=10, ProjectileIcon="🗡️", Description="Hizli / Dengeli" },
            new PlayerClass { ClassName="Vampire",     Avatar="🧛", MaxHP=120, MaxMana=90,  Damage=12, ManaCost=15, ProjectileIcon="🩸", Description="Can Calar" }
        };

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.CursorVisible = false;
            try
            {
                Console.SetBufferSize(100, 30);
                Console.SetWindowSize(100, 30);
            }
            catch { }

            ShowLoginScreen();
            ConnectToServer();
        }

        private static void ShowLoginScreen()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("⚡ CRYPT ARENA: FINAL BATTLE ⚡\n---------------------------------------");
            Console.ResetColor();
            Console.Write("Kahraman Adin: ");
            _myName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(_myName)) _myName = "Hero" + new Random().Next(100, 999);

            for (int i = 0; i < _classes.Length; i++)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{i + 1}] {_classes[i].Avatar} {_classes[i].ClassName} - {_classes[i].Description}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    HP: {_classes[i].MaxHP} | MP: {_classes[i].MaxMana} | DMG: {_classes[i].Damage}\n");
            }

            Console.ResetColor();
            Console.Write("Secimin (1-4): ");
            char choiceChar = Console.ReadKey(true).KeyChar;
            int choice = char.IsDigit(choiceChar) ? (int)char.GetNumericValue(choiceChar) - 1 : 2;
            if (choice < 0 || choice >= 4) choice = 2;

            var s = _classes[choice];
            _myAvatar = s.Avatar;
            _aliveAvatar = s.Avatar;
            _myMaxHP = _myHP = s.MaxHP;
            _myMaxMana = _myMana = s.MaxMana;
            _myDamage = s.Damage;
            _manaCost = s.ManaCost;
            _myProjectile = s.ProjectileIcon;

            Console.Clear();
            Console.WriteLine("Sunucuya baglaniliyor...");
        }

        private static void ConnectToServer()
        {
            try
            {
                _tcpClient = new TcpClient(_serverIp, _tcpPort);
                _tcpStream = _tcpClient.GetStream();
                _udpClient = new UdpClient();
                _udpClient.Connect(_serverIp, _udpPort);

                new Thread(TcpReceiveLoop) { IsBackground = true }.Start();
                new Thread(UdpReceiveLoop) { IsBackground = true }.Start();
                new Thread(ManaRegenLoop) { IsBackground = true }.Start();

                Thread.Sleep(100);
                SendMyStatusTCP();
                Thread.Sleep(50);
                SendUdpData($"MOV:{_myName}|{_myAvatar}|{_myX}|{_myY}");

                Console.Clear();
                DrawUI();
                MoveLoop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sunucu hatasi: {ex.Message}");
                Console.ReadKey();
            }
        }

        private static void ManaRegenLoop()
        {
            while (true)
            {
                Thread.Sleep(1000);
                if (!_isDead && _myMana < _myMaxMana)
                {
                    lock (_drawLock)
                    {
                        _myMana = Math.Min(_myMana + _manaRegenRate, _myMaxMana);
                    }
                    DrawUI();
                }
            }
        }

        private static void TcpReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            while (true)
            {
                try
                {
                    int n = _tcpStream.Read(buffer, 0, buffer.Length);
                    if (n <= 0) break;
                    string fullMsg = Encoding.UTF8.GetString(buffer, 0, n);
                    string[] messages = fullMsg.Split(new[] { "STAT:" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var m in messages)
                    {
                        if (!string.IsNullOrWhiteSpace(m))
                            ProcessStatusData("STAT:" + m);
                    }
                }
                catch { break; }
            }
        }

        private static void UdpReceiveLoop()
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] d = _udpClient.Receive(ref ep);
                    string m = Encoding.UTF8.GetString(d);
                    if (m.StartsWith("MOV:")) ProcessMoveData(m);
                    else if (m.StartsWith("ATK:")) ProcessAttackData(m);
                }
                catch { break; }
            }
        }

        private static void ProcessMoveData(string m)
        {
            try
            {
                string[] p = m.Substring(4).Split('|');
                if (p.Length < 4) return;

                // Kendi adımızı işleme - sadece erken çık
                if (p[0] == _myName) return;

                lock (_enemyLock) // Düzeltme: Ayrı lock kullanımı
                {
                    if (!_enemies.ContainsKey(p[0]))
                        _enemies[p[0]] = new Enemy { Name = p[0], HP = 100, MaxHP = 100 };

                    var enemy = _enemies[p[0]];
                    enemy.Avatar = p[1];
                    enemy.X = int.Parse(p[2]);
                    enemy.Y = int.Parse(p[3]);
                }

                lock (_drawLock)
                {
                    EraseEnemy(p[0]);
                    DrawGame();
                }
            }
            catch { }
        }

        private static void ProcessStatusData(string m)
        {
            try
            {
                string[] p = m.Substring(5).Split('|');
                if (p.Length < 3) return;

                // Kendi durumumuzu işleme - sadece erken çık
                if (p[0] == _myName) return;

                lock (_enemyLock) // Düzeltme: Ayrı lock kullanımı
                {
                    if (!_enemies.ContainsKey(p[0]))
                        _enemies[p[0]] = new Enemy { Name = p[0] };

                    int hp = int.Parse(p[1]);
                    int maxHp = int.Parse(p[2]);

                    var enemy = _enemies[p[0]];
                    enemy.HP = hp;
                    enemy.MaxHP = maxHp;

                    if (hp <= 0 && !enemy.Avatar.Contains("💀") && !enemy.Avatar.Contains("👻"))
                        enemy.Avatar = "👻";
                }

                lock (_drawLock)
                {
                    DrawGame();
                }
            }
            catch { }
        }

        private static void ProcessAttackData(string m)
        {
            try
            {
                string[] p = m.Substring(4).Split('|');
                if (p.Length >= 6)
                {
                    new Thread(() => AnimateProjectile(
                        int.Parse(p[0]),
                        int.Parse(p[1]),
                        int.Parse(p[2]),
                        int.Parse(p[3]),
                        false,
                        int.Parse(p[4]),
                        p[5]
                    ))
                    { IsBackground = true }.Start();
                }
            }
            catch { }
        }

        private static void MoveLoop()
        {
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true).Key;

                    if (_isDead)
                    {
                        int oldX = _myX, oldY = _myY;
                        bool moved = false;

                        if (k == ConsoleKey.W && _myY > 3) { _myY--; moved = true; }
                        else if (k == ConsoleKey.S && _myY < 28) { _myY++; moved = true; }
                        else if (k == ConsoleKey.A && _myX > 2) { _myX--; moved = true; }
                        else if (k == ConsoleKey.D && _myX < 80) { _myX++; moved = true; }

                        if (moved)
                        {
                            lock (_drawLock)
                            {
                                ClearPosition(oldX, oldY, _myName.Length + 3);
                                DrawGame();
                            }
                            SendUdpData($"MOV:{_myName}|{_myAvatar}|{_myX}|{_myY}");
                        }
                    }
                    else
                    {
                        int oldX = _myX, oldY = _myY;
                        bool moved = false;

                        if (k == ConsoleKey.W && _myY > 3) { _myY--; _lastDirX = 0; _lastDirY = -1; moved = true; }
                        else if (k == ConsoleKey.S && _myY < 28) { _myY++; _lastDirX = 0; _lastDirY = 1; moved = true; }
                        else if (k == ConsoleKey.A && _myX > 2) { _myX--; _lastDirX = -1; _lastDirY = 0; moved = true; }
                        else if (k == ConsoleKey.D && _myX < 80) { _myX++; _lastDirX = 1; _lastDirY = 0; moved = true; }
                        else if (k == ConsoleKey.Spacebar) TryAttack();

                        if (moved)
                        {
                            lock (_drawLock)
                            {
                                ClearPosition(oldX, oldY, _myName.Length + 3);
                                DrawGame();
                            }
                            SendUdpData($"MOV:{_myName}|{_myAvatar}|{_myX}|{_myY}");
                        }
                    }
                }
                Thread.Sleep(30);
            }
        }

        private static void TryAttack()
        {
            if (_isDead) return;

            if (_myMana >= _manaCost)
            {
                lock (_drawLock) { _myMana -= _manaCost; }
                DrawUI();
                int sx = _myX + _lastDirX, sy = _myY + _lastDirY;
                new Thread(() => AnimateProjectile(sx, sy, _lastDirX, _lastDirY, true, 0, _myProjectile)) { IsBackground = true }.Start();
                SendUdpData($"ATK:{sx}|{sy}|{_lastDirX}|{_lastDirY}|{_myDamage}|{_myProjectile}");
            }
        }

        private static void AnimateProjectile(int x, int y, int dx, int dy, bool mine, int dmg, string icon)
        {
            for (int i = 0; i < 15; i++)
            {
                if (x < 1 || x > 95 || y < 3 || y > 28) break;
                if (!mine && x == _myX && y == _myY && !_isDead) { TakeDamage(dmg); break; }

                lock (_drawLock)
                {
                    Console.SetCursorPosition(x, y);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(icon);
                }

                Thread.Sleep(50);

                lock (_drawLock)
                {
                    ClearPosition(x, y, 3);
                    DrawGame();
                }

                x += dx;
                y += dy;
            }
        }

        private static void TakeDamage(int d)
        {
            if (_isDead) return;

            lock (_drawLock)
            {
                _myHP = Math.Max(0, _myHP - d);
                if (_myHP <= 0)
                {
                    _isDead = true;
                    _myAvatar = "👻";
                }
            }

            SendMyStatusTCP();
            DrawGame();
        }

        static void DrawGame()
        {
            lock (_drawLock)
            {
                DrawUI();

                // Önce tüm eski pozisyonları temizle
                foreach (var kvp in _lastDrawnPositions.ToArray())
                {
                    if (kvp.Key == _myName)
                    {
                        if (kvp.Value.X != _myX || kvp.Value.Y != _myY)
                            ClearPosition(kvp.Value.X, kvp.Value.Y, kvp.Value.Length);
                    }
                    else
                    {
                        lock (_enemyLock)
                        {
                            if (_enemies.ContainsKey(kvp.Key))
                            {
                                var e = _enemies[kvp.Key];
                                if (kvp.Value.X != e.X || kvp.Value.Y != e.Y)
                                    ClearPosition(kvp.Value.X, kvp.Value.Y, kvp.Value.Length);
                            }
                        }
                    }
                }

                // Kendi karakterini çiz (avatar + isim)
                Console.SetCursorPosition(_myX, _myY);
                Console.ForegroundColor = _isDead ? ConsoleColor.DarkGray : ConsoleColor.Cyan;
                string myDisplay = $"{_myAvatar} {_myName}";
                Console.Write(myDisplay);
                _lastDrawnPositions[_myName] = (_myX, _myY, myDisplay.Length);

                // Düşmanları çiz
                lock (_enemyLock)
                {
                    foreach (var e in _enemies.Values)
                    {
                        if (e.X < 1 || e.Y < 3) continue;
                        Console.SetCursorPosition(e.X, e.Y);

                        string displayText;
                        if (e.IsDead)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            displayText = $"👻 {e.Name}: OLDU";
                            Console.Write(displayText);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            displayText = $"{e.Avatar} {e.Name}: {e.HP}";
                            Console.Write(displayText);
                        }

                        _lastDrawnPositions[e.Name] = (e.X, e.Y, displayText.Length);
                    }
                }
            }
        }

        static void DrawUI()
        {
            Console.SetCursorPosition(0, 0);
            if (_isDead)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"[ OLDUN - IZLEYICI MODU ] ADIN: {_myName} (Hareket: WASD) ".PadRight(95));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"HP: {_myHP}/{_myMaxHP} | MP: {_myMana}/{_myMaxMana} | ADIN: {_myName} ".PadRight(95));
            }
            Console.SetCursorPosition(0, 1);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(new string('-', 95));
        }

        static void EraseEnemy(string name)
        {
            if (_lastDrawnPositions.ContainsKey(name))
            {
                var pos = _lastDrawnPositions[name];
                ClearPosition(pos.X, pos.Y, pos.Length);
            }
        }

        static void ClearPosition(int x, int y, int length)
        {
            if (x >= 0 && x < 100 && y >= 0 && y < 30)
            {
                Console.SetCursorPosition(x, y);
                Console.Write(new string(' ', Math.Min(length + 5, 95 - x)));
            }
        }

        private static void SendUdpData(string m)
        {
            byte[] d = Encoding.UTF8.GetBytes(m);
            try { _udpClient.Send(d, d.Length); } catch { }
        }

        private static void SendMyStatusTCP()
        {
            string m = $"STAT:{_myName}|{_myHP}|{_myMaxHP}";
            byte[] d = Encoding.UTF8.GetBytes(m);
            try { _tcpStream.Write(d, 0, d.Length); _tcpStream.Flush(); } catch { }
        }
    }
}