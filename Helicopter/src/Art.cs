using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// A small pixel canvas and the mod's pictures, drawn in code from flat palette colours so they match the game's pixel art (hard edges,
	/// no blending) and need no files. Row 0 is the bottom, as in a Unity texture.
	/// </summary>
	internal sealed class Canvas
	{
		internal readonly int Width;
		internal readonly int Height;
		internal readonly Color32[] Pixels;

		internal Canvas(int width, int height)
		{
			Width = width;
			Height = height;
			Pixels = new Color32[width * height];
		}

		internal void Clear()
		{
			for (int i = 0; i < Pixels.Length; i++)
			{
				Pixels[i] = default;
			}
		}

		internal void Set(int x, int y, Color32 c)
		{
			if (x >= 0 && y >= 0 && x < Width && y < Height)
			{
				Pixels[y * Width + x] = c;
			}
		}

		internal Color32 Get(int x, int y)
		{
			return x >= 0 && y >= 0 && x < Width && y < Height ? Pixels[y * Width + x] : default;
		}

		internal void Rect(int x0, int y0, int w, int h, Color32 c)
		{
			for (int y = y0; y < y0 + h; y++)
			{
				for (int x = x0; x < x0 + w; x++)
				{
					Set(x, y, c);
				}
			}
		}

		/// <summary>A filled ellipse inside the box, tested at pixel centres.</summary>
		internal void Ellipse(int x0, int y0, int w, int h, Color32 c)
		{
			float rx = w * 0.5f;
			float ry = h * 0.5f;
			for (int y = y0; y < y0 + h; y++)
			{
				for (int x = x0; x < x0 + w; x++)
				{
					float dx = (x + 0.5f - x0 - rx) / rx;
					float dy = (y + 0.5f - y0 - ry) / ry;
					if (dx * dx + dy * dy <= 1f)
					{
						Set(x, y, c);
					}
				}
			}
		}

		/// <summary>A line <paramref name="thick"/> pixels across (Bresenham, a square brush).</summary>
		internal void Line(int x0, int y0, int x1, int y1, int thick, Color32 c)
		{
			int dx = Mathf.Abs(x1 - x0);
			int dy = -Mathf.Abs(y1 - y0);
			int sx = x0 < x1 ? 1 : -1;
			int sy = y0 < y1 ? 1 : -1;
			int err = dx + dy;
			int lo = -(thick - 1) / 2;
			while (true)
			{
				Rect(x0 + lo, y0 + lo, thick, thick, c);
				if (x0 == x1 && y0 == y1)
				{
					break;
				}
				int e2 = 2 * err;
				if (e2 >= dy)
				{
					err += dy;
					x0 += sx;
				}
				if (e2 <= dx)
				{
					err += dx;
					y0 += sy;
				}
			}
		}

		/// <summary>
		/// Shades what is drawn the way the game's dash parts are lit, from the top left: a light pixel where the one up-left of it is
		/// empty, a dark one where the one down-right is, and a black outline round the shape.
		/// </summary>
		internal void Shade(Color32 light, Color32 dark, Color32 outline)
		{
			Color32[] src = (Color32[])Pixels.Clone();
			bool Filled(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && src[y * Width + x].a > 0;
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					if (Filled(x, y))
					{
						if (!Filled(x - 1, y + 1) || !Filled(x, y + 1))
						{
							Pixels[y * Width + x] = Mix(src[y * Width + x], light);
						}
						else if (!Filled(x + 1, y - 1) || !Filled(x, y - 1))
						{
							Pixels[y * Width + x] = Mix(src[y * Width + x], dark);
						}
						continue;
					}
					if (Filled(x - 1, y) || Filled(x + 1, y) || Filled(x, y - 1) || Filled(x, y + 1))
					{
						Pixels[y * Width + x] = outline;
					}
				}
			}
		}

		/// <summary>Half way between two palette colours, snapped to the nearer of the two plus the step (keeps a flat look).</summary>
		private static Color32 Mix(Color32 a, Color32 b)
		{
			return new Color32((byte)((a.r + b.r) / 2), (byte)((a.g + b.g) / 2), (byte)((a.b + b.b) / 2), 255);
		}

		internal Texture2D Texture(string name)
		{
			Texture2D t = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
			t.name = name;
			t.filterMode = FilterMode.Point;
			t.wrapMode = TextureWrapMode.Clamp;
			t.hideFlags = HideFlags.HideAndDontSave;
			t.SetPixels32(Pixels);
			t.Apply(false, false);
			return t;
		}
	}

	/// <summary>The mod's pictures: the shop icon and the cockpit's parts.</summary>
	internal static class Art
	{
		internal static readonly Color32 Black = new Color32(20, 20, 24, 255);
		internal static readonly Color32 Dark = new Color32(48, 50, 56, 255);
		internal static readonly Color32 Grey = new Color32(96, 100, 108, 255);
		internal static readonly Color32 Light = new Color32(170, 174, 182, 255);
		internal static readonly Color32 White = new Color32(236, 236, 230, 255);
		internal static readonly Color32 Red = new Color32(196, 44, 40, 255);
		internal static readonly Color32 Yellow = new Color32(232, 184, 40, 255);
		internal static readonly Color32 Olive = new Color32(84, 100, 60, 255);
		internal static readonly Color32 Glass = new Color32(120, 176, 200, 255);
		internal static readonly Color32 Green = new Color32(72, 180, 80, 255);

		/// <summary>
		/// The shop icon: a helicopter from the side in olive, filling a <paramref name="w"/> by <paramref name="h"/> card picture (the
		/// chassis item it copies decides the size, so it lines up like the game's own body pictures).
		/// </summary>
		internal static Canvas Icon(int w, int h)
		{
			Canvas c = new Canvas(w, h);
			// Proportions as shares of the picture, so any card size gets the same helicopter.
			int bodyW = Mathf.Max(6, Mathf.RoundToInt(w * 0.48f));
			int bodyH = Mathf.Max(4, Mathf.RoundToInt(h * 0.62f));
			int bodyX = Mathf.RoundToInt(w * 0.5f);
			int skidY = 1;
			int bodyY = skidY + Mathf.Max(2, Mathf.RoundToInt(h * 0.15f));
			int mastTop = Mathf.Min(h - 2, bodyY + bodyH + Mathf.Max(1, h / 10));
			// Tail boom to the left, tail fin at its end.
			int boomY = bodyY + bodyH / 2;
			int boomH = Mathf.Max(1, bodyH / 4);
			c.Rect(2, boomY, bodyX - 2, boomH, Olive);
			c.Rect(1, boomY, Mathf.Max(2, w / 20), Mathf.Max(3, bodyH * 3 / 4), Olive);
			// Body and cabin glass.
			c.Ellipse(bodyX - bodyW / 4, bodyY, bodyW, bodyH, Olive);
			c.Ellipse(bodyX + bodyW / 4, bodyY + bodyH / 3, bodyW / 2, bodyH * 2 / 3, Glass);
			c.Shade(Light, Dark, Black);
			// Mast and the main rotor across the top, the skids under it.
			int mastX = bodyX + bodyW / 4;
			c.Rect(mastX, bodyY + bodyH - 1, 1, mastTop - (bodyY + bodyH - 1), Black);
			c.Rect(Mathf.RoundToInt(w * 0.12f), mastTop, Mathf.RoundToInt(w * 0.8f), 1, Black);
			c.Rect(bodyX - bodyW / 4 + 1, skidY, bodyW, 1, Black);
			c.Rect(bodyX, skidY, 1, bodyY - skidY, Black);
			c.Rect(bodyX + bodyW / 2, skidY, 1, bodyY - skidY, Black);
			return c;
		}

		/// <summary>The stick's grip: a black grip with a trigger, the top button red while the stick is held where it is (locked).</summary>
		internal static Canvas Grip(bool locked)
		{
			Canvas c = new Canvas(12, 16);
			c.Rect(3, 0, 6, 12, Dark);
			c.Ellipse(2, 7, 8, 8, Dark);
			c.Rect(8, 6, 2, 3, Grey);
			c.Shade(Grey, Black, Black);
			c.Rect(5, 12, 2, 2, locked ? Red : Grey);
			c.Rect(4, 8, 1, 3, Black);
			return c;
		}

		/// <summary>The rubber boot at the stick's foot.</summary>
		internal static Canvas Boot()
		{
			Canvas c = new Canvas(20, 9);
			c.Ellipse(1, 1, 18, 7, Dark);
			c.Shade(Grey, Black, Black);
			c.Ellipse(7, 3, 6, 3, Black);
			return c;
		}

		/// <summary>The slot a lever runs in, <paramref name="length"/> pixels long.</summary>
		internal static Canvas Slot(int length, Color32 mark)
		{
			Canvas c = new Canvas(8, length);
			c.Rect(1, 1, 6, length - 2, Grey);
			c.Shade(Light, Dark, Black);
			c.Rect(3, 2, 2, length - 4, Black);
			// Ticks every quarter, the hover mark (half way) in the lever's colour.
			for (int i = 1; i < 4; i++)
			{
				int y = 2 + (length - 4) * i / 4;
				c.Set(1, y, i == 2 ? mark : Dark);
				c.Set(6, y, i == 2 ? mark : Dark);
			}
			return c;
		}

		/// <summary>A lever's handle: a bar in <paramref name="colour"/> across its slot.</summary>
		internal static Canvas Handle(Color32 colour, bool banded)
		{
			Canvas c = new Canvas(16, 8);
			c.Rect(1, 1, 14, 6, colour);
			if (banded)
			{
				for (int x = 2; x < 15; x += 4)
				{
					c.Rect(x, 1, 2, 6, Black);
				}
			}
			c.Shade(Light, Dark, Black);
			return c;
		}
	}
}
