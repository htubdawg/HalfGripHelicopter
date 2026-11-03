# HalfGripHelicopter

A Half Grip (Steam app 4163270, Unity, Windows) mod: a chassis item, **Helicopter** (id `helicopter.heli`), that fits alone (but for guns and the MiniMap GPS), replaces the
cockpit's wheel and shifter with a stick, a collective and a throttle, and flies the car. Made mainly for the Open World mod
(`htubdawg/HalfGripOpenWorld`). Current release: see `Helicopter/mod.json`.

## Always be ready for a context clear

Sessions lose their context without warning. This file is the only memory the next session has, so after **every** step that
changes the state of things, this file must already say everything needed to carry on:

- **Update Status in the same commit as the change** (not "later"): what changed, what was checked and how, what is untested,
  what waits on whom, the next step. An owner's report goes in Status as soon as it arrives, with the causes found, before fixing.
- **Never leave work only in the session:** commit and push each finished step to the branch. No uncommitted edits at the end of
  a turn. Scratch renders and decompiled code are lost; anything worth keeping goes in the repo (`docs/`, this file).
- **Write down what was learned the hard way** (game facts, causes of bugs, the owner's wishes) under *Game facts* and *Owner's
  wishes* below, so a fresh session doesn't repeat a mistake or re-ask a question.
- Keep it short enough to read first thing; move settled history out of Status into one line each.

## How the mod runs

- A **source mod** for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools) (BepInEx 5): the loader compiles
  `Helicopter/src/*.cs` at game start (**C# 10**, netstandard2.1). What ships is the `Helicopter/` folder, installed to
  `<game>\BepInEx\HalfGripMods\Helicopter\`. Loader API: HalfGripTools' `docs/WRITING_MODS.md` and `docs/GAME_NOTES.md`. Game DLLs and
  scene dumps: private `htubdawg/HalfGripRefs`.

## Layout of src/

| File | Role |
|---|---|
| `Mod.cs` | Config, registers the item and patches, the persistent `HeliDriver` object |
| `HeliItem.cs` | The item: a clone of chassis `aero_1` (or any chassis) with stock numbers, no downforce, a drawn icon, price `Shop.Price` (80); `Fitted`/`Current` read `PlayerLoadout`; `disabled` unless `Shop.InRuns` (set at make and on the setting's change) |
| `Lock.cs` | Fits alone but for `Lock.Allowed` (any `UpgradeCategory.GUN`, and the MiniMap's GPS by its id `minimap.gps` only): `InventorySlot.OnDrop` (refuse others / clear others), `LoadoutSpawner.Awake` (cut the loadout to the allowed items, stats again from them), `LoadoutRefit.Swap` (live refits); the Open World's `Garage.Offered` offers the Helicopter though `disabled` (flag lifted for that call); `CarLink.FixedUpdate` skipped while flying; no `ACH_BIG_AIR`; the Open World's `ShifterHand` patched by name |
| `HeliDriver.cs` | Every 0.25 s: the player's `CarLink` + item fitted → builds cockpit, flight, rotor; takes them off when not; `DriveCar` in place of the car's driving (brakes when parked, `engineIndicator` = rotor speed) |
| `Cockpit.cs` | Finds the MiniMap's GPS on the dash (`GpsScreen` by name, `FindGps`) and mounts it on a plate sprite (`BuildMount`, `Art.Mount`), whose box keeps the panel's face plain behind it; hides the car's read-outs (dials, gear box, mirror, money and enemy-targeting lights); hides wheel, all shifters, handbrake (renderers + `InteriorCollider`s off, objects alive, re-hidden every 0.5 s; the wheel's lock-button sprites every frame, since `NSteeringWheel.Update` turns one back on); stick = `DoubleLinearObject` (spring off while held, on when let go, right click toggles a lock that leaves the spring off on release, not the game's `frozen`, so a locked stick still moves while held), levers = `LinearObject` with no spring; placed and sized from the hidden parts' bounds and the dash's pixel size; shaft redrawn in whole pixels |
| `Flight.cs` | The flight model (see its summary): thrust `m g 2 C rpm²`, climb-rate damping `g/ClimbRate`, attitude hold (critically damped at `TiltResponse` rad/s, default 3, turning capped at `TiltRate` deg/s, default 30), yaw from the stick + tail fin, drag balanced at `TopSpeed` |
| `Shape.cs` | The helicopter's geometry as plain triangles per `Paint` (Unity's managed structs only, so `tools/preview` draws it too): `Measure` sizes the pod from the car (length >= 0.62 car lengths, height 0.4-0.5 of the length, head 57% up and 40% back), `Build` gives the outer skin + outside parts, the cabin (inner skin facing in with holes at the glass, glareshield shelf, panel pedestal with a drawn face, floor, seats), the panes (glass seen from inside), the main and tail rotors. Windows are cut at level heights; every edge level owns a mesh column on every ring (`Angles`), so edges are clean. Pixel arrays for the speckle and glass textures; `PanelFace` (the face's corner and edge vectors), `PanelSize`, `PanelPixels` (the guessed face). No `Quaternion.Euler` (native; use `AboutX`/`AboutY`) |
| `Airframe.cs` | Hides the car's skin models (`CarSkinController.carModels`) and wheel meshes (re-hidden every 0.5 s, shown again on removal); turns `Shape`'s pieces into meshes on the body's layer, one URP Lit material per paint, `_Cull` Back, speckle and panel textures; the panes in the game's `Sprite-Unlit-Default` (alpha blended, always in the build, both sides; hidden from outside by the opaque outer glass); the head is the car's child camera tagged MainCamera; the cabin and panes are always drawn except for the camera named `RearViewCamera` (`beginCameraRendering`); rotors spin with `Flight.Rpm`; `FitPanel` redraws the panel's face round the cockpit's controls (below) |
| `Art.cs` | `Canvas` (pixel drawing) and the pictures: icon, stick grip and shaft, boot, the levers' gate plate (`Gate`) and levers, `Speckle` |
| `Remote.cs` | Multiplayer: tells the other players (HalfGripTools' `ctx.Net`, read by reflection so 1.0.2 still works) whether the helicopter is on, its `Shape.Frame` inputs and rotor speed; puts `Airframe.BuildRemote` (outside only) on the Multiplayer mod's stand-ins (`Multiplayer.RemoteCar`, by name: `PeerId`, `IsAi`) |
| `Panel.cs` | The instrument panel's face (managed only): `Draw(w, h, seenFrom, zones)` in the dash's style, kept clear where the controls fall (`Zone`s; a grey console under the levers' gate), gauges and strips centred in the free stretches of the part seen above the screen's edge; `GuessZones` from the owner's 0.1.6 screenshot |

## Rules that matter

- The item id `helicopter.heli` is saved in runs: never change it.
- Offered only in the Open World by default (owner, 0.2.6); `Shop.InRuns` adds normal runs, `Shop.Price` sets the price (default 80, was a fixed 150).
- **No keybindings** (owner): everything is worked by the cockpit hand. W/S do nothing while flying.
- The stick springs to the middle when let go (the helicopter levels and hovers); right click locks it: still moved by the hand, but it stays where it is let go. The levers keep their place.
- Fits alone but for guns and the GPS (owner, 0.2.0; `Lock.Allowed` is the one list): keep every route closed (garage slot, race spawn, live refit). Do not invent other routes around `LoadoutRefit.Swap`.
- Pixel look: flat palette colours, point filtering, whole dash pixels; no rotated smooth sprites (the shaft is redrawn instead).
- The flight model works from the car's mass, `Physics.gravity` and the config only: no constants that assume a world scale.
- C# 10, netstandard2.1. Tabs, `///` summaries that explain *why*, `Mod.Info` for anything but warnings (only with
  `Debug.VerboseLog`), `Mod.Log.LogWarning` with a try/catch round risky work.
- Bump `version` in `mod.json` with each release and keep the README's version in step.
- Work on a branch and open a PR; do not push to `main`.
- **Keep Status current** after every push, PR or owner report.

## Game facts (found in the scene dumps and decompiled code)

- The **cockpit view's world camera** is the car's own `Main Camera` (tag MainCamera) at `CarBodyPivot/Head`, about
  (-0.33, 2.02, -0.21) in the car's (`CarModel`, scale 1.5) space, so about roof height on the game's car. It draws the car's
  layers, so the airframe is seen from inside. Keys 1/3 turn it up to 120 degrees left/right (`CameraSwitch`).
- The **dash** (dials, stick, levers, hand) is a separate overlay: sprites on the INTERIOR layer, far from the car, drawn by
  `InteriorCamera` (clear Nothing, depth 2) over the world. Things placed in the world sit behind it.
- `RearViewCamera` (the mirror) sits by the head, about (0, 2.12, -0.12), looking back.
- `CameraSwitch` hides the car's `windshields` and shows its `interiorModels` in the cockpit view.
- The game's car: wheels at x +-1.65, z 2.75 / -2.5 (car space). Real body bounds not yet known (see Status).
- `NSteeringWheel.Update` turns one of its lock-button sprites back on every frame (hence the every-frame hide).
- Two small round lights above the shifter, at `CarInterior/DollarSign` (`MoneyIndicator`, blinks on money) and
  `CarInterior/EnemyTargetingLED` (`EnemyTargetingIndicator`): each a dark socket sprite with a blinking one over it.
- The "AirTime x1.2s" line on the screen is a combo: `CarControllerNew.FixedUpdate` calls
  `ComboManager.IncrementComboContinuous("AirTime", ...)` while no wheel touches (`Lock.NoAirTime` skips it while flying). Other
  combos: Drift (`GripIndicator`), SpinOut (`CarLink.FixedUpdate`, already skipped), OverTAKE, Kill, ShortCut.
- Dash sprites draw in **sorting order first**, depth only after; unlit sprites (the GPS's screen) write no depth. A part meant to
  be behind another dash sprite needs a lower sorting order, not only a larger z (0.2.2's GPS plate covered the GPS).
- The MiniMap's GPS builds its sprites (`Casing`, `Screen`) in `GpsScreen.Start`/`Update`, after the cockpit; a live refit can add
  or remove it.
- Unity's front faces are clockwise; `Vector3.Cross(b - a, c - a)` points at the viewer. The URP Lit materials cull back faces.
- See-through materials: the game ships `Universal Render Pipeline/2D/Sprite-Unlit-Default` (alpha blended, both sides) everywhere;
  a transparent URP Lit variant may be stripped from the build, so don't use one.
- **Multiplayer** (the Multiplayer mod 1.0.0, `4l3x4nd3r0/HalfGripMods`, `mods/Multiplayer`): another player's car is a stand-in,
  an enemy car from the game's prefab (`RaceSync.SpawnProxy`), kinematic, placed at that player's car position, dressed only by a
  paint line (`Skins.cs`: body index + 3 colours). No loadout or equipment is sent. So nothing a mod builds on its own car shows on
  the others' screens unless the mod sends it itself (`ctx.Net`, HalfGripTools 1.0.6+; same mods everywhere, channel = mod id).
- **Where items are offered:** the shop (`UpgradeMapper` query), race rewards (`RewardReveal`), unlocks (`ItemUnlocks`), per-run tier
  dealing (`ItemTiers.DealForRun`) and the Open World's garage (`Garage.Offered`) all skip `disabled` items and read the flag live
  when they pick; the id lookup ignores it (a saved run still loads the item). Locked items still show in the Open World's garage.
- Native Unity calls (`Quaternion.Euler`, `AngleAxis`, ...) throw in `tools/preview`; keep `Shape.cs` to managed struct code.

## Owner's wishes (for the look)

- Exterior: the green / blue glass / dark metal helicopter is liked ("exterior is fine"); glass opaque from outside.
- From the seat you must see out, and the cabin must look modelled and textured: no empty holes, no big dark shapes in the view
  (0.1.3's "giant black spike"), clean window edges ("front very messy" in 0.1.3).
- Wanted: textured controls/panel, glass that looks like glass.
- Cockpit (0.1.4-0.1.7): no car read-outs (dials, gear box, mirror, lap-delta meter), but **keep the board at the top right**. The
  controls in the car's own dash style (chunky shaded sprites, speckled grey plates like the H-shifter), the stick big (2-3x 0.1.5's)
  with its mount out of sight, nothing on the panel behind the controls ("can't have the stick mounted on a speedometer"), the
  panel at the dash's pixel size. 0.1.7's look is loved; the gate must follow the console's angle, and the levers must not hide
  their slots.
- **Look at it before handing it over.** 0.1.2 went out unseen and was wrong twice over; since then every shape change is checked
  with `tools/preview --model` / `--close` first, and the render is sent with the reply.

## Checking changes

This session's setup (cloud container, repos side by side in `/home/user`): `HalfGripRefs` (note the capitals: pass
`-p:Refs=/home/user/HalfGripRefs` to `tools/preview`), `HalfGripTools` (already built:
`/home/user/HalfGripTools/dist/BepInEx/plugins/HalfGripTools/HalfGripTools.dll`), `halfgripopenworld`. Decompile with
`dotnet tool install -g ilspycmd` then `DOTNET_ROLL_FORWARD=Major ~/.dotnet/tools/ilspycmd -t <Type> <refs>/game/HalfGrip_Data/Managed/Assembly-CSharp.dll`.
Scene dumps: `<refs>/scenedumps/0.418/<Map>/*.txt` (object tree with positions, components, fields).

- Compile (before every push), with HalfGripTools built against the refs:
  ```
  dotnet build <HalfGripTools>/HalfGripTools/HalfGripTools.csproj -c Release -p:GameDir=<halfgriprefs>/game
  tools/check.sh <halfgriprefs>/game <HalfGripTools>/dist/BepInEx/plugins/HalfGripTools/HalfGripTools.dll
  ```
- Pictures: `dotnet run -c Release --project tools/preview -p:Refs=<halfgriprefs> -- docs/preview.png` (exits 1 on a blank icon).
  `-- --panel <png>` draws the panel's face as first guessed; `-- --fitted <png>` as fitted in the owner's 0.1.7 log, with the GPS's plate and a stand-in GPS. `-- --clear <png>` draws the same sheet transparent at one pixel per dash pixel, to lay over an owner's screenshot.
  Look at it after any art change.
- Model: `dotnet run -c Release --project tools/preview -p:Refs=<halfgriprefs> -- --model docs/model.png` draws `Shape` with back-face
  culling and depth, from outside and from the head (ahead, left, right, the mirror back), for the game's car and a smaller one.
  **Look at it after any change to the shape**, before the owner does. `-- --close <png>` draws the seat view and the nose large
  (`docs/close.png`).
- In game (the owner tests): copy `Helicopter/` to `<game>\BepInEx\HalfGripMods\`, set `Debug.VerboseLog=true`, read
  `BepInEx\LogOutput.log` (look for `cockpit built`, `airframe built`, `on the car`, `linked to the Open World's garages`; a failure logs `could not put
  the helicopter on the car` once a race).

## Status (keep current)

**Now:** 0.2.5 released on `main` (PR #5, merged). **0.2.6** on `claude/practical-thompson-8lxknd` (owner's ask): the Helicopter is
offered only in the Open World unless `Shop.InRuns` (default false) is on, at `Shop.Price` (default 80; new keys, no migration).
The item is `disabled` unless `Shop.InRuns` (shops, rewards, unlocks skip it); `Lock` patches the Open World's `Garage.Offered` (by name,
with the garages link) to lift the flag for that call only (an every-frame check of `MapView.Active` was tried first and dropped:
the garage is the only Open World reader). So Open World event rewards don't offer it. Checked: compiles. **Not tested
in game.** Next: open the PR; owner's test: in a normal run it no longer shows in shops (on with `Shop.InRuns`), it shows in the
Open World's garage at 80. In a Multiplayer room outside the Open World its garage won't offer it either (unless `Shop.InRuns`).
History, 0.2.5: **Owner's report (Multiplayer):**
other players' helicopters showed as the last car they used, flying. Cause: the Multiplayer mod's stand-in for another player is an
enemy car dressed only in that player's paint and body index (see *Game facts*); this mod built the helicopter on the local car
only, so no other machine knew about it. **0.2.5** on `claude/practical-thompson-8lxknd`, PR #5 (`Remote.cs`): each copy sends over
`ctx.Net` four times a second while the helicopter is on (reliably once a second and on/off) its `Shape.Frame` inputs + rotor speed;
the others build an outside-only airframe on that player's stand-in (same shape, car body and wheels hidden, rotors at the sent
speed), rebuilt if the stand-in is remade, taken off on "off", after 10 s silence or out of the room. Logs (verbose) `player N flies
a helicopter`, `another player's helicopter built on '...'`; a missing `Multiplayer.RemoteCar` warns once. Checked: compiles against
HalfGripTools 1.0.5 (no channel, so off) and 1.0.6. **Not tested in game** (needs two players, both on 0.2.5 and HalfGripTools 1.0.6).
**Next step:** the owner's test in a room: each sees the other's helicopter, rotors spinning, gone when the item comes off.
History of 0.2.x: Owner's asks after testing 0.2.0 (screenshot: Open World,
GPS fitted): (1) two small lights left over above the old shifter: the car's money and enemy-targeting lights (`DollarSign`,
`EnemyTargetingLED`), now hidden with the other read-outs; (2) the "AirTime" combo line on the screen: `Lock.NoAirTime` skips it
while flying; (3) the GPS looked stuck on, over the panel's drawn gauges. 0.2.1 drew a dark recessed bay on the panel's face round it:
the owner's test (screenshot) said "not quite what we were going for" (a thin dark frame on a blank stretch of the 3D face, which is
seen at an angle, so it never quite lines up with the flat GPS sprite). Asked; the owner chose **mounted on a plate**: 0.2.2 draws a
speckled grey plate sprite in the gate's style (`Art.Mount`: 5-pixel rim, 4-row dark front face, a dark seat line round the casing,
corner screws) 0.03 behind the GPS's casing, in the dash's own pixels so it lines up exactly (`Cockpit.BuildMount`). The panel's face
is only kept plain behind the plate (no bay). `HeliDriver` checks the GPS every 0.25 s; when it comes, moves or goes the plate is
rebuilt and the face redrawn (not while the head is turned, `Airframe.HeadAsFitted`). Logs `the GPS is on the dash at ..., on a
plate` and a `GPS` zone in `panel fitted`. Checked: compiles, `tools/preview -- --fitted` looked at. **Not tested in game.** **Next
step:** the owner's test of 0.2.2 (the GPS on its plate; lights gone; no AirTime line). If the plate looks wrong, tune `MountRim`,
`MountFace` and `Art.Mount`. The plate's sprites (texture, material) from a rebuild are only freed with the cockpit (rare, fine).
Earlier: 0.2.0 (owner): guns (any `UpgradeCategory.GUN`) and the MiniMap's GPS (id `minimap.gps` only, not other MEDIUM items) may
go on with the Helicopter, on every route (`Lock.Allowed`). Checked in the Forest dump: the guns are prebuilt in `CarInterior` high
up (y about 8, switched by `GunLoadoutActivator` from `PlayerStats.gun`), far from the stick and levers and not among the hidden
read-outs; their outside parts go on `ExteriorSocket`s beside the body model, which `Airframe` doesn't hide (they may sit inside the
pod). The GPS goes in module slot SLOT1/2, right of the old shifter. Compiles; **not tested in game.** 0.1.9 (owner's asks after 0.1.8): the right-click lock no longer uses `frozen` (which stopped the hand moving the stick);
it only keeps the spring off on release, so a locked stick still moves while grabbed and stays where let go. Pitch/bank was too
quick: the attitude spring's frequency is now `Flight.TiltResponse` (default 3, was a fixed 5) and its turning rate is capped at
`Flight.TiltRate` (default 30 deg/s); new keys, so no migration. Compiles; **not tested in game.** **Next step:** the owner's test
of 0.2.0 (a gun and the GPS fitted with it, shown and working; other items still refused) and 0.1.9 (lock behaviour, pitch feel; tune the defaults from it) and of 0.1.8 (gate lean: the `panel fitted ... gate leaning L / R`
log line, L and R about -0.1), then the flight checks still open (below).

History:

- **0.1.0** (owner's first test, Open World in a Multiplayer room): the cockpit build threw in `BuildStick` (the grab events are
  serialized fields, null on a part added in code), the half-built cockpit was not cleaned up and the build was retried every
  0.25 s, so the dash was a mess and no airframe; the stick also sat on the wheel's middle, near the screen's bottom edge. The owner
  wants the car to look like a helicopter from outside, not a car with a rotor.
- **0.1.1** on `claude/practical-thompson-8lxknd`: events created before use; a failed build is cleaned up and not retried until
  the next race; the stick's grip rests just above the dials' bottom edge; `Airframe` replaces `Rotor` (car hidden, helicopter
  built). Compiles; **not tested in game.**
- **0.1.1 test** (owner, screenshots): flies; the model was squashed flat (fuselage sized from the car's height); the cockpit
  camera sat inside the canopy with no cabin modelled, and the glass was opaque from inside.
- **0.1.2** (owner's test): the inside was see-through (the cabin was drawn only for a camera it took for the cockpit's, and in game
  that never matched) and the pod was a ball (height from the car's width and the head's height). Neither had been looked at.
- **0.1.3** on `claude/practical-thompson-8lxknd`: `Shape.cs` builds everything as meshes; pod a lofted helicopter shape (rounded
  nose, cabin, tail cone into the boom, engine housing), never rounder than 2:1; the cabin always drawn (hidden only for the
  rear-view camera); roof, pillars, floor, seats, panel inside. Checked with `tools/preview --model` (`docs/model.png`), compiles;
  **not tested in game.**
- **0.1.3 test** (owner, screenshots): cabin seen from the seat, exterior fine; but a big dark spike ahead (the cabin floor's last
  strip running into the nose tip under the glass, which was cut by angle round each ring), messy glass edges at the front, the
  windscreen an empty hole, and two grey slanted marks above the dials (the wheel's lock-button sprites, which
  `NSteeringWheel.Update` turns back on every frame).
- **0.1.4** on `claude/practical-thompson-8lxknd`: windows cut at level heights with their own mesh columns (clean edges, no chin
  bubble); the windscreen's bottom lowered (bubble nose) with a grey glareshield shelf; a panel pedestal with gauges, radio,
  lights and switches; tinted glass with glare bands seen from inside; the lock buttons hidden every frame. Checked with
  `--model` and `--close`, compiles; **not tested in game.**
- **0.1.4 test** (owner, one screenshot, Open World): the seat view is good, but the stick and levers float over the cabin panel
  (not attached to anything); the car's dash read-outs still show (speed/rev dials with the digital speed: `SpeedDisplay`; the "N"
  gear/grip box: `ShifDials`; the dash's `RearViewMirror`); and a slanted dark bar by the left pillar, the car's own
  `LapDeltaMeter` (sprites in the body model, which `Airframe` didn't hide: it hid only meshes). **Keep the board at the top right**
  (owner: leaderboard + lap counter, the Open World's `EventBoard`).
- **0.1.5** on `claude/practical-thompson-8lxknd`: `Cockpit.FindDashParts` hides each read-out's group under `CarInterior` (from
  `CarLink.dialrpm/dialspeed/gearIndicator/indicator*`, `SpeedDashboard`, sprites named `rearview*`), never a group holding the
  wheel, shifters, a `Grabbable`, `StatusInfo` or `LapCounter`; the money-shift/stall lights off every frame (`CarLink` sets them
  each frame); `Airframe` hides `LapDeltaTimerDisplay`'s renderers too. The stick's foot is on the screen's bottom edge (measured
  once from `InteriorCamera`, `ScreenBottom`), a bigger floor boot half showing, the shaft pivoting there; the levers on
  `Art.Console` (plate, recess per slot, colour tags, seams) running 24 pixels past the bottom edge. Checked with `tools/preview`
  (`docs/preview.png`), compiles; **not tested in game.**
- **0.1.5 test** (owner, screenshot + the car's own cockpit for comparison): better; read-outs, mirror and slanted bar gone, but a
  white `||` was left over the panel (the gear box's tyre monitor / shift arrows: their scripts switch sprites on every frame); the
  stick must be **2-3x bigger** (its mount out of sight is fine); the console's levers "don't fit the cockpit" (flat, tall, thin).
  The car's style: chunky shaded sprites, the H-shifter a speckled grey plate in perspective (wider at the front) with dark slots
  and a big knob up on a shaft.
- **0.1.6** on `claude/practical-thompson-8lxknd`: the read-outs' renderers off every frame (`HideDash`); the stick ~2.5x (grip
  28x40 dash pixels with ridges, trigger, hat switch; shaft 6 thick; foot 20 pixels below the screen's edge, out of sight; gate
  +-16); the levers stand out of `Art.Gate` (speckled grey trapezoid 66/52 wide, 62 high, dark front face, slots with quarter
  ticks, yellow hover mark, red full mark), front edge 4 pixels below the screen's edge, as `Art.Lever` (collar in the slot,
  24-pixel shaft, knob: collective black head with a yellow/black band, throttle a red twist grip). Checked with `tools/preview`
  and laid over the owner's screenshot (sizes fit the panel), compiles; **not tested in game.**
- **0.1.6 feedback** (owner): it won't look good against the textured instrument panel: the stick stands on a gauge, and the
  panel's pixels were ~5x the dash's (64x24 over the whole face).
- **0.1.7** on `claude/practical-thompson-8lxknd`: `Panel.cs` draws the face at one texture pixel per dash pixel in the dash's style;
  `Cockpit.Areas` (boxes the grip's swing, the shaft and the levers+gate cover, in the interior's space) are taken to the screen by
  the dash camera and back out through the head camera (`MainCamera` under the car) onto the face's plane (`Airframe.FitPanel`,
  called by `HeliDriver` after both are built); the face is plain there (grey console under the gate), gauges/strips centred in the
  free stretches above the screen's bottom edge; logs `panel fitted, WxH, seen from v, clear [u0-u1 x v0-v1]...`. Falls back to
  `Panel.GuessZones`. Checked with `tools/preview -- --panel` and `--close`, compiles; **not tested in game.** Assumes the head
  camera looks ahead at the build (a head turned then would misplace the zones; it is not redone).
- **0.1.7 test** (owner, screenshot + log): "really good ... I love it". The panel fitted round the controls (log: 258x159 pixels,
  seen from 0.57, stick zones u -0.10..0.17, console u 0.43..0.79; dash pixel 0.037 on that window). Two points: the lever gate is
  "slightly off-angle" (it was a symmetric trapezoid; the console behind, seen from the seat left of centre, leans ~7 degrees left
  going up), and the levers shouldn't line up with their slots, since they hide them. The log's real car measures are now in
  `Model.Car` (pod 7.59 x 3.03 x 2.58 in game, 7.59 x 3.04 x 2.58 in the preview).
- **0.1.8** on `claude/practical-thompson-8lxknd`: `Airframe.FitPanel` also measures the console's lean at the gate's two front
  corners (a step up the face, through the head camera and back into the dash at the gate's depth) and calls `Cockpit.FitGate`: the
  plate's sides and the slots lean that way (`Art.Gate(..., slopeLeft, slopeRight)`) and the levers' axis turns with them
  (`LinearObject.anchorAngle` is read live; its anchor is fixed at wake, at the slot's bottom; `maxLimit` lengthened so the travel
  is the same height). The levers' shafts lean out 10 pixels (`Art.LeverLean`), the knobs beside the slots; their colliders
  follow. Checked with `tools/preview`, compiles; **not tested in game.**
- **Waiting on the owner:** in-game test of 0.1.8 (gate angle, levers beside their slots, still grabbed by the knobs); and of 0.1.6 (stick size, levers' look, the `||` gone, the board still there; the stick and
  levers still grab and work); still open from 0.1.4: the nose from outside, and the log line `airframe built on body ... head ... pod ...` (the real car measures for
  `Model.Car`). Earlier checks still open: controls placement and grab, lift-off at about half collective, hover, landing, the
  garage on dropping the collective, other items refused. Unknown: whether the interior camera moves (head look) enough to show the
  stick's foot or the console's end.
- **Unknowns to watch:** the controls' placement and size on each car (from the hidden parts' bounds); the Open World's garage UI may
  still show items as chosen that the lock keeps off; run boons and a starting kit's permanent stats are not removed.
