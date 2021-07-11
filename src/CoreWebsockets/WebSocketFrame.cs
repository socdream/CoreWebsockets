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

        /// <summary>
        /// Decodes a byte buffer into a websocket frame
        /// </summary>
        /// <param name="message">Byte buffer containing the message, it could be bigger than an actual message</param>
        /// <param name="count">The byte count of the decoded message, this is used to keep the resting bytes for the next request</param>
        /// <param name="masked">The server receives the  message encoded so this should be marked only for server</param>
        /// <returns>Decoded websocket frame</returns>
        public static WebSocketFrame DecodeFrame(byte[] message, out int count, bool masked = true)
        {
            count = 0;

            if (message.Length < 2)
                return null;

            var code = (OpCode)(message[0] & 0x0F);
            var length = masked ? (int)message[1] - 128 : (int)message[1];
            var index = 2;

            if (length == 126)
            {
                if (message.Length < index + 2)
                    return null;

                length = BitConverter.ToUInt16(message.Skip(index).Take(2).Reverse().ToArray(), 0);
                index += 2;
            }
            else if (length == 127)
            {
                if (message.Length < index + 8)
                    return null;

                length = (int)BitConverter.ToUInt64(message.Skip(index).Take(8).Reverse().ToArray(), 0);
                index += 8;
            }

            var decoded = new byte[length];

            if (masked)
            {
                if (message.Length < index + 4 + length)
                    return null;

                var key = message.Skip(index).Take(4).ToArray();
                index += 4;

                for (int i = 0; i < decoded.Length; i++)
                    decoded[i] = (byte)(message[i + index] ^ key[i % 4]);
            }
            else
            {
                if (message.Length < index + length)
                    return null;

                decoded = message.Skip(index).Take(length).ToArray();
            }

            count = index + length;

            return new WebSocketFrame()
            {
                Code = code,
                Data = decoded
            };
        }

        /// <summary>
        /// Encodes a websocket frame into a byte buffer to be sent through the network
        /// </summary>
        /// <param name="frame">Websocket frame to send</param>
        /// <param name="mask">Only client should mask the message</param>
        /// <returns>The encoded byte buffer</returns>
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

            result[0] = (byte)(128 + frame.Code);
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
