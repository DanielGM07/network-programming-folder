using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ClienteChat
{
    
    class ClienteChat
    {
        public static async Task Main()
        {
            string ip = "127.0.0.1";
            int puerto = 5000;
            using var cliente = new TcpClient();
            await cliente.ConnectAsync(ip, puerto);
            Console.WriteLine("Conectado al servidor. Escribí mensajes ('salir' para terminar).");

            var flujo = cliente.GetStream();

            _ = Task.Run(async () =>
            {
                var buf = new byte[1024];
                while (true)
                {
                    int leidos = await flujo.ReadAsync(buf, 0, buf.Length);
                    if (leidos == 0) break;
                    Console.WriteLine(Encoding.UTF8.GetString(buf, 0, leidos));
                }
            });

            while (true)
            {
                string linea = Console.ReadLine();
                if (linea == "salir") break;
                var bytes = Encoding.UTF8.GetBytes(linea);
                await flujo.WriteAsync(bytes, 0, bytes.Length);
            }
        }
    }
}
