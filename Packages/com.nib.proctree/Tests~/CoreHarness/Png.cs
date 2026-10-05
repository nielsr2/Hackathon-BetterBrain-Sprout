using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Nib.ProcTree.Harness
{
    /// <summary>Minimal RGB8 PNG writer (zlib via ZLibStream) so the harness needs no image library.</summary>
    static class Png
    {
        public static void Write(string path, int w, int h, byte[] rgb)
        {
            using var fs = File.Create(path);
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            BE(ihdr, 0, w); BE(ihdr, 4, h);
            ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
            Chunk(fs, "IHDR", ihdr);
            using var raw = new MemoryStream();
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (int y = 0; y < h; y++)
                {
                    z.WriteByte(0);
                    z.Write(rgb, y * w * 3, w * 3);
                }
            }
            Chunk(fs, "IDAT", raw.ToArray());
            Chunk(fs, "IEND", Array.Empty<byte>());
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
            var t = Encoding.ASCII.GetBytes(type);
            s.Write(t); s.Write(data);
            uint crc = Crc(t, 0xFFFFFFFFu); crc = Crc(data, crc) ^ 0xFFFFFFFFu;
            var c = new byte[4]; BE(c, 0, (int)crc); s.Write(c);
        }

        static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static uint[] _table;
        static uint Crc(byte[] data, uint crc)
        {
            if (_table == null)
            {
                _table = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _table[n] = c; }
            }
            foreach (byte b in data) crc = _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
