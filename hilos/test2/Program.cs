using System;
using System.Threading;

namespace test2
{
    class Program
    {
        // Contadores separados para comparar
        static int contadorInseguro = 0;
        static int contadorSeguro = 0;

        // Candado para el contadorSeguro
        static readonly object _lock = new object();

        // Cantidad de incrementos por hilo (ajustá si querés)
        const int IteracionesPorHilo = 5000;

        static void Main()
        {
            // 2 hilos SIN lock (condición de carrera)
            Thread t1 = new Thread(IncrementarSinLock);
            Thread t2 = new Thread(IncrementarSinLock);

            // 2 hilos CON lock (sin condición de carrera)
            Thread t3 = new Thread(IncrementarConLock);
            Thread t4 = new Thread(IncrementarConLock);

            // Arrancan todos
            t1.Start();
            t2.Start();

            t3.Start();
            t4.Start();

            // Esperamos a que terminen
            t1.Join();
            t2.Join();

            t3.Join();
            t4.Join();

            int esperadoPorGrupo = 2 * IteracionesPorHilo;

            Console.WriteLine($"[SIN lock]  Valor final: {contadorInseguro:N0} (esperado teórico: {esperadoPorGrupo:N0})");
            Console.WriteLine($"[CON lock]  Valor final: {contadorSeguro:N0}   (esperado exacto:  {esperadoPorGrupo:N0})");
        }

        static void IncrementarSinLock()
        {
            for (int i = 0; i < IteracionesPorHilo; i++)
            {
                // NO atómico → puede perder incrementos
                contadorInseguro++;
            }
        }

        static void IncrementarConLock()
        {
            for (int i = 0; i < IteracionesPorHilo; i++)
            {
                // Sección crítica protegida → un hilo a la vez
                lock (_lock)
                {
                    contadorSeguro++;
                }
            }
        }
    }
}
