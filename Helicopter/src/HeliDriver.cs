using System;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// Puts the helicopter on the player's car and takes it off. Four times a second it looks for the player's <see cref="CarLink"/> and
	/// whether the loadout carries the Helicopter, so it follows race starts, scene changes and the Open World's live refits alike.
	/// While it is on, <see cref="Active"/> tells the patches in <see cref="Lock"/> to stop the car's own driving
	/// (<see cref="DriveCar"/> runs instead), to keep the Big Air achievement back and to let the collective open garages.
	/// </summary>
	internal sealed class HeliDriver : MonoBehaviour
	{
		internal static bool Active;
		internal static CarLink Link;

		private static Cockpit cockpit;
		private static Flight flight;
		private static Rotor rotor;
		private static bool failed;
		private float next;

		private void Update()
		{
			if (Active && Link == null)
			{
				// The race's scene is gone, and the helicopter with it.
				Clear();
			}
			if (Time.unscaledTime < next)
			{
				return;
			}
			next = Time.unscaledTime + 0.25f;
			try
			{
				CarLink link = Rig.First<CarLink>();
				bool want = link != null && link.cc != null && HeliItem.Fitted;
				if (link != Link)
				{
					failed = false;
				}
				if (want && !Active && !failed)
				{
					Install(link);
				}
				else if (!want && Active)
				{
					Remove();
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Helicopter: " + e);
				failed = true;
			}
		}

		private static void Install(CarLink link)
		{
			try
			{
				Link = link;
				cockpit = Cockpit.Build(link);
				if (cockpit == null)
				{
					failed = true;
					Remove();
					return;
				}
				int interior = LayerMask.NameToLayer("INTERIOR");
				Bounds body = Rotor.Body(link.cc, interior);
				flight = link.cc.gameObject.AddComponent<Flight>();
				flight.Init(link, body.size.z);
				rotor = Rotor.Build(link.cc, flight, body, interior);
				Active = true;
				Mod.Info("Helicopter: on the car (body " + body.size + ", mass " + (link.cc.rb != null ? link.cc.rb.mass : 0f) + ", gravity " + Physics.gravity.y + ").");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Helicopter: could not put the helicopter on the car: " + e);
				failed = true;
				Remove();
			}
		}

		private static void Remove()
		{
			if (cockpit != null)
			{
				Destroy(cockpit.gameObject);
			}
			if (flight != null)
			{
				Destroy(flight);
			}
			if (rotor != null)
			{
				Destroy(rotor.gameObject);
			}
			if (Active)
			{
				Mod.Info("Helicopter: off the car.");
			}
			Clear();
		}

		private static void Clear()
		{
			Active = false;
			Link = null;
			cockpit = null;
			flight = null;
			rotor = null;
		}

		/// <summary>
		/// In place of the car's own driving (<see cref="CarLink"/>'s FixedUpdate): no engine, no steering, the brakes on while parked,
		/// and the engine's sound and rev dial following the rotor speed.
		/// </summary>
		internal static void DriveCar(CarLink link)
		{
			CarControllerNew cc = link.cc;
			if (cc == null)
			{
				return;
			}
			float park = flight != null ? flight.Parking : 1f;
			float rpm = flight != null ? flight.Rpm : 0f;
			cc.engineAccel = 0f;
			cc.pedalInput = 0f;
			cc.steeringAngleDeg = 0f;
			cc.brakeAccel = link.brakeDeccel * park;
			cc.handBrake = link.brakeDeccel * park;
			link.engineIndicator = 0.1f + rpm;
		}

		private void OnDestroy()
		{
			Remove();
		}
	}
}
