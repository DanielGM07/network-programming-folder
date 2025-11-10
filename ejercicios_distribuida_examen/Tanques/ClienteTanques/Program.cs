using System;
using System.Collections.Generic;
using System.IO;
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

        static TcpClient tcp;
        static NetworkStream net;
        static StreamReader reader;
        static StreamWriter writer;

        static volatile bool running = true;

        static readonly object _stateLock = new object();
        static StateSnapshot snapshot = null;
        static int myId = -1;
        static string infoLine = "";

        static void Main(string[] args)
        {
            Console.Title = "ClienteTanques";
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
                tcp = new TcpClient();
                tcp.NoDelay = true;
                tcp.Connect(ip, PORT);
                net = tcp.GetStream();
                reader = new StreamReader(net, Encoding.UTF8, false, 1024, leaveOpen: true);
                writer = new StreamWriter(net, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine("No se pudo conectar: " + ex.Message);
                WaitExit();
                return;
            }

            new Thread(ReadLoop) { IsBackground = true }.Start();
            SendLine("JOIN " + name);
            new Thread(RenderLoop) { IsBackground = true }.Start();

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

            try { net?.Dispose(); } catch { }
            try { tcp?.Close(); } catch { }
            Console.CursorVisible = true;
        }

        static void ReadLoop()
        {
            try
            {
                while (running && tcp.Connected)
                {
                    string line = reader.ReadLine(); // bloqueante
                    if (line == null) break;
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    if (line.StartsWith("INFO "))
                    {
                        infoLine = line.Substring(5);
                        int idIdx = infoLine.IndexOf("id ");
                        if (idIdx >= 0)
                        {
                            string tail = infoLine.Substring(idIdx + 3).TrimEnd('.');
                            if (int.TryParse(tail, out var parsed)) myId = parsed;
                        }
                    }
                    else if (line.StartsWith("STATE "))
                    {
                        string json = line.Substring(6);
                        try
                        {
                            var snap = JsonSerializer.Deserialize<StateSnapshot>(json, new JsonSerializerOptions { IncludeFields = true });
                            if (snap != null && snap.W > 0 && snap.H > 0)
                            {
                                snap.Tanks = snap.Tanks ?? new List<Tank>();
                                snap.Shots = snap.Shots ?? new List<Shot>();
                                snap.Obstacles = snap.Obstacles ?? new List<int[]>();
                                lock (_stateLock) snapshot = snap;
                                // No cerramos aunque venga "ended"; dejamos que el usuario vea el estado
                            }
                        }
                        catch
                        {
                            // Ignorar frames corruptos
                        }
                    }
                    else if (line.StartsWith("END "))
                    {
                        // Mostramos fin pero NO cortamos de inmediato
                        infoLine = "Partida terminada.";
                    }
                }
            }
            catch (IOException) { /* servidor cerró */ }
            catch (Exception) { }
            finally
            {
                // No matamos de golpe: dejamos que el render muestre algo
                running = false;
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
                    SafeWriteAt(0, 0, "Conectado. Esperando estado...");

                Thread.Sleep(33); // ~30 FPS
            }

            // Al salir, mostramos lo último que tengamos
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

            var myTank = FindTank(s, myId);
            string hud = myTank != null
                ? "Yo[" + (myTank.Symbol == '\0' ? (char)('A' + (myTank.Id % 26)) : myTank.Symbol) + "] HP:" + myTank.HP + "  Pos(" + myTank.X + "," + myTank.Y + ")"
                : "Unido. Buscando tanque...";
            string phase = s.Phase == "running" ? "En juego" : "Terminado";
            string info = string.IsNullOrEmpty(infoLine) ? "" : " | " + infoLine;

            Console.SetCursorPosition(0, 0);
            for (int y = 0; y < s.H; y++) Console.WriteLine(lines[y].PadRight(s.W));
            Console.WriteLine(new string('-', s.W));
            Console.WriteLine((hud + " | " + phase + info).PadRight(s.W));
            Console.WriteLine("Controles: WASD/↑↓←→ mover, ESPACIO disparar, ESC salir".PadRight(s.W));
        }

        static Tank FindTank(StateSnapshot s, int id)
        {
            if (s == null) return null;
            foreach (var t in s.Tanks) if (t.Id == id) return t;
            return null;
        }

        static bool Inside(int x, int y, int w, int h) => x >= 0 && x < w && y >= 0 && y < h;

        static void SendLine(string line)
        {
            try
            {
                writer?.WriteLine(line);
            }
            catch { running = false; }
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
