using System;
using System.Threading;

namespace test1
{
    class Program
    {
        // Esta es nuestra "llave del baño" (candado)
        private static readonly object _lock = new object();

        static void Main()
        {
            // Creamos 5 hilos, cada uno con un nombre distinto
            for (int i = 1; i <= 5; i++)
            {
                int id = i; // necesaria copia local para que el closure funcione correctamente
                Thread t = new Thread(() => EntrarAlBaño(id));
                t.Start();
            }
        }

        static void EntrarAlBaño(int id)
        {
            Console.WriteLine($"🧍‍♂️ Hilo {id} quiere entrar al baño...");

            lock (_lock)
            {
                Console.WriteLine($"🚪 Hilo {id} entró al baño.");
                Thread.Sleep(1000); // simula que el hilo está usando el recurso
                Console.WriteLine($"🚶‍♂️ Hilo {id} salió del baño.");
            }
        }
    }
}
