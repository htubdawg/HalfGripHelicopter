using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// The rotors on the outside of the car, built from boxes in a dark copy of the body's material (no texture), sized from the body:
	/// the main rotor on a short mast on the roof, a little wider than the car is long, and a tail rotor at the back. They spin with the
	/// rotor speed. Their colliders are taken off at once, so they never touch the car, the ground or the car's own ground rays.
	/// </summary>
	internal sealed class Rotor : MonoBehaviour
	{
		private Transform main;
		private Transform tail;
		private Flight flight;
		private float angle;
		private readonly List<Object> owned = new List<Object>();

		/// <summary>The car's body bounds in its own space: every mesh but the wheels and the cockpit's.</summary>
		internal static Bounds Body(CarControllerNew cc, int interiorLayer)
		{
			HashSet<Transform> wheels = new HashSet<Transform>();
			foreach (Transform w in cc.frontWheels)
			{
				if (w != null)
				{
					wheels.Add(w);
				}
			}
			foreach (Transform w in cc.backWheels)
			{
				if (w != null)
				{
					wheels.Add(w);
				}
			}
			bool any = false;
			Bounds b = new Bounds(Vector3.zero, new Vector3(5f, 3f, 13f));
			foreach (MeshRenderer r in cc.GetComponentsInChildren<MeshRenderer>(false))
			{
				if (!r.enabled || r.gameObject.layer == interiorLayer || UnderAny(r.transform, wheels))
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

		private static bool UnderAny(Transform t, HashSet<Transform> roots)
		{
			for (; t != null; t = t.parent)
			{
				if (roots.Contains(t))
				{
					return true;
				}
			}
			return false;
		}

		internal static Rotor Build(CarControllerNew cc, Flight flight, Bounds body, int interiorLayer)
		{
			GameObject holder = new GameObject("HelicopterRotors");
			holder.transform.SetParent(cc.transform, false);
			Rotor r = holder.AddComponent<Rotor>();
			r.flight = flight;
			r.Make(cc, body, interiorLayer);
			return r;
		}

		private void Make(CarControllerNew cc, Bounds body, int interiorLayer)
		{
			MeshRenderer like = null;
			foreach (MeshRenderer m in cc.GetComponentsInChildren<MeshRenderer>(false))
			{
				if (m.enabled && m.gameObject.layer != interiorLayer && m.sharedMaterial != null)
				{
					like = m;
					break;
				}
			}
			gameObject.layer = like != null ? like.gameObject.layer : cc.gameObject.layer;
			Material material = null;
			if (like != null)
			{
				material = new Material(like.sharedMaterial);
				material.name = "HelicopterRotor";
				material.hideFlags = HideFlags.HideAndDontSave;
				foreach (string p in new[] { "_BaseMap", "_MainTex" })
				{
					if (material.HasProperty(p))
					{
						material.SetTexture(p, null);
					}
				}
				foreach (string p in new[] { "_BaseColor", "_Color" })
				{
					if (material.HasProperty(p))
					{
						material.SetColor(p, new Color(0.16f, 0.17f, 0.19f));
					}
				}
				owned.Add(material);
			}

			float length = body.size.z;
			float mast = Mathf.Max(0.3f, length * 0.06f);
			Vector3 top = new Vector3(body.center.x, body.max.y, body.center.z);
			Box("Mast", transform, top + new Vector3(0f, mast * 0.5f, 0f), new Vector3(length * 0.03f, mast, length * 0.03f), material);
			main = new GameObject("MainRotor").transform;
			main.gameObject.layer = gameObject.layer;
			main.SetParent(transform, false);
			main.localPosition = top + new Vector3(0f, mast, 0f);
			Box("Hub", main, Vector3.zero, new Vector3(length * 0.06f, length * 0.02f, length * 0.06f), material);
			Box("Blades", main, Vector3.zero, new Vector3(length * 1.1f, length * 0.008f, length * 0.045f), material);

			tail = new GameObject("TailRotor").transform;
			tail.gameObject.layer = gameObject.layer;
			tail.SetParent(transform, false);
			tail.localPosition = new Vector3(body.max.x + length * 0.01f, body.center.y + body.size.y * 0.25f, body.min.z + length * 0.05f);
			Box("Blades", tail, Vector3.zero, new Vector3(length * 0.008f, length * 0.2f, length * 0.03f), material);
		}

		private void Box(string name, Transform parent, Vector3 at, Vector3 size, Material material)
		{
			GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Collider c = box.GetComponent<Collider>();
			if (c != null)
			{
				DestroyImmediate(c);
			}
			box.name = name;
			box.layer = gameObject.layer;
			box.transform.SetParent(parent, false);
			box.transform.localPosition = at;
			box.transform.localScale = size;
			MeshRenderer r = box.GetComponent<MeshRenderer>();
			if (material != null)
			{
				r.sharedMaterial = material;
			}
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
		}

		private void Update()
		{
			float rpm = flight != null ? flight.Rpm : 0f;
			// Up to 4 turns a second: fast enough to read as spinning, slow enough not to strobe at the game's frame rates.
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
