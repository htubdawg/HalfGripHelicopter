using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// The mod's icon for the HalfGripMods installer (<c>Helicopter/icon.png</c>, named by <c>"icon"</c> in <c>mod.json</c>): the
	/// helicopter from the side as it looks in game (olive pod, blue glass nose and door window, engine housing, tail boom and fin,
	/// skids, dark rotors), drawn at 64 by 64 and saved at 2x, on a clear background like the other jMods icons. Only the installer
	/// shows it, so it is drawn here and not shipped in <c>src/</c>.
	///   dotnet run -c Release --project tools/preview -p:Refs=&lt;halfgriprefs&gt; -- --icon Helicopter/icon.png
	/// </summary>
	internal static class LoaderIcon
	{
		internal const int Size = 64;

		private static readonly Color32 Body = new Color32(104, 122, 64, 255);
		private static readonly Color32 BodyLight = new Color32(146, 164, 92, 255);
		private static readonly Color32 BodyDark = new Color32(66, 80, 42, 255);
		private static readonly Color32 Glass = new Color32(84, 146, 188, 255);
		private static readonly Color32 GlassLight = new Color32(168, 214, 232, 255);
		private static readonly Color32 GlassDark = new Color32(48, 92, 128, 255);

		internal static Canvas Draw()
		{
			// y counts up from the bottom, as in the game's textures. Each part is its own layer, shaded, then laid over the last.
			var pod = new Canvas(Size, Size);
			// Tail boom, thinning to the fin, and the fin leaning back.
			pod.Line(24, 28, 6, 31, 3, Body);
			pod.Line(24, 26, 10, 29, 3, Body);
			pod.Line(4, 30, 6, 38, 3, Body);
			// The pod: a rounded cabin, flatter belly, with the engine housing on top.
			pod.Ellipse(16, 13, 40, 28, Body);
			pod.Rect(22, 13, 24, 6, Body);
			pod.Rect(25, 38, 17, 6, Body);
			pod.Shade(BodyLight, BodyDark, Art.Black);

			var glass = new Canvas(Size, Size);
			// The bubble nose: the whole front of the pod, cut from the pod's own outline so the glass wraps round to the tip (an olive
			// roof above it, a belly strip under it), then the door window behind it.
			glass.Ellipse(16, 13, 40, 28, Glass);
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					if (x < 37 || y < 17 || y > 36)
					{
						glass.Pixels[y * Size + x] = default;
					}
				}
			}
			glass.Rect(26, 23, 8, 12, Glass);
			glass.Shade(GlassLight, GlassDark, Art.Black);
			// A glare streak on the nose.
			glass.Line(44, 33, 49, 26, 1, GlassLight);
			Over(pod, glass);
			// Door seam and a vent on the engine housing.
			pod.Rect(35, 19, 1, 18, BodyDark);
			pod.Rect(29, 40, 8, 1, BodyDark);

			var metal = new Canvas(Size, Size);
			// Skids: two struts down to a rail turned up at the front.
			metal.Rect(14, 6, 34, 2, Art.Dark);
			metal.Line(48, 7, 51, 10, 2, Art.Dark);
			metal.Line(24, 8, 26, 14, 2, Art.Dark);
			metal.Line(40, 8, 38, 14, 2, Art.Dark);
			// Mast up to the rotor hub.
			metal.Rect(32, 44, 3, 6, Art.Dark);
			metal.Shade(Art.Light, Art.Black, Art.Black);
			var under = new Canvas(Size, Size);
			Over(under, metal);
			Over(under, pod);
			// The rotors pass in front of the fin and over the housing, so they go on top again.
			var front = new Canvas(Size, Size);
			front.Rect(2, 51, 60, 2, Art.Dark);
			front.Rect(29, 50, 9, 4, Art.Grey);
			// Tail rotor on the fin, clear of the main rotor.
			front.Rect(1, 39, 11, 2, Art.Dark);
			front.Rect(5, 34, 2, 11, Art.Dark);
			front.Shade(Art.Light, Art.Black, Art.Black);
			Over(under, front);
			return under;
		}

		/// <summary>Lays the drawn (non-clear) pixels of <paramref name="top"/> over <paramref name="dst"/>.</summary>
		private static void Over(Canvas dst, Canvas top)
		{
			for (int i = 0; i < dst.Pixels.Length; i++)
			{
				if (top.Pixels[i].a > 0)
				{
					dst.Pixels[i] = top.Pixels[i];
				}
			}
		}
	}
}
