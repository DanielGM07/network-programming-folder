using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerAdivinanzas
{
    internal sealed class Program
    {
        private const int Port = 9000;
        private static readonly List<Riddle> Riddles = new List<Riddle>
        {
            new Riddle("Blanca por dentro, verde por fuera. Si quieres que te lo diga, espera.", "Pera"),
            new Riddle("Oro parece, plata no es. ¿Qué es?", "Platano"),
            new Riddle("Vuelo sin alas, lloro sin ojos. ¿Qué soy?", "Nube"),
            new Riddle("Me quitas fuera y me dejas dentro. ¿Qué soy?", "Agujero"),
            new Riddle("Tiene agujas pero no cose. ¿Qué es?", "Reloj"),
            new Riddle("Redondo, redondo, barril sin fondo", "Anillo"),
        };

        private static void Main(string[] args)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                Log("Servidor de Adivinanzas iniciado en puerto " + Port + "...");

                while (true)
                {
                    var client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
            finally
            {
                if (listener != null) listener.Stop();
            }
        }

        private static void HandleClient(object state)
        {
            var client = (TcpClient)state;
            var remote = client.Client.RemoteEndPoint != null ? client.Client.RemoteEndPoint.ToString() : "desconocido";
            Log("Cliente conectado: " + remote);

            try
            {
                using (client)
                using (var netStream = client.GetStream())
                using (var reader = new StreamReader(netStream, Encoding.UTF8, false, 1024, false))
                using (var writer = new StreamWriter(netStream, new UTF8Encoding(false), 1024, false) { AutoFlush = true })
                {
                    // Protocolo simple: líneas de texto
                    writer.WriteLine("OK ServerAdivinanzas v1");
                    writer.WriteLine("Comandos: START | QUIT");
                    writer.WriteLine("Escribí START para comenzar o QUIT para salir:");

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var cmd = (line ?? "").Trim().ToUpperInvariant();
                        if (cmd == "QUIT")
                        {
                            writer.WriteLine("BYE");
                            break;
                        }

                        if (cmd == "START")
                        {
                            RunQuizSession(reader, writer);
                            writer.WriteLine("Fin de la sesión. Escribí START para jugar de nuevo o QUIT para salir:");
                        }
                        else
                        {
                            writer.WriteLine("Desconocido. Usá START o QUIT:");
                        }
                    }
                }
            }
            catch (IOException)
            {
                Log("Cliente " + remote + " desconectado.");
            }
            catch (Exception ex)
            {
                Log("ERROR con " + remote + ": " + ex.Message);
            }
        }

        private static void RunQuizSession(StreamReader reader, StreamWriter writer)
        {
            var rnd = new Random(unchecked(Environment.TickCount * 31 + Thread.CurrentThread.ManagedThreadId));
            var order = new List<int>();
            for (int i = 0; i < Riddles.Count; i++) order.Add(i);
            // Fisher-Yates
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var t = order[i]; order[i] = order[j]; order[j] = t;
            }

            int score = 0;
            foreach (var idx in order)
            {
                var r = Riddles[idx];
                writer.WriteLine("ADIVINANZA: " + r.Question);
                writer.WriteLine("Tu respuesta (o escribe SKIP para saltar, EXIT para terminar):");

                string answer = reader.ReadLine();
                if (answer == null) return;

                var ansTrim = answer.Trim();

                if (ansTrim.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteLine("Terminando sesión...");
                    break;
                }
                if (ansTrim.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteLine("Saltada. La respuesta era: " + r.Answer);
                    continue;
                }

                if (IsCorrect(ansTrim, r.Answer))
                {
                    score++;
                    writer.WriteLine("CORRECTO ✅  Puntuación: " + score);
                }
                else
                {
                    writer.WriteLine("INCORRECTO ❌  Respuesta correcta: " + r.Answer);
                }

                writer.WriteLine("---");
            }

            writer.WriteLine("Puntuación final: " + score + " / " + Riddles.Count);
        }

        private static bool IsCorrect(string input, string expected)
        {
            // Normalización simple: quitar tildes y comparar sin mayúsculas/minúsculas
            var a = Normalize(input);
            var b = Normalize(expected);
            return a == b;
        }

        private static string Normalize(string s)
        {
            s = s.Trim().ToLowerInvariant();
            s = s.Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");
            // tolerar artículos comunes
            if (s.StartsWith("el ")) s = s.Substring(3);
            if (s.StartsWith("la ")) s = s.Substring(3);
            if (s.StartsWith("un ")) s = s.Substring(3);
            if (s.StartsWith("una ")) s = s.Substring(4);
            return s;
        }

        private static void Log(string msg)
        {
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg);
        }

        private sealed class Riddle
        {
            public string Question;
            public string Answer;

            public Riddle(string q, string a)
            {
                Question = q;
                Answer = a;
            }
        }
    }
}
