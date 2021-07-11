using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public abstract class WebSocketServer : IDisposable
    {
        public class MessageReceivedArgs
        {
            public WebSocketClient Client { get; set; }
            public string Message { get; set; }
        }

        public List<WebSocketClient> Clients { get; set; } = new List<WebSocketClient>();

        public event EventHandler<WebSocketClient> ClientConnected;
        public event EventHandler<MessageReceivedArgs> MessageReceived;

        protected void OnClientConnected(WebSocketClient client)
        {
            Task.Run(() => ClientConnected?.Invoke(this, client));
        }

        protected void OnMessageReceived(MessageReceivedArgs message)
        {
            Task.Run(() => MessageReceived?.Invoke(this, message));
        }

        public TcpListener Listener { get; set; }
        public int Port { get; set; }

        public bool Listening { get; protected set; }

        public WebSocketServer(int port)
        {
            Port = port;
        }

        public abstract void Run();

        public void Send(TcpClient client, string message)
        {
            Send(client, new WebSocketFrame()
            {
                Code = WebSocketFrame.OpCode.TextFrame,
                Data = Encoding.UTF8.GetBytes(message)
            });
        }

        public void Send(TcpClient client, WebSocketFrame message)
        {
            var buffer = WebSocketFrame.EncodeFrame(message, false);

            try
            {
                if (client.Connected)
                    client.Client.Send(buffer);
            }
            catch (Exception)
            {
            }
        }

        public void Send(WebSocketFrame message)
        {
            foreach (var client in Clients.ToList())
                if ((client?.UpgradedConnection ?? false) && (client?.Connected ?? false))
                    client.Send(message);
        }

        public void Send(string message)
        {
            Send(new WebSocketFrame()
            {
                Code = WebSocketFrame.OpCode.TextFrame,
                Data = Encoding.UTF8.GetBytes(message)
            });
        }

        public void Send<T>(T data)
        {
            Send(new WebSocketFrame()
            {
                Code = WebSocketFrame.OpCode.TextFrame,
                Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data))
            });
        }

        protected byte[] CreateWebsocketUpgradeReponse(string upgradeRequest)
        {
            return Encoding.UTF8.GetBytes($@"HTTP/1.1 101 Switching Protocols
Connection: Upgrade
Upgrade: websocket
Sec-WebSocket-Accept: {Convert.ToBase64String(
                        SHA1.Create().ComputeHash(
                            Encoding.UTF8.GetBytes(
                                new Regex("Sec-WebSocket-Key: (.*)").Match(upgradeRequest).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
                            )
                        )
                    )}

");
        }

        public Func<string, string, bool> AuthenticateUser { get; set; } = (user, password) =>
        {
            return true;
        };

        protected bool GetAuthentication(string upgradeRequest)
        {
            var url = new Regex("GET (.*) ").Match(upgradeRequest).Groups[1].Value.Trim();

            if (!url.StartsWith("http"))
                url = "http://localhost" + url;

            Uri myUri = new Uri(url);

            var queryString = System.Web.HttpUtility.ParseQueryString(myUri.Query);
            string user = queryString.Get("user");
            string password = queryString.Get("pwd");

            if (AuthenticateUser != null)
                return AuthenticateUser(user, password);

            return true;
        }

        public int ClientReceiveTimeout { get; set; } = 100;
        public int ClientUpgradeTimeout { get; set; } = 500;

        protected void ProcessMessage(WebSocketClient client, IEnumerable<WebSocketFrame> messages)
        {
            if (messages is null)
                return;

            foreach (var message in messages)
            {
                switch (message.Code)
                {
                    case WebSocketFrame.OpCode.ContinuationFrame:
                        break;
                    case WebSocketFrame.OpCode.TextFrame:
                        if (message.Data.Length > 0)
                            Task.Run(() => MessageReceived?.Invoke(this, new MessageReceivedArgs()
                            {
                                Client = client,
                                Message = Encoding.UTF8.GetString(message.Data)
                            }));
                        break;
                    case WebSocketFrame.OpCode.BinaryFrame:
                        break;
                    case WebSocketFrame.OpCode.ConnectionClose:
                        client.Dispose();
                        Clients.Remove(client);
                        Console.WriteLine("Client " + client.Id + " closing...");
                        break;
                    case WebSocketFrame.OpCode.Ping:
                        client.Send(new WebSocketFrame()
                        {
                            Code = WebSocketFrame.OpCode.Pong
                        });
                        break;
                    case WebSocketFrame.OpCode.Pong:
                        Console.WriteLine("Pong received.");
                        break;
                    default:
                        Console.WriteLine("Not supported command.");
                        break;
                }
            }
        }

        public bool Disposed { get; private set; } = false;

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                // Code to dispose the managed resources of the class
                Listener.Server.Dispose();
                Listener.Stop();

                foreach (var client in Clients)
                    client.Dispose();
            }

            // Code to dispose the un-managed resources of the class
            Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
