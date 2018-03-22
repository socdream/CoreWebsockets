using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CoreWebsockets
{
    public class WebSocketFrame
    {
        public OpCode Code { get; set; }
        public byte[] Data { get; set; }

        public enum OpCode : byte
        {
            ContinuationFrame = 0,
            TextFrame = 1,
            BinaryFrame = 2,
            ConnectionClose = 8,
            Ping = 9,
            Pong = 10
        }
        public enum CloseStatusCode : short
        {
            NormalClosure = 1000,
            GoingAway = 1001,
            ProtocolError = 1002,
            WrongDataType = 1003,
            StatusNotAvailable = 1004,
            NoCode = 1005,
            AbnormalClosed = 1006,
            DataNotConsistent = 1007,
            PolicyViolation = 1008,
            MessageTooBig = 1009,
            ExtensionsNotSupported = 1010,
            UnexpectedCondition = 1011,
            BadGateway = 1014,
            TlsHandshakeError = 1015
        }

        public static WebSocketFrame DecodeFrame(byte[] message, out int count, bool masked = true)
        {
            var code = (OpCode)(message[0] & 0x0F);
            var length = masked ? (int)message[1] - 128 : (int)message[1];
            var index = 2;

            if (length == 126)
            {
                length = BitConverter.ToUInt16(message.Skip(index).Take(2).Reverse().ToArray(), 0);
                index += 2;
            }
            else if (length == 127)
            {
                length = (int)BitConverter.ToUInt64(message.Skip(index).Take(8).Reverse().ToArray(), 0);
                index += 8;
            }

            var decoded = new byte[length];

            if (masked)
            {
                var key = message.Skip(index).Take(4).ToArray();
                index += 4;

                for (int i = 0; i < decoded.Length; i++)
                    decoded[i] = (byte)(message[i + index] ^ key[i % 4]);
            }
            else
            {
                decoded = message.Skip(index).Take(length).ToArray();
            }

            count = index + length;

            return new WebSocketFrame() { Code = code, Data = decoded };
        }

        public static byte[] EncodeFrame(WebSocketFrame frame, bool mask = false)
        {
            var buffer = frame.Data ?? new byte[0];
            var length = 0;
            byte[] result;
            var index = 0;

            var lenBuf = new byte[0];

            if (buffer.Length <= 125)
            {
                length = (mask) ? buffer.Length + 128 : buffer.Length;

                // op code + length + key + data
                result = new byte[1 + 1 + buffer.Length + (mask ? 4 : 0)];
                index = 2;
            }
            else if (buffer.Length >= 126 && buffer.Length <= ushort.MaxValue)
            {
                length = (mask) ? 126 + 128 : 126;

                // op code + length + ushort length + key + data
                lenBuf = BitConverter.GetBytes((ushort)buffer.Length);

                if (BitConverter.IsLittleEndian)
                    lenBuf = lenBuf.Reverse().ToArray();

                result = new byte[1 + 1 + 2 + buffer.Length + (mask ? 4 : 0)];

                Array.Copy(lenBuf, 0, result, 2, lenBuf.Length);

                result[2] = (byte)((buffer.Length >> 8) & 0xFF);
                result[3] = (byte)(buffer.Length & 0xFF);
                index = 4;
            }
            else
            {
                length = (mask) ? 127 + 128 : 127;

                // op code + length + ulong length + key + data
                result = new byte[1 + 1 + 8 + buffer.Length + (mask ? 4 : 0)];

                lenBuf = BitConverter.GetBytes((ulong)buffer.Length);

                if (BitConverter.IsLittleEndian)
                    lenBuf = lenBuf.Reverse().ToArray();

                Array.Copy(lenBuf, 0, result, 2, lenBuf.Length);

                index = 10;
            }

            result[0] = 129;
            result[1] = (byte)length;

            var key = (mask) ? new byte[] { 1, 2, 3, 4 } : new byte[] { 0, 0, 0, 0 };

            if (mask)
            {
                for (int i = 0; i < key.Length; i++)
                    result[index + i] = key[i];

                index += 4;
            }

            for (int i = 0; i < buffer.Length; i++)
                result[index + i] = (byte)(buffer[i] ^ key[i % 4]);

            return result;
        }
    }
}
