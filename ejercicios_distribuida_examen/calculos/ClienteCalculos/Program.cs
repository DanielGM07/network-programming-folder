using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ClienteCalculos
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            string host = args.Length > 0 ? args[0] : "127.0.0.1";
            int port = args.Length > 1 ? ParsePort(args[1], 9000) : 9000;

            Console.WriteLine($"Conectando a {host}:{port} ...");

            try
            {
                using (var client = new TcpClient())
                {
                    client.NoDelay = true;
                    client.Connect(host, port);

                    using (NetworkStream ns = client.GetStream())
                    using (var reader = new StreamReader(ns, Encoding.UTF8, false, 8192, true))
                    using (var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        // **Leer SOLO una línea de bienvenida**
                        string banner = reader.ReadLine();
                        if (!string.IsNullOrEmpty(banner))
                            Console.WriteLine(banner);

                        Console.WriteLine();
                        Console.WriteLine("Escribí comandos (ADD/SUB/MUL/DIV/POW/EVAL/PING/HELP/EXIT).");
                        Console.WriteLine("Ejemplos: ADD 2 3   |   EVAL (2+3*4)   |   POW 2 10");
                        Console.WriteLine("Para salir: EXIT");
                        Console.WriteLine();

                        for (;;)
                        {
                            Console.Write("> ");
                            string line = Console.ReadLine();
                            if (line == null) break;
                            line = line.Trim();
                            if (line.Length == 0) continue;

                            writer.WriteLine(line);

                            // **Leer EXACTAMENTE una respuesta por comando**
                            string response = reader.ReadLine();
                            if (response == null)
                            {
                                Console.WriteLine("Conexión cerrada por el servidor.");
                                break;
                            }

                            Console.WriteLine(response);

                            if (response == "BYE") break;
                        }
                    }
                }
            }
            catch (SocketException se)
            {
                Console.WriteLine("No se pudo conectar: " + se.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex);
            }
        }

        private static int ParsePort(string s, int def)
        {
            int p;
            if (int.TryParse(s, out p) && p > 0 && p < 65536) return p;
            return def;
        }
    }
}
