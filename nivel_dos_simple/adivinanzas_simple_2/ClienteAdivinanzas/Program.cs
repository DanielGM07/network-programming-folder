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
                using (var cli = new TcpClient(host, port))
                using (var ns = cli.GetStream())
                using (var rd = new StreamReader(ns, Encoding.UTF8))
                using (var wr = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    // banner inicial
                    string line = SafeRead(rd); if (line != null) Console.WriteLine(line);
                    line = SafeRead(rd); if (line != null) Console.WriteLine(line);

                    wr.WriteLine(isMod ? "MOD" : "JUGAR");

                    if (isMod) ModoModerador(rd, wr);
                    else ModoJugador(rd, wr);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
        }

        private static void ModoJugador(StreamReader rd, StreamWriter wr)
        {
            string line;
            while ((line = SafeRead(rd)) != null)
            {
                if (line.EndsWith("RESPONDE:"))
                {
                    Console.WriteLine(line);
                    Console.Write("> ");
                    string input = Console.ReadLine();
                    if (input == null) input = "salir";
                    wr.WriteLine(input);
                }
                else if (line.EndsWith("(S/N)"))
                {
                    Console.WriteLine(line);
                    Console.Write("> ");
                    string yn = Console.ReadLine();
                    if (yn == null) yn = "N";
                    wr.WriteLine(yn);
                }
                else
                {
                    Console.WriteLine(line);
                    if (line == "BYE") break;
                }
            }
        }

        private static void ModoModerador(StreamReader rd, StreamWriter wr)
        {
            string line;
            while ((line = SafeRead(rd)) != null)
            {
                if (line == "CMD>")
                {
                    Console.Write("MOD> ");
                    string cmd = Console.ReadLine();
                    if (cmd == null) cmd = "EXIT";
                    wr.WriteLine(cmd);
                }
                else
                {
                    Console.WriteLine(line);
                    if (line == "BYE") break;
                }
            }
        }

        private static string SafeRead(StreamReader rd)
        {
            try { return rd.ReadLine(); } catch { return null; }
        }

        private static int TryPort(string s, int fallback)
        {
            int p; return int.TryParse(s, out p) ? p : fallback;
        }
    }
}
