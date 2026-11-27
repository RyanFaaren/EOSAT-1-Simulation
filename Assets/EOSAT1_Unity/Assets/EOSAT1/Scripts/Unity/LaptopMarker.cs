// EOSAT-1 VR - shows where the real laptop (and its keyboard) is while you wear the headset.
//
// Set it once: sit at the laptop with the headset on, hands on the keyboard, look at the
// laptop's screen, and press J. For a closer fit, first lay a controller flat in the middle of
// the keyboard (on G / H): J then uses the controller's position. A glowing outline of a laptop is then
// drawn where the real one is, with a "LAPTOP" label, in the headset only. On it lies a drawn
// keyboard, every key labelled, with the keys the simulation uses lit up in cyan. It stays put in the
// real room however you fly, turn or jump between viewpoints, because it hangs on the
// headset's tracking space (the room), not on the virtual world.
//
// Above it floats a key card: every key and what it does, so you can work the simulation from
// the keyboard without seeing the laptop's screen (its buttons can't be aimed at in the headset).
//
//   J          set the laptop here (again any time, e.g. after moving the chair)
//   Shift + J  hide / show the outline
//
// Remembered between runs (and shared by the menu room and the simulation). If you recentre
// the headset (hold the Meta button), the room moves under it: press J again.
// Adds itself to the scene's VR rig at runtime, like the speed slider.
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Eosat1
{
    public sealed class LaptopMarker : MonoBehaviour
    {
        public VrRig rig;
        public Color color = new Color(0.3f, 0.95f, 1f, 1f);
        [Tooltip("Where the keyboard is from your eyes when you sit at the laptop looking at its screen, metres: ahead, then down.")]
        public float keyboardAhead = 0.38f, keyboardBelow = 0.45f;
        [Tooltip("Size of the outline: width x depth of the keyboard deck, height of the screen.")]
        public Vector3 laptopSize = new Vector3(0.34f, 0.23f, 0.22f);

        const string KeyPose = "eosat1.vr.laptopPose";     // x y z yaw, in tracking space
        const string KeyHidden = "eosat1.vr.laptopHidden";

        Transform marker;
        Material mat, cardMat;
        Material[] keyMats;
        GameObject focusWarning;

        // the key card, in two columns (proportional font: no lining up with spaces)
        static readonly string[] CardLeft =
        {
            "<b>TIME</b>",
            "Space   pause / play",
            "=  -   faster / slower",
            "\u2192  \u2190   skip 1 minute",
            "Backspace   start again",
            "<b>SATELLITE</b> (Sandbox)",
            "1  2   open solar panels",
            "3   open the antenna",
            "6  7   fold panels / antenna",
            "4  5   jam antenna / panel",
            "N  S   point at Earth / Sun",
            "R  T   imaging roll / tumble",
        };
        static readonly string[] CardRight =
        {
            "<b>VIEWS</b>",
            "Z   whole Earth",
            "C   satellite camera   [ ]  zoom",
            "V  F   next / first viewpoint",
            "G  B   body axes / camera line",
            "O  P   orbit path / panel line",
            "<b>PANELS AND SETTINGS</b>",
            "H   status panel (laptop)",
            "M   data source",
            "K   headset controllers on / off",
            "J   laptop is here   Shift+J  hide",
            "Esc   main menu",
        };
        bool hasPose;
        Vector3 posePosition;
        float poseYaw;

        // ------------------------------------------------------------------ adding itself
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Attach();
        }

        static void Attach()
        {
            VrRig r = FindAnyObjectByType<VrRig>();
            if (r == null || r.GetComponent<LaptopMarker>() != null) return;
            r.gameObject.AddComponent<LaptopMarker>().rig = r;
        }

        // ------------------------------------------------------------------ setting and remembering
        void Start()
        {
            string saved = PlayerPrefs.GetString(KeyPose, "");
            string[] p = saved.Split(' ');
            float x, y, z, yaw;
            if (p.Length == 4 && Parse(p[0], out x) && Parse(p[1], out y) && Parse(p[2], out z) && Parse(p[3], out yaw))
            {
                posePosition = new Vector3(x, y, z);
                poseYaw = yaw;
                hasPose = true;
            }
        }

        static bool Parse(string s, out float v)
        {
            return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        Transform Room
        {
            get { return rig == null ? null : (rig.cameraOffset != null ? rig.cameraOffset : rig.transform); }
        }

        /// <summary>Put the laptop where it is now: in front of and below the eyes of someone sitting at it.</summary>
        void SetHere()
        {
            Transform room = Room;
            if (room == null || rig.head == null) return;
            Vector3 eyes = room.InverseTransformPoint(rig.head.position);
            Vector3 fwd = room.InverseTransformDirection(rig.head.forward);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            posePosition = eyes + fwd * keyboardAhead + Vector3.down * keyboardBelow;
            poseYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            // a controller laid on the keyboard, near where we'd guess it, pins it down exactly
            bool fromController = false;
            Vector3 best = Vector3.zero;
            float bestDistance = 0.6f;
            foreach (XRNode node in new[] { XRNode.RightHand, XRNode.LeftHand })
            {
                InputDevice dev = InputDevices.GetDeviceAtXRNode(node);
                Vector3 cp;
                bool tracked;
                if (!dev.isValid || !dev.TryGetFeatureValue(CommonUsages.devicePosition, out cp)) continue;
                if (dev.TryGetFeatureValue(CommonUsages.isTracked, out tracked) && !tracked) continue;
                if (cp.y > eyes.y - 0.2f) continue;   // in a hand near the face, not on the desk
                float dist = Vector3.Distance(cp, posePosition);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    best = cp;
                    fromController = true;
                }
            }
            if (fromController) posePosition = best + Vector3.down * 0.025f;   // a controller lying flat is ~5 cm thick
            hasPose = true;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(KeyPose, posePosition.x.ToString("0.###", inv) + " " + posePosition.y.ToString("0.###", inv) + " " +
                                           posePosition.z.ToString("0.###", inv) + " " + poseYaw.ToString("0.#", inv));
            PlayerPrefs.SetInt(KeyHidden, 0);
            PlayerPrefs.Save();
            Say(fromController ? "Laptop marked from the controller on the keyboard (Shift + J hides it)"
                               : "Laptop marked: its outline shows where the keyboard is (for a closer fit, lay a controller on the middle of the keyboard and press J again)");
        }

        void Say(string message)
        {
            EosatApp app = rig != null ? rig.app : null;
            if (app != null) app.Say(message);
            else Debug.Log("EOSAT-1: " + message);
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            bool headset = XRSettings.isDeviceActive;
            if (headset && !HudStyle.ModalActive && InputShim.Down(KeyCode.J))
            {
                bool shift = InputShim.Held(KeyCode.LeftShift) || InputShim.Held(KeyCode.RightShift);
                if (!shift) SetHere();
                else if (hasPose)
                {
                    bool hide = PlayerPrefs.GetInt(KeyHidden, 0) == 0;
                    PlayerPrefs.SetInt(KeyHidden, hide ? 1 : 0);
                    PlayerPrefs.Save();
                    Say(hide ? "Laptop outline hidden (Shift + J shows it)" : "Laptop outline shown");
                }
                else Say("No laptop set yet: sit at it with the headset on, look at its screen and press J");
            }

            bool show = headset && hasPose && PlayerPrefs.GetInt(KeyHidden, 0) == 0 && Room != null;
            if (show && marker == null) Build();
            if (marker == null) return;
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
            if (!show) return;
            if (marker.parent != Room) marker.SetParent(Room, false);
            marker.localPosition = posePosition;
            marker.localRotation = Quaternion.Euler(0f, poseYaw, 0f);
            // a slow, gentle pulse so it reads as a guide, not part of the space scene
            // keys only reach the simulation while its window has the focus: say so where they look
            bool lost = !InputShim.KeyboardReaches;
            if (focusWarning != null && focusWarning.activeSelf != lost) focusWarning.SetActive(lost);
            Color c = color * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 2f));
            c.a = 1f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        // ------------------------------------------------------------------ the outline
        void Build()
        {
            Shader sh = Shader.Find("EOSAT1/UnlitColor");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) return;
            mat = new Material(sh) { name = "EOSAT1 Laptop Marker (runtime)" };

            marker = new GameObject("Real Laptop Marker").transform;
            marker.SetParent(Room, false);
            float w = laptopSize.x, d = laptopSize.y, h = laptopSize.z, t = 0.006f;
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (System.Exception) { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch (System.Exception) { }
            }

            // the marker's origin is the middle of the keyboard (between G and H); the keys run
            // from the space bar (front, towards you) to the function row (back)
            float back = 3.5f * KeyPitch + 0.015f;   // the deck's back edge, where the screen hinges
            Rect3(marker, new Vector3(0f, -0.004f, back - d * 0.5f), Quaternion.identity, w, d, t);   // deck (keys + touchpad)
            Rect3(marker, new Vector3(0f, -0.004f, back - d + 0.045f), Quaternion.identity, 0.11f, 0.06f, t * 0.6f);   // touchpad
            BuildKeyboard(marker, f);
            // the screen: hinged at the back edge, leaning back 15 degrees
            var screen = new GameObject("Screen").transform;
            screen.SetParent(marker, false);
            screen.localPosition = new Vector3(0f, 0f, back);
            screen.localRotation = Quaternion.Euler(-75f, 0f, 0f);
            Rect3(screen, new Vector3(0f, 0f, h * 0.5f), Quaternion.identity, w, h, t);

            // label above the screen
            var label = new GameObject("Label");
            label.transform.SetParent(marker, false);
            label.transform.localPosition = new Vector3(0f, h + 0.08f, back);
            var tm = label.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                label.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.text = "LAPTOP";
            tm.fontSize = 48;
            tm.characterSize = 0.006f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            label.AddComponent<FaceCamera>();

            // the key card: a dark panel standing above the laptop's screen, facing you
            cardMat = new Material(sh) { name = "EOSAT1 Laptop Key Card (runtime)" };
            Color cardColor = new Color(0.02f, 0.05f, 0.09f, 1f);
            if (cardMat.HasProperty("_BaseColor")) cardMat.SetColor("_BaseColor", cardColor);
            if (cardMat.HasProperty("_Color")) cardMat.SetColor("_Color", cardColor);
            var card = new GameObject("Key Card").transform;
            card.SetParent(marker, false);
            card.localPosition = new Vector3(0f, h + 0.3f, back + 0.06f);
            card.localRotation = Quaternion.Euler(-12f, 0f, 0f);   // leaning back a little, towards your eyes
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Backing";
            Destroy(panel.GetComponent<Collider>());
            panel.transform.SetParent(card, false);
            panel.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            panel.transform.localScale = new Vector3(0.58f, 0.33f, 1f);
            Renderer pr = panel.GetComponent<Renderer>();
            pr.sharedMaterial = cardMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            Rect3(card, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), 0.58f, 0.33f, 0.004f);   // cyan frame
            CardText(card, f, "<b>KEYS</b>   (easier than aiming the mouse in the headset)", new Vector3(-0.27f, 0.145f, 0f), 0.0034f, new Color(0.3f, 0.95f, 1f, 1f));
            CardText(card, f, string.Join("\n", CardLeft), new Vector3(-0.27f, 0.105f, 0f), 0.003f, new Color(0.85f, 0.95f, 1f, 1f));
            var warn = new GameObject("Keyboard Warning");
            warn.transform.SetParent(card, false);
            warn.transform.localPosition = new Vector3(0f, -0.19f, -0.002f);
            var wt = warn.AddComponent<TextMesh>();
            if (f != null)
            {
                wt.font = f;
                warn.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            wt.text = "KEYBOARD NOT CONNECTED: click the Unity window on the laptop once";
            wt.fontSize = 48;
            wt.characterSize = 0.0036f;
            wt.anchor = TextAnchor.UpperCenter;
            wt.alignment = TextAlignment.Center;
            wt.color = new Color(1f, 0.4f, 0.35f, 1f);
            focusWarning = warn;
            focusWarning.SetActive(false);
            CardText(card, f, string.Join("\n", CardRight), new Vector3(0.01f, 0.105f, 0f), 0.003f, new Color(0.85f, 0.95f, 1f, 1f));
            marker.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ the drawn keyboard
        const float KeyPitch = 0.019f;   // a laptop key and its gap

        // US laptop layout, back row first: label, width in keys
        static readonly string[][] Rows =
        {
            new[] { "Esc", "1", "F1", "1", "F2", "1", "F3", "1", "F4", "1", "F5", "1", "F6", "1", "F7", "1", "F8", "1", "F9", "1", "F10", "1", "F11", "1", "F12", "1", "Del", "2" },
            new[] { "`", "1", "1", "1", "2", "1", "3", "1", "4", "1", "5", "1", "6", "1", "7", "1", "8", "1", "9", "1", "0", "1", "-", "1", "=", "1", "Bksp", "2" },
            new[] { "Tab", "1.5", "Q", "1", "W", "1", "E", "1", "R", "1", "T", "1", "Y", "1", "U", "1", "I", "1", "O", "1", "P", "1", "[", "1", "]", "1", "\\", "1.5" },
            new[] { "Caps", "1.75", "A", "1", "S", "1", "D", "1", "F", "1", "G", "1", "H", "1", "J", "1", "K", "1", "L", "1", ";", "1", "'", "1", "Enter", "2.25" },
            new[] { "Shift", "2.25", "Z", "1", "X", "1", "C", "1", "V", "1", "B", "1", "N", "1", "M", "1", ",", "1", ".", "1", "/", "1", "Shift", "2.75" },
            new[] { "Ctrl", "1.5", "Fn", "1", "Win", "1", "Alt", "1", "Space", "5", "Alt", "1", "Ctrl", "1", "←", "1", "↑↓", "1", "→", "1.5" },
        };

        // the keys that do something in the simulation (the key card lists them)
        const string Used = " Esc F1 1 2 3 4 5 6 7 - = Bksp Tab R T O P [ ] S F G H J K L Z C V B N M , . Shift Space ← → ";

        static bool IsUsed(string key)
        {
            return Used.Contains(" " + key + " ");
        }

        void BuildKeyboard(Transform parent, Font f)
        {
            Shader sh = mat.shader;
            var lit = new Material(sh) { name = "EOSAT1 Laptop Key (used)" };
            var dim = new Material(sh) { name = "EOSAT1 Laptop Key" };
            SetColor(lit, new Color(0.05f, 0.35f, 0.42f, 1f));
            SetColor(dim, new Color(0.08f, 0.09f, 0.11f, 1f));
            keyMats = new[] { lit, dim };

            var kb = new GameObject("Keyboard").transform;
            kb.SetParent(parent, false);
            const float totalWidth = 15f;   // every row is 15 keys wide
            for (int r = 0; r < Rows.Length; r++)
            {
                // back to front: the home row (row 3, A S D F G H J K L) sits on the origin
                float z = (3 - r) * KeyPitch;
                if (r == 0) z -= 0.2f * KeyPitch;   // the short function keys sit just behind the number row
                string[] row = Rows[r];
                float x = -totalWidth * 0.5f;
                for (int i = 0; i + 1 < row.Length; i += 2)
                {
                    float width;
                    if (!float.TryParse(row[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out width) || width <= 0f)
                        width = 1f;
                    string key = row[i];
                    float cx = (x + width * 0.5f) * KeyPitch;
                    x += width;
                    Key(kb, f, key, new Vector3(cx, 0f, z), width * KeyPitch - 0.003f, (r == 0 ? 0.6f : 1f) * KeyPitch - 0.003f, IsUsed(key));
                }
            }
        }

        void Key(Transform parent, Font f, string label, Vector3 pos, float width, float depth, bool used)
        {
            Quaternion flat = Quaternion.Euler(90f, 0f, 0f);   // lying on the desk, readable from above
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Key " + label;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localRotation = flat;
            q.transform.localScale = new Vector3(width, depth, 1f);
            Renderer r = q.GetComponent<Renderer>();
            r.sharedMaterial = keyMats[used ? 0 : 1];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var t = new GameObject("Label");
            t.transform.SetParent(parent, false);
            t.transform.localPosition = pos + new Vector3(0f, 0.001f, 0f);
            t.transform.localRotation = flat;
            var tm = t.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                t.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.text = label;
            tm.fontSize = 48;
            tm.characterSize = label.Length > 2 ? 0.0011f : 0.0019f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = used ? new Color(0.75f, 1f, 1f, 1f) : new Color(0.45f, 0.5f, 0.55f, 1f);
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        static void CardText(Transform card, Font f, string text, Vector3 pos, float size, Color color)
        {
            var go = new GameObject("Card Text");
            go.transform.SetParent(card, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.richText = true;
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = size;
            tm.lineSpacing = 1.15f;
            tm.anchor = TextAnchor.UpperLeft;
            tm.alignment = TextAlignment.Left;
            tm.color = color;
        }

        /// <summary>A rectangle of thin bars lying in the local XZ plane (width along X, depth along Z).</summary>
        void Rect3(Transform parent, Vector3 centre, Quaternion rot, float width, float depth, float bar)
        {
            Bar(parent, centre + rot * new Vector3(0f, 0f, -depth * 0.5f), rot, new Vector3(width + bar, bar, bar));
            Bar(parent, centre + rot * new Vector3(0f, 0f, depth * 0.5f), rot, new Vector3(width + bar, bar, bar));
            Bar(parent, centre + rot * new Vector3(-width * 0.5f, 0f, 0f), rot, new Vector3(bar, bar, depth + bar));
            Bar(parent, centre + rot * new Vector3(width * 0.5f, 0f, 0f), rot, new Vector3(bar, bar, depth + bar));
        }

        void Bar(Transform parent, Vector3 pos, Quaternion rot, Vector3 size)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Edge";
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            g.transform.localScale = size;
            Renderer r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (cardMat != null) Destroy(cardMat);
            if (keyMats != null) foreach (Material m in keyMats) if (m != null) Destroy(m);
        }
    }
}
