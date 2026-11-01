using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace HalfGripHelicopter
{
	/// <summary>
	/// The helicopter's cockpit: the steering wheel, the shifters and the handbrake hidden (their renderers and hand colliders off, the
	/// objects left alive because <see cref="CarLink"/> and the Open World read them), and in their place:
	/// <list type="bullet">
	/// <item>the <b>stick</b> (cyclic) where the wheel was: the game's own two-axis gate (<see cref="DoubleLinearObject"/>, what the
	/// H-shifter is made of) that follows the hand while held and springs back to the middle when let go; a right click holds it where
	/// it is when let go (the spring off; the hand still moves it) and lets it go again;</item>
	/// <item>the <b>collective</b> and the <b>throttle</b> where the shifter was: the game's own sliding lever (<see cref="LinearObject"/>,
	/// what the nitro and the handbrake are made of) with no spring, so each stays where the hand leaves it.</item>
	/// </list>
	/// The hand holds one thing at a time, as in the car: set the throttle and the collective, then take the stick.
	///
	/// The controls are drawn so they look fixed to the helicopter and in the car's own style: a big stick standing up from below the
	/// screen's bottom edge (pivoting there), and the levers standing up out of a speckled grey gate plate seen in perspective, like the
	/// car's H-shifter. The car's own dash read-outs that
	/// mean nothing in the air (the speed and rev dials, the gear and grip box, the rear-view mirror) are hidden with the controls; the
	/// board at the top right stays (the Open World uses it for its events).
	///
	/// Everything is placed and sized from the parts it replaces (their drawn bounds and the dash's pixel size), so it fits any car's
	/// cockpit, and drawn in the dash's own lit sprite material, a copy per texture (the Lit shader reads the material's _BaseMap).
	/// </summary>
	internal sealed class Cockpit : MonoBehaviour
	{
		internal static Cockpit Instance;

		internal DoubleLinearObject Cyclic;
		internal LinearObject Collective;
		internal LinearObject Throttle;

		/// <summary>The stick's deflection, -1..1 each way: x to the right, y forward (up on the dash).</summary>
		internal Vector2 Stick => Cyclic != null ? new Vector2(Cyclic.value.x * 2f - 1f, Cyclic.value.y * 2f - 1f) : Vector2.zero;

		/// <summary>The camera that draws the dash, or null.</summary>
		internal Camera View { get; private set; }

		/// <summary>The dash's pixel size, in the interior's space.</summary>
		internal float Texel => texel;

		/// <summary>
		/// The parts of the screen the controls can cover, as boxes (min, max corners) in the interior's space: the stick's grip over its
		/// whole swing, the shaft below it, and the levers with their gate (Console). <see cref="Airframe.FitPanel"/> keeps the instrument
		/// panel's face clear behind them.
		/// </summary>
		internal readonly List<(Vector3 Min, Vector3 Max, bool Console)> Areas = new List<(Vector3, Vector3, bool)>();

		/// <summary>
		/// The MiniMap's GPS on the dash (a box in the interior's space, at its depth), or null when none is fitted. The panel's face gets a
		/// recessed bay behind it, so it looks set into the panel and not stuck over its gauges. Kept up to date by <see cref="FindGps"/>.
		/// </summary>
		internal (Vector3 Min, Vector3 Max)? Gps { get; private set; }

		/// <summary>A point in the interior's space as a point in the world.</summary>
		internal Vector3 World(Vector3 local) => transform.parent != null ? transform.parent.TransformPoint(local) : local;

		/// <summary>A point in the world as a point in the interior's space.</summary>
		internal Vector3 Local(Vector3 world) => transform.parent != null ? transform.parent.InverseTransformPoint(world) : world;

		internal float CollectiveValue => Collective != null ? Mathf.Clamp01(Collective.value) : 0f;
		internal float ThrottleValue => Throttle != null ? Mathf.Clamp01(Throttle.value) : 0f;

		// Stick sizes in dash pixels: the shortest shaft (when the screen's bottom edge can't be found), the gate's half width, the shaft's
		// thickness, and how far below the screen's bottom edge the foot is (out of sight, so a turn of the head shows no end).
		private const int StickLength = 40;
		private const int Gate = 16;
		private const int ShaftThick = 6;
		private const int BelowEdge = 20;
		// The levers' gate plate in dash pixels: front and back widths, height, front face, the slots' first row and travel, the levers'
		// distance from the middle, and how far the plate's front edge is below the screen's bottom edge.
		private const int PlateBottom = 66;
		// The plate's sides' lean (pixels across per row up) until the panel is measured: a plain trapezoid, as 0.1.6 drew it.
		private const float GuessSlope = 0.13f;
		private const int PlateHeight = 62;
		private const int PlateFace = 8;
		private const int Slot0 = 15;
		private const int Travel = 36;
		private const int LeverGap = 13;
		private const int PlateBelow = 4;

		private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
		private readonly List<InteriorCollider> hiddenColliders = new List<InteriorCollider>();
		private readonly List<Object> owned = new List<Object>();
		private readonly List<Transform> dashParts = new List<Transform>();
		private List<Renderer> dashRenderers;
		private CarLink link;
		private float texel = 0.05f;
		private Material template;
		private int sortingLayer;
		private int sortingOrder;
		private int layer;
		private float nextHide;

		private int stickLength = StickLength;
		private Transform stickBase;
		private SpriteRenderer gripRenderer;
		private Sprite gripFree;
		private Sprite gripLocked;
		/// <summary>Right-click lock: the stick stays where it is let go instead of springing back to the middle.</summary>
		private bool stickLocked;
		private Canvas shaft;
		private Texture2D shaftTexture;
		private Vector2Int shaftDrawn = new Vector2Int(int.MinValue, 0);

		private static int dropFrame = -10;
		private static bool dropTaken = true;

		/// <summary>
		/// True once, within a frame of the hand letting go of the collective at the bottom (what opens an Open World garage and signs on
		/// to its events for the Helicopter, in place of letting go of the shifter in neutral).
		/// </summary>
		internal static bool TakeCollectiveDrop()
		{
			if (dropTaken || Time.frameCount - dropFrame > 1)
			{
				return false;
			}
			dropTaken = true;
			return true;
		}

		/// <summary>Builds the cockpit in <paramref name="link"/>'s car interior, or returns null (after logging why).</summary>
		internal static Cockpit Build(CarLink link)
		{
			if (link.sw == null)
			{
				Mod.Log.LogWarning("Helicopter: the car has no steering wheel to take the place of, so there is no cockpit.");
				return null;
			}
			Transform root = link.sw.transform.parent;
			GameObject holder = new GameObject("HelicopterCockpit");
			holder.layer = root != null ? root.gameObject.layer : link.sw.gameObject.layer;
			holder.transform.SetParent(root, false);
			Cockpit c = holder.AddComponent<Cockpit>();
			c.link = link;
			c.layer = holder.layer;
			try
			{
				c.Make();
			}
			catch
			{
				// Takes the half-built controls away and shows the car's own again (OnDestroy).
				DestroyImmediate(holder);
				throw;
			}
			Instance = c;
			return c;
		}

		private void Make()
		{
			Transform root = transform.parent;
			Transform shifter = link.sa != null ? link.sa.transform : null;
			Measure(shifter != null ? shifter : link.sw.transform, root);
			Bounds wheel = LocalBounds(link.sw.transform, root, new Bounds(new Vector3(-1.8f, -0.36f, 1.14f), Vector3.one * 3f));
			Bounds lever = shifter != null ? LocalBounds(shifter, root, new Bounds(new Vector3(3.2f, 2.4f, -0.2f), Vector3.one * 2f)) : new Bounds(wheel.center + new Vector3(5f, 2.5f, -1.3f), Vector3.one * 2f);
			// Measured before they are hidden with the rest of the car's dash.
			Bounds dials = Dials(root, wheel);
			FindDashParts(root);
			Hide();
			Camera view = ViewCamera(root);
			View = view;

			// The stick: in line with the wheel's middle, its grip at rest just above the dials' bottom edge (the wheel's own middle is near
			// the screen's bottom edge, where the grip would be cut off), standing up from the floor: its foot just below the screen's
			// bottom edge.
			Vector3 grip = Snap(new Vector3(wheel.center.x, dials.min.y + 2f * texel, Mathf.Min(Mathf.Min(wheel.center.z, wheel.min.z), dials.min.z) - 0.1f));
			float? floor = ScreenBottom(view, root, grip);
			if (floor.HasValue)
			{
				stickLength = Mathf.Max(StickLength, Mathf.RoundToInt((grip.y - floor.Value) / texel) + BelowEdge);
			}
			Vector3 foot = grip - new Vector3(0f, stickLength * texel, 0f);
			BuildStick(foot, grip);
			float gx = (Gate + 15) * texel;
			Areas.Add((new Vector3(grip.x - gx, grip.y - (Gate + 2) * texel, grip.z), new Vector3(grip.x + gx, grip.y + (40 + Gate + 2) * texel, grip.z), false));
			float sx = (0.6f * Gate + ShaftThick) * texel;
			Areas.Add((new Vector3(grip.x - sx, foot.y, grip.z), new Vector3(grip.x + sx, grip.y, grip.z), false));

			// The levers where the shifter was, in their gate plate at the screen's bottom edge: the collective on the left, the throttle on
			// the right.
			Vector3 mid = Snap(new Vector3(lever.center.x, lever.center.y, Mathf.Min(lever.center.z, lever.min.z) - 0.1f));
			float? plateFloor = ScreenBottom(view, root, mid);
			Vector3 plate = Snap(new Vector3(mid.x, plateFloor.HasValue ? plateFloor.Value - PlateBelow * texel : mid.y - 40f * texel, mid.z));
			BuildGate(plate);
			Vector3 slot = plate + new Vector3(0f, (Slot0 - 1) * texel, 0f);
			Collective = BuildLever("Collective", slot + new Vector3(-LeverGap * texel, 0f, 0f), true);
			Throttle = BuildLever("Throttle", slot + new Vector3(LeverGap * texel, 0f, 0f), false);
			FitGate(GuessSlope, -GuessSlope);
			GateLeft = slot - new Vector3(PlateBottom * 0.5f * texel, 0f, 0f);
			GateRight = slot + new Vector3(PlateBottom * 0.5f * texel, 0f, 0f);
			float lx = Mathf.Max(PlateBottom * 0.5f, LeverGap + Art.LeverLean + 11f) * texel;
			Areas.Add((new Vector3(plate.x - lx, plate.y, plate.z), new Vector3(plate.x + lx, slot.y + (Travel + Art.LeverShaft + 20) * texel, plate.z), true));
			Collective.onReleased.AddListener(() =>
			{
				if (Collective.value <= 0.05f)
				{
					dropFrame = Time.frameCount;
					dropTaken = false;
				}
			});
			Mod.Info("Helicopter: cockpit built, pixel " + texel.ToString("0.0000") + ", stick at " + grip + " (" + stickLength + " pixels from the floor" + (floor.HasValue ? "" : ", guessed") + "), levers' plate at " + plate + (plateFloor.HasValue ? "" : " (guessed)") + ", view " + (view != null ? view.name : "not found") + ", hid " + dashParts.Count + " dash part(s), " + hiddenRenderers.Count + " renderer(s) and " + hiddenColliders.Count + " hand collider(s).");
		}

		/// <summary>The camera that draws the dash (the game's <c>InteriorCamera</c>, under the interior's head), or null.</summary>
		private static Camera ViewCamera(Transform root)
		{
			if (root == null)
			{
				return null;
			}
			Camera any = null;
			foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
			{
				if (c.name == "InteriorCamera")
				{
					return c;
				}
				any ??= c;
			}
			return any;
		}

		/// <summary>
		/// The height (in the interior's space) of the screen's bottom edge straight below <paramref name="at"/>, at its depth; null without
		/// the dash's camera. Taken once, at the window's shape then; the parts reach a little further down to allow for a change.
		/// </summary>
		private static float? ScreenBottom(Camera view, Transform root, Vector3 at)
		{
			if (view == null)
			{
				return null;
			}
			Vector3 world = root != null ? root.TransformPoint(at) : at;
			Vector3 vp = view.WorldToViewportPoint(world);
			if (vp.z <= view.nearClipPlane)
			{
				return null;
			}
			Vector3 edge = view.ViewportToWorldPoint(new Vector3(vp.x, 0f, vp.z));
			float y = (root != null ? root.InverseTransformPoint(edge) : edge).y;
			return y < at.y ? y : (float?)null;
		}

		/// <summary>
		/// The car's dash read-outs that mean nothing in a helicopter, each as its group under the interior (so its backing sprite goes with
		/// it): the speed display with the rev and speed dials, the gear and grip box, the rear-view mirror, the money and enemy-targeting
		/// lights. Not the board at the top right.
		/// </summary>
		private void FindDashParts(Transform root)
		{
			dashParts.Clear();
			if (root == null)
			{
				return;
			}
			void Add(Component c)
			{
				Transform t = c != null ? c.transform : null;
				while (t != null && t.parent != root)
				{
					t = t.parent;
				}
				if (t != null && t != transform && !dashParts.Contains(t))
				{
					dashParts.Add(t);
				}
			}
			Add(link.dialrpm);
			Add(link.dialspeed);
			Add(link.gearIndicator);
			Add(link.indicatorstall);
			Add(link.indicatormoneyshift);
			// The money light and the enemy-targeting light above the shifter (each a dark socket with a blinking sprite over it).
			foreach (MoneyIndicator m in root.GetComponentsInChildren<MoneyIndicator>(true))
			{
				Add(m);
			}
			foreach (EnemyTargetingIndicator e in root.GetComponentsInChildren<EnemyTargetingIndicator>(true))
			{
				Add(e);
			}
			foreach (SpeedDashboard d in root.GetComponentsInChildren<SpeedDashboard>(true))
			{
				Add(d);
			}
			foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(true))
			{
				if (r.name.StartsWith("rearview") || (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("MAT_RearviewMirror")))
				{
					Add(r);
				}
			}
			// Never the controls' own parents or the board: whatever holds the wheel, the shifters, the leaderboard or the lap counter stays.
			dashParts.RemoveAll(t => t == null || link.sw.transform.IsChildOf(t) || t.GetComponentInChildren<StatusInfo>(true) != null || t.GetComponentInChildren<LapCounter>(true) != null || t.GetComponentInChildren<ShifterBase>(true) != null || t.GetComponentInChildren<Grabbable>(true) != null);
		}

		/// <summary>The bounds of the rev and speed dials (in the cluster behind the wheel), or a guess from the wheel's.</summary>
		private Bounds Dials(Transform root, Bounds wheel)
		{
			Bounds guess = new Bounds(new Vector3(wheel.center.x, wheel.center.y + wheel.extents.y * 0.6f, wheel.center.z), new Vector3(wheel.size.x, wheel.size.y * 0.3f, 0.1f));
			bool any = false;
			Bounds b = guess;
			foreach (Dial d in new[] { link.dialrpm, link.dialspeed })
			{
				if (d == null)
				{
					continue;
				}
				Bounds one = LocalBounds(d.transform, root, guess);
				if (!any)
				{
					b = one;
					any = true;
				}
				else
				{
					b.Encapsulate(one);
				}
			}
			return b;
		}

		/// <summary>The dash's pixel size and sprite material, from the biggest sprite of the part we take the place of.</summary>
		private void Measure(Transform part, Transform root)
		{
			SpriteRenderer biggest = null;
			float area = 0f;
			foreach (SpriteRenderer r in part.GetComponentsInChildren<SpriteRenderer>(true))
			{
				if (r.sprite == null || r.sharedMaterial == null)
				{
					continue;
				}
				float a = r.sprite.rect.width * r.sprite.rect.height;
				if (a > area)
				{
					area = a;
					biggest = r;
				}
			}
			if (biggest == null)
			{
				Mod.Log.LogWarning("Helicopter: no dash sprite to measure; guessing the cockpit's pixel size.");
				return;
			}
			float rootScale = root != null ? Mathf.Abs(root.lossyScale.x) : 1f;
			float t = Mathf.Abs(biggest.transform.lossyScale.x) / biggest.sprite.pixelsPerUnit / Mathf.Max(1e-4f, rootScale);
			if (t > 0.005f && t < 0.5f)
			{
				texel = t;
			}
			template = biggest.sharedMaterial;
			sortingLayer = biggest.sortingLayerID;
			sortingOrder = biggest.sortingOrder;
		}

		/// <summary>The bounds of a part's shown sprites in the interior's own space (before they are hidden).</summary>
		private static Bounds LocalBounds(Transform part, Transform root, Bounds fallback)
		{
			return TryLocalBounds(part, root, out Bounds b) ? b : fallback;
		}

		/// <summary>The bounds of a part's shown sprites in the interior's own space; false if none is shown.</summary>
		private static bool TryLocalBounds(Transform part, Transform root, out Bounds b)
		{
			bool any = false;
			b = default;
			foreach (SpriteRenderer r in part.GetComponentsInChildren<SpriteRenderer>(false))
			{
				if (!r.enabled || r.sprite == null)
				{
					continue;
				}
				Bounds w = r.bounds;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z);
					Vector3 p = root != null ? root.InverseTransformPoint(corner) : corner;
					if (!any)
					{
						b = new Bounds(p, Vector3.zero);
						any = true;
					}
					else
					{
						b.Encapsulate(p);
					}
				}
			}
			return any;
		}

		private MonoBehaviour gpsScreen;
		private float nextGpsSearch;

		/// <summary>
		/// Looks for the MiniMap's GPS in the interior (its <c>GpsScreen</c>, found by name: each mod is compiled on its own) and measures
		/// its shown sprites. True when its box changed since the last look (it was fitted, built, moved or taken off), so the panel's face
		/// must be drawn again. The GPS builds its sprites a frame or more after it is spawned, and a live refit can add or take it off.
		/// </summary>
		internal bool FindGps()
		{
			Transform root = transform.parent;
			if (root == null)
			{
				return false;
			}
			if (gpsScreen == null && Time.unscaledTime >= nextGpsSearch)
			{
				nextGpsSearch = Time.unscaledTime + 1f;
				foreach (MonoBehaviour m in root.GetComponentsInChildren<MonoBehaviour>(false))
				{
					if (m != null && m.GetType().Name == "GpsScreen")
					{
						gpsScreen = m;
						break;
					}
				}
			}
			(Vector3 Min, Vector3 Max)? now = null;
			if (gpsScreen != null && gpsScreen.isActiveAndEnabled && TryLocalBounds(gpsScreen.transform, root, out Bounds b))
			{
				now = (new Vector3(b.min.x, b.min.y, b.center.z), new Vector3(b.max.x, b.max.y, b.center.z));
			}
			bool same = now.HasValue == Gps.HasValue && (!now.HasValue || ((now.Value.Min - Gps.Value.Min).sqrMagnitude < texel * texel * 0.25f && (now.Value.Max - Gps.Value.Max).sqrMagnitude < texel * texel * 0.25f));
			if (same)
			{
				return false;
			}
			Gps = now;
			Mod.Info("Helicopter: " + (now.HasValue ? "the GPS is on the dash at " + now.Value.Min + " to " + now.Value.Max : "no GPS on the dash") + ".");
			return true;
		}

		/// <summary>Onto the dash's pixel grid, so the parts line up with the pixels round them.</summary>
		private Vector3 Snap(Vector3 p)
		{
			return new Vector3(Mathf.Round(p.x / texel) * texel, Mathf.Round(p.y / texel) * texel, p.z);
		}

		// ---- Hiding the car's controls ----

		/// <summary>The wheel, every shifter and the handbrake: renderers and hand colliders off. Run again now and then, since a refit
		/// switches the shifters' sprites back on.</summary>
		private void Hide()
		{
			List<Transform> parts = new List<Transform>();
			parts.Add(link.sw.transform);
			foreach (ShifterBase s in Rig.All<ShifterBase>())
			{
				if (s != null)
				{
					parts.Add(s.transform);
				}
			}
			if (link.hba != null)
			{
				parts.Add(link.hba.transform);
			}
			parts.AddRange(dashParts);
			foreach (Transform p in parts)
			{
				if (p == null)
				{
					continue;
				}
				foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
				{
					if (r.enabled && !r.transform.IsChildOf(transform))
					{
						r.enabled = false;
						if (!hiddenRenderers.Contains(r))
						{
							hiddenRenderers.Add(r);
						}
					}
				}
				foreach (InteriorCollider c in p.GetComponentsInChildren<InteriorCollider>(true))
				{
					if (c.enabled)
					{
						c.enabled = false;
						if (!hiddenColliders.Contains(c))
						{
							hiddenColliders.Add(c);
						}
					}
				}
			}
		}

		/// <summary>Puts the car's controls back (the Helicopter taken off in a refit).</summary>
		internal void Restore()
		{
			foreach (Renderer r in hiddenRenderers)
			{
				if (r != null)
				{
					r.enabled = true;
				}
			}
			foreach (InteriorCollider c in hiddenColliders)
			{
				if (c != null)
				{
					c.enabled = true;
				}
			}
			hiddenRenderers.Clear();
			hiddenColliders.Clear();
		}

		// ---- Building the controls ----

		private void BuildStick(Vector3 foot, Vector3 grip)
		{
			stickBase = new GameObject("StickFoot").transform;
			stickBase.gameObject.layer = layer;
			stickBase.SetParent(transform, false);
			stickBase.localPosition = foot;

			// Below the screen's bottom edge when that is known (the stick comes up from the floor, out of sight).
			Part("Boot", Art.Boot(), new Vector2(0.5f, 0.5f), stickBase, Vector3.zero, 0);

			// The shaft: redrawn in whole pixels from the foot to the grip whenever the grip moves (no rotated sprite), so it pivots at the
			// floor.
			shaft = new Canvas(2 * Gate + ShaftThick + 6, stickLength + Gate + 4);
			shaftTexture = shaft.Texture("HelicopterShaft");
			owned.Add(shaftTexture);
			Part("Shaft", shaftTexture, new Vector2(0.5f, 0f), stickBase, new Vector3(0f, 0f, -0.02f), 1);

			GameObject knob = new GameObject("StickGrip");
			knob.layer = layer;
			knob.transform.SetParent(transform, false);
			knob.transform.localPosition = grip;
			// Pivot in the collar, where the shaft goes in.
			Vector2 pivot = new Vector2(0.5f, 2.5f / 40f);
			gripFree = MakeSprite(Art.Grip(false), pivot, "HelicopterGrip");
			gripLocked = MakeSprite(Art.Grip(true), pivot, "HelicopterGripLocked");
			gripRenderer = Part("Sprite", gripFree, knob.transform, new Vector3(0f, 0f, -0.04f), 2);
			InteriorCircleCollider touch = Touch<InteriorCircleCollider>(knob.transform, new Vector3(0f, 20f * texel, -0.1f));
			touch.radius = 15f * texel;
			touch.hitBoxExpand = 2f * texel;

			// Added after the pose is set: the gate takes its middle from where the object stands as it wakes.
			Cyclic = knob.AddComponent<DoubleLinearObject>();
			Cyclic.attachedCollider = touch;
			Cyclic.isPhysicsControlled = false;
			Cyclic.minLimit = new Vector2(-Gate * texel, -Gate * texel);
			Cyclic.maxLimit = new Vector2(Gate * texel, Gate * texel);
			Cyclic.targetValue = new Vector2(0.5f, 0.5f);
			Cyclic.axisSpringScale = Vector2.one;
			Cyclic.handSpringForce = 1f;
			Cyclic.objectSpringForce = 1f;
			Cyclic.positionSmoothing = 25f;
			Cyclic.releasedDamping = 1f;
			Cyclic.directSpeedKeptOnRelease = 0f;
			Cyclic.keepPhysicalCursorRange = 1f;
			Cyclic.releaseCursorBlend = 1f;
			Cyclic.handIndicatorCameraOffset = 0.5f;
			Events(Cyclic);
			// Held, it follows the hand all the way; let go, it springs back to the middle unless locked by a right click. The lock is
			// only the spring left off (not the game's `frozen`, which stops the hand moving it too), so a locked stick still moves
			// while grabbed and stays wherever it is let go (owner, after 0.1.8).
			Cyclic.onGrabbed.AddListener(() => Cyclic.objectSpringForce = 0f);
			Cyclic.onReleased.AddListener(() => Cyclic.objectSpringForce = stickLocked ? 0f : 1f);
			Cyclic.onRightClicked.AddListener(() =>
			{
				stickLocked = !stickLocked;
				if (!Cyclic.IsGrabbed)
				{
					Cyclic.objectSpringForce = stickLocked ? 0f : 1f;
				}
				gripRenderer.sprite = stickLocked ? gripLocked : gripFree;
				Mod.Info("Helicopter: stick " + (stickLocked ? "locked" : "unlocked"));
			});
		}

		/// <summary>The levers' gate plate, its front edge's middle at <paramref name="at"/>, behind the levers in depth and order.</summary>
		private void BuildGate(Vector3 at)
		{
			Transform gate = new GameObject("LeverGate").transform;
			gate.gameObject.layer = layer;
			gate.SetParent(transform, false);
			gate.localPosition = at;
			Canvas art = Art.Gate(PlateBottom, PlateHeight, PlateFace, LeverGap, Slot0, Travel, GuessSlope, -GuessSlope);
			gateRenderer = Part("Sprite", art, new Vector2(0.5f, 1f / art.Height), gate, new Vector3(0f, 0f, 0.03f), 0);
		}

		/// <summary>The front corners of the plate's top (at the slots' first row), in the interior's space, for measuring the lean.</summary>
		internal Vector3 GateLeft { get; private set; }
		internal Vector3 GateRight { get; private set; }

		/// <summary>
		/// Leans the gate's sides, its slots and the levers' travel to the console's lines as seen from the seat (pixels across per row
		/// up, at the plate's left and right sides): the plate is drawn as part of the console behind it, which leans with the panel's
		/// face (0.1.7: a plain trapezoid on a console that leant the other way looked "off-angle"). The slots keep their bottom ends, so
		/// the levers (which take their anchor from where they stood as they woke) only turn their axis.
		/// </summary>
		internal void FitGate(float slopeLeft, float slopeRight)
		{
			slopeLeft = Mathf.Clamp(slopeLeft, -0.6f, 0.6f);
			slopeRight = Mathf.Clamp(slopeRight, -0.6f, 0.6f);
			if (gateRenderer != null)
			{
				Canvas art = Art.Gate(PlateBottom, PlateHeight, PlateFace, LeverGap, Slot0, Travel, slopeLeft, slopeRight);
				Sprite sprite = MakeSprite(art, new Vector2(0.5f, 1f / art.Height), "HelicopterLeverGate");
				gateRenderer.sprite = sprite;
				Material m = gateRenderer.sharedMaterial;
				foreach (string p in new[] { "_BaseMap", "_MainTex" })
				{
					if (m != null && m.HasProperty(p))
					{
						m.SetTexture(p, sprite.texture);
					}
				}
			}
			foreach ((LinearObject lever, int side) in new[] { (Collective, -1), (Throttle, 1) })
			{
				if (lever == null)
				{
					continue;
				}
				float slope = Art.SlotSlope(PlateBottom, side * LeverGap, slopeLeft, slopeRight);
				// Along (slope, 1): the same height of travel, a little longer along the slant.
				lever.anchorAngle = Mathf.Atan2(1f, slope);
				lever.maxLimit = Travel * texel * Mathf.Sqrt(1f + slope * slope);
			}
		}

		/// <summary>A lever standing in its slot, its collar at <paramref name="bottom"/> (value 0) and up <see cref="Travel"/> pixels.</summary>
		private SpriteRenderer gateRenderer;

		private LinearObject BuildLever(string name, Vector3 bottom, bool collective)
		{
			GameObject handle = new GameObject(name);
			handle.layer = layer;
			handle.transform.SetParent(transform, false);
			// At the bottom of its travel: the lever's value runs 0 (down) to 1 (up).
			handle.transform.localPosition = bottom;
			int side = collective ? -1 : 1;
			Canvas art = Art.Lever(collective, side);
			Part("Sprite", art, new Vector2(0.5f, 2.5f / art.Height), handle.transform, new Vector3(0f, 0f, -0.04f), 2);
			// The hand takes it by the knob, which leans out beside the slot.
			InteriorSquareCollider touch = Touch<InteriorSquareCollider>(handle.transform, new Vector3(side * Art.LeverLean * texel, (Art.LeverShaft + 8) * texel, -0.1f));
			touch.width = 20f * texel;
			touch.height = 20f * texel;
			touch.hitBoxExpand = 1f * texel;

			LinearObject lever = handle.AddComponent<LinearObject>();
			lever.attachedCollider = touch;
			lever.isPhysicsControlled = false;
			lever.anchorAngle = Mathf.PI / 2f;
			lever.useLimits = true;
			lever.minLimit = 0f;
			lever.maxLimit = Travel * texel;
			lever.targetValue = 0f;
			lever.handSpringForce = 1f;
			lever.objectSpringForce = 0f;
			lever.positionSmoothing = 25f;
			lever.releasedDamping = 1f;
			lever.directSpeedKeptOnRelease = 0f;
			lever.keepPhysicalCursorRange = 1f;
			lever.releaseCursorBlend = 1f;
			lever.handIndicatorCameraOffset = 0.5f;
			Events(lever);
			return lever;
		}

		/// <summary>
		/// The grab events are serialized fields that a scene's objects get from their saved data; on one added in code they are null,
		/// and the Grabber and our listeners need them.
		/// </summary>
		private static void Events(Grabbable g)
		{
			g.onGrabbed ??= new UnityEvent();
			g.onReleased ??= new UnityEvent();
			g.onRightClicked ??= new UnityEvent();
		}

		private T Touch<T>(Transform parent, Vector3 at) where T : InteriorCollider
		{
			GameObject touch = new GameObject("Touch");
			touch.layer = layer;
			touch.transform.SetParent(parent, false);
			touch.transform.localPosition = at;
			T c = touch.AddComponent<T>();
			c.sortingLayer = 0;
			return c;
		}

		private Sprite MakeSprite(Canvas art, Vector2 pivot, string name)
		{
			Texture2D t = art.Texture(name);
			owned.Add(t);
			Sprite s = Sprite.Create(t, new Rect(0, 0, art.Width, art.Height), pivot, 1f / texel, 0, SpriteMeshType.FullRect);
			s.name = name;
			s.hideFlags = HideFlags.HideAndDontSave;
			owned.Add(s);
			return s;
		}

		private SpriteRenderer Part(string name, Canvas art, Vector2 pivot, Transform parent, Vector3 at, int above)
		{
			return Part(name, MakeSprite(art, pivot, "Helicopter" + name), parent, at, above);
		}

		private SpriteRenderer Part(string name, Texture2D texture, Vector2 pivot, Transform parent, Vector3 at, int above)
		{
			Sprite s = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, 1f / texel, 0, SpriteMeshType.FullRect);
			s.name = texture.name;
			s.hideFlags = HideFlags.HideAndDontSave;
			owned.Add(s);
			return Part(name, s, parent, at, above);
		}

		private SpriteRenderer Part(string name, Sprite sprite, Transform parent, Vector3 at, int above)
		{
			GameObject part = new GameObject(name);
			part.layer = layer;
			part.transform.SetParent(parent, false);
			part.transform.localPosition = at;
			SpriteRenderer r = part.AddComponent<SpriteRenderer>();
			r.sprite = sprite;
			if (template != null)
			{
				Material m = new Material(template);
				m.name = "Helicopter" + name + "Lit";
				m.hideFlags = HideFlags.HideAndDontSave;
				foreach (string p in new[] { "_BaseMap", "_MainTex" })
				{
					if (m.HasProperty(p))
					{
						m.SetTexture(p, sprite.texture);
						m.SetTextureScale(p, Vector2.one);
						m.SetTextureOffset(p, Vector2.zero);
					}
				}
				foreach (string p in new[] { "_BaseColor", "_Color" })
				{
					if (m.HasProperty(p))
					{
						m.SetColor(p, Color.white);
					}
				}
				owned.Add(m);
				r.sharedMaterial = m;
			}
			r.sortingLayerID = sortingLayer;
			r.sortingOrder = sortingOrder + above;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			return r;
		}

		// ---- Every frame ----

		private void LateUpdate()
		{
			if (Time.unscaledTime >= nextHide)
			{
				nextHide = Time.unscaledTime + 0.5f;
				Hide();
			}
			HideLockButtons();
			HideFlashers();
			HideDash();
			DrawShaft();
		}

		/// <summary>
		/// The wheel's steer-lock button sprites (on its spokes), off every frame: <see cref="NSteeringWheel"/>'s Update turns one of
		/// them back on each frame, so the half-second <see cref="Hide"/> left them showing as two slanted grey marks above the dials.
		/// </summary>
		private void HideLockButtons()
		{
			if (link == null || link.sw == null)
			{
				return;
			}
			if (link.sw.lockButton != null)
			{
				link.sw.lockButton.enabled = false;
			}
			if (link.sw.lockButtonPressed != null)
			{
				link.sw.lockButtonPressed.enabled = false;
			}
		}

		/// <summary>
		/// The read-outs' renderers off every frame: some of their scripts switch sprites on each frame (the tyre monitor, the shift arrows),
		/// which the half-second <see cref="Hide"/> left showing as a white "||" over the panel.
		/// </summary>
		private void HideDash()
		{
			if (dashRenderers == null)
			{
				dashRenderers = new List<Renderer>();
				foreach (Transform t in dashParts)
				{
					if (t != null)
					{
						dashRenderers.AddRange(t.GetComponentsInChildren<Renderer>(true));
					}
				}
			}
			foreach (Renderer r in dashRenderers)
			{
				if (r != null && r.enabled)
				{
					r.enabled = false;
					if (!hiddenRenderers.Contains(r))
					{
						hiddenRenderers.Add(r);
					}
				}
			}
		}

		/// <summary>The dash's money-shift and stall lights, which <see cref="CarLink"/> switches on and off every frame.</summary>
		private void HideFlashers()
		{
			if (link == null)
			{
				return;
			}
			if (link.indicatormoneyshift != null)
			{
				link.indicatormoneyshift.enabled = false;
			}
			if (link.indicatorstall != null)
			{
				link.indicatorstall.enabled = false;
			}
		}

		/// <summary>The shaft from the foot to the grip, in whole dash pixels; redrawn only when the grip has moved a pixel.</summary>
		private void DrawShaft()
		{
			if (Cyclic == null || shaft == null || stickBase == null)
			{
				return;
			}
			Vector3 d = Cyclic.transform.localPosition - stickBase.localPosition;
			Vector2Int to = new Vector2Int(Mathf.RoundToInt(d.x / texel), Mathf.RoundToInt(d.y / texel));
			if (to == shaftDrawn)
			{
				return;
			}
			shaftDrawn = to;
			int ox = shaft.Width / 2;
			Art.Shaft(shaft, ox, 2, ox + to.x, Mathf.Clamp(to.y, 2, shaft.Height - 4), ShaftThick);
			shaftTexture.SetPixels32(shaft.Pixels);
			shaftTexture.Apply(false, false);
		}

		private void OnDestroy()
		{
			Restore();
			foreach (Object o in owned)
			{
				if (o != null)
				{
					Destroy(o);
				}
			}
			owned.Clear();
			if (Instance == this)
			{
				Instance = null;
			}
		}
	}
}
