// EOSAT-1 VR - the headset's view on the laptop, with no headset: for screenshots, for videos, and
// for anyone at a demo who can't or doesn't want to wear the headset.
//
//   F9   switch it on / off (only while no headset is in use)
//
// The laptop's camera becomes a seated person's eyes at the spot the headset starts from: move the
// mouse to look around. Everything behaves as in the headset with its controllers switched off:
// the drawn Legion keyboard in front of you (keys glow as you type), C for the satellite camera
// on the drawn laptop's screen (twice for the full view), right-click for the VR menu (look and
// left-click), the status panel and the speed bar beside the laptop. The laptop's own control bar
// and status panel stay on top, as with a headset: Tab and H hide them for a clean picture.
// The drawn laptop sits where a seated person's keyboard would be; the position set with J in the
// headset is kept for the headset and not changed here.
//
// Adds itself to the scene's VR rig at runtime, like the speed slider.
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Eosat1
{
    [DefaultExecutionOrder(100)]   // after DesktopControls, so the camera is ours while previewing
    public sealed class VrPreview : MonoBehaviour
    {
        public VrRig rig;
        [Tooltip("Height of the eyes above the floor of the headset's play space, seated, metres.")]
        public float eyeHeight = 1.2f;
        [Tooltip("Degrees the view turns per pixel of mouse movement.")]
        public float lookSpeed = 0.12f;

        /// <summary>The preview is on: the laptop's camera shows the headset's view.</summary>
        public static bool Active { get; private set; }

        /// <summary>A headset is in use, or its view is being previewed: draw what the headset would.</summary>
        public static bool Headset { get { return XRSettings.isDeviceActive || Active; } }

        /// <summary>
        /// The headset's controllers are in use: switched on in Settings (K), and a real headset
        /// (the preview works like the controllers being off: mouse and keyboard).
        /// </summary>
        public static bool ControllersInUse { get { return MenuSettings.UseControllers && !Active; } }

        /// <summary>Where the eyes are in the headset's play space while previewing.</summary>
        public Vector3 EyeLocal { get { return new Vector3(0f, eyeHeight, 0f); } }

        public static VrPreview Current { get; private set; }

        float yaw, pitch = -28f;   // start looking down at the keyboard, with Earth beyond it
        Vector3 savedPosition;
        Quaternion savedRotation;

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
            if (r == null || r.GetComponent<VrPreview>() != null) return;
            // only in the simulation (the menu room has no satellite to look at)
            if (FindAnyObjectByType<EosatApp>() == null) return;
            r.gameObject.AddComponent<VrPreview>().rig = r;
        }

        void OnEnable()
        {
            Current = this;
        }

        void OnDisable()
        {
            if (Active) SetActive(false);
            if (Current == this) Current = null;
        }

        // ------------------------------------------------------------------ on and off
        void SetActive(bool on)
        {
            if (rig == null || rig.head == null) return;
            if (on)
            {
                savedPosition = rig.head.localPosition;
                savedRotation = rig.head.localRotation;
                yaw = 0f;
                pitch = -28f;
                Active = true;
                Say("Headset preview: move the mouse to look around. C camera, right-click menu, type to see keys glow. F9 leaves");
            }
            else
            {
                Active = false;
                rig.head.localPosition = savedPosition;
                rig.head.localRotation = savedRotation;
                Say("Headset preview off");
            }
        }

        void Say(string message)
        {
            if (rig != null && rig.app != null) rig.app.Say(message);
        }

        void Update()
        {
            if (XRSettings.isDeviceActive)
            {
                if (Active) SetActive(false);   // a real headset came on: it owns the view
                return;
            }
            if (!HudStyle.ModalActive && InputShim.Down(KeyCode.F9)) SetActive(!Active);
        }

        void LateUpdate()
        {
            if (!Active || rig == null || rig.head == null) return;
            // the mouse pointer is locked while previewing (as with the controllers off), so moving
            // the mouse turns the head; the VR menu's right-click and left-click work as in the headset
            Vector2 d = InputShim.MouseDelta();
            yaw += d.x * lookSpeed;
            pitch = Mathf.Clamp(pitch - d.y * lookSpeed, -85f, 70f);
            rig.head.localPosition = EyeLocal;
            rig.head.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
