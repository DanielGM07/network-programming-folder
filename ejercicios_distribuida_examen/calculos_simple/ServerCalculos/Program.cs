using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerCalculos
{
    internal class Program
    {
        static void Main(string[] args)
        {
            const int puerto = 9000;
            var servidor = new TcpListener(IPAddress.Any, puerto);
            servidor.Start();
            Console.WriteLine($"Servidor escuchando en el puerto {puerto}...");

            while (true)
            {
                // Acepta SIEMPRE y crea un hilo por cliente
                TcpClient cli = servidor.AcceptTcpClient();
                new Thread(AtenderCliente) { IsBackground = true }.Start(cli);
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
                    escritor.WriteLine("Bienvenido al Servidor de Calculos!");
                    escritor.WriteLine("Usa: ADD a b | SUB a b | MUL a b | DIV a b | EXIT");

                    while (true)
                    {
                        string linea = lector.ReadLine();
                        if (linea == null) break;

                        string msg = linea.Trim();
                        if (msg.Length == 0) { escritor.WriteLine("Formato incorrecto. Ej: ADD 2 3"); continue; }
                        if (msg.Equals("EXIT", StringComparison.OrdinalIgnoreCase)) break;

                        string[] p = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length != 3)
                        {
                            escritor.WriteLine("Formato incorrecto. Ej: ADD 2 3");
                            continue;
                        }

                        string cmd = p[0].ToUpperInvariant();
                        if (!double.TryParse(p[1], out double a) || !double.TryParse(p[2], out double b))
                        {
                            escritor.WriteLine("Error: los operandos deben ser números.");
                            continue;
                        }

                        switch (cmd)
                        {
                            case "ADD": escritor.WriteLine("Resultado: " + (a + b)); break;
                            case "SUB": escritor.WriteLine("Resultado: " + (a - b)); break;
                            case "MUL": escritor.WriteLine("Resultado: " + (a * b)); break;
                            case "DIV":
                                if (b == 0) escritor.WriteLine("Error: división por cero.");
                                else escritor.WriteLine("Resultado: " + (a / b));
                                break;
                            default: escritor.WriteLine("Comando desconocido."); break;
                        }
                    }
                }
            }
            catch (IOException) { /* cliente cerró */ }
            catch (Exception ex) { Console.WriteLine("Error con cliente: " + ex.Message); }
            finally
            {
                Console.WriteLine($"Cliente desconectado: {cliente.Client.RemoteEndPoint}");
            }
        }
    }
}
