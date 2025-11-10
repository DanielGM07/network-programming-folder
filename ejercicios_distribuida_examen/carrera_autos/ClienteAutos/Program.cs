// Program.cs — ClienteAutos (C# 8 compatible, sin top-level statements)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClienteAutos
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== UDP Racing Client ===");

            string serverIp;
            string playerName;

            if (args.Length >= 2)
            {
                serverIp = args[0];
                playerName = args[1];
            }
            else
            {
                Console.WriteLine("Uso: ClienteAutos <server-ip> <nombreJugador>");
                Console.Write("IP del servidor [127.0.0.1]: ");
                serverIp = (Console.ReadLine() ?? "").Trim();
                if (string.IsNullOrEmpty(serverIp)) serverIp = "127.0.0.1";

                Console.Write("Nombre de jugador [auto]: ");
                playerName = (Console.ReadLine() ?? "").Trim();
                if (string.IsNullOrEmpty(playerName))
                {
                    var rnd = new Random().Next(100, 999);
                    var user = Environment.UserName;
                    playerName = (string.IsNullOrWhiteSpace(user) ? "Jugador" : user) + rnd.ToString();
                }
            }

            var client = new RacingClient(serverIp, 7777, playerName);
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; client.Stop(); };

            try
            {
                await client.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error en cliente: " + ex.Message);
            }
            return 0;
        }
    }

    public class RacingClient
    {
        private readonly string _serverIp;
        private readonly int _serverPort;
        private readonly string _playerName;

        private readonly UdpClient _udp;
        private IPEndPoint _serverEp;

        private int _playerId = -1;
        private GameConfig _cfg = new GameConfig();
        private WorldState _world = new WorldState();

        private volatile bool _running = false;
        private readonly object _lock = new object();

        public RacingClient(string serverIp, int serverPort, string playerName)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;
            _playerName = playerName;

            IPAddress serverAddress = IPAddress.Parse(serverIp);
            _serverEp = new IPEndPoint(serverAddress, serverPort);

            _udp = new UdpClient(); // puerto local efímero
            _udp.Client.ReceiveTimeout = 3000; // por si el server no responde en el join

            // IGNORAR UDP CONNRESET en Windows
            const int SIO_UDP_CONNRESET = -1744830452;
            try
            {
                _udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch { /* en Linux/macOS no aplica; ignorar */ }
        }

        public async Task RunAsync()
        {
            _running = true;

            // JOIN inicial
            var join = new JoinMsg { type = "join", name = _playerName };
            Send(join);
            Console.WriteLine("Conectando... (JOIN)");

            await WaitWelcomeAsync();
            Console.WriteLine("Conectado! PlayerId=" + _playerId + " | Lanes=" + _cfg.Lanes);

            // Lanzar loops
            Task recvTask = Task.Run(new Func<Task>(ReceiveLoop));
            Task inputTask = Task.Run(new Func<Task>(InputLoop));
            Task renderTask = Task.Run(new Func<Task>(RenderLoop));

            await Task.WhenAny(recvTask, inputTask, renderTask);

            Stop();
        }

        public void Stop()
        {
            if (_running)
            {
                _running = false;
                try
                {
                    if (_playerId >= 0)
                        Send(new LeaveMsg { type = "leave", playerId = _playerId });
                }
                catch { /* ignore */ }
                try { _udp.Close(); } catch { }
            }
        }

        private void Send(object msg)
        {
            string json = JsonSerializer.Serialize(msg);
            byte[] data = Encoding.UTF8.GetBytes(json);
            _udp.Send(data, data.Length, _serverEp);
        }

        private async Task WaitWelcomeAsync()
        {
            DateTime start = DateTime.UtcNow;

            while (_playerId < 0 && (DateTime.UtcNow - start).TotalSeconds < 5.0 && _running)
            {
                try
                {
                    UdpReceiveResult res = await _udp.ReceiveAsync();
                    string json = Encoding.UTF8.GetString(res.Buffer);
                    BaseMsg baseMsg = null;
                    try { baseMsg = JsonSerializer.Deserialize<BaseMsg>(json); } catch { }

                    if (baseMsg != null && baseMsg.type == "welcome")
                    {
                        WelcomeMsg w = JsonSerializer.Deserialize<WelcomeMsg>(json);
                        if (w != null)
                        {
                            _playerId = w.playerId;
                            _cfg = w.cfg ?? new GameConfig();
                            return;
                        }
                    }
                }
                catch
                {
                    // reintentar
                }
                await Task.Delay(50);
            }

            if (_playerId < 0)
                throw new Exception("No se recibió WELCOME del servidor.");
        }

        private async Task ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    var res = await _udp.ReceiveAsync();
                    string json = Encoding.UTF8.GetString(res.Buffer);
                    BaseMsg baseMsg = null;
                    try { baseMsg = JsonSerializer.Deserialize<BaseMsg>(json); } catch { }

                    if (baseMsg == null) continue;

                    if (baseMsg.type == "snapshot")
                    {
                        SnapshotMsg s = null;
                        try { s = JsonSerializer.Deserialize<SnapshotMsg>(json); } catch { }
                        if (s == null) continue;

                        lock (_lock)
                        {
                            _world.Tick = s.tick;
                            _world.Players = (s.players != null) ? s.players : new List<PlayerDto>();
                            _world.Obstacles = (s.obstacles != null) ? s.obstacles : new List<ObstacleDto>();
                        }
                    }
                }
                catch (SocketException sx)
                {
                    // 10054 = WSAECONNRESET (UDP "connection reset" por ICMP)
                    // 10004 = WSAEINTR (interrumpido por cierre)
                    if (sx.ErrorCode == 10054 || sx.ErrorCode == 10004)
                    {
                        if (!_running) break;
                        await Task.Delay(10);
                        continue;
                    }
                    // Otros errores: opcional log
                    // Console.WriteLine("[DBG] UDP recv: " + sx.ErrorCode + " " + sx.Message);
                }
                catch (ObjectDisposedException)
                {
                    break; // socket cerrado
                }
                catch
                {
                    // swallow y seguir
                }
            }
        }

        private async Task InputLoop()
        {
            Console.WriteLine("Controles: ↑/↓ para cambiar de carril, ESC para salir");

            while (_running)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKey key = Console.ReadKey(true).Key;

                    if (key == ConsoleKey.Escape)
                    {
                        try
                        {
                            if (_playerId >= 0)
                                Send(new LeaveMsg { type = "leave", playerId = _playerId });
                        }
                        catch { }
                        _running = false;
                        break;
                    }

                    string action = null;
                    if (key == ConsoleKey.UpArrow) action = "up";
                    else if (key == ConsoleKey.DownArrow) action = "down";

                    if (action != null && _playerId >= 0)
                    {
                        var input = new InputMsg { type = "input", playerId = _playerId, action = action };
                        Send(input);
                    }
                }
                await Task.Delay(10);
            }
        }

        private async Task RenderLoop()
        {
            int targetFps = 20;
            int frameMs = 1000 / targetFps;

            try { Console.CursorVisible = false; } catch { }

            while (_running)
            {
                long t0 = Environment.TickCount64;
                WorldState snapshot;

                lock (_lock)
                {
                    snapshot = _world.Clone();
                }

                Draw(snapshot);

                int elapsed = (int)(Environment.TickCount64 - t0);
                int sleep = Math.Max(0, frameMs - elapsed);
                await Task.Delay(sleep);
            }

            try { Console.CursorVisible = true; } catch { }
        }

        private void Draw(WorldState w)
        {
            PlayerDto me = null;
            foreach (var p in w.Players)
            {
                if (p.id == _playerId) { me = p; break; }
            }

            double camX = (me != null) ? me.x : 0.0;

            int screenCols = Math.Max(60, Math.Max(1, Console.WindowWidth - 1));
            int screenRows = Math.Max(20, Math.Max(1, Console.WindowHeight - 1));

            double worldPerCol = 1.0;
            double leftX = Math.Max(0.0, camX - screenCols * 0.3);

            char[,] grid = new char[screenRows, screenCols];
            for (int r = 0; r < screenRows; r++)
                for (int c = 0; c < screenCols; c++)
                    grid[r, c] = ' ';

            int lanes = _cfg.Lanes;
            int top = _cfg.TrackHeightPadding;
            int bottom = screenRows - 1 - _cfg.TrackHeightPadding;
            int usable = Math.Max(1, bottom - top);

            Func<int, int> LaneToRow = lane =>
            {
                if (lanes <= 1) return (top + bottom) / 2;
                double t = lane / (double)(lanes - 1);
                int row = top + (int)Math.Round(t * usable);
                if (row < 0) row = 0;
                if (row >= screenRows) row = screenRows - 1;
                return row;
            };

            // bordes
            for (int c = 0; c < screenCols; c++)
            {
                int rTop = LaneToRow(0) - 1;
                int rBot = LaneToRow(lanes - 1) + 1;
                if (rTop >= 0 && rTop < screenRows) grid[rTop, c] = '-';
                if (rBot >= 0 && rBot < screenRows) grid[rBot, c] = '-';
            }

            // obstáculos
            foreach (var o in w.Obstacles)
            {
                int col = (int)Math.Round((o.x - leftX) / worldPerCol);
                if (col < 0 || col >= screenCols) continue;
                int row = LaneToRow(o.lane);
                if (row >= 0 && row < screenRows) grid[row, col] = '#';
            }

            // autos
            foreach (var p in w.Players)
            {
                int col = (int)Math.Round((p.x - leftX) / worldPerCol);
                if (col < 0 || col >= screenCols) continue;
                int row = LaneToRow(p.lane);
                if (row >= 0 && row < screenRows)
                {
                    char car = (p.id == _playerId) ? 'A' : 'a';
                    grid[row, col] = car;

                    string name = p.name ?? "";
                    if (name.Length > 8) name = name.Substring(0, 8);
                    for (int i = 0; i < name.Length && (col + i) < screenCols; i++)
                    {
                        int rr = row - 1;
                        if (rr >= 0) grid[rr, col + i] = name[i];
                    }
                }
            }

            // HUD
            string hud = "Tick:" + w.Tick +
                         "  You:" + (me != null ? me.name : "??") +
                         " Lane:" + (me != null ? me.lane.ToString() : "?") +
                         " X:" + (me != null ? me.x.ToString("F1") : "?") +
                         " Speed:" + (me != null ? me.speedFactor.ToString("F2") : "?");
            for (int i = 0; i < hud.Length && i < screenCols; i++) grid[0, i] = hud[i];

            var sb = new StringBuilder(screenRows * (screenCols + 1));
            for (int r = 0; r < screenRows; r++)
            {
                for (int c = 0; c < screenCols; c++) sb.Append(grid[r, c]);
                sb.Append('\n');
            }

            try { Console.SetCursorPosition(0, 0); } catch { }
            Console.Write(sb.ToString());
        }
    }

    // =================== Modelos / Mensajería ===================

    public class BaseMsg
    {
        public string type { get; set; }
    }

    public class JoinMsg : BaseMsg
    {
        public string name { get; set; }
        public JoinMsg() { type = "join"; name = ""; }
    }

    public class WelcomeMsg : BaseMsg
    {
        public int playerId { get; set; }
        public GameConfig cfg { get; set; }
        public WelcomeMsg() { type = "welcome"; }
    }

    public class InputMsg : BaseMsg
    {
        public int playerId { get; set; }
        public string action { get; set; }
        public InputMsg() { type = "input"; action = ""; }
    }

    public class LeaveMsg : BaseMsg
    {
        public int playerId { get; set; }
        public LeaveMsg() { type = "leave"; }
    }

    public class SnapshotMsg : BaseMsg
    {
        public int tick { get; set; }
        public List<PlayerDto> players { get; set; }
        public List<ObstacleDto> obstacles { get; set; }
        public SnapshotMsg()
        {
            type = "snapshot";
            players = new List<PlayerDto>();
            obstacles = new List<ObstacleDto>();
        }
    }

    // Debe coincidir con el del server para deserializar correctamente.
    public class GameConfig
    {
        public int ServerPort { get; set; } = 7777;
        public int TicksPerSecond { get; set; } = 20;
        public int Lanes { get; set; } = 5;
        public double BaseSpeed { get; set; } = 0.6;
        public double SlowFactor { get; set; } = 0.35;
        public int SlowDurationTicks { get; set; } = 12;
        public double ObstacleSpacingMin { get; set; } = 18.0;
        public double ObstacleSpacingMax { get; set; } = 32.0;
        public int SnapshotObstaclesAhead { get; set; } = 120;
        public int TrackHeightPadding { get; set; } = 1;
    }

    public class PlayerDto
    {
        public int id { get; set; }
        public string name { get; set; }
        public int lane { get; set; }
        public double x { get; set; }
        public double speedFactor { get; set; }
    }

    public class ObstacleDto
    {
        public int lane { get; set; }
        public double x { get; set; }
    }

    public class WorldState
    {
        public int Tick { get; set; }
        public List<PlayerDto> Players { get; set; }
        public List<ObstacleDto> Obstacles { get; set; }

        public WorldState()
        {
            Players = new List<PlayerDto>();
            Obstacles = new List<ObstacleDto>();
        }

        public WorldState Clone()
        {
            var copy = new WorldState();
            copy.Tick = this.Tick;

            foreach (var p in this.Players)
            {
                copy.Players.Add(new PlayerDto
                {
                    id = p.id,
                    name = p.name,
                    lane = p.lane,
                    x = p.x,
                    speedFactor = p.speedFactor
                });
            }
            foreach (var o in this.Obstacles)
            {
                copy.Obstacles.Add(new ObstacleDto { lane = o.lane, x = o.x });
            }
            return copy;
        }
    }
}
