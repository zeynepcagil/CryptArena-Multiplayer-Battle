using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GameServer
{
    class Program
    {
        // TCP: Güvenli işlemler (Giriş, Can, Ölüm)
        private static TcpListener _tcpListener;
        private static List<TcpClient> _tcpClients = new List<TcpClient>();

        // UDP: Hızlı işlemler (Hareket, Mermi)
        private static UdpClient _udpListener;
        private static HashSet<IPEndPoint> _udpClients = new HashSet<IPEndPoint>(); // UDP Adresleri

        private static int _tcpPort = 26000;
        private static int _udpPort = 26001;

        static void Main(string[] args)
        {
            Console.SetWindowSize(100, 30);
            Console.SetBufferSize(100, 30); // Buffer boyutu window boyutuna eşit veya büyük olmalı

            Console.OutputEncoding = Encoding.UTF8;
            
            Console.Title = "Crypt Arena - HYBRID SERVER (TCP/UDP)";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("SERVER BAŞLATILIYOR...");

            // 1. TCP Sunucusunu Başlat
            new Thread(StartTcpServer).Start();

            // 2. UDP Sunucusunu Başlat
            new Thread(StartUdpServer).Start();
        }

        // --- TCP KISMI (GÜVENLİ) ---
        private static void StartTcpServer()
        {
            _tcpListener = new TcpListener(IPAddress.Any, _tcpPort);
            _tcpListener.Start();
            Console.WriteLine($"[TCP] {_tcpPort} portunda dinleniyor (Giriş/HP/Ölüm).");

            while (true)
            {
                TcpClient client = _tcpListener.AcceptTcpClient();
                _tcpClients.Add(client);
                new Thread(new ParameterizedThreadStart(HandleTcpClient)).Start(client);
            }
        }

        private static void HandleTcpClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];
            int byteCount;

            try
            {
                while ((byteCount = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // TCP'den gelen mesajı (Örn: Hasar yedi) diğer TCP clientlara yay
                    byte[] dataToSend = new byte[byteCount];
                    Array.Copy(buffer, dataToSend, byteCount);
                    BroadcastTcp(dataToSend, client);
                }
            }
            catch
            {
                _tcpClients.Remove(client);
            }
        }

        private static void BroadcastTcp(byte[] data, TcpClient sender)
        {
            foreach (TcpClient c in _tcpClients)
            {
                if (c != sender) // Gönderen hariç herkese
                {
                    try
                    {
                        NetworkStream stream = c.GetStream();
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }
                    catch { }
                }
            }
        }

        // --- UDP KISMI (HIZLI/BROADCAST) ---
        private static void StartUdpServer()
        {
            _udpListener = new UdpClient(_udpPort);
            Console.WriteLine($"[UDP] {_udpPort} portunda dinleniyor (Hareket/Savaş).");

            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    // UDP paketi al
                    byte[] data = _udpListener.Receive(ref remoteEP);

                    // Göndereni listeye ekle (Daha önce yoksa)
                    if (!_udpClients.Contains(remoteEP))
                    {
                        _udpClients.Add(remoteEP);
                        Console.WriteLine($"[UDP] Yeni İstemci Eklendi: {remoteEP}");
                    }

                    // BROADCAST: Herkese Dağıt
                    foreach (IPEndPoint clientEP in _udpClients)
                    {
                        // Gönderen hariç herkese yolla
                        if (!clientEP.Equals(remoteEP))
                        {
                            _udpListener.Send(data, data.Length, clientEP);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("[UDP Hata] " + e.Message);
                }
            }
        }
    }
}