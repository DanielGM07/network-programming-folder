using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerTanques
{
    enum TankClass { LIGHT = 0, MEDIUM = 1, HEAVY = 2 }
    enum BulletKind { NORMAL = 0, HEAVY = 1, FAST = 2 }

    class Player
    {
        public int Id;
        public string Name;
        public int X, Y;
        public int HP;
        public int DMG;
        public int SPD;       // celdas por comando MOVE
        public int Face = 1;  // 0=U 1=R 2=D 3=L
        public TankClass Cls;
        public char Sym => (char)('A' + (Id % 26));
    }

    class Bullet
    {
        public int X, Y;
        public int DX, DY;
        public int SPD;    // celdas por tick
        public int DMG;
        public BulletKind Kind;
        public int Owner;
    }

    class Program
    {
        // =================== Config ===================
        const int PORT = 7777;
        const int W = 34, H = 18;
        const int TICK_MS = 120;
        const int COVER_COUNT = 24;

        // =================== Estado ===================
        static UdpClient udp;
        static readonly object L = new object();

        static readonly Dictionary<IPEndPoint, int> ep2id = new Dictionary<IPEndPoint, int>();
        static readonly Dictionary<int, IPEndPoint> id2ep = new Dictionary<int, IPEndPoint>();
        static readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
        static readonly List<Bullet> bullets = new List<Bullet>();
        static readonly HashSet<(int x,int y)> covers = new HashSet<(int,int)>();

        static int nextId = 0;
        static bool running = true;

        // =================== Main ===================
        static void Main(string[] args)
        {
            Console.Title = "ServerTanques UDP+Clases+Cover+Proyectiles";
            udp = new UdpClient(PORT);
            BuildCovers();

            new Thread(RecvLoop) { IsBackground = true }.Start();

            while (running)
            {
                Tick();
                BroadcastState();
                Thread.Sleep(TICK_MS);
            }
            try { udp.Close(); } catch { }
        }

        // =================== Util ===================
        static void BuildCovers()
        {
            var rnd = new Random(12345);
            while (covers.Count < COVER_COUNT)
            {
                int x = rnd.Next(1, W - 1);
                int y = rnd.Next(1, H - 1);
                covers.Add((x, y));
            }
        }

        static bool Inside(int x, int y) => x >= 0 && x < W && y >= 0 && y < H;

        static bool Free(int x, int y)
        {
            foreach (var kv in players)
                if (kv.Value.X == x && kv.Value.Y == y)
                    return false;
            return true;
        }

        static TankClass ParseClass(string s)
        {
            s = (s ?? "").ToUpperInvariant();
            if (s == "LIGHT") return TankClass.LIGHT;
            if (s == "HEAVY") return TankClass.HEAVY;
            return TankClass.MEDIUM;
        }

        static void ApplyClass(Player p, TankClass cls)
        {
            p.Cls = cls;
            switch (cls)
            {
                case TankClass.LIGHT:  p.HP = 2; p.DMG = 1; p.SPD = 2; break;
                case TankClass.HEAVY:  p.HP = 5; p.DMG = 2; p.SPD = 1; break;
                default:               p.HP = 3; p.DMG = 1; p.SPD = 1; break; // MEDIUM
            }
        }

        static (int spd, int dmg, BulletKind kind) ProjectileFor(string s, int baseDmg)
        {
            s = (s ?? "").ToUpperInvariant();
            if (s == "HEAVY") return (1, Math.Max(1, baseDmg + 1), BulletKind.HEAVY);
            if (s == "FAST")  return (2, baseDmg, BulletKind.FAST);
            return (1, baseDmg, BulletKind.NORMAL);
        }

        static void Send(IPEndPoint ep, string txt)
        {
            try { var dat = Encoding.UTF8.GetBytes(txt); udp.Send(dat, dat.Length, ep); } catch { }
        }

        // =================== Red ===================
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
                            // JOIN <name> <class?>
                            string name = "Jugador";
                            string clsTxt = "MEDIUM";
                            var parts = msg.Split(' ');
                            if (parts.Length >= 2) name = parts[1];
                            if (parts.Length >= 3) clsTxt = parts[2];

                            if (!ep2id.ContainsKey(any))
                            {
                                int id = nextId++;
                                ep2id[any] = id; id2ep[id] = any;

                                var rnd = new Random(id * 97 + Environment.TickCount);
                                var p = new Player { Id = id, Name = name, X = rnd.Next(W), Y = rnd.Next(H) };
                                ApplyClass(p, ParseClass(clsTxt));
                                players[id] = p;

                                Send(any, "INFO Bienvenido " + name + " id=" + id + " cls=" + p.Cls);
                            }
                        }
                        else if (msg.StartsWith("MOVE"))
                        {
                            if (!ep2id.ContainsKey(any)) continue;
                            int id = ep2id[any];
                            if (!players.ContainsKey(id)) continue;
                            var p = players[id];

                            char d = msg[msg.Length - 1];
                            int dx = 0, dy = 0;
                            if (d == 'U') { dy = -1; p.Face = 0; }
                            if (d == 'D') { dy = +1; p.Face = 2; }
                            if (d == 'L') { dx = -1; p.Face = 3; }
                            if (d == 'R') { dx = +1; p.Face = 1; }

                            // Avanza SPD pasos si puede
                            for (int step = 0; step < p.SPD; step++)
                            {
                                int nx = p.X + dx, ny = p.Y + dy;
                                if (Inside(nx, ny) && Free(nx, ny)) { p.X = nx; p.Y = ny; } else break;
                            }
                        }
                        else if (msg.StartsWith("FIRE"))
                        {
                            if (!ep2id.ContainsKey(any)) continue;
                            int id = ep2id[any];
                            if (!players.ContainsKey(id)) continue;
                            var p = players[id];

                            string kindTxt = msg.Length > 4 ? msg.Substring(5).Trim() : "NORMAL";
                            var proj = ProjectileFor(kindTxt, p.DMG);

                            int dx = 0, dy = 0;
                            if (p.Face == 0) dy = -1;
                            if (p.Face == 1) dx = +1;
                            if (p.Face == 2) dy = +1;
                            if (p.Face == 3) dx = -1;

                            bullets.Add(new Bullet { X = p.X + dx, Y = p.Y + dy, DX = dx, DY = dy, SPD = proj.spd, DMG = proj.dmg, Kind = proj.kind, Owner = id });
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

        // =================== Juego ===================
        static void Tick()
        {
            lock (L)
            {
                // Avanzar balas (cada bala mueve SPD subpasos por tick)
                for (int i = bullets.Count - 1; i >= 0; i--)
                {
                    var b = bullets[i];
                    bool remove = false;

                    for (int s = 0; s < Math.Max(1, b.SPD); s++)
                    {
                        b.X += b.DX; b.Y += b.DY;
                        if (!Inside(b.X, b.Y)) { remove = true; break; }

                        // Colisión con tanque
                        foreach (var kv in players)
                        {
                            var p = kv.Value;
                            if (p.Id == b.Owner) continue;
                            if (p.X == b.X && p.Y == b.Y)
                            {
                                int incoming = b.DMG;
                                // cover reduce 1 (mínimo 0)
                                if (covers.Contains((p.X, p.Y))) incoming = Math.Max(0, incoming - 1);
                                p.HP -= incoming;
                                remove = true;
                                if (p.HP <= 0) players.Remove(p.Id);
                                break;
                            }
                        }
                        if (remove) break;
                    }

                    if (remove) bullets.RemoveAt(i);
                }
            }
        }

        static void BroadcastState()
        {
            lock (L)
            {
                var sb = new StringBuilder();
                sb.Append("STATE ").Append(W).Append(' ').Append(H).Append('\n');

                foreach (var c in covers)
                    sb.Append("C ").Append(c.x).Append(' ').Append(c.y).Append('\n');

                foreach (var kv in players)
                {
                    var p = kv.Value;
                    // T id x y hp spd dmg cls name
                    sb.Append("T ").Append(p.Id).Append(' ')
                      .Append(p.X).Append(' ').Append(p.Y).Append(' ')
                      .Append(p.HP).Append(' ').Append(p.SPD).Append(' ')
                      .Append(p.DMG).Append(' ').Append((int)p.Cls).Append(' ')
                      .Append(p.Name).Append('\n');
                }

                foreach (var b in bullets)
                {
                    // B x y spd dmg kind
                    sb.Append("B ").Append(b.X).Append(' ').Append(b.Y).Append(' ')
                      .Append(b.SPD).Append(' ').Append(b.DMG).Append(' ')
                      .Append((int)b.Kind).Append('\n');
                }

                string pkt = sb.ToString();
                foreach (var kv in id2ep) Send(kv.Value, pkt);
            }
        }
    }
}
