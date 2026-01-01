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

    class Program
    {
        private static TcpClient _tcp;
        private static NetworkStream _stream;
        private static UdpClient _udp;
        private static int _tPort = 26000, _uPort = 26001;
        private static string _myName, _myAvatar, _myProj;
        private static int _myX = 20, _myY = 10, _myHP, _myMaxHP, _myMana, _myMaxMana, _myDmg, _cost, _myTeam;
        private static int _dX = 1, _dY = 0;
        private static bool _isDead = false;
        private static Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();
        private static readonly object _lock = new object();

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

            Login();
            Connect();
        }

        static void Login()
        {
            Console.Clear();
            Console.WriteLine("Adın: "); _myName = Console.ReadLine() ?? "Hero";
            Console.WriteLine("\nSınıf Seç (1-4):");
            for (int i = 0; i < 4; i++) Console.WriteLine($"{i + 1}-{_classes[i].Name}");
            int c = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out int r) ? r - 1 : 0;

            Console.WriteLine("\n\nTakım Seç:\n[1] Işık İttifakı (Mavi)\n[2] Gölge Birliği (Kırmızı)");
            _myTeam = Console.ReadKey(true).KeyChar == '2' ? 2 : 1;

            var s = _classes[c < 0 || c > 3 ? 0 : c];
            _myAvatar = s.Avatar; _myHP = _myMaxHP = s.MHP;
            _myMaxMana = _myMana = s.MMana; _myDmg = s.Dmg;
            _cost = s.Cost; _myProj = s.Proj;
        }

        static void Connect()
        {
            try
            {
                _tcp = new TcpClient("127.0.0.1", _tPort); _stream = _tcp.GetStream();
                _udp = new UdpClient(); _udp.Connect("127.0.0.1", _uPort);
                new Thread(TcpIn).Start(); new Thread(UdpIn).Start();
                new Thread(ManaRegen).Start();
                Console.Clear();

                // KRİTİK: Karşı tarafın bizi ölü görmemesi için peş peşe yayın
                SendStatus();
                Thread.Sleep(100);
                UpdatePos();

                Loop();
            }
            catch { Console.WriteLine("Sunucu Kapalı!"); Console.ReadKey(); }
        }

        static void ManaRegen()
        {
            while (true)
            {
                Thread.Sleep(1000);
                if (!_isDead && _myMana < _myMaxMana) { lock (_lock) _myMana = Math.Min(_myMana + 5, _myMaxMana); DrawUI(); }
            }
        }

        static void TcpIn()
        {
            byte[] buf = new byte[1024];
            while (true)
            {
                try
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    string m = Encoding.UTF8.GetString(buf, 0, n);
                    if (m.StartsWith("STAT:"))
                    {
                        var p = m.Substring(5).Split('|');
                        lock (_lock)
                        {
                            if (!_enemies.ContainsKey(p[0])) _enemies[p[0]] = new Enemy { Name = p[0] };
                            _enemies[p[0]].HP = int.Parse(p[1]);
                            _enemies[p[0]].MaxHP = int.Parse(p[2]);
                        }
                        DrawGame();
                    }
                }
                catch { break; }
            }
        }

        static void UdpIn()
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] d = _udp.Receive(ref ep);
                    string m = Encoding.UTF8.GetString(d);
                    if (m.StartsWith("MOV:"))
                    {
                        var p = m.Substring(4).Split('|');
                        if (p[0] == _myName) continue;
                        lock (_lock)
                        {
                            if (!_enemies.ContainsKey(p[0])) _enemies[p[0]] = new Enemy { Name = p[0] };
                            Put(_enemies[p[0]].X, _enemies[p[0]].Y, "      ");
                            _enemies[p[0]].Avatar = p[1];
                            _enemies[p[0]].X = int.Parse(p[2]);
                            _enemies[p[0]].Y = int.Parse(p[3]);
                            if (p.Length >= 6) { _enemies[p[0]].HP = int.Parse(p[4]); _enemies[p[0]].MaxHP = int.Parse(p[5]); }
                            if (p.Length >= 7) _enemies[p[0]].Team = int.Parse(p[6]);
                        }
                        DrawGame();
                    }
                    else if (m.StartsWith("ATK:"))
                    {
                        var p = m.Substring(4).Split('|');
                        new Thread(() => Projectile(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]), p[5], int.Parse(p[6]))).Start();
                    }
                }
                catch { }
            }
        }

        static void Loop()
        {
            DrawGame();
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true).Key;
                    lock (_lock) Put(_myX, _myY, "  ");
                    bool mv = false;
                    if (k == ConsoleKey.W && _myY > 3) { _myY--; _dX = 0; _dY = -1; mv = true; }
                    else if (k == ConsoleKey.S && _myY < 28) { _myY++; _dX = 0; _dY = 1; mv = true; }
                    else if (k == ConsoleKey.A && _myX > 2) { _myX--; _dX = -1; _dY = 0; mv = true; }
                    else if (k == ConsoleKey.D && _myX < 80) { _myX++; _dX = 1; _dY = 0; mv = true; }
                    else if (k == ConsoleKey.Spacebar && !_isDead && _myMana >= _cost)
                    {
                        lock (_lock) _myMana -= _cost; DrawUI();
                        new Thread(() => Projectile(_myX + _dX, _myY + _dY, _dX, _dY, _myDmg, _myProj, _myTeam)).Start();
                        SendUdp($"ATK:{_myX + _dX}|{_myY + _dY}|{_dX}|{_dY}|{_myDmg}|{_myProj}|{_myTeam}");
                    }
                    if (mv) UpdatePos();
                    DrawGame();
                }
                Thread.Sleep(30);
            }
        }

        static void Projectile(int x, int y, int dx, int dy, int dmg, string ic, int shooterTeam)
        {
            int range = (dx != 0) ? 30 : 15;
            int sleepTime = (dx != 0) ? 35 : 65;

            for (int i = 0; i < range; i++)
            {
                if (x < 1 || x > 95 || y < 3 || y > 29) break;
                if (shooterTeam != _myTeam && x == _myX && y == _myY && !_isDead) { Hit(dmg); break; }

                Put(x, y, ic, ConsoleColor.Yellow);
                Thread.Sleep(sleepTime);

                lock (_lock)
                {
                    if (y == _myY && (x == _myX || x == _myX + 1 || x == _myX - 1))
                    {
                        Put(_myX, _myY, _myAvatar, _isDead ? ConsoleColor.DarkGray : (_myTeam == 1 ? ConsoleColor.Cyan : ConsoleColor.Red));
                    }
                    else
                    {
                        bool restored = false;
                        foreach (var e in _enemies.Values)
                        {
                            if (y == e.Y && (x == e.X || x == e.X + 1))
                            {
                                Put(e.X, e.Y, e.Avatar, e.Team == 1 ? ConsoleColor.Cyan : ConsoleColor.Red);
                                restored = true; break;
                            }
                        }
                        if (!restored) Put(x, y, "  ");
                    }
                }
                x += dx; y += dy;
            }
            DrawGame();
        }

        static void Hit(int d)
        {
            lock (_lock) { _myHP = Math.Max(0, _myHP - d); if (_myHP <= 0) { _isDead = true; _myAvatar = "👻"; } }
            SendStatus(); UpdatePos(); DrawGame();
        }

        static void Put(int x, int y, string s, ConsoleColor c = ConsoleColor.White)
        {
            if (x < 0 || y < 0 || x >= 100 || y >= 30) return;
            lock (_lock) { Console.SetCursorPosition(x, y); Console.ForegroundColor = c; Console.Write(s); }
        }

        static void DrawUI()
        {
            lock (_lock)
            {
                Console.SetCursorPosition(0, 0);
                Console.ForegroundColor = _myTeam == 1 ? ConsoleColor.Cyan : ConsoleColor.Red;
                string tName = _myTeam == 1 ? "LIGHT" : "SHADOW";
                Console.Write($"[{tName}] HP: {_myHP}/{_myMaxHP} | MP: {_myMana}/{_myMaxMana} | ADIN: {_myName}   ".PadRight(95));
                Console.SetCursorPosition(0, 1); Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(new string('-', 95));
            }
        }

        static void DrawGame()
        {
            DrawUI();
            lock (_lock)
            {
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

        static void UpdatePos() { SendUdp($"MOV:{_myName}|{_myAvatar}|{_myX}|{_myY}|{_myHP}|{_myMaxHP}|{_myTeam}"); }
        static void SendStatus() { byte[] d = Encoding.UTF8.GetBytes($"STAT:{_myName}|{_myHP}|{_myMaxHP}"); try { _stream.Write(d, 0, d.Length); } catch { } }
        static void SendUdp(string m) { byte[] d = Encoding.UTF8.GetBytes(m); try { _udp.Send(d, d.Length); } catch { } }
    }
}