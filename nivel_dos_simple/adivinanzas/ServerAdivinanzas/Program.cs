using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ServerAdivinanzas
{
    internal sealed class Program
    {
        private const int Port = 9000;

        private static void Main(string[] args)
        {
            var store = new RiddleStore("riddles_extra.txt");
            var suggestionStore = new SuggestionStore("sugerencias.txt");

            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Console.WriteLine("Servidor escuchando en puerto " + Port);

            while (true) // simple: atiende un cliente a la vez
            {
                using (var client = listener.AcceptTcpClient())
                using (var ns = client.GetStream())
                using (var reader = new StreamReader(ns, Encoding.UTF8))
                using (var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    writer.WriteLine("OK ServerAdivinanzas v2");
                    writer.WriteLine("Escribí 'MOD' para entrar en modo moderador, o cualquier otra cosa para jugar:");
                    string first = reader.ReadLine();
                    if (first != null && first.Trim().ToUpperInvariant() == "MOD")
                    {
                        RunModeratorSession(reader, writer, store, suggestionStore);
                    }
                    else
                    {
                        RunPlayerSession(reader, writer, store, suggestionStore, GetClientLabel(client));
                    }
                }
            }
        }

        private static string GetClientLabel(TcpClient c)
        {
            try
            {
                return c.Client.RemoteEndPoint != null ? c.Client.RemoteEndPoint.ToString() : "desconocido";
            }
            catch { return "desconocido"; }
        }

        // ==================== PLAYER ====================
        private static void RunPlayerSession(StreamReader reader, StreamWriter writer, RiddleStore store, SuggestionStore suggestionStore, string clientLabel)
        {
            List<Riddle> all = store.GetAllRiddles();
            writer.WriteLine("Vas a jugar. Escribe 'salir' para terminar.");
            int score = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                writer.WriteLine("PREGUNTA: " + r.Question);
                writer.WriteLine("RESPONDE:");

                string ans = reader.ReadLine();
                if (ans == null || ans.Trim().ToLowerInvariant() == "salir")
                    break;

                if (Normalize(ans) == Normalize(r.Answer))
                {
                    score++;
                    writer.WriteLine("CORRECTO (" + score + ")");
                }
                else
                {
                    writer.WriteLine("INCORRECTO. Era: " + r.Answer);
                    writer.WriteLine("¿Querés enviar tu respuesta como sugerencia? (S/N)");
                    string yn = reader.ReadLine();
                    if (yn != null && yn.Trim().Equals("S", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = suggestionStore.AddSuggestion(r.Question, ans, clientLabel);
                        writer.WriteLine("Sugerencia enviada con ID " + id);
                    }
                }
            }

            writer.WriteLine("PUNTAJE FINAL: " + score);
            writer.WriteLine("BYE");
        }

        // ==================== MODERATOR ====================
        private static void RunModeratorSession(StreamReader reader, StreamWriter writer, RiddleStore store, SuggestionStore suggestionStore)
        {
            writer.WriteLine("MOD OK");
            writer.WriteLine("Comandos: LIST | APPROVE <id> | REJECT <id> | HELP | EXIT");
            writer.WriteLine("CMD>");

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = (line ?? "").Trim().Split(new char[] { ' ' }, 2);
                var cmd = parts[0].ToUpperInvariant();

                if (cmd == "EXIT")
                {
                    writer.WriteLine("Saliendo del modo moderador.");
                    break;
                }
                else if (cmd == "HELP")
                {
                    writer.WriteLine("LIST: muestra sugerencias pendientes");
                    writer.WriteLine("APPROVE <id>: aprueba y agrega a las adivinanzas");
                    writer.WriteLine("REJECT <id>: rechaza la sugerencia");
                }
                else if (cmd == "LIST")
                {
                    var list = suggestionStore.ListPending();
                    if (list.Count == 0)
                    {
                        writer.WriteLine("(Sin sugerencias pendientes)");
                    }
                    else
                    {
                        foreach (var s in list)
                        {
                            writer.WriteLine("ID " + s.Id + " | Pregunta: " + s.Question);
                            writer.WriteLine("   Propuesta: " + s.ProposedAnswer);
                            writer.WriteLine("   Por: " + s.By + " | Fecha: " + s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                    }
                }
                else if (cmd == "APPROVE")
                {
                    if (parts.Length < 2)
                    {
                        writer.WriteLine("Uso: APPROVE <id>");
                    }
                    else
                    {
                        int id;
                        if (!int.TryParse(parts[1], out id))
                        {
                            writer.WriteLine("ID inválido.");
                        }
                        else
                        {
                            var sug = suggestionStore.GetById(id);
                            if (sug == null || sug.Status != "PENDING")
                            {
                                writer.WriteLine("No existe o no está pendiente.");
                            }
                            else
                            {
                                // Agregar como nueva adivinanza
                                store.AppendExtra(sug.Question, sug.ProposedAnswer);
                                suggestionStore.UpdateStatus(id, "APPROVED");
                                writer.WriteLine("Aprobada. Agregada a las adivinanzas.");
                            }
                        }
                    }
                }
                else if (cmd == "REJECT")
                {
                    if (parts.Length < 2)
                    {
                        writer.WriteLine("Uso: REJECT <id>");
                    }
                    else
                    {
                        int id;
                        if (!int.TryParse(parts[1], out id))
                        {
                            writer.WriteLine("ID inválido.");
                        }
                        else
                        {
                            var ok = suggestionStore.UpdateStatus(id, "REJECTED");
                            writer.WriteLine(ok ? "Rechazada." : "No se pudo rechazar (ID incorrecto o ya procesada).");
                        }
                    }
                }
                else
                {
                    writer.WriteLine("Comando desconocido. Usá LIST | APPROVE <id> | REJECT <id> | HELP | EXIT");
                }

                writer.WriteLine("CMD>");
            }

            writer.WriteLine("BYE");
        }

        // ==================== HELPERS ====================
        private static string Normalize(string s)
        {
            if (s == null) return "";
            s = s.Trim().ToLowerInvariant();
            s = s.Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");
            return s;
        }
    }

    // ==================== MODELOS ====================
    internal sealed class Riddle
    {
        public string Question;
        public string Answer;
        public Riddle(string q, string a) { Question = q; Answer = a; }
    }

    internal sealed class Suggestion
    {
        public int Id;
        public string Status; // PENDING | APPROVED | REJECTED
        public string Question;
        public string ProposedAnswer;
        public string By;
        public DateTime Timestamp;
    }

    // ==================== PERSISTENCIA DE ADIVINANZAS ====================
    internal sealed class RiddleStore
    {
        private readonly string _extrasFile;

        public RiddleStore(string extrasFile)
        {
            _extrasFile = extrasFile;
            if (!File.Exists(_extrasFile))
            {
                File.WriteAllText(_extrasFile, "", new UTF8Encoding(false));
            }
        }

        public List<Riddle> GetAllRiddles()
        {
            var list = new List<Riddle>();

            // base simples
            list.Add(new Riddle("Blanca por dentro, verde por fuera. ¿Qué es?", "pera"));
            list.Add(new Riddle("Oro parece, plata no es. ¿Qué es?", "platano"));
            list.Add(new Riddle("Tiene agujas pero no cose. ¿Qué es?", "reloj"));

            // extras aprobadas
            string[] lines = File.ReadAllLines(_extrasFile, new UTF8Encoding(false));
            for (int i = 0; i < lines.Length; i++)
            {
                var ln = lines[i].Trim();
                if (ln.Length == 0) continue;
                var parts = ln.Split(new char[] { '|' }, 2);
                if (parts.Length == 2)
                {
                    list.Add(new Riddle(parts[0], parts[1]));
                }
            }

            return list;
        }

        public void AppendExtra(string question, string answer)
        {
            using (var sw = new StreamWriter(_extrasFile, true, new UTF8Encoding(false)))
            {
                sw.WriteLine(question.Replace("\r", " ").Replace("\n", " ") + "|" +
                             answer.Replace("\r", " ").Replace("\n", " "));
            }
        }
    }

    // ==================== PERSISTENCIA DE SUGERENCIAS ====================
    internal sealed class SuggestionStore
    {
        private readonly string _file;

        public SuggestionStore(string file)
        {
            _file = file;
            if (!File.Exists(_file))
            {
                File.WriteAllText(_file, "", new UTF8Encoding(false));
            }
        }

        public int AddSuggestion(string question, string proposedAnswer, string by)
        {
            int newId = GetNextId();
            var line = string.Join("|", new string[]
            {
                newId.ToString(CultureInfo.InvariantCulture),
                "PENDING",
                Sanitize(question),
                Sanitize(proposedAnswer),
                Sanitize(by),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            });

            using (var sw = new StreamWriter(_file, true, new UTF8Encoding(false)))
            {
                sw.WriteLine(line);
            }
            return newId;
        }

        public List<Suggestion> ListPending()
        {
            var list = new List<Suggestion>();
            foreach (var s in ReadAll())
            {
                if (s.Status == "PENDING") list.Add(s);
            }
            return list;
        }

        public Suggestion GetById(int id)
        {
            foreach (var s in ReadAll())
            {
                if (s.Id == id) return s;
            }
            return null;
        }

        public bool UpdateStatus(int id, string newStatus)
        {
            var all = ReadAll();
            bool changed = false;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id && all[i].Status == "PENDING")
                {
                    all[i].Status = newStatus;
                    changed = true;
                    break;
                }
            }
            if (changed) WriteAll(all);
            return changed;
        }

        private int GetNextId()
        {
            int max = 0;
            foreach (var s in ReadAll()) if (s.Id > max) max = s.Id;
            return max + 1;
        }

        private List<Suggestion> ReadAll()
        {
            var list = new List<Suggestion>();
            string[] lines = File.ReadAllLines(_file, new UTF8Encoding(false));
            for (int i = 0; i < lines.Length; i++)
            {
                var ln = lines[i].Trim();
                if (ln.Length == 0) continue;
                var parts = ln.Split('|');
                if (parts.Length >= 6)
                {
                    int id;
                    DateTime ts;
                    int.TryParse(parts[0], out id);
                    DateTime.TryParse(parts[5], CultureInfo.InvariantCulture, DateTimeStyles.None, out ts);
                    list.Add(new Suggestion
                    {
                        Id = id,
                        Status = parts[1],
                        Question = parts[2],
                        ProposedAnswer = parts[3],
                        By = parts[4],
                        Timestamp = ts
                    });
                }
            }
            return list;
        }

        private void WriteAll(List<Suggestion> items)
        {
            using (var sw = new StreamWriter(_file, false, new UTF8Encoding(false)))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var s = items[i];
                    sw.WriteLine(string.Join("|", new string[]
                    {
                        s.Id.ToString(CultureInfo.InvariantCulture),
                        s.Status,
                        Sanitize(s.Question),
                        Sanitize(s.ProposedAnswer),
                        Sanitize(s.By),
                        s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    }));
                }
            }
        }

        private static string Sanitize(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s;
        }
    }
}
