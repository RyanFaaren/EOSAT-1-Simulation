// EOSAT-1 VR - the main menu. The first thing you see: a mission control room with three
// wall screens. The centre screen is the menu (title, Start, Settings, Quit); the left one
// shows the ground track of a 500 km sun-synchronous orbit, the right one the spacecraft
// status. Start fades out and loads the simulation scene.
//
// Desktop: point with the mouse and click.  Enter = start, Esc = back.
// Headset: point either controller and pull its trigger (or press A / X). The left stick
//          walks you around the room.
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
        float lookYaw, lookPitch;

        // screens
        struct ScreenRect
        {
            public Transform Plane;
            public Vector2 Size;
        }
        readonly List<ScreenRect> screens = new List<ScreenRect>();
        GameObject mainPage, settingsPage;
        readonly List<MenuButton> mainButtons = new List<MenuButton>();
        readonly List<MenuButton> settingsButtons = new List<MenuButton>();
        readonly MenuButton[] settingButtons = new MenuButton[5];
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

            BuildRig();
            MissionControlRoom.Build(transform, roomShader, consoleShader, accent);
            BuildCentreScreen();
            BuildMapScreen();
            BuildStatusScreen();
            BuildSignage();
            BuildPointer();
            BuildFade();
            BuildSounds();

            RefreshSettings();
            ShowSettings(false);
            UpdateSlowDisplays();
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

        MenuButton Button(List<MenuButton> list, string name, Transform parent, Vector3 pos, Vector2 size, string label,
                          float textHeight, bool primary, Action onClick)
        {
            var b = new MenuButton(name, parent, pos, size, label, textHeight, panelShader, font, textMaterial, accent, primary);
            b.OnClick = onClick;
            list.Add(b);
            return b;
        }

        void BuildCentreScreen()
        {
            Transform screen = BuildScreen("Menu Screen", new Vector3(0f, 2.95f, MissionControlRoom.Front - 0.15f), 0f, new Vector2(6f, 3.4f), false);
            const float pageScale = 1.13f;   // the page layouts below are drawn for a 5 x 3 m screen

            // ---- main page
            mainPage = MenuKit.Group("Main Page", screen, new Vector3(0f, 0f, -0.015f)).gameObject;
            Transform p = mainPage.transform;
            p.localScale = new Vector3(pageScale, pageScale, 1f);
            TextMesh titleText = Label("Title", p, new Vector3(0f, 0.98f, 0f), title, 0.66f, Color.white, TextAnchor.MiddleCenter);
            titleText.fontStyle = FontStyle.Bold;
            Label("Subtitle", p, new Vector3(0f, 0.5f, 0f), MenuKit.Spaced(subtitle), 0.15f, accent, TextAnchor.MiddleCenter);
            Divider(p, 0.34f, 2.6f);
            Label("Tagline", p, new Vector3(0f, 0.21f, 0f), "Earth observation satellite  |  500 km sun-synchronous orbit", 0.08f,
                  TextDim, TextAnchor.MiddleCenter);

            var buttonSize = new Vector2(2.5f, 0.4f);
            Button(mainButtons, "Start Button", p, new Vector3(0f, -0.14f, 0f), buttonSize, "START SIMULATION", 0.17f, true, StartSimulation);
            Button(mainButtons, "Settings Button", p, new Vector3(0f, -0.62f, 0f), buttonSize, "SETTINGS", 0.17f, false,
                   delegate { ShowSettings(true); });
            Button(mainButtons, "Quit Button", p, new Vector3(0f, -1.1f, 0f), buttonSize, "QUIT", 0.17f, false, Quit);

            // ---- settings page
            settingsPage = MenuKit.Group("Settings Page", screen, new Vector3(0f, 0f, -0.015f)).gameObject;
            Transform s = settingsPage.transform;
            s.localScale = new Vector3(pageScale, pageScale, 1f);
            Label("Header", s, new Vector3(0f, 1.16f, 0f), MenuKit.Spaced("SETTINGS"), 0.26f, Color.white, TextAnchor.MiddleCenter);
            Divider(s, 0.93f, 4.3f);

            string[] names = { "DATA SOURCE", "TIME SPEED AT START", "INFO PANEL", "CONTROL HINTS", "SNAP TURN (HEADSET)" };
            string[] notes =
            {
                "",
                "Change it any time with - and = (or Y on the left controller)",
                "Tumbling, pointing, sunlight and antenna at a glance",
                "The key list along the bottom of the screen",
                "How far the right stick turns you",
            };
            Action[] actions =
            {
                delegate { MenuSettings.NextFeed(); },
                delegate { MenuSettings.NextSpeed(); },
                delegate { MenuSettings.ShowInfoPanel = !MenuSettings.ShowInfoPanel; },
                delegate { MenuSettings.ShowHints = !MenuSettings.ShowHints; },
                delegate { MenuSettings.NextSnap(); },
            };
            for (int i = 0; i < names.Length; i++)
            {
                float y = 0.62f - 0.37f * i;
                Label("Setting Name", s, new Vector3(-2.15f, y + 0.055f, 0f), names[i], 0.115f, TextBright, TextAnchor.MiddleLeft);
                TextMesh note = Label("Setting Note", s, new Vector3(-2.15f, y - 0.085f, 0f), notes[i], 0.068f, TextDim, TextAnchor.MiddleLeft);
                if (i == 0) feedDescription = note;
                Action change = actions[i];
                settingButtons[i] = Button(settingsButtons, "Setting Button", s, new Vector3(1.3f, y, 0f), new Vector2(1.6f, 0.28f), "", 0.11f, false,
                                           delegate
                                           {
                                               change();
                                               MenuSettings.Save();
                                               RefreshSettings();
                                           });
            }
            Button(settingsButtons, "Back Button", s, new Vector3(0f, -1.24f, 0f), new Vector2(1.4f, 0.3f), "BACK", 0.13f, true,
                   delegate { ShowSettings(false); });

            // ---- shared footer
            hintText = Label("Hint", screen, new Vector3(0f, -1.59f, -0.015f), "", 0.075f, TextDim, TextAnchor.MiddleCenter);
        }

        void BuildMapScreen()
        {
            Transform screen = BuildScreen("Ground Track Screen", new Vector3(-4.6f, 2.9f, 5.1f), -28f, new Vector2(3f, 2.2f), true);
            Label("Header", screen, new Vector3(-1.34f, 0.93f, -0.015f), MenuKit.Spaced("GROUND TRACK"), 0.1f, accent, TextAnchor.MiddleLeft);
            Label("Orbit", screen, new Vector3(1.34f, 0.93f, -0.015f), "500 KM SUN-SYNCHRONOUS", 0.07f, TextDim, TextAnchor.MiddleRight);
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
            Label("Header", screen, new Vector3(-1.34f, 0.93f, -0.015f), MenuKit.Spaced("SPACECRAFT STATUS"), 0.1f, accent, TextAnchor.MiddleLeft);
            TextMesh names = Label("Names", screen, new Vector3(-1.34f, 0.72f, -0.015f),
                                   "UTC\nSPACECRAFT\nORBIT\nPERIOD\nINCLINATION\nSOLAR WINGS\nANTENNA\nDATA SOURCE",
                                   0.1f, TextDim, TextAnchor.UpperLeft);
            names.lineSpacing = 1.35f;
            statusValues = Label("Values", screen, new Vector3(-0.22f, 0.72f, -0.015f), "", 0.1f, TextBright, TextAnchor.UpperLeft);
            statusValues.lineSpacing = 1.35f;
            goText = Label("Go", screen, new Vector3(0f, -0.8f, -0.015f), "AWAITING GO FOR SIMULATION", 0.11f, Amber, TextAnchor.MiddleCenter);
        }

        void BuildSignage()
        {
            // strip above the menu screen
            float z = MissionControlRoom.Front - 0.15f;
            Transform strip = MenuKit.Group("Sign Strip", transform, new Vector3(0f, 4.92f, z));
            MenuKit.Panel("Background", strip, panelShader, Vector3.zero, new Vector2(6f, 0.28f),
                          new Color(0.03f, 0.065f, 0.115f, 1f), new Color(0.02f, 0.04f, 0.075f, 1f),
                          new Color(accent.r, accent.g, accent.b, 0.6f), 0.04f, 0.2f, MenuKit.OrderPanel);
            Label("Name", strip, new Vector3(-2.8f, 0f, -0.015f), MenuKit.Spaced("MISSION CONTROL"), 0.11f, TextBright, TextAnchor.MiddleLeft);
            clockText = Label("Clock", strip, new Vector3(2.8f, 0f, -0.015f), "", 0.11f, Amber, TextAnchor.MiddleRight);

            // lettering on the back wall, for when you turn around
            Transform back = MenuKit.Group("Back Wall Sign", transform, new Vector3(0f, 3f, MissionControlRoom.Back + 0.02f));
            back.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh big = Label("Name", back, new Vector3(0f, 0.25f, 0f), title, 0.7f, new Color(0.4f, 0.47f, 0.56f, 1f), TextAnchor.MiddleCenter);
            big.fontStyle = FontStyle.Bold;
            Label("Line", back, new Vector3(0f, -0.3f, 0f), MenuKit.Spaced("MISSION OPERATIONS"), 0.14f,
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
        void ShowSettings(bool show)
        {
            if (mainPage != null) mainPage.SetActive(!show);
            if (settingsPage != null) settingsPage.SetActive(show);
            hovered = null;
            hintOverride = null;
        }

        void RefreshSettings()
        {
            settingButtons[0].Label.text = MenuSettings.FeedLabel;
            settingButtons[1].Label.text = MenuSettings.SpeedLabel;
            settingButtons[2].Label.text = MenuSettings.ShowInfoPanel ? "SHOWN" : "HIDDEN";
            settingButtons[3].Label.text = MenuSettings.ShowHints ? "SHOWN" : "HIDDEN";
            settingButtons[4].Label.text = MenuSettings.SnapLabel;
            if (feedDescription != null) feedDescription.text = MenuSettings.FeedDescription;
            UpdateSlowDisplays();
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
                hintOverride = "<color=#FF6B6B>Scene '" + simulationScene + "' is not in the Build Settings list. " +
                               "Use EOSAT-1 > Build Demo Scene, then try again.</color>";
                Debug.LogError("EOSAT-1 menu: scene '" + simulationScene + "' is not in File > Build Profiles (Build Settings). " +
                               "Run EOSAT-1 > Build Demo Scene to create and add it.");
                busy = false;
                yield break;
            }

            hintOverride = "LOADING SIMULATION ...";
            if (goText != null) goText.text = "<color=#7CFC9A>GO FOR SIMULATION</color>";
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

            // what is being pointed at
            List<MenuButton> active = settingsPage != null && settingsPage.activeSelf ? settingsButtons : mainButtons;
            MenuButton hit = null;
            float hitDistance = float.MaxValue;
            if (hasRay && !busy)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    float d;
                    if (active[i].Enabled && active[i].Hit(ray, out d) && d < hitDistance)
                    {
                        hit = active[i];
                        hitDistance = d;
                    }
                }
            }
            if (hit != hovered)
            {
                hovered = hit;
                if (hit != null) Play(tickClip);
            }
            for (int i = 0; i < mainButtons.Count; i++) mainButtons[i].Animate(mainButtons[i] == hovered, dt);
            for (int i = 0; i < settingsButtons.Count; i++) settingsButtons[i].Animate(settingsButtons[i] == hovered, dt);

            ShowPointer(headset, hasRay, ray, hit != null ? hitDistance : ScreenDistance(ray));

            // act
            if (!busy)
            {
                bool onSettings = settingsPage != null && settingsPage.activeSelf;
                if (click && hovered != null)
                {
                    MenuButton pressed = hovered;
                    hovered = null;
                    Play(clickClip);
                    if (pressed.OnClick != null) pressed.OnClick();
                }
                else if (!onSettings && InputShim.Down(KeyCode.Return))
                {
                    Play(clickClip);
                    StartSimulation();
                }
                else if (onSettings && InputShim.Down(KeyCode.Escape))
                {
                    Play(clickClip);
                    ShowSettings(false);
                }
            }

            // displays
            orbitPhase += dt * 2f * Mathf.PI / SecondsPerMapOrbit;
            MoveMarker();
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
                string hint = hintOverride != null ? hintOverride
                    : headset ? "Point a controller at a button and pull the trigger"
                    : (settingsPage != null && settingsPage.activeSelf ? "Click a setting to change it  |  Esc goes back"
                                                                         : "Click a button  |  Enter starts the simulation");
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
            // fixed viewpoint that leans a little towards the mouse
            Vector2 m = InputShim.MousePosition();
            float nx = Mathf.Clamp(m.x / Mathf.Max(1f, Screen.width) * 2f - 1f, -1f, 1f);
            float ny = Mathf.Clamp(m.y / Mathf.Max(1f, Screen.height) * 2f - 1f, -1f, 1f);
            float k = 1f - Mathf.Exp(-dt * 4f);
            lookYaw = Mathf.Lerp(lookYaw, nx * 4f, k);
            lookPitch = Mathf.Lerp(lookPitch, ny * 2.5f, k);
            head.localPosition = new Vector3(0f, eyeHeight, 0f);
            head.localRotation = Quaternion.Euler(-11f - lookPitch, lookYaw, 0f);
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
                                    "500 km sun-synchronous\n" +
                                    "94.6 min\n" +
                                    "97.4 deg\n" +
                                    "<color=#FFD166>STOWED</color>\n" +
                                    "<color=#FFD166>STOWED</color>\n" +
                                    "<color=#7CFC9A>" + MenuSettings.FeedLabel + "</color>";
            }
            if (trackReadout != null)
            {
                float lon, lat;
                TrackPoint(orbitPhase, out lon, out lat);
                trackReadout.text = "LAT " + Mathf.Abs(lat).ToString("0.0", Inv) + (lat >= 0f ? " N" : " S") +
                                    "     LON " + Mathf.Abs(lon).ToString("0.0", Inv) + (lon >= 0f ? " E" : " W") +
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
}
