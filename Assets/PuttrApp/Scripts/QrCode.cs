// A QR code for the TV sign-in link, drawn in the app (the API has no QR image). Byte mode, error correction M,
// the smallest version (1 to 20) that fits, the best of the eight masks. After Project Nayuki's QR Code generator
// (MIT), cut down to what a sign-in link needs.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PuttrApp
{
    public static class QrCode
    {
        // Error correction M: codewords per block and blocks, by version (index 0 unused).
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26 };
        static readonly int[] Blocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16 };

        /// <summary>The modules (true = dark), size x size, for this text.</summary>
        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int version = 1;
            for (; version <= 20; version++)
            {
                int capacityBits = DataCodewords(version) * 8;
                int need = 4 + (version < 10 ? 8 : 16) + data.Length * 8;
                if (need <= capacityBits) break;
            }
            if (version > 20) throw new ArgumentException("Too long for a QR code: " + data.Length + " bytes");

            // Bits: mode (byte = 0100), count, data, terminator, pad to a byte, pad bytes.
            var bits = new List<bool>();
            void Put(int value, int length) { for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
            Put(4, 4);
            Put(data.Length, version < 10 ? 8 : 16);
            foreach (byte b in data) Put(b, 8);
            int capacity = DataCodewords(version) * 8;
            Put(0, Math.Min(4, capacity - bits.Count));
            Put(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Put(pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            byte[] all = Interleave(version, codewords);
            int size = version * 4 + 17;
            var modules = new bool[size, size];
            var function = new bool[size, size];
            DrawFunctionPatterns(version, modules, function);
            DrawCodewords(all, modules, function);

            // The mask with the lowest penalty.
            int bestMask = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask, modules, function);
                DrawFormat(mask, modules, function);
                int penalty = Penalty(modules);
                if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
                ApplyMask(mask, modules, function);   // undo (XOR)
            }
            ApplyMask(bestMask, modules, function);
            DrawFormat(bestMask, modules, function);
            return modules;
        }

        /// <summary>A texture of the code: black on white with a four-module quiet zone, each module pixels big.</summary>
        public static Texture2D Texture(string text, int pixelsPerModule = 8)
        {
            bool[,] m = Encode(text);
            int size = m.GetLength(0), border = 4, n = (size + border * 2) * pixelsPerModule;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int mx = x / pixelsPerModule - border, my = y / pixelsPerModule - border;
                    bool dark = mx >= 0 && my >= 0 && mx < size && my < size && m[my, mx];
                    // Texture rows run bottom up: row 0 of the code is the top.
                    pixels[(n - 1 - y) * n + x] = dark ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        static int RawModules(int v)
        {
            int result = (16 * v + 128) * v + 64;
            if (v >= 2) { int align = v / 7 + 2; result -= (25 * align - 10) * align - 55; if (v >= 7) result -= 36; }
            return result;
        }

        static int DataCodewords(int v) => RawModules(v) / 8 - EccPerBlock[v] * Blocks[v];

        static byte[] Interleave(int version, byte[] data)
        {
            int blocks = Blocks[version], ecc = EccPerBlock[version], raw = RawModules(version) / 8;
            int shortBlocks = blocks - raw % blocks, shortLen = raw / blocks;
            byte[] divisor = Divisor(ecc);
            var parts = new List<byte[]>();
            for (int i = 0, k = 0; i < blocks; i++)
            {
                int len = shortLen - ecc + (i < shortBlocks ? 0 : 1);
                var dat = new byte[len];
                Array.Copy(data, k, dat, 0, len);
                k += len;
                byte[] rem = Remainder(dat, divisor);
                var block = new byte[shortLen + 1];
                Array.Copy(dat, 0, block, 0, len);
                Array.Copy(rem, 0, block, shortLen + 1 - ecc, ecc);
                parts.Add(block);
            }
            var result = new List<byte>();
            for (int i = 0; i < parts[0].Length; i++)
                for (int j = 0; j < parts.Count; j++)
                    if (i != shortLen - ecc || j >= shortBlocks) result.Add(parts[j][i]);
            return result.ToArray();
        }

        static byte[] Divisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        static byte[] Remainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
            }
            return result;
        }

        static byte Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        static void Set(bool[,] m, bool[,] f, int x, int y, bool dark) { m[y, x] = dark; f[y, x] = true; }

        static void DrawFunctionPatterns(int version, bool[,] m, bool[,] f)
        {
            int size = m.GetLength(0);
            for (int i = 0; i < size; i++) { Set(m, f, 6, i, i % 2 == 0); Set(m, f, i, 6, i % 2 == 0); }
            Finder(m, f, 3, 3); Finder(m, f, size - 4, 3); Finder(m, f, 3, size - 4);
            int[] align = AlignmentPositions(version);
            for (int i = 0; i < align.Length; i++)
                for (int j = 0; j < align.Length; j++)
                    if (!(i == 0 && j == 0) && !(i == 0 && j == align.Length - 1) && !(i == align.Length - 1 && j == 0))
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                                Set(m, f, align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            DrawFormat(0, m, f);   // reserves the format areas
            if (version >= 7)
            {
                int rem = version;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = version << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + i % 3, b = i / 3;
                    Set(m, f, a, b, bit); Set(m, f, b, a, bit);
                }
            }
        }

        static void Finder(bool[,] m, bool[,] f, int cx, int cy)
        {
            int size = m.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (x >= 0 && x < size && y >= 0 && y < size) Set(m, f, x, y, d != 2 && d != 4);
                }
        }

        static int[] AlignmentPositions(int version)
        {
            if (version == 1) return new int[0];
            int count = version / 7 + 2;
            int step = version == 32 ? 26 : (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = version * 4 + 10; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        static void DrawFormat(int mask, bool[,] m, bool[,] f)
        {
            int size = m.GetLength(0);
            int data = 0 << 3 | mask;   // error correction M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(m, f, 8, i, Bit(i));
            Set(m, f, 8, 7, Bit(6)); Set(m, f, 8, 8, Bit(7)); Set(m, f, 7, 8, Bit(8));
            for (int i = 9; i < 15; i++) Set(m, f, 14 - i, 8, Bit(i));
            for (int i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, Bit(i));
            for (int i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, Bit(i));
            Set(m, f, 8, size - 8, true);
        }

        static void DrawCodewords(byte[] data, bool[,] m, bool[,] f)
        {
            int size = m.GetLength(0), i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (!f[y, x] && i < data.Length * 8) { m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0; i++; }
                    }
            }
        }

        static void ApplyMask(int mask, bool[,] m, bool[,] f)
        {
            int size = m.GetLength(0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (f[y, x]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) m[y, x] = !m[y, x];
                }
        }

        // The standard's penalty: runs, 2x2 blocks, finder-like patterns, dark balance.
        static int Penalty(bool[,] m)
        {
            int size = m.GetLength(0), result = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 0; bool colour = false;
                    for (int b = 0; b < size; b++)
                    {
                        bool c = pass == 0 ? m[a, b] : m[b, a];
                        if (b == 0 || c != colour) { colour = c; run = 1; }
                        else { run++; if (run == 5) result += 3; else if (run > 5) result++; }
                    }
                    for (int b = 0; b + 10 < size + 1; b++)
                    {
                        if (b + 7 > size) break;
                        bool P(int k) => pass == 0 ? m[a, b + k] : m[b + k, a];
                        if (P(0) && !P(1) && P(2) && P(3) && P(4) && !P(5) && P(6))
                        {
                            bool lightBefore = true, lightAfter = true;
                            for (int k = 1; k <= 4; k++)
                            {
                                if (b - k >= 0 && (pass == 0 ? m[a, b - k] : m[b - k, a])) lightBefore = false;
                                if (b + 6 + k < size && (pass == 0 ? m[a, b + 6 + k] : m[b + 6 + k, a])) lightAfter = false;
                            }
                            if (lightBefore || lightAfter) result += 40;
                        }
                    }
                }
            for (int y = 0; y < size - 1; y++)
                for (int x = 0; x < size - 1; x++)
                {
                    bool c = m[y, x];
                    if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1]) result += 3;
                }
            int dark = 0;
            foreach (bool c in m) if (c) dark++;
            int total = size * size;
            int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return result + Math.Max(0, k2) * 10;
        }
    }
}
