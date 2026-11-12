// EOSAT-1 VR - the look of the on-screen display in the simulation, matched to the main
// menu (MissionControlMenu / MenuKit): dark navy panels with rounded corners and a thin
// cyan border, bright text, dim labels, cyan headings with spaced letters, amber for
// warnings. The panels are small textures drawn in code and stretched by their middle
// (9-slice), so no image files are needed.
//
// Call HudStyle.Ensure() at the start of OnGUI before using the styles.
using UnityEngine;

namespace Eosat1
{
    public static class HudStyle
    {
        // the menu's palette
        public static readonly Color Accent = new Color(0.25f, 0.8f, 1f, 1f);
        public static readonly Color TextBright = new Color(0.93f, 0.96f, 1f, 1f);
        public static readonly Color TextDim = new Color(0.55f, 0.66f, 0.76f, 1f);
        public static readonly Color Amber = new Color(1f, 0.82f, 0.4f, 1f);
        public static readonly Color Navy = new Color(0.02f, 0.06f, 0.1f, 1f);

        public const string AccentHex = "#40CCFF";
        public const string DimHex = "#8CA8C2";
        public const string BrightHex = "#EDF5FF";
        public const string GoodHex = "#7CFC9A";
        public const string WarnHex = "#FFD166";
        public const string BadHex = "#FF6B6B";

        public static GUIStyle Panel, Header, Text, Dim, Small, SmallDim, Button, ButtonHover, ButtonOn, ButtonOnHover, Toast, Chip;

        static Texture2D panelTex, buttonTex, buttonHoverTex, buttonOnTex, buttonOnHoverTex, toastTex, chipTex, softDot;
        static int builtForHeight = -1;

        /// <summary>Base text height in pixels; everything scales with the window.</summary>
        public static float Unit { get { return Mathf.Max(12f, Screen.height / 58f); } }

        public static void Ensure()
        {
            if (panelTex == null)
            {
                Color bT = new Color(0.06f, 0.1f, 0.16f, 0.94f), bB = new Color(0.035f, 0.06f, 0.1f, 0.94f);
                panelTex = RoundedRect(32, 7f, 1.25f, new Color(0.03f, 0.065f, 0.115f, 0.9f), new Color(0.012f, 0.025f, 0.05f, 0.9f), WithAlpha(Accent, 0.85f));
                buttonTex = RoundedRect(32, 7f, 1.25f, bT, bB, WithAlpha(Accent, 0.55f));
                Color hover = Color.Lerp(new Color(0.03f, 0.08f, 0.12f, 1f), Accent, 0.5f);
                buttonHoverTex = RoundedRect(32, 7f, 1.5f, hover, hover * new Color(0.7f, 0.7f, 0.7f, 1f), Color.Lerp(Accent, Color.white, 0.6f));
                buttonOnTex = RoundedRect(32, 7f, 1.5f, Color.Lerp(Navy, Accent, 0.32f), Color.Lerp(Navy, Accent, 0.16f), WithAlpha(Accent, 0.95f));
                Color onHover = Color.Lerp(Navy, Accent, 0.6f);
                buttonOnHoverTex = RoundedRect(32, 7f, 1.5f, onHover, onHover * new Color(0.75f, 0.75f, 0.75f, 1f), Color.Lerp(Accent, Color.white, 0.6f));
                toastTex = RoundedRect(32, 10f, 1.25f, new Color(0.03f, 0.075f, 0.13f, 0.95f), new Color(0.02f, 0.05f, 0.09f, 0.95f), WithAlpha(Accent, 0.95f));
                chipTex = RoundedRect(32, 6f, 1f, WithAlpha(Accent, 0.16f), WithAlpha(Accent, 0.1f), WithAlpha(Accent, 0.6f));
                builtForHeight = -1;
            }
            if (builtForHeight == Screen.height && Panel != null) return;
            builtForHeight = Screen.height;

            float u = Unit;
            int pad = Mathf.RoundToInt(u * 0.75f);
            Panel = Background(panelTex, 10);
            Text = Plain(Mathf.RoundToInt(u), TextBright, TextAnchor.UpperLeft, false);
            Dim = Plain(Mathf.RoundToInt(u), TextDim, TextAnchor.UpperLeft, false);
            Small = Plain(Mathf.RoundToInt(u * 0.86f), TextBright, TextAnchor.UpperLeft, false);
            SmallDim = Plain(Mathf.RoundToInt(u * 0.86f), TextDim, TextAnchor.UpperLeft, false);
            Header = Plain(Mathf.RoundToInt(u * 0.92f), Accent, TextAnchor.UpperLeft, true);

            Button = Background(buttonTex, 10);
            Button.alignment = TextAnchor.MiddleCenter;
            Button.fontSize = Mathf.RoundToInt(u * 0.92f);
            Button.richText = true;
            Button.normal.textColor = new Color(0.82f, 0.9f, 0.96f, 1f);
            ButtonHover = new GUIStyle(Button);
            ButtonHover.normal.background = buttonHoverTex;
            ButtonHover.normal.textColor = Color.white;
            ButtonOn = new GUIStyle(Button);
            ButtonOn.normal.background = buttonOnTex;
            ButtonOn.normal.textColor = Color.white;
            ButtonOnHover = new GUIStyle(ButtonOn);
            ButtonOnHover.normal.background = buttonOnHoverTex;

            Toast = Background(toastTex, 12);
            Toast.alignment = TextAnchor.MiddleCenter;
            Toast.fontSize = Mathf.RoundToInt(u);
            Toast.richText = true;
            Toast.normal.textColor = TextBright;
            Toast.padding = new RectOffset(pad * 2, pad * 2, pad, pad);

            Chip = Background(chipTex, 9);
            Chip.alignment = TextAnchor.MiddleCenter;
            Chip.fontSize = Mathf.RoundToInt(u * 0.82f);
            Chip.fontStyle = FontStyle.Bold;
            Chip.normal.textColor = Accent;
            Chip.padding = new RectOffset(pad, pad, 2, 2);
        }

        static GUIStyle Background(Texture2D tex, int slice)
        {
            var s = new GUIStyle();
            s.normal.background = tex;
            s.border = new RectOffset(slice, slice, slice, slice);
            s.wordWrap = false;
            s.clipping = TextClipping.Clip;
            return s;
        }

        static GUIStyle Plain(int size, Color color, TextAnchor anchor, bool bold)
        {
            var s = new GUIStyle();
            s.fontSize = size;
            s.alignment = anchor;
            s.richText = true;
            s.wordWrap = false;
            s.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            s.normal.textColor = color;
            return s;
        }

        public static Color WithAlpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }

        /// <summary>Letters with spaces between them, as the menu's headings.</summary>
        public static string Spaced(string s)
        {
            return MenuKit.Spaced(s);
        }

        // ------------------------------------------------------------------ drawing helpers (inside OnGUI)
        public static void DrawPanel(Rect r)
        {
            GUI.Box(r, GUIContent.none, Panel);
        }

        public static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Frame(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y + t, t, r.height - 2f * t), c);
            Fill(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), c);
        }

        /// <summary>Mouse position in OnGUI coordinates (origin top-left).</summary>
        public static Vector2 Mouse()
        {
            Vector2 m = InputShim.MousePosition();
            return new Vector2(m.x, Screen.height - m.y);
        }

        // ------------------------------------------------------------------ textures
        /// <summary>A rounded rectangle with a border and a top-to-bottom fill, antialiased.</summary>
        public static Texture2D RoundedRect(int size, float radius, float border, Color top, Color bottom, Color borderColor)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "EOSAT1 HUD panel";
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;   // < 0 inside
                    float inside = Mathf.Clamp01(0.5f - d);
                    float edge = Mathf.Clamp01(d + border + 0.5f) * borderColor.a;
                    Color fill = Color.Lerp(bottom, top, (y + 0.5f) / size);
                    Color c = Color.Lerp(fill, borderColor, edge);
                    c.a = Mathf.Lerp(fill.a, 1f, edge) * inside;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// A white dot with a solid core and a soft glow around it (alpha only). Read across
        /// its middle it is the cross-section of a glowing line.
        /// </summary>
        public static Texture2D SoftDot
        {
            get
            {
                if (softDot != null) return softDot;
                const int size = 64;
                softDot = new Texture2D(size, size, TextureFormat.RGBA32, false);
                softDot.name = "EOSAT1 soft dot";
                softDot.hideFlags = HideFlags.HideAndDontSave;
                softDot.wrapMode = TextureWrapMode.Clamp;
                softDot.filterMode = FilterMode.Bilinear;
                var px = new Color[size * size];
                float half = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float e = Mathf.Clamp01((r - 0.14f) / 0.16f);
                        float core = 1f - e * e * (3f - 2f * e);   // solid to 14% of the radius, gone by 30%
                        float glow = Mathf.Clamp01(1f - r);
                        glow = 0.42f * glow * glow;
                        px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core + glow * (1f - core)));
                    }
                }
                softDot.SetPixels(px);
                softDot.Apply(false, false);
                return softDot;
            }
        }
    }
}
