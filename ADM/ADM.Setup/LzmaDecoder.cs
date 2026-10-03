using System;
using System.IO;

namespace Axioos.Setup
{
    internal sealed class LzmaDecoder
    {
        private const uint TopValue = 0x1000000;
        private const ushort InitialProbability = 1024;
        private const int ReportEveryBytes = 1 << 20;

        private sealed class LengthCoder
        {
            internal readonly ushort[] Choice = NewProbabilities(2);
            internal readonly ushort[] Low = NewProbabilities(16 << 3);
            internal readonly ushort[] Mid = NewProbabilities(16 << 3);
            internal readonly ushort[] High = NewProbabilities(256);
        }

        private readonly byte[] input;
        private readonly int lc;
        private readonly int lp;
        private readonly int pb;
        private int inputPosition;
        private uint range = 0xFFFFFFFF;
        private uint code;

        internal LzmaDecoder(byte[] input, int start, int lc, int lp, int pb)
        {
            this.input = input;
            this.inputPosition = start;
            this.lc = lc;
            this.lp = lp;
            this.pb = pb;
        }

        private static ushort[] NewProbabilities(int count)
        {
            var values = new ushort[count];
            for (int i = 0; i < values.Length; i++) values[i] = InitialProbability;
            return values;
        }

        private uint ReadByte()
        {
            if (inputPosition >= input.Length) throw new InvalidDataException("The setup payload is truncated.");
            return input[inputPosition++];
        }

        private int Bit(ushort[] probabilities, int index)
        {
            unchecked
            {
                uint value = probabilities[index];
                uint bound = (range >> 11) * value;
                int symbol;
                if (code < bound)
                {
                    probabilities[index] = (ushort)(value + ((2048 - value) >> 5));
                    range = bound;
                    symbol = 0;
                }
                else
                {
                    probabilities[index] = (ushort)(value - (value >> 5));
                    code -= bound;
                    range -= bound;
                    symbol = 1;
                }
                if (range < TopValue)
                {
                    range <<= 8;
                    code = (code << 8) | ReadByte();
                }
                return symbol;
            }
        }

        private uint Direct(int count)
        {
            unchecked
            {
                uint result = 0;
                for (; count > 0; count--)
                {
                    range >>= 1;
                    code -= range;
                    uint mask = 0u - (code >> 31);
                    code += range & mask;
                    if (range < TopValue)
                    {
                        range <<= 8;
                        code = (code << 8) | ReadByte();
                    }
                    result = (result << 1) + mask + 1;
                }
                return result;
            }
        }

        private int Tree(ushort[] probabilities, int offset, int bits)
        {
            int m = 1;
            for (int i = 0; i < bits; i++) m = (m << 1) + Bit(probabilities, offset + m);
            return m - (1 << bits);
        }

        private int Reverse(ushort[] probabilities, int offset, int bits)
        {
            int m = 1;
            int symbol = 0;
            for (int i = 0; i < bits; i++)
            {
                int bit = Bit(probabilities, offset + m);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }

        private int ReadLength(LengthCoder coder, int posState)
        {
            if (Bit(coder.Choice, 0) == 0) return Tree(coder.Low, posState << 3, 3);
            if (Bit(coder.Choice, 1) == 0) return 8 + Tree(coder.Mid, posState << 3, 3);
            return 16 + Tree(coder.High, 0, 8);
        }

        internal byte[] Decode(int outputSize, Action<int> report)
        {
            var output = new byte[outputSize];
            if (ReadByte() != 0) throw new InvalidDataException("The setup payload is damaged.");
            for (int i = 0; i < 4; i++) code = unchecked((code << 8) | ReadByte());

            var literal = NewProbabilities(0x300 << (lc + lp));
            var isMatch = NewProbabilities(12 << 4);
            var isRep = NewProbabilities(12);
            var isRepG0 = NewProbabilities(12);
            var isRepG1 = NewProbabilities(12);
            var isRepG2 = NewProbabilities(12);
            var isRep0Long = NewProbabilities(12 << 4);
            var posSlot = NewProbabilities(4 << 6);
            var posSpecial = NewProbabilities(115);
            var align = NewProbabilities(16);
            var matchLength = new LengthCoder();
            var repLength = new LengthCoder();

            int pos = 0;
            int state = 0;
            uint rep0 = 0;
            uint rep1 = 0;
            uint rep2 = 0;
            uint rep3 = 0;
            int posMask = (1 << pb) - 1;
            int lpMask = (1 << lp) - 1;
            int nextReport = 0;
            int lastPercent = -1;

            while (pos < outputSize)
            {
                if (report != null && pos >= nextReport)
                {
                    nextReport = pos > int.MaxValue - ReportEveryBytes ? int.MaxValue : pos + ReportEveryBytes;
                    int percent = (int)((long)pos * 100 / outputSize);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        report(percent);
                    }
                }
                int posState = pos & posMask;
                if (Bit(isMatch, (state << 4) + posState) == 0)
                {
                    int previous = pos > 0 ? output[pos - 1] : 0;
                    int baseIndex = 0x300 * (((pos & lpMask) << lc) + (previous >> (8 - lc)));
                    int symbol = 1;
                    if (state >= 7)
                    {
                        int matchByte = output[pos - (int)rep0 - 1];
                        do
                        {
                            int matchBit = (matchByte >> 7) & 1;
                            matchByte = (matchByte << 1) & 0xFF;
                            int bit = Bit(literal, baseIndex + ((1 + matchBit) << 8) + symbol);
                            symbol = (symbol << 1) | bit;
                            if (matchBit != bit) break;
                        }
                        while (symbol < 0x100);
                    }
                    while (symbol < 0x100) symbol = (symbol << 1) | Bit(literal, baseIndex + symbol);
                    output[pos++] = (byte)(symbol & 0xFF);
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    continue;
                }
                int length;
                if (Bit(isRep, state) != 0)
                {
                    if (pos == 0) throw new InvalidDataException("The setup payload is damaged.");
                    if (Bit(isRepG0, state) == 0)
                    {
                        if (Bit(isRep0Long, (state << 4) + posState) == 0)
                        {
                            if (rep0 >= (uint)pos) throw new InvalidDataException("The setup payload is damaged.");
                            state = state < 7 ? 9 : 11;
                            output[pos] = output[pos - (int)rep0 - 1];
                            pos++;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (Bit(isRepG1, state) == 0)
                        {
                            distance = rep1;
                        }
                        else
                        {
                            if (Bit(isRepG2, state) == 0)
                            {
                                distance = rep2;
                            }
                            else
                            {
                                distance = rep3;
                                rep3 = rep2;
                            }
                            rep2 = rep1;
                        }
                        rep1 = rep0;
                        rep0 = distance;
                    }
                    length = ReadLength(repLength, posState);
                    state = state < 7 ? 8 : 11;
                }
                else
                {
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = rep0;
                    length = ReadLength(matchLength, posState);
                    state = state < 7 ? 7 : 10;
                    int lengthState = length > 3 ? 3 : length;
                    int slot = Tree(posSlot, lengthState << 6, 6);
                    if (slot < 4)
                    {
                        rep0 = (uint)slot;
                    }
                    else
                    {
                        int directBits = (slot >> 1) - 1;
                        uint distance = (uint)(2 | (slot & 1)) << directBits;
                        if (slot < 14)
                        {
                            distance = unchecked(distance + (uint)Reverse(posSpecial, (int)distance - slot, directBits));
                        }
                        else
                        {
                            distance = unchecked(distance + (Direct(directBits - 4) << 4));
                            distance = unchecked(distance + (uint)Reverse(align, 0, 4));
                        }
                        rep0 = distance;
                    }
                    if (rep0 == 0xFFFFFFFF) break;
                }
                if (rep0 >= (uint)pos) throw new InvalidDataException("The setup payload is damaged.");
                length += 2;
                if (length > outputSize - pos) throw new InvalidDataException("The setup payload is damaged.");
                int from = pos - (int)rep0 - 1;
                for (int i = 0; i < length; i++) output[pos + i] = output[from + i];
                pos += length;
            }
            if (pos != outputSize) throw new InvalidDataException("The setup payload is incomplete.");
            if (report != null) report(100);
            return output;
        }
    }
}
