using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tap2iDSampleWinUI.BarcodeServer
{
    internal class BarcodeServer
    {
        public static async Task StartServerAsync(Action<string> onDataReceived)
        {
            TcpListener server = new TcpListener(IPAddress.Any, 4011);
            server.Start();

            while (true)
            {
                Console.WriteLine("Waiting for a connection");
                TcpClient socket = await server.AcceptTcpClientAsync();
                Console.WriteLine("Client connected");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        NetworkStream stream = socket.GetStream();
                        byte[] buffer = new byte[1024];
                        int bytesRead;
                        StringBuilder message = new StringBuilder();

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            message.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                        }

                        Console.WriteLine(message.ToString());

                        // Invoke the onDataReceived callback with the message
                        onDataReceived?.Invoke(message.ToString());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling client: {ex.Message}");
                    }
                    finally
                    {
                        socket.Close();
                    }
                });

                // Optional: Introduce a small delay to avoid overwhelming the server
                // await Task.Delay(10);
            }
        }
    }
}
