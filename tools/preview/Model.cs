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
			foreach (Piece piece in Shape.Build(f))
			{
				if (mirror && piece.Name == "Cabin")
				{
					continue;
				}
				foreach (KeyValuePair<Paint, List<int>> kv in piece.Triangles)
				{
					Color c = Colour(kv.Key);
					List<int> tris = kv.Value;
					for (int i = 0; i < tris.Count; i += 3)
					{
						Vector3 a = piece.Vertices[tris[i]] + piece.Pivot;
						Vector3 b = piece.Vertices[tris[i + 1]] + piece.Pivot;
						Vector3 cc = piece.Vertices[tris[i + 2]] + piece.Pivot;
						Vector3 n = Vector3.Cross(b - a, cc - a);
						// Unity's front face: the cross product of a clockwise triangle points at the viewer.
						if (Vector3.Dot(n, eye - a) <= 0f)
						{
							continue;
						}
						float light = 0.45f + 0.55f * Mathf.Max(0f, Vector3.Dot(n.normalized, sun));
						Color32 col = new Color32((byte)(255 * Mathf.Clamp01(c.r * light * 1.6f)), (byte)(255 * Mathf.Clamp01(c.g * light * 1.6f)), (byte)(255 * Mathf.Clamp01(c.b * light * 1.6f)), 255);
						Raster(px, depth, w, h, focal, inv * (a - eye), inv * (b - eye), inv * (cc - eye), col);
					}
				}
			}
			return px;
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
				default: return new Color(0.30f, 0.36f, 0.22f);
			}
		}

		/// <summary>One triangle in camera space (z forward), clipped at the near plane, filled with a depth test.</summary>
		private static void Raster(Color32[] px, float[] depth, int w, int h, float focal, Vector3 a, Vector3 b, Vector3 c, Color32 col)
		{
			List<Vector3> poly = new List<Vector3> { a, b, c };
			List<Vector3> clipped = new List<Vector3>();
			for (int i = 0; i < poly.Count; i++)
			{
				Vector3 p = poly[i], q = poly[(i + 1) % poly.Count];
				bool pin = p.z >= Near, qin = q.z >= Near;
				if (pin)
				{
					clipped.Add(p);
				}
				if (pin != qin)
				{
					float t = (Near - p.z) / (q.z - p.z);
					clipped.Add(Vector3.Lerp(p, q, t));
				}
			}
			if (clipped.Count < 3)
			{
				return;
			}
			Vector3[] s = new Vector3[clipped.Count];
			for (int i = 0; i < s.Length; i++)
			{
				Vector3 p = clipped[i];
				s[i] = new Vector3(0.5f * w + focal * p.x / p.z, 0.5f * h + focal * p.y / p.z, p.z);
			}
			for (int i = 1; i + 1 < s.Length; i++)
			{
				Fill(px, depth, w, h, s[0], s[i], s[i + 1], col);
			}
		}

		private static void Fill(Color32[] px, float[] depth, int w, int h, Vector3 a, Vector3 b, Vector3 c, Color32 col)
		{
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
					float z = 1f / (w0 / a.z + w1 / b.z + w2 / c.z);
					int k = y * w + x;
					if (z < depth[k])
					{
						depth[k] = z;
						px[k] = col;
					}
				}
			}
		}

		/// <summary>The sheet: outside from the side, from front-left above and from behind; then from the seat ahead, left and right,
		/// and the mirror's view back. Top row the game's car, bottom row a smaller car.</summary>
		internal static void Sheet(string path)
		{
			const int w = 240, h = 150;
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
