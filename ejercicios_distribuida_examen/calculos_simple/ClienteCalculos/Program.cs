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

                // Leer mensajes iniciales
                Console.WriteLine(lector.ReadLine());
                Console.WriteLine(lector.ReadLine());

                while (true)
                {
                    Console.Write("> ");
                    string mensaje = Console.ReadLine();
                    if (mensaje == null || mensaje.ToUpper() == "EXIT")
                    {
                        escritor.WriteLine("EXIT");
                        break;
                    }

                    escritor.WriteLine(mensaje);
                    string respuesta = lector.ReadLine();
                    Console.WriteLine(respuesta);
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
