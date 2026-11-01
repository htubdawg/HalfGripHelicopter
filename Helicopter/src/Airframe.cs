using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripHelicopter
{
	/// <summary>
	/// What the car looks like from outside while the Helicopter is fitted: the car's body and wheels hidden and a helicopter in their
	/// place, built from primitives (their colliders taken off at once, so the car's own collider and ground rays are all that touch
	/// anything) in plain Lit materials, sized from the car's body:
	/// <list type="bullet">
	/// <item>an egg-shaped fuselage built round the driver's head (the cockpit view's camera), glass at the nose and in the doors, with
	/// a cabin inside (walls, floor, window frames) that only the cockpit view draws, so the view is from a seat looking out,</item>
	/// <item>a tail boom out past the car's back, a fin, a stabiliser and a tail rotor,</item>
	/// <item>two skids at the wheels' height on struts, so it stands where the car stood,</item>
	/// <item>the main rotor on a mast, a little wider than the car is long, spinning with the rotor speed.</item>
	/// </list>
	/// The body is the car's active skin model (<see cref="CarSkinController.carModels"/>; the Dacia Body mod swaps its meshes but keeps
	/// these objects) and the wheels' visual children; they are hidden again every half second, since a refit or a skin change switches
	/// them back on, and shown again when the Helicopter comes off. The parts go on the body's own layer, so every camera that draws the
	/// car draws the helicopter instead; that includes the cockpit view's world camera, which sits in the car at the driver's head.
	/// </summary>
	internal sealed class Airframe : MonoBehaviour
	{
		private static readonly Color BodyColour = new Color(0.30f, 0.36f, 0.22f);
		private static readonly Color GlassColour = new Color(0.22f, 0.36f, 0.44f);
		private static readonly Color MetalColour = new Color(0.14f, 0.15f, 0.16f);
		private static readonly Color WallColour = new Color(0.46f, 0.47f, 0.42f);
		private static readonly Color FloorColour = new Color(0.2f, 0.2f, 0.19f);

		private CarControllerNew cc;
		private Flight flight;
		private Transform main;
		private Transform tail;
		private float angle;
		private float nextHide;
		private Shader lit;
		private int partLayer;
		private readonly List<Renderer> hidden = new List<Renderer>();
		private readonly List<Object> owned = new List<Object>();
		private readonly List<Renderer> inner = new List<Renderer>();
		private Vector3 shellCentre;
		private Vector3 shellAxes = Vector3.one;
		private bool headFound;
		private bool hooked;
		private Texture2D speckle;

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

			float L = b.size.z;
			float W = b.size.x;
			float x = b.center.x;
			float z0 = b.center.z;
			Vector3 head = Head(b);

			// Fuselage: sized from the car's width and the driver's head, not the car's height (a car is far lower than a cabin, which
			// squashed 0.1.1's egg flat and left the head poking out of its roof). The head sits a little over half way up, a little
			// behind the middle, with the windscreen ahead of it and door windows beside it.
			float fw = 0.82f * W;
			float fh = Mathf.Max(1.12f * fw, (head.y - (ground + 0.16f * fw)) / 0.6f);
			float belly = Mathf.Min(ground + 0.16f * fw, head.y - 0.4f * fh);
			float fl = 0.68f * L;
			shellCentre = new Vector3(x, belly + 0.5f * fh, head.z + 0.08f * L);
			shellAxes = new Vector3(0.5f * fw, 0.5f * fh, 0.5f * fl);
			Shell();
			// The tail's and rotor's sizes were in car heights, the fuselage 0.84 of one.
			float H = fh / 0.84f;

			// Tail boom from the fuselage's back out past the car's back, with its fin, stabiliser and rotor.
			float boomY = shellCentre.y + 0.14f * fh;
			float tailZ = z0 - 0.62f * L;
			float boomFront = shellCentre.z - 0.8f * shellAxes.z;
			Part(PrimitiveType.Cylinder, "Boom", transform, new Vector3(x, boomY, (boomFront + tailZ) * 0.5f), new Vector3(0.12f * H, (boomFront - tailZ) * 0.5f, 0.12f * H), Quaternion.Euler(90f, 0f, 0f), BodyColour);
			Part(PrimitiveType.Cube, "Fin", transform, new Vector3(x, boomY + 0.24f * H, tailZ + 0.04f * L), new Vector3(0.04f * W, 0.5f * H, 0.1f * L), Quaternion.Euler(-20f, 0f, 0f), BodyColour);
			Part(PrimitiveType.Cube, "Stabiliser", transform, new Vector3(x, boomY, tailZ + 0.12f * L), new Vector3(0.5f * W, 0.03f * H, 0.07f * L), Quaternion.identity, BodyColour);
			tail = Pivot("TailRotor", new Vector3(x + 0.06f * W, boomY + 0.28f * H, tailZ + 0.04f * L));
			Part(PrimitiveType.Cube, "Blades", tail, Vector3.zero, new Vector3(0.015f * W, 0.36f * H, 0.035f * L), Quaternion.identity, MetalColour);

			// Skids on struts, at the wheels' bottom so it stands where the car stood.
			float skidLength = 0.6f * L;
			float skidZ = shellCentre.z - 0.02f * L;
			foreach (float side in new[] { -1f, 1f })
			{
				float sx = x + side * 0.46f * W;
				Part(PrimitiveType.Cube, "Skid", transform, new Vector3(sx, ground + 0.02f * W, skidZ), new Vector3(0.04f * W, 0.04f * W, skidLength), Quaternion.identity, MetalColour);
				foreach (float along in new[] { -0.28f, 0.22f })
				{
					float sz = skidZ + along * skidLength;
					Vector3 foot = new Vector3(sx, ground + 0.04f * W, sz);
					Vector3 top = new Vector3(x + side * 0.22f * W, belly + 0.12f * fh, sz);
					Strut(foot, top, 0.04f * W);
				}
			}

			// Mast and main rotor on the fuselage's top.
			float roof = shellCentre.y + shellAxes.y;
			float mast = 0.1f * fh;
			Part(PrimitiveType.Cylinder, "Mast", transform, new Vector3(x, roof + mast * 0.5f - 0.02f * fh, shellCentre.z), new Vector3(0.08f * W, mast * 0.5f + 0.02f * fh, 0.08f * W), Quaternion.identity, MetalColour);
			main = Pivot("MainRotor", new Vector3(x, roof + mast, shellCentre.z));
			Part(PrimitiveType.Cylinder, "Hub", main, Vector3.zero, new Vector3(0.14f * W, 0.03f * fh, 0.14f * W), Quaternion.identity, MetalColour);
			Part(PrimitiveType.Cube, "Blades", main, Vector3.zero, new Vector3(1.15f * L, 0.01f * fh, 0.05f * L), Quaternion.identity, MetalColour);
			Part(PrimitiveType.Cube, "Blades", main, Vector3.zero, new Vector3(0.05f * L, 0.01f * fh, 1.15f * L), Quaternion.identity, MetalColour);

			RenderPipelineManager.beginCameraRendering += OnCamera;
			hooked = true;
			Mod.Info("Helicopter: airframe built on body " + b.size + ", ground " + ground.ToString("0.00") + ", head " + head + (headFound ? "" : " (guessed)") + ", fuselage " + (2f * shellAxes) + " at " + shellCentre + ", layer " + LayerMask.LayerToName(partLayer) + ", hid " + hidden.Count + " car renderer(s), shader '" + (lit != null ? lit.name : "none") + "'.");
		}

		/// <summary>
		/// Where the cockpit view's world camera sits, in the car's space: the car's own camera tagged MainCamera (CarBodyPivot/Head in
		/// the game's cars). The cabin is built round it, so the view is from inside. Without one, a guess at the roof's middle.
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

		/// <summary>Whether a point (world space) is inside the fuselage.</summary>
		private bool Inside(Vector3 world)
		{
			Vector3 p = transform.InverseTransformPoint(world) - shellCentre;
			return new Vector3(p.x / shellAxes.x, p.y / shellAxes.y, p.z / shellAxes.z).sqrMagnitude < 1f;
		}

		/// <summary>
		/// The cabin's inside is drawn only for the cockpit view's camera. Every other camera is outside, except the rear-view mirror's,
		/// which sits by the head too: without this it would show the cabin's back wall instead of what is behind.
		/// </summary>
		private void OnCamera(ScriptableRenderContext context, Camera cam)
		{
			bool show = cam != null && cam.CompareTag("MainCamera") && Inside(cam.transform.position);
			foreach (Renderer r in inner)
			{
				if (r != null)
				{
					r.enabled = show;
				}
			}
		}

		/// <summary>
		/// Which parts of the fuselage are glass, from a point on it in the unit ellipsoid (u forward, v up, s sideways): the nose
		/// above the floor and its chin, and a door window each side by the head. <paramref name="grow"/> widens it, for the frames.
		/// </summary>
		private static bool Glass(float u, float v, float s, float grow)
		{
			bool front = u > 0.3f - grow && v > -0.45f - grow;
			bool chin = u > 0.72f - grow && v > -0.85f - grow;
			bool door = Mathf.Abs(s) > 0.45f - grow && v > -0.1f - grow && v < 0.7f + grow && u > -0.42f - grow;
			return front || chin || door;
		}

		private enum Skin { Body, Glass, Frame, Wall, Floor }

		/// <summary>
		/// The fuselage as one mesh pair, an egg tapering up toward the boom. The outer skin faces out, so from inside it is culled
		/// and the glass, opaque from outside, is clear from inside. The inner skin, a little smaller and facing in, has no
		/// triangles where the glass is: from inside the walls, floor and window frames show and the windows look out.
		/// </summary>
		private void Shell()
		{
			const int rings = 20;
			const int around = 28;
			Vector3[] pos = new Vector3[(rings + 1) * (around + 1)];
			Vector3[] unit = new Vector3[pos.Length];
			Vector2[] uv = new Vector2[pos.Length];
			// The inside's speckle: one texel about 0.04 car units, near the dash's pixel size seen from the seat.
			float texture = 32f * 0.04f;
			for (int i = 0; i <= rings; i++)
			{
				float phi = Mathf.PI * i / rings;
				float u = Mathf.Cos(phi);
				float r = Mathf.Sin(phi);
				float taper = u < 0f ? 1f - 0.35f * u * u : 1f;
				float lift = u < 0f ? 0.18f * u * u : 0f;
				for (int j = 0; j <= around; j++)
				{
					float th = 2f * Mathf.PI * j / around;
					float s = r * Mathf.Sin(th);
					float v = -r * Mathf.Cos(th);
					int k = i * (around + 1) + j;
					unit[k] = new Vector3(s, v, u);
					pos[k] = new Vector3(shellAxes.x * s * taper, shellAxes.y * (v * taper + lift), shellAxes.z * u);
					float radius = 0.5f * (shellAxes.x + shellAxes.y) * r * taper;
					uv[k] = new Vector2(pos[k].z / texture, th * radius / texture);
				}
			}

			var outer = new List<int>[3] { new List<int>(), new List<int>(), new List<int>() };
			var innerTris = new List<int>[3] { new List<int>(), new List<int>(), new List<int>() };
			for (int i = 0; i < rings; i++)
			{
				for (int j = 0; j < around; j++)
				{
					int a = i * (around + 1) + j;
					int bq = a + 1;
					int c = a + around + 1;
					int d = c + 1;
					Vector3 m = (unit[a] + unit[bq] + unit[c] + unit[d]) * 0.25f;
					bool glass = Glass(m.z, m.y, m.x, 0f);
					bool frame = !glass && Glass(m.z, m.y, m.x, 0.12f);
					int o = glass ? 1 : frame ? 2 : 0;
					Quad(outer[o], pos, a, bq, c, d, false);
					if (!glass)
					{
						Quad(innerTris[frame ? 2 : m.y < -0.55f ? 1 : 0], pos, a, bq, c, d, true);
					}
				}
			}

			Mesh outside = MakeMesh("HelicopterFuselage", pos, unit, uv, outer, 1f, false);
			Mesh inside = MakeMesh("HelicopterCabin", pos, unit, uv, innerTris, 0.96f, true);
			Renderer ro = MeshPart("Fuselage", outside, new[] { MaterialFor(BodyColour, false), MaterialFor(GlassColour, false), MaterialFor(MetalColour, false) });
			ro.shadowCastingMode = ShadowCastingMode.On;
			Renderer ri = MeshPart("Cabin", inside, new[] { MaterialFor(WallColour, true), MaterialFor(FloorColour, true), MaterialFor(MetalColour, false) });
			ri.shadowCastingMode = ShadowCastingMode.Off;
			ri.enabled = false;
			inner.Add(ri);
		}

		/// <summary>Two triangles of a quad, wound to face out (or in), checked against the egg's own outward direction.</summary>
		private void Quad(List<int> into, Vector3[] pos, int a, int b, int c, int d, bool inward)
		{
			Tri(into, pos, a, b, d, inward);
			Tri(into, pos, a, d, c, inward);
		}

		private void Tri(List<int> into, Vector3[] pos, int a, int b, int c, bool inward)
		{
			Vector3 n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
			if (n.sqrMagnitude < 1e-12f)
			{
				return;
			}
			Vector3 mid = (pos[a] + pos[b] + pos[c]) / 3f;
			Vector3 outward = new Vector3(mid.x / (shellAxes.x * shellAxes.x), mid.y / (shellAxes.y * shellAxes.y), mid.z / (shellAxes.z * shellAxes.z));
			bool facesOut = Vector3.Dot(n, outward) > 0f;
			if (facesOut != inward)
			{
				into.Add(a);
				into.Add(b);
				into.Add(c);
			}
			else
			{
				into.Add(a);
				into.Add(c);
				into.Add(b);
			}
		}

		private Mesh MakeMesh(string name, Vector3[] pos, Vector3[] unit, Vector2[] uv, List<int>[] subs, float scale, bool inward)
		{
			Vector3[] v = new Vector3[pos.Length];
			for (int i = 0; i < pos.Length; i++)
			{
				v[i] = pos[i] * scale;
			}
			Mesh mesh = new Mesh();
			mesh.name = name;
			mesh.hideFlags = HideFlags.HideAndDontSave;
			mesh.vertices = v;
			mesh.uv = uv;
			mesh.subMeshCount = subs.Length;
			for (int i = 0; i < subs.Length; i++)
			{
				mesh.SetTriangles(subs[i], i);
			}
			mesh.RecalculateNormals();
			// The poles' many copies each saw one triangle: point them straight along the egg.
			Vector3[] n = mesh.normals;
			for (int i = 0; i < n.Length; i++)
			{
				if (Mathf.Abs(unit[i].z) > 0.9999f)
				{
					n[i] = new Vector3(0f, 0f, Mathf.Sign(unit[i].z) * (inward ? -1f : 1f));
				}
			}
			mesh.normals = n;
			mesh.RecalculateBounds();
			owned.Add(mesh);
			return mesh;
		}

		private Renderer MeshPart(string name, Mesh mesh, Material[] mats)
		{
			GameObject part = new GameObject(name);
			part.layer = partLayer;
			part.transform.SetParent(transform, false);
			part.transform.localPosition = shellCentre;
			part.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer r = part.AddComponent<MeshRenderer>();
			r.sharedMaterials = mats;
			return r;
		}

		/// <summary>A grey speckle, point filtered, to tint the inside's walls and floor (flat colour reads as unfinished).</summary>
		private Texture2D Speckle()
		{
			if (speckle != null)
			{
				return speckle;
			}
			const int size = 32;
			speckle = new Texture2D(size, size, TextureFormat.RGBA32, false);
			speckle.name = "HelicopterSpeckle";
			speckle.hideFlags = HideFlags.HideAndDontSave;
			speckle.filterMode = FilterMode.Point;
			speckle.wrapMode = TextureWrapMode.Repeat;
			System.Random rng = new System.Random(4163270);
			Color32[] px = new Color32[size * size];
			for (int i = 0; i < px.Length; i++)
			{
				int roll = rng.Next(100);
				byte g = (byte)(roll < 12 ? 196 : roll < 22 ? 255 : 228);
				px[i] = new Color32(g, g, g, 255);
			}
			// Rivet rows every 8 texels across the panels.
			for (int y = 0; y < size; y += 8)
			{
				for (int x = 2; x < size; x += 4)
				{
					px[y * size + x] = new Color32(160, 160, 160, 255);
				}
			}
			speckle.SetPixels32(px);
			speckle.Apply(false, true);
			owned.Add(speckle);
			return speckle;
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

		private Transform Pivot(string name, Vector3 at)
		{
			Transform t = new GameObject(name).transform;
			t.gameObject.layer = partLayer;
			t.SetParent(transform, false);
			t.localPosition = at;
			return t;
		}

		private void Strut(Vector3 from, Vector3 to, float thick)
		{
			Vector3 d = to - from;
			Part(PrimitiveType.Cube, "Strut", transform, (from + to) * 0.5f, new Vector3(thick, d.magnitude, thick), Quaternion.FromToRotation(Vector3.up, d), MetalColour);
		}

		private readonly Dictionary<(Color, bool), Material> materials = new Dictionary<(Color, bool), Material>();

		private Material MaterialFor(Color colour, bool textured = false)
		{
			if (materials.TryGetValue((colour, textured), out Material m))
			{
				return m;
			}
			if (lit == null)
			{
				return null;
			}
			m = new Material(lit);
			m.name = "Helicopter";
			m.hideFlags = HideFlags.HideAndDontSave;
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", colour);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", colour == GlassColour ? 0.7f : 0.2f);
			}
			// Back faces culled, always: the glass is only opaque from outside because its inside faces are never drawn.
			if (m.HasProperty("_Cull"))
			{
				m.SetFloat("_Cull", (float)CullMode.Back);
			}
			if (textured && m.HasProperty("_BaseMap"))
			{
				m.SetTexture("_BaseMap", Speckle());
			}
			materials[(colour, textured)] = m;
			owned.Add(m);
			return m;
		}

		private void Part(PrimitiveType type, string name, Transform parent, Vector3 at, Vector3 size, Quaternion rotation, Color colour)
		{
			GameObject part = GameObject.CreatePrimitive(type);
			Collider c = part.GetComponent<Collider>();
			if (c != null)
			{
				DestroyImmediate(c);
			}
			part.name = name;
			part.layer = partLayer;
			part.transform.SetParent(parent, false);
			part.transform.localPosition = at;
			part.transform.localRotation = rotation;
			part.transform.localScale = size;
			MeshRenderer r = part.GetComponent<MeshRenderer>();
			Material m = MaterialFor(colour);
			if (m != null)
			{
				r.sharedMaterial = m;
			}
			r.shadowCastingMode = ShadowCastingMode.On;
		}

		/// <summary>The car's body and wheel meshes off (again: a refit or a skin change turns them back on).</summary>
		private void Hide()
		{
			foreach (IEnumerable<Renderer> set in new[] { BodyRenderers(cc), WheelRenderers(cc) })
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
