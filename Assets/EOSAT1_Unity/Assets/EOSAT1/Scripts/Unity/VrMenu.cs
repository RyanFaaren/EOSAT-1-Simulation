// EOSAT-1 VR - the menu you pull up in the headset, and the messages shown in the headset.
//
// Press the MENU button on the left controller: a panel opens in front of you, in the same
// style as the main menu. Point a controller at it and pull the trigger to press a button.
//
//   CONTROLS      what every button and stick on the two controllers does
//   SATELLITE     open / fold the solar panels and antenna, point the satellite, test a jam,
//                 and the data source (Sandbox / Replay / Live link, the computer's M key)
//   TIME          pause, slower / faster, skip, start again, and every speed the computer has
//                 (0.5x ... 300x), one button each
//   VIEWS         the satellite camera and its zoom, the whole Earth, the orbit path, the
//                 helper lines, the view frame, the computer's viewpoints, the info pad
//   bottom row    back to where you started, the main menu, close
//
// Together with the controllers this does everything the computer's keys and buttons do.
//
// The panel stays where it opened (it moves with you when you fly). Press MENU again, or
// CLOSE MENU, to put it away.
//
// Messages: what a command did ("Solar panel -Y: deploy"), a reminder of the MENU button
// for the first seconds, and the jetpack's height line, shown low in front of you.
//
// VrRig adds this component by itself in the simulation.
using System;
using System.Collections.Generic;
using System.Text;
using Eosat1.Core;
using UnityEngine;
using UnityEngine.XR;

namespace Eosat1
{
    [DefaultExecutionOrder(20)]   // after VrRig, so the controller poses are this frame's
    public sealed class VrMenu : MonoBehaviour
    {
        public VrRig rig;
        [Tooltip("How far in front of you the menu opens, in metres.")]
        public float distance = 0.85f;
        public Color accent = new Color(0.25f, 0.8f, 1f, 1f);
        [Tooltip("Show what each command did, low in front of you, for a few seconds.")]
        public bool showMessages = true;
        [Tooltip("Seconds the 'press MENU' reminder stays up after the headset goes on (until the menu is first opened).")]
        public float reminderSeconds = 25f;

        public bool IsOpen { get { return open; } }

        static readonly Vector2 PanelSize = new Vector2(0.92f, 0.64f);
        static readonly Color TextBright = new Color(0.93f, 0.96f, 1f, 1f);
        static readonly Color TextDim = new Color(0.55f, 0.66f, 0.76f, 1f);
        static readonly Color ActionText = new Color(0.74f, 0.82f, 0.9f, 1f);
        static readonly Color Amber = new Color(1f, 0.82f, 0.4f, 1f);
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        EosatApp app;
        Font font;
        Material textMaterial;
        Shader panelShader, unlitShader;
        readonly List<Material> ownMaterials = new List<Material>();

        // the menu
        Transform root, content, tabMarker;
        const int PageCount = 4;
        readonly Transform[] pages = new Transform[PageCount];
        readonly MenuButton[] tabs = new MenuButton[PageCount];
        readonly List<MenuButton> buttons = new List<MenuButton>();
        readonly List<MenuButton> commandButtons = new List<MenuButton>();
        readonly List<MenuButton> timeButtons = new List<MenuButton>();
        MenuButton pauseButton, jamPanelButton, jamAntennaButton, sourceButton;
        MenuButton camButton, earthButton, pathButton, panelLineButton, axesButton, cameraLineButton;
        MenuButton zoomInButton, zoomOutButton, frameButton, padButton;
        readonly List<MenuButton> speedButtons = new List<MenuButton>();
        Transform speedMarker;
        TextMesh footer, commandNote;
        Transform laser, reticle;
        MenuButton hovered;
        bool built, open, everOpened, useRight = true;
        int page;
        float nextRefresh;

        // the messages
        Transform strip;
        MeshRenderer stripBack;
        TextMesh stripText;
        string stripShown = "", flightLine, lastMessage, lastMessageWrapped;
        float reminderUntil = -1f;
        bool headsetSeen, stripPlaced;

        // ------------------------------------------------------------------ setting up
        void Start()
        {
            if (rig == null) rig = GetComponent<VrRig>();
            if (rig == null || rig.app == null)
            {
                enabled = false;
                return;
            }
            app = rig.app;
            Shader textShader = Shader.Find("EOSAT1/HudText");
            panelShader = Shader.Find("EOSAT1/UiPanel");
            unlitShader = Shader.Find("EOSAT1/UnlitColor");
            if (textShader == null)
            {
                Debug.LogWarning("EOSAT-1: the VR menu needs the shader EOSAT1/HudText (EOSAT1/Shaders folder).");
                enabled = false;
                return;
            }
            if (panelShader == null) panelShader = textShader;   // plain rectangles instead of rounded ones
            if (unlitShader == null) unlitShader = textShader;
            font = LoadFont();
            textMaterial = MenuKit.NewMaterial(textShader, "VR Menu Text");
            ownMaterials.Add(textMaterial);
            AssignFontTexture();

            BuildMenu();
            BuildPointer();
            BuildStrip();
            SetLayer(root.gameObject);
            SetLayer(laser.gameObject);
            SetLayer(reticle.gameObject);
            SetLayer(strip.gameObject);
            built = true;
            SetPage(0);
        }

        void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
        }

        void OnDisable()
        {
            Font.textureRebuilt -= OnFontRebuilt;
        }

        void OnDestroy()
        {
            for (int i = 0; i < ownMaterials.Count; i++) if (ownMaterials[i] != null) Destroy(ownMaterials[i]);
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

        static void SetLayer(GameObject go)
        {
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = ViewDirector.HiddenFromSatCameraLayer;
        }

        static Color WithAlpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }

        TextMesh Label(string name, Transform parent, Vector3 pos, string text, float lineHeight, Color color, TextAnchor anchor)
        {
            return MenuKit.Text(name, parent, pos, text, lineHeight, color, anchor, font, textMaterial);
        }

        MenuButton Button(string name, Transform parent, Vector3 pos, Vector2 size, string label, float textHeight, bool primary, Action onClick)
        {
            var b = new MenuButton(name, parent, pos, size, label, textHeight, panelShader, font, textMaterial, accent, primary);
            b.OnClick = onClick;
            buttons.Add(b);
            return b;
        }

        Transform Bar(string name, Transform parent, Vector3 pos, Vector2 size, Color color)
        {
            Material m = MenuKit.NewMaterial(unlitShader, name);
            m.SetColor("_Color", color);
            ownMaterials.Add(m);
            return MenuKit.MeshObject(name, parent, MenuKit.Quad, m, pos, new Vector3(size.x, size.y, 1f)).transform;
        }

        /// <summary>Where a button goes on the SATELLITE, TIME and VIEWS pages: three columns, four rows.</summary>
        static Vector3 Cell(int col, int row)
        {
            return new Vector3(-0.285f + col * 0.285f, 0.105f - row * 0.065f, 0f);
        }

        /// <summary>Where tab i sits along the top of the menu.</summary>
        static float TabX(int i)
        {
            return -0.33f + i * 0.22f;
        }

        static string SpeedText(float s)
        {
            return (s < 1f ? s.ToString("0.0", Inv) : s.ToString("0", Inv)) + "x";
        }

        void BuildMenu()
        {
            var rootGo = new GameObject("VR Menu");
            root = rootGo.transform;
            root.SetParent(rig.transform, false);

            Color top = new Color(0.03f, 0.065f, 0.115f, 0.96f), bottom = new Color(0.012f, 0.025f, 0.05f, 0.96f);
            MeshRenderer back = MenuKit.Panel("Background", root, panelShader, Vector3.zero, PanelSize, top, bottom,
                                              WithAlpha(accent, 0.85f), 0.006f, 0.04f, MenuKit.OrderPanel);
            ownMaterials.Add(back.sharedMaterial);
            content = MenuKit.Group("Content", root, new Vector3(0f, 0f, -0.004f));

            Label("Title", content, new Vector3(-0.42f, 0.272f, 0f), MenuKit.Spaced("EOSAT-1 VR MENU"), 0.032f, accent, TextAnchor.MiddleLeft);
            Label("Close Hint", content, new Vector3(0.42f, 0.272f, 0f), "Point and pull the trigger.  MENU button closes.", 0.018f,
                  TextDim, TextAnchor.MiddleRight);

            // tabs
            var tabSize = new Vector2(0.205f, 0.058f);
            string[] tabNames = { "CONTROLS", "SATELLITE", "TIME", "VIEWS" };
            for (int i = 0; i < PageCount; i++)
            {
                int index = i;
                tabs[i] = Button("Tab " + tabNames[i], content, new Vector3(TabX(i), 0.205f, 0f), tabSize, tabNames[i], 0.024f, false,
                                 delegate { SetPage(index); });
            }
            tabMarker = Bar("Tab Marker", content, new Vector3(TabX(0), 0.168f, -0.002f), new Vector2(0.205f, 0.006f), accent);
            Bar("Divider", content, new Vector3(0f, 0.16f, -0.001f), new Vector2(0.84f, 0.0015f), Color.Lerp(Color.black, accent, 0.45f));

            for (int i = 0; i < PageCount; i++) pages[i] = MenuKit.Group("Page " + tabNames[i], content, Vector3.zero);
            BuildControlsPage(pages[0]);
            BuildSatellitePage(pages[1]);
            BuildTimePage(pages[2]);
            BuildViewsPage(pages[3]);

            // bottom row, on every page
            var wide = new Vector2(0.27f, 0.055f);
            Button("Back To Start", content, new Vector3(-0.285f, -0.215f, 0f), wide, "BACK TO START", 0.02f, false,
                   delegate
                   {
                       rig.ReturnHome();
                       Open();   // the menu comes with you
                       app.Say("Back at the start");
                   });
            Button("Main Menu", content, new Vector3(0f, -0.215f, 0f), wide, "MAIN MENU", 0.02f, false, delegate { LeaveToMenu(); });
            Button("Close", content, new Vector3(0.285f, -0.215f, 0f), wide, "CLOSE MENU", 0.02f, true, delegate { Close(); });

            footer = Label("Status", content, new Vector3(0f, -0.283f, 0f), "", 0.018f, TextDim, TextAnchor.MiddleCenter);
            rootGo.SetActive(false);
        }

        void BuildControlsPage(Transform p)
        {
            const float h = 0.021f, spacing = 1.25f;
            // left hand
            Label("Left Heading", p, new Vector3(-0.42f, 0.13f, 0f), "LEFT HAND", 0.024f, accent, TextAnchor.MiddleLeft);
            TextMesh lk = Label("Left Keys", p, new Vector3(-0.42f, 0.108f, 0f),
                                "Stick\nStick click\nX\nY\nTrigger\nGrip\nMENU", h, Color.white, TextAnchor.UpperLeft);
            lk.lineSpacing = spacing;
            TextMesh la = Label("Left Actions", p, new Vector3(-0.29f, 0.108f, 0f),
                                "fly: forwards, back, sideways\norbit path on / off\ninfo pad on / off\nnext speed, 0.5x to 300x\ncamera line on / off\npause / play\nopen / close this menu",
                                h, ActionText, TextAnchor.UpperLeft);
            la.lineSpacing = spacing;
            // right hand
            Label("Right Heading", p, new Vector3(0.04f, 0.13f, 0f), "RIGHT HAND", 0.024f, accent, TextAnchor.MiddleLeft);
            TextMesh rk = Label("Right Keys", p, new Vector3(0.04f, 0.108f, 0f),
                                "Stick ← →\nStick ↑ ↓\nStick click\nA\nB\nTrigger\nGrip", h, Color.white, TextAnchor.UpperLeft);
            rk.lineSpacing = spacing;
            TextMesh ra = Label("Right Actions", p, new Vector3(0.175f, 0.108f, 0f),
                                "turn left / right\njetpack up / down\nsatellite camera screen\nopen next solar panel\nopen the antenna\nbody axes on / off\nwhole Earth view",
                                h, ActionText, TextAnchor.UpperLeft);
            ra.lineSpacing = spacing;
            Label("Tip", p, new Vector3(0f, -0.128f, 0f),
                  "Info pad: turn your left wrist towards you, as if checking a watch.\n" +
                  "Walk around for real, or fly with the jetpack: up to 6 m from the satellite, and above and below it.\n" +
                  "A and B also free a jam.  The other pages have a button for everything the computer can do.",
                  0.0165f, TextDim, TextAnchor.MiddleCenter);
        }

        MenuButton Command(Transform p, int col, int row, string label, SandboxCommand cmd)
        {
            MenuButton b = Button(label, p, Cell(col, row), new Vector2(0.27f, 0.055f), label, 0.02f, false,
                                  delegate { app.Command(cmd); });
            commandButtons.Add(b);
            return b;
        }

        void BuildSatellitePage(Transform p)
        {
            Command(p, 0, 0, "OPEN PANEL 1", SandboxCommand.DeployWingMinusY);
            Command(p, 1, 0, "OPEN PANEL 2", SandboxCommand.DeployWingPlusY);
            Command(p, 2, 0, "OPEN ANTENNA", SandboxCommand.DeployAntenna);
            Command(p, 0, 1, "FOLD PANELS", SandboxCommand.StowWings);
            Command(p, 1, 1, "FOLD ANTENNA", SandboxCommand.StowAntenna);
            Command(p, 2, 1, "TUMBLE", SandboxCommand.AttitudeTumble);
            Command(p, 0, 2, "CAMERA AT EARTH", SandboxCommand.AttitudeNadir);
            Command(p, 1, 2, "PANELS AT SUN", SandboxCommand.AttitudeSun);
            Command(p, 2, 2, "ROLL TO IMAGE", SandboxCommand.AttitudeRollImaging);
            jamPanelButton = Command(p, 0, 3, "JAM A PANEL", SandboxCommand.ToggleWingJam);
            jamAntennaButton = Command(p, 1, 3, "JAM THE ANTENNA", SandboxCommand.ToggleAntennaJam);
            // the computer's M key: always usable, it is how you get back to Sandbox
            sourceButton = Button("Source", p, Cell(2, 3), new Vector2(0.27f, 0.055f), "SOURCE: SANDBOX", 0.02f, false,
                                  delegate { app.CycleFeed(); });
            commandNote = Label("Note", p, new Vector3(0f, -0.15f, 0f),
                                "In this mode the satellite is commanded from mission control.  SOURCE switches to Sandbox.", 0.018f, Amber,
                                TextAnchor.MiddleCenter);
            commandNote.gameObject.SetActive(false);
        }

        MenuButton TimeButton(Transform p, int col, int row, string label, Action onClick)
        {
            MenuButton b = Button(label, p, Cell(col, row), new Vector2(0.27f, 0.055f), label, 0.02f, false, onClick);
            timeButtons.Add(b);
            return b;
        }

        MenuButton ViewButton(Transform p, int col, int row, string label, Action onClick)
        {
            return Button(label, p, Cell(col, row), new Vector2(0.27f, 0.055f), label, 0.02f, false, onClick);
        }

        void BuildTimePage(Transform p)
        {
            pauseButton = TimeButton(p, 0, 0, "PAUSE", delegate { app.TogglePause(); });
            TimeButton(p, 1, 0, "SLOWER", delegate { app.SlowDown(); });
            TimeButton(p, 2, 0, "FASTER", delegate { app.SpeedUp(); });
            TimeButton(p, 0, 1, "BACK 1 MIN", delegate { app.JumpSeconds(-60); });
            TimeButton(p, 1, 1, "SKIP 1 MIN", delegate { app.JumpSeconds(60); });
            TimeButton(p, 2, 1, "START AGAIN", delegate { app.Restart(); });

            // every speed the computer has (its - and = keys and its speed bar), one button each
            Label("Speed Heading", p, new Vector3(-0.42f, -0.003f, 0f), "SPEED", 0.02f, accent, TextAnchor.MiddleLeft);
            int n = app.speedSteps != null ? app.speedSteps.Length : 0;
            const float gap = 0.0075f;
            float w = n > 0 ? (0.855f - gap * (n - 1)) / n : 0.1f;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                string text = SpeedText(app.speedSteps[i]);
                var at = new Vector3(-0.4275f + w * 0.5f + i * (w + gap), -0.047f, 0f);
                MenuButton b = Button("Speed " + text, p, at, new Vector2(w, 0.05f), text, 0.019f, false, delegate { app.SetSpeedIndex(index); });
                timeButtons.Add(b);
                speedButtons.Add(b);
            }
            speedMarker = Bar("Speed Marker", p, new Vector3(0f, -0.0775f, -0.002f), new Vector2(w, 0.005f), accent);
            Label("Speed Note", p, new Vector3(0f, -0.118f, 0f),
                  "The same speeds as the computer (its - and = keys and speed bar).\n" +
                  "Y on the left controller picks the next one; the info pad on your wrist has a slider.",
                  0.0165f, TextDim, TextAnchor.MiddleCenter);
        }

        void BuildViewsPage(Transform p)
        {
            camButton = ViewButton(p, 0, 0, "SAT CAMERA", delegate { if (rig.views != null) rig.views.CycleSatCamera(); });
            earthButton = ViewButton(p, 1, 0, "WHOLE EARTH", delegate { if (rig.views != null) rig.views.ToggleOverview(); });
            pathButton = ViewButton(p, 2, 0, "ORBIT PATH", delegate { if (rig.views != null) rig.views.TogglePath(); });
            panelLineButton = ViewButton(p, 0, 1, "PANEL LINE", delegate { if (rig.views != null) rig.views.TogglePanelAxes(); });
            axesButton = ViewButton(p, 1, 1, "BODY AXES", delegate
            {
                if (rig.views != null) rig.views.ToggleBodyAxes();
                else app.ToggleAxes();
            });
            cameraLineButton = ViewButton(p, 2, 1, "CAMERA LINE", delegate
            {
                if (rig.views != null) rig.views.ToggleCameraLine();
                else app.ToggleBoresight();
            });
            // the computer's [ and ] keys: the satellite camera's zoom, down to the real field of view
            zoomInButton = ViewButton(p, 0, 2, "CAMERA ZOOM IN", delegate { ZoomSatCamera(2f); });
            zoomOutButton = ViewButton(p, 1, 2, "CAMERA ZOOM OUT", delegate { ZoomSatCamera(-2f); });
            // the computer's L key
            frameButton = ViewButton(p, 2, 2, "VIEW: EARTH BELOW", delegate { app.ToggleViewFrame(); });
            // the computer's V and F keys: its camera angles, as places to stand
            ViewButton(p, 0, 3, "NEXT VIEWPOINT", delegate
            {
                rig.NextViewpoint();
                Open();   // the menu comes with you
            });
            ViewButton(p, 1, 3, "FIRST VIEWPOINT", delegate
            {
                rig.FirstViewpoint();
                Open();
            });
            // the computer's H key, for the headset's own info pad (X does the same)
            padButton = ViewButton(p, 2, 3, "INFO PAD: ON", delegate { ToggleInfoPad(); });
        }

        void ZoomSatCamera(float notches)
        {
            ViewDirector v = rig.views;
            if (v == null || !v.SatCameraOn) return;
            v.ZoomSatCamera(notches);
            bool real = v.fieldOfViewDeg <= ViewDirector.TrueFieldOfViewDeg * 1.01f;
            app.Say("Satellite camera: " + v.fieldOfViewDeg.ToString("0.0", Inv) + " degrees wide" + (real ? " (the real field of view)" : ""));
        }

        void ToggleInfoPad()
        {
            InfoPanel info = rig.infoPanel;
            if (info == null) return;
            info.ToggleHeadset();
            app.Say(info.showInHeadset ? "Info pad on: look at your left wrist, like checking a watch" : "Info pad off (X turns it back on)");
        }

        void BuildPointer()
        {
            Material beam = MenuKit.NewMaterial(unlitShader, "VR Menu Pointer");
            beam.SetColor("_Color", accent);
            ownMaterials.Add(beam);
            laser = MenuKit.BoxObject("VR Menu Pointer", rig.transform, beam, Vector3.zero, Vector3.one).transform;
            MeshRenderer dot = MenuKit.Panel("VR Menu Pointer Dot", rig.transform, panelShader, Vector3.zero, new Vector2(0.022f, 0.022f),
                                             Color.white, Color.white, accent, 0.2f, 0.5f, MenuKit.OrderMarker);
            ownMaterials.Add(dot.sharedMaterial);
            reticle = dot.transform;
            laser.gameObject.SetActive(false);
            reticle.gameObject.SetActive(false);
        }

        void BuildStrip()
        {
            strip = MenuKit.Group("VR Messages", rig.transform, Vector3.zero);
            Color top = new Color(0.03f, 0.075f, 0.13f, 0.9f), bottom = new Color(0.02f, 0.05f, 0.09f, 0.9f);
            stripBack = MenuKit.Panel("Background", strip, panelShader, Vector3.zero, new Vector2(0.6f, 0.08f), top, bottom,
                                      WithAlpha(accent, 0.8f), 0.05f, 0.3f, MenuKit.OrderPanel);
            ownMaterials.Add(stripBack.sharedMaterial);
            stripText = Label("Text", strip, new Vector3(0f, 0f, -0.004f), "", 0.028f, TextBright, TextAnchor.MiddleCenter);
            strip.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ opening and closing
        public void Toggle()
        {
            if (open) Close();
            else Open();
        }

        /// <summary>Open the menu in front of the wearer (or bring an open one back in front).</summary>
        public void Open()
        {
            if (!built || rig.head == null) return;
            root.localScale = Vector3.one;
            Transform laptopScreen, side;
            if (LookToPoint && LaptopMarker.Current != null && LaptopMarker.Current.TryGetAnchors(out laptopScreen, out side))
            {
                // controllers off: the menu comes up on the laptop's screen, shrunk to fit it,
                // a little in front of the camera picture
                float fit = Mathf.Min(LaptopMarker.DisplaySize.x / PanelSize.x, LaptopMarker.DisplaySize.y / PanelSize.y);
                root.SetPositionAndRotation(laptopScreen.position - laptopScreen.forward * 0.008f, laptopScreen.rotation);
                root.localScale = Vector3.one * fit;
                root.gameObject.SetActive(true);
                open = true;
                everOpened = true;
                Refresh();
                return;
            }
            Transform head = rig.head;
            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = rig.transform.forward;
            fwd.Normalize();
            root.SetPositionAndRotation(head.position + fwd * Mathf.Max(0.4f, distance) + Vector3.down * 0.06f,
                                        Quaternion.LookRotation(fwd, Vector3.up));
            root.gameObject.SetActive(true);
            open = true;
            everOpened = true;
            Refresh();
        }

        public void Close()
        {
            if (!built) return;
            open = false;
            hovered = null;
            root.gameObject.SetActive(false);
            laser.gameObject.SetActive(false);
            reticle.gameObject.SetActive(false);
        }

        void SetPage(int index)
        {
            page = Mathf.Clamp(index, 0, PageCount - 1);
            for (int i = 0; i < PageCount; i++) pages[i].gameObject.SetActive(i == page);
            Vector3 m = tabMarker.localPosition;
            m.x = TabX(page);
            tabMarker.localPosition = m;
        }

        void LeaveToMenu()
        {
            if (Application.CanStreamedLevelBeLoaded(MenuSettings.MenuSceneName))
                UnityEngine.SceneManagement.SceneManager.LoadScene(MenuSettings.MenuSceneName);
            else
                app.Say("The main menu scene is not in the build list");
        }

        /// <summary>The jetpack's line of text (null to clear it).</summary>
        public void SetFlightLine(string line)
        {
            flightLine = line;
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!built) return;
            if (!XRSettings.isDeviceActive)
            {
                if (open) Close();
                if (strip.gameObject.activeSelf) strip.gameObject.SetActive(false);
                return;
            }
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            if (!headsetSeen)
            {
                headsetSeen = true;
                reminderUntil = now + reminderSeconds;
            }
            UpdateStrip(now, dt);
            if (!open) return;

            if (now >= nextRefresh)
            {
                nextRefresh = now + 0.25f;
                Refresh();
            }
            Ray ray;
            bool hasRay = PointerRay(out ray);
            float hit = 1.5f;
            MenuButton over = hasRay ? Pick(ray, ref hit) : null;
            if (over != hovered)
            {
                hovered = over;
                if (over != null && over.Enabled) VrRig.Buzz(useRight, 0.12f, 0.02f);
            }
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Visible) buttons[i].Animate(buttons[i] == hovered, dt);
            ShowPointer(hasRay, ray, hit);
        }

        /// <summary>Controllers switched off (Settings, or K): you point with your head.</summary>
        static bool LookToPoint { get { return !MenuSettings.UseControllers; } }

        bool PointerRay(out Ray ray)
        {
            if (LookToPoint)
            {
                if (rig.head == null)
                {
                    ray = default(Ray);
                    return false;
                }
                ray = new Ray(rig.head.position, rig.head.forward);
                return true;
            }
            if (rig.TryGetPointerRay(useRight, out ray)) return true;
            if (rig.TryGetPointerRay(!useRight, out ray))
            {
                useRight = !useRight;
                return true;
            }
            return false;
        }

        /// <summary>The button the ray points at, if any; 'distance' becomes how far along the ray the menu is.</summary>
        MenuButton Pick(Ray ray, ref float distanceAlong)
        {
            Vector3 local;
            float d;
            if (MenuKit.RayToLocalPlane(ray, root, out local, out d) &&
                Mathf.Abs(local.x) <= PanelSize.x * 0.5f && Mathf.Abs(local.y) <= PanelSize.y * 0.5f)
                distanceAlong = d;
            MenuButton best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < buttons.Count; i++)
            {
                MenuButton b = buttons[i];
                if (!b.Visible || !b.Hit(ray, out d) || d >= bestD) continue;
                best = b;
                bestD = d;
            }
            if (best != null) distanceAlong = bestD;
            return best;
        }

        void ShowPointer(bool hasRay, Ray ray, float length)
        {
            bool beam = hasRay && !LookToPoint;   // pointing with your head: just the dot, no beam
            if (laser.gameObject.activeSelf != beam) laser.gameObject.SetActive(beam);
            if (reticle.gameObject.activeSelf != hasRay) reticle.gameObject.SetActive(hasRay);
            if (!hasRay) return;
            length = Mathf.Max(0.05f, length - 0.01f);
            Quaternion along = Quaternion.LookRotation(ray.direction);
            laser.SetPositionAndRotation(ray.origin + ray.direction * (length * 0.5f), along);
            laser.localScale = new Vector3(0.003f, 0.003f, length);
            reticle.SetPositionAndRotation(ray.origin + ray.direction * length, root.rotation);
            float s = hovered != null && hovered.Enabled ? 1.5f : 1f;
            reticle.localScale = new Vector3(0.022f * s, 0.022f * s, 1f);
        }

        /// <summary>Left mouse click with the controllers off: press the button you are looking at.</summary>
        public void ClickWhereLooking()
        {
            if (!built || !open || rig.head == null) return;
            var ray = new Ray(rig.head.position, rig.head.forward);
            float d = 1.5f;
            MenuButton b = Pick(ray, ref d);
            if (b == null || !b.Enabled || b.OnClick == null) return;
            b.OnClick();
            if (open) Refresh();
        }

        /// <summary>A trigger was pulled (VrRig calls this while the menu is open).</summary>
        public void Click(bool rightSide)
        {
            if (!built || !open) return;
            useRight = rightSide;
            Ray ray;
            if (!rig.TryGetPointerRay(rightSide, out ray)) return;
            float d = 1.5f;
            MenuButton b = Pick(ray, ref d);
            if (b == null || !b.Enabled || b.OnClick == null) return;
            b.OnClick();
            if (open) Refresh();
        }

        // ------------------------------------------------------------------ what the buttons say
        static void SetLabel(MenuButton b, string text)
        {
            if (b != null && b.Label != null && b.Label.text != text) b.Label.text = text;
        }

        static string OnOff(string name, bool on)
        {
            return name + (on ? ": ON" : ": OFF");
        }

        void Refresh()
        {
            DisplayState s = app.State;
            ViewDirector v = rig.views;
            bool canCommand = app.CanCommand, canTime = app.CanControlTime;
            for (int i = 0; i < commandButtons.Count; i++) commandButtons[i].Enabled = canCommand;
            for (int i = 0; i < timeButtons.Count; i++) timeButtons[i].Enabled = canTime;
            if (commandNote != null && commandNote.gameObject.activeSelf == canCommand) commandNote.gameObject.SetActive(!canCommand);

            SetLabel(pauseButton, app.paused ? "PLAY" : "PAUSE");
            SetLabel(jamPanelButton, app.WingJammed ? "FREE THE PANEL" : "JAM A PANEL");
            bool antennaJammed = s.Valid && s.Antenna.Present && s.Antenna.State == DeployState.Jammed;
            SetLabel(jamAntennaButton, antennaJammed ? "FREE THE ANTENNA" : "JAM THE ANTENNA");
            SetLabel(camButton, OnOff("SAT CAMERA", v != null && v.SatCameraOn));
            SetLabel(earthButton, OnOff("WHOLE EARTH", v != null && v.Overview));
            SetLabel(pathButton, OnOff("ORBIT PATH", v != null && v.PathVisible));
            SetLabel(panelLineButton, OnOff("PANEL LINE", v != null && v.PanelAxesVisible));
            SetLabel(axesButton, OnOff("BODY AXES", app.satelliteView != null && app.satelliteView.AxesVisible));
            SetLabel(cameraLineButton, OnOff("CAMERA LINE", app.satelliteView != null && app.satelliteView.BoresightVisible));

            // the buttons that match the computer's other keys
            bool camOn = v != null && v.SatCameraOn;
            if (zoomInButton != null) zoomInButton.Enabled = camOn;
            if (zoomOutButton != null) zoomOutButton.Enabled = camOn;
            bool earthBelow = app.space == null || app.space.viewFrame == SpaceScene.ViewFrame.EarthBelow;
            SetLabel(frameButton, earthBelow ? "VIEW: EARTH BELOW" : "VIEW: STARS FIXED");
            SetLabel(padButton, OnOff("INFO PAD", rig.infoPanel != null && rig.infoPanel.showInHeadset));
            string source = app.ActiveFeed == FeedKind.Replay ? "REPLAY" : app.ActiveFeed == FeedKind.Tcp ? "LIVE LINK" : "SANDBOX";
            SetLabel(sourceButton, "SOURCE: " + source);
            if (speedMarker != null && speedButtons.Count > 0)
            {
                int at = Mathf.Clamp(app.speedIndex, 0, speedButtons.Count - 1);
                Vector3 mp = speedMarker.localPosition;
                mp.x = speedButtons[at].Root.localPosition.x;
                speedMarker.localPosition = mp;
                bool show = canTime && !app.paused;
                if (speedMarker.gameObject.activeSelf != show) speedMarker.gameObject.SetActive(show);
            }

            if (footer != null)
            {
                string time = s.Valid ? AstroTime.ToIso(s.SimTime).Replace('T', ' ').Replace("Z", " UTC") : "waiting for data";
                string speed = !canTime ? "" : (app.paused ? "PAUSED" : "x" + app.Speed.ToString("0.##", Inv));
                string mode;
                if (app.ActiveFeed == FeedKind.Sandbox && app.Sandbox != null) mode = app.Sandbox.ModeName(app.SandboxTime);
                else mode = app.ActiveFeed == FeedKind.Replay ? "REPLAY" : "LIVE LINK";
                string text = time + "      " + speed + "      " + mode;
                if (footer.text != text) footer.text = text;
            }
        }

        // ------------------------------------------------------------------ messages
        void UpdateStrip(float now, float dt)
        {
            string text = null;
            if (!everOpened && now < reminderUntil)
                text = "Press the <color=" + HudStyle.AccentHex + ">MENU</color> button on your left controller\nto see the controls";
            if (showMessages && !string.IsNullOrEmpty(app.LastMessage) && now - app.LastMessageTime < 4.5f)
            {
                if (!ReferenceEquals(app.LastMessage, lastMessage))
                {
                    lastMessage = app.LastMessage;
                    lastMessageWrapped = Wrap(lastMessage, 46);
                }
                text = lastMessageWrapped;
            }
            if (!string.IsNullOrEmpty(flightLine))
            {
                string line = "<color=" + HudStyle.AccentHex + ">" + flightLine + "</color>";
                text = string.IsNullOrEmpty(text) ? line : text + "\n" + line;
            }

            bool show = !string.IsNullOrEmpty(text) && rig.head != null;
            if (strip.gameObject.activeSelf != show)
            {
                strip.gameObject.SetActive(show);
                stripPlaced = false;
            }
            if (!show) return;

            if (text != stripShown)
            {
                stripShown = text;
                stripText.text = text;
                int lines = 1, longest = 0, run = 0;
                bool inTag = false;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '\n')
                    {
                        lines++;
                        longest = Mathf.Max(longest, run);
                        run = 0;
                    }
                    else if (c == '<') inTag = true;
                    else if (c == '>') inTag = false;
                    else if (!inTag) run++;
                }
                longest = Mathf.Max(longest, run);
                float w = Mathf.Clamp(longest * 0.0152f + 0.09f, 0.3f, 1.2f);
                float h = lines * 0.028f + 0.05f;
                stripBack.transform.localScale = new Vector3(w, h, 1f);
                stripBack.sharedMaterial.SetFloat("_Aspect", w / h);
            }

            // low in front of you, following your head gently; under the menu while it is open
            Transform head = rig.head;
            Vector3 targetPos;
            Quaternion targetRot;
            if (open)
            {
                targetPos = root.position - root.up * (PanelSize.y * 0.5f * root.localScale.y + 0.075f) - root.forward * 0.03f;
                targetRot = root.rotation;
            }
            else
            {
                Vector3 fwd = head.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = rig.transform.forward;
                fwd.Normalize();
                targetPos = head.position + fwd * 1.15f + Vector3.down * 0.42f;
                targetRot = Quaternion.LookRotation(targetPos - head.position, Vector3.up);
            }
            float k = stripPlaced ? 1f - Mathf.Exp(-6f * dt) : 1f;
            stripPlaced = true;
            strip.SetPositionAndRotation(Vector3.Lerp(strip.position, targetPos, k), Quaternion.Slerp(strip.rotation, targetRot, k));
        }

        /// <summary>Break a sentence into lines of at most 'max' letters, at the spaces.</summary>
        static string Wrap(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            var sb = new StringBuilder(s.Length + 8);
            int run = 0;
            string[] words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (run > 0 && run + 1 + w.Length > max)
                {
                    sb.Append('\n');
                    run = 0;
                }
                else if (run > 0)
                {
                    sb.Append(' ');
                    run++;
                }
                sb.Append(w);
                run += w.Length;
            }
            return sb.ToString();
        }
    }
}
