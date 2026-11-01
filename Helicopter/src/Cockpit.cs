using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// The helicopter's cockpit: the steering wheel, the shifters and the handbrake hidden (their renderers and hand colliders off, the
	/// objects left alive because <see cref="CarLink"/> and the Open World read them), and in their place:
	/// <list type="bullet">
	/// <item>the <b>stick</b> (cyclic) where the wheel was: the game's own two-axis gate (<see cref="DoubleLinearObject"/>, what the
	/// H-shifter is made of) that follows the hand while held and springs back to the middle when let go; a right click holds it where
	/// it is (frozen, like the wheel's steering lock) and lets it go again;</item>
	/// <item>the <b>collective</b> and the <b>throttle</b> where the shifter was: the game's own sliding lever (<see cref="LinearObject"/>,
	/// what the nitro and the handbrake are made of) with no spring, so each stays where the hand leaves it.</item>
	/// </list>
	/// The hand holds one thing at a time, as in the car: set the throttle and the collective, then take the stick.
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

		internal float CollectiveValue => Collective != null ? Mathf.Clamp01(Collective.value) : 0f;
		internal float ThrottleValue => Throttle != null ? Mathf.Clamp01(Throttle.value) : 0f;

		// Stick sizes in dash pixels.
		private const int StickLength = 34;
		private const int Gate = 12;
		// Lever sizes in dash pixels.
		private const int SlotLength = 44;
		private const int Travel = 36;
		private const int LeverGap = 10;

		private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
		private readonly List<InteriorCollider> hiddenColliders = new List<InteriorCollider>();
		private readonly List<Object> owned = new List<Object>();
		private CarLink link;
		private float texel = 0.05f;
		private Material template;
		private int sortingLayer;
		private int sortingOrder;
		private int layer;
		private float nextHide;

		private Transform stickBase;
		private SpriteRenderer gripRenderer;
		private Sprite gripFree;
		private Sprite gripLocked;
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
			c.Make();
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
			Hide();

			// The stick: grip at rest where the wheel's middle was, its foot below.
			Vector3 grip = Snap(new Vector3(wheel.center.x, wheel.center.y, Mathf.Min(wheel.center.z, wheel.min.z) - 0.1f));
			Vector3 foot = grip - new Vector3(0f, StickLength * texel, 0f);
			BuildStick(foot, grip);

			// The levers either side of where the shifter was: the collective on the left, the throttle on the right.
			Vector3 mid = Snap(new Vector3(lever.center.x, lever.center.y, Mathf.Min(lever.center.z, lever.min.z) - 0.1f));
			Collective = BuildLever("Collective", mid + new Vector3(-LeverGap * texel, 0f, 0f), Art.Yellow, true);
			Throttle = BuildLever("Throttle", mid + new Vector3(LeverGap * texel, 0f, 0f), Art.Red, false);
			Collective.onReleased.AddListener(() =>
			{
				if (Collective.value <= 0.05f)
				{
					dropFrame = Time.frameCount;
					dropTaken = false;
				}
			});
			Mod.Info("Helicopter: cockpit built, pixel " + texel.ToString("0.0000") + ", stick at " + grip + ", levers at " + mid + ", hid " + hiddenRenderers.Count + " renderer(s) and " + hiddenColliders.Count + " hand collider(s).");
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
			bool any = false;
			Bounds b = default;
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
			return any ? b : fallback;
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
			foreach (Transform p in parts)
			{
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

			Part("Boot", Art.Boot(), new Vector2(0.5f, 0.5f), stickBase, Vector3.zero, 0);

			// The shaft: redrawn in whole pixels from the foot to the grip whenever the grip moves (no rotated sprite).
			shaft = new Canvas(2 * Gate + 9, StickLength + Gate + 4);
			shaftTexture = shaft.Texture("HelicopterShaft");
			owned.Add(shaftTexture);
			Part("Shaft", shaftTexture, new Vector2(0.5f, 0f), stickBase, new Vector3(0f, 0f, -0.02f), 1);

			GameObject knob = new GameObject("StickGrip");
			knob.layer = layer;
			knob.transform.SetParent(transform, false);
			knob.transform.localPosition = grip;
			gripFree = MakeSprite(Art.Grip(false), new Vector2(0.5f, 0.1f), "HelicopterGrip");
			gripLocked = MakeSprite(Art.Grip(true), new Vector2(0.5f, 0.1f), "HelicopterGripLocked");
			gripRenderer = Part("Sprite", gripFree, knob.transform, new Vector3(0f, 0f, -0.04f), 2);
			InteriorCircleCollider touch = Touch<InteriorCircleCollider>(knob.transform, new Vector3(0f, 6f * texel, -0.1f));
			touch.radius = 8f * texel;
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
			// Held, it follows the hand all the way; let go, it springs back to the middle (unless held by a right click).
			Cyclic.onGrabbed.AddListener(() => Cyclic.objectSpringForce = 0f);
			Cyclic.onReleased.AddListener(() => Cyclic.objectSpringForce = 1f);
			Cyclic.onRightClicked.AddListener(() =>
			{
				Cyclic.frozen = !Cyclic.frozen;
				gripRenderer.sprite = Cyclic.frozen ? gripLocked : gripFree;
				Mod.Info("Helicopter: stick " + (Cyclic.frozen ? "held" : "let go"));
			});
		}

		private LinearObject BuildLever(string name, Vector3 middle, Color32 colour, bool banded)
		{
			Transform slot = new GameObject(name + "Slot").transform;
			slot.gameObject.layer = layer;
			slot.SetParent(transform, false);
			slot.localPosition = middle;
			Part("Sprite", Art.Slot(SlotLength, banded ? Art.Yellow : Art.Grey), new Vector2(0.5f, 0.5f), slot, Vector3.zero, 0);

			GameObject handle = new GameObject(name);
			handle.layer = layer;
			handle.transform.SetParent(transform, false);
			// At the bottom of its travel: the lever's value runs 0 (down) to 1 (up).
			handle.transform.localPosition = middle - new Vector3(0f, Travel * 0.5f * texel, 0f);
			Part("Sprite", Art.Handle(colour, banded), new Vector2(0.5f, 0.5f), handle.transform, new Vector3(0f, 0f, -0.04f), 2);
			InteriorSquareCollider touch = Touch<InteriorSquareCollider>(handle.transform, new Vector3(0f, 0f, -0.1f));
			touch.width = 16f * texel;
			touch.height = 10f * texel;
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
			return lever;
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
			DrawShaft();
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
			shaft.Clear();
			shaft.Line(ox, 2, ox + to.x, Mathf.Clamp(to.y, 2, shaft.Height - 2), 3, Art.Grey);
			shaft.Shade(Art.Light, Art.Dark, Art.Black);
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
