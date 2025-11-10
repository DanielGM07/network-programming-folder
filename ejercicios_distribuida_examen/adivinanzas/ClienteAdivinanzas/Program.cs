using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ClienteAdivinanzas
{
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            string host = args != null && args.Length > 0 ? args[0] : "127.0.0.1";
            int port = args != null && args.Length > 1 ? ParsePort(args[1], 9000) : 9000;

            Console.WriteLine("Conectando a " + host + ":" + port + " ...");
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(host, port);
                    using (var netStream = client.GetStream())
                    using (var reader = new StreamReader(netStream, Encoding.UTF8, false, 1024, false))
                    using (var writer = new StreamWriter(netStream, new UTF8Encoding(false), 1024, false) { AutoFlush = true })
                    {
                        // Mostrar mensaje inicial del servidor (hasta que deje de haber líneas disponibles brevemente)
                        ReadAvailableLines(reader);

                        // Bucle principal: leer del usuario y enviar; mostrar respuestas del server
                        while (true)
                        {
                            Console.Write("> ");
                            string input = Console.ReadLine();
                            if (input == null) break;

                            writer.WriteLine(input);

                            // Si pedimos QUIT, el server responderá BYE y podremos salir
                            if (input.Trim().Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                            {
                                // Mostrar lo que diga y salir
                                string bye = SafeReadLine(reader);
                                if (bye != null) Console.WriteLine(bye);
                                break;
                            }

                            // Después de cada comando, leemos hasta que el servidor "pare" (no hay nuevas líneas momentáneamente)
                            ReadAvailableLines(reader);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
        }

        private static int ParsePort(string s, int fallback)
        {
            int p;
            return int.TryParse(s, out p) ? p : fallback;
        }

        private static void ReadAvailableLines(StreamReader reader)
        {
            // Lee inmediatamente todas las líneas disponibles sin bloquear indefinidamente
            // Intento breve de no bloquear: si no hay datos prontos, salimos.
            // Como NetworkStream.CanRead no nos dice si hay datos, usamos ReadLine con ReadTimeout corto.
            var baseStream = reader.BaseStream as NetworkStream;
            if (baseStream == null) return;

            int oldTimeout = baseStream.ReadTimeout;
            try
            {
                baseStream.ReadTimeout = 150; // ms
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    Console.WriteLine(line);
                }
            }
            catch (IOException)
            {
                // No hay más líneas disponibles ahora mismo; continuar
            }
            finally
            {
                baseStream.ReadTimeout = oldTimeout;
            }
        }

        private static string SafeReadLine(StreamReader reader)
        {
            try { return reader.ReadLine(); }
            catch { return null; }
        }
    }
}
