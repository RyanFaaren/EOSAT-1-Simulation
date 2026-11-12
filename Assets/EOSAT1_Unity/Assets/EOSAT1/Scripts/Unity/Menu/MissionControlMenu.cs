// EOSAT-1 VR - the main menu. The first thing you see: a mission control room with a
// screen for everything, all around you:
//   front        the menu (title, Start, Settings, Mission Info, Quit)
//   front left   ground track of the 500 km sun-synchronous orbit on a world map
//   front right  spacecraft status
//   right        Settings (language, data source, speed, panels, snap turn)
//   left         Mission Info (launch timeline)
//   behind you   a 3D hologram of the Earth with the orbit and the satellite going round,
//                with two Controls screens either side of it: keyboard and mouse on
//                the right, VR headset on the left. Point at the Earth and hold the
//                trigger (or click and drag with the mouse) to spin it round.
// Start fades out and loads the simulation scene. Every word is in English and French
// (MenuText at the bottom of this file; add a column there for another language).
//
// Desktop: point with the mouse and click. A / D, the arrow keys or right-drag turn you
//          round; Enter = start, Esc = face the menu again.
// Headset: look around; point either controller and pull its trigger (or press A / X).
//          The left stick walks you around the room.
//
// The whole room is built at run time from this one component, so the menu scene holds a
// single object. It needs no UI package, no colliders and no pipeline lights.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Eosat1
{
    [DefaultExecutionOrder(10)]   // after VrRig, so the controller poses are this frame's
    public sealed class MissionControlMenu : MonoBehaviour
    {
        [Header("Shaders (found by name when left empty)")]
        public Shader roomShader;
        public Shader panelShader;
        public Shader mapShader;
        public Shader consoleShader;
        public Shader textShader;
        public Shader unlitShader;

        [Header("Look")]
        [Tooltip("Equirectangular Earth map: longitude -180 at the left, north at the top.")]
        public Texture2D earthMap;
        public Color accent = new Color(0.25f, 0.8f, 1f, 1f);
        public string title = "EOSAT-1";
        public string subtitle = "VR MISSION SIMULATOR";
        public float eyeHeight = 1.6f;

        [Header("Simulation")]
        [Tooltip("Name of the scene that Start loads. It must be in the Build Settings list.")]
        public string simulationScene = MenuSettings.SimulationSceneName;

        // orbit drawn on the map: 500 km sun-synchronous
        const float InclinationRad = 97.4f * Mathf.Deg2Rad;
        const float EarthTurnPerOrbitRad = 94.6f / 1436.07f;   // orbit period / sidereal day
        const float SecondsPerMapOrbit = 100f;                  // sped up so the marker visibly moves
        const float StartLongitudeRad = -1.9f;

        static readonly Color TextBright = new Color(0.93f, 0.96f, 1f, 1f);
        static readonly Color TextDim = new Color(0.55f, 0.66f, 0.76f, 1f);
        static readonly Color Amber = new Color(1f, 0.82f, 0.4f, 1f);
        static readonly Color TrackColor = new Color(1f, 0.8f, 0.25f, 1f);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        Font font;
        Material textMaterial;

        // rig
        Transform head, leftHand, rightHand;
        Camera cam;
        float lookYaw, lookPitch;      // small lean towards the mouse
        float viewYaw, targetYaw;      // which way you face on the desktop (0 = the menu)

        // where the screens are (yaw from the middle of the room)
        const float SettingsYaw = 90f;
        const float InfoYaw = -90f;
        const float HologramYaw = 180f;
        const float ControlsYaw = -133f;          // keyboard and mouse screen (the headset one is at +133)
        static readonly Color ActionText = new Color(0.8f, 0.87f, 0.94f, 1f);

        // screens
        struct ScreenRect
        {
            public Transform Plane;
            public Vector2 Size;
        }
        readonly List<ScreenRect> screens = new List<ScreenRect>();
        readonly List<MenuButton> buttons = new List<MenuButton>();
        readonly MenuButton[] settingButtons = new MenuButton[6];
        MenuButton hovered;
        TextMesh hintText, feedDescription, clockText, statusValues, goText, trackReadout;
        string hintOverride;

        // ground track
        Vector2 mapSize;
        Mesh trackMesh;
        readonly List<Vector3> trackVerts = new List<Vector3>(1024);
        readonly List<int> trackTris = new List<int>(1536);
        Transform marker, markerRing;
        Material markerRingMaterial;
        float orbitPhase = 0.9f;
        float nextSlowRefresh;

        // 3D orbit hologram (you can grab it and spin it)
        Transform holoCentre, holoGlobe, holoMarker;
        bool grabbing;
        Vector3 grabPrevious;
        Vector3 spinAxis = Vector3.up;
        float spinSpeed;                       // degrees per second, left over after letting go
        const float HoloRadius = 0.5f;
        const float HoloAltitudeScale = 4f;   // 500 km drawn 4x higher so the gap shows

        // pointer
        Transform laser, reticle;
        bool useRightHand = true;
        bool prevTriggerL, prevTriggerR, prevPrimaryL, prevPrimaryR;

        // fade and sound
        Material fadeMaterial;
        MeshRenderer fadeRenderer;
        float fadeAlpha = 1f, fadeTarget;
        AudioSource audioSource;
        AudioClip tickClip, clickClip;
        bool busy;

        // ------------------------------------------------------------------ life cycle
        void Awake()
        {
            roomShader = Resolve(roomShader, "EOSAT1/Room");
            panelShader = Resolve(panelShader, "EOSAT1/UiPanel");
            mapShader = Resolve(mapShader, "EOSAT1/MapScreen");
            consoleShader = Resolve(consoleShader, "EOSAT1/ConsoleScreen");
            textShader = Resolve(textShader, "EOSAT1/HudText");
            unlitShader = Resolve(unlitShader, "EOSAT1/UnlitColor");

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            font = LoadFont();
            textMaterial = MenuKit.NewMaterial(textShader, "Menu Text");
            AssignFontTexture();

            BuildAll();
        }

        void BuildAll()
        {
            BuildRig();
            MissionControlRoom.Build(transform, roomShader, consoleShader, accent);
            BuildCentreScreen();
            BuildMapScreen();
            BuildStatusScreen();
            BuildSettingsScreen();
            BuildInfoScreen();
            BuildOrbitHologram();
            BuildControlsScreen();
            BuildSignage();
            BuildPointer();
            BuildFade();
            BuildSounds();

            RefreshSettings();
            UpdateSlowDisplays();
        }

        /// <summary>Throw the room away and build it again, so every word follows a new language.</summary>
        void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            screens.Clear();
            buttons.Clear();
            for (int i = 0; i < settingButtons.Length; i++) settingButtons[i] = null;
            hovered = null;
            grabbing = false;
            spinSpeed = 0f;
            hintOverride = null;
            BuildAll();
            fadeAlpha = 0.7f;   // a short blink instead of the full fade-in
            fadeTarget = 0f;
            ApplyFade();
            Play(clickClip);
        }

        void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
        }

        void OnDisable()
        {
            Font.textureRebuilt -= OnFontRebuilt;
        }

        void OnFontRebuilt(Font f)
        {
            if (f == font) AssignFontTexture();
        }

        void AssignFontTexture()
        {
            if (font != null && font.material != null && textMaterial != null)
                textMaterial.SetTexture("_MainTex", font.material.mainTexture);
        }

        static Shader Resolve(Shader assigned, string shaderName)
        {
            if (assigned != null) return assigned;
            Shader s = Shader.Find(shaderName);
            if (s != null) return s;
            Debug.LogError("EOSAT-1 menu: shader '" + shaderName + "' was not found. Check that the EOSAT1/Shaders folder is in the project.");
            return Shader.Find("Hidden/InternalErrorShader");
        }

        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (Exception) { f = null; }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }   // Unity 2021 and older
                catch (Exception) { f = null; }
            }
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 64);
            return f;
        }

        // ------------------------------------------------------------------ building
        void BuildRig()
        {
            var rigGo = new GameObject("Player Rig");
            rigGo.transform.SetParent(transform, false);
            Transform offset = MenuKit.Group("Camera Offset", rigGo.transform, Vector3.zero);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            head = camGo.transform;
            head.SetParent(offset, false);
            head.localPosition = new Vector3(0f, eyeHeight, 0f);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.004f, 0.006f, 0.012f, 1f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();

            Material handMaterial = MenuKit.NewMaterial(roomShader, "Menu Controller");
            handMaterial.SetColor("_Color", new Color(0.5f, 0.55f, 0.62f));
            handMaterial.SetColor("_Emission", new Color(0.03f, 0.06f, 0.08f));
            handMaterial.SetFloat("_Gloss", 0.4f);
            leftHand = BuildHand("Left Controller", offset, handMaterial);
            rightHand = BuildHand("Right Controller", offset, handMaterial);

            // the same light rig as the simulation: tracks the headset and controllers, and
            // lets you walk the room with the left stick (no rising or sinking in here)
            var rig = rigGo.AddComponent<VrRig>();
            rig.cameraOffset = offset;
            rig.head = head;
            rig.leftHand = leftHand;
            rig.rightHand = rightHand;
            rig.fallbackEyeHeight = eyeHeight;
            rig.verticalSpeed = 0f;
            rig.moveSpeed = 1.2f;
        }

        static Transform BuildHand(string name, Transform parent, Material mat)
        {
            Transform hand = MenuKit.Group(name, parent, Vector3.zero);
            MenuKit.BoxObject("Body", hand, mat, new Vector3(0f, -0.01f, -0.03f), new Vector3(0.04f, 0.035f, 0.12f));
            hand.gameObject.SetActive(false);
            return hand;
        }

        Transform BuildScreen(string name, Vector3 position, float yawDegrees, Vector2 size, bool floorStand)
        {
            Transform t = MenuKit.Group(name, transform, position);
            t.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            Material housing = MenuKit.NewMaterial(roomShader, name + " Housing");
            housing.SetColor("_Color", new Color(0.05f, 0.055f, 0.07f));
            housing.SetFloat("_Gloss", 0.3f);
            MenuKit.BoxObject("Housing", t, housing, new Vector3(0f, 0f, 0.07f), new Vector3(size.x + 0.16f, size.y + 0.16f, 0.12f));
            if (floorStand)
            {
                // a post and a foot behind the screen, down to the floor
                float bottom = -size.y * 0.5f;
                float floor = -position.y;
                MenuKit.BoxObject("Stand Post", t, housing, new Vector3(0f, (bottom + floor) * 0.5f, 0.1f), new Vector3(0.28f, bottom - floor + 0.3f, 0.1f));
                MenuKit.BoxObject("Stand Foot", t, housing, new Vector3(0f, floor + 0.03f, 0.1f), new Vector3(1.4f, 0.06f, 0.6f));
            }
            MenuKit.Panel("Background", t, panelShader, Vector3.zero, size,
                          new Color(0.03f, 0.065f, 0.115f, 1f), new Color(0.012f, 0.025f, 0.05f, 1f),
                          new Color(accent.r, accent.g, accent.b, 0.85f), 0.006f, 0.02f, MenuKit.OrderPanel);
            ScreenRect rect;
            rect.Plane = t;
            rect.Size = size;
            screens.Add(rect);
            return t;
        }

        TextMesh Label(string name, Transform parent, Vector3 pos, string text, float lineHeight, Color color, TextAnchor anchor)
        {
            return MenuKit.Text(name, parent, pos, text, lineHeight, color, anchor, font, textMaterial);
        }

        void Divider(Transform parent, float y, float width)
        {
            Material mat = MenuKit.NewMaterial(unlitShader, "Menu Divider");
            mat.SetColor("_Color", new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, 1f));
            MenuKit.MeshObject("Divider", parent, MenuKit.Quad, mat, new Vector3(0f, y, 0f), new Vector3(width, 0.008f, 1f));
        }

        MenuButton Button(string name, Transform parent, Vector3 pos, Vector2 size, string label,
                          float textHeight, bool primary, Action onClick)
        {
            var b = new MenuButton(name, parent, pos, size, label, textHeight, panelShader, font, textMaterial, accent, primary);
            b.OnClick = onClick;
            buttons.Add(b);
            return b;
        }

        static string T(string key) { return MenuText.Get(key); }

        /// <summary>Turn to face a screen: the desktop camera swings round, a headset gets a hint.</summary>
        void Face(float yaw, string headsetHintKey)
        {
            targetYaw = viewYaw + Mathf.DeltaAngle(viewYaw, yaw);
            if (XRSettings.isDeviceActive && headsetHintKey != null) hintOverride = T(headsetHintKey);
            else hintOverride = null;
        }

        /// <summary>A screen on a stand 4 m from the middle of the room, facing you.</summary>
        static Vector3 StandSpot(float yaw)
        {
            float a = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * 4f, 1.9f, Mathf.Cos(a) * 4f);
        }

        void BuildCentreScreen()
        {
            Transform screen = BuildScreen("Menu Screen", new Vector3(0f, 2.95f, MissionControlRoom.Front - 0.15f), 0f, new Vector2(6f, 3.4f), false);
            const float pageScale = 1.13f;   // the layout below is drawn for a 5 x 3 m screen

            Transform p = MenuKit.Group("Main Page", screen, new Vector3(0f, 0f, -0.015f));
            p.localScale = new Vector3(pageScale, pageScale, 1f);
            TextMesh titleText = Label("Title", p, new Vector3(0f, 0.98f, 0f), title, 0.66f, Color.white, TextAnchor.MiddleCenter);
            titleText.fontStyle = FontStyle.Bold;
            Label("Subtitle", p, new Vector3(0f, 0.5f, 0f), MenuKit.Spaced(T("mc.subtitle")), 0.15f, accent, TextAnchor.MiddleCenter);
            Divider(p, 0.34f, 2.6f);
            Label("Tagline", p, new Vector3(0f, 0.21f, 0f), T("mc.tagline"), 0.08f, TextDim, TextAnchor.MiddleCenter);

            Button("Start Button", p, new Vector3(0f, -0.12f, 0f), new Vector2(2.9f, 0.44f), T("btn.start"), 0.18f, true, StartSimulation);
            // first row: settings, mission info, quit
            var small = new Vector2(1.3f, 0.3f);
            Button("Settings Button", p, new Vector3(-1.4f, -0.6f, 0f), small, T("btn.settings"), 0.11f, false,
                   delegate { Face(SettingsYaw, "hint.turnRight"); });
            Button("Info Button", p, new Vector3(0f, -0.6f, 0f), small, T("btn.info"), 0.11f, false,
                   delegate { Face(InfoYaw, "hint.turnLeft"); });
            Button("Quit Button", p, new Vector3(1.4f, -0.6f, 0f), small, T("btn.quit"), 0.11f, false, Quit);
            // second row: the two controls screens
            var wide = new Vector2(1.6f, 0.3f);
            Button("PC Controls Button", p, new Vector3(-0.85f, -0.94f, 0f), wide, T("btn.pcControls"), 0.11f, false,
                   delegate { Face(ControlsYaw, "hint.turnPcControls"); });
            Button("VR Controls Button", p, new Vector3(0.85f, -0.94f, 0f), wide, T("btn.vrControls"), 0.11f, false,
                   delegate { Face(-ControlsYaw, "hint.turnVrControls"); });
            Label("Around", p, new Vector3(0f, -1.23f, 0f), T("mc.around"), 0.065f, TextDim, TextAnchor.MiddleCenter);

            hintText = Label("Hint", screen, new Vector3(0f, -1.59f, -0.015f), "", 0.075f, TextDim, TextAnchor.MiddleCenter);
        }

        void BuildSettingsScreen()
        {
            Transform screen = BuildScreen("Settings Screen", StandSpot(SettingsYaw), SettingsYaw, new Vector2(3f, 2.9f), true);
            Transform s = MenuKit.Group("Settings Page", screen, new Vector3(0f, 0f, -0.015f));
            Label("Header", s, new Vector3(0f, 1.24f, 0f), MenuKit.Spaced(T("set.title")), 0.15f, Color.white, TextAnchor.MiddleCenter);
            Divider(s, 1.08f, 2.8f);

            string[] names = { T("set.language"), T("set.source"), T("set.speed"), T("set.info"), T("set.hints"), T("set.snap") };
            string[] notes = { T("note.language"), "", T("note.speed"), T("note.info"), T("note.hints"), T("note.snap") };
            Action[] actions =
            {
                null,   // language: rebuilds everything, handled below
                delegate { MenuSettings.NextFeed(); },
                delegate { MenuSettings.NextSpeed(); },
                delegate { MenuSettings.ShowInfoPanel = !MenuSettings.ShowInfoPanel; },
                delegate { MenuSettings.ShowHints = !MenuSettings.ShowHints; },
                delegate { MenuSettings.NextSnap(); },
            };
            for (int i = 0; i < names.Length; i++)
            {
                float y = 0.84f - 0.32f * i;
                Label("Setting Name", s, new Vector3(-1.38f, y + 0.045f, 0f), names[i], 0.09f, TextBright, TextAnchor.MiddleLeft);
                TextMesh note = Label("Setting Note", s, new Vector3(-1.38f, y - 0.075f, 0f), notes[i], 0.056f, ActionText, TextAnchor.MiddleLeft);
                if (i == 1) feedDescription = note;
                Action change = actions[i];
                Action onClick;
                if (change == null)
                    onClick = delegate
                    {
                        MenuText.NextLanguage();
                        MenuSettings.Save();
                        Rebuild();
                    };
                else
                    onClick = delegate
                    {
                        change();
                        MenuSettings.Save();
                        RefreshSettings();
                    };
                settingButtons[i] = Button("Setting Button", s, new Vector3(0.8f, y, 0f), new Vector2(1.2f, 0.24f), "", 0.085f, false, onClick);
            }
            Button("Back Button", s, new Vector3(0f, -1.24f, 0f), new Vector2(1.4f, 0.22f), T("btn.back"), 0.09f, true,
                   delegate { Face(0f, "hint.turnBack"); });
        }

        void BuildInfoScreen()
        {
            Transform screen = BuildScreen("Mission Info Screen", StandSpot(InfoYaw), InfoYaw, new Vector2(3f, 2.9f), true);
            Transform s = MenuKit.Group("Info Page", screen, new Vector3(0f, 0f, -0.015f));
            Label("Header", s, new Vector3(0f, 1.24f, 0f), MenuKit.Spaced(T("info.title")), 0.15f, Color.white, TextAnchor.MiddleCenter);
            Divider(s, 1.08f, 2.8f);

            Label("Timeline Header", s, new Vector3(-1.38f, 0.93f, 0f), T("info.timeline"), 0.085f, accent, TextAnchor.MiddleLeft);
            TextMesh times = Label("Times", s, new Vector3(-1.38f, 0.82f, 0f),
                                   "0 s\n600 s\n900 \u2013 1200 s\n1300 \u2013 1430 s\n~1980 \u2013 4000 s\n4300 \u2013 4800 s",
                                   0.09f, Color.white, TextAnchor.UpperLeft);
            times.lineSpacing = 1.25f;
            TextMesh events = Label("Events", s, new Vector3(-0.68f, 0.82f, 0f), T("info.events"), 0.09f, ActionText, TextAnchor.UpperLeft);
            events.lineSpacing = 1.25f;

            Label("Orbit Header", s, new Vector3(-1.38f, -0.2f, 0f), T("info.orbitTitle"), 0.085f, accent, TextAnchor.MiddleLeft);
            TextMesh orbit = Label("Orbit", s, new Vector3(-1.38f, -0.3f, 0f), T("info.orbit"), 0.075f, ActionText, TextAnchor.UpperLeft);
            orbit.lineSpacing = 1.25f;

            Button("Back Button", s, new Vector3(0f, -1.24f, 0f), new Vector2(1.4f, 0.22f), T("btn.back"), 0.09f, true,
                   delegate { Face(0f, "hint.turnBack"); });
        }

        void BuildOrbitHologram()
        {
            // a round stand behind you, with the Earth and the orbit floating above it
            Transform root = MenuKit.Group("Orbit Hologram", transform, new Vector3(0f, 0f, -2.4f));
            Material stand = MenuKit.NewMaterial(roomShader, "Hologram Stand");
            stand.SetColor("_Color", new Color(0.05f, 0.055f, 0.07f));
            stand.SetFloat("_Gloss", 0.4f);
            MenuKit.BoxObject("Stand", root, stand, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 0.8f, 0.9f));
            Material glow = MenuKit.NewMaterial(unlitShader, "Hologram Glow");
            glow.SetColor("_Color", accent);
            MenuKit.BoxObject("Stand Light", root, glow, new Vector3(0f, 0.805f, 0f), new Vector3(0.7f, 0.01f, 0.7f));

            Transform centre = MenuKit.Group("Centre", root, new Vector3(0f, 1.55f, 0f));
            holoCentre = centre;

            Material globeMat = MenuKit.NewMaterial(mapShader, "Hologram Earth");
            if (earthMap != null) globeMat.SetTexture("_MainTex", earthMap);
            Mesh sphere = ProceduralMeshes.UvSphere(64, 32, "Hologram Sphere");
            holoGlobe = MenuKit.MeshObject("Earth", centre, sphere, globeMat, Vector3.zero, Vector3.one * HoloRadius).transform;

            // the orbit ring
            float r = HoloRadius * (1f + HoloAltitudeScale * 500f / 6371f);
            Quaternion plane = OrbitPlane();
            const int n = 160;
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * Mathf.PI / n;
                ring[i] = plane * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            Material ringMat = MenuKit.NewMaterial(unlitShader, "Hologram Orbit");
            ringMat.SetColor("_Color", TrackColor);
            var ringGo = new GameObject("Orbit");
            ringGo.transform.SetParent(centre, false);
            var line = ringGo.AddComponent<LineRenderer>();
            line.sharedMaterial = ringMat;
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = 0.008f;
            line.positionCount = n;
            line.SetPositions(ring);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Material markerMat = MenuKit.NewMaterial(unlitShader, "Hologram Satellite");
            markerMat.SetColor("_Color", Color.white);
            holoMarker = MenuKit.MeshObject("Satellite", centre, ProceduralMeshes.UvSphere(16, 8, "Hologram Marker"), markerMat,
                                            Vector3.zero, Vector3.one * 0.035f).transform;

            // words above it, facing you when you turn round
            Transform sign = MenuKit.Group("Hologram Sign", root, new Vector3(0f, 2.45f, 0f));
            sign.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Label("Title", sign, new Vector3(0f, 0.08f, 0f), MenuKit.Spaced(T("holo.title")), 0.13f, accent, TextAnchor.MiddleCenter);
            Label("Facts", sign, new Vector3(0f, -0.1f, 0f), T("holo.facts"), 0.07f, TextDim, TextAnchor.MiddleCenter);
            Label("How To", sign, new Vector3(0f, -0.24f, 0f), T("holo.grab"), 0.065f, ActionText, TextAnchor.MiddleCenter);
            MoveHologram(0f);
        }

        void BuildControlsScreen()
        {
            // two screens on stands behind you, either side of the hologram, about 4 m away
            // (close enough to read, and placed so they don't hide the side screens or the sign)
            var size = new Vector2(3f, 2.9f);
            string accentHex = "#" + ColorHex(accent);

            // ---- keyboard and mouse, behind you on the right
            Transform kb = BuildScreen("Controls Keyboard Screen", StandSpot(ControlsYaw), ControlsYaw, size, true);
            Transform k = MenuKit.Group("Keyboard Page", kb, new Vector3(0f, 0f, -0.015f));
            Label("Header", k, new Vector3(0f, 1.24f, 0f), MenuKit.Spaced(T("ctl.kbTitle")), 0.14f, Color.white, TextAnchor.MiddleCenter);
            Divider(k, 1.08f, 2.8f);
            const float h = 0.08f;
            ControlsBlock(k, -1.38f, 0.95f, 0.6f, h, T("ctl.camHead"), T("ctl.camKeys"), T("ctl.camDo"));
            // the display keys go in the same block as the satellite keys, under their own
            // heading line, so the two lists can never be drawn on top of each other
            string keys = T("ctl.satKeys") + "\n<color=" + accentHex + ">" + T("ctl.dispHead") + "</color>\n" + T("ctl.dispKeys");
            string actions = T("ctl.satDo") + "\n\n" + T("ctl.dispDo");
            ControlsBlock(k, 0.2f, 0.95f, 0.3f, h, T("ctl.satHead"), keys, actions);
            Label("Note", k, new Vector3(0f, -1.06f, 0f), T("ctl.note"), 0.065f, TextDim, TextAnchor.MiddleCenter);
            Button("Back Button", k, new Vector3(0f, -1.27f, 0f), new Vector2(1.4f, 0.22f), T("btn.back"), 0.09f, true,
                   delegate { Face(0f, "hint.turnBack"); });

            // ---- VR headset, behind you on the left
            Transform vr = BuildScreen("Controls Headset Screen", StandSpot(-ControlsYaw), -ControlsYaw, size, true);
            Transform v = MenuKit.Group("Headset Page", vr, new Vector3(0f, 0f, -0.015f));
            Label("Header", v, new Vector3(0f, 1.24f, 0f), MenuKit.Spaced(T("ctl.vrTitle")), 0.14f, Color.white, TextAnchor.MiddleCenter);
            Divider(v, 1.08f, 2.8f);
            ControlsBlock(v, -1.05f, 0.95f, 1.05f, 0.095f, T("ctl.vrHead"), T("ctl.vrKeys"), T("ctl.vrDo"));
            Label("Note", v, new Vector3(0f, -1.06f, 0f), T("ctl.vrNote"), 0.065f, TextDim, TextAnchor.MiddleCenter);
            Button("Back Button", v, new Vector3(0f, -1.27f, 0f), new Vector2(1.4f, 0.22f), T("btn.back"), 0.09f, true,
                   delegate { Face(0f, "hint.turnBack"); });
        }

        static string ColorHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        /// <summary>A heading, then keys in one column and what they do in the next (same number of lines).</summary>
        void ControlsBlock(Transform parent, float x, float y, float keyWidth, float lineHeight,
                           string header, string keys, string actions)
        {
            const float spacing = 1.2f;
            Label("Controls Heading", parent, new Vector3(x, y, 0f), header, lineHeight * 1.05f, accent, TextAnchor.MiddleLeft);
            float listTop = y - lineHeight * 1.2f;
            TextMesh kt = Label("Keys", parent, new Vector3(x, listTop, 0f), keys, lineHeight, Color.white, TextAnchor.UpperLeft);
            kt.lineSpacing = spacing;
            TextMesh at = Label("Actions", parent, new Vector3(x + keyWidth, listTop, 0f), actions, lineHeight, ActionText, TextAnchor.UpperLeft);
            at.lineSpacing = spacing;
        }

        float HoloGrabRadius { get { return HoloRadius * (1f + HoloAltitudeScale * 500f / 6371f) * 1.15f; } }

        /// <summary>Does the ray hit the hologram (Earth plus orbit)? Distance along the ray when it does.</summary>
        bool RayHitsHologram(Ray ray, out float distance)
        {
            distance = 0f;
            if (holoCentre == null) return false;
            Vector3 toCentre = holoCentre.position - ray.origin;
            float along = Vector3.Dot(toCentre, ray.direction);
            if (along <= 0f) return false;
            float r = HoloGrabRadius;
            float missSq = toCentre.sqrMagnitude - along * along;
            if (missSq > r * r) return false;
            distance = along - Mathf.Sqrt(Mathf.Max(0f, r * r - missSq));
            return true;
        }

        /// <summary>The point on the hologram's grab sphere under the ray (or its edge when the ray misses), from the centre.</summary>
        Vector3 GrabPoint(Ray ray)
        {
            Vector3 c = holoCentre.position;
            float along = Vector3.Dot(c - ray.origin, ray.direction);
            Vector3 closest = ray.origin + ray.direction * along;
            Vector3 v = closest - c;
            float r = HoloGrabRadius;
            float d = v.magnitude;
            if (d >= r) return d > 1e-5f ? v * (r / d) : Vector3.up * r;
            return v - ray.direction * Mathf.Sqrt(r * r - d * d);   // the near side of the sphere
        }

        /// <summary>Spin the hologram while it is held, and let it coast to a stop afterwards.</summary>
        void UpdateGrab(bool held, bool hasRay, Ray ray, float dt)
        {
            if (holoCentre == null) return;
            if (grabbing)
            {
                if (!held || !hasRay)
                {
                    grabbing = false;
                    return;
                }
                Vector3 now = GrabPoint(ray);
                Quaternion turn = Quaternion.FromToRotation(grabPrevious, now);
                holoCentre.rotation = turn * holoCentre.rotation;
                float angle;
                Vector3 axis;
                turn.ToAngleAxis(out angle, out axis);
                if (angle > 180f) angle -= 360f;
                bool axisOk = !float.IsNaN(axis.x) && !float.IsInfinity(axis.x) && axis.sqrMagnitude > 0.5f;
                if (axisOk && Mathf.Abs(angle) > 0.01f && dt > 1e-4f)
                {
                    spinAxis = axis;
                    spinSpeed = Mathf.Clamp(angle / dt, -720f, 720f);
                }
                else spinSpeed = Mathf.MoveTowards(spinSpeed, 0f, dt * 600f);
                grabPrevious = now;
            }
            else if (Mathf.Abs(spinSpeed) > 0.5f)
            {
                holoCentre.Rotate(spinAxis, spinSpeed * dt, Space.World);
                spinSpeed *= Mathf.Exp(-dt * 2f);
            }
        }

        /// <summary>Is the click button still held: left mouse, or the pointing controller's trigger or A / X.</summary>
        bool PointerHeld(bool headset)
        {
            if (!headset) return InputShim.MouseHeld(0);
            InputDevice d = InputDevices.GetDeviceAtXRNode(useRightHand ? XRNode.RightHand : XRNode.LeftHand);
            if (!d.isValid) return false;
            bool v;
            if (d.TryGetFeatureValue(CommonUsages.triggerButton, out v) && v) return true;
            return d.TryGetFeatureValue(CommonUsages.primaryButton, out v) && v;
        }

        static Quaternion OrbitPlane()
        {
            return Quaternion.AngleAxis(35f, Vector3.up) * Quaternion.AngleAxis(InclinationRad * Mathf.Rad2Deg, Vector3.right);
        }

        void MoveHologram(float dt)
        {
            if (holoGlobe == null || holoMarker == null) return;
            holoGlobe.Rotate(Vector3.up, -6f * dt, Space.Self);
            float r = HoloRadius * (1f + HoloAltitudeScale * 500f / 6371f);
            holoMarker.localPosition = OrbitPlane() * new Vector3(Mathf.Cos(orbitPhase) * r, 0f, Mathf.Sin(orbitPhase) * r);
        }

        void BuildMapScreen()
        {
            Transform screen = BuildScreen("Ground Track Screen", new Vector3(-4.6f, 2.9f, 5.1f), -28f, new Vector2(3f, 2.2f), true);
            Label("Header", screen, new Vector3(-1.34f, 0.93f, -0.015f), MenuKit.Spaced(T("map.title")), 0.1f, accent, TextAnchor.MiddleLeft);
            Label("Orbit", screen, new Vector3(1.34f, 0.93f, -0.015f), T("map.orbit"), 0.07f, TextDim, TextAnchor.MiddleRight);
            trackReadout = Label("Readout", screen, new Vector3(-1.34f, -0.94f, -0.015f), "", 0.08f, TextBright, TextAnchor.MiddleLeft);

            mapSize = new Vector2(2.7f, 1.35f);
            Material mapMaterial = MenuKit.NewMaterial(mapShader, "Ground Track Map");
            if (earthMap != null) mapMaterial.SetTexture("_MainTex", earthMap);
            MenuKit.MeshObject("Map", screen, MenuKit.Quad, mapMaterial, new Vector3(0f, -0.03f, -0.01f), new Vector3(mapSize.x, mapSize.y, 1f));

            Transform overlay = MenuKit.Group("Map Overlay", screen, new Vector3(0f, -0.03f, -0.02f));
            trackMesh = new Mesh();
            trackMesh.name = "Ground Track";
            trackMesh.MarkDynamic();
            Material trackMaterial = MenuKit.NewMaterial(unlitShader, "Ground Track Line");
            trackMaterial.SetColor("_Color", TrackColor);
            MenuKit.MeshObject("Track", overlay, trackMesh, trackMaterial, Vector3.zero, Vector3.one);

            MeshRenderer ring = MenuKit.Panel("Marker Ring", overlay, panelShader, new Vector3(0f, 0f, -0.006f), new Vector2(0.07f, 0.07f),
                                              new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f), Color.white, 0.1f, 0.5f, MenuKit.OrderMarker);
            markerRing = ring.transform;
            markerRingMaterial = ring.sharedMaterial;
            MeshRenderer dot = MenuKit.Panel("Marker", overlay, panelShader, new Vector3(0f, 0f, -0.008f), new Vector2(0.07f, 0.07f),
                                             Color.white, Color.white, TrackColor, 0.18f, 0.5f, MenuKit.OrderMarker + 1);
            marker = dot.transform;
        }

        void BuildStatusScreen()
        {
            Transform screen = BuildScreen("Status Screen", new Vector3(4.6f, 2.9f, 5.1f), 28f, new Vector2(3f, 2.2f), true);
            Label("Header", screen, new Vector3(-1.34f, 0.93f, -0.015f), MenuKit.Spaced(T("status.title")), 0.1f, accent, TextAnchor.MiddleLeft);
            TextMesh names = Label("Names", screen, new Vector3(-1.34f, 0.72f, -0.015f),
                                   T("status.names"),
                                   0.1f, TextDim, TextAnchor.UpperLeft);
            names.lineSpacing = 1.35f;
            statusValues = Label("Values", screen, new Vector3(-0.22f, 0.72f, -0.015f), "", 0.1f, TextBright, TextAnchor.UpperLeft);
            statusValues.lineSpacing = 1.35f;
            goText = Label("Go", screen, new Vector3(0f, -0.8f, -0.015f), T("status.awaiting"), 0.11f, Amber, TextAnchor.MiddleCenter);
        }

        void BuildSignage()
        {
            // strip above the menu screen
            float z = MissionControlRoom.Front - 0.15f;
            Transform strip = MenuKit.Group("Sign Strip", transform, new Vector3(0f, 4.92f, z));
            MenuKit.Panel("Background", strip, panelShader, Vector3.zero, new Vector2(6f, 0.28f),
                          new Color(0.03f, 0.065f, 0.115f, 1f), new Color(0.02f, 0.04f, 0.075f, 1f),
                          new Color(accent.r, accent.g, accent.b, 0.6f), 0.04f, 0.2f, MenuKit.OrderPanel);
            Label("Name", strip, new Vector3(-2.8f, 0f, -0.015f), MenuKit.Spaced(T("sign.mc")), 0.11f, TextBright, TextAnchor.MiddleLeft);
            clockText = Label("Clock", strip, new Vector3(2.8f, 0f, -0.015f), "", 0.11f, Amber, TextAnchor.MiddleRight);

            // lettering on the back wall, for when you turn around
            Transform back = MenuKit.Group("Back Wall Sign", transform, new Vector3(0f, 3f, MissionControlRoom.Back + 0.02f));
            back.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh big = Label("Name", back, new Vector3(0f, 0.25f, 0f), title, 0.7f, new Color(0.4f, 0.47f, 0.56f, 1f), TextAnchor.MiddleCenter);
            big.fontStyle = FontStyle.Bold;
            Label("Line", back, new Vector3(0f, -0.3f, 0f), MenuKit.Spaced(T("sign.ops")), 0.14f,
                  new Color(accent.r, accent.g, accent.b, 0.8f), TextAnchor.MiddleCenter);
        }

        void BuildPointer()
        {
            Material beam = MenuKit.NewMaterial(unlitShader, "Menu Pointer");
            beam.SetColor("_Color", Color.Lerp(accent, Color.white, 0.35f));
            laser = MenuKit.BoxObject("Pointer Beam", transform, beam, Vector3.zero, Vector3.one).transform;
            laser.gameObject.SetActive(false);
            reticle = MenuKit.Panel("Pointer Dot", transform, panelShader, Vector3.zero, new Vector2(0.06f, 0.06f),
                                    Color.white, Color.white, accent, 0.2f, 0.5f, MenuKit.OrderMarker + 2).transform;
            reticle.gameObject.SetActive(false);
        }

        void BuildFade()
        {
            // a black sheet just in front of the eyes; starts opaque and clears
            fadeRenderer = MenuKit.Panel("Fade", head, panelShader, new Vector3(0f, 0f, 0.15f), new Vector2(1.5f, 1.5f),
                                         Color.black, Color.black, Color.clear, 0f, 0f, MenuKit.OrderFade);
            fadeMaterial = fadeRenderer.sharedMaterial;
            fadeAlpha = 1f;
            fadeTarget = 0f;
            ApplyFade();
        }

        void BuildSounds()
        {
            try
            {
                audioSource = head.gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
                tickClip = Tone("Menu Tick", 1500f, 0.03f, 0.1f);
                clickClip = Tone("Menu Click", 880f, 0.1f, 0.22f);
            }
            catch (Exception e)
            {
                Debug.LogWarning("EOSAT-1 menu: no sound (" + e.Message + ")");
                audioSource = null;
            }
        }

        static AudioClip Tone(string name, float hertz, float seconds, float gain)
        {
            const int rate = 44100;
            int n = Mathf.Max(1, (int)(rate * seconds));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 5f / seconds) * Mathf.Clamp01(i / 40f);
                data[i] = Mathf.Sin(2f * Mathf.PI * hertz * t) * envelope * gain;
            }
            AudioClip clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        // ------------------------------------------------------------------ pages and actions
        void RefreshSettings()
        {
            if (settingButtons[0] != null) settingButtons[0].Label.text = MenuText.LanguageName;
            if (settingButtons[1] != null) settingButtons[1].Label.text = FeedLabel();
            if (settingButtons[2] != null) settingButtons[2].Label.text = MenuSettings.SpeedLabel;
            if (settingButtons[3] != null) settingButtons[3].Label.text = T(MenuSettings.ShowInfoPanel ? "val.shown" : "val.hidden");
            if (settingButtons[4] != null) settingButtons[4].Label.text = T(MenuSettings.ShowHints ? "val.shown" : "val.hidden");
            if (settingButtons[5] != null) settingButtons[5].Label.text = MenuSettings.SnapTurnDegrees.ToString("0", Inv) + "\u00b0";
            if (feedDescription != null) feedDescription.text = FeedDescription();
            UpdateSlowDisplays();
        }

        static string FeedLabel()
        {
            switch (MenuSettings.Feed)
            {
                case FeedKind.Replay: return T("feed.replay");
                case FeedKind.Tcp: return T("feed.live");
                default: return T("feed.sandbox");
            }
        }

        static string FeedDescription()
        {
            switch (MenuSettings.Feed)
            {
                case FeedKind.Replay: return T("feed.replay.note");
                case FeedKind.Tcp: return T("feed.live.note");
                default: return T("feed.sandbox.note");
            }
        }

        void StartSimulation()
        {
            if (!busy) StartCoroutine(Launch());
        }

        IEnumerator Launch()
        {
            busy = true;
            string scenePath = FindSimulationScene();
            if (scenePath == null)
            {
                hintOverride = "<color=#FF6B6B>" + string.Format(T("err.notInBuild"), simulationScene) + "</color>";
                Debug.LogError("EOSAT-1 menu: scene '" + simulationScene + "' is not in File > Build Profiles (Build Settings). " +
                               "Run EOSAT-1 > Build Demo Scene to create and add it.");
                busy = false;
                yield break;
            }

            hintOverride = T("hint.loading");
            if (goText != null) goText.text = "<color=#7CFC9A>" + T("status.go") + "</color>";
            MenuSettings.Save();
            MenuSettings.LaunchedFromMenu = true;

            fadeTarget = 1f;
            float waited = 0f;
            while (fadeAlpha < 0.999f && waited < 2f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
        }

        /// <summary>The build-list path of the simulation scene, or null when it is not in the list.</summary>
        string FindSimulationScene()
        {
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                if (string.Equals(Path.GetFileNameWithoutExtension(path), simulationScene, StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            return null;
        }

        void Quit()
        {
            MenuSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool headset = XRSettings.isDeviceActive;

            bool click = ReadClick(headset);
            if (!headset) AimDesktopCamera(dt);

            Ray ray;
            bool hasRay = PointerRay(headset, out ray);

            // what is being pointed at (any button on any screen)
            MenuButton hit = null;
            float hitDistance = float.MaxValue;
            if (hasRay && !busy)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    float d;
                    if (buttons[i].Enabled && buttons[i].Visible && buttons[i].Hit(ray, out d) && d < hitDistance)
                    {
                        hit = buttons[i];
                        hitDistance = d;
                    }
                }
            }
            if (hit != hovered)
            {
                hovered = hit;
                if (hit != null) Play(tickClip);
            }
            for (int i = 0; i < buttons.Count; i++) buttons[i].Animate(buttons[i] == hovered, dt);

            float holoDistance = 0f;
            bool onHologram = hasRay && !busy && hit == null && RayHitsHologram(ray, out holoDistance);
            float pointerLength = hit != null ? hitDistance
                                : grabbing && holoCentre != null ? Vector3.Distance(ray.origin, holoCentre.position + GrabPoint(ray))
                                : onHologram ? holoDistance
                                : ScreenDistance(ray);
            ShowPointer(headset, hasRay, ray, pointerLength);

            // act
            if (!busy)
            {
                if (click && hovered != null)
                {
                    MenuButton pressed = hovered;
                    hovered = null;
                    Play(clickClip);
                    if (pressed.OnClick != null) pressed.OnClick();
                }
                else if (click && onHologram)
                {
                    grabbing = true;
                    grabPrevious = GrabPoint(ray);
                    spinSpeed = 0f;
                    Play(tickClip);
                }
                else if (InputShim.Down(KeyCode.Return))
                {
                    Play(clickClip);
                    StartSimulation();
                }
                else if (InputShim.Down(KeyCode.Escape))
                {
                    Face(0f, null);
                }
            }

            // displays
            orbitPhase += dt * 2f * Mathf.PI / SecondsPerMapOrbit;
            MoveMarker();
            UpdateGrab(PointerHeld(headset), hasRay, ray, dt);
            MoveHologram(dt);
            if (Time.unscaledTime >= nextSlowRefresh)
            {
                nextSlowRefresh = Time.unscaledTime + 0.25f;
                UpdateSlowDisplays();
            }
            if (goText != null && !busy)
            {
                Color c = Amber;
                c.a = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
                goText.color = c;
            }
            if (hintText != null)
            {
                string hint = hintOverride != null ? hintOverride : T(headset ? "hint.headset" : "hint.desktop");
                if (hintText.text != hint) hintText.text = hint;
            }

            // fade
            if (!Mathf.Approximately(fadeAlpha, fadeTarget))
            {
                fadeAlpha = Mathf.MoveTowards(fadeAlpha, fadeTarget, dt / 0.6f);
                ApplyFade();
            }
        }

        void ApplyFade()
        {
            if (fadeMaterial == null) return;
            var c = new Color(0f, 0f, 0f, fadeAlpha);
            fadeMaterial.SetColor("_Color", c);
            fadeMaterial.SetColor("_Color2", c);
            if (fadeRenderer != null) fadeRenderer.enabled = fadeAlpha > 0.001f;
        }

        // ------------------------------------------------------------------ pointing
        void AimDesktopCamera(float dt)
        {
            // turn with A / D, the arrow keys, or by dragging with the right mouse button
            float turn = 0f;
            if (InputShim.Held(KeyCode.A) || InputShim.Held(KeyCode.LeftArrow)) turn -= 1f;
            if (InputShim.Held(KeyCode.D) || InputShim.Held(KeyCode.RightArrow)) turn += 1f;
            bool dragging = InputShim.MouseHeld(1);
            if (turn != 0f || dragging)
            {
                targetYaw += turn * 90f * dt;
                if (dragging) targetYaw += InputShim.MouseDelta().x * 0.15f;
                if (hintOverride != null && !busy) hintOverride = null;
            }
            viewYaw = Mathf.Lerp(viewYaw, targetYaw, 1f - Mathf.Exp(-dt * 6f));

            // lean a little towards the mouse (not while dragging)
            Vector2 m = InputShim.MousePosition();
            float nx = dragging ? 0f : Mathf.Clamp(m.x / Mathf.Max(1f, Screen.width) * 2f - 1f, -1f, 1f);
            float ny = dragging ? 0f : Mathf.Clamp(m.y / Mathf.Max(1f, Screen.height) * 2f - 1f, -1f, 1f);
            float k = 1f - Mathf.Exp(-dt * 4f);
            lookYaw = Mathf.Lerp(lookYaw, nx * 4f, k);
            lookPitch = Mathf.Lerp(lookPitch, ny * 2.5f, k);

            // look up at the big front screen, level at the side walls, a little down at the hologram
            float facing = 0.5f + 0.5f * Mathf.Cos(viewYaw * Mathf.Deg2Rad);
            float basePitch = Mathf.Lerp(3f, -11f, facing);
            head.localPosition = new Vector3(0f, eyeHeight, 0f);
            head.localRotation = Quaternion.Euler(basePitch - lookPitch, viewYaw + lookYaw, 0f);
        }

        bool ReadClick(bool headset)
        {
            if (!headset)
            {
                prevTriggerL = prevTriggerR = prevPrimaryL = prevPrimaryR = false;
                return InputShim.MouseDown(0);
            }
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool l = Edge(left, CommonUsages.triggerButton, ref prevTriggerL) | Edge(left, CommonUsages.primaryButton, ref prevPrimaryL);
            bool r = Edge(right, CommonUsages.triggerButton, ref prevTriggerR) | Edge(right, CommonUsages.primaryButton, ref prevPrimaryR);
            // the hand that clicks becomes the pointing hand
            if (r) useRightHand = true;
            else if (l) useRightHand = false;
            if (useRightHand && !right.isValid && left.isValid) useRightHand = false;
            else if (!useRightHand && !left.isValid && right.isValid) useRightHand = true;
            return l || r;
        }

        static bool Edge(InputDevice device, InputFeatureUsage<bool> usage, ref bool previous)
        {
            bool now = false;
            bool value;
            if (device.isValid && device.TryGetFeatureValue(usage, out value)) now = value;
            bool edge = now && !previous;
            previous = now;
            return edge;
        }

        bool PointerRay(bool headset, out Ray ray)
        {
            if (!headset)
            {
                ray = cam.ScreenPointToRay(InputShim.MousePosition());
                return true;
            }
            Transform hand = useRightHand ? rightHand : leftHand;
            InputDevice device = InputDevices.GetDeviceAtXRNode(useRightHand ? XRNode.RightHand : XRNode.LeftHand);
            if (hand != null && device.isValid)
            {
                ray = new Ray(hand.position, hand.forward);
                return true;
            }
            // no controllers: point with your head
            ray = new Ray(head.position, head.forward);
            return true;
        }

        /// <summary>Distance along the ray to the nearest wall screen, or a default when it misses them all.</summary>
        float ScreenDistance(Ray ray)
        {
            float best = 8f;
            for (int i = 0; i < screens.Count; i++)
            {
                Vector3 local;
                float d;
                if (!MenuKit.RayToLocalPlane(ray, screens[i].Plane, out local, out d)) continue;
                if (Mathf.Abs(local.x) <= screens[i].Size.x * 0.5f && Mathf.Abs(local.y) <= screens[i].Size.y * 0.5f && d < best) best = d;
            }
            return best;
        }

        void ShowPointer(bool headset, bool hasRay, Ray ray, float distance)
        {
            bool show = headset && hasRay && !busy;
            if (laser.gameObject.activeSelf != show) laser.gameObject.SetActive(show);
            if (reticle.gameObject.activeSelf != show) reticle.gameObject.SetActive(show);
            if (!show) return;
            Quaternion along = Quaternion.LookRotation(ray.direction);
            float length = Mathf.Max(0.05f, distance - 0.03f);
            laser.SetPositionAndRotation(ray.origin + ray.direction * (length * 0.5f), along);
            laser.localScale = new Vector3(0.003f, 0.003f, length);
            reticle.SetPositionAndRotation(ray.origin + ray.direction * length, along);
            float size = hovered != null ? 1.5f : 1f;
            reticle.localScale = new Vector3(0.06f * size, 0.06f * size, 1f);
        }

        // ------------------------------------------------------------------ displays
        /// <summary>Sub-satellite point for an angle u along the orbit from the ascending node.</summary>
        static void TrackPoint(float u, out float lonDeg, out float latDeg)
        {
            float lat = Mathf.Asin(Mathf.Sin(InclinationRad) * Mathf.Sin(u));
            float lon = Mathf.Atan2(Mathf.Cos(InclinationRad) * Mathf.Sin(u), Mathf.Cos(u)) - u * EarthTurnPerOrbitRad + StartLongitudeRad;
            lonDeg = Mathf.Repeat(lon * Mathf.Rad2Deg + 180f, 360f) - 180f;
            latDeg = lat * Mathf.Rad2Deg;
        }

        Vector3 MapPoint(float lonDeg, float latDeg, float z)
        {
            return new Vector3(lonDeg / 360f * mapSize.x, latDeg / 180f * mapSize.y, z);
        }

        void MoveMarker()
        {
            if (marker == null) return;
            float lon, lat;
            TrackPoint(orbitPhase, out lon, out lat);
            marker.localPosition = MapPoint(lon, lat, -0.008f);
            float pulse = Mathf.Repeat(Time.unscaledTime * 0.7f, 1f);
            markerRing.localPosition = MapPoint(lon, lat, -0.006f);
            float s = 0.07f * (1f + 2.6f * pulse);
            markerRing.localScale = new Vector3(s, s, 1f);
            markerRingMaterial.SetColor("_BorderColor", new Color(1f, 1f, 1f, 1f - pulse));
        }

        void UpdateSlowDisplays()
        {
            DateTime now = DateTime.UtcNow;
            if (clockText != null) clockText.text = "UTC " + now.ToString("HH:mm:ss", Inv);
            if (statusValues != null)
            {
                statusValues.text = now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + "\n" +
                                    "EOS SAT-1\n" +
                                    T("status.orbit") + "\n" +
                                    "94.6 min\n" +
                                    "97.4\u00b0\n" +
                                    "<color=#FFD166>" + T("status.stowed") + "</color>\n" +
                                    "<color=#FFD166>" + T("status.stowed") + "</color>\n" +
                                    "<color=#7CFC9A>" + FeedLabel() + "</color>";
            }
            if (trackReadout != null)
            {
                float lon, lat;
                TrackPoint(orbitPhase, out lon, out lat);
                trackReadout.text = "LAT " + Mathf.Abs(lat).ToString("0.0", Inv) + (lat >= 0f ? " N" : " S") +
                                    "     LON " + Mathf.Abs(lon).ToString("0.0", Inv) + (lon >= 0f ? " E" : T("dir.west")) +
                                    "     ALT 500 KM";
            }
            RebuildTrack();
        }

        /// <summary>The track from half an orbit behind the marker to one and a half ahead, as a ribbon of thin quads.</summary>
        void RebuildTrack()
        {
            if (trackMesh == null) return;
            const int segments = 240;
            const float halfWidth = 0.006f;
            float from = orbitPhase - Mathf.PI;
            float step = 4f * Mathf.PI / segments;
            trackVerts.Clear();
            trackTris.Clear();
            float lon, lat;
            TrackPoint(from, out lon, out lat);
            Vector3 previous = MapPoint(lon, lat, 0f);
            for (int i = 1; i <= segments; i++)
            {
                TrackPoint(from + step * i, out lon, out lat);
                Vector3 next = MapPoint(lon, lat, 0f);
                Vector3 along = next - previous;
                // skip the jump across the +/-180 degree edge of the map
                if (Mathf.Abs(along.x) < mapSize.x * 0.5f && along.sqrMagnitude > 1e-10f)
                {
                    along.Normalize();
                    Vector3 side = new Vector3(-along.y, along.x, 0f) * halfWidth;
                    int b = trackVerts.Count;
                    trackVerts.Add(previous - side);
                    trackVerts.Add(previous + side);
                    trackVerts.Add(next - side);
                    trackVerts.Add(next + side);
                    trackTris.Add(b);
                    trackTris.Add(b + 2);
                    trackTris.Add(b + 1);
                    trackTris.Add(b + 2);
                    trackTris.Add(b + 3);
                    trackTris.Add(b + 1);
                }
                previous = next;
            }
            trackMesh.Clear();
            trackMesh.SetVertices(trackVerts);
            trackMesh.SetTriangles(trackTris, 0);
        }
    }

    /// <summary>
    /// Every word on the menu, in each language. To add a language, add its name to Names
    /// and one more string to every row (a missing one falls back to English).
    /// </summary>
    public static class MenuText
    {
        public static readonly string[] Names = { "ENGLISH", "FRAN\u00c7AIS" };

        const string PrefKey = "eosat1.menu.language";

        public static int Language
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, Names.Length - 1); }
            set { PlayerPrefs.SetInt(PrefKey, ((value % Names.Length) + Names.Length) % Names.Length); }
        }

        public static string LanguageName { get { return Names[Language]; } }

        public static void NextLanguage() { Language = Language + 1; }

        public static string Get(string key)
        {
            string[] row;
            if (key == null || !Table.TryGetValue(key, out row)) return key ?? "";
            int i = Language;
            return i < row.Length && !string.IsNullOrEmpty(row[i]) ? row[i] : row[0];
        }

        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // front screen
            { "mc.subtitle",   new[] { "VR MISSION SIMULATOR", "SIMULATEUR DE MISSION RV" } },
            { "mc.tagline",    new[] { "Earth observation satellite  |  500 km sun-synchronous orbit",
                                       "Satellite d\u2019observation de la Terre  |  orbite h\u00e9liosynchrone de 500 km" } },
            { "mc.around",     new[] { "Look around:  settings on your right  |  mission info on your left  |  3D orbit and controls behind you",
                                       "Regardez autour :  param\u00e8tres \u00e0 droite  |  infos \u00e0 gauche  |  orbite 3D et commandes derri\u00e8re vous" } },
            { "btn.start",     new[] { "START SIMULATION", "LANCER LA SIMULATION" } },
            { "btn.settings",  new[] { "SETTINGS", "PARAM\u00c8TRES" } },
            { "btn.info",      new[] { "MISSION INFO", "INFOS MISSION" } },
            { "btn.quit",      new[] { "QUIT", "QUITTER" } },
            { "btn.pcControls", new[] { "PC CONTROLS", "COMMANDES PC" } },
            { "btn.vrControls", new[] { "VR CONTROLS", "COMMANDES RV" } },
            { "btn.back",      new[] { "BACK TO MENU", "RETOUR AU MENU" } },

            // hints along the bottom of the menu screen
            { "hint.desktop",  new[] { "Click a button  |  Enter starts  |  A / D or right-drag to look around  |  Esc faces the menu",
                                       "Cliquez un bouton  |  Entr\u00e9e lance  |  A / D ou clic droit glissé pour tourner  |  \u00c9chap revient au menu" } },
            { "hint.headset",  new[] { "Look around the room  |  point a controller at a button and pull the trigger",
                                       "Regardez autour de vous  |  pointez une manette et appuyez sur la g\u00e2chette" } },
            { "hint.turnRight", new[] { "SETTINGS ARE ON YOUR RIGHT  >>", "LES PARAM\u00c8TRES SONT \u00c0 DROITE  >>" } },
            { "hint.turnLeft", new[] { "<<  MISSION INFO IS ON YOUR LEFT", "<<  LES INFOS SONT \u00c0 GAUCHE" } },
            { "hint.turnPcControls", new[] { "PC CONTROLS ARE BEHIND YOU, ON THE RIGHT", "COMMANDES PC : DERRI\u00c8RE VOUS, \u00c0 DROITE" } },
            { "hint.turnVrControls", new[] { "VR CONTROLS ARE BEHIND YOU, ON THE LEFT", "COMMANDES RV : DERRI\u00c8RE VOUS, \u00c0 GAUCHE" } },
            { "hint.turnBack", new[] { "THE MENU IS IN FRONT OF YOU", "LE MENU EST DEVANT VOUS" } },
            { "hint.loading",  new[] { "LOADING SIMULATION ...", "CHARGEMENT DE LA SIMULATION ..." } },
            { "err.notInBuild", new[] { "Scene '{0}' is not in the Build Settings list. Use EOSAT-1 > Build Demo Scene, then try again.",
                                        "La sc\u00e8ne \u00ab {0} \u00bb n\u2019est pas dans les Build Settings. Lancez EOSAT-1 > Build Demo Scene puis r\u00e9essayez." } },

            // settings screen
            { "set.title",     new[] { "SETTINGS", "PARAM\u00c8TRES" } },
            { "set.language",  new[] { "LANGUAGE", "LANGUE" } },
            { "set.source",    new[] { "DATA SOURCE", "SOURCE DES DONN\u00c9ES" } },
            { "set.speed",     new[] { "TIME SPEED AT START", "VITESSE AU D\u00c9PART" } },
            { "set.info",      new[] { "INFO PANEL", "PANNEAU D\u2019INFOS" } },
            { "set.hints",     new[] { "CONTROL HINTS", "AIDE DES COMMANDES" } },
            { "set.snap",      new[] { "SNAP TURN (HEADSET)", "ROTATION (CASQUE)" } },
            { "note.language", new[] { "Menu language", "Langue du menu" } },
            { "note.speed",    new[] { "Change it any time with - and = (or Y)", "Modifiable avec - et = (ou Y)" } },
            { "note.info",     new[] { "Tumbling, pointing, sunlight and antenna", "Rotation, pointage, soleil et antenne" } },
            { "note.hints",    new[] { "The key list along the bottom of the screen", "La liste des touches en bas de l\u2019\u00e9cran" } },
            { "note.snap",     new[] { "How far the right stick turns you", "Angle de rotation du stick droit" } },
            { "val.shown",     new[] { "SHOWN", "AFFICH\u00c9" } },
            { "val.hidden",    new[] { "HIDDEN", "MASQU\u00c9" } },
            { "feed.sandbox",  new[] { "SANDBOX", "BAC \u00c0 SABLE" } },
            { "feed.replay",   new[] { "REPLAY", "RELECTURE" } },
            { "feed.live",     new[] { "LIVE LINK", "EN DIRECT" } },
            { "feed.sandbox.note", new[] { "Built-in simulator, takes commands", "Simulateur int\u00e9gr\u00e9, accepte les commandes" } },
            { "feed.replay.note",  new[] { "Plays the recorded one-orbit pass", "Rejoue le passage enregistr\u00e9 d\u2019une orbite" } },
            { "feed.live.note",    new[] { "Course simulator over TCP, port 8100", "Simulateur du cours par TCP, port 8100" } },

            // mission info screen
            { "info.title",    new[] { "MISSION INFO", "INFOS MISSION" } },
            { "info.timeline", new[] { "LAUNCH SCENARIO (SANDBOX)", "SC\u00c9NARIO DE LANCEMENT (BAC \u00c0 SABLE)" } },
            { "info.events",   new[] { "Separation, tumbling\nUHF antenna deploys\nTurns to face the Earth\nSolar panels deploy\nEclipse\n25\u00b0 roll to take images",
                                       "S\u00e9paration, culbute\nD\u00e9ploiement de l\u2019antenne UHF\nSe tourne vers la Terre\nD\u00e9ploiement des panneaux solaires\n\u00c9clipse\nRoulis de 25\u00b0 pour l\u2019imagerie" } },
            { "info.orbitTitle", new[] { "THE ORBIT", "L\u2019ORBITE" } },
            { "info.orbit",    new[] { "500 km up, 97.4\u00b0, sun-synchronous\nOne lap of the Earth every 94.6 minutes\nSee it in 3D behind you: grab it and spin it\nAll the controls: PC CONTROLS / VR CONTROLS",
                                       "500 km d\u2019altitude, 97,4\u00b0, h\u00e9liosynchrone\nUn tour de la Terre toutes les 94,6 minutes\nEn 3D derri\u00e8re vous : attrapez-la et tournez-la\nToutes les commandes : COMMANDES PC / RV" } },

            // controls screens
            { "ctl.kbTitle",   new[] { "KEYBOARD & MOUSE", "CLAVIER ET SOURIS" } },
            { "ctl.vrTitle",   new[] { "VR HEADSET", "CASQUE RV" } },
            { "ctl.camHead",   new[] { "CAMERA AND TIME", "CAM\u00c9RA ET TEMPS" } },
            { "ctl.camKeys",   new[] { "Mouse drag\nScroll\nV / F\nShift + \u2190 \u2192\nSpace\n= / -\n\u2190 / \u2192\nBackspace\nEsc",
                                       "Souris\nMolette\nV / F\nMaj + \u2190 \u2192\nEspace\n= / -\n\u2190 / \u2192\nRetour arr.\n\u00c9chap" } },
            { "ctl.camDo",     new[] { "orbit camera\nzoom\nnext / first view\nturn camera\npause\nfaster / slower\njump 1 min\nrestart\nmain menu",
                                       "tourner autour\nzoom\nvue suiv. / 1re\ntourner\npause\nvite / lent\nsauter 1 min\nrecommencer\nmenu" } },
            { "ctl.satHead",   new[] { "SATELLITE", "SATELLITE" } },
            { "ctl.satKeys",   new[] { "1 / 2\n3\n4\n5\n6\n7\nN\nS\nR\nT", "1 / 2\n3\n4\n5\n6\n7\nN\nS\nR\nT" } },
            { "ctl.satDo",     new[] { "wing -Y / +Y\nantenna\njam antenna\njam wings\nstow wings\nstow antenna\npoint at Earth\npoint at Sun\nimaging roll\ntumble",
                                       "panneau -Y / +Y\nantenne\nbloquer antenne\nbloquer panneaux\nreplier panneaux\nreplier antenne\nviser la Terre\nviser le Soleil\nroulis imagerie\nculbute" } },
            { "ctl.dispHead",  new[] { "DISPLAY", "AFFICHAGE" } },
            { "ctl.dispKeys",  new[] { "G\nB\nL\nM\nH", "G\nB\nL\nM\nH" } },
            { "ctl.dispDo",    new[] { "body axes\nboresight\nview frame\ndata source\ninfo panel",
                                       "axes\nvis\u00e9e cam\u00e9ra\nrep\u00e8re de vue\nsource\npanneau d\u2019infos" } },
            { "ctl.vrHead",    new[] { "IN THE SIMULATION", "DANS LA SIMULATION" } },
            { "ctl.vrKeys",    new[] { "Left stick\nRight stick\nA\nB\nX\nY\nLeft grip\nMenu\nRight trigger\nLeft trigger",
                                       "Stick gauche\nStick droit\nA\nB\nX\nY\nGrip gauche\nMenu\nG\u00e2chette dr.\nG\u00e2chette g." } },
            { "ctl.vrDo",      new[] { "move\nturn / rise, sink\nnext wing / unjam\nantenna / unjam\ninfo panel\nspeed 1x / 10x / 60x\npause\nrestart\nbody axes\nboresight",
                                       "avancer\ntourner / monter\npanneau / d\u00e9bloquer\nantenne / d\u00e9bloquer\npanneau d\u2019infos\nvitesse 1x/10x/60x\npause\nrecommencer\naxes\nvis\u00e9e cam\u00e9ra" } },
            { "ctl.note",      new[] { "Satellite keys work in Sandbox mode only.", "Touches du satellite : mode bac \u00e0 sable seulement." } },
            { "ctl.vrNote",    new[] { "In this menu: point a controller and pull the trigger.", "Dans ce menu : pointez une manette et appuyez sur la g\u00e2chette." } },

            // other screens and signs
            { "map.title",     new[] { "GROUND TRACK", "TRACE AU SOL" } },
            { "map.orbit",     new[] { "500 KM SUN-SYNCHRONOUS", "500 KM H\u00c9LIOSYNCHRONE" } },
            { "status.title",  new[] { "SPACECRAFT STATUS", "\u00c9TAT DU SATELLITE" } },
            { "status.names",  new[] { "UTC\nSPACECRAFT\nORBIT\nPERIOD\nINCLINATION\nSOLAR WINGS\nANTENNA\nDATA SOURCE",
                                       "UTC\nSATELLITE\nORBITE\nP\u00c9RIODE\nINCLINAISON\nPANNEAUX\nANTENNE\nDONN\u00c9ES" } },
            { "status.orbit",  new[] { "500 km sun-synchronous", "500 km h\u00e9liosynchrone" } },
            { "status.stowed", new[] { "STOWED", "REPLI\u00c9" } },
            { "status.awaiting", new[] { "AWAITING GO FOR SIMULATION", "EN ATTENTE DU FEU VERT" } },
            { "status.go",     new[] { "GO FOR SIMULATION", "FEU VERT POUR LA SIMULATION" } },
            { "sign.mc",       new[] { "MISSION CONTROL", "CENTRE DE CONTR\u00d4LE" } },
            { "sign.ops",      new[] { "MISSION OPERATIONS", "OP\u00c9RATIONS DE MISSION" } },
            { "holo.title",    new[] { "ORBIT 3D", "ORBITE 3D" } },
            { "holo.facts",    new[] { "500 km  |  97.4\u00b0  |  one lap = 94.6 min  |  height drawn 4\u00d7",
                                       "500 km  |  97,4\u00b0  |  un tour = 94,6 min  |  altitude dessin\u00e9e 4\u00d7" } },
            { "holo.grab",     new[] { "VR: point at it and hold the trigger to spin it   |   PC: click and drag",
                                       "RV : pointez et maintenez la g\u00e2chette pour la tourner   |   PC : cliquez et glissez" } },
            { "dir.west",      new[] { " W", " O" } },
        };
    }
}
