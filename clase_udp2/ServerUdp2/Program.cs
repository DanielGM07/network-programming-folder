using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ServerUdp2
{
  class Program
  {
    static void Main(string[] args)
    {
      UdpClient servidor = new UdpClient(5000);
      Console.WriteLine("Servidor iniciado");

      var jugadores = new Dictionary<IPEndPoint, (int, int)>();
      Random rand = new Random();

      while (true)
      {
        IPEndPoint cliente = new IPEndPoint(IPAddress.Any, 0);
        byte[] data = servidor.Receive(ref cliente);
        string comando = Encoding.UTF8.GetString(data);

        if (!jugadores.ContainsKey(cliente))
        {
          jugadores[cliente] = (rand.Next(0, 40), rand.Next(0, 15));
          Console.WriteLine($"Nuevo jugador conectado: {cliente}");
        }

        var pos = jugadores[cliente]; // pos.Item1 = x, pos.Item2 = y

        if (comando == "UP") pos.Item2--;
        if (comando == "DOWN") pos.Item2++;
        if (comando == "LEFT") pos.Item1--;
        if (comando == "RIGHT") pos.Item1++;

        jugadores[cliente] = pos;

        StringBuilder sb = new StringBuilder();
        foreach (var jugador in jugadores)
        {
          sb.Append($"{jugador.Value.Item1},{jugador.Value.Item2}|");
        }

        byte[] mensaje = Encoding.UTF8.GetBytes(sb.ToString());
        foreach (var kvp in jugadores.Keys)
        {
          servidor.Send(mensaje, mensaje.Length, kvp);
        }
      }
    }
  }
}
