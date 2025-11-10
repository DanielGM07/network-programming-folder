using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClienteAutos
{
    class Program
    {
        static void Main()
        {
            Console.Write("Tu nombre: ");
            string name = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(name)) name = "Jugador";

            Console.Write("Tipo de auto [Ligero | Equilibrado | Blindado] (enter = Equilibrado): ");
            string type = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(type)) type = "Equilibrado";

            UdpClient udp = new UdpClient();
            IPEndPoint server = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 7777);

            // recibir
            Task.Run(() =>
            {
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                try { Console.CursorVisible = false; } catch { }
                while (true)
                {
                    try
                    {
                        var res = udp.Receive(ref any);
                        string json = Encoding.UTF8.GetString(res);
                        var state = JsonSerializer.Deserialize<GameState>(json);
                        if (state != null) Dibujar(state, name);
                    }
                    catch { }
                }
            });

            // join con tipo
            Enviar(udp, server, new Dictionary<string, string> { { "name", name }, { "type", type } });
            Console.WriteLine("Usá ↑ / ↓ para moverte | ESC para salir");

            // input
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    string action = null;
                    if (key == ConsoleKey.UpArrow) action = "up";
                    else if (key == ConsoleKey.DownArrow) action = "down";
                    else if (key == ConsoleKey.Escape)
                    {
                        Enviar(udp, server, new Dictionary<string, string> { { "name", name }, { "action", "quit" } });
                        try { Console.CursorVisible = true; } catch { }
                        break;
                    }
                    if (action != null)
                        Enviar(udp, server, new Dictionary<string, string> { { "name", name }, { "action", action } });
                }
                Thread.Sleep(50);
            }
        }

        static void Enviar(UdpClient udp, IPEndPoint server, object obj)
        {
            string json = JsonSerializer.Serialize(obj);
            byte[] data = Encoding.UTF8.GetBytes(json);
            udp.Send(data, data.Length, server);
        }

        static void Dibujar(GameState state, string myName)
        {
            try { Console.SetCursorPosition(0, 0); } catch { }

            int ancho = 70;
            int alto = 14;
            char[,] grid = new char[alto, ancho];
            for (int y = 0; y < alto; y++)
                for (int x = 0; x < ancho; x++)
                    grid[y, x] = ' ';

            // Cámara centrada en mi auto si existe, si no en el primero
            double camX = 0;
            int myLane = 0, myLife = 0;
            foreach (var p in state.Players)
            {
                if (string.Equals(p.Name, myName, StringComparison.OrdinalIgnoreCase))
                {
                    camX = p.X; myLane = p.Lane; myLife = p.Life; break;
                }
            }
            if (camX == 0 && state.Players.Count > 0) camX = state.Players[0].X;

            // Obstáculos
            foreach (var o in state.Obstacles)
            {
                int x = (int)(o.X - camX + ancho / 3);
                if (x >= 0 && x < ancho && o.Lane >= 0 && o.Lane < alto)
                    grid[o.Lane, x] = '#';
            }

            // Autos
            foreach (var p in state.Players)
            {
                int x = (int)(p.X - camX + ancho / 3);
                if (x >= 0 && x < ancho && p.Lane >= 0 && p.Lane < alto)
                {
                    char c = (p.Name.Length > 0 ? char.ToUpperInvariant(p.Name[0]) : 'A');
                    grid[p.Lane, x] = c;
                    // Nombre encima (capado a 8)
                    string label = $"{p.Name}:{p.Life}";
                    if (label.Length > 10) label = label.Substring(0, 10);
                    int rr = p.Lane - 1;
                    for (int i = 0; rr >= 0 && i < label.Length && (x + i) < ancho; i++)
                        grid[rr, x + i] = label[i];
                }
            }

            // Pintar
            for (int y = 0; y < alto; y++)
            {
                for (int x = 0; x < ancho; x++) Console.Write(grid[y, x]);
                Console.WriteLine();
            }

            Console.WriteLine($"Tick:{state.Tick}  MiVida:{myLife}  MiLane:{myLane}   ↑/↓ mover, ESC salir");
        }

        class PlayerView
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int Life { get; set; }
            public double X { get; set; }
            public int Lane { get; set; }
        }

        class Obstacle { public double X { get; set; } public int Lane { get; set; } }

        class GameState
        {
            public int Tick { get; set; }
            public List<PlayerView> Players { get; set; } = new List<PlayerView>();
            public List<Obstacle> Obstacles { get; set; } = new List<Obstacle>();
        }
    }
}
