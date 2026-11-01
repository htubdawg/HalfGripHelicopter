# HalfGripHelicopter

A Half Grip (Steam app 4163270, Unity, Windows) mod: a chassis item, **Helicopter** (id `helicopter.heli`), that fits alone, replaces the
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
| `HeliItem.cs` | The item: a clone of chassis `aero_1` (or any chassis) with stock numbers, no downforce, a drawn icon; `Fitted`/`Current` read `PlayerLoadout` |
| `Lock.cs` | Fits alone: `InventorySlot.OnDrop` (refuse others / clear others), `LoadoutSpawner.Awake` (cut the loadout to the Helicopter, stats again), `LoadoutRefit.Swap` (live refits); `CarLink.FixedUpdate` skipped while flying; no `ACH_BIG_AIR`; the Open World's `ShifterHand` patched by name |
| `HeliDriver.cs` | Every 0.25 s: the player's `CarLink` + item fitted → builds cockpit, flight, rotor; takes them off when not; `DriveCar` in place of the car's driving (brakes when parked, `engineIndicator` = rotor speed) |
| `Cockpit.cs` | Hides wheel, all shifters, handbrake (renderers + `InteriorCollider`s off, objects alive, re-hidden every 0.5 s; the wheel's lock-button sprites every frame, since `NSteeringWheel.Update` turns one back on); stick = `DoubleLinearObject` (spring off while held, on when let go, right click toggles `frozen`), levers = `LinearObject` with no spring; placed and sized from the hidden parts' bounds and the dash's pixel size; shaft redrawn in whole pixels |
| `Flight.cs` | The flight model (see its summary): thrust `m g 2 C rpm²`, climb-rate damping `g/ClimbRate`, attitude hold (5 rad/s, critically damped), yaw from the stick + tail fin, drag balanced at `TopSpeed` |
| `Shape.cs` | The helicopter's geometry as plain triangles per `Paint` (Unity's managed structs only, so `tools/preview` draws it too): `Measure` sizes the pod from the car (length >= 0.62 car lengths, height 0.4-0.5 of the length, head 57% up and 40% back), `Build` gives the outer skin + outside parts, the cabin (inner skin facing in with holes at the glass, glareshield shelf, panel pedestal with a drawn face, floor, seats), the panes (glass seen from inside), the main and tail rotors. Windows are cut at level heights; every edge level owns a mesh column on every ring (`Angles`), so edges are clean. Pixel arrays for the speckle, glass and panel textures. No `Quaternion.Euler` (native; use `AboutX`/`AboutY`) |
| `Airframe.cs` | Hides the car's skin models (`CarSkinController.carModels`) and wheel meshes (re-hidden every 0.5 s, shown again on removal); turns `Shape`'s pieces into meshes on the body's layer, one URP Lit material per paint, `_Cull` Back, speckle and panel textures; the panes in the game's `Sprite-Unlit-Default` (alpha blended, always in the build, both sides; hidden from outside by the opaque outer glass); the head is the car's child camera tagged MainCamera; the cabin and panes are always drawn except for the camera named `RearViewCamera` (`beginCameraRendering`); rotors spin with `Flight.Rpm` |
| `Art.cs` | `Canvas` (pixel drawing) and the pictures: icon, grip, boot, slot, handles |

## Rules that matter

- The item id `helicopter.heli` is saved in runs: never change it.
- **No keybindings** (owner): everything is worked by the cockpit hand. W/S do nothing while flying.
- The stick springs to the middle when let go (the helicopter levels and hovers); right click holds it. The levers keep their place.
- Fits alone: keep every route closed (garage slot, race spawn, live refit). Do not invent other routes around `LoadoutRefit.Swap`.
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
- Unity's front faces are clockwise; `Vector3.Cross(b - a, c - a)` points at the viewer. The URP Lit materials cull back faces.
- See-through materials: the game ships `Universal Render Pipeline/2D/Sprite-Unlit-Default` (alpha blended, both sides) everywhere;
  a transparent URP Lit variant may be stripped from the build, so don't use one.
- Native Unity calls (`Quaternion.Euler`, `AngleAxis`, ...) throw in `tools/preview`; keep `Shape.cs` to managed struct code.

## Owner's wishes (for the look)

- Exterior: the green / blue glass / dark metal helicopter is liked ("exterior is fine"); glass opaque from outside.
- From the seat you must see out, and the cabin must look modelled and textured: no empty holes, no big dark shapes in the view
  (0.1.3's "giant black spike"), clean window edges ("front very messy" in 0.1.3).
- Wanted: textured controls/panel, glass that looks like glass.
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
  Look at it after any art change.
- Model: `dotnet run -c Release --project tools/preview -p:Refs=<halfgriprefs> -- --model docs/model.png` draws `Shape` with back-face
  culling and depth, from outside and from the head (ahead, left, right, the mirror back), for the game's car and a smaller one.
  **Look at it after any change to the shape**, before the owner does. `-- --close <png>` draws the seat view and the nose large
  (`docs/close.png`).
- In game (the owner tests): copy `Helicopter/` to `<game>\BepInEx\HalfGripMods\`, set `Debug.VerboseLog=true`, read
  `BepInEx\LogOutput.log` (look for `cockpit built`, `airframe built`, `on the car`, `linked to the Open World's garages`; a failure logs `could not put
  the helicopter on the car` once a race).

## Status (keep current)

**Now:** 0.1.4 pushed to `claude/practical-thompson-8lxknd` (no PR yet: the owner hasn't asked for one). Waiting on the owner's
in-game test of 0.1.4. **Next step:** read their screenshots and the `airframe built` log line; put the real body bounds, ground
and head into `Model.Car` in `tools/preview/Model.cs`, re-render, and fix what they report.

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
- **Waiting on the owner:** in-game test of 0.1.4: the view from the seat (no spike, panel, glass tint), the nose from outside,
  the grey marks gone, the mirror; and the log line `airframe built on body ... head ... pod ...` (the real car measures for
  `Model.Car`). Earlier checks still open: controls placement and grab, lift-off at about half collective, hover, landing, the
  garage on dropping the collective, other items refused.
- **Unknowns to watch:** the controls' placement and size on each car (from the hidden parts' bounds); the Open World's garage UI may
  still show items as chosen that the lock keeps off; run boons and a starting kit's permanent stats are not removed.
