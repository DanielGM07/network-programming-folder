using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ClienteUdp
{
  class Program
  {
    static void Main(string[] args)
    {
      int puertoLocal = new Random().Next(6000, 7000);
      UdpClient cliente = new UdpClient(puertoLocal);

      IPEndPoint servidor = new IPEndPoint(IPAddress.Loopback, 5000);
      IPEndPoint remoto = new IPEndPoint(IPAddress.Any, 0);

      Console.CursorVisible = false;
      Console.WriteLine("Cliente conectado en puerto " + puertoLocal + ". Use W A S D para moverse.");

      Thread receptor = new Thread(() =>
      {
        while (true)
        {
          byte[] data = cliente.Receive(ref remoto);
          string texto = Encoding.UTF8.GetString(data);

          Console.Clear();

          string[] jugadores = texto.Split('|');
          foreach (string j in jugadores)
          {
            if (j.Length == 0) continue;
            string[] pos = j.Split(',');
            int x = int.Parse(pos[0]);
            int y = int.Parse(pos[1]);

            Console.SetCursorPosition(x, y);
            Console.Write("@");
          }
        }
      });
      receptor.Start();

      while (true)
      {
        ConsoleKey key = Console.ReadKey(true).Key;
        string comando = "";

        if (key == ConsoleKey.W) comando = "UP";
        if (key == ConsoleKey.S) comando = "DOWN";
        if (key == ConsoleKey.A) comando = "LEFT";
        if (key == ConsoleKey.D) comando = "RIGHT";

        if (comando != "")
        {
          byte[] data = Encoding.UTF8.GetBytes(comando);
          cliente.Send(data, data.Length, servidor);
        }
      }
    }
  }
}
