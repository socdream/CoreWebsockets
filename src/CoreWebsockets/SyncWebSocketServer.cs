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

                        Clients.Add(new WebSocketClient(client)
                        {
                            Id = _nextClientId++,
                            ServerClient = true
                        });
                    }

                    foreach (var item in Clients.Where(a => !a.UpgradedConnection))
                        ProcessConnection(item);

                    foreach (var item in Clients)
                        if (!item.Connected)
                            item.Dispose();

                    Clients = Clients.Where(a => a.Connected).ToList();

                    foreach (var item in Clients.Where(a => a.UpgradedConnection).ToList())
                    {
                        var messages = item.GetMessages().GetAwaiter().GetResult();

                        ProcessMessage(item, messages);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Server error: " + e.Message);
                }

                Thread.Sleep(5);
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

        private bool ProcessWebsocketUpgrade(WebSocketClient client)
        {
            var message = client.GetHttpRequest().GetAwaiter().GetResult();

            if (message?.StartsWith("GET") ?? false)
            {
                if (!GetAuthentication(message))
                    return false;

                var parsedConnection = new Regex("Connection:(.*)", RegexOptions.IgnoreCase).Match(message).Groups[1].Value.Trim();
                if (!parsedConnection.Contains("Upgrade") && !parsedConnection.Contains("upgrade"))
                    return false;

                var response = CreateWebsocketUpgradeReponse(message);

                client.SendRawData(response).GetAwaiter().GetResult();

                return true;
            }

            return false;
        }
    }
}
