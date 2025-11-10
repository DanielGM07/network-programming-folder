using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ClienteTanques
{
    enum TankClass { LIGHT=0, MEDIUM=1, HEAVY=2 }
    enum BulletKind { NORMAL=0, HEAVY=1, FAST=2 }

    class Tnk { public int Id, X, Y, HP, SPD, DMG; public TankClass Cls; public string Name; public char Sym => (char)('A' + (Id % 26)); }
    class Bul { public int X, Y, SPD, DMG; public BulletKind Kind; }

    class Program
    {
        const int PORT = 7777;
        static UdpClient udp;
        static IPEndPoint server;
        static volatile bool running = true;

        static int W = 0, H = 0;
        static readonly object L = new object();
        static readonly Dictionary<int, Tnk> tanks = new Dictionary<int, Tnk>();
        static readonly List<Bul> bullets = new List<Bul>();
        static readonly HashSet<(int x,int y)> covers = new HashSet<(int,int)>();

        static void Main(string[] args)
        {
            Console.Title = "ClienteTanques UDP (clases + cover + proyectiles)";
            Console.CursorVisible = false;
            Console.Clear();

            Console.Write("IP del servidor (enter=127.0.0.1): ");
            string ip = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

            Console.Write("Nombre: ");
            string name = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(name)) name = "Jugador";

            Console.Write("Clase (LIGHT/MEDIUM/HEAVY, enter=MEDIUM): ");
            string cls = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(cls)) cls = "MEDIUM";

            try
            {
                udp = new UdpClient();
                server = new IPEndPoint(IPAddress.Parse(ip), PORT);
                udp.Connect(server);
            }
            catch (Exception ex)
            {
                Console.WriteLine("No se pudo iniciar UDP: " + ex.Message);
                WaitExit(); return;
            }

            Send("JOIN " + name + " " + cls);

            new Thread(ReadLoop) { IsBackground = true }.Start();
            new Thread(RenderLoop) { IsBackground = true }.Start();

            while (running)
            {
                if (!Console.KeyAvailable) { Thread.Sleep(1); continue; }
                var k = Console.ReadKey(true).Key;

                if (k == ConsoleKey.W || k == ConsoleKey.UpArrow) Send("MOVE U");
                else if (k == ConsoleKey.S || k == ConsoleKey.DownArrow) Send("MOVE D");
                else if (k == ConsoleKey.A || k == ConsoleKey.LeftArrow) Send("MOVE L");
                else if (k == ConsoleKey.D || k == ConsoleKey.RightArrow) Send("MOVE R");
                else if (k == ConsoleKey.D1) Send("FIRE NORMAL");
                else if (k == ConsoleKey.D2) Send("FIRE HEAVY");
                else if (k == ConsoleKey.D3) Send("FIRE FAST");
                else if (k == ConsoleKey.Spacebar) Send("FIRE");     // por defecto NORMAL
                else if (k == ConsoleKey.Escape) { Send("QUIT"); running = false; }
            }

            try { udp.Close(); } catch { }
            Console.CursorVisible = true;
        }

        static void ReadLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] dat = udp.Receive(ref any);
                    string msg = Encoding.UTF8.GetString(dat);

                    if (msg.StartsWith("STATE "))
                        ParseState(msg);
                }
                catch { break; }
            }
            running = false;
        }

        static void ParseState(string txt)
        {
            // STATE W H
            // C x y
            // T id x y hp spd dmg cls name...
            // B x y spd dmg kind
            var lines = txt.Split('\n');
            if (lines.Length == 0) return;
            var head = lines[0].Split(' ');
            if (head.Length < 3) return;

            int w, h;
            if (!int.TryParse(head[1], out w)) return;
            if (!int.TryParse(head[2], out h)) return;

            var newTanks = new Dictionary<int, Tnk>();
            var newBul = new List<Bul>();
            var newCovers = new HashSet<(int,int)>();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;

                if (line[0] == 'C')
                {
                    var sp = line.Split(' ');
                    if (sp.Length < 3) continue;
                    newCovers.Add((int.Parse(sp[1]), int.Parse(sp[2])));
                }
                else if (line[0] == 'T')
                {
                    var sp = line.Split(' ');
                    if (sp.Length < 9) continue;
                    int id = int.Parse(sp[1]);
                    int x = int.Parse(sp[2]);
                    int y = int.Parse(sp[3]);
                    int hp = int.Parse(sp[4]);
                    int spd = int.Parse(sp[5]);
                    int dmg = int.Parse(sp[6]);
                    int clsVal = int.Parse(sp[7]);
                    string nm = string.Join(" ", sp, 8, sp.Length - 8);
                    newTanks[id] = new Tnk { Id = id, X = x, Y = y, HP = hp, SPD = spd, DMG = dmg, Cls = (TankClass)clsVal, Name = nm };
                }
                else if (line[0] == 'B')
                {
                    var sp = line.Split(' ');
                    if (sp.Length < 6) continue;
                    newBul.Add(new Bul { X = int.Parse(sp[1]), Y = int.Parse(sp[2]), SPD = int.Parse(sp[3]), DMG = int.Parse(sp[4]), Kind = (BulletKind)int.Parse(sp[5]) });
                }
            }

            lock (L)
            {
                W = w; H = h;
                covers.Clear(); foreach (var c in newCovers) covers.Add(c);
                tanks.Clear(); foreach (var kv in newTanks) tanks[kv.Key] = kv.Value;
                bullets.Clear(); bullets.AddRange(newBul);
            }
        }

        static void RenderLoop()
        {
            while (running)
            {
                int w, h;
                char[,] grid;
                string hud;

                lock (L)
                {
                    w = W; h = H;
                    if (w <= 0 || h <= 0)
                    {
                        SafeWrite(0, 0, "Conectado. Esperando estado...");
                        Thread.Sleep(100);
                        continue;
                    }

                    grid = new char[h, w];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            grid[y, x] = ' ';

                    // Covers
                    foreach (var c in covers)
                        if (c.x >= 0 && c.x < w && c.y >= 0 && c.y < h)
                            grid[c.y, c.x] = 'C';

                    // Bullets
                    foreach (var b in bullets)
                    {
                        if (b.X < 0 || b.X >= w || b.Y < 0 || b.Y >= h) continue;
                        grid[b.Y, b.X] = b.Kind == BulletKind.HEAVY ? 'O' : (b.Kind == BulletKind.FAST ? '.' : '*');
                    }

                    // Tanks
                    foreach (var kv in tanks)
                    {
                        var t = kv.Value;
                        if (t.X < 0 || t.X >= w || t.Y < 0 || t.Y >= h) continue;
                        grid[t.Y, t.X] = t.Sym;
                    }

                    hud = $"Tanks:{tanks.Count}  Bullets:{bullets.Count}  [1]NORMAL [2]HEAVY [3]FAST";
                }

                Console.SetCursorPosition(0, 0);
                for (int y = 0; y < h; y++)
                {
                    var sb = new StringBuilder(w);
                    for (int x = 0; x < w; x++) sb.Append(grid[y, x]);
                    Console.WriteLine(sb.ToString().PadRight(w));
                }
                Console.WriteLine(new string('-', Math.Max(20, w)));
                Console.WriteLine(hud.PadRight(w));
                Console.WriteLine("WASD/Flechas: mover | ESPACIO/1/2/3: disparar | ESC: salir".PadRight(w));

                Thread.Sleep(33);
            }

            Console.SetCursorPosition(0, 0);
            Console.Clear();
            Console.WriteLine("Conexión cerrada.");
            WaitExit();
        }

        static void Send(string s)
        {
            try { var dat = Encoding.UTF8.GetBytes(s); udp.Send(dat, dat.Length); }
            catch { running = false; }
        }

        static void SafeWrite(int x, int y, string t)
        {
            try { Console.SetCursorPosition(x, y); Console.Write(t); } catch { }
        }

        static void WaitExit()
        {
            Console.WriteLine("Pulsa ENTER para salir...");
            Console.ReadLine();
        }
    }
}
