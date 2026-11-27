// EOSAT-1 VR - shows where the real laptop (a Lenovo Legion 7, 16") and its keyboard are while
// you wear the headset, so you can find the keys without taking it off.
//
// Setting it (J), best first:
//   1. Lay one controller flat on the desk against each side of the laptop, level with the
//      keyboard's middle row (A S D F ...), and press J. The spot and the angle come from the two
//      controllers, so the drawn keyboard sits on the real one.
//   2. One controller: against the left side, J; then against the right side, J.
//   3. No controller on the desk: sit at the laptop, look at its screen, J. A guess from your head.
//   Shift + J hides / shows it.
//   Fine-tuning, if the drawn keys sit a little off the real ones:
//     Ctrl + arrows            move it left / right / away / towards you
//     Ctrl + Shift + up/down   raise / lower it
//     Ctrl + [ ]               turn it
//   Hold for longer moves; it is saved when you let go.
//   Ctrl + J   the keyboard test: a drawn key flashes, you press the real key under that spot,
//              five times (G, Q, P, Z, M). Keys you hit instead show which way the drawing is
//              off; at the end it says how many matched and moves the drawing to correct it.
//              Ctrl + J again stops it. While it runs, the keys do nothing else.
//
// With the headset controllers switched off (K), the headset's screens appear on this laptop
// instead of on your hands: the satellite camera on its screen, the VR menu (right-click) over
// it, and the status panel standing beside it on the left, like a second monitor.
//
// What you see: the outline of the laptop (deck, touchpad, screen) and its keyboard drawn flat on
// the deck, every key labelled, the keys the simulation uses lit up in cyan. It hangs on the
// headset's tracking space (the room), not on the virtual world, so it stays on the real laptop
// however you fly, turn or jump between viewpoints. Remembered between runs (menu room and
// simulation alike). If you recentre the headset (hold the Meta button), press J again.
//
// The layout is the Legion 7 16" family's: main keys, short right Shift with the up arrow beside
// it, the arrows under it, and a number pad. Generations differ in small details (the top row's
// right end, the arrows' exact place), so a key or two may be off by a little.
// Adds itself to the scene's VR rig at runtime, like the speed slider.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Eosat1
{
    public sealed class LaptopMarker : MonoBehaviour
    {
        public VrRig rig;
        public Color color = new Color(0.3f, 0.95f, 1f, 1f);
        [Tooltip("Where the keyboard is from your eyes when you sit at the laptop looking at its screen, metres: ahead, then down (the guess used with no controller).")]
        public float keyboardAhead = 0.38f, keyboardBelow = 0.45f;
        [Tooltip("Legion 7 16\": width x depth of the base, height of the lid, metres.")]
        public Vector3 laptopSize = new Vector3(0.358f, 0.262f, 0.24f);
        [Tooltip("How far the keyboard's top is below a controller's tracked point when the controller lies flat on the desk beside the laptop, metres.")]
        public float besideDrop = 0.01f;

        const string KeyPose = "eosat1.vr.laptopPose";     // x y z yaw, in tracking space
        const string KeyHidden = "eosat1.vr.laptopHidden";

        Transform marker;
        Transform screenAnchor, sideAnchor;
        Material mat;

        /// <summary>The laptop marker of the scene (null until the VR rig has one).</summary>
        public static LaptopMarker Current { get; private set; }

        /// <summary>
        /// Where the headset's screens go when the controllers are off: 'screen' is the middle of the
        /// drawn laptop's screen, 'side' a spot beside it on the left. Both face you (their forward
        /// points away from you, into the screen). False while the laptop isn't set, is hidden
        /// (Shift + J), or no headset is in use.
        /// </summary>
        public bool TryGetAnchors(out Transform screen, out Transform side)
        {
            screen = screenAnchor;
            side = sideAnchor;
            return marker != null && marker.gameObject.activeInHierarchy && screen != null && side != null;
        }

        /// <summary>Usable size of the laptop's display, metres (a Legion 7 16": 16:10, about 34.5 x 21.5 cm).</summary>
        public static readonly Vector2 DisplaySize = new Vector2(0.345f, 0.215f);

        void OnEnable()
        {
            Current = this;
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
        }
        Material[] keyMats;

        bool hasPose;
        Vector3 posePosition;   // the middle of the keyboard, on its middle row (A S D F ...)
        float poseYaw;

        // fine-tuning (Ctrl + arrows): unsaved changes, and whether the how-to has been shown
        bool nudged, nudgeHintShown;
        const float NudgeSpeed = 0.02f;      // metres per second while a key is held
        const float NudgeTurnSpeed = 4f;     // degrees per second
        const float NudgeStep = 0.002f;      // the first, single step of a press: 2 mm
        const float NudgeTurnStep = 0.25f;   // and a quarter of a degree

        // the keyboard test (Ctrl + J)
        static readonly string[] TestKeys = { "G", "Q", "P", "Z", "M" };   // the middle, then the four corners of the letters
        bool testing;
        int testIndex;
        readonly List<Vector2> testTargets = new List<Vector2>();   // where each target key is (x along the rows, z away)
        readonly List<Vector2> testErrors = new List<Vector2>();    // pressed key minus target key, metres
        readonly Dictionary<string, Renderer> keyQuads = new Dictionary<string, Renderer>();
        Material flashMat;
        TextMesh testText;
        Renderer flashing;
        Material flashingWas;

        // the keys the test can recognise, with their labels on the drawn keyboard
        static readonly KeyValuePair<KeyCode, string>[] Recognised = BuildRecognised();

        static KeyValuePair<KeyCode, string>[] BuildRecognised()
        {
            var list = new List<KeyValuePair<KeyCode, string>>();
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++) list.Add(new KeyValuePair<KeyCode, string>(k, ((char)('A' + (k - KeyCode.A))).ToString()));
            for (int d = 0; d <= 9; d++) list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Alpha0 + d, d.ToString()));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Space, "Space"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Tab, "Tab"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Backspace, "Bksp"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Return, "Enter"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Minus, "-"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Equals, "="));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Comma, ","));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Period, "."));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.LeftBracket, "["));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.RightBracket, "]"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.Home, "Home"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.LeftArrow, "\u2190"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.RightArrow, "\u2192"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.UpArrow, "\u2191"));
            list.Add(new KeyValuePair<KeyCode, string>(KeyCode.DownArrow, "\u2193"));
            return list.ToArray();
        }

        /// <summary>The main-key cap with this label (not the number pad's), if the layout has one.</summary>
        static bool FindCap(string label, out KeyCap cap)
        {
            foreach (KeyCap k in Layout())
                if (k.Label == label && !k.Pad)
                {
                    cap = k;
                    return true;
                }
            cap = default(KeyCap);
            return false;
        }

        // one controller: the first press waits here for the second
        bool waitingForRight;
        Vector3 leftSide;
        float leftSideTime;

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

        void SetHere()
        {
            Transform room = Room;
            if (room == null || rig.head == null) return;
            Vector3 eyes = room.InverseTransformPoint(rig.head.position);
            Vector3 fwd = Flat(room.InverseTransformDirection(rig.head.forward));
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 guess = eyes + fwd * keyboardAhead + Vector3.down * keyboardBelow;

            // controllers lying on the desk around where the laptop should be
            var onDesk = new List<Vector3>();
            foreach (XRNode node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                InputDevice dev = InputDevices.GetDeviceAtXRNode(node);
                Vector3 cp;
                bool tracked;
                if (!dev.isValid || !dev.TryGetFeatureValue(CommonUsages.devicePosition, out cp)) continue;
                if (dev.TryGetFeatureValue(CommonUsages.isTracked, out tracked) && !tracked) continue;
                if (cp.y > eyes.y - 0.2f) continue;                  // in a hand near the face, not on the desk
                if (Vector3.Distance(cp, guess) > 0.7f) continue;    // somewhere else in the room
                onDesk.Add(cp);
            }

            if (onDesk.Count == 2 && Flat(onDesk[1] - onDesk[0]).magnitude > 0.2f)
            {
                FitSides(onDesk[0], onDesk[1], fwd);
                waitingForRight = false;
                Save("Laptop set from the controllers on each side. If the keys sit a little off, fine-tune with Ctrl + arrows");
                return;
            }
            if (onDesk.Count >= 1)
            {
                Vector3 c = onDesk[0];
                if (onDesk.Count == 2 && Vector3.Distance(onDesk[1], guess) < Vector3.Distance(c, guess)) c = onDesk[1];
                if (waitingForRight && Time.unscaledTime - leftSideTime < 90f && Flat(c - leftSide).magnitude > 0.2f)
                {
                    waitingForRight = false;
                    FitSides(leftSide, c, fwd);
                    Save("Laptop set from both sides. If the keys sit a little off, fine-tune with Ctrl + arrows");
                    return;
                }
                waitingForRight = true;
                leftSide = c;
                leftSideTime = Time.unscaledTime;
                Say("One side noted. Now lay the controller against the OTHER side of the laptop, level with the A S D F row, and press J");
                return;
            }

            // no controller on the desk: a guess from the head
            waitingForRight = false;
            posePosition = guess;
            poseYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            Save("Laptop marked from where you sit (a guess). Lay a controller against each side and press J, or fine-tune with Ctrl + arrows");
        }

        /// <summary>
        /// Ctrl + arrows (and Ctrl + Shift + up/down, Ctrl + [ ]) move the drawn laptop a little at a
        /// time, along its own rows, so the drawn keys can be laid exactly on the real ones.
        /// A tap moves 2 mm (or a quarter degree); holding keeps going.
        /// </summary>
        void Nudge()
        {
            if (!InputShim.Ctrl)
            {
                if (nudged)
                {
                    nudged = false;
                    Save("Keyboard position saved");
                }
                return;
            }
            bool shift = InputShim.Held(KeyCode.LeftShift) || InputShim.Held(KeyCode.RightShift);
            float dt = Time.unscaledDeltaTime;
            Quaternion yaw = Quaternion.Euler(0f, poseYaw, 0f);
            Vector3 across = yaw * Vector3.right, ahead = yaw * Vector3.forward;
            Vector3 move = Vector3.zero;
            float turn = 0f;

            move += across * Axis(KeyCode.RightArrow, KeyCode.LeftArrow, NudgeStep, NudgeSpeed * dt);
            float upDown = Axis(KeyCode.UpArrow, KeyCode.DownArrow, NudgeStep, NudgeSpeed * dt);
            if (shift) move += Vector3.up * upDown;
            else move += ahead * upDown;
            turn += Axis(KeyCode.RightBracket, KeyCode.LeftBracket, NudgeTurnStep, NudgeTurnSpeed * dt);

            if (move.sqrMagnitude < 1e-12f && Mathf.Abs(turn) < 1e-6f) return;
            if (Mathf.Abs(turn) > 0f)
            {
                // turn about the keyboard's middle, so it doesn't swing away
                poseYaw = Mathf.Repeat(poseYaw + turn + 180f, 360f) - 180f;
            }
            posePosition += move;
            hasPose = true;
            nudged = true;
            if (!nudgeHintShown)
            {
                nudgeHintShown = true;
                Say("Moving the keyboard: Ctrl + arrows, Ctrl + Shift + up/down for height, Ctrl + [ ] to turn. Let go of Ctrl to save");
            }
        }

        // ------------------------------------------------------------------ the keyboard test
        void StartTest()
        {
            if (marker == null) Build();
            if (marker == null) return;
            testing = true;
            testIndex = 0;
            testTargets.Clear();
            testErrors.Clear();
            HudStyle.MarkModal();   // from now on the keys only answer the test
            ShowTarget();
            Say("Keyboard test: press the real key under the flashing drawn key (Ctrl + J stops)");
        }

        void StopTest(string message)
        {
            testing = false;
            Unflash();
            if (testText != null) testText.gameObject.SetActive(false);
            if (message != null) Say(message);
        }

        void ShowTarget()
        {
            Unflash();
            string key = TestKeys[testIndex];
            Renderer r;
            if (keyQuads.TryGetValue(key, out r) && r != null)
            {
                flashing = r;
                flashingWas = r.sharedMaterial;
            }
            if (testText != null)
            {
                testText.text = "KEYBOARD TEST  " + (testIndex + 1) + " / " + TestKeys.Length +
                                "\nput a finger on the flashing key and press it     (Ctrl + J stops)";
                testText.gameObject.SetActive(true);
            }
        }

        void Unflash()
        {
            if (flashing != null && flashingWas != null) flashing.sharedMaterial = flashingWas;
            flashing = null;
            flashingWas = null;
        }

        void UpdateTest()
        {
            HudStyle.MarkModal();   // every frame: the simulation's own keys wait
            if (flashing != null && flashMat != null)
                flashing.sharedMaterial = (Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.3f) ? flashMat : flashingWas;

            string pressed = null;
            foreach (KeyValuePair<KeyCode, string> k in Recognised)
                if (InputShim.Down(k.Key))
                {
                    pressed = k.Value;
                    break;
                }
            if (pressed == null) return;

            KeyCap target, hit;
            if (!FindCap(TestKeys[testIndex], out target)) return;
            if (!FindCap(pressed, out hit))
            {
                Say("That key isn't on the drawn keyboard: try the flashing key again");
                return;
            }
            var error = new Vector2(hit.X - target.X, hit.Z - target.Z);
            if (error.magnitude > 4.5f * Pitch)
            {
                Say("That was " + pressed + ", far from the flashing key. Try again: rest a finger on the flashing key first");
                return;
            }
            testTargets.Add(new Vector2(target.X, target.Z));
            testErrors.Add(error);
            testIndex++;
            if (testIndex < TestKeys.Length)
            {
                Say(pressed == TestKeys[testIndex - 1] ? "Right key" : "You pressed " + pressed + " (wanted " + TestKeys[testIndex - 1] + ")");
                ShowTarget();
                return;
            }
            FinishTest();
        }

        /// <summary>
        /// Every miss says where the real keyboard is under the drawn one: at the drawn target the
        /// real key was 'pressed', so the real keyboard sits at minus the error. Move the drawing by
        /// the average of that, and turn it by how the front/back errors change from left to right.
        /// </summary>
        void FinishTest()
        {
            testing = false;
            Unflash();
            if (testText != null) testText.gameObject.SetActive(false);
            int n = testErrors.Count, matched = 0;
            float ex = 0f, ez = 0f, mx = 0f;
            for (int i = 0; i < n; i++)
            {
                if (testErrors[i].magnitude < 0.5f * Pitch) matched++;
                ex += testErrors[i].x;
                ez += testErrors[i].y;
                mx += testTargets[i].x;
            }
            if (n == 0) return;
            ex /= n;
            ez /= n;
            mx /= n;
            if (matched == n)
            {
                Say("Keyboard test: " + matched + " of " + n + " keys matched. It's lined up");
                return;
            }
            // the turn: fit the front/back error against where the key is along the rows
            float sxx = 0f, sxz = 0f;
            for (int i = 0; i < n; i++)
            {
                float dx = testTargets[i].x - mx;
                sxx += dx * dx;
                sxz += dx * (testErrors[i].y - ez);
            }
            float turnDeg = 0f;
            if (sxx > 1e-4f)
            {
                float slope = -sxz / sxx;   // how the real keyboard's rows run, in the drawing's frame
                turnDeg = -Mathf.Atan(slope) * Mathf.Rad2Deg;
                if (Mathf.Abs(turnDeg) < 0.5f) turnDeg = 0f;
            }
            Quaternion yaw = Quaternion.Euler(0f, poseYaw, 0f);
            posePosition += yaw * new Vector3(-ex, 0f, -ez);
            poseYaw = Mathf.Repeat(poseYaw + turnDeg + 180f, 360f) - 180f;
            string move = "";
            if (Mathf.Abs(ex) > 0.001f) move += Mathf.Abs(ex * 1000f).ToString("0") + " mm " + (ex > 0f ? "left" : "right");
            if (Mathf.Abs(ez) > 0.001f) move += (move.Length > 0 ? ", " : "") + Mathf.Abs(ez * 1000f).ToString("0") + " mm " + (ez > 0f ? "towards you" : "away");
            if (turnDeg != 0f) move += (move.Length > 0 ? ", " : "") + "turned " + Mathf.Abs(turnDeg).ToString("0.0") + " degrees";
            Save("Keyboard test: " + matched + " of " + n + " keys matched. Moved the drawing " + move + ". Ctrl + J tests it again");
        }

        /// <summary>+1 / -1 keys: a single step on the press, then steady movement while held.</summary>
        static float Axis(KeyCode plus, KeyCode minus, float step, float held)
        {
            float v = 0f;
            if (InputShim.Down(plus)) v += step;
            else if (InputShim.Held(plus)) v += held;
            if (InputShim.Down(minus)) v -= step;
            else if (InputShim.Held(minus)) v -= held;
            return v;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>
        /// The laptop between two controllers lying against its sides, level with the middle row:
        /// the middle of the keyboard is halfway between them, and the line between them runs along
        /// its rows. Which one is left doesn't matter; the keyboard faces the way you are facing.
        /// </summary>
        void FitSides(Vector3 a, Vector3 b, Vector3 facing)
        {
            Vector3 across = Flat(b - a).normalized;
            if (Vector3.Dot(across, Vector3.Cross(Vector3.up, facing)) < 0f) across = -across;   // left to right as you sit
            Vector3 ahead = Vector3.Cross(across, Vector3.up);   // away from you, along the desk
            posePosition = (a + b) * 0.5f + Vector3.down * besideDrop;
            poseYaw = Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
        }

        void Save(string message)
        {
            hasPose = true;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(KeyPose, posePosition.x.ToString("0.####", inv) + " " + posePosition.y.ToString("0.####", inv) + " " +
                                           posePosition.z.ToString("0.####", inv) + " " + poseYaw.ToString("0.##", inv));
            PlayerPrefs.SetInt(KeyHidden, 0);
            PlayerPrefs.Save();
            Say(message);
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
            if (headset && InputShim.Ctrl && InputShim.Down(KeyCode.J))
            {
                if (testing) StopTest("Keyboard test stopped");
                else if (hasPose && PlayerPrefs.GetInt(KeyHidden, 0) == 0) StartTest();
                else Say("Set the laptop first (J), then Ctrl + J tests it");
            }
            else if (testing)
            {
                if (!headset) StopTest(null);
                else UpdateTest();
            }
            else if (headset && !HudStyle.ModalActive && InputShim.Down(KeyCode.J))
            {
                bool shift = InputShim.Held(KeyCode.LeftShift) || InputShim.Held(KeyCode.RightShift);
                if (!shift) SetHere();
                else if (hasPose)
                {
                    bool hide = PlayerPrefs.GetInt(KeyHidden, 0) == 0;
                    PlayerPrefs.SetInt(KeyHidden, hide ? 1 : 0);
                    PlayerPrefs.Save();
                    Say(hide ? "Laptop keyboard hidden (Shift + J shows it)" : "Laptop keyboard shown");
                }
                else Say("No laptop set yet: lay a controller against each side of it and press J");
            }

            if (headset && hasPose && !testing && !HudStyle.ModalActive) Nudge();

            bool show = headset && hasPose && PlayerPrefs.GetInt(KeyHidden, 0) == 0 && Room != null;
            if (show && marker == null) Build();
            if (marker == null) return;
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
            if (!show) return;
            if (marker.parent != Room) marker.SetParent(Room, false);
            marker.localPosition = posePosition;
            marker.localRotation = Quaternion.Euler(0f, poseYaw, 0f);
            // a slow, gentle pulse on the outline so it reads as a guide, not part of the space scene
            Color c = color * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 2f));
            c.a = 1f;
            SetColor(mat, c);
        }

        // ------------------------------------------------------------------ the Legion 7 keyboard
        // Key pitch (a key and its gap) on the Legion's TrueStrike keyboard, and its number pad's narrower keys.
        const float Pitch = 0.0185f;
        const float PadKey = 0.85f;    // number pad key width, in keys
        const float PadGap = 0.3f;     // gap between the main keys and the number pad, in keys
        const float TotalKeys = 15f + PadGap + 4f * PadKey;   // 18.7 keys wide

        // back row first: label, width in keys. "" draws nothing (the space under a tall key). "|" = the gap.
        static readonly string[][] Rows =
        {
            new[] { "Esc", "1", "F1", "1", "F2", "1", "F3", "1", "F4", "1", "F5", "1", "F6", "1", "F7", "1", "F8", "1", "F9", "1", "F10", "1", "F11", "1", "F12", "1",
                    "PrtSc", "1", "Ins", "1", "Del", "1", "Home", "0.9", "End", "0.9", "PgUp", "0.9" },
            new[] { "`", "1", "1", "1", "2", "1", "3", "1", "4", "1", "5", "1", "6", "1", "7", "1", "8", "1", "9", "1", "0", "1", "-", "1", "=", "1", "Bksp", "2",
                    "|", "0.3", "Num", "0.85", "/", "0.85", "*", "0.85", "−", "0.85" },
            new[] { "Tab", "1.5", "Q", "1", "W", "1", "E", "1", "R", "1", "T", "1", "Y", "1", "U", "1", "I", "1", "O", "1", "P", "1", "[", "1", "]", "1", "\\", "1.5",
                    "|", "0.3", "7", "0.85", "8", "0.85", "9", "0.85", "+", "0.85" },
            new[] { "Caps", "1.75", "A", "1", "S", "1", "D", "1", "F", "1", "G", "1", "H", "1", "J", "1", "K", "1", "L", "1", ";", "1", "'", "1", "Enter", "2.25",
                    "|", "0.3", "4", "0.85", "5", "0.85", "6", "0.85", "", "0.85" },
            new[] { "Shift", "2.25", "Z", "1", "X", "1", "C", "1", "V", "1", "B", "1", "N", "1", "M", "1", ",", "1", ".", "1", "/", "1", "Shift", "0.75", "↑", "1", "", "1",
                    "|", "0.3", "1", "0.85", "2", "0.85", "3", "0.85", "Ent", "0.85" },
            new[] { "Ctrl", "1.25", "Fn", "1", "Win", "1", "Alt", "1", "Space", "5.25", "Alt", "1", "Ctrl", "1.5", "←", "1", "↓", "1", "→", "1",
                    "|", "0.3", "0", "1.7", ".", "0.85", "", "0.85" },
        };

        // the keys that do something in the simulation (the number pad's + and - change the speed too)
        const string Used = " Esc F1 Home 1 2 3 4 5 6 7 - = Bksp Tab R T O P [ ] S F G H J K L Z C V B N M , . Shift Ctrl Space ← → ↑ ↓ + − ";

        struct KeyCap
        {
            public string Label;
            public float X, Z, Width, Depth;   // metres, from the middle of the keyboard's middle row
            public bool Used;
            public bool Pad;
        }

        static List<KeyCap> layout;

        static List<KeyCap> Layout()
        {
            if (layout != null) return layout;
            layout = new List<KeyCap>();
            for (int r = 0; r < Rows.Length; r++)
            {
                // the middle row (r = 3, A S D F ...) sits on the origin; the short top row hugs the number row
                float z = (3 - r) * Pitch;
                float depth = Pitch;
                if (r == 0)
                {
                    z -= 0.2f * Pitch;
                    depth = 0.6f * Pitch;
                }
                string[] row = Rows[r];
                float x = -TotalKeys * 0.5f;
                bool pad = false;
                for (int i = 0; i + 1 < row.Length; i += 2)
                {
                    float width;
                    if (!float.TryParse(row[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out width) || width <= 0f)
                        width = 1f;
                    string label = row[i];
                    float cx = x + width * 0.5f;
                    x += width;
                    if (label == "|")
                    {
                        pad = true;
                        continue;
                    }
                    if (label.Length == 0) continue;
                    // the minus is "−" on the number pad and "-" on the main keys; both change the speed
                    bool used = Used.Contains(" " + label + " ") && !(pad && label == "/") && !(pad && label == ".");
                    if (pad && (label == "1" || label == "2" || label == "3" || label == "4" || label == "5" || label == "6" || label == "7")) used = false;
                    layout.Add(new KeyCap
                    {
                        Label = label,
                        X = cx * Pitch,
                        Z = z,
                        Width = width * Pitch - 0.003f,
                        Depth = depth - 0.003f,
                        Used = used,
                        Pad = pad,
                    });
                }
            }
            return layout;
        }

        // ------------------------------------------------------------------ building it
        void Build()
        {
            Shader sh = Shader.Find("EOSAT1/UnlitColor");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) return;
            mat = new Material(sh) { name = "EOSAT1 Laptop Marker (runtime)" };
            var lit = new Material(sh) { name = "EOSAT1 Laptop Key (used)" };
            var dim = new Material(sh) { name = "EOSAT1 Laptop Key" };
            SetColor(lit, new Color(0.05f, 0.35f, 0.42f, 1f));
            SetColor(dim, new Color(0.08f, 0.09f, 0.11f, 1f));
            keyMats = new[] { lit, dim };

            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (System.Exception) { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch (System.Exception) { }
            }

            marker = new GameObject("Real Laptop Marker (Legion 7)").transform;
            marker.SetParent(Room, false);
            float w = laptopSize.x, d = laptopSize.y, h = laptopSize.z, t = 0.005f;

            // the keyboard's back edge, and the hinge a little behind it
            float keysBack = 3.1f * Pitch;
            float back = keysBack + 0.028f;
            float keysFront = -2.5f * Pitch;
            Rect3(marker, new Vector3(0f, -0.004f, back - d * 0.5f), w, d, t);   // the base
            // the touchpad: centred under the space bar
            float spaceX = (-TotalKeys * 0.5f + 4.25f + 2.625f) * Pitch;
            float padDepth = 0.075f;
            Rect3(marker, new Vector3(spaceX, -0.004f, keysFront - 0.018f - padDepth * 0.5f), 0.12f, padDepth, t * 0.6f);

            var kb = new GameObject("Keyboard").transform;
            kb.SetParent(marker, false);
            foreach (KeyCap k in Layout()) Key(kb, f, k);

            flashMat = new Material(sh) { name = "EOSAT1 Laptop Key (test)" };
            SetColor(flashMat, new Color(1f, 0.72f, 0.2f, 1f));
            var tt = new GameObject("Keyboard Test Text");
            tt.transform.SetParent(marker, false);
            tt.transform.localPosition = new Vector3(0f, 0.035f, keysBack + 0.012f);   // upright, just behind the top row
            testText = tt.AddComponent<TextMesh>();
            if (f != null)
            {
                testText.font = f;
                tt.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            testText.fontSize = 48;
            testText.characterSize = 0.0024f;
            testText.anchor = TextAnchor.LowerCenter;
            testText.alignment = TextAlignment.Center;
            testText.color = new Color(1f, 0.8f, 0.35f, 1f);
            tt.SetActive(false);

            // the screen: hinged at the back edge, leaning back 15 degrees
            var screen = new GameObject("Screen").transform;
            screen.SetParent(marker, false);
            screen.localPosition = new Vector3(0f, 0f, back);
            screen.localRotation = Quaternion.Euler(-75f, 0f, 0f);
            Rect3(screen, new Vector3(0f, 0f, h * 0.5f), w, h, t);

            // where the headset's screens go when the controllers are off: on the laptop's screen,
            // and beside it on the left; turned to face you (forward = into the screen, up = up the lid)
            Quaternion facing = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            screenAnchor = new GameObject("Screen Anchor").transform;
            screenAnchor.SetParent(screen, false);
            screenAnchor.localPosition = new Vector3(0f, 0.004f, h * 0.5f);
            screenAnchor.localRotation = facing;
            sideAnchor = new GameObject("Side Anchor").transform;
            sideAnchor.SetParent(screen, false);
            sideAnchor.localPosition = new Vector3(-(w * 0.5f + 0.2f), 0.004f, h * 0.5f);
            sideAnchor.localRotation = facing;

            marker.gameObject.SetActive(false);
        }

        void Key(Transform parent, Font f, KeyCap k)
        {
            Quaternion flat = Quaternion.Euler(90f, 0f, 0f);   // lying on the deck, readable from above
            var pos = new Vector3(k.X, 0f, k.Z);
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Key " + k.Label;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localRotation = flat;
            q.transform.localScale = new Vector3(k.Width, k.Depth, 1f);
            Renderer r = q.GetComponent<Renderer>();
            r.sharedMaterial = keyMats[k.Used ? 0 : 1];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (!k.Pad && !keyQuads.ContainsKey(k.Label)) keyQuads[k.Label] = r;

            var g = new GameObject("Label");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos + new Vector3(0f, 0.001f, 0f);
            g.transform.localRotation = flat;
            var tm = g.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                g.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.text = k.Label;
            tm.fontSize = 48;
            tm.characterSize = k.Label.Length > 2 ? 0.001f : 0.0018f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = k.Used ? new Color(0.75f, 1f, 1f, 1f) : new Color(0.45f, 0.5f, 0.55f, 1f);
        }

        static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>A rectangle of thin bars lying in the local XZ plane (width along X, depth along Z).</summary>
        void Rect3(Transform parent, Vector3 centre, float width, float depth, float bar)
        {
            Bar(parent, centre + new Vector3(0f, 0f, -depth * 0.5f), new Vector3(width + bar, bar, bar));
            Bar(parent, centre + new Vector3(0f, 0f, depth * 0.5f), new Vector3(width + bar, bar, bar));
            Bar(parent, centre + new Vector3(-width * 0.5f, 0f, 0f), new Vector3(bar, bar, depth + bar));
            Bar(parent, centre + new Vector3(width * 0.5f, 0f, 0f), new Vector3(bar, bar, depth + bar));
        }

        void Bar(Transform parent, Vector3 pos, Vector3 size)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Edge";
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            Renderer r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (keyMats != null) foreach (Material m in keyMats) if (m != null) Destroy(m);
            if (flashMat != null) Destroy(flashMat);
        }
    }
}
