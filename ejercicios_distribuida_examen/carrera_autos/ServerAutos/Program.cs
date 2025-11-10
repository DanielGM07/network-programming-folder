// Program.cs — ServerAutos (C# 8 compatible, sin top-level statements)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServerAutos
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var config = new GameConfig();
            var server = new GameServer(config);

            Console.WriteLine("=== UDP Racing Server ===");
            Console.WriteLine("Escuchando UDP en puerto " + config.ServerPort + " ...");
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; server.Stop(); };

            await server.RunAsync();
        }
    }

    // =============== Configuración del juego ===============
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

    // =============== Servidor de juego ===============
    public class GameServer
    {
        private readonly GameConfig _cfg;
        private readonly UdpClient _udp;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentQueue<Tuple<IPEndPoint, string>> _inbox = new ConcurrentQueue<Tuple<IPEndPoint, string>>();

        private readonly Dictionary<int, Player> _players = new Dictionary<int, Player>();
        private readonly Dictionary<IPEndPoint, int> _endpointToId = new Dictionary<IPEndPoint, int>(new IPEndPointComparer());
        private readonly List<Obstacle> _obstacles = new List<Obstacle>();
        private readonly Random _rng = new Random();

        private int _nextId = 1;
        private int _tick = 0;
        private bool _running = false;

        public GameServer(GameConfig cfg)
        {
            _cfg = cfg;
            _udp = new UdpClient(cfg.ServerPort);

            // IGNORAR UDP CONNRESET en Windows (evita spam por ICMP Port Unreachable)
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
            Task recvTask = Task.Run(new Func<Task>(ReceiveLoop), _cts.Token);
            int tickMs = 1000 / _cfg.TicksPerSecond;

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            SeedObstacles(0, 100);

            while (_running)
            {
                long t0 = sw.ElapsedMilliseconds;

                // Procesar mensajes
                Tuple<IPEndPoint, string> item;
                while (_inbox.TryDequeue(out item))
                {
                    try { HandleMessage(item.Item1, item.Item2); }
                    catch (Exception ex) { Console.WriteLine("[WARN] HandleMessage: " + ex.Message); }
                }

                // Simulación y broadcast
                Simulate();
                await BroadcastSnapshotAsync();

                _tick++;
                int elapsed = (int)(sw.ElapsedMilliseconds - t0);
                int sleep = Math.Max(0, tickMs - elapsed);
                await Task.Delay(sleep);
            }

            try { _udp.Close(); } catch { }
            _cts.Cancel();
            await recvTask;
        }

        public void Stop() { _running = false; }

        private void SeedObstacles(double fromX, double toX)
        {
            for (int lane = 0; lane < _cfg.Lanes; lane++)
            {
                double x = fromX + _rng.NextDouble() * (_cfg.ObstacleSpacingMin + 5.0);
                while (x < toX)
                {
                    _obstacles.Add(new Obstacle { Lane = lane, X = x });
                    x += _cfg.ObstacleSpacingMin + _rng.NextDouble() * (_cfg.ObstacleSpacingMax - _cfg.ObstacleSpacingMin);
                }
            }
        }

        private async Task ReceiveLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
#if NET8_0_OR_GREATER || NET9_0_OR_GREATER
                    var result = await _udp.ReceiveAsync(_cts.Token);
#else
                    var result = await _udp.ReceiveAsync();
#endif
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    _inbox.Enqueue(Tuple.Create(result.RemoteEndPoint, json));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException sx)
                {
                    // Filtrar ruidos comunes al cerrar/cancelar
                    if (sx.ErrorCode == 10054 || sx.ErrorCode == 10004) continue;
                    Console.WriteLine("[WARN] ReceiveLoop: " + sx.Message);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WARN] ReceiveLoop: " + ex.Message);
                }
            }
        }

        private void HandleMessage(IPEndPoint ep, string json)
        {
            BaseMsg msg = null;
            try { msg = JsonSerializer.Deserialize<BaseMsg>(json); } catch { }
            if (msg == null) return;

            if (msg.type == "join")
            {
                JoinMsg j = JsonSerializer.Deserialize<JoinMsg>(json);
                if (j == null) return;
                if (_endpointToId.ContainsKey(ep)) return;

                int playerId = _nextId++;
                int lane = _players.Count % _cfg.Lanes;

                var p = new Player
                {
                    Id = playerId,
                    Name = string.IsNullOrWhiteSpace(j.name) ? "P" + playerId : j.name.Trim(),
                    Lane = lane,
                    X = 0.0,
                    SpeedFactor = 1.0,
                    PendingLaneDelta = 0,
                    SlowUntilTick = 0,
                    LastHitObstacleX = -9999.0,
                    LastHitTick = -9999
                };

                _players[playerId] = p;
                _endpointToId[ep] = playerId;

                Console.WriteLine("JOIN " + p.Name + " (" + playerId + ") desde " + ep + " lane=" + lane);

                var welcome = new WelcomeMsg
                {
                    type = "welcome",
                    playerId = playerId,
                    cfg = _cfg
                };
                Send(ep, welcome);
            }
            else if (msg.type == "input")
            {
                InputMsg im = JsonSerializer.Deserialize<InputMsg>(json);
                if (im == null) return;
                Player p;
                if (!_players.TryGetValue(im.playerId, out p)) return;
                int mapped;
                if (!_endpointToId.TryGetValue(ep, out mapped) || mapped != im.playerId) return;

                if (im.action == "up") p.PendingLaneDelta = -1;
                else if (im.action == "down") p.PendingLaneDelta = +1;
            }
            else if (msg.type == "leave")
            {
                LeaveMsg lm = JsonSerializer.Deserialize<LeaveMsg>(json);
                if (lm == null) return;

                // Encontrar endpoint del player y removerlo
                IPEndPoint toRemoveEp = null;
                foreach (var kv in _endpointToId)
                {
                    if (kv.Value == lm.playerId) { toRemoveEp = kv.Key; break; }
                }
                if (toRemoveEp != null) _endpointToId.Remove(toRemoveEp);
                _players.Remove(lm.playerId);

                Console.WriteLine("LEAVE playerId=" + lm.playerId);
            }
            else if (msg.type == "ping")
            {
                // opcional
            }
        }

        private void Simulate()
        {
            double maxX = (_players.Count > 0) ? _players.Values.Max(pp => pp.X) : 0.0;
            double neededTo = maxX + 200.0;

            if (_obstacles.Count == 0 || _obstacles.Max(o => o.X) < neededTo - 60.0)
            {
                double start = (_obstacles.Count == 0) ? 0.0 : _obstacles.Max(o => o.X) + 10.0;
                SeedObstacles(start, neededTo);
            }

            foreach (var p in _players.Values)
            {
                if (p.PendingLaneDelta != 0)
                {
                    int newLane = Clamp(p.Lane + p.PendingLaneDelta, 0, _cfg.Lanes - 1);
                    p.Lane = newLane;
                    p.PendingLaneDelta = 0;
                }

                p.SpeedFactor = (p.SlowUntilTick > _tick) ? _cfg.SlowFactor : 1.0;
                p.X += _cfg.BaseSpeed * p.SpeedFactor;

                foreach (var o in _obstacles)
                {
                    if (o.Lane != p.Lane) continue;
                    if (Math.Abs(o.X - p.X) < 0.6)
                    {
                        if (Math.Abs(o.X - p.LastHitObstacleX) < 0.6 && (_tick - p.LastHitTick) < _cfg.SlowDurationTicks)
                            continue;

                        p.SlowUntilTick = _tick + _cfg.SlowDurationTicks;
                        p.LastHitObstacleX = o.X;
                        p.LastHitTick = _tick;
                        break;
                    }
                }
            }
        }

        private async Task BroadcastSnapshotAsync()
        {
            if (_endpointToId.Count == 0) return;

            double maxX = (_players.Count == 0) ? 0.0 : _players.Values.Max(pp => pp.X);
            double minX = (_players.Count == 0) ? 0.0 : _players.Values.Min(pp => pp.X);

            double rightEdge = maxX + _cfg.SnapshotObstaclesAhead;
            double leftEdge = Math.Max(0.0, minX - 10.0);

            List<ObstacleDto> obs = _obstacles
                .Where(o => o.X >= leftEdge && o.X <= rightEdge)
                .Select(o => new ObstacleDto { lane = o.Lane, x = o.X })
                .ToList();

            List<PlayerDto> players = _players.Values
                .Select(p => new PlayerDto { id = p.Id, name = p.Name, lane = p.Lane, x = p.X, speedFactor = p.SpeedFactor })
                .ToList();

            var snap = new SnapshotMsg
            {
                type = "snapshot",
                tick = _tick,
                players = players,
                obstacles = obs
            };

            string json = JsonSerializer.Serialize(snap);
            byte[] data = Encoding.UTF8.GetBytes(json);

            var deadEndpoints = new List<IPEndPoint>();

            foreach (var kv in _endpointToId)
            {
                var ep = kv.Key;
                try
                {
                    await _udp.SendAsync(data, data.Length, ep);
                }
                catch (SocketException sx)
                {
                    // 10054/10004: endpoint ya no escucha / cancelado
                    if (sx.ErrorCode == 10054 || sx.ErrorCode == 10004) deadEndpoints.Add(ep);
                }
                catch
                {
                    deadEndpoints.Add(ep);
                }
            }

            foreach (var ep in deadEndpoints)
            {
                int pid;
                if (_endpointToId.TryGetValue(ep, out pid))
                    _players.Remove(pid);
                _endpointToId.Remove(ep);
            }
        }

        private void Send(IPEndPoint ep, object msg)
        {
            string json = JsonSerializer.Serialize(msg);
            byte[] data = Encoding.UTF8.GetBytes(json);
            _udp.Send(data, data.Length, ep);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    // =============== Modelos y DTOs ===============
    public class Player
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Lane { get; set; }
        public double X { get; set; }
        public double SpeedFactor { get; set; }

        public int PendingLaneDelta { get; set; }
        public int SlowUntilTick { get; set; }

        public double LastHitObstacleX { get; set; }
        public int LastHitTick { get; set; }
    }

    public class Obstacle
    {
        public int Lane { get; set; }
        public double X { get; set; }
    }

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

    // =============== Util: comparar IPEndPoint como clave de diccionario ===============
    public class IPEndPointComparer : IEqualityComparer<IPEndPoint>
    {
        public bool Equals(IPEndPoint x, IPEndPoint y)
        {
            if (object.ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            return x.Address.Equals(y.Address) && x.Port == y.Port;
        }

        public int GetHashCode(IPEndPoint obj)
        {
            return obj.Address.GetHashCode() ^ obj.Port.GetHashCode();
        }
    }
}
