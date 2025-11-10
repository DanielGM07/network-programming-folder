using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerTanques
{
    class Player { public int Id; public string Name; public int X; public int Y; public int HP = 3; public char Sym => (char)('A' + (Id % 26)); public int Face = 1; }
    class Bullet { public int X; public int Y; public int DX; public int DY; public int Owner; }

    class Program
    {
        // Config mínima
        const int PORT = 7777;
        const int W = 30, H = 15;
        const int TICK_MS = 120;

        static UdpClient udp;
        static readonly object L = new object();
        static readonly Dictionary<IPEndPoint, int> ep2id = new Dictionary<IPEndPoint, int>();
        static readonly Dictionary<int, IPEndPoint> id2ep = new Dictionary<int, IPEndPoint>();
        static readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
        static readonly List<Bullet> bullets = new List<Bullet>();
        static int nextId = 0;
        static bool running = true;

        static void Main(string[] args)
        {
            Console.Title = "ServerTanques (UDP simple)";
            udp = new UdpClient(PORT);
            new Thread(RecvLoop) { IsBackground = true }.Start();

            while (running)
            {
                Tick();
                BroadcastState();
                Thread.Sleep(TICK_MS);
            }
            udp.Close();
        }

        static void RecvLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] dat = udp.Receive(ref any);
                    string msg = Encoding.UTF8.GetString(dat).Trim();
                    if (msg.Length == 0) continue;

                    lock (L)
                    {
                        if (msg.StartsWith("JOIN"))
                        {
                            string name = msg.Length > 4 ? msg.Substring(5).Trim() : "Jugador";
                            if (!ep2id.ContainsKey(any))
                            {
                                int id = nextId++;
                                ep2id[any] = id; id2ep[id] = any;
                                var rnd = new Random(id * 97 + Environment.TickCount);
                                players[id] = new Player { Id = id, Name = name, X = rnd.Next(W), Y = rnd.Next(H) };
                                Send(any, "INFO Bienvenido " + name + " id=" + id);
                            }
                        }
                        else if (msg.StartsWith("MOVE"))
                        {
                            if (!ep2id.ContainsKey(any)) continue;
                            int id = ep2id[any];
                            if (!players.ContainsKey(id)) continue;
                            var p = players[id];
                            char d = msg[msg.Length - 1];
                            int nx = p.X, ny = p.Y;
                            if (d == 'U') { ny--; p.Face = 0; }
                            if (d == 'D') { ny++; p.Face = 2; }
                            if (d == 'L') { nx--; p.Face = 3; }
                            if (d == 'R') { nx++; p.Face = 1; }
                            if (Inside(nx, ny) && Free(nx, ny)) { p.X = nx; p.Y = ny; }
                        }
                        else if (msg.StartsWith("FIRE"))
                        {
                            if (!ep2id.ContainsKey(any)) continue;
                            int id = ep2id[any];
                            if (!players.ContainsKey(id)) continue;
                            var p = players[id];
                            int dx = 0, dy = 0;
                            if (p.Face == 0) dy = -1;
                            if (p.Face == 1) dx = +1;
                            if (p.Face == 2) dy = +1;
                            if (p.Face == 3) dx = -1;
                            bullets.Add(new Bullet { X = p.X + dx, Y = p.Y + dy, DX = dx, DY = dy, Owner = id });
                        }
                        else if (msg.StartsWith("QUIT"))
                        {
                            if (!ep2id.ContainsKey(any)) continue;
                            int id = ep2id[any];
                            ep2id.Remove(any); id2ep.Remove(id); players.Remove(id);
                        }
                    }
                }
                catch { break; }
            }
        }

        static bool Inside(int x, int y) => x >= 0 && x < W && y >= 0 && y < H;
        static bool Free(int x, int y)
        {
            foreach (var kv in players) if (kv.Value.X == x && kv.Value.Y == y) return false;
            return true;
        }

        static void Tick()
        {
            lock (L)
            {
                // Avanzar balas y chequear golpes
                for (int i = bullets.Count - 1; i >= 0; i--)
                {
                    var b = bullets[i];
                    b.X += b.DX; b.Y += b.DY;
                    if (!Inside(b.X, b.Y)) { bullets.RemoveAt(i); continue; }
                    foreach (var kv in players)
                    {
                        var p = kv.Value;
                        if (p.X == b.X && p.Y == b.Y && p.Id != b.Owner)
                        {
                            p.HP--;
                            bullets.RemoveAt(i);
                            if (p.HP <= 0) players.Remove(p.Id);
                            break;
                        }
                    }
                }
            }
        }

        static void BroadcastState()
        {
            lock (L)
            {
                var sb = new StringBuilder();
                // Encabezado: ancho, alto
                sb.Append("STATE ").Append(W).Append(' ').Append(H).Append('\n');
                // Jugadores: T id x y hp name
                foreach (var kv in players)
                    sb.Append("T ").Append(kv.Value.Id).Append(' ').Append(kv.Value.X).Append(' ').Append(kv.Value.Y).Append(' ').Append(kv.Value.HP).Append(' ').Append(kv.Value.Name).Append('\n');
                // Balas: B x y
                foreach (var b in bullets)
                    sb.Append("B ").Append(b.X).Append(' ').Append(b.Y).Append('\n');
                string pkt = sb.ToString();
                foreach (var kv in id2ep) Send(kv.Value, pkt);
            }
        }

        static void Send(IPEndPoint ep, string txt)
        {
            try { var dat = Encoding.UTF8.GetBytes(txt); udp.Send(dat, dat.Length, ep); } catch { }
        }
    }
}
