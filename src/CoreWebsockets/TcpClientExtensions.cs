using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public static class TcpClientExtensions
    {
        public static async Task<string> GetHttpRequestAsync(this TcpClient tcp)
        {
            string result = string.Empty;

            using (var stream = tcp.GetStream())
            {
                // receive data
                using (var memory = new MemoryStream())
                {
                    await stream.CopyToAsync(memory).ConfigureAwait(false);
                    memory.Position = 0;
                    var data = memory.ToArray();

                    var index = BinaryMatch(data, Encoding.ASCII.GetBytes("\r\n\r\n")) + 4;
                    var headers = Encoding.ASCII.GetString(data, 0, index);
                    memory.Position = index;

                    if (headers.IndexOf("Content-Encoding: gzip") > 0)
                    {
                        using (var decompressionStream = new GZipStream(memory, CompressionMode.Decompress))
                        using (var decompressedMemory = new MemoryStream())
                        {
                            decompressionStream.CopyTo(decompressedMemory);
                            decompressedMemory.Position = 0;
                            result = Encoding.UTF8.GetString(decompressedMemory.ToArray());
                        }
                    }
                    else
                    {
                        result = Encoding.UTF8.GetString(data, index, data.Length - index);
                    }
                }

                return result;
            }
        }

        public static async Task SendHttpRequestAsync(this TcpClient tcp)
        {
            string result = string.Empty;

            using (var stream = tcp.GetStream())
            {
                tcp.SendTimeout = 500;
                tcp.ReceiveTimeout = 1000;
                // Send request headers
                var builder = new StringBuilder();
                builder.AppendLine("GET /?scope=images&nr=1 HTTP/1.1");
                builder.AppendLine("Host: www.bing.com");
                //builder.AppendLine("Content-Length: " + data.Length);   // only for POST request
                builder.AppendLine("Connection: close");
                builder.AppendLine();
                var header = Encoding.ASCII.GetBytes(builder.ToString());
                await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);

                // Send payload data if you are POST request
                //await stream.WriteAsync(data, 0, data.Length);
            }
        }

        private static int BinaryMatch(byte[] input, byte[] pattern)
        {
            int sLen = input.Length - pattern.Length + 1;
            for (int i = 0; i < sLen; ++i)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; ++j)
                {
                    if (input[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }
            }
            return -1;
        }
        public static bool IsConnected(this TcpClient client, int timeout = 0)
        {
            if (!(client?.Connected ?? false))
                return false;

            if (!(client?.Client.Poll(timeout, SelectMode.SelectWrite) ?? false))
                return false;

            if ((client?.Client.Poll(timeout, SelectMode.SelectRead) ?? true) && ((client?.Client.Available ?? 0) == 0))
                return false;

            if (client?.Client.Poll(timeout, SelectMode.SelectError) ?? true)
                return false;

            return client?.Connected ?? false;
        }
    }
}
