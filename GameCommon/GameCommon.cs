using System;

namespace GameCommon
{
    // Genel Ayarlar ve Protokol
    public static class GameCommon
    {
        // Portlar çakışırsa buradan değiştir, firewall izni gerekebilir
        public const int TCP_PORT = 26000;
        public const int UDP_PORT = 26001;

        // Ağ paket başlıkları. String split ile parçalanıyor, formatı bozma.
        // ":" karakteri komut ayracı olarak kullanılıyor.
        public const string MSG_MOVE = "MOV:";
        public const string MSG_ATTACK = "ATK:";
        public const string MSG_STATUS = "STAT:";
        public const string MSG_TIME = "TIME:";
        public const string MSG_LOBBY = "LOBBY:";
        public const string MSG_WIN = "WIN:";
        public const string MSG_ITEM_SPAWN = "ITEM:SPAWN:";
        public const string MSG_ITEM_DESTROY = "ITEM:DESTROY:";
        public const string MSG_RESET = "RESET:LOBBY";

        // Server-Client arası özel komutlar
        public const string CMD_START = "CMD:START";
        public const string CMD_READY = "CMD:READY:";
        public const string CMD_HOST_START = "CMD:HOST_START:";
        public const string CMD_ITEM_TAKEN = "CMD:ITEM_TAKEN:";
    }

    // Oyuncu Data Yapıları
    public class PlayerData
    {
        // Bu sınıf JSON yerine düz string olarak serialize ediliyor, o yüzden property'ler basit tutuldu
        public string Name { get; set; }
        public int Team { get; set; }
        public int HP { get; set; }
        public int MaxHP { get; set; }
        public bool IsReady { get; set; }

        public PlayerData()
        {
            Name = "";
            Team = 1;
            HP = 100;
            MaxHP = 100;
            IsReady = false;
        }

        public PlayerData(string name, int team, int hp, int maxHp)
        {
            Name = name;
            Team = team;
            HP = hp;
            MaxHP = maxHp;
            IsReady = false;
        }
    }

    // NPC / Mob Verileri (Şu an aktif kullanılmıyor olabilir)
    public class Enemy
    {
        public string Name { get; set; }
        public string Avatar { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int HP { get; set; }
        public int MaxHP { get; set; }
        public int Team { get; set; }

        public bool IsDead => HP <= 0;

        public Enemy()
        {
            Name = "";
            Avatar = "👤";
            X = 0;
            Y = 0;
            HP = 100;
            MaxHP = 100;
            Team = 1;
        }
    }

    // Lobi İçin Hafif Veri Yapısı
    // Class yerine Struct kullandım, lobi listesi sık güncellendiği için heap'i yormasın
    public struct LobbyPlayer
    {
        public string Name;
        public int Team;
        public bool IsReady;

        public LobbyPlayer(string name, int team, bool isReady)
        {
            Name = name;
            Team = team;
            IsReady = isReady;
        }
    }

    // Karakter Dengeleri (Balance)
    public struct PlayerClass
    {
        public string Name;
        public string Avatar; // Karakter emojisi
        public string Proj;   // Mermi emojisi
        public int MHP;       // Maksimum Can
        public int MMana;     // Maksimum Mana (Henüz tam implemente edilmedi)
        public int Dmg;       // Vuruş hasarı
        public int Cost;      // Saldırı maliyeti

        public PlayerClass(string name, string avatar, string proj, int mhp, int mmana, int dmg, int cost)
        {
            Name = name;
            Avatar = avatar;
            Proj = proj;
            MHP = mhp;
            MMana = mmana;
            Dmg = dmg;
            Cost = cost;
        }
    }

    // Oyun Konfigürasyonu
    public static class GameConstants
    {
        // Konsol pencere boyutları. Buffer hatası almamak için burayı değiştirince Console.SetWindowSize'a dikkat et
        public const int SCREEN_WIDTH = 100;
        public const int SCREEN_HEIGHT = 30;

        // Oynanabilir alan sınırları (UI payı bırakıldı)
        public const int MIN_X = 2;
        public const int MAX_X = 95;
        public const int MIN_Y = 3;
        public const int MAX_Y = 28;

        // Oyun mekanikleri
        public const int GAME_DURATION = 60; // Maç süresi (sn)
        public const int MANA_REGEN_RATE = 5;
        public const int MANA_REGEN_INTERVAL = 1000;
        public const int ITEM_HEAL_AMOUNT = 20;
        public const int MAX_ITEMS_ON_MAP = 5; // Haritada aynı anda bulunabilecek max eşya
        public const int ITEM_SPAWN_INTERVAL = 20000;

        // Takım ID'leri
        public const int TEAM_LIGHT = 1;
        public const int TEAM_SHADOW = 2;

        // Spawn noktaları (Hardcoded, harita değişirse burayı güncelle)
        public const int LIGHT_START_X = 5;
        public const int LIGHT_START_Y = 15;
        public const int SHADOW_START_X = 94;
        public const int SHADOW_START_Y = 15;

        // Mermi fizikleri
        public const int PROJECTILE_HORIZONTAL_RANGE = 30;
        public const int PROJECTILE_VERTICAL_RANGE = 15;
        public const int PROJECTILE_HORIZONTAL_SPEED = 30; // Düşük değer = Daha hızlı (Thread.Sleep süresi)
        public const int PROJECTILE_VERTICAL_SPEED = 60;

        // Sınıf listesi - Yeni karakter eklerken buraya bir satır ekle
        public static readonly PlayerClass[] CLASSES = {
            new PlayerClass("Necromancer", "🧙", "💀", 80,  100, 30, 20),
            new PlayerClass("Paladin",     "🛡️", "✨", 150, 60,  10, 10),
            new PlayerClass("Rogue",       "🥷", "🗡️", 100, 80,  15, 10),
            new PlayerClass("Vampire",     "🧛", "🩸", 120, 90,  12, 15)
        };

        // Görsel assetler (Emoji)
        public const string EMOJI_ITEM = "🍷";
        public const string EMOJI_DEAD = "💀";
        public const string EMOJI_GHOST = "👻";
        public const string EMOJI_WALL = "▓";
    }

    // Harita ve Fizik
    public static class MapManager
    {
        // Engelleri koordinat listesi olarak döndürür
        // İleride burayı text dosyasından okuyacak şekilde refactor etmek lazım
        public static (int x, int y)[] GetObstacles()
        {
            var obstacles = new System.Collections.Generic.List<(int x, int y)>();

            // Orta duvar (Siper)
            for (int x = 40; x < 60; x++)
                obstacles.Add((x, 15));

            // Sol base koruma
            for (int y = 8; y < 12; y++)
            {
                obstacles.Add((20, y));
                obstacles.Add((21, y));
            }

            // Sağ base koruma
            for (int y = 8; y < 12; y++)
            {
                obstacles.Add((80, y));
                obstacles.Add((81, y));
            }

            // Alt platformlar
            for (int x = 30; x < 35; x++) obstacles.Add((x, 22));
            for (int x = 65; x < 70; x++) obstacles.Add((x, 22));

            return obstacles.ToArray();
        }

        // Çarpışma kontrolü (Collision)
        public static bool IsObstacle(int x, int y, (int x, int y)[] obstacles)
        {
            foreach (var obs in obstacles)
            {
                if (obs.x == x && obs.y == y)
                    return true;
            }
            return false;
        }

        // Harita dışına çıkmayı engelle
        public static bool IsValidPosition(int x, int y)
        {
            return x >= GameConstants.MIN_X &&
                   x <= GameConstants.MAX_X &&
                   y >= GameConstants.MIN_Y &&
                   y <= GameConstants.MAX_Y;
        }

        // Eşya doğacak yerin kontrolü (Duvar içi olmasın)
        public static bool IsValidItemSpawnPosition(int x, int y)
        {
            // Orta bölge çok kalabalık, oraya spawn atma
            if (y > 13 && y < 17 && x > 38 && x < 62)
                return false;

            return IsValidPosition(x, y);
        }
    }

    // Yardımcı Araçlar
    public static class GameUtils
    {
        public static string GetTeamName(int team)
        {
            return team == GameConstants.TEAM_LIGHT ? "LIGHT" : "SHADOW";
        }

        public static string GetTeamNameTurkish(int team)
        {
            return team == GameConstants.TEAM_LIGHT ? "IŞIK" : "GÖLGE";
        }

        public static ConsoleColor GetTeamColor(int team)
        {
            return team == GameConstants.TEAM_LIGHT ? ConsoleColor.Cyan : ConsoleColor.Red;
        }

        public static (int x, int y) GetTeamStartPosition(int team)
        {
            if (team == GameConstants.TEAM_LIGHT)
                return (GameConstants.LIGHT_START_X, GameConstants.LIGHT_START_Y);
            else
                return (GameConstants.SHADOW_START_X, GameConstants.SHADOW_START_Y);
        }

        // Merminin gideceği yön (1: sağa, -1: sola)
        public static int GetStartDirection(int team)
        {
            return team == GameConstants.TEAM_LIGHT ? 1 : -1;
        }

        public static string GenerateRandomName()
        {
            return "Hero" + new Random().Next(100, 999);
        }

        // Koordinatları string olarak paketle
        public static string FormatItemPosition(int x, int y)
        {
            return $"{x}|{y}";
        }

        public static (int x, int y) ParseItemPosition(string pos)
        {
            var parts = pos.Split('|');
            return (int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }

    // Paket Oluşturucu (Builder)
    // String birleştirme (concatenation) kullanılıyor, format: "HEADER:Param1|Param2|..."
    public static class MessageBuilder
    {
        public static string BuildMoveMessage(string name, string avatar, int x, int y, int hp, int maxHp, int team)
        {
            return $"{GameCommon.MSG_MOVE}{name}|{avatar}|{x}|{y}|{hp}|{maxHp}|{team}";
        }

        public static string BuildAttackMessage(int x, int y, int dx, int dy, int dmg, string proj, int team)
        {
            return $"{GameCommon.MSG_ATTACK}{x}|{y}|{dx}|{dy}|{dmg}|{proj}|{team}";
        }

        public static string BuildStatusMessage(string name, int hp, int maxHp)
        {
            return $"{GameCommon.MSG_STATUS}{name}|{hp}|{maxHp}";
        }

        public static string BuildTimeMessage(int seconds)
        {
            return $"{GameCommon.MSG_TIME}{seconds}";
        }

        public static string BuildWinMessage(int winner, long score1, long score2)
        {
            return $"{GameCommon.MSG_WIN}{winner}|{score1}|{score2}";
        }

        public static string BuildItemSpawnMessage(int x, int y)
        {
            return $"{GameCommon.MSG_ITEM_SPAWN}{x}|{y}";
        }

        public static string BuildItemDestroyMessage(int x, int y)
        {
            return $"{GameCommon.MSG_ITEM_DESTROY}{x}|{y}";
        }

        public static string BuildReadyCommand(string name)
        {
            return $"{GameCommon.CMD_READY}{name}";
        }

        public static string BuildHostStartCommand(string name)
        {
            return $"{GameCommon.CMD_HOST_START}{name}";
        }

        public static string BuildItemTakenCommand(int x, int y)
        {
            return $"{GameCommon.CMD_ITEM_TAKEN}{x}|{y}";
        }
    }

    // Paket Çözücü (Parser)
    public static class MessageParser
    {
        // Gelen string paketi parçalarına ayırıp değişkenlere atar
        public static bool TryParseMoveMessage(string msg, out string name, out string avatar,
            out int x, out int y, out int hp, out int maxHp, out int team)
        {
            name = avatar = "";
            x = y = hp = maxHp = team = 0;

            if (!msg.StartsWith(GameCommon.MSG_MOVE))
                return false;

            // Header'ı at, gerisini '|' ile böl
            var parts = msg.Substring(GameCommon.MSG_MOVE.Length).Split('|');
            if (parts.Length < 7)
                return false; // Eksik veri gelmiş

            try
            {
                name = parts[0];
                avatar = parts[1];
                x = int.Parse(parts[2]);
                y = int.Parse(parts[3]);
                hp = int.Parse(parts[4]);
                maxHp = int.Parse(parts[5]);
                team = int.Parse(parts[6]);
                return true;
            }
            catch
            {
                // Parse hatası (int beklerken string gelmesi vs.)
                return false;
            }
        }

        public static bool TryParseAttackMessage(string msg, out int x, out int y, out int dx,
            out int dy, out int dmg, out string proj, out int team)
        {
            x = y = dx = dy = dmg = team = 0;
            proj = "";

            if (!msg.StartsWith(GameCommon.MSG_ATTACK))
                return false;

            var parts = msg.Substring(GameCommon.MSG_ATTACK.Length).Split('|');
            if (parts.Length < 7)
                return false;

            try
            {
                x = int.Parse(parts[0]);
                y = int.Parse(parts[1]);
                dx = int.Parse(parts[2]);
                dy = int.Parse(parts[3]);
                dmg = int.Parse(parts[4]);
                proj = parts[5];
                team = int.Parse(parts[6]);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}