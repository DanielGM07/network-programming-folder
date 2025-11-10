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

            Console.WriteLine("Conectando a " + host + ":" + port + "...");
            try
            {
                using (var client = new TcpClient(host, port))
                using (var ns = client.GetStream())
                using (var reader = new StreamReader(ns, Encoding.UTF8))
                using (var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.EndsWith("RESPONDE:"))
                        {
                            Console.WriteLine(line);
                            Console.Write("> ");
                            string input = Console.ReadLine();
                            if (input == null) input = "salir";
                            writer.WriteLine(input);
                        }
                        else
                        {
                            Console.WriteLine(line);
                            if (line == "BYE") break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
        }

        private static int TryPort(string s, int fallback)
        {
            int p;
            return int.TryParse(s, out p) ? p : fallback;
        }
    }
}
