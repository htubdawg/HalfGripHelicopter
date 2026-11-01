using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// Draws the helicopter from <see cref="Shape"/> the way the game does in the ways that matter here: back faces culled (Unity's
	/// clockwise front faces), a depth buffer, the cockpit camera's field of view and near plane, flat lighting from one sun. Views from
	/// outside and from the driver's head, so a cabin that is see-through or a pod that is a ball shows before it reaches the game.
	/// </summary>
	internal static class Model
	{
		private const float Fov = 60.57f;
		private const float Near = 0.1f;

		/// <summary>A car like the game's (wheelbase 5.25, track 3.3, head at CarBodyPivot/Head), and a smaller one.</summary>
		internal static Shape.Frame Car(bool small)
		{
			Vector3 head = new Vector3(-0.33f, 2.02f, -0.21f);
			return small
				? Shape.Measure(5.2f, 2.6f, 0f, 0.2f, -0.1f, head)
				: Shape.Measure(8.0f, 3.6f, 0f, 0.1f, -0.1f, head);
		}

		internal static Color32[] View(Shape.Frame f, Vector3 eye, float yaw, float pitch, int w, int h, bool mirror)
		{
			Color32[] px = new Color32[w * h];
			float[] depth = new float[w * h];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float t = (float)y / h;
					px[y * w + x] = y < h / 2 ? new Color32(70, 120, 40, 255) : new Color32((byte)(150 - 40 * t), (byte)(180 - 40 * t), (byte)(215 - 30 * t), 255);
					depth[y * w + x] = float.MaxValue;
				}
			}
			Quaternion rot = Shape.AboutY(yaw) * Shape.AboutX(pitch);
			Quaternion inv = new Quaternion(-rot.x, -rot.y, -rot.z, rot.w);
			float focal = 0.5f * h / Mathf.Tan(0.5f * Fov * Mathf.Deg2Rad);
			Vector3 sun = new Vector3(0.4f, 0.8f, 0.45f).normalized;
			List<Piece> pieces = Shape.Build(f);
			// Opaque first, then the see-through panes over them (no depth write), as the game's transparent queue does.
			for (int pass = 0; pass < 2; pass++)
			{
				foreach (Piece piece in pieces)
				{
					if (mirror && (piece.Name == "Cabin" || piece.Name == "Panes"))
					{
						continue;
					}
					foreach (KeyValuePair<Paint, List<int>> kv in piece.Triangles)
					{
						bool pane = kv.Key == Paint.Pane;
						if (pane != (pass == 1))
						{
							continue;
						}
						Tex tex = TexOf(kv.Key);
						Color c = Colour(kv.Key);
						List<int> tris = kv.Value;
						for (int i = 0; i < tris.Count; i += 3)
						{
							int ia = tris[i], ib = tris[i + 1], ic = tris[i + 2];
							Vector3 a = piece.Vertices[ia] + piece.Pivot;
							Vector3 b = piece.Vertices[ib] + piece.Pivot;
							Vector3 cc = piece.Vertices[ic] + piece.Pivot;
							Vector3 n = Vector3.Cross(b - a, cc - a);
							// Unity's front face: the cross product of a clockwise triangle points at the viewer. The sprite shader
							// of the panes draws both sides.
							if (!pane && Vector3.Dot(n, eye - a) <= 0f)
							{
								continue;
							}
							float light = pane ? 1f : 0.45f + 0.55f * Mathf.Max(0f, Vector3.Dot(n.normalized, sun));
							Shade shade = new Shade { Colour = c, Light = pane ? 1f : light * 1.6f, Tex = tex, Blend = pane };
							Raster(px, depth, w, h, focal, inv * (a - eye), inv * (b - eye), inv * (cc - eye), piece.Uvs[ia], piece.Uvs[ib], piece.Uvs[ic], shade);
						}
					}
				}
			}
			return px;
		}

		private sealed class Tex
		{
			internal Color32[] Px;
			internal int W, H;
			internal bool Clamp;
		}

		private struct Shade
		{
			internal Color Colour;
			internal float Light;
			internal Tex Tex;
			internal bool Blend;
		}

		private static readonly Dictionary<Paint, Tex> textures = new Dictionary<Paint, Tex>();

		private static Tex TexOf(Paint p)
		{
			if (textures.TryGetValue(p, out Tex t))
			{
				return t;
			}
			int w, h;
			switch (p)
			{
				case Paint.Wall:
				case Paint.Floor:
				case Paint.Seat:
					t = new Tex { Px = Shape.Speckle(out w), W = w, H = w };
					break;
				case Paint.Panel:
					t = new Tex { Px = Shape.PanelPixels(Car(false), out w, out h), W = w, H = h, Clamp = true };
					break;
				case Paint.Pane:
					t = new Tex { Px = Shape.GlassPixels(out w), W = w, H = w };
					break;
			}
			textures[p] = t;
			return t;
		}

		private static Color Colour(Paint p)
		{
			switch (p)
			{
				case Paint.Glass: return new Color(0.22f, 0.36f, 0.44f);
				case Paint.Metal: return new Color(0.14f, 0.15f, 0.16f);
				case Paint.Wall: return new Color(0.46f, 0.47f, 0.42f);
				case Paint.Floor: return new Color(0.2f, 0.2f, 0.19f);
				case Paint.Seat: return new Color(0.33f, 0.24f, 0.17f);
				case Paint.Panel:
				case Paint.Pane: return Color.white;
				default: return new Color(0.30f, 0.36f, 0.22f);
			}
		}

		/// <summary>One triangle in camera space (z forward), clipped at the near plane, filled with a depth test.</summary>
		private static void Raster(Color32[] px, float[] depth, int w, int h, float focal, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Shade shade)
		{
			var poly = new List<(Vector3 p, Vector2 uv)> { (a, ua), (b, ub), (c, uc) };
			var clipped = new List<(Vector3 p, Vector2 uv)>();
			for (int i = 0; i < poly.Count; i++)
			{
				var p = poly[i];
				var q = poly[(i + 1) % poly.Count];
				bool pin = p.p.z >= Near, qin = q.p.z >= Near;
				if (pin)
				{
					clipped.Add(p);
				}
				if (pin != qin)
				{
					float t = (Near - p.p.z) / (q.p.z - p.p.z);
					clipped.Add((Vector3.Lerp(p.p, q.p, t), Vector2.Lerp(p.uv, q.uv, t)));
				}
			}
			if (clipped.Count < 3)
			{
				return;
			}
			var s = new (Vector3 p, Vector2 uv)[clipped.Count];
			for (int i = 0; i < s.Length; i++)
			{
				Vector3 p = clipped[i].p;
				s[i] = (new Vector3(0.5f * w + focal * p.x / p.z, 0.5f * h + focal * p.y / p.z, p.z), clipped[i].uv);
			}
			for (int i = 1; i + 1 < s.Length; i++)
			{
				Fill(px, depth, w, h, s[0], s[i], s[i + 1], shade);
			}
		}

		private static void Fill(Color32[] px, float[] depth, int w, int h, (Vector3 p, Vector2 uv) A, (Vector3 p, Vector2 uv) B, (Vector3 p, Vector2 uv) C, Shade shade)
		{
			Vector3 a = A.p, b = B.p, c = C.p;
			int x0 = Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
			int x1 = Math.Min(w - 1, (int)Mathf.Ceil(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
			int y0 = Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
			int y1 = Math.Min(h - 1, (int)Mathf.Ceil(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
			float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
			if (Mathf.Abs(area) < 1e-6f)
			{
				return;
			}
			for (int y = y0; y <= y1; y++)
			{
				for (int x = x0; x <= x1; x++)
				{
					float px0 = x + 0.5f, py0 = y + 0.5f;
					float w0 = ((b.x - px0) * (c.y - py0) - (c.x - px0) * (b.y - py0)) / area;
					float w1 = ((c.x - px0) * (a.y - py0) - (a.x - px0) * (c.y - py0)) / area;
					float w2 = 1f - w0 - w1;
					if (w0 < 0f || w1 < 0f || w2 < 0f)
					{
						continue;
					}
					float iz = w0 / a.z + w1 / b.z + w2 / c.z;
					float z = 1f / iz;
					int k = y * w + x;
					if (z >= depth[k])
					{
						continue;
					}
					Color col = shade.Colour;
					float alpha = 1f;
					if (shade.Tex != null)
					{
						Vector2 uv = (A.uv * (w0 / a.z) + B.uv * (w1 / b.z) + C.uv * (w2 / c.z)) / iz;
						Tex t = shade.Tex;
						float u = shade.Tex.Clamp ? Mathf.Clamp01(uv.x) : uv.x - Mathf.Floor(uv.x);
						float v = shade.Tex.Clamp ? Mathf.Clamp01(uv.y) : uv.y - Mathf.Floor(uv.y);
						Color32 tc = t.Px[Math.Min(t.H - 1, (int)(v * t.H)) * t.W + Math.Min(t.W - 1, (int)(u * t.W))];
						col = new Color(col.r * tc.r / 255f, col.g * tc.g / 255f, col.b * tc.b / 255f);
						alpha = tc.a / 255f;
					}
					Color lit = new Color(Mathf.Clamp01(col.r * shade.Light), Mathf.Clamp01(col.g * shade.Light), Mathf.Clamp01(col.b * shade.Light));
					if (shade.Blend)
					{
						Color32 under = px[k];
						px[k] = new Color32((byte)Mathf.Lerp(under.r, 255f * lit.r, alpha), (byte)Mathf.Lerp(under.g, 255f * lit.g, alpha), (byte)Mathf.Lerp(under.b, 255f * lit.b, alpha), 255);
					}
					else
					{
						depth[k] = z;
						px[k] = new Color32((byte)(255f * lit.r), (byte)(255f * lit.g), (byte)(255f * lit.b), 255);
					}
				}
			}
		}

		/// <summary>Two large views of the game's car: from the seat ahead, and the nose from front-left.</summary>
		internal static void Close(string path)
		{
			const int w = 640, h = 360;
			Shape.Frame f = Car(false);
			Color32[] seat = View(f, f.Head, 0f, 6f, w, h, false);
			Vector3 nose = new Vector3(0f, f.CentreY, f.NoseZ - 0.2f * f.PodLength);
			Color32[] front = View(f, nose + new Vector3(-2.2f, 1.2f, 3.2f), 145f, 18f, w, h, false);
			Color32[] sheet = new Color32[w * 2 * h];
			for (int y = 0; y < h; y++)
			{
				Array.Copy(seat, y * w, sheet, y * 2 * w, w);
				Array.Copy(front, y * w, sheet, y * 2 * w + w, w);
			}
			Png.Save(path, sheet, 2 * w, h, 1);
			Console.WriteLine("wrote " + path);
		}

		/// <summary>The sheet: outside from the side, from front-left above and from behind; then from the seat ahead, left and right,
		/// and the mirror's view back. Top row the game's car, bottom row a smaller car.</summary>
		internal static void Sheet(string path)
		{
			const int w = 320, h = 180;
			int cols = 4, rows = 4;
			Color32[] sheet = new Color32[w * cols * h * rows];
			for (int r = 0; r < 2; r++)
			{
				Shape.Frame f = Car(r == 1);
				Vector3 mid = new Vector3(0f, f.CentreY, f.NoseZ - 0.6f * f.PodLength);
				float d = 1.15f * f.Length;
				var views = new List<(Vector3 eye, float yaw, float pitch, bool mirror)>
				{
					(mid + new Vector3(d, 0.1f * d, 0f), -90f, 5f, false),
					(mid + new Vector3(-0.6f * d, 0.45f * d, 0.7f * d), 140f, 22f, false),
					(mid + new Vector3(0.35f * d, 0.2f * d, -0.9f * d), -20f, 10f, false),
					(mid + new Vector3(0f, 0.9f * d, 0.3f * d), 180f, 70f, false),
					(f.Head, 0f, 6f, false),
					(f.Head, -60f, 6f, false),
					(f.Head, 60f, 6f, false),
					(f.Head + new Vector3(0.33f, 0.1f, 0.1f), 180f, 4f, true),
				};
				for (int v = 0; v < views.Count; v++)
				{
					var view = views[v];
					Color32[] img = View(f, view.eye, view.yaw, view.pitch, w, h, view.mirror);
					int col = v % cols;
					int row = rows - 1 - (2 * r + v / cols);
					for (int y = 0; y < h; y++)
					{
						Array.Copy(img, y * w, sheet, (row * h + y) * w * cols + col * w, w);
					}
				}
				Console.WriteLine((r == 0 ? "game car" : "small car") + ": pod " + f.PodLength.ToString("0.00") + " x " + f.PodHeight.ToString("0.00") + " x " + f.PodWidth.ToString("0.00") + ", belly " + (f.CentreY - 0.5f * f.PodHeight - f.Ground).ToString("0.00") + " off the ground, head at " + ((f.Head.y - (f.CentreY - 0.5f * f.PodHeight)) / f.PodHeight * 100f).ToString("0") + "% of the height");
			}
			Png.Save(path, sheet, w * cols, h * rows, 2);
			Console.WriteLine("wrote " + path);
		}
	}
}
