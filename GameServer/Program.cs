using System;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly object _tcpLock = new object(); // TCP client listesi için lock

        // UDP: Hızlı işlemler (Hareket, Mermi)
        private static UdpClient _udpListener;
        private static HashSet<IPEndPoint> _udpClients = new HashSet<IPEndPoint>();
        private static readonly object _udpLock = new object(); // UDP client listesi için lock

        private static int _tcpPort = 26000;
        private static int _udpPort = 26001;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                Console.SetBufferSize(100, 30);
                Console.SetWindowSize(100, 30);
            }
            catch { }

            Console.Title = "Crypt Arena - HYBRID SERVER (TCP/UDP)";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("SERVER BAŞLATILIYOR...");

            // 1. TCP Sunucusunu Başlat
            new Thread(StartTcpServer) { IsBackground = true }.Start();

            // 2. UDP Sunucusunu Başlat
            new Thread(StartUdpServer) { IsBackground = true }.Start();

            // 3. Cleanup Thread - Kopuk bağlantıları temizle
            new Thread(CleanupThread) { IsBackground = true }.Start();

            Console.WriteLine("\nSunucu çalışıyor. Durdurmak için bir tuşa basın...");
            Console.ReadKey();
        }

        // --- TCP KISMI (GÜVENLİ) ---
        private static void StartTcpServer()
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _tcpPort);
                _tcpListener.Start();
                Console.WriteLine($"[TCP] {_tcpPort} portunda dinleniyor (Giriş/HP/Ölüm).");

                while (true)
                {
                    TcpClient client = _tcpListener.AcceptTcpClient();
                    lock (_tcpLock)
                    {
                        _tcpClients.Add(client);
                    }
                    Console.WriteLine($"[TCP] Yeni client bağlandı. Toplam: {_tcpClients.Count}");
                    new Thread(new ParameterizedThreadStart(HandleTcpClient)) { IsBackground = true }.Start(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP Hata] {ex.Message}");
            }
        }

        private static void HandleTcpClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = null;

            try
            {
                stream = client.GetStream();
                byte[] buffer = new byte[4096];
                int byteCount;

                while ((byteCount = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // TCP'den gelen mesajı (Örn: Hasar yedi) diğer TCP clientlara yay
                    byte[] dataToSend = new byte[byteCount];
                    Array.Copy(buffer, dataToSend, byteCount);
                    BroadcastTcp(dataToSend, client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP Client Hata] {ex.Message}");
            }
            finally
            {
                // Bağlantı koptuğunda temizlik yap
                lock (_tcpLock)
                {
                    _tcpClients.Remove(client);
                    Console.WriteLine($"[TCP] Client ayrıldı. Kalan: {_tcpClients.Count}");
                }

                try { stream?.Close(); } catch { }
                try { client?.Close(); } catch { }
            }
        }

        private static void BroadcastTcp(byte[] data, TcpClient sender)
        {
            List<TcpClient> clientsCopy;
            lock (_tcpLock)
            {
                clientsCopy = new List<TcpClient>(_tcpClients);
            }

            foreach (TcpClient c in clientsCopy)
            {
                if (c != sender && c.Connected) // Gönderen hariç ve bağlantılı olanlar
                {
                    try
                    {
                        NetworkStream stream = c.GetStream();
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }
                    catch
                    {
                        // Hata durumunda client'ı listeden çıkar
                        lock (_tcpLock)
                        {
                            _tcpClients.Remove(c);
                        }
                    }
                }
            }
        }

        // --- UDP KISMI (HIZLI/BROADCAST) ---
        private static void StartUdpServer()
        {
            try
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
                        lock (_udpLock)
                        {
                            if (!_udpClients.Contains(remoteEP))
                            {
                                _udpClients.Add(remoteEP);
                                Console.WriteLine($"[UDP] Yeni İstemci: {remoteEP} - Toplam: {_udpClients.Count}");
                            }
                        }

                        // BROADCAST: Herkese Dağıt (gönderen hariç)
                        List<IPEndPoint> clientsCopy;
                        lock (_udpLock)
                        {
                            clientsCopy = new List<IPEndPoint>(_udpClients);
                        }

                        foreach (IPEndPoint clientEP in clientsCopy)
                        {
                            if (!clientEP.Equals(remoteEP))
                            {
                                try
                                {
                                    _udpListener.Send(data, data.Length, clientEP);
                                }
                                catch
                                {
                                    // UDP hatasında client'ı temizleme
                                    lock (_udpLock)
                                    {
                                        _udpClients.Remove(clientEP);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"[UDP Paket Hata] {e.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP Hata] {ex.Message}");
            }
        }

        // --- CLEANUP THREAD ---
        private static void CleanupThread()
        {
            while (true)
            {
                Thread.Sleep(10000); // Her 10 saniyede bir kontrol

                lock (_tcpLock)
                {
                    // Bağlantısı kopmuş TCP clientları temizle
                    var deadClients = _tcpClients.Where(c => !c.Connected).ToList();
                    foreach (var client in deadClients)
                    {
                        _tcpClients.Remove(client);
                        try { client.Close(); } catch { }
                    }

                    if (deadClients.Count > 0)
                    {
                        Console.WriteLine($"[Cleanup] {deadClients.Count} kopuk TCP bağlantısı temizlendi.");
                    }
                }
            }
        }
    }
}