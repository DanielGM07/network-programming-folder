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
            // Evitar 10054 en Windows (ICMP Port Unreachable)
            const int SIO_UDP_CONNRESET = -1744830452;
            try { udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null); } catch { }

            Console.WriteLine("Servidor iniciado en puerto 7777...");
            var clients = new Dictionary<IPEndPoint, Player>();
            var random = new Random();
            var obstacles = new List<Obstacle>();
            int nextId = 1;
            int tick = 0;

            // Tipos de auto (vida, resistencia[0..1], velocidad)
            var carTypes = new Dictionary<string, CarType>(StringComparer.OrdinalIgnoreCase)
            {
                {"Ligero",     new CarType{ Name="Ligero",     MaxLife=3, Resistance=0.2, Speed=1.0 }},
                {"Equilibrado",new CarType{ Name="Equilibrado",MaxLife=4, Resistance=0.5, Speed=0.8 }},
                {"Blindado",   new CarType{ Name="Blindado",   MaxLife=5, Resistance=0.8, Speed=0.6 }},
            };

            // Obstáculos iniciales
            for (int i = 0; i < 20; i++)
                obstacles.Add(new Obstacle { X = i * 15 + random.Next(5), Lane = random.Next(0, 12) });

            // Hilo de simulación / broadcast
            new Thread(() =>
            {
                while (true)
                {
                    tick++;

                    // avanzar y chequear colisiones
                    foreach (var player in clients.Values)
                    {
                        player.X += player.Speed; // movimiento automático

                        // colisión simple (mismo carril y cerca en X)
                        bool hit = false;
                        double hitX = 0;
                        foreach (var o in obstacles)
                        {
                            if (o.Lane == player.Lane && Math.Abs(o.X - player.X) < 1.0)
                            {
                                // cooldown para no pegar múltiples veces seguidas en el mismo obstáculo
                                if (Math.Abs(o.X - player.LastHitX) < 1.0 && (tick - player.LastHitTick) < 6)
                                    continue;

                                hit = true; hitX = o.X;
                                break;
                            }
                        }

                        if (hit)
                        {
                            player.Life -= 1;
                            player.LastHitX = hitX;
                            player.LastHitTick = tick;

                            // retroceso según resistencia (si resistencia alta, atraviesa sin retroceder)
                            if (player.Resistance < 0.7)
                            {
                                double knock = (1.0 - player.Resistance) * 5.0;
                                player.X = Math.Max(0, player.X - knock);
                            }
                        }
                    }

                    // Eliminar los sin vida (anunciar)
                    var toRemove = new List<IPEndPoint>();
                    foreach (var kv in clients)
                    {
                        if (kv.Value.Life <= 0)
                        {
                            Console.WriteLine($"Eliminado por vida = 0: {kv.Value.Name}");
                            toRemove.Add(kv.Key);
                        }
                    }
                    foreach (var ep in toRemove) clients.Remove(ep);

                    // Extender obstáculos
                    double maxX = 0;
                    foreach (var pl in clients.Values) if (pl.X > maxX) maxX = pl.X;
                    if (obstacles.Count == 0 || maxX > obstacles[obstacles.Count - 1].X - 100)
                    {
                        double baseX = obstacles.Count == 0 ? 0 : obstacles[obstacles.Count - 1].X;
                        for (int i = 0; i < 6; i++)
                            obstacles.Add(new Obstacle { X = baseX + random.Next(10, 20), Lane = random.Next(0, 12) });
                    }

                    // snapshot
                    var snap = new GameState
                    {
                        Players = new List<PlayerView>(),
                        Obstacles = obstacles,
                        Tick = tick
                    };
                    foreach (var p in clients.Values)
                        snap.Players.Add(new PlayerView { Name = p.Name, X = p.X, Lane = p.Lane, Life = p.Life, Type = p.CarName });

                    string json = JsonSerializer.Serialize(snap);
                    byte[] data = Encoding.UTF8.GetBytes(json);

                    // enviar (remover desconectados si falla)
                    var dead = new List<IPEndPoint>();
                    foreach (var ep in clients.Keys)
                    {
                        try { udp.Send(data, data.Length, ep); }
                        catch { dead.Add(ep); }
                    }
                    foreach (var ep in dead)
                    {
                        if (clients.TryGetValue(ep, out var p)) Console.WriteLine($"Cliente desconectado (envío fallido): {p.Name}");
                        clients.Remove(ep);
                    }

                    Thread.Sleep(100); // ~10 FPS
                }
            })
            { IsBackground = true }.Start();

            // Recepción: join / input / quit
            while (true)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes;
                try { bytes = udp.Receive(ref remote); }
                catch (SocketException sx) { if (sx.ErrorCode == 10054 || sx.ErrorCode == 10004) continue; Console.WriteLine("[WARN] " + sx.Message); continue; }
                catch (Exception) { continue; }

                Dictionary<string, string> dict = null;
                try { dict = JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(bytes)); } catch { }
                if (dict == null) continue;

                if (!clients.ContainsKey(remote))
                {
                    string name = dict.ContainsKey("name") ? dict["name"] : ("P" + nextId);
                    string type = dict.ContainsKey("type") ? dict["type"] : "Equilibrado";
                    if (!carTypes.TryGetValue(type, out var ct)) ct = carTypes["Equilibrado"];

                    clients[remote] = new Player
                    {
                        Id = nextId++,
                        Name = name,
                        CarName = ct.Name,
                        Life = ct.MaxLife,
                        Resistance = ct.Resistance,
                        Speed = ct.Speed,
                        Lane = 5,
                        X = 0
                    };
                    Console.WriteLine($"{clients[remote].Name} ({ct.Name}) se unió ({remote})");
                }

                var pRef = clients[remote];

                // salida voluntaria
                if (dict.TryGetValue("action", out var act) && act == "quit")
                {
                    Console.WriteLine($"{pRef.Name} salió del juego");
                    clients.Remove(remote);
                    continue;
                }

                // input vertical
                if (dict.TryGetValue("action", out act))
                {
                    if (act == "up") pRef.Lane--;
                    else if (act == "down") pRef.Lane++;
                    pRef.Lane = Math.Clamp(pRef.Lane, 0, 11);
                }
            }
        }
    }

    class CarType
    {
        public string Name;
        public int MaxLife;
        public double Resistance; // 0..1
        public double Speed;
    }

    class Player
    {
        public int Id;
        public string Name = "";
        public string CarName = "";
        public int Life;
        public double Resistance;
        public double Speed;
        public double X;
        public int Lane;
        // anti-multi-hit
        public double LastHitX = -9999;
        public int LastHitTick = -9999;
    }

    class PlayerView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Life { get; set; }
        public double X { get; set; }
        public int Lane { get; set; }
    }

    class Obstacle { public double X { get; set; } public int Lane { get; set; } }

    class GameState
    {
        public int Tick { get; set; }
        public List<PlayerView> Players { get; set; } = new List<PlayerView>();
        public List<Obstacle> Obstacles { get; set; } = new List<Obstacle>();
    }
}
