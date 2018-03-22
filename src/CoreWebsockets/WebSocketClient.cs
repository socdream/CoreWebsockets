using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public class WebSocketClient : IDisposable
    {
        public int Id { get; set; }
        public TcpClient TcpClient { get; set; }
        /// <summary>
        /// True if the client has successfully upgraded the connection to use websockets
        /// </summary> 
        public bool UpgradedConnection { get; set; } = false;

        public bool Connected { get { return TcpClient?.Connected ?? false; } }

        public event EventHandler<string> MessageReceived;
        public event EventHandler<byte[]> BinaryMessageReceived;
        public event EventHandler<string> ContinuationFrameReceived;
        public event EventHandler<WebSocketFrame.CloseStatusCode> ConnectionClosed;
        public event EventHandler<Exception> UnexpectedException;
        public event EventHandler Pong;

        public bool Connect(string url, int timeout = 5000)
        {
            var regex = new Regex(@"(ws[s]?|http[s]?):\/\/([\w.]*):?([0-9]*)?");

            var match = regex.Match(url);

            var dns = match.Groups[2].Value;
            var port = match.Groups[3].Value;

            if (string.IsNullOrWhiteSpace(port))
                port = "80";

            TcpClient = new TcpClient(dns, int.Parse(port))
            {
                NoDelay = true
            };

            TcpClient.Client.NoDelay = true;

            if (!TcpClient.Connected)
                return false;

            var retries = 0;

            SendUpgradeConnection(url);

            while (TcpClient.Connected)
            {
                if (ReceiveUpgradeConnection())
                {
                    UpgradedConnection = true;
                    return true;
                }
                else
                {
                    retries++;

                    if (retries * 500 > timeout)
                        break;

                    Thread.Sleep(500);
                }
            }

            if (!TcpClient.Connected)
                TcpClient.Dispose();

            return false;
        }

        public void Run()
        {
            while (!disposedValue)
            {
                if (TcpClient == null || TcpClient.Connected == false)
                    break;

                ProcessMessage();

                Thread.Sleep(10);
            }
        }

        public int ClientReceiveTimeout { get; set; } = 100;

        private List<byte> _buffer = new List<byte>();

        public string GetHttpRequest()
        {
            _buffer.AddRange(TcpClient.GetStream().GetStreamDataAvailable(ClientReceiveTimeout));

            if (_buffer.Count == 0)
                return null;

            var request = Encoding.UTF8.GetString(_buffer.ToArray());

            if (request.EndsWith("\r\n\r\n"))
            {
                _buffer.Clear();
                return request;
            }

            return null;
        }

        /// <summary>
        /// Gets the next WebSocket frame in the socket
        /// </summary>
        /// <param name="server">Sets if the client is a standalone client connected to a server or a client residing on the server</param>
        /// <returns></returns>
        public WebSocketFrame GetMessage(bool server)
        {
            _buffer.AddRange(TcpClient.GetStream().GetStreamDataAvailable(ClientReceiveTimeout));

            if (_buffer.Count == 0)
                return null;

            int count;

            var message = WebSocketFrame.DecodeFrame(_buffer.ToArray(), out count, false);

            if (message != null)
                _buffer = _buffer.Skip(count).ToList();

            return message;
        }

        protected void ProcessMessage()
        {
            WebSocketFrame message;

            lock (_buffer)
                message = GetMessage(false);

            if (message == null)
                return;

            switch (message.Code)
            {
                case WebSocketFrame.OpCode.ContinuationFrame:
                    Task.Run(() => ContinuationFrameReceived?.Invoke(this, Encoding.UTF8.GetString(message.Data)));
                    break;
                case WebSocketFrame.OpCode.TextFrame:
                    Task.Run(() => MessageReceived?.Invoke(this, Encoding.UTF8.GetString(message.Data)));
                    break;
                case WebSocketFrame.OpCode.BinaryFrame:
                    Task.Run(() => BinaryMessageReceived?.Invoke(this, message.Data));
                    break;
                case WebSocketFrame.OpCode.ConnectionClose:
                    TcpClient.Close();
                    var code = BitConverter.ToUInt16(message.Data.Take(2).Reverse().ToArray(), 0);
                    Task.Run(() => ConnectionClosed?.Invoke(this, (WebSocketFrame.CloseStatusCode)code));
                    break;
                case WebSocketFrame.OpCode.Ping:
                    Console.WriteLine("Ping received.");
                    Task.Run(() => Send(new WebSocketFrame() { Code = WebSocketFrame.OpCode.Pong, Data = new byte[0] }));
                    break;
                case WebSocketFrame.OpCode.Pong:
                    Task.Run(() => Pong?.Invoke(this, null));
                    break;
                default:
                    Console.WriteLine("Not supported command.");
                    break;
            }
        }

        public void Send(string message)
        {
            Send(new WebSocketFrame() { Code = WebSocketFrame.OpCode.TextFrame, Data = Encoding.UTF8.GetBytes(message) });
        }

        public void Send(WebSocketFrame message)
        {
            var buffer = WebSocketFrame.EncodeFrame(message, true);

            try
            {
                TcpClient.Client.Send(buffer);
            }
            catch (Exception)
            {
            }
        }

        public void Send<T>(T data)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));

            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, data);

                Send(new WebSocketFrame() { Code = WebSocketFrame.OpCode.TextFrame, Data = stream.ToArray() });
            }
        }

        public void SendUpgradeConnection(string url)
        {
            var regex = new Regex(@"(ws[s]?|http[s]?):\/\/([\w.]*):?([0-9]*)?");

            var match = regex.Match(url);

            var dns = match.Groups[2].Value;

            var handshake =
                $@"GET {url} HTTP/1.1
Upgrade: websocket
Connection: Upgrade
Host: {dns}
Origin: http://localhost
Sec-WebSocket-Key: Pkhf2NDbC2be4sqOVnsyxQ==
Sec-WebSocket-Version: 13

";

            var buffer = Encoding.UTF8.GetBytes(handshake);

            TcpClient.Client.Send(buffer);
        }

        public bool ReceiveUpgradeConnection()
        {
            var response = GetHttpRequest();

            if (string.IsNullOrWhiteSpace(response))
                return false;

            if (new Regex("Connection:(.*)").Match(response).Groups[1].Value.Trim() != "Upgrade")
                return false;

            return TcpClient.Connected;
        }

        public void Disconnect()
        {
            try
            {
                if (TcpClient?.Connected ?? false)
                    Send(new WebSocketFrame
                    {
                        Code = WebSocketFrame.OpCode.ConnectionClose,
                        Data = BitConverter.GetBytes((short)WebSocketFrame.CloseStatusCode.NormalClosure)
                    });
            }
            catch (Exception e)
            {
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Disconnect();

                    if (TcpClient != null)
                        TcpClient.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
