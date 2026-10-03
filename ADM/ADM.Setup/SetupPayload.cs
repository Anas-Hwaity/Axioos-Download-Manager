using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Axioos.Setup
{
    internal sealed class SetupPayload
    {
        internal const string ResourceName = "payload.bin";
        private const string Magic = "AXSETUP1";
        private const int HeaderSize = 54;
        private const long MaxUnpackedBytes = 0x7FFFFFC7;

        internal string FileName { get; private set; }
        internal byte[] Content { get; private set; }

        internal static SetupPayload Unpack(Action<int> report)
        {
            byte[] packed = ReadResource();
            if (packed.Length < HeaderSize || Encoding.ASCII.GetString(packed, 0, 8) != Magic)
            {
                throw new InvalidDataException("The setup payload is missing or damaged.");
            }
            bool x86Filter = (packed[8] & 1) != 0;
            int lc = packed[9];
            int lp = packed[10];
            int pb = packed[11];
            long unpackedSize = BitConverter.ToInt64(packed, 12);
            byte[] expectedHash = new byte[32];
            Buffer.BlockCopy(packed, 20, expectedHash, 0, 32);
            int nameLength = BitConverter.ToUInt16(packed, 52);
            if (unpackedSize <= 0 || unpackedSize > MaxUnpackedBytes || lc > 8 || lp > 4 || pb > 4 ||
                nameLength <= 0 || packed.Length < HeaderSize + nameLength)
            {
                throw new InvalidDataException("The setup payload is damaged.");
            }
            string name = Encoding.UTF8.GetString(packed, HeaderSize, nameLength);
            if (name.Length == 0 || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException("The setup payload is damaged.");
            }
            byte[] content;
            try
            {
                var decoder = new LzmaDecoder(packed, HeaderSize + nameLength, lc, lp, pb);
                content = decoder.Decode((int)unpackedSize, report);
                if (x86Filter) X86Filter.Undo(content);
            }
            catch (IndexOutOfRangeException error)
            {
                throw new InvalidDataException("The setup payload is damaged.", error);
            }
            catch (OutOfMemoryException error)
            {
                throw new InvalidDataException("The setup payload is damaged or this computer is out of memory.", error);
            }
            using (var sha = SHA256.Create())
            {
                byte[] actual = sha.ComputeHash(content);
                for (int i = 0; i < expectedHash.Length; i++)
                {
                    if (actual[i] != expectedHash[i]) throw new InvalidDataException("The setup payload failed its integrity check.");
                }
            }
            return new SetupPayload { FileName = name, Content = content };
        }

        private static byte[] ReadResource()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("This setup file holds no installer.");
                byte[] data = new byte[stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read <= 0) throw new InvalidDataException("The setup payload is truncated.");
                    offset += read;
                }
                return data;
            }
        }
    }

    internal static class X86Filter
    {
        private static bool IsEdge(byte value)
        {
            return value == 0 || value == 0xFF;
        }

        internal static void Undo(byte[] buffer)
        {
            bool[] allowed = { true, true, true, false, true, false, false, false };
            int[] bitNumber = { 0, 1, 2, 2, 3, 3, 3, 3 };
            int size = buffer.Length;
            if (size < 5) return;
            unchecked
            {
                uint prevMask = 0;
                uint prevPos = 0xFFFFFFFB;
                int i = 0;
                int limit = size - 5;
                while (i <= limit)
                {
                    byte b = buffer[i];
                    if (b != 0xE8 && b != 0xE9)
                    {
                        i++;
                        continue;
                    }
                    uint offset = (uint)i - prevPos;
                    prevPos = (uint)i;
                    if (offset > 5)
                    {
                        prevMask = 0;
                    }
                    else
                    {
                        for (uint k = 0; k < offset; k++)
                        {
                            prevMask &= 0x77;
                            prevMask <<= 1;
                        }
                    }
                    b = buffer[i + 4];
                    if (IsEdge(b) && allowed[(int)((prevMask >> 1) & 7)] && (prevMask >> 1) < 0x10)
                    {
                        uint src = ((uint)b << 24) | ((uint)buffer[i + 3] << 16) | ((uint)buffer[i + 2] << 8) | buffer[i + 1];
                        uint dest;
                        for (;;)
                        {
                            dest = src - ((uint)i + 5);
                            if (prevMask == 0) break;
                            int index = bitNumber[(int)((prevMask >> 1) & 7)];
                            b = (byte)(dest >> (24 - index * 8));
                            if (!IsEdge(b)) break;
                            src = dest ^ ((1u << (32 - index * 8)) - 1);
                        }
                        buffer[i + 4] = (byte)(~(((dest >> 24) & 1) - 1));
                        buffer[i + 3] = (byte)(dest >> 16);
                        buffer[i + 2] = (byte)(dest >> 8);
                        buffer[i + 1] = (byte)dest;
                        i += 5;
                        prevMask = 0;
                    }
                    else
                    {
                        i++;
                        prevMask |= 1;
                        if (IsEdge(b)) prevMask |= 0x10;
                    }
                }
            }
        }
    }
}
