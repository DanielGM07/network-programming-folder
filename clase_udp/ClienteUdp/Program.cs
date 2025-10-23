using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ClienteUdp
{
  class Program
  {
    static void Main(string[] args)
    {
      UdpClient cliente = new UdpClient();
      IPEndPoint servidor = new IPEndPoint(IPAddress.Loopback, 5000);

      Console.WriteLine("Cliente conectado. W A S D para moverse");

      while (true)
      {
        ConsoleKey key = Console.ReadKey(true).Key;

        string comando = "";

        if (key == ConsoleKey.W) comando = "UP";
        if (key == ConsoleKey.S) comando = "DOWN";
        if (key == ConsoleKey.A) comando = "LEFT";
        if (key == ConsoleKey.D) comando = "RIGHT";

        byte[] data = Encoding.UTF8.GetBytes(comando);
        cliente.Send(data, data.Length, servidor);

        IPEndPoint remote = null;
        byte[] respuesta = cliente.Receive(ref remote);

        string respuestaString = Encoding.UTF8.GetString(respuesta);

        string[] posiciones = respuestaString.Split(' ');

        int posX = int.Parse(posiciones[0]);
        int posY = int.Parse(posiciones[1]);

        Console.SetCursorPosition(posX, posY);
        Console.WriteLine("@");
      }
    }
  }
}