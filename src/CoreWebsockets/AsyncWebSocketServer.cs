using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public class AsyncWebSocketServer : WebSocketServer
    {
        public AsyncWebSocketServer(int port)
            : base(port)
        {
        }

        private int _nextClientId = 0;

        public override void Run()
        {
            Listener = new TcpListener(System.Net.IPAddress.Any, Port);
            Listener.Start();

            Listening = true;

            while (!Disposed)
            {
                try
                {
                    if (Listener.Pending())
                    {
                        var client = Listener.AcceptTcpClient();
                        client.NoDelay = true;
                        client.Client.NoDelay = true;

                        Task.Run(() => Process(client));
                    }

                    Thread.Sleep(100);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Server error: " + e.Message);
                }
            }

            Listening = false;
        }

        private void Process(System.Net.Sockets.TcpClient client)
        {
            var clientId = _nextClientId++;
            string clientEndPoint = client.Client.RemoteEndPoint.ToString();

            var wsClient = new WebSocketClient()
            {
                Id = clientId,
                TcpClient = client
            };

            Clients.Add(wsClient);

            OnClientConnected(wsClient);

            try
            {
                //check if it's a websocket connection
                while (client.Connected)
                {
                    if (ProcessWebsocketUpgrade(wsClient))
                    {
                        wsClient.UpgradedConnection = true;
                        break;
                    }
                    else
                    {
                        Thread.Sleep(50);
                    }
                }

                while (client.Connected)
                    ProcessMessage(wsClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (client != null)
                client.Dispose();

            Clients.Remove(wsClient);
        }

        protected override bool ProcessWebsocketUpgrade(WebSocketClient client)
        {
            var message = client.GetHttpRequest();

            if (message?.StartsWith("GET") ?? false)
            {
                if (!GetAuthentication(message))
                    return false;

                if (new Regex("Connection:(.*)").Match(message).Groups[1].Value.Trim() != "Upgrade")
                    return false;

                var response = CreateWebsocketUpgradeReponse(message);

                client.TcpClient.GetStream().Write(response, 0, response.Length);

                return true;
            }

            return false;
        }
    }
}
