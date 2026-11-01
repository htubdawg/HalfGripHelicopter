using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>A minimal PNG writer (8-bit RGBA, no filtering), so the preview needs nothing but .NET.</summary>
	internal static class Png
	{
		private static readonly uint[] table = Table();

		/// <summary>Writes pixels (rows from the bottom, as Unity keeps them) as a PNG, each pixel <paramref name="scale"/> pixels square.</summary>
		internal static void Save(string path, Color32[] px, int w, int h, int scale = 1)
		{
			int sw = w * scale, sh = h * scale;
			MemoryStream raw = new MemoryStream();
			for (int y = sh - 1; y >= 0; y--)
			{
				raw.WriteByte(0);
				for (int x = 0; x < sw; x++)
				{
					Color32 c = px[(y / scale) * w + x / scale];
					raw.WriteByte(c.r);
					raw.WriteByte(c.g);
					raw.WriteByte(c.b);
					raw.WriteByte(c.a);
				}
			}
			MemoryStream packed = new MemoryStream();
			using (ZLibStream z = new ZLibStream(packed, System.IO.Compression.CompressionLevel.SmallestSize, true))
			{
				raw.Position = 0;
				raw.CopyTo(z);
			}
			using FileStream f = File.Create(path);
			f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
			MemoryStream header = new MemoryStream();
			Big(header, (uint)sw);
			Big(header, (uint)sh);
			header.Write(new byte[] { 8, 6, 0, 0, 0 });
			Chunk(f, "IHDR", header.ToArray());
			Chunk(f, "IDAT", packed.ToArray());
			Chunk(f, "IEND", new byte[0]);
		}

		private static void Chunk(Stream s, string type, byte[] data)
		{
			Big(s, (uint)data.Length);
			byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
			s.Write(t);
			s.Write(data);
			uint crc = 0xffffffff;
			foreach (byte b in t)
			{
				crc = table[(crc ^ b) & 0xff] ^ (crc >> 8);
			}
			foreach (byte b in data)
			{
				crc = table[(crc ^ b) & 0xff] ^ (crc >> 8);
			}
			Big(s, crc ^ 0xffffffff);
		}

		private static void Big(Stream s, uint v)
		{
			s.Write(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
		}

		private static uint[] Table()
		{
			uint[] t = new uint[256];
			for (uint n = 0; n < 256; n++)
			{
				uint c = n;
				for (int k = 0; k < 8; k++)
				{
					c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
				}
				t[n] = c;
			}
			return t;
		}
	}
}
