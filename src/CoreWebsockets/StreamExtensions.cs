using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public static class StreamExtensions
    {
        public static string GetHttpRequest(this Stream stream, int timeout = 1000)
        {
            var result = new List<byte>();

            if (!((NetworkStream)stream).DataAvailable)
                return "";

            try
            {
                while (true)
                {
                    result.Add(stream.GetByte(timeout));

                    if (result.Count > 4 && result.Skip(result.Count - 4).ToArray().SequenceEqual(new byte[] { 0xD, 0xA, 0xD, 0xA }))
                        break;
                }
            }
            catch (Exception e)
            {

            }

            return Encoding.UTF8.GetString(result.ToArray());
        }

        public static byte GetByte(this Stream stream, int timeout = 100)
        {
            var task = Task.Run(() => stream.ReadByte());

            Task.WhenAny(task, Task.Delay(timeout)).Wait();

            if (!task.IsCompleted)
                throw new Exception("Timeout");

            if (task.Result < 0)
                throw new Exception("EOF");

            return (byte)task.Result;
        }

        public static WebSocketFrame GetWebSocketRequest(this Stream stream, bool masked, int timeout = 100)
        {
            var result = new List<byte>();

            if (!((NetworkStream)stream).DataAvailable)
                return null;

            try
            {
                var header = stream.GetByte(timeout);
                var opCode = (WebSocketFrame.OpCode)(header & 0x0F);
                var length = (int)stream.GetByte(timeout);

                if (masked)
                    length = length - 128;

                var lenBuf = new byte[0];

                if (length == 126)
                {
                    lenBuf = new byte[] { stream.GetByte(timeout), stream.GetByte(timeout) };

                    if (BitConverter.IsLittleEndian)
                        lenBuf = lenBuf.Reverse().ToArray();

                    length = BitConverter.ToUInt16(lenBuf, 0);
                }
                else if (length == 127)
                {
                    lenBuf = Enumerable.Range(0, 8).Select(a => stream.GetByte(timeout)).ToArray();

                    if (BitConverter.IsLittleEndian)
                        lenBuf = lenBuf.Reverse().ToArray();

                    length = (int)BitConverter.ToUInt64(lenBuf, 0);
                }

                if (masked)
                {
                    var key = Enumerable.Range(0, 4).Select(a => stream.GetByte(timeout)).ToArray();

                    return new WebSocketFrame() { Code = opCode, Data = Enumerable.Range(0, length).Select(a => (byte)(stream.GetByte(timeout) ^ key[a % 4])).ToArray() };
                }
                else
                {
                    return new WebSocketFrame() { Code = opCode, Data = Enumerable.Range(0, length).Select(a => stream.GetByte(timeout)).ToArray() };
                }
            }
            catch (Exception e)
            {
                throw new IOException("Frame couldn't be completely read.", e);
            }
        }

        public static List<byte> GetStreamDataAvailable(this NetworkStream stream)
        {
            var result = new List<byte>();

            var bufferLength = 8192;
            var buffer = new byte[bufferLength];
            var received = 0;
            var ini = DateTime.Now;

            do
            {
                if (!stream.DataAvailable)
                    break;

                received = stream.Read(buffer, 0, buffer.Length);
                result.AddRange(buffer.Take(received));

            } while (received == bufferLength);

            return result;
        }
    }
}
