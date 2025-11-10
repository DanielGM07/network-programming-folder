using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ServerChat
{
    
    class ServidorChat
    {
        private readonly TcpListener escucha;
        private readonly ConcurrentDictionary<int, TcpClient> clientes = new ConcurrentDictionary<int, TcpClient>();
        private int siguienteId = 0;

        public ServidorChat(int puerto)
        {
            escucha = new TcpListener(IPAddress.Any, puerto);
        }

        public async Task IniciarAsync()
        {
            escucha.Start();
            Console.WriteLine("Servidor escuchando...");
            while (true)
            {
                var cliente = await escucha.AcceptTcpClientAsync();
                int id = +1;
                clientes[id] = cliente;
                _ = ManejarClienteAsync(id, cliente);
            }
        }

        private async Task ManejarClienteAsync(int id, TcpClient cliente)
        {
            var flujo = cliente.GetStream();
            var buffer = new byte[1024];
            try
            {
                while (true)
                {
                    int leidos = await flujo.ReadAsync(buffer, 0, buffer.Length);
                    if (leidos == 0) break;
                    string mensaje = Encoding.UTF8.GetString(buffer, 0, leidos);
                    Console.WriteLine($"[{id}] {mensaje}");
                    await BroadcastAsync($"[{id}] {mensaje}", id);
                }
            }
            finally
            {
                clientes.TryRemove(id, out _);
                cliente.Close();
                Console.WriteLine($"Cliente {id} desconectado.");
            }
        }

        private async Task BroadcastAsync(string mensaje, int excluyeId = -1)
        {
            var datos = Encoding.UTF8.GetBytes(mensaje);
            foreach (var kv in clientes)
            {
                if (kv.Key == excluyeId) continue;
                try
                {
                    var flujo = kv.Value.GetStream();
                    await flujo.WriteAsync(datos, 0, datos.Length);
                }
                catch { }
            }
        }

        public static async Task Main()
        {
            var servidor = new ServidorChat(5000);
            await servidor.IniciarAsync();
        }
    }
}
