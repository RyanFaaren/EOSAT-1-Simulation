// EOSAT-1 VR - a small panel of text that answers the four questions from the course
// slides at a glance: Is it tumbling? Is the camera facing Earth? Are the wings in the
// sun? Did the antenna come out? Plus where it is, the time, and the link health.
// Desktop: drawn on screen (H toggles). Headset: the info pad that pops out of the left
// wrist when you look at it like a watch (VrWristPad draws it from the data below; X toggles).
// The computer's panel stays on the laptop screen while someone wears the headset, and the
// two are switched separately, so the person at the laptop and the one in the headset each
// choose for themselves.
// The buttons and the key list are DesktopHud's (desktop) and VrMenu's (headset).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public sealed class InfoPanel : MonoBehaviour
    {
        public EosatApp app;
        [Tooltip("The old headset panel on the left controller. Hidden while the wrist info pad (VrWristPad) is in use.")]
        public TextMesh worldText;
        public GameObject worldPanel;
        /// <summary>Set by VrWristPad: the info pad on the wrist replaces the panel on the left controller.</summary>
        [NonSerialized] public bool wristPadTakesOver;
        [Tooltip("The status panel on the computer's screen (H toggles), with or without a headset in use.")]
        public bool showOnScreen = true;
        [Tooltip("Headset: the info pad on the left wrist can pop out (X toggles).")]
        public bool showInHeadset = true;
        [Tooltip("Desktop: show the control bar along the bottom of the screen (DesktopHud draws it; Tab toggles).")]
        public bool showHelp = true;
        public float refreshInterval = 0.25f;

        public bool Visible { get; private set; }

        readonly StringBuilder sb = new StringBuilder(1024);
        string text = "";
        float nextRefresh;
        readonly List<string> rowLabels = new List<string>();
        readonly List<string> rowValues = new List<string>();
        readonly List<bool> rowQuestion = new List<bool>();
        string rowLabel = "";
        bool rowIsQuestion;
        int rowStart = -1;
        string headerTime = "", headerChip = "", sourceShort = "";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
            if (worldText != null && f == worldText.font)
            {
                var r = worldText.GetComponent<MeshRenderer>();
                if (r != null && r.sharedMaterial != null) r.sharedMaterial.mainTexture = f.material.mainTexture;
            }
        }

        /// <summary>The computer's status panel on / off (H, or the desktop's buttons).</summary>
        public void Toggle()
        {
            showOnScreen = !showOnScreen;
        }

        /// <summary>The headset's info pad on / off (X on the left controller).</summary>
        public void ToggleHeadset()
        {
            showInHeadset = !showInHeadset;
        }

        // set by Build: the weak-connection warning, if there is one
        string linkWarning, linkWarningShort;
        bool linkLost;

        // ------------------------------------------------------------------ for the headset's info pad (VrWristPad)
        public enum AnswerLevel { Unknown, Good, Warn, Bad }

        /// <summary>One of slide 17's four questions, answered in a word or two.</summary>
        public sealed class Answer
        {
            public readonly string Question;
            public string Value = "?";
            public string Detail = "";
            public AnswerLevel Level = AnswerLevel.Unknown;

            public Answer(string question)
            {
                Question = question;
            }
        }

        /// <summary>Tumbling? Camera on Earth? Panels in sun? Antenna out? (in that order), rebuilt with the panel.</summary>
        public readonly Answer[] Answers =
        {
            new Answer("TUMBLING?"), new Answer("CAMERA ON EARTH?"), new Answer("PANELS IN SUN?"), new Answer("ANTENNA OUT?"),
        };

        public string HeaderTime { get { return headerTime; } }
        public string HeaderChip { get { return headerChip; } }
        /// <summary>The data source in a few words: the Source row without the link statistics.</summary>
        public string SourceShort { get { return sourceShort; } }
        /// <summary>The weak-link warning in a few words, or null while the link is fine.</summary>
        public string LinkWarningShort { get { return linkWarningShort; } }
        public bool HasLinkWarning { get { return linkWarning != null; } }
        public bool LinkLost { get { return linkLost; } }
        public int RowCount { get { return rowLabels.Count; } }
        public string RowLabel(int i) { return rowLabels[i]; }
        public string RowValue(int i) { return rowValues[i]; }
        /// <summary>True for the four rows that the Answers above also cover.</summary>
        public bool RowIsQuestion(int i) { return rowQuestion[i]; }
        /// <summary>Goes up by one every time the text is rebuilt (four times a second).</summary>
        public int Version { get; private set; }

        static void SetAnswer(Answer a, AnswerLevel level, string value, string detail)
        {
            a.Level = level;
            a.Value = value;
            a.Detail = detail;
        }

        void Update()
        {
            bool headset = VrPreview.Headset;
            // controllers off (K): the panel stands beside the laptop drawn in the headset, as there's no hand to hold it
            bool beside = headset && PlaceBesideLaptop();
            // in the headset the panel on the left controller comes up by itself when the link is weak
            // (unless the wrist info pad has taken over: then the wrist screen blinks instead)
            bool wantPanel = headset && (beside || !wristPadTakesOver) && (showInHeadset || linkWarning != null);
            if (worldPanel != null && worldPanel.activeSelf != wantPanel) worldPanel.SetActive(wantPanel);
            Visible = headset ? showInHeadset : showOnScreen;
            if (Time.unscaledTime < nextRefresh || app == null) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Build(app);
            if (worldText != null && headset && (beside || !wristPadTakesOver)) worldText.text = text;
        }

        // where the panel hangs normally (the left controller), to put it back
        Transform homeParent;
        Vector3 homePosition;
        Quaternion homeRotation;
        bool homeKnown;

        /// <summary>
        /// With the headset controllers off and the laptop set (J), stand the panel beside the drawn
        /// laptop; otherwise put it back on the left controller. True while it is beside the laptop.
        /// </summary>
        bool PlaceBesideLaptop()
        {
            if (worldPanel == null) return false;
            Transform t = worldPanel.transform;
            if (!homeKnown)
            {
                homeParent = t.parent;
                homePosition = t.localPosition;
                homeRotation = t.localRotation;
                homeKnown = true;
            }
            Transform screen, side = null;
            bool beside = !VrPreview.ControllersInUse && LaptopMarker.Current != null && LaptopMarker.Current.TryGetAnchors(out screen, out side);
            Transform want = beside ? side : homeParent;
            if (t.parent != want)
            {
                t.SetParent(want, false);
                t.localPosition = beside ? Vector3.zero : homePosition;
                t.localRotation = beside ? Quaternion.identity : homeRotation;
            }
            return beside;
        }

        void OnGUI()
        {
            // drawn on the computer's screen even while the headset is in use (it never shows in the headset)
            if (app == null) return;
            if (Event.current.type != EventType.Repaint) return;
            HudStyle.Ensure();
            float u = HudStyle.Unit;
            float pad = Mathf.Round(u * 0.8f);
            float margin = 12f;

            if (showOnScreen) DrawStatus(u, pad, margin);
            DrawLinkWarning();   // the key list is gone: DesktopHud's control bar replaced it

            // the latest message, for a few seconds, fading out at the end
            float age = Time.unscaledTime - app.LastMessageTime;
            if (!string.IsNullOrEmpty(app.LastMessage) && age < 4f)
            {
                var mc = new GUIContent(app.LastMessage);
                Vector2 ms = HudStyle.Toast.CalcSize(mc);
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((4f - age) / 0.6f));
                GUI.Box(new Rect((Screen.width - ms.x) * 0.5f, Screen.height * 0.12f, ms.x, ms.y), mc, HudStyle.Toast);
                GUI.color = old;
            }
        }

        /// <summary>A banner across the top while the live link is weak or lost (shown even with the panel hidden).</summary>
        void DrawLinkWarning()
        {
            if (linkWarning == null) return;
            var content = new GUIContent(linkWarning);
            Vector2 size = HudStyle.Toast.CalcSize(content);
            float w = Mathf.Min(size.x, Screen.width - 24f);
            Color old = GUI.color;
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f);
            GUI.color = linkLost ? new Color(1f, 0.5f, 0.45f, pulse) : new Color(1f, 0.82f, 0.4f, pulse);
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.04f, w, size.y), content, HudStyle.Toast);
            GUI.color = old;
        }

        /// <summary>
        /// The client asked for graceful degradation: when Live link data stops, keep the picture
        /// moving by prediction and say so plainly. Null when all is well.
        /// </summary>
        void UpdateLinkWarning(EosatApp a, DisplayState s)
        {
            linkWarning = null;
            linkWarningShort = null;
            linkLost = false;
            if (a.ActiveFeed != FeedKind.Tcp || !s.Valid || s.DataAge < 2.5) return;
            TcpFeedClient l = a.Link;
            linkLost = l == null || !l.Connected;
            string age = s.DataAge < 90.0 ? s.DataAge.ToString("0", Inv) + " s" : (s.DataAge / 60.0).ToString("0", Inv) + " min";
            string head = linkLost ? "CONNECTION LOST" : "WEAK CONNECTION";
            string what = s.PredictionCapped ? "prediction stopped, holding the last picture"
                        : s.Predicting ? "showing a prediction from the last data"
                        : "waiting for data";
            linkWarning = head + "  -  no data for " + age + "  -  " + what;
            linkWarningShort = (linkLost ? "LOST" : "WEAK") + "  no data for " + age
                               + (s.PredictionCapped ? ", holding" : s.Predicting ? ", predicting" : "");
        }

        /// <summary>The status panel, top left: a heading, the time, then one row per question.</summary>
        void DrawStatus(float u, float pad, float margin)
        {
            float rowH = Mathf.Round(u * 1.5f);
            float gap = Mathf.Round(u * 1.1f);
            var title = new GUIContent(HudStyle.Spaced("EOSAT-1"));
            var time = new GUIContent(headerTime);
            var chip = new GUIContent(headerChip);
            Vector2 titleSize = HudStyle.Header.CalcSize(title);
            Vector2 timeSize = HudStyle.Dim.CalcSize(time);
            float chipW = string.IsNullOrEmpty(headerChip) ? 0f : HudStyle.Chip.CalcSize(chip).x + gap * 0.6f;

            float labelW = 0f, valueW = 0f;
            for (int i = 0; i < rowLabels.Count; i++)
            {
                labelW = Mathf.Max(labelW, HudStyle.Dim.CalcSize(new GUIContent(rowLabels[i])).x);
                valueW = Mathf.Max(valueW, HudStyle.Text.CalcSize(new GUIContent(rowValues[i])).x);
            }
            float inner = Mathf.Max(labelW + gap + valueW, titleSize.x + gap + timeSize.x + chipW);
            float headH = Mathf.Round(u * 1.9f);
            var panel = new Rect(margin, margin, inner + pad * 2f, pad * 2f + headH + rowLabels.Count * rowH);
            HudStyle.DrawPanel(panel);
            HudStyle.Block(panel);

            float x = panel.x + pad, y = panel.y + pad;
            GUI.Label(new Rect(x, y, titleSize.x, rowH), title, HudStyle.Header);
            float right = panel.xMax - pad;
            if (chipW > 0f)
            {
                float cw = chipW - gap * 0.6f;
                GUI.Box(new Rect(right - cw, y - 1f, cw, Mathf.Round(u * 1.35f)), chip, HudStyle.Chip);
                right -= chipW;
            }
            GUI.Label(new Rect(right - timeSize.x, y, timeSize.x, rowH), time, HudStyle.Dim);
            y += headH - Mathf.Round(u * 0.45f);
            HudStyle.Fill(new Rect(x, y, inner, 1f), HudStyle.WithAlpha(HudStyle.Accent, 0.35f));
            y += Mathf.Round(u * 0.45f);

            for (int i = 0; i < rowLabels.Count; i++)
            {
                GUI.Label(new Rect(x, y, labelW, rowH), rowLabels[i], HudStyle.Dim);
                GUI.Label(new Rect(x + labelW + gap, y, valueW, rowH), rowValues[i], HudStyle.Text);
                y += rowH;
            }
        }

        // rows for the on-screen panel are cut out of the same text the headset panel shows
        void BeginRow(string label, string paddedLabel, bool question = false)
        {
            EndRow();
            rowLabel = label;
            rowIsQuestion = question;
            sb.Append(paddedLabel);
            rowStart = sb.Length;
        }

        void EndRow()
        {
            if (rowStart < 0) return;
            rowLabels.Add(rowLabel);
            rowValues.Add(sb.ToString(rowStart, sb.Length - rowStart).TrimEnd('\n'));
            rowQuestion.Add(rowIsQuestion);
            rowStart = -1;
        }

        // ------------------------------------------------------------------ text
        void Build(EosatApp a)
        {
            DisplayState s = a.State;
            Version++;
            sb.Length = 0;
            rowLabels.Clear();
            rowValues.Clear();
            rowQuestion.Clear();
            rowStart = -1;
            sourceShort = "";
            for (int i = 0; i < Answers.Length; i++)
                SetAnswer(Answers[i], AnswerLevel.Unknown, "?", s.Valid ? "" : "waiting for data");
            headerTime = s.Valid ? AstroTime.ToIso(s.SimTime).Replace('T', ' ').Replace("Z", " UTC") : "waiting for data";
            if (a.CanControlTime) headerChip = a.paused ? "PAUSED" : "x" + a.Speed.ToString("0.##", Inv);
            else if (s.Valid && s.Frozen) headerChip = "FROZEN";
            else if (s.Valid && !double.IsNaN(s.Speed)) headerChip = s.Speed > 1.01 || s.Speed < 0.99 ? "sim x" + s.Speed.ToString("0.##", Inv) : "";
            else headerChip = s.Valid && s.SimRate > 1.5 ? "sim x" + s.SimRate.ToString("0", Inv) : "";
            sb.Append("<b>EOSAT-1</b>  ").Append(headerTime);
            if (headerChip == "PAUSED") sb.Append("   <color=#ffcc55>PAUSED</color>");
            else if (headerChip == "FROZEN") sb.Append("   <color=#88ccff>FROZEN by instructor</color>");
            else if (headerChip.Length > 0) sb.Append("   ").Append(headerChip);
            sb.Append('\n');
            UpdateLinkWarning(a, s);
            if (linkWarning != null) sb.Append(linkLost ? Bad(linkWarning) : Warn(linkWarning)).Append('\n');
            BeginRow("Source", "Source  ");
            AppendSource(a, s);
            if (!s.Valid)
            {
                EndRow();
                text = sb.ToString();
                return;
            }

            // Is it tumbling?
            double rate = s.BodyRateDegS;
            BeginRow("Tumbling?", "Tumbling?        ", true);
            if (double.IsNaN(rate)) sb.Append("? (rate unknown at this data speed)");
            else if (rate > 0.5) sb.Append(Bad("YES")).Append("  ").Append(rate.ToString("0.0", Inv)).Append(" deg/s");
            else sb.Append(Good("no")).Append("  ").Append(rate.ToString("0.00", Inv)).Append(" deg/s");
            sb.Append('\n');
            if (double.IsNaN(rate)) SetAnswer(Answers[0], AnswerLevel.Unknown, "?", "rate unknown");
            else if (rate > 0.5) SetAnswer(Answers[0], AnswerLevel.Bad, "YES", rate.ToString("0.0", Inv) + "\u00b0/s");
            else SetAnswer(Answers[0], AnswerLevel.Good, "NO", rate.ToString("0.00", Inv) + "\u00b0/s");

            // Is the camera facing Earth?
            Vec3d camEci = s.QBodyToEci.Rotate(Vec3d.UnitZ);
            double offNadir = Vec3d.Angle(camEci, -s.PosEciKm) * MathUtil.Rad2Deg;
            BeginRow("Camera on Earth?", "Camera on Earth? ", true);
            if (offNadir < 3.0) sb.Append(Good("YES"));
            else if (offNadir < 68.0) sb.Append(Warn("off-pointed"));
            else sb.Append(Bad("NO"));
            sb.Append("  ").Append(offNadir.ToString("0.0", Inv)).Append(" deg off nadir\n");
            string offText = offNadir.ToString("0.0", Inv) + "\u00b0 off nadir";
            if (offNadir < 3.0) SetAnswer(Answers[1], AnswerLevel.Good, "YES", offText);
            else if (offNadir < 68.0) SetAnswer(Answers[1], AnswerLevel.Warn, "OFF-POINTED", offText);
            else SetAnswer(Answers[1], AnswerLevel.Bad, "NO", offText);

            // Are the wings in the sun?
            Vec3d sun = s.HasSun ? s.SunDirEci : Vec3d.Zero;
            bool eclipse = a.space != null ? a.space.Illumination < 0.5 : s.InEclipse;
            BeginRow("Panels in sun?", "Panels in sun?   ", true);
            if (eclipse) sb.Append(Warn("ECLIPSE")).Append(" (Earth's shadow)  ");
            double litMy = AppendWing("-Y", s.WingMinusY, -1, s, sun, eclipse);
            sb.Append("  ");
            double litPy = AppendWing("+Y", s.WingPlusY, +1, s, sun, eclipse);
            sb.Append('\n');
            AnswerPanels(eclipse, s.HasSun, s.WingMinusY, litMy, s.WingPlusY, litPy);

            // Did the antenna come out?
            BeginRow("Antenna out?", "Antenna out?     ", true);
            AnswerAntenna(s);
            if (!s.Antenna.Present) sb.Append("?");
            else if (s.Antenna.State == DeployState.Jammed) sb.Append(Bad("JAMMED"));
            else if (s.Antenna.State == DeployState.Deployed) sb.Append(Good("DEPLOYED"));
            else sb.Append(Warn(s.Antenna.Label));
            // the deployment microswitch can disagree with the truth when the instructor injects a failure
            if (s.AntennaSensorDisagrees)
                sb.Append("  ").Append(Warn("sensor says " + s.AntennaSensor));
            else if (!string.IsNullOrEmpty(s.AntennaSensor) && s.Antenna.Present && s.Antenna.State != DeployState.Deployed
                     && s.Antenna.State != DeployState.Stowed && s.AntennaSensor != "UNKNOWN")
                sb.Append("  sensor ").Append(s.AntennaSensor);
            sb.Append('\n');

            // What the simulator, flight director and instructor have set (smo-vr/1 mission fields)
            if (s.Phase != null || s.ScMode != null)
            {
                BeginRow("Mission", "Mission          ");
                sb.Append(s.Phase != null ? Pretty(s.Phase) : "?").Append("   mode ");
                string m = s.ScMode ?? "?";
                sb.Append(m == "EMERGENCY" ? Bad(m) : m == "SAFE" ? Warn(m) : m == "?" ? m : Good(m));
                if (!double.IsNaN(s.MetS)) sb.Append("   MET ").Append(Met(s.MetS));
                sb.Append('\n');
            }
            if (s.AocsMode != null || s.Pointing != null)
            {
                BeginRow("Attitude control", "Attitude control ");
                sb.Append(s.AocsMode != null ? Pretty(s.AocsMode) : "?");
                if (s.Pointing != null) sb.Append("   pointing ").Append(Pretty(s.Pointing));
                sb.Append('\n');
            }
            if (s.HasContact)
            {
                BeginRow("Ground link", "Ground link      ");
                if (s.InContact) sb.Append(Good("IN CONTACT")).Append(string.IsNullOrEmpty(s.GroundStation) ? "" : "  " + s.GroundStation);
                else sb.Append("no station in view");
                sb.Append('\n');
            }
            if (s.Power != null) AppendPower(s.Power);
            if (a.ActiveFeed == FeedKind.Tcp && (s.Frozen || !double.IsNaN(s.Speed)))
            {
                BeginRow("Sim speed", "Sim speed        ");
                if (s.Frozen) sb.Append(Warn("FROZEN")).Append(" (instructor)");
                else sb.Append("x").Append(s.Speed.ToString("0.##", Inv)).Append(" (set by the instructor)");
                sb.Append('\n');
            }

            // Where is it?
            QuatD toEcef = AstroTime.EcefToEci(s.SimTime, a.space != null ? a.space.eciConvention : EciConvention.J2000).Conjugate;
            Vec3d e = toEcef.Rotate(s.PosEciKm);
            double lat = Math.Asin(MathUtil.Clamp(e.z / e.Length, -1, 1)) * MathUtil.Rad2Deg;
            double lon = Math.Atan2(e.y, e.x) * MathUtil.Rad2Deg;
            double alt = s.PosEciKm.Length - Ephemeris.EarthRadiusKm;
            BeginRow("Over", "Over             ");
            sb.Append(Math.Abs(lat).ToString("0.0", Inv)).Append(lat >= 0 ? " N  " : " S  ")
              .Append(Math.Abs(lon).ToString("0.0", Inv)).Append(lon >= 0 ? " E" : " W")
              .Append("   alt ").Append(alt.ToString("0", Inv)).Append(" km");
            if (a.ActiveFeed == FeedKind.Sandbox && a.Sandbox != null)
            {
                sb.Append('\n');
                BeginRow("Mode", "Mode             ");
                sb.Append(a.Sandbox.ModeName(a.SandboxTime));
            }
            EndRow();
            text = sb.ToString();
        }

        /// <summary>Channel 2 (power): what the arrays make, what's used, the battery, and any trouble.</summary>
        void AppendPower(PowerInfo p)
        {
            if (!double.IsNaN(p.NetW) || !double.IsNaN(p.GeneratedW))
            {
                BeginRow("Power", "Power            ");
                if (!double.IsNaN(p.NetW))
                {
                    string net = (p.NetW >= 0 ? "+" : "") + p.NetW.ToString("0.0", Inv) + " W";
                    sb.Append(p.NetW >= 0 ? Good(net) : Warn(net)).Append(p.NetW >= 0 ? " spare" : " short");
                }
                if (!double.IsNaN(p.GeneratedW)) sb.Append("   makes ").Append(p.GeneratedW.ToString("0.0", Inv)).Append(" W");
                if (!double.IsNaN(p.ConsumedW)) sb.Append(", uses ").Append(p.ConsumedW.ToString("0.0", Inv)).Append(" W");
                if (!double.IsNaN(p.BusVoltageV)) sb.Append("   bus ").Append(p.BusVoltageV.ToString("0.0", Inv)).Append(" V");
                sb.Append('\n');
            }
            if (!double.IsNaN(p.WingPyW) || !double.IsNaN(p.WingMyW))
            {
                BeginRow("Solar output", "Solar output     ");
                sb.Append("-Y ").Append(Watts(p.WingMyW)).Append("   +Y ").Append(Watts(p.WingPyW));
                if (!double.IsNaN(p.BodyFacesW)) sb.Append("   body ").Append(Watts(p.BodyFacesW));
                if (p.HasStrings && (!p.StringAEnabled || !p.StringBEnabled))
                    sb.Append("  ").Append(Bad("STRING " + (!p.StringAEnabled && !p.StringBEnabled ? "A+B" : !p.StringAEnabled ? "A" : "B") + " OFF"));
                sb.Append('\n');
            }
            if (!double.IsNaN(p.BatterySocPct))
            {
                BeginRow("Battery", "Battery          ");
                string soc = p.BatterySocPct.ToString("0", Inv) + "%";
                sb.Append(p.BatterySocPct < 15 ? Bad(soc) : p.BatterySocPct < 35 ? Warn(soc) : Good(soc));
                if (p.HasCharging) sb.Append(p.Charging ? "  charging" : "  draining");
                if (!double.IsNaN(p.BatteryVoltageV)) sb.Append("   ").Append(p.BatteryVoltageV.ToString("0.0", Inv)).Append(" V");
                if (!double.IsNaN(p.BatteryTempC)) sb.Append("   ").Append(p.BatteryTempC.ToString("0", Inv)).Append(" \u00b0C");
                if (p.LoadShedStage > 0) sb.Append("  ").Append(Bad("LOAD SHED " + p.LoadShedStage));
                sb.Append('\n');
            }
        }

        static string Watts(double w)
        {
            return double.IsNaN(w) ? "?" : w.ToString("0.0", Inv) + " W";
        }

        /// <summary>COARSE_SUN -> Coarse sun; keeps short words (LEOP, SAFE) as they are.</summary>
        static string Pretty(string word)
        {
            if (string.IsNullOrEmpty(word)) return "?";
            if (word.Length <= 4) return word;
            string w = word.Replace('_', ' ').ToLowerInvariant();
            return char.ToUpperInvariant(w[0]) + w.Substring(1);
        }

        /// <summary>Mission elapsed time as d hh:mm:ss.</summary>
        static string Met(double s)
        {
            if (s < 0) return "-" + Met(-s);
            long t = (long)s;
            long d = t / 86400, h = t / 3600 % 24, m = t / 60 % 60, sec = t % 60;
            string hms = h.ToString("00", Inv) + ":" + m.ToString("00", Inv) + ":" + sec.ToString("00", Inv);
            return d > 0 ? d.ToString(Inv) + "d " + hms : hms;
        }

        void AppendSource(EosatApp a, DisplayState s)
        {
            switch (a.ActiveFeed)
            {
                case FeedKind.Sandbox:
                    sb.Append("SANDBOX (built-in simulator, takes commands)\n");
                    sourceShort = "SANDBOX  built-in simulator";
                    break;
                case FeedKind.Replay:
                    sb.Append("REPLAY");
                    sourceShort = "REPLAY";
                    if (a.Replay != null)
                    {
                        sb.Append("  frame ").Append(a.Replay.Cursor).Append('/').Append(a.Replay.Count);
                        sourceShort += "  frame " + a.Replay.Cursor + "/" + a.Replay.Count;
                    }
                    AppendTimelineStats(a.Timeline);
                    sb.Append('\n');
                    break;
                case FeedKind.Tcp:
                    TcpFeedClient l = a.Link;
                    string endpoint = l != null ? l.ActiveEndpoint : TcpFeedClient.Describe(a.tcpHost, a.tcpPort);
                    string linkState;
                    if (l == null) linkState = Bad("stopped");
                    else if (!l.Connected) linkState = Bad(s.Predicting ? "not connected - predicting" : "not connected");
                    else if (s.Valid && s.DataAge > 2.5) linkState = Warn((s.Predicting ? "WEAK - predicting, " : "STALE ") + s.DataAge.ToString("0", Inv) + " s");
                    else linkState = Good("connected");
                    sourceShort = "LIVE " + endpoint + "  " + linkState;
                    sb.Append("LIVE ").Append(endpoint);
                    if (l != null && l.Connected && l.OnPowerChannel) sb.Append(" (with power)");
                    sb.Append("  ");
                    sb.Append(linkState);
                    if (l != null)
                    {
                        sb.Append("  seq ").Append(l.LastSeq).Append("  skipped ").Append(l.ParseErrors);
                        sb.Append("  status 0x").Append(l.ComputeStatusWord().ToString("X4", Inv));
                    }
                    AppendTimelineStats(a.Timeline);
                    sb.Append('\n');
                    break;
            }
        }

        void AppendTimelineStats(FrameTimeline t)
        {
            if (t == null) return;
            if (t.Duplicates > 0) sb.Append("  dup ").Append(t.Duplicates);
            if (t.BackwardJumps > 0) sb.Append("  reloads ").Append(t.BackwardJumps);
            if (t.ForwardJumps > 0) sb.Append("  jumps ").Append(t.ForwardJumps);
        }

        /// <returns>How squarely the cells face the Sun (a cosine), or NaN when that isn't known or it's in eclipse.</returns>
        double AppendWing(string name, DeployInfo w, int side, DisplayState s, Vec3d sunEci, bool eclipse)
        {
            sb.Append(name).Append(' ');
            if (!w.Present)
            {
                sb.Append('?');
                return double.NaN;
            }
            if (w.State == DeployState.Stowed) sb.Append("stowed");
            else if (w.State == DeployState.Deployed) sb.Append("out");
            else if (w.State == DeployState.Jammed)
                sb.Append(Bad("JAMMED")).Append(" at ").Append(Math.Round(w.EffectiveFraction * 100).ToString(Inv)).Append('%');
            else sb.Append(w.Label.ToLowerInvariant());
            if (eclipse || !s.HasSun) return double.NaN;
            Vec3d cellNormal;
            if (app == null || app.satelliteView == null || !app.satelliteView.TryGetCellNormalBody(side, out cellNormal))
                cellNormal = WingCellNormalBody(side, w.EffectiveFraction);
            double cosInc = Vec3d.Dot(s.QBodyToEci.Rotate(cellNormal), sunEci);
            if (cosInc > 0.05) sb.Append(' ').Append(Good("lit " + Math.Round(cosInc * 100).ToString(Inv) + "%"));
            else sb.Append(' ').Append(Warn("cells face away"));
            return cosInc;
        }

        void AnswerPanels(bool eclipse, bool hasSun, DeployInfo my, double litMy, DeployInfo py, double litPy)
        {
            Answer a = Answers[2];
            string detail = "-Y " + WingWord(my, litMy) + "   +Y " + WingWord(py, litPy);
            bool jammed = (my.Present && my.State == DeployState.Jammed) || (py.Present && py.State == DeployState.Jammed);
            if (jammed) SetAnswer(a, AnswerLevel.Bad, "JAMMED", detail);
            else if (eclipse) SetAnswer(a, AnswerLevel.Warn, "ECLIPSE", "in Earth's shadow");
            else if (!hasSun || (double.IsNaN(litMy) && double.IsNaN(litPy))) SetAnswer(a, AnswerLevel.Unknown, "?", "no Sun direction");
            else
            {
                int known = 0, lit = 0;
                if (!double.IsNaN(litMy))
                {
                    known++;
                    if (litMy > 0.05) lit++;
                }
                if (!double.IsNaN(litPy))
                {
                    known++;
                    if (litPy > 0.05) lit++;
                }
                if (lit == known) SetAnswer(a, AnswerLevel.Good, "YES", detail);
                else if (lit == 0) SetAnswer(a, AnswerLevel.Bad, "NO", detail);
                else SetAnswer(a, AnswerLevel.Warn, "PARTLY", detail);
            }
        }

        static string WingWord(DeployInfo w, double lit)
        {
            if (!w.Present) return "?";
            if (w.State == DeployState.Jammed) return "jammed";
            if (double.IsNaN(lit))
                return w.State == DeployState.Stowed ? "stowed" : w.State == DeployState.Deployed ? "out" : "?";
            return lit > 0.05 ? Math.Round(lit * 100).ToString(Inv) + "%" : "away";
        }

        void AnswerAntenna(DisplayState s)
        {
            Answer a = Answers[3];
            DeployInfo ant = s.Antenna;
            string pct = Math.Round(ant.EffectiveFraction * 100).ToString(Inv) + "%";
            if (!ant.Present || ant.State == DeployState.Unknown) SetAnswer(a, AnswerLevel.Unknown, "?", "no data");
            else if (ant.State == DeployState.Jammed) SetAnswer(a, AnswerLevel.Bad, "JAMMED", "stuck at " + pct);
            else if (ant.State == DeployState.Deployed) SetAnswer(a, AnswerLevel.Good, "YES", "deployed");
            else if (ant.State == DeployState.Deploying) SetAnswer(a, AnswerLevel.Warn, "OPENING", pct + " out");
            else SetAnswer(a, AnswerLevel.Warn, "NO", "stowed");
            if (s.AntennaSensorDisagrees)
            {
                a.Detail = "sensor says " + s.AntennaSensor;
                if (a.Level == AnswerLevel.Good) a.Level = AnswerLevel.Warn;
            }
        }

        /// <summary>
        /// Fallback when the model can't be asked (normally SatelliteView reads the panel itself):
        /// EOS SAT-1 panels lie on the two rear faces of the diamond-shaped body (cells facing
        /// out at +/-140.9 deg from +X) and fold out about the rear ridge by 39.1 deg each, until
        /// both face -X.
        /// </summary>
        public static Vec3d WingCellNormalBody(int side, double fraction)
        {
            double deg = side * (140.89 + 39.11 * MathUtil.Clamp01(fraction));
            double a = deg * MathUtil.Deg2Rad;
            return new Vec3d(Math.Cos(a), Math.Sin(a), 0);
        }

        static string Good(string s) { return "<b><color=#7CFC9A>" + s + "</color></b>"; }
        static string Warn(string s) { return "<b><color=#FFD166>" + s + "</color></b>"; }
        static string Bad(string s) { return "<b><color=#FF6B6B>" + s + "</color></b>"; }
    }
}
