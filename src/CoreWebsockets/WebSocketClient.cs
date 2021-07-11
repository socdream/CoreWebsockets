using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public class WebSocketClient : IDisposable
    {
        public int Id { get; set; }
        private TcpClient TcpClient { get; set; }
        private Stream _stream;
        /// <summary>
        /// True if the client has successfully upgraded the connection to use websockets
        /// </summary> 
        public bool UpgradedConnection { get; set; } = false;

        public bool Connected => TcpClient?.Connected ?? false;
        /// <summary>
        /// Time in miliseconds to check the connection state of the socket
        /// </summary>
        public int KeepAlive { get; set; } = 1000;

        public bool ServerClient { get; set; }
        public bool Ssl { get; set; }
        public string Dns { get; set; }
        public int Port { get; set; }

        public event EventHandler<string> MessageReceived;
        public event EventHandler<byte[]> BinaryMessageReceived;
        public event EventHandler<string> ContinuationFrameReceived;
        public event EventHandler<WebSocketFrame.CloseStatusCode> ConnectionClosed;
        public event EventHandler Pong;
        public event EventHandler Ping;
        public event EventHandler ClientConnected;
        public event EventHandler ClientDisconnected;

        public WebSocketClient() { }

        public WebSocketClient(TcpClient client)
        {
            TcpClient = client;
            _stream = client.GetStream();
        }

        public async Task<bool> Connect(string url, Dictionary<string, string> extraHeaders = null, int timeout = 5000)
        {
            var regex = new Regex(@"(ws[s]?|http[s]?):\/\/([\w.\-]*):?([0-9]*)?");

            var match = regex.Match(url);

            Ssl = match.Groups[1].Value.ToLower() == "wss";
            Dns = match.Groups[2].Value;
            var port = match.Groups[3].Value;

            if (string.IsNullOrWhiteSpace(port))
                port = Ssl ? "443" : "80";

            try
            {
                Port = int.Parse(port);

                TcpClient = new TcpClient()
                {
                    NoDelay = true
                };

                await TcpClient.ConnectAsync(Dns, Port).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }

            TcpClient.Client.NoDelay = true;

            if (!TcpClient.Connected)
                return false;

            if (!Ssl)
                _stream = TcpClient.GetStream();
            else
            {
                _stream = new SslStream(TcpClient.GetStream(), true, new RemoteCertificateValidationCallback(CertificateValidation));

                var certs = CertificatesProvider.GetClientCertificates();

                await (_stream as SslStream).AuthenticateAsClientAsync(Dns, certs, System.Security.Authentication.SslProtocols.Tls12, false).ConfigureAwait(false);
                _stream.ReadTimeout = 1;
            }

            var retries = 0;

            await SendUpgradeConnection(url, extraHeaders).ConfigureAwait(false);
            var loopTime = 50;

            while (TcpClient.IsConnected())
            {
                if (await ReceiveUpgradeConnection().ConfigureAwait(false))
                {
                    UpgradedConnection = true;
                    ClientConnected?.Invoke(this, null);

                    return true;
                }
                else
                {
                    retries++;

                    if (retries * loopTime > timeout)
                        break;

                    await Task.Delay(loopTime).ConfigureAwait(false);
                }
            }

            if (!TcpClient.Connected)
                TcpClient.Dispose();

            return false;
        }

        private DateTime _lastConnectionCheck = DateTime.Now;

        public async Task Run()
        {
            while (!disposedValue)
            {
                if ((DateTime.Now - _lastConnectionCheck).TotalMilliseconds > KeepAlive)
                {
                    _lastConnectionCheck = DateTime.Now;

                    if (!(TcpClient?.IsConnected() ?? false))
                    {
                        TcpClient?.Dispose();
                        ClientDisconnected?.Invoke(this, null);
                        return;
                    }
                }

                await ProcessMessage().ConfigureAwait(false);

                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public int ClientReceiveTimeout { get; set; } = 50;

        private List<byte> _buffer = new List<byte>();

        public async Task<string> GetHttpRequest()
        {
            _buffer.AddRange(await _stream.GetStreamDataAvailableAsync().ConfigureAwait(false));

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
        public async Task<List<WebSocketFrame>> GetMessages()
        {
            var data = await _stream.GetStreamDataAvailableAsync().ConfigureAwait(false);

            lock (_buffer)
                _buffer.AddRange(data);

            var message = WebSocketFrame.DecodeFrame(_buffer.ToArray(), out int count, ServerClient);
            var result = new List<WebSocketFrame>();

            while (message != null)
            {
                result.Add(message);

                lock (_buffer)
                    _buffer = _buffer.Skip(count).ToList();

                message = WebSocketFrame.DecodeFrame(_buffer.ToArray(), out count, ServerClient);
            }

            return result;
        }

        protected async Task ProcessMessage()
        {
            var messages = await GetMessages().ConfigureAwait(false);

            if (messages is null)
                return;

            foreach (var message in messages)
            {
                switch (message.Code)
                {
                    case WebSocketFrame.OpCode.ContinuationFrame:
                        ContinuationFrameReceived?.Invoke(this, Encoding.UTF8.GetString(message.Data));
                        break;
                    case WebSocketFrame.OpCode.TextFrame:
                        if (message.Data.Length > 0)
                            MessageReceived?.Invoke(this, Encoding.UTF8.GetString(message.Data));
                        break;
                    case WebSocketFrame.OpCode.BinaryFrame:
                        BinaryMessageReceived?.Invoke(this, message.Data);
                        break;
                    case WebSocketFrame.OpCode.ConnectionClose:
                        TcpClient.Close();
                        var code = BitConverter.ToUInt16(message.Data.Take(2).Reverse().ToArray(), 0);
                        ConnectionClosed?.Invoke(this, (WebSocketFrame.CloseStatusCode)code);
                        break;
                    case WebSocketFrame.OpCode.Ping:
                        Ping?.Invoke(this, null);
                        break;
                    case WebSocketFrame.OpCode.Pong:
                        Pong?.Invoke(this, null);
                        break;
                    default:
                        Console.WriteLine("Not supported command.");
                        break;
                }
            }
        }

        public async Task Send(string message)
        {
            await Send(new WebSocketFrame()
            {
                Code = WebSocketFrame.OpCode.TextFrame,
                Data = Encoding.UTF8.GetBytes(message)
            }).ConfigureAwait(false);
        }

        public async Task Send(WebSocketFrame message)
        {
            var buffer = WebSocketFrame.EncodeFrame(message, !ServerClient);

            try
            {
                await SendRawData(buffer).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        public async Task Send<T>(T data)
        {
            await Send(new WebSocketFrame()
            {
                Code = WebSocketFrame.OpCode.TextFrame,
                Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data))
            }).ConfigureAwait(false);
        }
        public bool CertificateValidation(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public async Task SendRawData(byte[] buffer)
        {
            if (!Ssl)
                await _stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            else
                await (_stream as SslStream).WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        }

        public async Task SendUpgradeConnection(string url, Dictionary<string, string> extraHeaders = null)
        {
            var regex = new Regex(@"(ws[s]?|http[s]?):\/\/([\w.]*):?([0-9]*)?");

            var match = regex.Match(url);

            var dns = match.Groups[2].Value;

            var handshake =
                $@"GET {url} HTTP/1.1
Upgrade: websocket
Connection: Upgrade
Host: {dns}
Sec-WebSocket-Key: Pkhf2NDbC2be4sqOVnsyxQ==
Sec-WebSocket-Protocol: chat, superchat
Sec-WebSocket-Version: 13
{(extraHeaders is null ? "" : string.Join("\r\n", extraHeaders.Select(a => $"{a.Key}: {a.Value}")))}
";

            var buffer = Encoding.UTF8.GetBytes(handshake);

            await SendRawData(buffer).ConfigureAwait(false);
        }

        public async Task<bool> ReceiveUpgradeConnection()
        {
            var response = await GetHttpRequest().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
                return false;

            if (new Regex("Connection:(.*)").Match(response).Groups[1].Value.Trim() != "Upgrade")
                return false;

            return TcpClient.Connected;
        }

        public async Task Disconnect()
        {
            try
            {
                if (TcpClient?.Connected ?? false)
                    await Send(new WebSocketFrame
                    {
                        Code = WebSocketFrame.OpCode.ConnectionClose,
                        Data = BitConverter.GetBytes((short)WebSocketFrame.CloseStatusCode.NormalClosure)
                    }).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual async void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    await Disconnect().ConfigureAwait(false);

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
