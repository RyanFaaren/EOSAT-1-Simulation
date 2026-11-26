// EOSAT-1 VR - the computer screen's controls, made for someone using the simulation for
// the first time:
//
//   Control bar (bottom)  every action as a labelled button, grouped (time, solar panels and
//                         antenna, pointing, faults), with the key that does the same shown
//                         on the button. Buttons light up to show the current state.
//                         Tab hides or shows the bar.
//   Tooltips              rest the mouse on any button (here or in the Views panel) and a
//                         sentence says what it does.
//   Help card (F1)        all the keys on one card. It opens by itself the first time the
//                         simulation is run on a computer.
//   Welcome line          for the first seconds: how to look around.
//
// EosatApp adds this component by itself at start, so an existing scene needs no rebuild.
// While someone wears the headset all of this stays on the laptop's screen, over the picture
// the headset sees, so a second person can run the simulation for them with the mouse and
// keys (the headset has its own menu, VrMenu). Only the camera is the headset's.
using System;
using System.Collections.Generic;
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public sealed class DesktopHud : MonoBehaviour
    {
        public EosatApp app;
        public InfoPanel infoPanel;
        [Tooltip("Open the help card by itself the first time the simulation is run on this computer.")]
        public bool helpOnFirstRun = true;
        [Tooltip("Seconds the 'how to look around' line stays on screen after the start.")]
        public float welcomeSeconds = 25f;

        /// <summary>The frame in which Esc was used to close the help card (so it does not also leave to the menu).</summary>
        public static int EscapeFrame = -100;
        /// <summary>Screen y (from the top) just above the control bar, or above the "Tab" hint when the bar is hidden; -1 before the first draw. Other panels sit above it.</summary>
        public static float TopOfControls = -1f;

        public bool HelpOpen { get { return helpOpen; } }

        const string SeenKey = "eosat1.hud.helpSeen";
        const float Margin = 12f;

        sealed class Btn
        {
            public string Label = "", AltLabel, Key = "", Tip = "";
            public Action Click;
            public Func<bool> On;
            public Func<string> Text;
            public Rect R;
        }

        sealed class Section
        {
            public string Title = "";
            public string Note;
            public Func<bool> Visible;
            public Func<string> Extra;
            public readonly List<Btn> Buttons = new List<Btn>();
            public Rect R;
            public float ColW, W;
            public int Cols;
        }

        readonly List<Section> sections = new List<Section>();
        readonly List<Section> shown = new List<Section>();
        readonly List<int> lineStart = new List<int>();
        readonly List<float> lineWidth = new List<float>();
        bool helpOpen, usedBar, built, headsetShown;
        float startTime;
        // the bar is laid out at full size and then shrunk about its bottom centre to fit the window
        float scale = 1f, barTopOnScreen;
        Vector2 pivot;
        float pad, buttonH, gap, headH, sectionGap;
        int pointing;
        float nextState;

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ setting up
        void Start()
        {
            if (app == null) app = GetComponent<EosatApp>();
            if (app == null) app = FindAnyObjectByType<EosatApp>();
            if (app == null)
            {
                enabled = false;
                return;
            }
            if (infoPanel == null) infoPanel = app.infoPanel;
            BuildSections();
            built = true;
            startTime = Time.unscaledTime;
            if (helpOnFirstRun && !UnityEngine.XR.XRSettings.isDeviceActive && PlayerPrefs.GetInt(SeenKey, 0) == 0)
            {
                helpOpen = true;
                PlayerPrefs.SetInt(SeenKey, 1);
                PlayerPrefs.Save();
            }
        }

        bool BarVisible { get { return infoPanel == null || infoPanel.showHelp; } }

        Section AddSection(string title, Func<bool> visible)
        {
            var s = new Section();
            s.Title = title;
            s.Visible = visible;
            sections.Add(s);
            return s;
        }

        static Btn Add(Section s, string label, string key, string tip, Action click)
        {
            var b = new Btn();
            b.Label = label;
            b.Key = key;
            b.Tip = tip;
            b.Click = click;
            s.Buttons.Add(b);
            return b;
        }

        void BuildSections()
        {
            Func<bool> canTime = delegate { return app.CanControlTime; };
            Func<bool> canCommand = delegate { return app.CanCommand; };

            // ---- time
            Section time = AddSection("TIME", canTime);
            time.Extra = delegate { return app.paused ? "PAUSED" : "x" + app.Speed.ToString("0.##", Inv); };
            Btn pause = Add(time, "Pause", "Space", "Stop the clock, or start it again. You can still look around while it is paused.",
                            delegate { app.TogglePause(); });
            pause.AltLabel = "Play";
            pause.Text = delegate { return app.paused ? "Play" : "Pause"; };
            pause.On = delegate { return app.paused; };
            Add(time, "Slower", "-", "Make time pass more slowly (down to half speed).", delegate { app.SlowDown(); });
            Add(time, "Faster", "=", "Make time pass faster (up to 300 times). One trip round the Earth takes about 95 minutes, so speed it up to see an eclipse.",
                delegate { app.SpeedUp(); });
            Add(time, "Start again", "Bksp", "Go back to the beginning: the moment the satellite leaves the rocket, tumbling.",
                delegate { app.Restart(); });
            Add(time, "Back 1 min", "←", "Jump one minute back in time.", delegate { app.JumpSeconds(-60); });
            Add(time, "Skip 1 min", "→", "Jump one minute forward in time.", delegate { app.JumpSeconds(60); });

            // ---- solar panels and antenna
            Section deploy = AddSection("SOLAR PANELS & ANTENNA", canCommand);
            Btn p1 = Add(deploy, "Open panel 1", "1", "Unfold solar panel 1 (the one on the -Y side). It takes about 30 seconds.",
                         delegate { app.Command(SandboxCommand.DeployWingMinusY); });
            p1.On = delegate { return IsOut(app.State.WingMinusY); };
            Btn p2 = Add(deploy, "Open panel 2", "2", "Unfold solar panel 2 (the one on the +Y side). It takes about 30 seconds.",
                         delegate { app.Command(SandboxCommand.DeployWingPlusY); });
            p2.On = delegate { return IsOut(app.State.WingPlusY); };
            Btn ant = Add(deploy, "Open antenna", "3", "Unfold the radio antenna on top of the satellite.",
                          delegate { app.Command(SandboxCommand.DeployAntenna); });
            ant.On = delegate { return IsOut(app.State.Antenna); };
            Add(deploy, "Fold panels", "6", "Fold both solar panels back against the body.", delegate { app.Command(SandboxCommand.StowWings); });
            Add(deploy, "Fold antenna", "7", "Fold the antenna back down.", delegate { app.Command(SandboxCommand.StowAntenna); });

            // ---- pointing
            Section point = AddSection("POINT THE SATELLITE", canCommand);
            Btn earth = Add(point, "Camera at Earth", "N", "Turn the satellite so its cameras look straight down at the Earth.",
                            delegate { app.Command(SandboxCommand.AttitudeNadir); });
            earth.On = delegate { return pointing == 1; };
            Btn sun = Add(point, "Panels at Sun", "S", "Turn the satellite so the open solar panels face the Sun, for the most power.",
                          delegate { app.Command(SandboxCommand.AttitudeSun); });
            sun.On = delegate { return pointing == 2; };
            Btn roll = Add(point, "Roll to image", "R", "Lean the satellite 25 degrees to one side, as it does to photograph a place that is not right below it.",
                           delegate { app.Command(SandboxCommand.AttitudeRollImaging); });
            roll.On = delegate { return pointing == 3; };
            Btn tumble = Add(point, "Tumble", "T", "Let the satellite spin freely, as it does just after leaving the rocket.",
                             delegate { app.Command(SandboxCommand.AttitudeTumble); });
            tumble.On = delegate { return pointing == 4; };

            // ---- faults
            Section fault = AddSection("TEST A FAULT", canCommand);
            Btn jamPanel = Add(fault, "Jam a panel", "5", "Pretend a solar panel gets stuck part-way the next time it unfolds. Click again to free it.",
                               delegate { app.Command(SandboxCommand.ToggleWingJam); });
            jamPanel.AltLabel = "Free the panel";
            jamPanel.Text = delegate { return app.WingJammed ? "Free the panel" : "Jam a panel"; };
            jamPanel.On = delegate { return app.WingJammed; };
            Btn jamAnt = Add(fault, "Jam the antenna", "4", "Pretend the antenna gets stuck. Click again to free it.",
                             delegate { app.Command(SandboxCommand.ToggleAntennaJam); });
            jamAnt.AltLabel = "Free the antenna";
            jamAnt.Text = delegate { return AntennaJammed ? "Free the antenna" : "Jam the antenna"; };
            jamAnt.On = delegate { return AntennaJammed; };

            // ---- when the satellite is not ours to command
            Section note = AddSection("SATELLITE", delegate { return !app.CanCommand; });
            note.Note = "In this mode the satellite is commanded\nfrom mission control. Press M to switch\nto Sandbox and command it yourself.";

            // ---- help
            Section more = AddSection("HELP", null);
            Add(more, "All controls", "F1", "Open the help card: every key and what it does.", delegate { ToggleHelp(); });
            Add(more, "Main menu", "Esc", "Leave the simulation and go back to the mission control room.", delegate { LeaveToMenu(); });
        }

        static bool IsOut(DeployInfo d)
        {
            return d.Present && (d.State == DeployState.Deployed || d.State == DeployState.Deploying);
        }

        bool AntennaJammed
        {
            get { return app.State.Valid && app.State.Antenna.Present && app.State.Antenna.State == DeployState.Jammed; }
        }

        public void ToggleHelp()
        {
            helpOpen = !helpOpen;
        }

        void LeaveToMenu()
        {
            if (Application.CanStreamedLevelBeLoaded(MenuSettings.MenuSceneName))
                UnityEngine.SceneManagement.SceneManager.LoadScene(MenuSettings.MenuSceneName);
            else
                app.Say("The main menu scene is not in the build list (EOSAT-1 > Rebuild Menu Scene adds it)");
        }

        /// <summary>Which pointing button to light: 0 none, 1 Earth, 2 Sun, 3 roll, 4 tumble.</summary>
        int ReadPointing()
        {
            if (app.ActiveFeed != FeedKind.Sandbox || app.Sandbox == null) return 0;
            string m = app.Sandbox.ModeName(app.SandboxTime);
            if (string.IsNullOrEmpty(m)) return 0;
            if (m.StartsWith("DETUMBLING", StringComparison.Ordinal)) return 0;
            if (m.StartsWith("TUMBLING", StringComparison.Ordinal)) return 4;
            if (m.StartsWith("SUN", StringComparison.Ordinal) || m.StartsWith("SLEWING", StringComparison.Ordinal)) return 2;
            if (m.StartsWith("ROLLING", StringComparison.Ordinal) || m.StartsWith("IMAGING", StringComparison.Ordinal)) return 3;
            return 1;
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!built) return;
            // the welcome line starts over when the headset goes on or comes off: it says different things
            bool headset = UnityEngine.XR.XRSettings.isDeviceActive;
            if (headset != headsetShown)
            {
                headsetShown = headset;
                startTime = Time.unscaledTime;
            }

            if (InputShim.Down(KeyCode.F1)) ToggleHelp();
            if (helpOpen)
            {
                HudStyle.MarkModal();
                if (InputShim.Down(KeyCode.Escape))
                {
                    helpOpen = false;
                    EscapeFrame = Time.frameCount;
                }
                else if (InputShim.MouseDown(0)) helpOpen = false;
                return;
            }
            if (HudStyle.ModalActive) return;   // the click that closed the card is not for a button

            if (InputShim.Down(KeyCode.Tab) && infoPanel != null)
            {
                infoPanel.showHelp = !infoPanel.showHelp;
                app.Say(infoPanel.showHelp ? "Control bar shown" : "Control bar hidden (Tab brings it back)");
            }

            if (Time.unscaledTime >= nextState)
            {
                nextState = Time.unscaledTime + 0.25f;
                pointing = ReadPointing();
            }

            // the rectangles are the ones the last drawn frame laid out
            if (BarVisible && InputShim.MouseDown(0))
            {
                Vector2 m = ToBar(HudStyle.Mouse());
                for (int i = 0; i < shown.Count; i++)
                {
                    List<Btn> list = shown[i].Buttons;
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (!list[k].R.Contains(m)) continue;
                        usedBar = true;
                        if (list[k].Click != null) list[k].Click();
                        return;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ layout
        Vector2 ToBar(Vector2 screen)
        {
            return pivot + (screen - pivot) / Mathf.Max(0.01f, scale);
        }

        Rect ToScreen(Rect r)
        {
            return new Rect(pivot.x + (r.x - pivot.x) * scale, pivot.y + (r.y - pivot.y) * scale, r.width * scale, r.height * scale);
        }

        void Measure(Section s)
        {
            float titleW = HudStyle.Header.CalcSize(new GUIContent(s.Title)).x;
            if (s.Extra != null) titleW += HudStyle.Unit * 5f;
            if (s.Buttons.Count == 0)
            {
                float noteW = string.IsNullOrEmpty(s.Note) ? 0f : HudStyle.SmallDim.CalcSize(new GUIContent(s.Note)).x;
                s.Cols = 0;
                s.ColW = 0f;
                s.W = Mathf.Ceil(Mathf.Max(noteW, titleW) + pad * 2f);
                return;
            }
            s.Cols = (s.Buttons.Count + 1) / 2;
            float w = 0f;
            for (int i = 0; i < s.Buttons.Count; i++)
            {
                Btn b = s.Buttons[i];
                w = Mathf.Max(w, HudStyle.ButtonWidth(b.Label, b.Key));
                if (b.AltLabel != null) w = Mathf.Max(w, HudStyle.ButtonWidth(b.AltLabel, b.Key));
            }
            float need = (titleW - (s.Cols - 1) * gap) / s.Cols;   // wide enough for the heading too
            s.ColW = Mathf.Ceil(Mathf.Max(w, need));
            s.W = s.Cols * s.ColW + (s.Cols - 1) * gap + pad * 2f;
        }

        void Layout()
        {
            HudStyle.Ensure();
            float u = HudStyle.Unit;
            pad = Mathf.Round(u * 0.6f);
            buttonH = Mathf.Round(u * 2f);
            gap = Mathf.Round(u * 0.35f);
            headH = Mathf.Round(u * 1.55f);
            sectionGap = Mathf.Round(u * 0.5f);

            shown.Clear();
            float natural = 0f;
            for (int i = 0; i < sections.Count; i++)
            {
                Section s = sections[i];
                if (s.Visible != null && !s.Visible()) continue;
                Measure(s);
                natural += s.W + (shown.Count > 0 ? sectionGap : 0f);
                shown.Add(s);
            }

            // one row, shrunk a little if need be; a narrow window gets two rows instead
            float avail = Mathf.Max(100f, Screen.width - Margin * 2f);
            float limit = natural * 0.72f <= avail ? Mathf.Max(natural, avail) : avail / 0.85f;
            lineStart.Clear();
            lineWidth.Clear();
            float x = 0f;
            for (int i = 0; i < shown.Count; i++)
            {
                float w = shown[i].W;
                if (lineStart.Count == 0 || x + sectionGap + w > limit + 0.5f)
                {
                    lineStart.Add(i);
                    lineWidth.Add(w);
                    x = w;
                }
                else
                {
                    x += sectionGap + w;
                    lineWidth[lineWidth.Count - 1] = x;
                }
            }
            float widest = 1f;
            for (int i = 0; i < lineWidth.Count; i++) widest = Mathf.Max(widest, lineWidth[i]);
            scale = Mathf.Min(1f, avail / widest);

            float lineH = pad * 2f + headH + buttonH * 2f + gap;
            float totalH = lineStart.Count * lineH + Mathf.Max(0, lineStart.Count - 1) * sectionGap;
            pivot = new Vector2(Screen.width * 0.5f, Screen.height - Margin);
            barTopOnScreen = pivot.y - totalH * scale;

            float y = pivot.y - totalH;
            for (int line = 0; line < lineStart.Count; line++)
            {
                int from = lineStart[line];
                int to = line + 1 < lineStart.Count ? lineStart[line + 1] : shown.Count;
                float sx = Mathf.Round(pivot.x - lineWidth[line] * 0.5f);
                for (int i = from; i < to; i++)
                {
                    Section s = shown[i];
                    s.R = new Rect(sx, y, s.W, lineH);
                    for (int k = 0; k < s.Buttons.Count; k++)
                    {
                        int row = k / s.Cols, col = k % s.Cols;
                        s.Buttons[k].R = new Rect(sx + pad + col * (s.ColW + gap), y + pad + headH + row * (buttonH + gap), s.ColW, buttonH);
                    }
                    sx += s.W + sectionGap;
                }
                y += lineH + sectionGap;
            }
        }

        // ------------------------------------------------------------------ drawing
        void OnGUI()
        {
            if (!built) return;   // on the laptop's screen with or without a headset in use
            if (Event.current.type != EventType.Repaint) return;
            GUI.depth = -20;   // in front of the other panels: the help card covers them
            HudStyle.Ensure();
            float u = HudStyle.Unit;
            Vector2 mouse = HudStyle.Mouse();
            float above = Screen.height - Margin;

            if (BarVisible)
            {
                Layout();
                Matrix4x4 old = GUI.matrix;
                GUI.matrix = Matrix4x4.TRS(new Vector3(pivot.x * (1f - scale), pivot.y * (1f - scale), 0f), Quaternion.identity,
                                           new Vector3(scale, scale, 1f));
                Vector2 m = helpOpen ? new Vector2(-1e6f, -1e6f) : ToBar(mouse);
                for (int i = 0; i < shown.Count; i++) DrawSection(shown[i], m, u);
                GUI.matrix = old;
                above = barTopOnScreen - Mathf.Round(u * 0.5f);
            }
            else
            {
                var c = new GUIContent("<b>Tab</b>  show the controls        <b>F1</b>  help");
                Vector2 cs = HudStyle.Small.CalcSize(c);
                float cp = Mathf.Round(u * 0.6f);
                var r = new Rect(Margin, Screen.height - Margin - cs.y - cp, cs.x + cp * 2f, cs.y + cp);
                HudStyle.DrawPanel(r);
                GUI.Label(new Rect(r.x + cp, r.y + cp * 0.5f, cs.x, cs.y), c, HudStyle.Small);
                HudStyle.Block(r);
                above = r.y - Mathf.Round(u * 0.5f);
            }

            TopOfControls = above;

            if (helpOpen)
            {
                DrawHelp(u);
                return;
            }
            Rect anchor;
            string tip;
            if (HudStyle.TryGetTip(out anchor, out tip)) DrawTip(u, anchor, tip);
            else DrawWelcome(u, above);
        }

        void DrawSection(Section s, Vector2 mouse, float u)
        {
            HudStyle.DrawPanel(s.R);
            HudStyle.Block(ToScreen(s.R));
            float x = s.R.x + pad, y = s.R.y + pad;
            GUI.Label(new Rect(x, y, s.R.width - pad * 2f, headH), s.Title, HudStyle.Header);
            if (s.Extra != null)
            {
                var ec = new GUIContent(s.Extra());
                Vector2 es = HudStyle.Chip.CalcSize(ec);
                GUI.Box(new Rect(s.R.xMax - pad - es.x, y - 2f, es.x, Mathf.Round(u * 1.3f)), ec, HudStyle.Chip);
            }
            if (!string.IsNullOrEmpty(s.Note))
                GUI.Label(new Rect(x, y + headH, s.R.width - pad * 2f, s.R.height - headH - pad * 2f), s.Note, HudStyle.SmallDim);
            for (int i = 0; i < s.Buttons.Count; i++)
            {
                Btn b = s.Buttons[i];
                bool hover = b.R.Contains(mouse);
                string label = b.Text != null ? b.Text() : b.Label;
                HudStyle.DrawButton(b.R, label, b.Key, b.On != null && b.On(), hover);
                if (hover) HudStyle.SetTip(ToScreen(b.R), b.Tip);
            }
        }

        /// <summary>The sentence for the button under the mouse, next to that button.</summary>
        static void DrawTip(float u, Rect anchor, string text)
        {
            var c = new GUIContent(text);
            float tp = Mathf.Round(u * 0.7f);
            float w = Mathf.Min(u * 24f, Screen.width - Margin * 2f);
            w = Mathf.Min(w, HudStyle.Wrap.CalcSize(c).x + tp * 2f + 2f);
            float h = HudStyle.Wrap.CalcHeight(c, w - tp * 2f) + tp * 2f;
            var r = new Rect(anchor.center.x - w * 0.5f, 0f, w, h);
            float space = Mathf.Round(u * 0.4f);
            if (anchor.y > Screen.height * 0.5f) r.y = anchor.y - h - space;            // bottom bar: above the button
            else if (anchor.x > Screen.width * 0.6f)                                    // Views panel: to its left
            {
                r.x = anchor.x - w - space * 2f;
                r.y = anchor.y;
            }
            else r.y = anchor.yMax + space;
            r.x = Mathf.Clamp(r.x, Margin, Mathf.Max(Margin, Screen.width - Margin - w));
            r.y = Mathf.Clamp(r.y, Margin, Mathf.Max(Margin, Screen.height - Margin - h));
            GUI.Box(r, GUIContent.none, HudStyle.Toast);
            GUI.Label(new Rect(r.x + tp, r.y + tp, w - tp * 2f, h - tp * 2f), c, HudStyle.Wrap);
        }

        /// <summary>For the first seconds: how to look around, just above the bar.</summary>
        void DrawWelcome(float u, float bottom)
        {
            float age = Time.unscaledTime - startTime;
            if (usedBar || age > welcomeSeconds) return;
            var c = new GUIContent(headsetShown
                ? "<b>Headset in use:</b> this screen shows what they see        <b>Click a button</b> to do it for them        <b>F1</b> shows every control"
                : "<b>Drag the mouse</b> to look around        <b>Scroll</b> to zoom        <b>Click a button</b> to try it        <b>F1</b> shows every control");
            Vector2 s = HudStyle.Toast.CalcSize(c);
            float w = Mathf.Min(s.x, Screen.width - Margin * 2f);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(welcomeSeconds - age));
            GUI.Box(new Rect((Screen.width - w) * 0.5f, bottom - s.y, w, s.y), c, HudStyle.Toast);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ the help card
        // title, column (0-2), then key / what it does pairs
        static readonly string[][] Help =
        {
            new[] { "LOOK AROUND", "0",
                    "Drag", "look around the satellite",
                    "Scroll", "zoom in and out",
                    "V", "next camera angle",
                    "F", "back to the first angle",
                    "Z", "see the whole Earth",
                    "C", "the satellite's own camera" },
            new[] { "TIME", "0",
                    "Space", "pause / play",
                    "=    -", "faster / slower",
                    "→    ←", "skip / go back 1 minute",
                    "Backspace", "start again" },
            new[] { "SATELLITE  (Sandbox mode)", "1",
                    "1    2", "open solar panel 1 / 2",
                    "3", "open the antenna",
                    "6", "fold the solar panels",
                    "7", "fold the antenna",
                    "N", "point the camera at Earth",
                    "S", "point the panels at the Sun",
                    "R", "roll 25 degrees to image",
                    "T", "tumble",
                    "5", "jam a panel (test a fault)",
                    "4", "jam the antenna (test a fault)" },
            new[] { "SHOW AND HIDE", "2",
                    "O", "orbit path",
                    "P", "solar panel direction line",
                    "G", "body axes (X, Y, Z arrows)",
                    "B", "camera line and footprint",
                    "L", "view: Earth below / fixed stars",
                    "M", "data source",
                    "H", "status panel",
                    "Tab", "control bar",
                    "F1", "this help",
                    "Esc", "main menu" },
        };

        void DrawHelp(float u)
        {
            HudStyle.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.72f));

            float cardPad = Mathf.Round(u * 1.6f);
            float keyW = Mathf.Round(u * 6.4f), textW = Mathf.Round(u * 16f), colGap = Mathf.Round(u * 1.6f);
            float rowH = Mathf.Round(u * 1.5f), groupHead = Mathf.Round(u * 2.1f), groupGap = Mathf.Round(u * 0.9f);
            float titleH = Mathf.Round(u * 2.4f), subH = Mathf.Round(u * 2.2f), footH = Mathf.Round(u * 2.4f);

            // how tall is each column?
            float[] colH = new float[3];
            for (int g = 0; g < Help.Length; g++)
            {
                int col = Help[g][1][0] - '0';
                if (colH[col] > 0f) colH[col] += groupGap;
                colH[col] += groupHead + (Help[g].Length - 2) / 2 * rowH;
            }
            float bodyH = Mathf.Max(colH[0], Mathf.Max(colH[1], colH[2]));
            float w = cardPad * 2f + 3f * (keyW + textW) + 2f * colGap;
            float h = cardPad * 2f + titleH + subH + bodyH + footH;

            // shrink the whole card if the window is small
            float s = Mathf.Min(1f, Mathf.Min((Screen.width - 24f) / w, (Screen.height - 24f) / h));
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(centre.x * (1f - s), centre.y * (1f - s), 0f), Quaternion.identity, new Vector3(s, s, 1f));

            var card = new Rect(centre.x - w * 0.5f, centre.y - h * 0.5f, w, h);
            HudStyle.DrawPanel(card);
            float x0 = card.x + cardPad, y0 = card.y + cardPad;
            GUI.Label(new Rect(x0, y0, w - cardPad * 2f, titleH), HudStyle.Spaced("HOW TO USE THE SIMULATION"), HudStyle.Title);
            var sub = new GUIContent(headsetShown
                ? "Someone is in the headset: this screen shows what they see, and every button and key works for them too (the camera is theirs)."
                : "You are next to EOS SAT-1, life size, 500 km above the Earth. Every button on screen does the same as its key.");
            var subStyle = new GUIStyle(HudStyle.Dim);
            subStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x0, y0 + titleH, w - cardPad * 2f, subH), sub, subStyle);
            HudStyle.Fill(new Rect(x0, y0 + titleH + subH - Mathf.Round(u * 0.3f), w - cardPad * 2f, 1f), HudStyle.WithAlpha(HudStyle.Accent, 0.35f));

            float[] colY = new float[3];
            float top = y0 + titleH + subH + Mathf.Round(u * 0.4f);
            for (int g = 0; g < Help.Length; g++)
            {
                string[] rows = Help[g];
                int col = rows[1][0] - '0';
                float x = x0 + col * (keyW + textW + colGap);
                if (colY[col] > 0f) colY[col] += groupGap;
                float y = top + colY[col];
                GUI.Label(new Rect(x, y, keyW + textW, groupHead), rows[0], HudStyle.Header);
                y += groupHead;
                for (int i = 2; i + 1 < rows.Length; i += 2)
                {
                    GUI.Label(new Rect(x, y, keyW, rowH), "<b><color=" + HudStyle.AccentHex + ">" + rows[i] + "</color></b>", HudStyle.Text);
                    GUI.Label(new Rect(x + keyW, y, textW, rowH), rows[i + 1], HudStyle.Text);
                    y += rowH;
                }
                colY[col] = y - top;
            }

            var foot = new GUIContent("Click anywhere, or press <b>F1</b> or <b>Esc</b>, to close.   Rest the mouse on any button to see what it does.");
            var footStyle = new GUIStyle(HudStyle.Dim);
            footStyle.alignment = TextAnchor.LowerCenter;
            GUI.Label(new Rect(x0, card.yMax - cardPad - footH, w - cardPad * 2f, footH), foot, footStyle);
            GUI.matrix = old;
        }
    }
}
