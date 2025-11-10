using System;
using System.Collections.Generic;
using System.IO;
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
        public int W;
        public int H;
        public List<Tank> Tanks;
        public List<Shot> Shots;
        public List<int[]> Obstacles; // [x,y]
        public string Phase; // "running" | "ended"
        public int WinnerId;
    }

    public class InputAction
    {
        public int PlayerId;
        public string Type; // MOVE / FIRE / QUIT
        public Dir MoveDir;
    }

    public class ClientConn
    {
        public int PlayerId = -1;
        public string Name = "?";
        public TcpClient Tcp;
        public NetworkStream Net;
        public StreamReader Reader;
        public StreamWriter Writer;
    }

    public class Program
    {
        // Config
        const int WIDTH = 40;
        const int HEIGHT = 20;
        const int TICK_MS = 100;
        const int PORT = 7777;

        static readonly object _lock = new object();
        static readonly Dictionary<int, ClientConn> clients = new Dictionary<int, ClientConn>();
        static readonly Dictionary<int, Tank> tanks = new Dictionary<int, Tank>();
        static readonly List<Shot> shots = new List<Shot>();
        static readonly HashSet<(int, int)> obstacles = new HashSet<(int, int)>();
        static readonly Queue<InputAction> inbox = new Queue<InputAction>();

        static int nextId = 0;
        static int winnerId = -1;
        static bool running = true;

        static void Main(string[] args)
        {
            Console.Title = "ServerTanques";
            Console.WriteLine("== ServerTanques ==");
            Console.WriteLine("Usando TCP en puerto 7777. Grid 40x20.");
            Console.WriteLine("[BOOT] Server listo, esperando clientes...");

            BuildObstacles();

            var listener = new TcpListener(IPAddress.Any, PORT);
            listener.Start();

            // Aceptador
            new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        var tcp = listener.AcceptTcpClient();
                        tcp.NoDelay = true;
                        var net = tcp.GetStream();
                        var conn = new ClientConn
                        {
                            Tcp = tcp,
                            Net = net,
                            Reader = new StreamReader(net, Encoding.UTF8, false, 1024, leaveOpen: true),
                            Writer = new StreamWriter(net, new UTF8Encoding(false)) { AutoFlush = true }
                        };
                        new Thread(() => HandleClient(conn)) { IsBackground = true }.Start();
                    }
                    catch { }
                }
            })
            { IsBackground = true }.Start();

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
                Console.Title = $"ServerTanques | jugadores:{clients.Count} tiros:{shots.Count}";
            }

            try { listener.Stop(); } catch { }
        }

        static void BuildObstacles()
        {
            // Obstáculos simples en cruz
            for (int x = 8; x < 32; x += 2) obstacles.Add((x, HEIGHT / 2));
            for (int y = 4; y < HEIGHT - 4; y += 2) obstacles.Add((WIDTH / 2, y));
        }

        static void HandleClient(ClientConn conn)
        {
            try
            {
                // Bucle de lectura bloqueante por líneas
                while (running && conn.Tcp.Connected)
                {
                    string line = conn.Reader.ReadLine(); // null => desconexión
                    if (line == null) break;
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    if (line.StartsWith("JOIN "))
                    {
                        string name = line.Substring(5).Trim();
                        int pid = RegisterPlayer(conn, name);
                        Send(conn, $"INFO Bienvenido {name}! Tu id es {pid}");
                        BroadcastInfo($"{name} se unió (id {pid}).");
                        // Enviar estado inmediato SOLO al nuevo
                        SendStateTo(conn, "running");
                        // Y broadcast normal (por si hay otros)
                        BroadcastState("running");
                    }
                    else if (line.StartsWith("MOVE "))
                    {
                        string dirTxt = line.Substring(5).Trim().ToUpperInvariant();
                        lock (_lock) inbox.Enqueue(new InputAction
                        {
                            PlayerId = conn.PlayerId,
                            Type = "MOVE",
                            MoveDir = ParseDir(dirTxt)
                        });
                    }
                    else if (line == "FIRE")
                    {
                        lock (_lock) inbox.Enqueue(new InputAction { PlayerId = conn.PlayerId, Type = "FIRE" });
                    }
                    else if (line == "QUIT")
                    {
                        lock (_lock) inbox.Enqueue(new InputAction { PlayerId = conn.PlayerId, Type = "QUIT" });
                        break;
                    }
                }
            }
            catch (IOException) { /* cliente se fue */ }
            catch (Exception ex) { Console.WriteLine("[ERR] " + ex.Message); }
            finally
            {
                if (conn.PlayerId >= 0) Disconnect(conn.PlayerId, "Desconectado.");
                try { conn.Net?.Dispose(); } catch { }
                try { conn.Tcp?.Close(); } catch { }
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

        static int RegisterPlayer(ClientConn conn, string name)
        {
            lock (_lock)
            {
                int id = nextId++;
                conn.PlayerId = id;
                conn.Name = name;

                var rnd = new Random(id ^ Environment.TickCount);
                int x = (id % 2 == 0) ? 1 : (WIDTH - 2);
                int y = 1 + rnd.Next(HEIGHT - 2);
                var t = new Tank { Id = id, Name = name, X = x, Y = y, Facing = (id % 2 == 0) ? Dir.Right : Dir.Left };

                clients[id] = conn;
                tanks[id] = t;

                Console.WriteLine($"[JOIN] {name} -> id {id}. Conectados: {clients.Count}");
                return id;
            }
        }

        static void Disconnect(int id, string reason)
        {
            lock (_lock)
            {
                if (clients.TryGetValue(id, out var c))
                {
                    TrySend(c, "INFO " + reason);
                    clients.Remove(id);
                }
                if (tanks.TryGetValue(id, out var t))
                {
                    t.Alive = false;
                }
            }
            BroadcastInfo("Jugador " + id + " salió.");
            BroadcastState("running");
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

        static void Tick()
        {
            lock (_lock)
            {
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
                        if (clients.TryGetValue(a.PlayerId, out var c)) { try { c.Tcp.Close(); } catch { } }
                        if (tanks.TryGetValue(a.PlayerId, out var tk)) tk.Alive = false;
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

                // 3) Fin de partida (server sigue vivo)
                int alive = 0; int cand = -1;
                foreach (var kv in tanks) if (kv.Value.Alive) { alive++; cand = kv.Key; }
                if (alive <= 1 && winnerId == -1 && nextId > 0)
                {
                    winnerId = cand; // -1 si nadie
                }

                // 4) Broadcast estado
                BroadcastState(winnerId == -1 ? "running" : "ended");
                if (winnerId != -1) BroadcastLine("END " + winnerId);
            }
        }

        static void BroadcastState(string phase)
        {
            var snap = new StateSnapshot
            {
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

        static void SendStateTo(ClientConn c, string phase)
        {
            var snap = new StateSnapshot
            {
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
            TrySend(c, "STATE " + json);
        }


        static void BroadcastInfo(string txt) => BroadcastLine("INFO " + txt);

        static void BroadcastLine(string line)
        {
            foreach (var kv in clients) TrySend(kv.Value, line);
        }

        static void Send(ClientConn c, string line) => TrySend(c, line);

        static void TrySend(ClientConn c, string line)
        {
            try
            {
                if (c.Writer != null && c.Tcp.Connected)
                    c.Writer.WriteLine(line);
            }
            catch { }
        }
    }
}
