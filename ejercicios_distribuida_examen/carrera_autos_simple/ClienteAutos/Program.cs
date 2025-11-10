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

            UdpClient udp = new UdpClient();
            IPEndPoint server = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 7777);

            // Recibir estado del juego
            Task.Run(() =>
            {
                // usar endpoint "any" para no sobreescribir 'server'
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                try { Console.CursorVisible = false; } catch { }

                while (true)
                {
                    try
                    {
                        var res = udp.Receive(ref any);
                        string json = Encoding.UTF8.GetString(res);
                        var state = JsonSerializer.Deserialize<GameState>(json);
                        if (state != null) Dibujar(state);
                    }
                    catch { }
                }
            });

            // Enviar join
            Enviar(udp, server, new { name });
            Console.WriteLine("Usá ↑ / ↓ para moverte | ESC para salir");

            // Loop de input
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
                        // Avisar salida y terminar
                        Enviar(udp, server, new { name, action = "quit" });
                        try { Console.CursorVisible = true; } catch { }
                        break;
                    }

                    if (action != null)
                        Enviar(udp, server, new { name, action });
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

        static void Dibujar(GameState state)
        {
            // Evita parpadeo/cursores saltando
            try { Console.SetCursorPosition(0, 0); } catch { }

            int ancho = 60;
            int alto = 12;
            char[,] grid = new char[alto, ancho];

            for (int y = 0; y < alto; y++)
                for (int x = 0; x < ancho; x++)
                    grid[y, x] = ' ';

            // Centrar cámara en el primer jugador (si hay)
            double camX = 0;
            if (state.Players.Count > 0)
                camX = state.Players[0].X;

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
                    grid[p.Lane, x] = !string.IsNullOrEmpty(p.Name) ? p.Name[0] : 'A';
            }

            // Pintar
            for (int y = 0; y < alto; y++)
            {
                for (int x = 0; x < ancho; x++)
                    Console.Write(grid[y, x]);
                Console.WriteLine();
            }

            Console.WriteLine("↑/↓ para moverte, ESC para salir");
        }

        class Player
        {
            public string Name { get; set; }
            public double X { get; set; }
            public int Lane { get; set; }
        }

        class Obstacle
        {
            public double X { get; set; }
            public int Lane { get; set; }
        }

        class GameState
        {
            public List<Player> Players { get; set; } = new List<Player>();
            public List<Obstacle> Obstacles { get; set; } = new List<Obstacle>();
        }
    }
}
