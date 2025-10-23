using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ServerUdp
{
  class Program
  {
    public static object IPEndpoint { get; private set; }

    static void Main(string[] args)
    {
      UdpClient servidor = new UdpClient(5000);
      Console.WriteLine("Server conectado");

      int posX = 5, posY = 5;

      IPEndPoint cliente = null;

      while (true)
      {
        byte[] datos = servidor.Receive(ref cliente);
        string comando = Encoding.UTF8.GetString(datos);

        if (comando == "UP") posY--;
        if (comando == "DOWN") posY++;
        if (comando == "LEFT") posX--;
        if (comando == "RIGHT") posX++;

        Console.WriteLine("@");
        Console.SetCursorPosition(posX, posY);
        string posiciones = posX + " " + posY;
        byte[] respuesta = Encoding.UTF8.GetBytes(posiciones);
        servidor.Send(respuesta, respuesta.Length, cliente);
      }

    }
  }
}