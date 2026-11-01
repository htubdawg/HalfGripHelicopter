using System;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// One sheet of the mod's pictures, from its own drawing code: the shop icon at the game's body-card size, then a mock of the
	/// cockpit cut off by the screen's bottom edge, as in game (stick at rest, pushed forward-left and held, its boot on the bottom edge;
	/// the levers' console running off the bottom, collective at the hover mark, throttle full). Exits 1 if the icon is blank.
	///   dotnet run -c Release --project tools/preview -- out.png
	/// (or -- --clear out.png: transparent, one pixel per dash pixel, for laying over a screenshot);
	/// the instrument panel's face as first drawn: -- --panel panel.png; as fitted in the owner's 0.1.7 log with the MiniMap's GPS on the
	/// right (on its plate): -- --fitted fitted.png;
	/// and the helicopter's shape from outside and from the seat (<see cref="Model"/>):
	///   dotnet run -c Release --project tools/preview -- --model model.png
	/// </summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			if (args.Length > 0 && args[0] == "--model")
			{
				Model.Sheet(args.Length > 1 ? args[1] : "model.png");
				return 0;
			}
			if (args.Length > 0 && args[0] == "--panel")
			{
				// The panel's face as first drawn (the guessed layout), at the game's car's size, 3 screen pixels per pixel.
				Color32[] px = Shape.PanelPixels(Model.Car(false), out int w, out int h);
				Png.Save(args.Length > 1 ? args[1] : "panel.png", px, w, h, 3);
				Console.WriteLine("wrote panel " + w + "x" + h);
				return 0;
			}
			if (args.Length > 0 && args[0] == "--fitted")
			{
				// The owner's 0.1.7 fit (258x159, seen from 0.57, stick and console zones) and a GPS where the owner's 0.2.0 screenshot has it.
				var zones = new System.Collections.Generic.List<Panel.Zone>
				{
					new Panel.Zone(-0.10f, 0f, 0.17f, 1f, false),
					new Panel.Zone(0.43f, 0f, 0.79f, 1f, true),
					new Panel.Zone(0.815f, 0.64f, 0.995f, 0.98f, false, true),
				};
				Canvas face = Panel.Draw(258, 159, 0.57f, zones);
				// The GPS's mounting plate over the face where the GPS stands, with a stand-in casing (grey box, dark screen) at the size the
				// owner's screenshot shows it (about 34 x 30 dash pixels).
				const int gw = 34, gh = 30;
				// The casing's shape, its corners cut, as the plate's hole.
				bool[] hole = new bool[gw * gh];
				for (int y = 0; y < gh; y++)
				{
					for (int x = 0; x < gw; x++)
					{
						int dx = System.Math.Min(x, gw - 1 - x), dy = System.Math.Min(y, gh - 1 - y);
						hole[y * gw + x] = dx + dy >= 2;
					}
				}
				Canvas mount = Art.Mount(gw, gh, 5, 4, hole);
				int mx = 217 - 6, my = 112 - 10;
				for (int y = 0; y < mount.Height; y++)
				{
					for (int x = 0; x < mount.Width; x++)
					{
						Color32 p = mount.Get(x, y);
						if (p.a > 0)
						{
							face.Set(mx + x, my + y, p);
						}
					}
				}
				for (int y = 0; y < gh; y++)
				{
					for (int x = 0; x < gw; x++)
					{
						if (hole[y * gw + x])
						{
							bool inner = x >= 3 && y >= 3 && x < gw - 3 && y < gh - 3;
							face.Set(mx + 6 + x, my + 10 + y, inner ? new Color32(60, 120, 60, 255) : Art.Grey);
						}
					}
				}
				Png.Save(args.Length > 1 ? args[1] : "fitted.png", face.Pixels, 258, 159, 3);
				Console.WriteLine("wrote fitted panel 258x159");
				return 0;
			}
			if (args.Length > 0 && args[0] == "--close")
			{
				Model.Close(args.Length > 1 ? args[1] : "close.png");
				return 0;
			}
			// --clear: the same sheet on a transparent background, one pixel per dash pixel, to lay over a screenshot.
			bool clear = args.Length > 0 && args[0] == "--clear";
			string path = clear ? (args.Length > 1 ? args[1] : "clear.png") : args.Length > 0 ? args[0] : "preview.png";
			Canvas sheet = new Canvas(230, 130);
			if (!clear)
			{
				sheet.Rect(0, 0, sheet.Width, sheet.Height, new Color32(58, 52, 50, 255));
			}

			Canvas icon = Art.Icon(43, 13);
			int filled = 0;
			foreach (Color32 c in icon.Pixels)
			{
				filled += c.a > 0 ? 1 : 0;
			}
			Blit(sheet, icon, 4, 112);

			// Row 0 is the screen's bottom edge: the stick's foot is out of sight below it, the gate's front edge just under it.
			int length = 80;
			Stick(sheet, 40, -20, length, 0, 0, false);
			Stick(sheet, 100, -20, length, -16, 16, true);
			// The gate as fitted to the owner's 0.1.7 screenshot: the console's lines lean left going up.
			int gap = 13, slot0 = 15, travel = 36;
			float sl = -0.12f, sr = -0.08f;
			Canvas gate = Art.Gate(66, 62, 8, gap, slot0, travel, sl, sr);
			int gx = 175 - gate.Width / 2, gy = -4 - 1;
			Blit(sheet, gate, gx, gy);
			Lever(sheet, 175 - gap, gy + slot0, travel, 0.5f, true, Art.SlotSlope(66, -gap, sl, sr));
			Lever(sheet, 175 + gap, gy + slot0, travel, 1f, false, Art.SlotSlope(66, gap, sl, sr));

			Png.Save(path, sheet.Pixels, sheet.Width, sheet.Height, clear ? 1 : 4);
			Console.WriteLine("wrote " + path + ", icon " + filled + " of " + icon.Pixels.Length + " pixels drawn");
			return filled == 0 ? 1 : 0;
		}

		/// <summary>The stick as the cockpit draws it: boot at the foot, the shaft in whole pixels, the grip at (dx, dy) from rest.</summary>
		private static void Stick(Canvas sheet, int footX, int footY, int length, int dx, int dy, bool locked)
		{
			Canvas boot = Art.Boot();
			Blit(sheet, boot, footX - boot.Width / 2, footY - boot.Height / 2);
			int gate = 16, thick = 6;
			Canvas shaft = new Canvas(2 * gate + thick + 6, length + gate + 4);
			int ox = shaft.Width / 2;
			Art.Shaft(shaft, ox, 2, ox + dx, Math.Clamp(length + dy, 2, shaft.Height - 4), thick);
			Blit(sheet, shaft, footX - ox, footY - 2);
			Canvas grip = Art.Grip(locked);
			Blit(sheet, grip, footX + dx - grip.Width / 2, footY + length + dy - 2);
		}

		/// <summary>A lever with its collar at value along its slot, which leans <paramref name="slope"/> pixels across per row.</summary>
		private static void Lever(Canvas sheet, int x, int y0, int travel, float value, bool collective, float slope)
		{
			Canvas art = Art.Lever(collective, collective ? -1 : 1);
			int dy = Mathf.RoundToInt(value * travel);
			Blit(sheet, art, x + Mathf.RoundToInt(slope * dy) - art.Width / 2, y0 + dy - 2);
		}

		private static void Blit(Canvas into, Canvas art, int x0, int y0)
		{
			for (int y = 0; y < art.Height; y++)
			{
				for (int x = 0; x < art.Width; x++)
				{
					Color32 c = art.Get(x, y);
					if (c.a > 0)
					{
						into.Set(x0 + x, y0 + y, c);
					}
				}
			}
		}
	}
}
