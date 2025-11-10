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
            int port = args != null && args.Length > 1 ? TryPort(args[1], 9000) : 9000;

            Console.Write("¿Entrar como moderador? (s/n): ");
            bool isMod = (Console.ReadLine() ?? "").Trim().ToLowerInvariant() == "s";

            Console.WriteLine("Conectando a " + host + ":" + port + "...");
            try
            {
                using (var client = new TcpClient(host, port))
                using (var ns = client.GetStream())
                using (var reader = new StreamReader(ns, Encoding.UTF8))
                using (var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    // banner
                    string line = SafeReadLine(reader);
                    if (line != null) Console.WriteLine(line);
                    line = SafeReadLine(reader);
                    if (line != null) Console.WriteLine(line);

                    // elegir modo
                    writer.WriteLine(isMod ? "MOD" : "JUGAR");

                    if (isMod)
                        RunModerator(reader, writer);
                    else
                        RunPlayer(reader, writer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
        }

        private static void RunPlayer(StreamReader reader, StreamWriter writer)
        {
            string line;
            while ((line = SafeReadLine(reader)) != null)
            {
                if (line.EndsWith("RESPONDE:"))
                {
                    Console.WriteLine(line);
                    Console.Write("> ");
                    string input = Console.ReadLine();
                    if (input == null) input = "salir";
                    writer.WriteLine(input);
                }
                else if (line.EndsWith("(S/N)"))
                {
                    // Pregunta de sugerencia
                    Console.WriteLine(line);
                    Console.Write("> ");
                    string yn = Console.ReadLine();
                    if (yn == null) yn = "N";
                    writer.WriteLine(yn);
                }
                else
                {
                    Console.WriteLine(line);
                    if (line == "BYE") break;
                }
            }
        }

        private static void RunModerator(StreamReader reader, StreamWriter writer)
        {
            string line;
            while ((line = SafeReadLine(reader)) != null)
            {
                if (line == "CMD>")
                {
                    Console.Write("MOD> ");
                    string cmd = Console.ReadLine();
                    if (cmd == null) cmd = "EXIT";
                    writer.WriteLine(cmd);
                    if (cmd.Trim().ToUpperInvariant() == "EXIT")
                    {
                        // Esperar el BYE
                        continue;
                    }
                }
                else
                {
                    Console.WriteLine(line);
                    if (line == "BYE") break;
                }
            }
        }

        private static string SafeReadLine(StreamReader reader)
        {
            try { return reader.ReadLine(); }
            catch { return null; }
        }

        private static int TryPort(string s, int fallback)
        {
            int p;
            return int.TryParse(s, out p) ? p : fallback;
        }
    }
}
