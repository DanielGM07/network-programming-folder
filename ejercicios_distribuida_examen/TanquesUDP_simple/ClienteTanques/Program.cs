using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ClienteTanques
{
    class Tnk { public int Id; public int X; public int Y; public int HP; public string Name; public char Sym => (char)('A' + (Id % 26)); }
    class Bul { public int X; public int Y; }

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

        static void Main(string[] args)
        {
            Console.Title = "ClienteTanques (UDP simple)";
            Console.CursorVisible = false;
            Console.Clear();

            Console.Write("IP del servidor (enter = 127.0.0.1): ");
            string ip = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";
            Console.Write("Nombre: ");
            string name = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(name)) name = "Jugador";

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

            Send("JOIN " + name);
            new Thread(ReadLoop) { IsBackground = true }.Start();
            new Thread(RenderLoop) { IsBackground = true }.Start();

            // Input muy simple
            while (running)
            {
                if (!Console.KeyAvailable) { Thread.Sleep(1); continue; }
                var k = Console.ReadKey(true).Key;
                if (k == ConsoleKey.W || k == ConsoleKey.UpArrow) Send("MOVE U");
                else if (k == ConsoleKey.S || k == ConsoleKey.DownArrow) Send("MOVE D");
                else if (k == ConsoleKey.A || k == ConsoleKey.LeftArrow) Send("MOVE L");
                else if (k == ConsoleKey.D || k == ConsoleKey.RightArrow) Send("MOVE R");
                else if (k == ConsoleKey.Spacebar) Send("FIRE");
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
                    {
                        ParseState(msg);
                    }
                    // INFO se ignora para simplificar
                }
                catch { break; }
            }
            running = false;
        }

        static void ParseState(string txt)
        {
            // Formato:
            // STATE W H
            // T id x y hp name
            // B x y
            var lines = txt.Split('\n');
            if (lines.Length == 0) return;
            var head = lines[0].Split(' ');
            if (head.Length < 3) return;

            int w, h;
            if (!int.TryParse(head[1], out w)) return;
            if (!int.TryParse(head[2], out h)) return;

            var newTanks = new Dictionary<int, Tnk>();
            var newBul = new List<Bul>();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line[0] == 'T')
                {
                    var sp = line.Split(' ');
                    if (sp.Length < 6) continue;
                    int id = int.Parse(sp[1]);
                    int x = int.Parse(sp[2]);
                    int y = int.Parse(sp[3]);
                    int hp = int.Parse(sp[4]);
                    string nm = string.Join(" ", sp, 5, sp.Length - 5);
                    newTanks[id] = new Tnk { Id = id, X = x, Y = y, HP = hp, Name = nm };
                }
                else if (line[0] == 'B')
                {
                    var sp = line.Split(' ');
                    if (sp.Length < 3) continue;
                    newBul.Add(new Bul { X = int.Parse(sp[1]), Y = int.Parse(sp[2]) });
                }
            }

            lock (L)
            {
                W = w; H = h;
                tanks.Clear();
                foreach (var kv in newTanks) tanks[kv.Key] = kv.Value;
                bullets.Clear();
                bullets.AddRange(newBul);
            }
        }

        static void RenderLoop()
        {
            while (running)
            {
                int w, h;
                char[,] grid;
                lock (L)
                {
                    w = W; h = H;
                    if (w <= 0 || h <= 0) { SafeWrite(0, 0, "Conectado. Esperando estado..."); Thread.Sleep(100); continue; }
                    grid = new char[h, w];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            grid[y, x] = ' ';
                    foreach (var b in bullets)
                        if (b.X >= 0 && b.X < w && b.Y >= 0 && b.Y < h) grid[b.Y, b.X] = '*';
                    foreach (var kv in tanks)
                        if (kv.Value.X >= 0 && kv.Value.X < w && kv.Value.Y >= 0 && kv.Value.Y < h) grid[kv.Value.Y, kv.Value.X] = kv.Value.Sym;
                }

                Console.SetCursorPosition(0, 0);
                for (int y = 0; y < h; y++)
                {
                    var sb = new StringBuilder(w);
                    for (int x = 0; x < w; x++) sb.Append(grid[y, x]);
                    Console.WriteLine(sb.ToString().PadRight(w));
                }
                Console.WriteLine(new string('-', Math.Max(10, w)));
                Console.WriteLine("WASD/FLECHAS: mover | ESPACIO: disparar | ESC: salir");
                Thread.Sleep(33);
            }

            Console.SetCursorPosition(0, 0);
            Console.Clear();
            Console.WriteLine("Conexión cerrada.");
            WaitExit();
        }

        static void Send(string s)
        {
            try { var dat = Encoding.UTF8.GetBytes(s); udp.Send(dat, dat.Length); } catch { running = false; }
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
