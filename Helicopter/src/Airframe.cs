using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// What the car looks like from outside while the Helicopter is fitted: the car's body and wheels hidden and a helicopter in their
	/// place, built from primitives (their colliders taken off at once, so the car's own collider and ground rays are all that touch
	/// anything) in plain Lit materials, sized from the car's body:
	/// <list type="bullet">
	/// <item>a rounded fuselage over the front two thirds with a glass canopy at its nose,</item>
	/// <item>a tail boom out past the car's back, a fin, a stabiliser and a tail rotor,</item>
	/// <item>two skids at the wheels' height on struts, so it stands where the car stood,</item>
	/// <item>the main rotor on a mast, a little wider than the car is long, spinning with the rotor speed.</item>
	/// </list>
	/// The body is the car's active skin model (<see cref="CarSkinController.carModels"/>; the Dacia Body mod swaps its meshes but keeps
	/// these objects) and the wheels' visual children; they are hidden again every half second, since a refit or a skin change switches
	/// them back on, and shown again when the Helicopter comes off. The parts go on the body's own layer, so every camera that draws the
	/// car draws the helicopter instead (the cockpit view does not draw the car's body, and so not this either).
	/// </summary>
	internal sealed class Airframe : MonoBehaviour
	{
		private static readonly Color BodyColour = new Color(0.30f, 0.36f, 0.22f);
		private static readonly Color GlassColour = new Color(0.22f, 0.36f, 0.44f);
		private static readonly Color MetalColour = new Color(0.14f, 0.15f, 0.16f);

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
			float H = Mathf.Max(b.max.y - ground, 0.5f);
			float x = b.center.x;
			float z0 = b.center.z;

			// Fuselage: an egg over the front two thirds, its belly clear of the skids.
			float bellyY = ground + 0.18f * H;
			Vector3 fuselage = new Vector3(x, bellyY + 0.42f * H, z0 + 0.16f * L);
			Vector3 fuselageSize = new Vector3(0.95f * W, 0.84f * H, 0.64f * L);
			Part(PrimitiveType.Sphere, "Fuselage", transform, fuselage, fuselageSize, Quaternion.identity, BodyColour);
			Part(PrimitiveType.Sphere, "Canopy", transform, fuselage + new Vector3(0f, 0.12f * H, 0.17f * L), new Vector3(0.8f * W, 0.62f * H, 0.36f * L), Quaternion.identity, GlassColour);

			// Tail boom out past the car's back, with its fin, stabiliser and rotor.
			float boomY = fuselage.y + 0.16f * H;
			float tailZ = z0 - 0.62f * L;
			float boomFront = z0 - 0.05f * L;
			Part(PrimitiveType.Cylinder, "Boom", transform, new Vector3(x, boomY, (boomFront + tailZ) * 0.5f), new Vector3(0.16f * H, (boomFront - tailZ) * 0.5f, 0.16f * H), Quaternion.Euler(90f, 0f, 0f), BodyColour);
			Part(PrimitiveType.Cube, "Fin", transform, new Vector3(x, boomY + 0.28f * H, tailZ + 0.04f * L), new Vector3(0.04f * W, 0.6f * H, 0.1f * L), Quaternion.Euler(-20f, 0f, 0f), BodyColour);
			Part(PrimitiveType.Cube, "Stabiliser", transform, new Vector3(x, boomY, tailZ + 0.12f * L), new Vector3(0.5f * W, 0.03f * H, 0.07f * L), Quaternion.identity, BodyColour);
			tail = Pivot("TailRotor", new Vector3(x + 0.06f * W, boomY + 0.32f * H, tailZ + 0.04f * L));
			Part(PrimitiveType.Cube, "Blades", tail, Vector3.zero, new Vector3(0.015f * W, 0.42f * H, 0.035f * L), Quaternion.identity, MetalColour);

			// Skids on struts, at the wheels' bottom so it stands where the car stood.
			float skidLength = 0.66f * L;
			float skidZ = z0 + 0.12f * L;
			foreach (float side in new[] { -1f, 1f })
			{
				float sx = x + side * 0.46f * W;
				Part(PrimitiveType.Cube, "Skid", transform, new Vector3(sx, ground + 0.025f * H, skidZ), new Vector3(0.05f * W, 0.05f * H, skidLength), Quaternion.identity, MetalColour);
				foreach (float along in new[] { -0.28f, 0.22f })
				{
					float sz = skidZ + along * skidLength;
					Vector3 foot = new Vector3(sx, ground + 0.05f * H, sz);
					Vector3 top = new Vector3(x + side * 0.3f * W, bellyY + 0.12f * H, sz);
					Strut(foot, top, 0.04f * W);
				}
			}

			// Mast and main rotor on the fuselage's top.
			float roof = fuselage.y + fuselageSize.y * 0.5f;
			float mast = 0.14f * H;
			Part(PrimitiveType.Cylinder, "Mast", transform, new Vector3(x, roof + mast * 0.5f, fuselage.z), new Vector3(0.08f * W, mast * 0.5f, 0.08f * W), Quaternion.identity, MetalColour);
			main = Pivot("MainRotor", new Vector3(x, roof + mast, fuselage.z));
			Part(PrimitiveType.Cylinder, "Hub", main, Vector3.zero, new Vector3(0.14f * W, 0.03f * H, 0.14f * W), Quaternion.identity, MetalColour);
			Part(PrimitiveType.Cube, "Blades", main, Vector3.zero, new Vector3(1.15f * L, 0.012f * H, 0.05f * L), Quaternion.identity, MetalColour);
			Part(PrimitiveType.Cube, "Blades", main, Vector3.zero, new Vector3(0.05f * L, 0.012f * H, 1.15f * L), Quaternion.identity, MetalColour);

			Mod.Info("Helicopter: airframe built on body " + b.size + ", ground " + ground.ToString("0.00") + ", layer " + LayerMask.LayerToName(partLayer) + ", hid " + hidden.Count + " car renderer(s), shader '" + (lit != null ? lit.name : "none") + "'.");
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

		private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

		private Material MaterialFor(Color colour)
		{
			if (materials.TryGetValue(colour, out Material m))
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
			materials[colour] = m;
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
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
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
