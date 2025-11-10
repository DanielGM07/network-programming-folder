using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerCalculos
{
    internal static class Program
    {
        private static volatile bool _running = true;
        private const int Port = 9000;

        private static void Main(string[] args)
        {
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _running = false;
                Console.WriteLine("Deteniendo servidor...");
            };

            var listener = new TcpListener(IPAddress.Any, Port);
            try
            {
                listener.Start();
                Console.WriteLine($"Servidor de Cálculos TCP escuchando en puerto {Port}...");

                while (_running)
                {
                    if (!listener.Pending())
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    var client = listener.AcceptTcpClient();
                    client.NoDelay = true;
                    Console.WriteLine($"Cliente conectado: {client.Client.RemoteEndPoint}");

                    var th = new Thread(HandleClient) { IsBackground = true };
                    th.Start(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error servidor: {ex.Message}");
            }
            finally
            {
                try { listener.Stop(); } catch { }
                Console.WriteLine("Servidor detenido.");
            }
        }

        private static void HandleClient(object obj)
        {
            var client = (TcpClient)obj;
            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                using (var reader = new StreamReader(ns, Encoding.UTF8, false, 8192, true))
                using (var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    // **Una sola línea de banner**
                    writer.WriteLine("OK ServidorCalculos v1 | Comandos: ADD a b | SUB a b | MUL a b | DIV a b | POW a b | EVAL expr | PING | HELP | EXIT");

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string request = line.Trim();
                        if (request.Length == 0) { writer.WriteLine("ERR Empty"); continue; }

                        string response;
                        try
                        {
                            if (CommandParser.IsExit(request))
                            {
                                writer.WriteLine("BYE");
                                break;
                            }
                            response = CommandParser.Process(request);
                        }
                        catch (Exception ex)
                        {
                            response = "ERR " + ex.Message;
                        }

                        // **Una sola línea por respuesta**
                        writer.WriteLine(response);
                    }
                }
            }
            catch (IOException) { /* cliente cerró */ }
            catch (Exception ex)
            {
                Console.WriteLine($"Error con cliente {client.Client.RemoteEndPoint}: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"Cliente desconectado: {client.Client.RemoteEndPoint}");
            }
        }
    }

    internal static class CommandParser
    {
        public static bool IsExit(string s)
        {
            var up = s.Trim().ToUpperInvariant();
            return up == "EXIT" || up == "QUIT";
        }

        public static string Process(string input)
        {
            string trimmed = input.Trim();
            if (trimmed.Length == 0) return "ERR Empty";

            string up = trimmed.ToUpperInvariant();

            if (up == "PING") return "PONG";
            if (up == "HELP")
                return "OK Comandos: ADD a b | SUB a b | MUL a b | DIV a b | POW a b | EVAL expr | PING | HELP | EXIT";

            if (up.StartsWith("EVAL "))
            {
                string expr = trimmed.Substring(5);
                double val = ExpressionEvaluator.Evaluate(expr);
                return "OK " + val.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
            }

            // formato: CMD a b
            string[] parts = trimmed.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries); // <= límite 3
            if (parts.Length == 0) return "ERR Empty";

            string cmd = parts[0].ToUpperInvariant();

            if (cmd == "ADD" || cmd == "SUB" || cmd == "MUL" || cmd == "DIV" || cmd == "POW")
            {
                if (parts.Length != 3) return "ERR Formato: " + cmd + " a b";

                if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double a))
                    return "ERR a inválido";
                if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double b))
                    return "ERR b inválido";

                double res;
                switch (cmd)
                {
                    case "ADD": res = a + b; break;
                    case "SUB": res = a - b; break;
                    case "MUL": res = a * b; break;
                    case "DIV":
                        if (b == 0) return "ERR División por cero";
                        res = a / b; break;
                    case "POW": res = Math.Pow(a, b); break;
                    default: return "ERR Comando no soportado";
                }

                return "OK " + res.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
            }

            return "ERR Comando no reconocido";
        }
    }

    // Evaluador igual que antes (sin cambios)
    internal static class ExpressionEvaluator
    {
        public static double Evaluate(string expr)
        {
            var tokens = Tokenize(expr);
            var rpn = ToRpn(tokens);
            return EvalRpn(rpn);
        }

        private static string[] Tokenize(string expr)
        {
            var list = new System.Collections.Generic.List<string>();
            int i = 0;
            string prev = null;

            while (i < expr.Length)
            {
                char c = expr[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    i++;
                    while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                    list.Add(expr.Substring(start, i - start));
                    prev = "NUM";
                    continue;
                }

                if (c == '(' || c == ')')
                {
                    list.Add(c.ToString());
                    i++;
                    prev = c.ToString();
                    continue;
                }

                if ("+-*/^".IndexOf(c) >= 0)
                {
                    if (c == '-' && (prev == null || prev == "(" || IsOperator(prev)))
                    {
                        list.Add("0");
                        list.Add("-");
                        i++;
                        prev = "-";
                        continue;
                    }

                    list.Add(c.ToString());
                    i++;
                    prev = c.ToString();
                    continue;
                }

                throw new Exception("Carácter inválido: " + c);
            }

            return list.ToArray();
        }

        private static bool IsOperator(string t) => t == "+" || t == "-" || t == "*" || t == "/" || t == "^";
        private static int Prec(string op) => op == "^" ? 4 : (op == "*" || op == "/") ? 3 : (op == "+" || op == "-") ? 2 : 0;
        private static bool RightAssoc(string op) => op == "^";

        private static System.Collections.Generic.List<string> ToRpn(string[] tokens)
        {
            var output = new System.Collections.Generic.List<string>();
            var stack = new System.Collections.Generic.Stack<string>();

            foreach (var t in tokens)
            {
                if (double.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double _))
                {
                    output.Add(t);
                }
                else if (IsOperator(t))
                {
                    while (stack.Count > 0 && IsOperator(stack.Peek()))
                    {
                        var top = stack.Peek();
                        if ((RightAssoc(t) && Prec(t) < Prec(top)) || (!RightAssoc(t) && Prec(t) <= Prec(top)))
                            output.Add(stack.Pop());
                        else break;
                    }
                    stack.Push(t);
                }
                else if (t == "(") stack.Push(t);
                else if (t == ")")
                {
                    while (stack.Count > 0 && stack.Peek() != "(") output.Add(stack.Pop());
                    if (stack.Count == 0) throw new Exception("Paréntesis desbalanceados");
                    stack.Pop();
                }
                else throw new Exception("Token inválido: " + t);
            }

            while (stack.Count > 0)
            {
                var x = stack.Pop();
                if (x == "(" || x == ")") throw new Exception("Paréntesis desbalanceados");
                output.Add(x);
            }

            return output;
        }

        private static double EvalRpn(System.Collections.Generic.List<string> rpn)
        {
            var st = new System.Collections.Generic.Stack<double>();
            foreach (var t in rpn)
            {
                if (double.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num))
                {
                    st.Push(num);
                }
                else
                {
                    if (st.Count < 2) throw new Exception("Expresión inválida");
                    double b = st.Pop();
                    double a = st.Pop();
                    double r;
                    switch (t)
                    {
                        case "+": r = a + b; break;
                        case "-": r = a - b; break;
                        case "*": r = a * b; break;
                        case "/": if (b == 0) throw new Exception("División por cero"); r = a / b; break;
                        case "^": r = Math.Pow(a, b); break;
                        default: throw new Exception("Operador inválido: " + t);
                    }
                    st.Push(r);
                }
            }
            if (st.Count != 1) throw new Exception("Expresión inválida");
            return st.Pop();
        }
    }
}
