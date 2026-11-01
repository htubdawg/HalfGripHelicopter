using System;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// One sheet of the mod's pictures, from its own drawing code: the shop icon at the game's body-card size, then a mock of the
	/// cockpit cut off by the screen's bottom edge, as in game (stick at rest, pushed forward-left and held, its boot on the bottom edge;
	/// the levers' console running off the bottom, collective at the hover mark, throttle full). Exits 1 if the icon is blank.
	///   dotnet run -c Release --project tools/preview -- out.png
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
			if (args.Length > 0 && args[0] == "--close")
			{
				Model.Close(args.Length > 1 ? args[1] : "close.png");
				return 0;
			}
			string path = args.Length > 0 ? args[0] : "preview.png";
			Canvas sheet = new Canvas(220, 110);
			sheet.Rect(0, 0, sheet.Width, sheet.Height, new Color32(58, 52, 50, 255));

			Canvas icon = Art.Icon(43, 13);
			int filled = 0;
			foreach (Color32 c in icon.Pixels)
			{
				filled += c.a > 0 ? 1 : 0;
			}
			Blit(sheet, icon, 4, 92);

			// Row 0 is the screen's bottom edge: the boots' middles sit on it, the console runs below it.
			int length = 60;
			Stick(sheet, 30, 0, length, 0, 0, false);
			Stick(sheet, 80, 0, length, -12, 12, true);
			int gap = 10, up = 44 / 2 + 7, down = 50 + 24;
			Canvas console = Art.Console(2 * gap + 26, up + down, gap, up);
			Blit(sheet, console, 160 - console.Width / 2, 50 - down);
			Lever(sheet, 160 - gap, 50, 0.5f, Art.Yellow, true);
			Lever(sheet, 160 + gap, 50, 1f, Art.Red, false);

			Png.Save(path, sheet.Pixels, sheet.Width, sheet.Height, 4);
			Console.WriteLine("wrote " + path + ", icon " + filled + " of " + icon.Pixels.Length + " pixels drawn");
			return filled == 0 ? 1 : 0;
		}

		/// <summary>The stick as the cockpit draws it: boot at the foot, the shaft in whole pixels, the grip at (dx, dy) from rest.</summary>
		private static void Stick(Canvas sheet, int footX, int footY, int length, int dx, int dy, bool locked)
		{
			Canvas boot = Art.Boot();
			Blit(sheet, boot, footX - boot.Width / 2, footY - boot.Height / 2);
			int gate = 12;
			Canvas shaft = new Canvas(2 * gate + 9, length + gate + 4);
			int ox = shaft.Width / 2;
			shaft.Line(ox, 2, ox + dx, Math.Clamp(length + dy, 2, shaft.Height - 2), 3, Art.Grey);
			shaft.Shade(Art.Light, Art.Dark, Art.Black);
			Blit(sheet, shaft, footX - ox, footY);
			Canvas grip = Art.Grip(locked);
			Blit(sheet, grip, footX + dx - grip.Width / 2, footY + length + dy - Mathf.RoundToInt(grip.Height * 0.1f));
		}

		private static void Lever(Canvas sheet, int x, int y, float value, Color32 colour, bool banded)
		{
			int slotLength = 44, travel = 36;
			Canvas slot = Art.Slot(slotLength, banded ? Art.Yellow : Art.Grey);
			Blit(sheet, slot, x - slot.Width / 2, y - slotLength / 2);
			Canvas handle = Art.Handle(colour, banded);
			int hy = y - travel / 2 + Mathf.RoundToInt(value * travel);
			Blit(sheet, handle, x - handle.Width / 2, hy - handle.Height / 2);
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
