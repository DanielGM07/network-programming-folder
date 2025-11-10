using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Linq;

namespace ServerCalculos
{
    internal class Program
    {
        static readonly object _fileLock = new object();
        const string HistoryFile = "historial_calculos.csv";

        static void Main(string[] args)
        {
            const int puerto = 9000;
            PrepararHistorial();

            var servidor = new TcpListener(IPAddress.Any, puerto);
            servidor.Start();
            Console.WriteLine($"Servidor escuchando en el puerto {puerto}...");

            while (true)
            {
                TcpClient cli = servidor.AcceptTcpClient();
                new Thread(AtenderCliente) { IsBackground = true }.Start(cli);
            }
        }

        static void PrepararHistorial()
        {
            if (!File.Exists(HistoryFile))
            {
                lock (_fileLock)
                {
                    File.WriteAllText(HistoryFile, "FechaHora,Operacion,Entrada,Resultado\n", Encoding.UTF8);
                }
            }
        }

        static void LogOperacion(string operacion, string entrada, string resultado)
        {
            string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string linea = $"{fecha},{operacion},{entrada},{resultado}\n";
            lock (_fileLock)
            {
                File.AppendAllText(HistoryFile, linea, Encoding.UTF8);
            }
        }

        static void AtenderCliente(object obj)
        {
            var cliente = (TcpClient)obj;
            Console.WriteLine($"Cliente conectado: {cliente.Client.RemoteEndPoint}");
            try
            {
                using (cliente)
                using (var stream = cliente.GetStream())
                using (var lector = new StreamReader(stream, Encoding.UTF8))
                using (var escritor = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    escritor.WriteLine("Servidor de Calculos listo.");
                    escritor.WriteLine("Comandos: ADD/SUB/MUL/DIV/POW, SQRT, PROM, MOD, ABS, MAX, MIN, HIST [n], EXIT");

                    while (true)
                    {
                        string linea = lector.ReadLine();
                        if (linea == null) break;

                        string msg = linea.Trim();
                        if (msg.Length == 0) { escritor.WriteLine("Formato incorrecto."); continue; }
                        if (msg.Equals("EXIT", StringComparison.OrdinalIgnoreCase)) break;

                        if (msg.StartsWith("HIST", StringComparison.OrdinalIgnoreCase))
                        {
                            EnviarHistorial(escritor, msg);
                            continue;
                        }

                        string respuesta = ProcesarOperacion(msg);
                        escritor.WriteLine(respuesta);
                    }
                }
            }
            catch (IOException) { }
            catch (Exception ex) { Console.WriteLine("Error con cliente: " + ex.Message); }
            finally
            {
                Console.WriteLine($"Cliente desconectado: {cliente.Client.RemoteEndPoint}");
            }
        }

        static void EnviarHistorial(StreamWriter escritor, string msg)
        {
            int n = 10;
            var partesH = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partesH.Length == 2) int.TryParse(partesH[1], out n);
            if (n <= 0) n = 10;

            string[] lines;
            lock (_fileLock)
            {
                lines = File.ReadAllLines(HistoryFile, Encoding.UTF8);
            }

            var ultimas = lines.Skip(1).Reverse().Take(n).Reverse().ToArray();

            escritor.WriteLine("Historial:");
            if (ultimas.Length == 0)
            {
                escritor.WriteLine("(vacío)");
            }
            else
            {
                foreach (var l in ultimas)
                {
                    if (!string.IsNullOrWhiteSpace(l))
                        escritor.WriteLine(l);
                }
            }
            escritor.WriteLine("END"); // terminador claro
        }

        static string ProcesarOperacion(string msg)
        {
            var inv = CultureInfo.InvariantCulture;
            string[] p = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string cmd = p[0].ToUpperInvariant();

            try
            {
                switch (cmd)
                {
                    case "ADD":
                    case "SUB":
                    case "MUL":
                    case "DIV":
                    case "POW":
                    case "MOD":
                    case "MAX":
                    case "MIN":
                    {
                        if (p.Length != 3) return "Formato: " + cmd + " a b";
                        if (!double.TryParse(p[1], NumberStyles.Any, inv, out double a) ||
                            !double.TryParse(p[2], NumberStyles.Any, inv, out double b))
                            return "Error: los operandos deben ser números.";

                        double r;
                        switch (cmd)
                        {
                            case "ADD": r = a + b; break;
                            case "SUB": r = a - b; break;
                            case "MUL": r = a * b; break;
                            case "DIV": if (b == 0) return "Error: división por cero."; r = a / b; break;
                            case "POW": r = Math.Pow(a, b); break;
                            case "MOD": if (b == 0) return "Error: módulo por cero."; r = a % b; break;
                            case "MAX": r = Math.Max(a, b); break;
                            case "MIN": r = Math.Min(a, b); break;
                            default: return "Comando desconocido.";
                        }

                        string res = r.ToString("G17", inv);
                        LogOperacion(cmd, p[1] + " " + p[2], res);
                        return "Resultado: " + res;
                    }

                    case "SQRT":
                    case "ABS":
                    {
                        if (p.Length != 2) return "Formato: " + cmd + " x";
                        if (!double.TryParse(p[1], NumberStyles.Any, inv, out double x))
                            return "Error: el operando debe ser un número.";

                        double r = (cmd == "SQRT")
                            ? (x < 0 ? double.NaN : Math.Sqrt(x))
                            : Math.Abs(x);

                        if (cmd == "SQRT" && double.IsNaN(r))
                            return "Error: raíz de número negativo.";

                        string res = r.ToString("G17", inv);
                        LogOperacion(cmd, p[1], res);
                        return "Resultado: " + res;
                    }

                    case "PROM":
                    {
                        if (p.Length < 2) return "Formato: PROM n1 n2 n3 ...";
                        double suma = 0; int c = 0;
                        for (int i = 1; i < p.Length; i++)
                        {
                            if (!double.TryParse(p[i], NumberStyles.Any, inv, out double v))
                                return "Error: todos los valores deben ser números.";
                            suma += v; c++;
                        }
                        if (c == 0) return "Error: sin valores.";
                        double r = suma / c;
                        string res = r.ToString("G17", inv);
                        LogOperacion("PROM", string.Join(' ', p.Skip(1)), res);
                        return "Resultado: " + res;
                    }

                    case "HELP":
                        return "Comandos: ADD/SUB/MUL/DIV/POW, SQRT, PROM, MOD, ABS, MAX, MIN, HIST [n], EXIT";

                    default:
                        return "Comando desconocido. Escribí HELP para ver opciones.";
                }
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
