using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ServerAdivinanzas
{
    internal sealed class Program
    {
        private const int Port = 9000;

        private static readonly string[,] Riddles = new string[,]
        {
            { "Blanca por dentro, verde por fuera. ¿Qué es?", "pera" },
            { "Oro parece, plata no es. ¿Qué es?", "platano" },
            { "Tiene agujas pero no cose. ¿Qué es?", "reloj" },
        };

        private static void Main(string[] args)
        {
            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Console.WriteLine("Servidor escuchando en puerto " + Port);

            while (true) // súper simple: atiende un cliente a la vez
            {
                using (var client = listener.AcceptTcpClient())
                using (var ns = client.GetStream())
                using (var reader = new StreamReader(ns, Encoding.UTF8))
                using (var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    writer.WriteLine("OK ServerAdivinanzas");
                    writer.WriteLine("Escribe tus respuestas. Escribe 'salir' para terminar.");

                    int score = 0;
                    for (int i = 0; i < Riddles.GetLength(0); i++)
                    {
                        string q = Riddles[i, 0];
                        string a = Riddles[i, 1];

                        writer.WriteLine("PREGUNTA: " + q);
                        writer.WriteLine("RESPONDE:");

                        string ans = reader.ReadLine();
                        if (ans == null || ans.Trim().ToLowerInvariant() == "salir") break;

                        if (Normalize(ans) == Normalize(a))
                        {
                            score++;
                            writer.WriteLine("CORRECTO (" + score + ")");
                        }
                        else
                        {
                            writer.WriteLine("INCORRECTO. Era: " + a);
                        }
                    }

                    writer.WriteLine("PUNTAJE FINAL: " + score);
                    writer.WriteLine("BYE");
                }
            }
        }

        private static string Normalize(string s)
        {
            if (s == null) return "";
            s = s.Trim().ToLowerInvariant();
            s = s.Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");
            return s;
        }
    }
}
