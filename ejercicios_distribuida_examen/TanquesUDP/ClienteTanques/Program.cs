using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ClienteTanques
{
    public enum Dir { Up, Down, Left, Right }

    public class Tank
    {
        public int Id;
        public string Name;
        public int X;
        public int Y;
        public int HP;
        public bool Alive;
        public Dir Facing;
        public char Symbol;
    }

    public class Shot
    {
        public int X;
        public int Y;
        public Dir D;
        public int OwnerId;
        public bool Active;
    }

    public class StateSnapshot
    {
        public int Tick;
        public int W;
        public int H;
        public List<Tank> Tanks;
        public List<Shot> Shots;
        public List<int[]> Obstacles;
        public string Phase;
        public int WinnerId;
    }

    public class Program
    {
        const int PORT = 7777;

        static UdpClient udp;
        static IPEndPoint serverEP;

        static volatile bool running = true;
        static volatile int lastTick = -1;

        static readonly object _stateLock = new object();
        static StateSnapshot snapshot = null;
        static string infoLine = "";
        static string myName = "Jugador";

        static void Main(string[] args)
        {
            Console.Title = "ClienteTanques (UDP)";
            Console.CursorVisible = false;
            Console.Clear();

            Console.Write("IP del servidor (enter = 127.0.0.1): ");
            string ip = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

            Console.Write("Nombre: ");
            string name = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(name)) myName = name;

            try
            {
                udp = new UdpClient();
                // Opcional: aumentar buffer
                udp.Client.ReceiveBufferSize = 1 << 20;

                serverEP = new IPEndPoint(IPAddress.Parse(ip), PORT);
                udp.Connect(serverEP);
            }
            catch (Exception ex)
            {
                Console.WriteLine("No se pudo inicializar UDP: " + ex.Message);
                WaitExit();
                return;
            }

            // Hilo de recepción
            new Thread(ReadLoop) { IsBackground = true }.Start();
            // Hilo de render
            new Thread(RenderLoop) { IsBackground = true }.Start();
            // Hilo keepalive (PING cada 1s)
            new Thread(KeepAliveLoop) { IsBackground = true }.Start();

            // JOIN inicial
            SendLine("JOIN " + myName);

            // Loop de entrada de usuario
            while (running)
            {
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(1);
                    continue;
                }
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.W || key == ConsoleKey.UpArrow) SendLine("MOVE UP");
                else if (key == ConsoleKey.S || key == ConsoleKey.DownArrow) SendLine("MOVE DOWN");
                else if (key == ConsoleKey.A || key == ConsoleKey.LeftArrow) SendLine("MOVE LEFT");
                else if (key == ConsoleKey.D || key == ConsoleKey.RightArrow) SendLine("MOVE RIGHT");
                else if (key == ConsoleKey.Spacebar) SendLine("FIRE");
                else if (key == ConsoleKey.Escape) { SendLine("QUIT"); running = false; }
            }

            try { udp?.Close(); } catch { }
            Console.CursorVisible = true;
        }

        static void ReadLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] dat = udp.Receive(ref any); // bloqueante, pero con Connect solo acepta del server
                    string line = Encoding.UTF8.GetString(dat).Trim();
                    if (line.Length == 0) continue;

                    if (line.StartsWith("INFO "))
                    {
                        infoLine = line.Substring(5);
                    }
                    else if (line.StartsWith("STATE "))
                    {
                        string json = line.Substring(6);
                        try
                        {
                            var opts = new JsonSerializerOptions { IncludeFields = true };
                            var snap = JsonSerializer.Deserialize<StateSnapshot>(json, opts);
                            if (snap != null && snap.W > 0 && snap.H > 0)
                            {
                                // Ignorar estados viejos
                                if (snap.Tick <= lastTick) continue;
                                lastTick = snap.Tick;

                                snap.Tanks = snap.Tanks ?? new List<Tank>();
                                snap.Shots = snap.Shots ?? new List<Shot>();
                                snap.Obstacles = snap.Obstacles ?? new List<int[]>();
                                lock (_stateLock) snapshot = snap;
                            }
                        }
                        catch
                        {
                            // ignorar frames corruptos
                        }
                    }
                    else if (line.StartsWith("END "))
                    {
                        infoLine = "Partida terminada.";
                    }
                }
                catch (SocketException) { /* socket cerrado */ break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { }
            }
            running = false;
        }

        static void KeepAliveLoop()
        {
            while (running)
            {
                SendLine("PING");
                Thread.Sleep(1000);
            }
        }

        static void RenderLoop()
        {
            while (running)
            {
                StateSnapshot s;
                lock (_stateLock) s = snapshot;

                if (s != null)
                    DrawSnapshot(s);
                else
                    SafeWriteAt(0, 0, "Conectado (UDP). Esperando estado...");

                Thread.Sleep(33); // ~30 FPS
            }

            // Al salir
            StateSnapshot end;
            lock (_stateLock) end = snapshot;

            Console.SetCursorPosition(0, 0);
            Console.Clear();
            if (end != null && end.WinnerId >= 0)
            {
                string winnerName = "?";
                foreach (var t in end.Tanks) if (t.Id == end.WinnerId) winnerName = t.Name;
                Console.WriteLine("¡Fin! Ganador: " + winnerName + " (id " + end.WinnerId + ")");
            }
            else
            {
                Console.WriteLine(string.IsNullOrEmpty(infoLine) ? "Conexión cerrada." : infoLine);
            }
            WaitExit();
        }

        static void DrawSnapshot(StateSnapshot s)
        {
            if (s == null) return;
            if (s.W <= 0 || s.H <= 0)
            {
                SafeWriteAt(0, 0, "Esperando tamaño de mapa...");
                return;
            }

            var tanks = s.Tanks ?? new List<Tank>();
            var shots = s.Shots ?? new List<Shot>();
            var obstacles = s.Obstacles ?? new List<int[]>();

            var lines = new string[s.H + 3];
            var grid = new char[s.H, s.W];

            for (int y = 0; y < s.H; y++)
                for (int x = 0; x < s.W; x++)
                    grid[y, x] = ' ';

            foreach (var o in obstacles)
            {
                if (o == null || o.Length < 2) continue;
                int ox = o[0], oy = o[1];
                if (Inside(ox, oy, s.W, s.H)) grid[oy, ox] = '#';
            }

            foreach (var shot in shots)
            {
                if (shot == null) continue;
                if (Inside(shot.X, shot.Y, s.W, s.H)) grid[shot.Y, shot.X] = '*';
            }

            foreach (var t in tanks)
            {
                if (t == null || !t.Alive) continue;
                char symbol = t.Symbol == '\0' ? (char)('A' + (t.Id % 26)) : t.Symbol;
                if (Inside(t.X, t.Y, s.W, s.H)) grid[t.Y, t.X] = symbol;
            }

            for (int y = 0; y < s.H; y++)
            {
                var sb = new StringBuilder(s.W);
                for (int x = 0; x < s.W; x++) sb.Append(grid[y, x]);
                lines[y] = sb.ToString();
            }

            string hud = "Tick:" + s.Tick + "  " + (string.IsNullOrEmpty(infoLine) ? "" : infoLine);
            Console.SetCursorPosition(0, 0);
            for (int y = 0; y < s.H; y++) Console.WriteLine(lines[y].PadRight(s.W));
            Console.WriteLine(new string('-', s.W));
            Console.WriteLine(hud.PadRight(s.W));
            Console.WriteLine("Controles: WASD/↑↓←→ mover, ESPACIO disparar, ESC salir".PadRight(s.W));
        }

        static bool Inside(int x, int y, int w, int h) => x >= 0 && x < w && y >= 0 && y < h;

        static void SendLine(string line)
        {
            try
            {
                byte[] dat = Encoding.UTF8.GetBytes(line);
                udp.Send(dat, dat.Length);
            }
            catch
            {
                running = false;
            }
        }

        static void SafeWriteAt(int x, int y, string txt)
        {
            try
            {
                Console.SetCursorPosition(x, y);
                Console.Write(txt);
            }
            catch { }
        }

        static void WaitExit()
        {
            Console.WriteLine("Pulsa ENTER para salir...");
            Console.ReadLine();
        }
    }
}
