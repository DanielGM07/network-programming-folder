using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ServerAutos
{
    class Program
    {
        static void Main()
        {
            UdpClient udp = new UdpClient(7777);

            // ✅ Ignorar ICMP "Port Unreachable" (Windows) para evitar SocketException 10054 en Receive/Send
            const int SIO_UDP_CONNRESET = -1744830452;
            try { udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null); }
            catch { /* en Linux/macOS no aplica, ignorar */ }

            Console.WriteLine("Servidor iniciado en puerto 7777...");
            var clients = new Dictionary<IPEndPoint, Player>();
            var random = new Random();
            var obstacles = new List<Obstacle>();
            int nextId = 1;

            // Obstáculos iniciales
            for (int i = 0; i < 20; i++)
                obstacles.Add(new Obstacle { X = i * 15 + random.Next(5), Lane = random.Next(0, 12) });

            // Hilo de simulación + broadcast
            new Thread(() =>
            {
                while (true)
                {
                    // Actualizar jugadores (auto-movimiento y colisión simple)
                    foreach (var player in clients.Values)
                    {
                        bool hit = false;
                        foreach (var o in obstacles)
                        {
                            if (Math.Abs(o.X - player.X) < 1.0 && o.Lane == player.Lane) { hit = true; break; }
                        }
                        player.Speed = hit ? 0.3 : 0.8;
                        player.X += player.Speed;
                    }

                    // Extender obstáculos hacia adelante
                    double maxX = 0;
                    foreach (var pl in clients.Values) if (pl.X > maxX) maxX = pl.X;

                    if (obstacles.Count == 0 || maxX > obstacles[obstacles.Count - 1].X - 100)
                    {
                        double baseX = obstacles.Count == 0 ? 0 : obstacles[obstacles.Count - 1].X;
                        for (int i = 0; i < 5; i++)
                            obstacles.Add(new Obstacle { X = baseX + random.Next(10, 20), Lane = random.Next(0, 12) });
                    }

                    // Snapshot
                    var snapshot = new GameState
                    {
                        Players = new List<Player>(clients.Values),
                        Obstacles = obstacles
                    };

                    string json = JsonSerializer.Serialize(snapshot);
                    byte[] snapData = Encoding.UTF8.GetBytes(json);

                    // Enviar a todos (robusto ante desconexiones)
                    var disconnected = new List<IPEndPoint>();
                    foreach (var ep in clients.Keys)
                    {
                        try
                        {
                            udp.Send(snapData, snapData.Length, ep);
                        }
                        catch (SocketException sx)
                        {
                            // 10054/10004: endpoint ya no escucha / cancelado
                            if (sx.ErrorCode == 10054 || sx.ErrorCode == 10004) disconnected.Add(ep);
                            else disconnected.Add(ep);
                        }
                        catch (Exception)
                        {
                            disconnected.Add(ep);
                        }
                    }

                    // Limpiar desconectados
                    foreach (var ep in disconnected)
                    {
                        if (clients.TryGetValue(ep, out var p))
                            Console.WriteLine($"Cliente desconectado (por envío fallido): {p.Name}");
                        clients.Remove(ep);
                    }

                    Thread.Sleep(100); // ~10 FPS
                }
            })
            { IsBackground = true }.Start();

            // Bucle de recepción (join / input / quit)
            while (true)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;

                try
                {
                    data = udp.Receive(ref remote);
                }
                catch (SocketException sx)
                {
                    // ✅ Filtrar errores típicos al cerrar sockets (no cortar el server)
                    if (sx.ErrorCode == 10054 || sx.ErrorCode == 10004) continue;
                    Console.WriteLine("[WARN] Receive: " + sx.Message);
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WARN] Receive genérico: " + ex.Message);
                    continue;
                }

                string msg = Encoding.UTF8.GetString(data);

                Dictionary<string, string> dict = null;
                try { dict = JsonSerializer.Deserialize<Dictionary<string, string>>(msg); }
                catch { continue; }
                if (dict == null) continue;

                if (!clients.ContainsKey(remote))
                {
                    string name = dict.ContainsKey("name") ? dict["name"] : ("P" + nextId);
                    clients[remote] = new Player { Id = nextId++, Name = name, Lane = 5, X = 0, Speed = 0.8 };
                    Console.WriteLine($"{clients[remote].Name} se unió ({remote})");
                }

                var p = clients[remote];

                // Cliente avisa que sale
                if (dict.ContainsKey("action") && dict["action"] == "quit")
                {
                    Console.WriteLine($"{p.Name} salió del juego");
                    clients.Remove(remote);
                    continue;
                }

                // Input vertical
                if (dict.ContainsKey("action"))
                {
                    if (dict["action"] == "up") p.Lane--;
                    else if (dict["action"] == "down") p.Lane++;
                    p.Lane = Math.Clamp(p.Lane, 0, 11); // 12 filas (0..11)
                }
            }
        }
    }

    class Player
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double X { get; set; }
        public int Lane { get; set; }
        public double Speed { get; set; }
    }

    class Obstacle
    {
        public double X { get; set; }
        public int Lane { get; set; }
    }

    class GameState
    {
        public List<Player> Players { get; set; } = new List<Player>();
        public List<Obstacle> Obstacles { get; set; } = new List<Obstacle>();
    }
}
