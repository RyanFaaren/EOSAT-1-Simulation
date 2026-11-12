// EOSAT-1 VR - a small panel of text that answers the four questions from the course
// slides at a glance: Is it tumbling? Is the camera facing Earth? Are the wings in the
// sun? Did the antenna come out? Plus where it is, the time, and the link health.
// Desktop: drawn on screen (H toggles). Headset: a panel on the left controller (X toggles).
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
        [Tooltip("World-space text for the headset (shown on the left controller).")]
        public TextMesh worldText;
        public GameObject worldPanel;
        public bool showOnScreen = true;
        public bool showHelp = true;
        public float refreshInterval = 0.25f;

        public bool Visible { get; private set; }

        readonly StringBuilder sb = new StringBuilder(1024);
        string text = "";
        float nextRefresh;
        readonly List<string> rowLabels = new List<string>();
        readonly List<string> rowValues = new List<string>();
        string rowLabel = "";
        int rowStart = -1;
        string headerTime = "", headerChip = "";
        List<string> helpLines, helpHeads;
        int helpFlags = -1;

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

        public void Toggle()
        {
            showOnScreen = !showOnScreen;
        }

        void Update()
        {
            bool headset = UnityEngine.XR.XRSettings.isDeviceActive;
            if (worldPanel != null && worldPanel.activeSelf != (headset && showOnScreen)) worldPanel.SetActive(headset && showOnScreen);
            Visible = showOnScreen;
            if (Time.unscaledTime < nextRefresh || app == null) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Build(app);
            if (worldText != null && headset) worldText.text = text;
        }

        void OnGUI()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive || app == null) return;
            if (Event.current.type != EventType.Repaint) return;
            HudStyle.Ensure();
            float u = HudStyle.Unit;
            float pad = Mathf.Round(u * 0.8f);
            float margin = 12f;

            if (showOnScreen) DrawStatus(u, pad, margin);
            if (showHelp) DrawHelp(u, pad, margin);

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

        /// <summary>The key list, bottom left: one line per group, like the menu's controls screen.</summary>
        void DrawHelp(float u, float pad, float margin)
        {
            int flags = (app.CanCommand ? 1 : 0) | (app.CanControlTime ? 2 : 0);
            if (helpLines == null || helpFlags != flags)
            {
                helpFlags = flags;
                helpLines = new List<string>();
                helpHeads = new List<string>();
                helpHeads.Add("VIEW");
                helpLines.Add(Keys("Drag", "orbit", "Wheel", "zoom", "V", "next view", "F", "first view", "C", "satellite camera",
                                   "Z", "whole Earth", "O", "orbit path", "P", "panel axes"));
                if (app.CanControlTime)
                {
                    helpHeads.Add("TIME");
                    helpLines.Add(Keys("Space", "pause", "-  =", "slower / faster", "Left  Right", "jump 60 s", "Backspace", "restart"));
                }
                if (app.CanCommand)
                {
                    helpHeads.Add("SATELLITE");
                    helpLines.Add(Keys("1  2", "solar panels", "3", "antenna", "4", "antenna jam", "5", "stow", "N", "face Earth",
                                       "S", "face Sun", "R", "roll to image", "T", "tumble"));
                }
                helpHeads.Add("DISPLAY");
                helpLines.Add(Keys("G", "body axes", "B", "boresight", "L", "view frame", "M", "data source", "H", "status panel", "Esc", "menu"));
            }
            float rowH = Mathf.Round(u * 1.35f);
            float gap = Mathf.Round(u * 1.0f);
            float headW = 0f, lineW = 0f;
            for (int i = 0; i < helpLines.Count; i++)
            {
                headW = Mathf.Max(headW, HudStyle.Header.CalcSize(new GUIContent(helpHeads[i])).x);
                lineW = Mathf.Max(lineW, HudStyle.SmallDim.CalcSize(new GUIContent(helpLines[i])).x);
            }
            float w = Mathf.Min(headW + gap + lineW + pad * 2f, Screen.width - margin * 2f);
            float h = pad * 1.6f + helpLines.Count * rowH;
            var panel = new Rect(margin, Screen.height - margin - h, w, h);
            HudStyle.DrawPanel(panel);
            float x = panel.x + pad, y = panel.y + pad * 0.8f;
            for (int i = 0; i < helpLines.Count; i++)
            {
                GUI.Label(new Rect(x, y, headW, rowH), helpHeads[i], HudStyle.Header);
                GUI.Label(new Rect(x + headW + gap, y, Mathf.Max(0f, panel.xMax - pad - (x + headW + gap)), rowH), helpLines[i], HudStyle.SmallDim);
                y += rowH;
            }
        }

        /// <summary>"key, what it does" pairs as one line: keys bright, descriptions dim.</summary>
        static string Keys(params string[] pairs)
        {
            var b = new StringBuilder(256);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (i > 0) b.Append("     ");
                b.Append("<b><color=").Append(HudStyle.BrightHex).Append('>').Append(pairs[i]).Append("</color></b>  ").Append(pairs[i + 1]);
            }
            return b.ToString();
        }

        // rows for the on-screen panel are cut out of the same text the headset panel shows
        void BeginRow(string label, string paddedLabel)
        {
            EndRow();
            rowLabel = label;
            sb.Append(paddedLabel);
            rowStart = sb.Length;
        }

        void EndRow()
        {
            if (rowStart < 0) return;
            rowLabels.Add(rowLabel);
            rowValues.Add(sb.ToString(rowStart, sb.Length - rowStart).TrimEnd('\n'));
            rowStart = -1;
        }

        // ------------------------------------------------------------------ text
        void Build(EosatApp a)
        {
            DisplayState s = a.State;
            sb.Length = 0;
            rowLabels.Clear();
            rowValues.Clear();
            rowStart = -1;
            headerTime = s.Valid ? AstroTime.ToIso(s.SimTime).Replace('T', ' ').Replace("Z", " UTC") : "waiting for data";
            if (a.CanControlTime) headerChip = a.paused ? "PAUSED" : "x" + a.Speed.ToString("0.##", Inv);
            else headerChip = s.Valid && s.SimRate > 1.5 ? "sim x" + s.SimRate.ToString("0", Inv) : "";
            sb.Append("<b>EOSAT-1</b>  ").Append(headerTime);
            if (headerChip == "PAUSED") sb.Append("   <color=#ffcc55>PAUSED</color>");
            else if (headerChip.Length > 0) sb.Append("   ").Append(headerChip);
            sb.Append('\n');
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
            BeginRow("Tumbling?", "Tumbling?        ");
            if (double.IsNaN(rate)) sb.Append("? (rate unknown at this data speed)");
            else if (rate > 0.5) sb.Append(Bad("YES")).Append("  ").Append(rate.ToString("0.0", Inv)).Append(" deg/s");
            else sb.Append(Good("no")).Append("  ").Append(rate.ToString("0.00", Inv)).Append(" deg/s");
            sb.Append('\n');

            // Is the camera facing Earth?
            Vec3d camEci = s.QBodyToEci.Rotate(Vec3d.UnitZ);
            double offNadir = Vec3d.Angle(camEci, -s.PosEciKm) * MathUtil.Rad2Deg;
            BeginRow("Camera on Earth?", "Camera on Earth? ");
            if (offNadir < 3.0) sb.Append(Good("YES"));
            else if (offNadir < 68.0) sb.Append(Warn("off-pointed"));
            else sb.Append(Bad("NO"));
            sb.Append("  ").Append(offNadir.ToString("0.0", Inv)).Append(" deg off nadir\n");

            // Are the wings in the sun?
            Vec3d sun = s.HasSun ? s.SunDirEci : Vec3d.Zero;
            bool eclipse = a.space != null ? a.space.Illumination < 0.5 : s.InEclipse;
            BeginRow("Panels in sun?", "Panels in sun?   ");
            if (eclipse) sb.Append(Warn("ECLIPSE")).Append(" (Earth's shadow)  ");
            AppendWing("-Y", s.WingMinusY, -1, s, sun, eclipse);
            sb.Append("  ");
            AppendWing("+Y", s.WingPlusY, +1, s, sun, eclipse);
            sb.Append('\n');

            // Did the antenna come out?
            BeginRow("Antenna out?", "Antenna out?     ");
            if (!s.Antenna.Present) sb.Append("?");
            else if (s.Antenna.State == DeployState.Jammed) sb.Append(Bad("JAMMED"));
            else if (s.Antenna.State == DeployState.Deployed) sb.Append(Good("DEPLOYED"));
            else sb.Append(Warn(s.Antenna.Label));
            sb.Append('\n');

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

        void AppendSource(EosatApp a, DisplayState s)
        {
            switch (a.ActiveFeed)
            {
                case FeedKind.Sandbox:
                    sb.Append("SANDBOX (built-in simulator, takes commands)\n");
                    break;
                case FeedKind.Replay:
                    sb.Append("REPLAY");
                    if (a.Replay != null)
                        sb.Append("  frame ").Append(a.Replay.Cursor).Append('/').Append(a.Replay.Count);
                    AppendTimelineStats(a.Timeline);
                    sb.Append('\n');
                    break;
                case FeedKind.Tcp:
                    TcpFeedClient l = a.Link;
                    sb.Append("LIVE ").Append(a.tcpHost).Append(':').Append(a.tcpPort).Append("  ");
                    if (l == null) sb.Append(Bad("stopped"));
                    else if (!l.Connected) sb.Append(Bad("not connected"));
                    else if (s.Valid && s.DataAge > 2.0) sb.Append(Warn("STALE " + s.DataAge.ToString("0", Inv) + " s"));
                    else sb.Append(Good("connected"));
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

        void AppendWing(string name, DeployInfo w, int side, DisplayState s, Vec3d sunEci, bool eclipse)
        {
            sb.Append(name).Append(' ');
            if (!w.Present)
            {
                sb.Append('?');
                return;
            }
            if (w.State == DeployState.Stowed) sb.Append("stowed");
            else if (w.State == DeployState.Deployed) sb.Append("out");
            else sb.Append(w.Label.ToLowerInvariant());
            if (eclipse || !s.HasSun) return;
            Vec3d cellNormal;
            if (app == null || app.satelliteView == null || !app.satelliteView.TryGetCellNormalBody(side, out cellNormal))
                cellNormal = WingCellNormalBody(side, w.EffectiveFraction);
            double cosInc = Vec3d.Dot(s.QBodyToEci.Rotate(cellNormal), sunEci);
            if (cosInc > 0.05) sb.Append(' ').Append(Good("lit " + Math.Round(cosInc * 100).ToString(Inv) + "%"));
            else sb.Append(' ').Append(Warn("cells face away"));
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
