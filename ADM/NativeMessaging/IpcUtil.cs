using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NativeMessaging
{
    public static class IpcUtil
    {
        public const int MaxArguments = 256;
        public const int MaxArgumentChars = 65536;
        public const int MaxEncodedLineChars = 128 * 1024;
        public const int MaxDecodedPayloadChars = 512 * 1024;
        public static string MessageHeader = "---ADM-MESSAGE-START---";
        public static string MessageFooter = "---ADM-MESSAGE-END---";
        public static void Send(Stream socketOut, IEnumerable<string> args)
        {
            if (socketOut == null) throw new ArgumentNullException(nameof(socketOut));
            if (args == null) throw new ArgumentNullException(nameof(args));
            var values = new List<string>();
            var totalChars = 0;
            foreach (var arg in args)
            {
                if (values.Count >= MaxArguments) throw new IOException("Too many legacy IPC arguments.");
                if (arg == null || arg.Length > MaxArgumentChars) throw new IOException("Legacy IPC argument is too large.");
                totalChars += arg.Length;
                if (totalChars > MaxDecodedPayloadChars) throw new IOException("Legacy IPC payload is too large.");
                values.Add(arg);
            }

            WriteLine(socketOut, MessageHeader);
            foreach (var arg in values)
            {
                var str = Convert.ToBase64String(Encoding.UTF8.GetBytes(arg));
                if (str.Length > MaxEncodedLineChars) throw new IOException("Encoded legacy IPC argument is too large.");
                WriteLine(socketOut, str);
            }
            WriteLine(socketOut, MessageFooter);
            socketOut.Flush();
        }

        public static List<string> Receive(Stream socketIn)
        {
            if (socketIn == null) throw new ArgumentNullException(nameof(socketIn));
            var args = new List<string>();
            var totalChars = 0;
            var header = ReadLine(socketIn, MessageHeader.Length + 16);
            if (header == MessageHeader)
            {
                while (true)
                {
                    var line = ReadLine(socketIn, MaxEncodedLineChars);
                    if (line == MessageFooter)
                    {
                        return args;
                    }
                    if (args.Count >= MaxArguments) throw new IOException("Too many legacy IPC arguments.");
                    byte[] decoded;
                    try { decoded = Convert.FromBase64String(line); }
                    catch (FormatException ex) { throw new IOException("Invalid legacy IPC argument encoding.", ex); }
                    var value = Encoding.UTF8.GetString(decoded);
                    if (value.Length > MaxArgumentChars) throw new IOException("Legacy IPC argument is too large.");
                    totalChars += value.Length;
                    if (totalChars > MaxDecodedPayloadChars) throw new IOException("Legacy IPC payload is too large.");
                    args.Add(value);
                }
            }
            throw new IOException("Invalid data");
        }

        private static string ReadLine(Stream stream, int maxChars)
        {
            var buf = new StringBuilder();
            while (true)
            {
                var x = stream.ReadByte();
                if (x == -1) throw new IOException("Unexpected EOF");
                if (x == '\n')
                {
                    return buf.ToString();
                }
                if (x != '\r')
                {
                    if (buf.Length >= maxChars) throw new IOException("Legacy IPC line is too long.");
                    buf.Append((char)x);
                }
            }
        }

        private static void WriteLine(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
