using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;

namespace ServerAdivinanzas
{
    internal sealed class Program
    {
        private const int PORT = 9000;
        private const string SUG_FILE = "sugerencias.txt";
        private const string EXTRA_FILE = "riddles_extra.txt";

        private static void Main(string[] args)
        {
            if (!File.Exists(SUG_FILE)) File.WriteAllText(SUG_FILE, "", new UTF8Encoding(false));
            if (!File.Exists(EXTRA_FILE)) File.WriteAllText(EXTRA_FILE, "", new UTF8Encoding(false));

            var listener = new TcpListener(IPAddress.Any, PORT);
            listener.Start();
            Console.WriteLine("Servidor Adivinanzas (simple) puerto " + PORT);

            while (true) // un cliente por vez (sencillo)
            {
                using (var client = listener.AcceptTcpClient())
                using (var ns = client.GetStream())
                using (var rd = new StreamReader(ns, Encoding.UTF8))
                using (var wr = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    wr.WriteLine("OK ServerAdivinanzas");
                    wr.WriteLine("Escribe 'MOD' para moderador o cualquier cosa para jugar:");
                    string modo = ReadOrDefault(rd, "");
                    if (modo.Trim().ToUpperInvariant() == "MOD")
                        Moderador(rd, wr);
                    else
                        Jugar(rd, wr, client);
                }
            }
        }

        // ======== JUGADOR ========
        private static void Jugar(StreamReader rd, StreamWriter wr, TcpClient c)
        {
            var preguntas = CargarAdivinanzasBase();                // solo base
            var aprobadas = CargarRespuestasAprobadasPorPregunta(); // alternativas por pregunta

            wr.WriteLine("Vas a jugar. Escribe 'salir' para terminar.");
            int score = 0;

            for (int i = 0; i < preguntas.Count; i++)
            {
                var r = preguntas[i];
                wr.WriteLine("PREGUNTA: " + r.q);
                wr.WriteLine("RESPONDE:");

                string ans = ReadOrDefault(rd, "salir");
                if (ans.Trim().ToLowerInvariant() == "salir") break;

                if (EsCorrecta(r.q, r.a, ans, aprobadas))
                {
                    score++;
                    wr.WriteLine("CORRECTO (" + score + ")");
                }
                else
                {
                    wr.WriteLine("INCORRECTO. Era: " + r.a);
                    wr.WriteLine("¿Enviar tu respuesta como sugerencia? (S/N)");
                    string yn = ReadOrDefault(rd, "N");
                    if (yn.Trim().ToUpperInvariant() == "S")
                    {
                        int id = AgregarSugerencia(r.q, ans, GetWho(c));
                        wr.WriteLine("Sugerencia enviada. ID " + id);
                    }
                }
            }

            wr.WriteLine("PUNTAJE FINAL: " + score);
            wr.WriteLine("BYE");
        }

        private static bool EsCorrecta(string pregunta, string respuestaBase, string respuestaUsuario,
                                       Dictionary<string, HashSet<string>> aprobadas)
        {
            string u = Norm(respuestaUsuario);
            if (u == Norm(respuestaBase)) return true;

            HashSet<string> set;
            if (aprobadas.TryGetValue(pregunta, out set))
            {
                return set.Contains(u);
            }
            return false;
        }

        // ======== MODERADOR ========
        private static void Moderador(StreamReader rd, StreamWriter wr)
        {
            wr.WriteLine("MOD OK");
            wr.WriteLine("Comandos: LIST | APPROVE <id> | REJECT <id> | EXIT");
            while (true)
            {
                wr.WriteLine("CMD>");
                string line = ReadOrDefault(rd, "EXIT").Trim();
                if (line.Length == 0) continue;

                var parts = line.Split(new char[] { ' ' }, 2);
                string cmd = parts[0].ToUpperInvariant();

                if (cmd == "EXIT") { wr.WriteLine("BYE"); break; }
                else if (cmd == "LIST") ListarPendientes(wr);
                else if (cmd == "APPROVE") Procesar(parts, wr, true);
                else if (cmd == "REJECT") Procesar(parts, wr, false);
                else wr.WriteLine("Desconocido. Usa LIST | APPROVE <id> | REJECT <id> | EXIT");
            }
        }

        private static void Procesar(string[] parts, StreamWriter wr, bool aprobar)
        {
            if (parts.Length < 2) { wr.WriteLine("Uso: " + (aprobar ? "APPROVE" : "REJECT") + " <id>"); return; }
            int id;
            if (!int.TryParse(parts[1], out id)) { wr.WriteLine("ID inválido."); return; }

            var todas = LeerSugerencias();
            bool ok = false;
            for (int i = 0; i < todas.Count; i++)
            {
                if (todas[i].id == id && todas[i].estado == "PENDING")
                {
                    if (aprobar)
                    {
                        // Guardamos como respuesta alternativa para esa pregunta
                        File.AppendAllText(EXTRA_FILE, todas[i].pregunta + "|" + todas[i].propuesta + Environment.NewLine, new UTF8Encoding(false));
                        todas[i] = (todas[i].id, "APPROVED", todas[i].pregunta, todas[i].propuesta, todas[i].quien, todas[i].fecha);
                        ok = true;
                        break;
                    }
                    else
                    {
                        todas[i] = (todas[i].id, "REJECTED", todas[i].pregunta, todas[i].propuesta, todas[i].quien, todas[i].fecha);
                        ok = true;
                        break;
                    }
                }
            }
            if (ok) EscribirSugerencias(todas);
            wr.WriteLine(ok ? (aprobar ? "Aprobada." : "Rechazada.") : "No encontrada o no pendiente.");
        }

        private static void ListarPendientes(StreamWriter wr)
        {
            var todas = LeerSugerencias();
            int count = 0;
            for (int i = 0; i < todas.Count; i++)
            {
                if (todas[i].estado == "PENDING")
                {
                    wr.WriteLine("ID " + todas[i].id + " | " + todas[i].pregunta);
                    wr.WriteLine("   Propuesta: " + todas[i].propuesta + " | Por: " + todas[i].quien + " | " + todas[i].fecha);
                    count++;
                }
            }
            if (count == 0) wr.WriteLine("(Sin pendientes)");
        }

        // ======== ARCHIVOS / UTIL ========
        private static List<(string q, string a)> CargarAdivinanzasBase()
        {
            var list = new List<(string, string)>();
            list.Add(("Blanca por dentro, verde por fuera. ¿Qué es?", "pera"));
            list.Add(("Oro parece, plata no es. ¿Qué es?", "platano"));
            list.Add(("Tiene agujas pero no cose. ¿Qué es?", "reloj"));
            return list;
        }

        // Carga respuestas alternativas aprobadas por pregunta (normalizadas)
        private static Dictionary<string, HashSet<string>> CargarRespuestasAprobadasPorPregunta()
        {
            var map = new Dictionary<string, HashSet<string>>();
            foreach (var ln in File.ReadAllLines(EXTRA_FILE, new UTF8Encoding(false)))
            {
                var s = ln.Trim();
                if (s.Length == 0) continue;
                var p = s.Split('|');
                if (p.Length == 2)
                {
                    string pregunta = p[0];
                    string respAlt = Norm(p[1]);
                    HashSet<string> set;
                    if (!map.TryGetValue(pregunta, out set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        map[pregunta] = set;
                    }
                    set.Add(respAlt);
                }
            }
            return map;
        }

        private static int AgregarSugerencia(string pregunta, string propuesta, string quien)
        {
            var todas = LeerSugerencias();
            int max = 0; for (int i = 0; i < todas.Count; i++) if (todas[i].id > max) max = todas[i].id;
            int nuevoId = max + 1;
            todas.Add((nuevoId, "PENDING", San(pregunta), San(propuesta), San(quien), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            EscribirSugerencias(todas);
            return nuevoId;
        }

        private static List<(int id, string estado, string pregunta, string propuesta, string quien, string fecha)> LeerSugerencias()
        {
            var list = new List<(int, string, string, string, string, string)>();
            foreach (var ln in File.ReadAllLines(SUG_FILE, new UTF8Encoding(false)))
            {
                var s = ln.Trim();
                if (s.Length == 0) continue;
                var p = s.Split('|');
                if (p.Length >= 6)
                {
                    int id; int.TryParse(p[0], out id);
                    list.Add((id, p[1], p[2], p[3], p[4], p[5]));
                }
            }
            return list;
        }

        private static void EscribirSugerencias(List<(int id, string estado, string pregunta, string propuesta, string quien, string fecha)> items)
        {
            using (var sw = new StreamWriter(SUG_FILE, false, new UTF8Encoding(false)))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var x = items[i];
                    sw.WriteLine(x.id + "|" + x.estado + "|" + x.pregunta + "|" + x.propuesta + "|" + x.quien + "|" + x.fecha);
                }
            }
        }

        private static string GetWho(TcpClient c)
        {
            try { return c.Client.RemoteEndPoint != null ? c.Client.RemoteEndPoint.ToString() : "desconocido"; }
            catch { return "desconocido"; }
        }

        private static string ReadOrDefault(StreamReader rd, string defVal)
        {
            try { var s = rd.ReadLine(); return s == null ? defVal : s; } catch { return defVal; }
        }

        private static string San(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ");
        }

        private static string Norm(string s)
        {
            if (s == null) return "";
            s = s.Trim().ToLowerInvariant();
            s = s.Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");
            return s;
        }
    }
}
