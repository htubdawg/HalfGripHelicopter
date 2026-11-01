# HalfGripHelicopter

A Half Grip (Steam app 4163270, Unity, Windows) mod: a chassis item, **Helicopter** (id `helicopter.heli`), that fits alone, replaces the
cockpit's wheel and shifter with a stick, a collective and a throttle, and flies the car. Made mainly for the Open World mod
(`htubdawg/HalfGripOpenWorld`). Current release: see `Helicopter/mod.json`.

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
| `Cockpit.cs` | Hides wheel, all shifters, handbrake (renderers + `InteriorCollider`s off, objects alive, re-hidden every 0.5 s); stick = `DoubleLinearObject` (spring off while held, on when let go, right click toggles `frozen`), levers = `LinearObject` with no spring; placed and sized from the hidden parts' bounds and the dash's pixel size; shaft redrawn in whole pixels |
| `Flight.cs` | The flight model (see its summary): thrust `m g 2 C rpm²`, climb-rate damping `g/ClimbRate`, attitude hold (5 rad/s, critically damped), yaw from the stick + tail fin, drag balanced at `TopSpeed` |
| `Shape.cs` | The helicopter's geometry as plain triangles per `Paint` (Unity's managed structs only, so `tools/preview` draws it too): `Measure` sizes the pod from the car (length >= 0.62 car lengths, height 0.4-0.5 of the length, head 57% up and 40% back), `Build` gives the outer skin + outside parts, the cabin (inner skin facing in with holes at the glass, floor, seats, panel), the main and tail rotors. No `Quaternion.Euler` (native; use `AboutX`/`AboutY`) |
| `Airframe.cs` | Hides the car's skin models (`CarSkinController.carModels`) and wheel meshes (re-hidden every 0.5 s, shown again on removal); turns `Shape`'s pieces into meshes on the body's layer, one URP Lit material per paint, `_Cull` Back, speckle texture inside; the head is the car's child camera tagged MainCamera; the cabin is always drawn except for the camera named `RearViewCamera` (`beginCameraRendering`); rotors spin with `Flight.Rpm` |
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

## Checking changes

- Compile (before every push), with HalfGripTools built against the refs:
  ```
  dotnet build <HalfGripTools>/HalfGripTools/HalfGripTools.csproj -c Release -p:GameDir=<halfgriprefs>/game
  tools/check.sh <halfgriprefs>/game <HalfGripTools>/dist/BepInEx/plugins/HalfGripTools/HalfGripTools.dll
  ```
- Pictures: `dotnet run -c Release --project tools/preview -p:Refs=<halfgriprefs> -- docs/preview.png` (exits 1 on a blank icon).
  Look at it after any art change.
- Model: `dotnet run -c Release --project tools/preview -p:Refs=<halfgriprefs> -- --model docs/model.png` draws `Shape` with back-face
  culling and depth, from outside and from the head (ahead, left, right, the mirror back), for the game's car and a smaller one.
  **Look at it after any change to the shape**, before the owner does.
- In game (the owner tests): copy `Helicopter/` to `<game>\BepInEx\HalfGripMods\`, set `Debug.VerboseLog=true`, read
  `BepInEx\LogOutput.log` (look for `cockpit built`, `airframe built`, `on the car`, `linked to the Open World's garages`; a failure logs `could not put
  the helicopter on the car` once a race).

## Status (keep current)

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
- **Waiting on the owner:** in-game test of 0.1.3: the outside, the view from the seat, the mirror; the log line `airframe built on
  body ... head ... pod ...` (gives the real car measures to put into `Model.Car`; `(guessed)` means the camera was not found).
  Earlier checks still open: controls placement and grab, lift-off at about half collective, hover, landing, the garage on dropping
  the collective, other items refused.
- **Unknowns to watch:** the controls' placement and size on each car (from the hidden parts' bounds); the Open World's garage UI may
  still show items as chosen that the lock keeps off; run boons and a starting kit's permanent stats are not removed.
