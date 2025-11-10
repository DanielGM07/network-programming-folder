using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ServerTanques
{
    public enum Dir { Up, Down, Left, Right }

    public class Tank
    {
        public int Id;
        public string Name;
        public int X;
        public int Y;
        public int HP = 3;
        public bool Alive = true;
        public Dir Facing = Dir.Right;
        public char Symbol => (char)('A' + (Id % 26));
    }

    public class Shot
    {
        public int X;
        public int Y;
        public Dir D;
        public int OwnerId;
        public bool Active = true;
    }

    public class StateSnapshot
    {
        public int Tick;                    // número de tick del servidor (para ordenar en cliente)
        public int W;
        public int H;
        public List<Tank> Tanks;
        public List<Shot> Shots;
        public List<int[]> Obstacles;       // [x,y]
        public string Phase;                // "running" | "ended"
        public int WinnerId;
    }

    public class InputAction
    {
        public int PlayerId;
        public string Type;  // MOVE / FIRE / QUIT
        public Dir MoveDir;
    }

    public class ClientInfo
    {
        public int PlayerId = -1;
        public string Name = "?";
        public IPEndPoint EndPoint;
        public DateTime LastSeen = DateTime.UtcNow;
    }

    public class Program
    {
        // Config del juego / red
        const int WIDTH = 40;
        const int HEIGHT = 20;
        const int TICK_MS = 100;         // 10 Hz
        const int PORT = 7777;
        const int CLIENT_TIMEOUT_SEC = 30;

        // Estado
        static readonly object _lock = new object();
        static readonly Dictionary<int, ClientInfo> clientsById = new Dictionary<int, ClientInfo>();
        static readonly Dictionary<string, ClientInfo> clientsByKey = new Dictionary<string, ClientInfo>(); // ip:port -> info
        static readonly Dictionary<int, Tank> tanks = new Dictionary<int, Tank>();
        static readonly List<Shot> shots = new List<Shot>();
        static readonly HashSet<(int, int)> obstacles = new HashSet<(int, int)>();
        static readonly Queue<InputAction> inbox = new Queue<InputAction>();

        static int nextId = 0;
        static int winnerId = -1;
        static int tickCounter = 0;
        static bool running = true;

        static UdpClient udp;

        static void Main(string[] args)
        {
            Console.Title = "ServerTanques (UDP)";
            Console.WriteLine("== ServerTanques (UDP) ==");
            Console.WriteLine($"Escuchando UDP en 0.0.0.0:{PORT} | Grid {WIDTH}x{HEIGHT}");
            Console.WriteLine("[BOOT] Server listo, esperando clientes...");

            BuildObstacles();

            udp = new UdpClient(PORT);
            // Hilo de recepción
            new Thread(RecvLoop) { IsBackground = true }.Start();

            // Game loop
            var last = Environment.TickCount;
            while (running)
            {
                var now = Environment.TickCount;
                if (now - last >= TICK_MS)
                {
                    Tick();
                    last = now;
                }
                Thread.Sleep(1);
                Console.Title = $"ServerTanques (UDP) | jugadores:{clientsById.Count} tiros:{shots.Count} tick:{tickCounter}";
            }

            try { udp?.Close(); } catch { }
        }

        static void BuildObstacles()
        {
            // Obstáculos simples en cruz
            for (int x = 8; x < 32; x += 2) obstacles.Add((x, HEIGHT / 2));
            for (int y = 4; y < HEIGHT - 4; y += 2) obstacles.Add((WIDTH / 2, y));
        }

        static void RecvLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] dat = udp.Receive(ref remote); // bloqueante
                    string msg = Encoding.UTF8.GetString(dat).Trim();
                    if (msg.Length == 0) continue;

                    string key = remote.ToString(); // "ip:port"
                    lock (_lock)
                    {
                        // Mantener vivo/registrar endpoint
                        if (!clientsByKey.TryGetValue(key, out var info))
                        {
                            info = new ClientInfo { EndPoint = new IPEndPoint(remote.Address, remote.Port) };
                            clientsByKey[key] = info;
                        }
                        info.LastSeen = DateTime.UtcNow;

                        if (msg.StartsWith("JOIN "))
                        {
                            string name = msg.Substring(5).Trim();
                            RegisterOrAttach(info, name);
                            SendInfo(info, $"Bienvenido {info.Name}! Tu id es {info.PlayerId}");
                            Console.WriteLine($"[JOIN] {info.Name} -> id {info.PlayerId} desde {key}. Conectados: {clientsById.Count}");
                            // Primer estado al recién llegado
                            SendStateTo(info, "running");
                        }
                        else if (msg.StartsWith("MOVE "))
                        {
                            if (info.PlayerId >= 0)
                            {
                                string dirTxt = msg.Substring(5).Trim().ToUpperInvariant();
                                inbox.Enqueue(new InputAction { PlayerId = info.PlayerId, Type = "MOVE", MoveDir = ParseDir(dirTxt) });
                            }
                        }
                        else if (msg == "FIRE")
                        {
                            if (info.PlayerId >= 0) inbox.Enqueue(new InputAction { PlayerId = info.PlayerId, Type = "FIRE" });
                        }
                        else if (msg == "QUIT")
                        {
                            if (info.PlayerId >= 0)
                            {
                                int pid = info.PlayerId;
                                Disconnect(pid, "Desconectado.");
                            }
                        }
                        else if (msg == "PING")
                        {
                            // keepalive; no-op
                        }
                        else
                        {
                            // desconocido: ignorar
                        }
                    }
                }
                catch (SocketException) { /* puerto cerrado o error temporal */ }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Console.WriteLine("[ERR/RECV] " + ex.Message); }
            }
        }

        static Dir ParseDir(string s)
        {
            switch (s)
            {
                case "UP": return Dir.Up;
                case "DOWN": return Dir.Down;
                case "LEFT": return Dir.Left;
                default: return Dir.Right;
            }
        }

        static void RegisterOrAttach(ClientInfo info, string name)
        {
            // Si ya tenía playerId asignado, solo actualizamos nombre
            if (info.PlayerId >= 0)
            {
                info.Name = string.IsNullOrWhiteSpace(name) ? info.Name : name;
                if (tanks.TryGetValue(info.PlayerId, out var existing))
                    existing.Name = info.Name;
                return;
            }

            int id = nextId++;
            info.PlayerId = id;
            info.Name = string.IsNullOrWhiteSpace(name) ? ("Jugador" + id) : name;

            var rnd = new Random(id ^ Environment.TickCount);
            int x = (id % 2 == 0) ? 1 : (WIDTH - 2);
            int y = 1 + rnd.Next(HEIGHT - 2);
            var t = new Tank { Id = id, Name = info.Name, X = x, Y = y, Facing = (id % 2 == 0) ? Dir.Right : Dir.Left };

            clientsById[id] = info;
            tanks[id] = t;
        }

        static void Disconnect(int id, string reason)
        {
            // Elimina cliente y marca tanque muerto
            foreach (var kv in clientsByKey)
            {
                if (kv.Value.PlayerId == id)
                {
                    TrySend(kv.Value.EndPoint, "INFO " + reason);
                }
            }

            if (clientsById.TryGetValue(id, out var ci))
            {
                clientsById.Remove(id);
            }
            foreach (var k in new List<string>(clientsByKey.Keys))
            {
                if (clientsByKey[k].PlayerId == id) clientsByKey.Remove(k);
            }

            if (tanks.TryGetValue(id, out var t))
            {
                t.Alive = false;
            }

            BroadcastInfo("Jugador " + id + " salió.");
        }

        static void CleanupTimeouts()
        {
            var now = DateTime.UtcNow;
            var toKick = new List<int>();
            foreach (var kv in clientsById)
            {
                if ((now - kv.Value.LastSeen).TotalSeconds > CLIENT_TIMEOUT_SEC)
                    toKick.Add(kv.Key);
            }
            foreach (var pid in toKick)
            {
                Console.WriteLine("[TIMEOUT] id " + pid);
                Disconnect(pid, "Timeout.");
            }
        }

        static void Tick()
        {
            lock (_lock)
            {
                tickCounter++;

                // Limpia desconectados por timeout
                CleanupTimeouts();

                // 1) Entradas
                while (inbox.Count > 0)
                {
                    var a = inbox.Dequeue();
                    if (!tanks.TryGetValue(a.PlayerId, out var t) || !t.Alive) continue;

                    if (a.Type == "MOVE")
                    {
                        int nx = t.X, ny = t.Y;
                        t.Facing = a.MoveDir;
                        switch (a.MoveDir)
                        {
                            case Dir.Up: ny--; break;
                            case Dir.Down: ny++; break;
                            case Dir.Left: nx--; break;
                            case Dir.Right: nx++; break;
                        }
                        if (IsFree(nx, ny)) { t.X = nx; t.Y = ny; }
                    }
                    else if (a.Type == "FIRE")
                    {
                        int sx = t.X, sy = t.Y;
                        switch (t.Facing)
                        {
                            case Dir.Up: sy--; break;
                            case Dir.Down: sy++; break;
                            case Dir.Left: sx--; break;
                            case Dir.Right: sx++; break;
                        }
                        if (Inside(sx, sy) && !obstacles.Contains((sx, sy)))
                            shots.Add(new Shot { X = sx, Y = sy, D = t.Facing, OwnerId = t.Id });
                    }
                    else if (a.Type == "QUIT")
                    {
                        Disconnect(a.PlayerId, "Desconectado.");
                    }
                }

                // 2) Proyectiles
                for (int i = shots.Count - 1; i >= 0; i--)
                {
                    var s = shots[i];
                    if (!s.Active) { shots.RemoveAt(i); continue; }

                    StepShot(s);
                    if (!Inside(s.X, s.Y) || obstacles.Contains((s.X, s.Y)))
                    {
                        s.Active = false;
                        shots.RemoveAt(i);
                        continue;
                    }

                    foreach (var kv in tanks)
                    {
                        var t = kv.Value;
                        if (!t.Alive) continue;
                        if (t.X == s.X && t.Y == s.Y && t.Id != s.OwnerId)
                        {
                            t.HP--;
                            s.Active = false;
                            shots.RemoveAt(i);
                            if (t.HP <= 0) t.Alive = false;
                            break;
                        }
                    }
                }

                // 3) Fin de partida (servidor sigue vivo)
                int alive = 0; int cand = -1;
                foreach (var kv in tanks) if (kv.Value.Alive) { alive++; cand = kv.Key; }
                if (alive <= 1 && winnerId == -1 && nextId > 0)
                {
                    winnerId = cand; // -1 si nadie
                }

                // 4) Broadcast estado por UDP
                BroadcastState(winnerId == -1 ? "running" : "ended");
                if (winnerId != -1) BroadcastLine("END " + winnerId);
            }
        }

        static bool Inside(int x, int y) => x >= 0 && x < WIDTH && y >= 0 && y < HEIGHT;

        static bool IsFree(int x, int y)
        {
            if (!Inside(x, y)) return false;
            if (obstacles.Contains((x, y))) return false;
            foreach (var kv in tanks)
            {
                var t = kv.Value;
                if (t.Alive && t.X == x && t.Y == y) return false;
            }
            return true;
        }

        static void StepShot(Shot s)
        {
            switch (s.D)
            {
                case Dir.Up: s.Y--; break;
                case Dir.Down: s.Y++; break;
                case Dir.Left: s.X--; break;
                case Dir.Right: s.X++; break;
            }
        }

        static void BroadcastState(string phase)
        {
            var snap = new StateSnapshot
            {
                Tick = tickCounter,
                W = WIDTH,
                H = HEIGHT,
                Tanks = new List<Tank>(tanks.Values),
                Shots = new List<Shot>(shots),
                Obstacles = new List<int[]>(),
                Phase = phase,
                WinnerId = winnerId
            };
            foreach (var o in obstacles) snap.Obstacles.Add(new[] { o.Item1, o.Item2 });

            var opts = new JsonSerializerOptions { IncludeFields = true };
            string json = JsonSerializer.Serialize(snap, opts);
            BroadcastLine("STATE " + json);
        }

        static void SendStateTo(ClientInfo c, string phase)
        {
            var snap = new StateSnapshot
            {
                Tick = tickCounter,
                W = WIDTH,
                H = HEIGHT,
                Tanks = new List<Tank>(tanks.Values),
                Shots = new List<Shot>(shots),
                Obstacles = new List<int[]>(),
                Phase = phase,
                WinnerId = winnerId
            };
            foreach (var o in obstacles) snap.Obstacles.Add(new[] { o.Item1, o.Item2 });
            var opts = new JsonSerializerOptions { IncludeFields = true };
            string json = JsonSerializer.Serialize(snap, opts);
            TrySend(c.EndPoint, "STATE " + json);
        }

        static void BroadcastInfo(string txt) => BroadcastLine("INFO " + txt);

        static void BroadcastLine(string line)
        {
            foreach (var kv in clientsById)
            {
                TrySend(kv.Value.EndPoint, line);
            }
        }

        static void SendInfo(ClientInfo c, string line) => TrySend(c.EndPoint, "INFO " + line);

        static void TrySend(IPEndPoint ep, string line)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(line);
                udp.Send(data, data.Length, ep);
            }
            catch { /* UDP: mejor-effort */ }
        }
    }
}
