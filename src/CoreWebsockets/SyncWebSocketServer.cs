using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public class SyncWebSocketServer : WebSocketServer
    {
        public SyncWebSocketServer(int port)
            : base(port)
        {
        }

        private int _nextClientId = 0;

        public override void Run()
        {
            Listener = new TcpListener(System.Net.IPAddress.Any, Port);
            Listener.Server.NoDelay = true;
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

                        var wsClient = new WebSocketClient()
                        {
                            Id = _nextClientId++,
                            TcpClient = client
                        };

                        Clients.Add(wsClient);
                    }

                    foreach (var item in Clients.Where(a => !a.UpgradedConnection))
                        ProcessConnection(item);

                    foreach (var item in Clients)
                        if (!item.TcpClient.Connected)
                            item.TcpClient.Dispose();

                    Clients = Clients.Where(a => a.TcpClient.Connected).ToList();

                    foreach (var item in Clients.Where(a => a.UpgradedConnection))
                        ProcessMessage(item);

                    Thread.Sleep(10);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Server error: " + e.Message);
                }
            }

            Listening = false;
        }

        private void ProcessConnection(WebSocketClient client)
        {
            try
            {
                client.UpgradedConnection = ProcessWebsocketUpgrade(client);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

                client?.Dispose();

                Clients.Remove(client);

                return;
            }

            if (client.UpgradedConnection)
            {
                Task
                    .Delay(ClientUpgradeTimeout)
                    .ContinueWith(t => OnClientConnected(client));
            }
        }

        protected override bool ProcessWebsocketUpgrade(WebSocketClient client)
        {
            var message = client.GetHttpRequest();

            if (message?.StartsWith("GET") ?? false)
            {
                if (!GetAuthentication(message))
                    return false;

                if (!new Regex("Connection:(.*)").Match(message).Groups[1].Value.Trim().Contains("Upgrade"))
                    return false;

                var response = CreateWebsocketUpgradeReponse(message);

                client.TcpClient.Client.Send(response);

                return true;
            }

            return false;
        }
    }
}
