// EOSAT-1 VR - shows where the real laptop (and its keyboard) is while you wear the headset.
//
// Set it once: sit at the laptop with the headset on, hands on the keyboard, look at the
// laptop's screen, and press J. A glowing outline of a laptop (keyboard and screen) is then
// drawn where the real one is, with a "LAPTOP" label, in the headset only. It stays put in the
// real room however you fly, turn or jump between viewpoints, because it hangs on the
// headset's tracking space (the room), not on the virtual world.
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
        Material mat;
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
            hasPose = true;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(KeyPose, posePosition.x.ToString("0.###", inv) + " " + posePosition.y.ToString("0.###", inv) + " " +
                                           posePosition.z.ToString("0.###", inv) + " " + poseYaw.ToString("0.#", inv));
            PlayerPrefs.SetInt(KeyHidden, 0);
            PlayerPrefs.Save();
            Say("Laptop marked: its outline now shows where the real keyboard is (Shift + J hides it)");
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

            // keyboard deck: a rectangle on the desk, centred on the marker, front edge towards you
            Rect3(marker, Vector3.zero, Quaternion.identity, w, d, t);
            // the keys: a smaller rectangle inside it
            Rect3(marker, new Vector3(0f, 0f, 0.02f), Quaternion.identity, w * 0.85f, d * 0.45f, t * 0.6f);
            // the screen: hinged at the back edge, leaning back 15 degrees
            var screen = new GameObject("Screen").transform;
            screen.SetParent(marker, false);
            screen.localPosition = new Vector3(0f, 0f, d * 0.5f);
            screen.localRotation = Quaternion.Euler(-75f, 0f, 0f);
            Rect3(screen, new Vector3(0f, 0f, h * 0.5f), Quaternion.identity, w, h, t);

            // label above the screen
            var label = new GameObject("Label");
            label.transform.SetParent(marker, false);
            label.transform.localPosition = new Vector3(0f, h + 0.08f, d * 0.5f);
            var tm = label.AddComponent<TextMesh>();
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (System.Exception) { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch (System.Exception) { }
            }
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
            marker.gameObject.SetActive(false);
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
        }
    }
}
