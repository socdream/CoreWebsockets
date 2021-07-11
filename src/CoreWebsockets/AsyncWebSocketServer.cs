using NLog;
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
        public static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public AsyncWebSocketServer(int port)
            : base(port)
        {
        }

        private int _nextClientId = 0;

        public override async void Run()
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
                        var client = await Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        client.NoDelay = true;
                        client.Client.NoDelay = true;

                        Process(client);
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.Error(e);
                }
            }

            Listening = false;
        }

        private async void Process(System.Net.Sockets.TcpClient client)
        {
            var clientId = _nextClientId++;

            var wsClient = new WebSocketClient(client)
            {
                Id = clientId,
                ServerClient = true
            };

            Clients.Add(wsClient);

            OnClientConnected(wsClient);

            try
            {
                //check if it's a websocket connection
                while (client.Connected)
                {
                    if (await ProcessWebsocketUpgrade(wsClient).ConfigureAwait(false))
                    {
                        wsClient.UpgradedConnection = true;
                        break;
                    }
                    else
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                }

                while (client.IsConnected())
                {
                    var messages = await wsClient.GetMessages().ConfigureAwait(false);

                    ProcessMessage(wsClient, messages);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            client?.Dispose();

            Clients.Remove(wsClient);
        }

        private async Task<bool> ProcessWebsocketUpgrade(WebSocketClient client)
        {
            var message = await client.GetHttpRequest().ConfigureAwait(false);

            if (message?.StartsWith("GET") ?? false)
            {
                if (!GetAuthentication(message))
                    return false;

                if (!new Regex("Connection:(.*)").Match(message).Groups[1].Value.Trim().StartsWith("Upgrade"))
                    return false;

                var response = CreateWebsocketUpgradeReponse(message);
                await client.SendRawData(response).ConfigureAwait(false);

                return true;
            }

            return false;
        }
    }
}
