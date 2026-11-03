using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HalfGripTools;
using UnityEngine;

namespace HalfGripHelicopter
{
	/// <summary>
	/// The Helicopter chassis item: a copy of one of the game's chassis items with stock numbers (no downforce, which would hold the
	/// helicopter down) and a drawn icon. It has no interior or exterior prefab: <see cref="HeliDriver"/> builds the cockpit and the rotor
	/// itself when it sees the item fitted, because the cockpit has to take the place of parts that are in the scene, not on a mount.
	/// <para>Where it is offered (<see cref="UpdateOffer"/>): the game's shop, race rewards and unlocks and the Open World's garages all
	/// skip items with <c>disabled</c> set, and all read it live when they pick. So the item is kept <c>disabled</c> except while an Open
	/// World race is loaded, or always offered with <c>Shop.InRuns</c> on.</para>
	/// </summary>
	internal static class HeliItem
	{
		/// <summary>Saved in runs and loadouts: never change it.</summary>
		internal const string Id = "helicopter.heli";

		internal static UpgradeComponent Item;

		internal static void Register(ModContext ctx)
		{
			ctx.Items.Add(Id, catalog =>
			{
				Item = Make(ctx, catalog);
				return Item;
			});
			ctx.OnUnload(() => Item = null);
			Mod.Price.SettingChanged += (_, _) =>
			{
				if (Item != null)
				{
					Item.cost = Mod.Price.Value;
				}
			};
		}

		// ---- Where it is offered ----

		private static bool owLinked;
		private static Func<bool> owActive;

		/// <summary>
		/// Finds the Open World's public <c>HalfGripOpenWorld.MapView.Active</c> (true while an Open World race is loaded), by name since
		/// each mod is compiled on its own. Tried at each scene load until the Open World is found; without it the item is offered only
		/// with <c>Shop.InRuns</c>.
		/// </summary>
		internal static void LinkOpenWorld()
		{
			if (owLinked)
			{
				return;
			}
			try
			{
				Type view = AppDomain.CurrentDomain.GetAssemblies().Select(a => Lock.SafeType(a, "HalfGripOpenWorld.MapView")).FirstOrDefault(t => t != null);
				if (view == null)
				{
					return;
				}
				owLinked = true;
				PropertyInfo active = view.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
				if (active == null || active.PropertyType != typeof(bool))
				{
					Mod.Log.LogWarning("Helicopter: the Open World's MapView has no Active, so its garages offer the Helicopter only with Shop.InRuns on.");
					return;
				}
				owActive = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), active.GetGetMethod());
				Mod.Info("Helicopter: linked to the Open World's MapView (the Helicopter is offered while its world is loaded).");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Helicopter: could not link to the Open World's MapView: " + e.Message);
			}
		}

		/// <summary>True when the item should be offered now: always with <c>Shop.InRuns</c>, otherwise only in an Open World race.</summary>
		private static bool Offered
		{
			get
			{
				if (Mod.InRuns.Value)
				{
					return true;
				}
				try
				{
					return owActive != null && owActive();
				}
				catch (Exception)
				{
					return false;
				}
			}
		}

		/// <summary>
		/// Sets the catalog item's <c>disabled</c> to match <see cref="Offered"/>. Called every frame (a bool and a property read), so a
		/// shop rolled just after leaving the Open World already sees it off. <c>disabled</c> does not touch the id lookup, so a run or
		/// loadout that holds the item still loads it when it is off.
		/// </summary>
		internal static void UpdateOffer()
		{
			if (Item == null)
			{
				return;
			}
			bool offered = Offered;
			if (Item.disabled == offered)
			{
				Item.disabled = !offered;
				Mod.Info("Helicopter: " + (offered ? "offered" : "no longer offered") + (Mod.InRuns.Value ? " (Shop.InRuns on)" : owActive != null && offered ? " (an Open World race)" : ""));
			}
		}

		/// <summary>True for the Helicopter item (by id, so a copy the game made of it counts too).</summary>
		internal static bool Is(UpgradeComponent u)
		{
			return u != null && u.id == Id;
		}

		/// <summary>True when the player's loadout, as the game knows it, carries the Helicopter.</summary>
		internal static bool Fitted
		{
			get
			{
				if (!PlayerLoadout.Known)
				{
					return false;
				}
				foreach (UpgradeComponent u in PlayerLoadout.Equipped)
				{
					if (Is(u))
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>The fitted Helicopter item itself (what the loadout holds), or the catalog's.</summary>
		internal static UpgradeComponent Current
		{
			get
			{
				if (PlayerLoadout.Known)
				{
					foreach (UpgradeComponent u in PlayerLoadout.Equipped)
					{
						if (Is(u))
						{
							return u;
						}
					}
				}
				return Item;
			}
		}

		private static UpgradeComponent Make(ModContext ctx, ItemCatalog catalog)
		{
			ChassisUpgrade basis = ctx.Items.FirstOf<ChassisUpgrade>(catalog, u => u.id == "aero_1") ?? ctx.Items.FirstOf<ChassisUpgrade>(catalog);
			if (basis == null)
			{
				Mod.Log.LogWarning("Helicopter: the game has no chassis item to copy, so there is no Helicopter.");
				return null;
			}
			ChassisUpgrade made = ctx.Items.Clone(basis, Id);
			made.description = "Helicopter\n<color=#7e7e7e>The roads are optional</color>";
			made.interaction = "Fits with guns and the GPS only: everything else comes off. Throttle up, raise the collective past half, fly with the stick (right click locks it)";
			made.secondaryDescription = "Takes the wheel and the shifter out. In the Open World, land in a garage, drop the collective to the bottom and let go to open it.";
			made.cost = Mod.Price.Value;
			made.tier = ItemTier.RaceSpec;
			made.disabled = !Offered;
			made.startsLocked = false;
			made.upgradeCategory = UpgradeCategory.CHASSIS;
			made.mountSlot = MountSlot.None;
			made.interiorPrefab = null;
			made.exteriorPrefab = null;
			made.exteriorSocket = "";
			made.statBoosts = new List<StatBoost>();
			made.customSprites = new List<CustomSprite>();
			made.hideStatLines = true;
			made.forceShow = new List<string>();
			made.forceHide = new List<string>();
			made.rollingFriction = PlayerStats.Stock.rollingFriction;
			made.airDrag = PlayerStats.Stock.airDrag;
			made.downForce = 0f;
			made.isFwd = PlayerStats.Stock.isFwd;
			made.isAwd = PlayerStats.Stock.isAwd;
			made.carSkin = null;
			made.image = Icon(basis.image);
			Mod.Info("Helicopter: made from '" + basis.id + "', icon " + (made.image != null ? made.image.rect.size.ToString() : "none"));
			return made;
		}

		/// <summary>The icon at the size, pixel density and pivot of the chassis picture it replaces, so it sits on the card the same way.</summary>
		private static Sprite Icon(Sprite like)
		{
			try
			{
				int w = like != null ? Mathf.RoundToInt(like.rect.width) : 43;
				int h = like != null ? Mathf.RoundToInt(like.rect.height) : 13;
				w = Mathf.Clamp(w, 16, 128);
				h = Mathf.Clamp(h, 8, 128);
				float ppu = like != null ? like.pixelsPerUnit : 100f;
				Vector2 pivot = like != null && like.rect.width > 0f ? new Vector2(like.pivot.x / like.rect.width, like.pivot.y / like.rect.height) : new Vector2(0.5f, 0.5f);
				Texture2D t = Art.Icon(w, h).Texture("HelicopterIcon");
				Sprite s = Sprite.Create(t, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
				s.name = "HelicopterIcon";
				s.hideFlags = HideFlags.HideAndDontSave;
				return s;
			}
			catch (System.Exception e)
			{
				Mod.Log.LogWarning("Helicopter: could not draw the icon, so it keeps the chassis picture it copies: " + e.Message);
				return like;
			}
		}
	}
}
