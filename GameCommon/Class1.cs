using System;

namespace GameCommon
{
    // Proje ayarları ve sabitler
    public static class GameConfig
    {
        public const int TcpPort = 26000;
        public const int UdpPort = 26001;
        public const int ItemSpawnInterval = 20000; // 20 sn
        public const int MaxItems = 5;
    }

    // Protokol komutları
    public static class Protocol
    {
        public const string Move = "MOV:";
        public const string Lobby = "LOBBY:";
        public const string Attack = "ATK:";
        public const string Ready = "CMD:READY";
        public const string HostStart = "CMD:HOST_START";
        public const string GameStart = "CMD:START";
        public const string ItemSpawn = "ITEM:SPAWN:";
        public const string ItemTaken = "CMD:ITEM_TAKEN";
        public const string ItemDestroy = "ITEM:DESTROY:";
        public const string Time = "TIME:";
        public const string Win = "WIN:";
        public const string ResetLobby = "RESET:LOBBY";
        public const string Stat = "STAT:";
    }
}