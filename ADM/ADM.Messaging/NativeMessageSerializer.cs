using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ADM.Core.BrowserMonitoring
{
    public static class NativeMessageSerializer
    {
        internal static int MessageSignature = Int32.MaxValue - 1024;

        public static void WriteMessage(Stream pipe, string message, bool withHeader = true)
        {
            var msgBytes = Encoding.UTF8.GetBytes(message);
            WriteMessage(pipe, msgBytes, withHeader);
        }

        public static void WriteMessage(Stream pipe, byte[] msgBytes, bool withHeader = true)
        {
            if (msgBytes == null) throw new ArgumentNullException(nameof(msgBytes));
            if (msgBytes.Length > BrowserProtocolV1.MaxApplicationMessageBytes)
            {
                throw new ArgumentException($"Message length too long: {msgBytes.Length}", nameof(msgBytes));
            }
            if (withHeader)
            {
                pipe.Write(BitConverter.GetBytes(MessageSignature), 0, 4);
            }
            pipe.Write(BitConverter.GetBytes(msgBytes.Length), 0, 4);
            pipe.Write(msgBytes, 0, msgBytes.Length);
            pipe.Flush();
        }

        public static byte[] ReadMessageBytes(Stream pipe, bool withHeader = true)
        {
            var b4 = new byte[4];
            if (withHeader)
            {
                ReadFully(pipe, b4, 4);
                var sig = BitConverter.ToInt32(b4, 0);
                if (sig != MessageSignature)
                {
                    throw new IOException("Invalid message signature from ADM");
                }
            }
            ReadFully(pipe, b4, 4);
            var syncLength = BitConverter.ToInt32(b4, 0);
            if (syncLength < 0 || syncLength > BrowserProtocolV1.MaxApplicationMessageBytes)
            {
                throw new ArgumentException($"Message length invalid: {syncLength}");
            }
            var bytes = new byte[syncLength];
            ReadFully(pipe, bytes, syncLength);
            return bytes;
        }

        private static void ReadFully(Stream stream, byte[] buf, int bytesToRead)
        {
            var rem = bytesToRead;
            var index = 0;
            while (rem > 0)
            {
                var c = stream.Read(buf, index, rem);
                if (c == 0) throw new IOException("Unexpected EOF");
                index += c;
                rem -= c;
            }
        }
    }
}
