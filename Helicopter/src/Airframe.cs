using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripHelicopter
{
	/// <summary>
	/// What the car looks like while the Helicopter is fitted: the car's body and wheels hidden and a helicopter in their place, made
	/// of meshes built from <see cref="Shape"/> (no colliders, so the car's own collider and ground rays are all that touch anything) in
	/// plain Lit materials, one per <see cref="Paint"/>. Its shape is worked out from the car's body bounds, the wheels' bottom and the
	/// driver's head (the car's camera tagged MainCamera, which is also the cockpit view's world camera), so that view is from inside
	/// the cabin.
	/// <para>The cabin and the panes (the glass seen from inside) are drawn for every camera: they sit inside the opaque outer skin, so
	/// from outside they can't be seen. The one exception is the rear-view mirror's camera, which sits by the head and would see the
	/// bulkhead; it skips them. (0.1.2 drew
	/// the cabin only for a camera it recognised as the cockpit's, and in the game that never matched, so the inside was empty.)</para>
	/// <para>The body is the car's active skin model (<see cref="CarSkinController.carModels"/>; the Dacia Body mod swaps its meshes but
	/// keeps these objects) and the wheels' visual children; they are hidden again every half second, since a refit or a skin change
	/// switches them back on, and shown again when the Helicopter comes off. The parts go on the body's own layer, so every camera that
	/// draws the car draws the helicopter instead.</para>
	/// </summary>
	internal sealed class Airframe : MonoBehaviour
	{
		private static readonly Color BodyColour = new Color(0.30f, 0.36f, 0.22f);
		private static readonly Color GlassColour = new Color(0.22f, 0.36f, 0.44f);
		private static readonly Color MetalColour = new Color(0.14f, 0.15f, 0.16f);
		private static readonly Color WallColour = new Color(0.46f, 0.47f, 0.42f);
		private static readonly Color FloorColour = new Color(0.2f, 0.2f, 0.19f);
		private static readonly Color SeatColour = new Color(0.33f, 0.24f, 0.17f);

		private CarControllerNew cc;
		private Flight flight;
		private Transform main;
		private Transform tail;
		private readonly List<Renderer> inside = new List<Renderer>();
		private float angle;
		private float nextHide;
		private Shader lit;
		private int partLayer;
		private bool headFound;
		private bool hooked;
		private readonly List<Renderer> hidden = new List<Renderer>();
		private readonly List<Object> owned = new List<Object>();
		private readonly Dictionary<Paint, Material> materials = new Dictionary<Paint, Material>();

		/// <summary>The car's body bounds in its own space (from its active skin model), or a car-sized guess.</summary>
		internal static Bounds Body(CarControllerNew cc)
		{
			bool any = false;
			Bounds b = new Bounds(new Vector3(0f, 1.2f, 0f), new Vector3(3.3f, 1.6f, 8.7f));
			foreach (Renderer r in BodyRenderers(cc))
			{
				if (!r.enabled || !r.gameObject.activeInHierarchy)
				{
					continue;
				}
				Bounds w = r.bounds;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z);
					Vector3 p = cc.transform.InverseTransformPoint(corner);
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
			return b;
		}

		/// <summary>The renderers of the car's skin models (all of them, so the one shown later is hidden too).</summary>
		private static IEnumerable<Renderer> BodyRenderers(CarControllerNew cc)
		{
			CarSkinController skin = cc.GetComponent<CarSkinController>();
			if (skin == null || skin.carModels == null)
			{
				yield break;
			}
			foreach (GameObject model in skin.carModels)
			{
				if (model == null)
				{
					continue;
				}
				foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
				{
					if (r is MeshRenderer || r is SkinnedMeshRenderer)
					{
						yield return r;
					}
				}
			}
		}

		/// <summary>
		/// The car's lap-delta meter: sprites on the car's own dashboard (in the body model, but not meshes), which would hang in the cabin
		/// by the left pillar as a slanted dark bar.
		/// </summary>
		private static IEnumerable<Renderer> MeterRenderers(CarControllerNew cc)
		{
			foreach (LapDeltaTimerDisplay d in cc.GetComponentsInChildren<LapDeltaTimerDisplay>(true))
			{
				foreach (Renderer r in d.GetComponentsInChildren<Renderer>(true))
				{
					yield return r;
				}
			}
		}

		/// <summary>The wheels' meshes (not their particles or trails).</summary>
		private static IEnumerable<Renderer> WheelRenderers(CarControllerNew cc)
		{
			foreach (List<Transform> list in new[] { cc.frontWheels, cc.backWheels })
			{
				foreach (Transform w in list)
				{
					if (w == null)
					{
						continue;
					}
					foreach (Renderer r in w.GetComponentsInChildren<Renderer>(true))
					{
						if (r is MeshRenderer || r is SkinnedMeshRenderer)
						{
							yield return r;
						}
					}
				}
			}
		}

		/// <summary>The bottom of the wheels in the car's space: where the skids go.</summary>
		private static float Ground(CarControllerNew cc, Bounds body)
		{
			float low = float.MaxValue;
			foreach (Renderer r in WheelRenderers(cc))
			{
				if (!r.enabled || !r.gameObject.activeInHierarchy)
				{
					continue;
				}
				Bounds w = r.bounds;
				low = Mathf.Min(low, cc.transform.InverseTransformPoint(new Vector3(w.center.x, w.min.y, w.center.z)).y);
			}
			return low < float.MaxValue ? low : body.min.y;
		}

		internal static Airframe Build(CarControllerNew cc, Flight flight, Bounds body)
		{
			GameObject holder = new GameObject("HelicopterAirframe");
			holder.transform.SetParent(cc.transform, false);
			Airframe a = holder.AddComponent<Airframe>();
			try
			{
				a.cc = cc;
				a.flight = flight;
				a.Make(body);
			}
			catch
			{
				Destroy(holder);
				throw;
			}
			return a;
		}

		private void Make(Bounds b)
		{
			float ground = Ground(cc, b);
			partLayer = cc.gameObject.layer;
			foreach (Renderer r in BodyRenderers(cc))
			{
				partLayer = r.gameObject.layer;
				break;
			}
			gameObject.layer = partLayer;
			lit = FindLit();
			Hide();

			Vector3 head = Head(b);
			Shape.Frame f = Shape.Measure(b.size.z, b.size.x, b.center.x, b.center.z, ground, head);
			foreach (Piece piece in Shape.Build(f))
			{
				Transform t = MeshPart(piece);
				if (piece.Name == "MainRotor")
				{
					main = t;
				}
				else if (piece.Name == "TailRotor")
				{
					tail = t;
				}
				else if (piece.Name == "Cabin" || piece.Name == "Panes")
				{
					inside.Add(t.GetComponent<Renderer>());
				}
			}
			RenderPipelineManager.beginCameraRendering += OnCamera;
			hooked = true;
			Mod.Info("Helicopter: airframe built on body " + b.size + " at " + b.center + ", ground " + ground.ToString("0.00") + ", head " + head + (headFound ? "" : " (guessed)") + ", pod " + f.PodLength.ToString("0.00") + " long, " + f.PodHeight.ToString("0.00") + " high, " + f.PodWidth.ToString("0.00") + " wide, layer " + LayerMask.LayerToName(partLayer) + ", hid " + hidden.Count + " car renderer(s), shader '" + (lit != null ? lit.name : "none") + "'.");
		}

		/// <summary>
		/// Where the cockpit view's world camera sits, in the car's space: the car's own camera tagged MainCamera (CarBodyPivot/Head in
		/// the game's cars). The cabin is built round it. Without one, a guess at the roof's middle.
		/// </summary>
		private Vector3 Head(Bounds b)
		{
			foreach (Camera c in cc.GetComponentsInChildren<Camera>(true))
			{
				if (c.CompareTag("MainCamera"))
				{
					Vector3 p = cc.transform.InverseTransformPoint(c.transform.position);
					Bounds near = b;
					near.Expand(b.size);
					if (near.Contains(p))
					{
						headFound = true;
						return p;
					}
				}
			}
			return new Vector3(b.center.x, b.max.y - 0.1f * b.size.y, b.center.z - 0.03f * b.size.z);
		}

		/// <summary>The rear-view mirror's camera sits by the head, inside the cabin: it skips the cabin and the panes so the mirror
		/// shows what is behind. Every other camera draws them (from outside they are hidden by the outer skin).</summary>
		private void OnCamera(ScriptableRenderContext context, Camera cam)
		{
			bool show = cam == null || cam.name != "RearViewCamera";
			foreach (Renderer r in inside)
			{
				if (r != null)
				{
					r.enabled = show;
				}
			}
		}

		private Transform MeshPart(Piece piece)
		{
			Mesh mesh = new Mesh();
			mesh.name = "Helicopter" + piece.Name;
			mesh.hideFlags = HideFlags.HideAndDontSave;
			if (piece.Vertices.Count > 65000)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(piece.Vertices);
			mesh.SetUVs(0, piece.Uvs);
			// White vertex colours: the sprite shader the panes use multiplies by them.
			Color32[] white = new Color32[piece.Vertices.Count];
			for (int i = 0; i < white.Length; i++)
			{
				white[i] = new Color32(255, 255, 255, 255);
			}
			mesh.colors32 = white;
			List<Material> mats = new List<Material>();
			mesh.subMeshCount = piece.Triangles.Count;
			int sub = 0;
			foreach (KeyValuePair<Paint, List<int>> kv in piece.Triangles)
			{
				mesh.SetTriangles(kv.Value, sub++);
				mats.Add(MaterialFor(kv.Key));
			}
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			owned.Add(mesh);

			GameObject part = new GameObject(piece.Name);
			part.layer = partLayer;
			part.transform.SetParent(transform, false);
			part.transform.localPosition = piece.Pivot;
			part.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer r = part.AddComponent<MeshRenderer>();
			r.sharedMaterials = mats.ToArray();
			r.shadowCastingMode = piece.CastsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
			return part.transform;
		}

		private static Color ColourOf(Paint paint)
		{
			switch (paint)
			{
				case Paint.Glass: return GlassColour;
				case Paint.Metal: return MetalColour;
				case Paint.Wall: return WallColour;
				case Paint.Floor: return FloorColour;
				case Paint.Seat: return SeatColour;
				default: return BodyColour;
			}
		}

		private Material MaterialFor(Paint paint)
		{
			if (materials.TryGetValue(paint, out Material m))
			{
				return m;
			}
			if (paint == Paint.Pane)
			{
				m = PaneMaterial();
			}
			else if (lit != null)
			{
				m = new Material(lit);
				Color colour = paint == Paint.Panel ? Color.white : ColourOf(paint);
				if (m.HasProperty("_BaseColor"))
				{
					m.SetColor("_BaseColor", colour);
				}
				if (m.HasProperty("_Smoothness"))
				{
					m.SetFloat("_Smoothness", paint == Paint.Glass ? 0.7f : 0.2f);
				}
				// Back faces culled, always: the glass is opaque only from outside because its inside faces are never drawn, and
				// the cabin's walls only from inside.
				if (m.HasProperty("_Cull"))
				{
					m.SetFloat("_Cull", (float)CullMode.Back);
				}
				Texture2D tex = null;
				if (paint == Paint.Wall || paint == Paint.Floor || paint == Paint.Seat)
				{
					tex = Texture("HelicopterSpeckle", Shape.Speckle(out int n), n, n);
				}
				else if (paint == Paint.Panel)
				{
					tex = Texture("HelicopterPanel", Shape.PanelPixels(out int w, out int h), w, h);
					tex.wrapMode = TextureWrapMode.Clamp;
				}
				if (tex != null && m.HasProperty("_BaseMap"))
				{
					m.SetTexture("_BaseMap", tex);
				}
			}
			if (m == null)
			{
				return null;
			}
			m.name = "Helicopter" + paint;
			m.hideFlags = HideFlags.HideAndDontSave;
			materials[paint] = m;
			owned.Add(m);
			return m;
		}

		/// <summary>
		/// The glass seen from inside: the game's own unlit sprite shader (alpha blended, in every race's dash and world, so never
		/// stripped from the build), with the faint glass texture. A transparent Lit material would need a shader variant the game
		/// may not ship. The sprite shader draws both sides; from outside the opaque outer glass is in front and hides it.
		/// </summary>
		private Material PaneMaterial()
		{
			const string name = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
			Shader shader = null;
			foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>())
			{
				if (s != null && s.name == name)
				{
					shader = s;
					break;
				}
			}
			if (shader == null)
			{
				shader = Shader.Find(name);
			}
			if (shader == null)
			{
				Mod.Log.LogWarning("Helicopter: no sprite shader for the glass, so the windows are left clear.");
				return null;
			}
			Material m = new Material(shader);
			m.mainTexture = Texture("HelicopterGlass", Shape.GlassPixels(out int n), n, n);
			return m;
		}

		/// <summary>A point-filtered, repeating texture from plain pixels (rows from the bottom).</summary>
		private Texture2D Texture(string name, Color32[] px, int w, int h)
		{
			Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
			t.name = name;
			t.hideFlags = HideFlags.HideAndDontSave;
			t.filterMode = FilterMode.Point;
			t.wrapMode = TextureWrapMode.Repeat;
			t.SetPixels32(px);
			t.Apply(false, false);
			owned.Add(t);
			return t;
		}

		/// <summary>The URP Lit shader, from a material already in the car (always in the build), or by name.</summary>
		private Shader FindLit()
		{
			foreach (Renderer r in cc.GetComponentsInChildren<Renderer>(true))
			{
				foreach (Material m in r.sharedMaterials)
				{
					if (m != null && m.shader != null && m.shader.name == "Universal Render Pipeline/Lit")
					{
						return m.shader;
					}
				}
			}
			return Shader.Find("Universal Render Pipeline/Lit");
		}

		/// <summary>The car's body and wheel meshes and its lap-delta meter off (again: a refit or a skin change turns them back on).</summary>
		private void Hide()
		{
			foreach (IEnumerable<Renderer> set in new[] { BodyRenderers(cc), WheelRenderers(cc), MeterRenderers(cc) })
			{
				foreach (Renderer r in set)
				{
					if (r != null && r.enabled)
					{
						r.enabled = false;
						if (!hidden.Contains(r))
						{
							hidden.Add(r);
						}
					}
				}
			}
		}

		private void Update()
		{
			if (Time.unscaledTime >= nextHide && cc != null)
			{
				nextHide = Time.unscaledTime + 0.5f;
				Hide();
			}
			float rpm = flight != null ? flight.Rpm : 0f;
			// Up to 4 turns a second: reads as spinning without strobing at the game's frame rates.
			angle = (angle + rpm * 1440f * Time.deltaTime) % 360f;
			if (main != null)
			{
				main.localRotation = Quaternion.Euler(0f, angle, 0f);
			}
			if (tail != null)
			{
				tail.localRotation = Quaternion.Euler(angle * 2.3f, 0f, 0f);
			}
		}

		private void OnDestroy()
		{
			if (hooked)
			{
				RenderPipelineManager.beginCameraRendering -= OnCamera;
				hooked = false;
			}
			foreach (Renderer r in hidden)
			{
				if (r != null)
				{
					r.enabled = true;
				}
			}
			hidden.Clear();
			foreach (Object o in owned)
			{
				if (o != null)
				{
					Destroy(o);
				}
			}
			owned.Clear();
		}
	}
}
