using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ClienteCalculos
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string servidor = "127.0.0.1";
            int puerto = 9000;

            try
            {
                TcpClient cliente = new TcpClient(servidor, puerto);
                Console.WriteLine($"Conectado a {servidor}:{puerto}");

                NetworkStream stream = cliente.GetStream();
                StreamReader lector = new StreamReader(stream, Encoding.UTF8);
                StreamWriter escritor = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                Console.WriteLine(lector.ReadLine());
                Console.WriteLine(lector.ReadLine());
                Console.WriteLine("Escribí HELP para ver comandos.");

                while (true)
                {
                    Console.Write("> ");
                    string mensaje = Console.ReadLine();
                    if (mensaje == null) break;

                    if (mensaje.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                    {
                        escritor.WriteLine("EXIT");
                        break;
                    }

                    escritor.WriteLine(mensaje);

                    // Si es HIST, leemos hasta encontrar "END"
                    if (mensaje.StartsWith("HIST", StringComparison.OrdinalIgnoreCase))
                    {
                        string linea = lector.ReadLine();
                        while (linea != null)
                        {
                            Console.WriteLine(linea);
                            if (linea == "END") break;
                            linea = lector.ReadLine();
                        }
                    }
                    else
                    {
                        // Respuesta de una sola línea
                        string respuesta = lector.ReadLine();
                        if (respuesta == null) break;
                        Console.WriteLine(respuesta);
                    }
                }

                cliente.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
