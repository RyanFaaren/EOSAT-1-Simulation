# EOS SAT-1 VR — Unity project, part 1: the scene and the data link

A VR view that puts you next to EOS SAT-1 (EOSAT-1), life size, with the Earth, Sun, Moon
and stars where they really are. It turns the spacecraft exactly as the simulator says,
lights it from the Sun's direction, dims it in Earth's shadow, and shows the solar panels
and the antenna deploying (or jamming).

The spacecraft is the real EOS SAT-1 as far as public pictures and figures show it: the
diamond-shaped body in gold foil, the two DragonEye cameras, the two solar panels on the
rear faces, sun sensors, magnetometer booms, star cameras and the separation ring. It is
built from one parts list, `Data/eosat1_model.json`, which the CAD assembly (next step)
will reuse.

## Quick start (about 10 minutes)

1. **Unity 6** (6000.0 LTS or newer; 2022.3 LTS should also work). New project from the
   **Universal 3D** template.
2. Copy the `EOSAT1` folder from this zip anywhere under your project's `Assets` folder
   (`Assets/EOSAT1` is simplest). Wait for Unity to compile.
3. Menu **EOSAT-1 > Build Demo Scene**. This creates the materials, meshes and
   `EOSAT1/Scenes/EOSAT1_Demo.unity` next to the scripts, and opens it.
4. Press **Play**. You are behind EOS SAT-1, which is tumbling just after separation, with
   the Earth 500 km below. Press `=` a few times to speed time up and watch slide 23 play
   out: antenna at 600 s, turn to face Earth at 900–1200 s, panels at 1300–1430 s, eclipse
   about 1980–4000 s, 25° roll to image at 4300–4800 s.

> The C# has been compiled outside Unity against stand-ins for Unity's API, which catches
> most mistakes, but not inside Unity itself. If the Console shows an error, paste it to me.

**Updating from an earlier version:** copy the new files over the old ones, then use
**EOSAT-1 > Reset Materials and Rebuild** (it makes the new spacecraft materials and
textures settings). If an older build left a separate `Assets/EOSAT1` folder that holds
only `Generated`, `Materials` and `Scenes`, delete it.

## Main menu (mission control room)

Pressing Play (or starting a build) opens `Scenes/EOSAT1_Menu.unity` first: you stand at the
back of a mission control room, facing three wall screens.

- **Centre screen** is the menu: the title, **Start Simulation**, **Settings** and **Quit**.
- **Left screen** shows the ground track of a 500 km sun-synchronous orbit, **right screen**
  the spacecraft status and the data source you picked.
- **Desktop:** point with the mouse and click. `Enter` starts, `Esc` leaves Settings.
- **Headset:** point either controller and pull its trigger (or press `A` / `X`); the left
  stick walks you around the room. Without controllers the pointer follows your head.
- In the simulation, `Esc` returns to the menu.

**Settings** are remembered between runs and apply when you start from the menu: data source
(Sandbox / Replay / Live link), time speed at start (1x / 10x / 60x), info panel and control
hints shown or hidden, and the headset's snap-turn angle. `eosat1_link.json` and the
`-eosat...` command-line options still override them.

The room is built by code when the scene starts (`Scripts/Unity/Menu`), so the scene holds
one object, **Mission Control Menu**; its Inspector has the title, subtitle and accent colour.
In the Editor's Scene view the room only appears while playing.

| EOSAT-1 menu item | Does |
|---|---|
| **Open Menu Scene** | opens `EOSAT1_Menu.unity` |
| **Rebuild Menu Scene** | recreates it and puts it first in the build list |
| **Start Play Mode at Main Menu** | ticked by default: Play always starts at the menu. Untick it to play the open scene directly while working on it |

## Controls

**Mouse and keyboard** (also work while someone wears the headset)

| Key | Does |
|---|---|
| Mouse drag / wheel | orbit / zoom around the spacecraft |
| `V` / `F` | next preset view / back to the first view (the same angle as the project's reference picture) |
| `Space` | pause |
| `-` `=` | slower / faster (0.5× to 300×) |
| `←` `→` | jump back / forward 60 s (back = like reloading a saved point) |
| `Backspace` | restart the scenario |
| `1` `2` | deploy solar panel −Y / +Y |
| `3` `4` `5` | deploy antenna / inject or clear an antenna jam / stow everything |
| `N` `S` `R` `T` | point at Earth / point the panels at the Sun / roll 25° to image / tumble |
| `G` `B` | body axes / camera boresight and its footprint on Earth |
| `L` | view frame: Earth below (default) or inertial (ECI axes, up = celestial north) |
| `M` | data source: Sandbox → Replay → Live link |
| `H` | info panel |
| `C` | satellite camera view: off → corner picture → full screen (wheel or `[` `]` zooms it) |
| `Z` | zoom out to the whole Earth / back to the spacecraft |
| `O` | orbit path on / off |
| `P` | solar panel axes on / off |

The last four also have buttons at the top right of the screen (the **Views** panel).

Commands (`1`–`5`, `N S R T`) act on the built-in simulator only. With the real simulator,
commands come from mission control, as they should.

**Headset** (Quest or any OpenXR headset): walk around the spacecraft for real, or

| Control | Does |
|---|---|
| Left stick | move where you look |
| Right stick | left/right snap turn, up/down rise/sink |
| `A` / `B` | deploy the next panel / deploy the antenna (or clear a jam) |
| `X` / `Y` | info panel on the left controller / time 1× → 10× → 60× |
| Left grip / Menu | pause / restart |
| Right / left trigger | body axes / camera boresight |
| Right grip | whole Earth / back to the spacecraft |
| Right stick click | satellite camera screen (above the right controller) |
| Left stick click | orbit path on / off |

You start about 3 m behind and to the left of the spacecraft, facing it. It floats with
its centre at 1.45 m, so you look slightly down at the cameras' side and up at the top.

## Satellite camera, whole Earth and orbit path

- **Satellite camera (`C`).** What the DragonEye imagers see: a camera on the camera face
  looking along the boresight (+Z), flight direction at the top of the picture. It starts
  45° wide so you can tell where you are; zoom in as far as the real field of view, about
  5.1° (the two imagers together cover a 44 km wide strip from 500 km). A box in the middle
  marks that real view while you are zoomed out. The Earth texture is too coarse to show
  detail at the real zoom; a sharper Earth map fixes that (see Credits and limits).
- **Whole Earth (`Z`).** The Earth becomes a 1 m globe, north up, with the spacecraft going
  round it and the Moon at its true distance (30 m away on this scale; zoom out with the
  wheel). The orbit height, the Earth, the Moon and the day/night line are to scale. Only the
  spacecraft is drawn bigger (it would be a speck) and marked with a yellow dot. In the
  headset the globe floats where the spacecraft was, and you can walk around it.
- **Orbit path (`O`, on by default).** One full orbit, worked out from the spacecraft's
  position and velocity at that moment, so the line always passes through it. It is a glowing
  cyan line: bright ahead, a fading trail behind, violet inside Earth's shadow, with an amber
  dot where the eclipse starts and another where it ends.
- **Solar panel axes (`P`, on by default).** An orange arrow out of each solar panel, along
  the direction its cells face. Folded, the two panels face different ways; once both are
  out the arrows are parallel. The status panel's "Panels in sun?" row gives the matching
  numbers.

The on-screen display (status panel, key list, messages and the Views buttons) uses the
main menu's style: navy panels with a cyan border, cyan headings, amber for warnings. It is
drawn by `HudStyle`.

The satellite camera and the whole-Earth view cannot be on together; choosing one closes the
other. These views are built when the scene starts, so there is nothing to rebuild.

## The info panel answers slide 17's four questions

*Is it tumbling?* (body rate), *Is the camera facing Earth?* (degrees off nadir),
*Are the wings in the sun?* (eclipse, and how squarely each panel's cells face the Sun,
read from the model itself), *Did the antenna come out?* — plus the time, where it is
(latitude/longitude) and the link health.

## The spacecraft model

| | |
|---|---|
| Envelope | 1390 × 1130.3 × 821 mm (published), 178 kg |
| Body | a rhombic prism: long diagonal 1390 mm along Y (the two tips), short diagonal 1130.3 mm along X, 821 mm deep along the camera axis; sides 896 mm, corners 78.2° at the tips and 101.8° front and rear |
| Camera face (+Z, towards Earth) | two DragonEye imagers on the long diagonal, two small gold cups, two magnetometer booms |
| Tips (±Y) | white fine sun sensors, front and back |
| Rear faces | the two solar panels, stowed flat on the faces either side of the rear ridge (−X) |
| Top (−Z) | separation ring, two star cameras, and the course's UHF antenna (see below) |

Body axes follow the course: +X flight direction, +Y solar-wing axis (the long diagonal),
+Z camera boresight. The positions and sizes on the camera face were measured on the
project's reference picture (EOS SAT-1 with its panels closed) by fitting the model to it
through the picture's perspective: corners, lens rims and booms line up to about 1% of the
picture's width. The rest comes from the exploded view and the published figures (EOS Data
Analytics, eoPortal, Wikipedia, satcatalog).

**Two things are not public, so they are design choices — tell me if the client knows better:**

- **How the panels deploy.** Here each panel swings out 39.1° about a hinge on the rear
  ridge, until the two form one flat array facing −X (the panels then span the Y axis, as
  the course's "+Y = wing axis" expects). "Point at the Sun" (`S`) turns that array to the
  Sun and keeps the cameras as close to Earth as it allows. Stowed, the cells already
  catch about 78% of the light when the rear faces the Sun.
- **The UHF antenna.** The course simulator has a deployable antenna that can jam, but the
  real satellite shows none. Two tape elements on a small board sit on the top face so the
  course's antenna states can be shown. Remove them from the parts list if not wanted.

**Changing the model.** Everything is in `Tools/eosat1_model.py`: run it to rewrite
`Data/eosat1_model.json`, then **EOSAT-1 > Build Demo Scene** again (the scene is rebuilt
from the file; your material tweaks are kept). `Tools/preview_model.py` renders the model
without Unity (`python3 preview_model.py --view three-quarter --deploy 1`), handy for
checking a change. The panel and foil textures come from
`Tools/make_spacecraft_textures.py` (cell layouts counted on the picture: panel −Y has 3
strings × 2 rows × 18 cells, panel +Y 3 × 3 × 14).

**Moving parts** are driven by `DeployableJoint` components (hinge axis + angle, captured
stowed pose) on the `Hinge …` objects under the spacecraft; `SatelliteView` moves them from
the data. A CAD import can reuse them: put the CAD panels under the same hinge objects.

## Data sources

Pick one on the **EOSAT-1 App** object (`Feed`), or press `M` while playing.

| Feed | What it is |
|---|---|
| **Sandbox** (default) | Built-in simulator flying the slide-23 orbit. Takes commands. No network. |
| **Replay** | A recording, one JSON object per line. Ships with `Data/eosat1_one_orbit.jsonl.txt`; also plays sessions recorded with the course's `vr_client_example.py` (set `Recording Path`). |
| **Tcp** | The live link on port 8100: `vr_stub.py`, the real simulator, or the mock stub below. |

The live link follows slide 24: socket reads happen on a background thread, unreadable
lines are skipped, unknown fields ignored, duplicates dropped, the clock may jump back
(saved-point reload), frames are interpolated at normal speed and shown as jumps when time
is sped up. Once a second the headset sends back a status line with the status word:

| Bits | Meaning |
|---|---|
| 0–3 | `RX_OK`, `PARSE_OK`, `RENDERING`, `DATA_FRESH` (15 = healthy; a frozen app drops `RENDERING`) |
| 8 | headset active |
| 9 | interpolating between frames |
| 10 | showing jumps (sim time sped up) |
| 11 | eclipse shown |
| 12 | antenna jam shown |
| 13 | info panel shown |

### Testing the link without the course stub

`Tools/mock_vr_stub.py` is an unofficial stand-in for `vr_stub.py` (Python 3, no packages):

```
cd Tools
python3 mock_vr_stub.py                  # 1x, one frame a second, port 8100
python3 mock_vr_stub.py --speed 10       # sim time 10x: frames jump 10 s each
python3 mock_vr_stub.py --chaos --loop   # broken lines, garbage, duplicates, stalls, reloads
```

It prints the status lines it receives, decoded. In Unity set `Feed = Tcp`,
`Tcp Host = 127.0.0.1` (or the laptop's IP for a headset; same Wi-Fi, port 8100 open).

**On a standalone headset** you can change the link without rebuilding: create
`eosat1_link.json` containing `{"feed":"tcp","host":"192.168.1.20","port":8100}` and copy it
to the app's data folder, e.g.
`adb push eosat1_link.json /sdcard/Android/data/<your.package.name>/files/`.
Desktop builds also accept `-eosatFeed tcp -eosatHost 192.168.1.20 -eosatPort 8100`.

## Headset setup (OpenXR)

1. **Window > Package Manager > Unity Registry**: install **XR Plug-in Management** and
   **OpenXR Plugin**.
2. **Edit > Project Settings > XR Plug-in Management**:
   - *Windows* tab: tick **OpenXR**. Under **OpenXR > Enabled Interaction Profiles** add
     **Oculus Touch Controller Profile** (and **Meta Quest Touch Plus** for Quest 3).
   - *Android* tab (standalone Quest): tick **OpenXR** and the **Meta Quest** feature group,
     same interaction profiles.
3. **Quest Link from the editor**: in the Meta Quest Link PC app make it the active OpenXR
   runtime, connect the headset, start Link, press Play.
4. **Standalone build**: switch the platform to Android, then follow Meta's "set up Unity
   for Quest" checklist for the Player settings (IL2CPP, ARM64, minimum API level). The
   scene builder already turns on the Internet permission the link needs.

The rig uses Unity's built-in XR input, so no XR Interaction Toolkit is needed. You can
swap in the XRI "XR Origin" later if you want teleporting or grabbing.

## How the picture is built (and the conventions)

- **Axes (slide 21).** ECI is J2000, right-handed, Z through the North Pole. Body: +Z
  camera face, +X flight direction, +Y solar-wing axis. Quaternions `[x, y, z, w]`,
  body → ECI. Conversion to Unity happens in one file, `SimToUnity.cs`:
  vector `(x, z, y)`, quaternion `(−x, −z, −y, w)`. Checked numerically: when the
  spacecraft points at Earth, its camera face looks straight down.
- **Scale (slide 24, "cheat the distances").** The spacecraft is life size at the centre.
  Everything else is shrunk *around the viewer* (1 km → 1 m), which keeps every angle
  exact: the Earth fills the sky it would from 500 km, its edge 22° below the horizontal,
  the Sun and Moon 0.5° across. The Earth's surface is ~500 m away, far enough that your
  eyes read it as "very far".
- **View frame.** By default down is towards Earth's centre and forward is the flight
  direction, so you ride behind the spacecraft with the planet under your feet. `L`
  switches to fixed ECI axes for checking conversions.
- **Sun, eclipse, Moon, Earth.** The Sun lights the spacecraft from the direction in the
  frame and fades out in Earth's shadow (`in_eclipse`, or the exact sunlit fraction when
  the source sends one); the Earth keeps its own day/night terminator, city lights, sun
  glint and a limb glow, so during an eclipse you see the night side with a bright rim.
  The Earth turns by sidereal time (with precession to J2000); the Moon is placed from a
  low-precision ephemeris and lit by the Sun, so it shows the right phase. Earthshine lights
  the Earth-facing side.

## What I had to assume (please check against `VR_INTERFACE_ICD.pdf`)

| Topic | Assumed |
|---|---|
| ECI frame | J2000. If the simulator uses TEME/true-of-date, set `Space > Eci Convention = OfDate` (0.36° difference). |
| Second wing | `deploy.wing_py` (the slides only show `wing_my`). `wing_my` is the panel on the −Y side. |
| Velocity | optional `orbit.vel_eci_km_s`; if absent it is worked out from positions. |
| Status line | `{"type":"status","status_word":15,"last_seq":...}`; field names are a guess. |
| Sun pointing | the sandbox's "point at the Sun" turns the deployed array (−X) to the Sun, as described above. |

If any of these differ, it's a one-line change; send me the ICD and I'll match it exactly.

## Files

```
EOSAT1/
  Scripts/Core/     plain C#, no Unity: maths, time & ephemeris, orbit, JSON, frame parser,
                    FrameTimeline (interpolation/jumps), TcpFeedClient, JsonlReplay, SandboxSim
  Scripts/Unity/Menu/  MissionControlMenu (the menu and its screens), MissionControlRoom (the room),
                    MenuKit (quads, text, buttons), MenuSettings (remembered choices)
  Scripts/Unity/    EosatApp (conductor), SpaceScene (Earth/Sun/Moon/stars placement),
                    SatelliteView + DeployableJoint, InfoPanel, DesktopControls, VrRig,
                    ViewDirector (satellite camera, whole Earth, panel axes, buttons), OrbitPathView,
                    HudStyle (the on-screen display's look),
                    InputShim, SimToUnity, ProceduralMeshes, FaceCamera
  Editor/           EosatSceneBuilder (the EOSAT-1 menu; builds the spacecraft from the parts list)
                    EosatMenuTools (menu scene helpers; Play starts at the main menu)
  Resources/        EosatScreen.shader (the satellite camera screen in the headset)
  Shaders/          Planet, Atmosphere, SunGlow, Stars, UnlitColor, GlowAdditive, GridFloor, HudText
                    Room, UiPanel, MapScreen, ConsoleScreen (main menu)
  Textures/         earth_day, earth_night, moon; panel_a/panel_b (+ _mask), mli_gold (+ _normal)
  Data/             eosat1_model.json (the spacecraft), eosat1_one_orbit.jsonl.txt (5,671 frames),
                    bright_stars.txt
Tools/
  eosat1_model.py               the EOS SAT-1 parts list (writes Data/eosat1_model.json)
  preview_model.py              renders the parts list without Unity
  make_spacecraft_textures.py   solar panel and gold-foil textures
  eosat_physics.py              reference physics (same formulas as the C#)
  make_one_orbit.py             regenerates the one-orbit recording and runs physics checks
  mock_vr_stub.py               TCP stand-in for vr_stub.py, with --chaos
  make_textures.py              Earth, night lights, Moon, stars; geo_data.py holds the geography
```

## Credits and limits

- EOS SAT-1 figures: EOS Data Analytics, eoPortal, Wikipedia, satcatalog. Geometry fitted
  to the project's reference picture and exploded view. Internal parts are not modelled
  yet.
- Earth textures are generated by `make_textures.py` from hand-entered coastlines (accurate
  to about half a degree around recognisable features) and a city list. For real detail swap
  `earth_day.png` for NASA's Blue Marble (public domain, visibleearth.nasa.gov) or an Asset
  Store Earth with the same layout (longitude −180 at the left, north at the top).
- Named stars (to about magnitude 3) are at their real J2000 positions; fainter stars are
  random filler, denser along the Milky Way.
- Sun and Moon: Astronomical Almanac / Vallado low-precision formulas (Sun ~0.01°,
  Moon ~0.3°). Earth's shadow: conical umbra/penumbra (Montenbruck & Gill).
- The sample orbit is the course's slide-23 timeline flown in a morning sun-synchronous
  orbit (descending node 09:43, β = 31°) on 2026-08-20 from 12:00 UTC; the Sun direction at
  12:30:12 matches the sample frame on slide 20.

## Troubleshooting

- **Compile errors**: paste them to me (see the note at the top).
- **A message about missing files after building**: the `Textures` and `Data` folders must
  sit next to `Scripts` inside the EOSAT1 folder.
- **Pink materials**: the scene was built before URP was active, or with another pipeline.
  Use **EOSAT-1 > Reset Materials and Rebuild**.
- **Plain white Earth or spacecraft**: same fix; it also happens if the textures were added
  after the first build.
- **Something renders in one eye only**: tell me which object; all custom shaders are
  written for single-pass stereo, but it is the first thing to check on a new headset.
- **Headset can't connect**: same Wi-Fi as the laptop, laptop firewall allows inbound
  TCP 8100, and prove it with `vr_client_example.py` (or `mock_vr_stub.py`) first.
- **"InvalidOperationException ... UnityEngine.Input"**: not from these scripts
  (they support both input systems); check other scripts in the project.
