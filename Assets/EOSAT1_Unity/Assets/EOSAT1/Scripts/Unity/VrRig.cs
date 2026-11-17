// EOSAT-1 VR - a light headset rig that needs no extra packages beyond XR Plug-in
// Management + OpenXR: it reads the headset and controllers through UnityEngine.XR.
// Walk around the spacecraft for real (it is life size), or fly with the jetpack.
//
// Left stick   fly forwards / back / sideways   Right stick  left/right snap turn,
//                                                             up/down jetpack up / down
// A            next solar panel / free its jam  B            antenna / free its jam
// X            status panel on/off              Y            time 1x -> 10x -> 60x
// Left grip    pause                            MENU (left)  the VR menu: every control
//                                                            explained, and a button for each
// Right trigger body axes                       Left trigger camera line
//   (while the VR menu is open, either trigger presses the button you point at)
// Right grip   whole Earth / back               Right stick click  satellite camera screen
// Left stick click  orbit path on/off
// Commands only act in Sandbox mode; with the simulator they come from mission control.
//
// In the simulation the rig also carries space gloves (VrHands), the VR menu and messages
// (VrMenu) and the jetpack (VrJetpack). It adds them by itself at start, so an existing
// scene needs no rebuild. The main menu room uses the same rig with the gloves only.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Eosat1
{
    public sealed class VrRig : MonoBehaviour
    {
        public EosatApp app;
        public InfoPanel infoPanel;
        [Tooltip("Extra views (whole Earth, satellite camera, orbit path). Found at start if left empty.")]
        public ViewDirector views;
        [Tooltip("Parent of the tracked head and hands; lifted to eye height if the runtime has no floor origin.")]
        public Transform cameraOffset;
        public Transform head;
        public Transform leftHand;
        public Transform rightHand;

        [Tooltip("Walking speed with the left stick where there is no jetpack (the main menu room). In the simulation the jetpack sets the speeds.")]
        public float moveSpeed = 0.7f;
        [Tooltip("Rise / sink speed with the right stick where there is no jetpack. 0 = stay on the floor.")]
        public float verticalSpeed = 0.5f;
        public float snapTurnDegrees = 30f;
        public float fallbackEyeHeight = 1.5f;
        public float deadZone = 0.25f;
        [Tooltip("Show space-suit gloves instead of the plain controller blocks.")]
        public bool spaceGloves = true;

        public VrMenu Menu { get { return menu; } }
        public VrJetpack Jetpack { get { return jetpack; } }

        /// <summary>
        /// Does the runtime report the controller's "grip" pose (OpenXR: the handle along Z) rather
        /// than a pose that points forwards? Worked out by comparing it with the aim pose once a
        /// controller is tracked; until then, guessed from the runtime's name.
        /// </summary>
        public bool DevicePoseIsGrip
        {
            get
            {
                if (poseKnown) return poseIsGrip;
                string device = XRSettings.loadedDeviceName;
                return string.IsNullOrEmpty(device) || device.ToLowerInvariant().Contains("openxr");
            }
        }

        sealed class Aim
        {
            public bool Found;
            public float NextLook;
            public InputFeatureUsage<Vector3> Position;
            public InputFeatureUsage<Quaternion> Rotation;
        }

        readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        readonly List<InputFeatureUsage> usages = new List<InputFeatureUsage>();
        readonly Aim aimLeft = new Aim(), aimRight = new Aim();
        VrHands hands;
        VrMenu menu;
        VrJetpack jetpack;
        Vector3 homePosition;
        Quaternion homeRotation;
        float nextOriginCheck;
        bool floorConfirmed;
        bool turnLatched;
        bool poseKnown, poseIsGrip;
        bool prevA, prevB, prevX, prevY, prevTrigR, prevTrigL, prevMenu, prevGripL;
        bool prevGripR, prevStickR, prevStickL;

        void Start()
        {
            if (MenuSettings.LaunchedFromMenu) snapTurnDegrees = MenuSettings.SnapTurnDegrees;
            homePosition = transform.position;
            homeRotation = transform.rotation;

            if (spaceGloves && (leftHand != null || rightHand != null))
            {
                hands = GetComponent<VrHands>();
                if (hands == null) hands = gameObject.AddComponent<VrHands>();
                hands.rig = this;
                if (hands.leftHand == null) hands.leftHand = leftHand;
                if (hands.rightHand == null) hands.rightHand = rightHand;
            }
            // the simulation only: the jetpack, and the menu you pull up with the MENU button
            if (app != null)
            {
                jetpack = GetComponent<VrJetpack>();
                if (jetpack == null) jetpack = gameObject.AddComponent<VrJetpack>();
                jetpack.rig = this;
                menu = GetComponent<VrMenu>();
                if (menu == null) menu = gameObject.AddComponent<VrMenu>();
                menu.rig = this;
            }
        }

        void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
        }

        void Update()
        {
            bool active = XRSettings.isDeviceActive;
            SetActive(leftHand, active);
            SetActive(rightHand, active);
            if (!active)
            {
                floorConfirmed = false;
                return;
            }
            EnsureFloorOrigin();
            UpdatePoses();
            if (!poseKnown)
            {
                // find out which kind of pose the controllers report (the gloves need to know)
                Ray probe;
                if (!TryGetPointerRay(true, out probe)) TryGetPointerRay(false, out probe);
            }
            Locomotion();
            Buttons();
        }

        [BeforeRenderOrder(100)]
        void OnBeforeRender()
        {
            if (XRSettings.isDeviceActive) UpdatePoses();
        }

        static void SetActive(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        void EnsureFloorOrigin()
        {
            if (floorConfirmed || Time.unscaledTime < nextOriginCheck) return;
            nextOriginCheck = Time.unscaledTime + 1f;
            SubsystemManager.GetSubsystems(subsystems);
            bool floor = false;
            for (int i = 0; i < subsystems.Count; i++)
            {
                XRInputSubsystem s = subsystems[i];
                if (s == null || !s.running) continue;
                if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                {
                    if (s.GetTrackingOriginMode() != TrackingOriginModeFlags.Floor)
                        s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    if (s.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor) floor = true;
                }
            }
            if (cameraOffset != null) cameraOffset.localPosition = new Vector3(0f, floor ? 0f : fallbackEyeHeight, 0f);
            floorConfirmed = floor;
        }

        void UpdatePoses()
        {
            InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (hmd.isValid && head != null)
            {
                Vector3 p;
                Quaternion q;
                if (hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out p) ||
                    hmd.TryGetFeatureValue(CommonUsages.devicePosition, out p))
                    head.localPosition = p;
                if (hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out q) ||
                    hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out q))
                    head.localRotation = q;
            }
            TrackHand(XRNode.LeftHand, leftHand);
            TrackHand(XRNode.RightHand, rightHand);
        }

        static void TrackHand(XRNode node, Transform t)
        {
            if (t == null) return;
            InputDevice d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return;
            Vector3 p;
            Quaternion q;
            if (d.TryGetFeatureValue(CommonUsages.devicePosition, out p)) t.localPosition = p;
            if (d.TryGetFeatureValue(CommonUsages.deviceRotation, out q)) t.localRotation = q;
        }

        // ------------------------------------------------------------------ pointing
        /// <summary>
        /// The ray a controller points along, in world space, for pressing buttons on the VR
        /// menus. Uses the runtime's aim pose when it has one (the direction the controller is
        /// made to point); otherwise the tracked pose's forward direction.
        /// </summary>
        public bool TryGetPointerRay(bool rightSide, out Ray ray)
        {
            ray = new Ray(Vector3.zero, Vector3.forward);
            InputDevice d = InputDevices.GetDeviceAtXRNode(rightSide ? XRNode.RightHand : XRNode.LeftHand);
            if (!d.isValid) return false;
            Vector3 p;
            Quaternion q;
            if (TryAimPose(d, rightSide ? aimRight : aimLeft, out p, out q))
            {
                Transform space = cameraOffset != null ? cameraOffset : transform;
                ray = new Ray(space.TransformPoint(p), space.rotation * q * Vector3.forward);
                return true;
            }
            Transform hand = rightSide ? rightHand : leftHand;
            if (hand == null) return false;
            ray = new Ray(hand.position, hand.forward);
            return true;
        }

        bool TryAimPose(InputDevice d, Aim aim, out Vector3 p, out Quaternion q)
        {
            p = Vector3.zero;
            q = Quaternion.identity;
            if (!aim.Found && Time.unscaledTime >= aim.NextLook)
            {
                // the aim pose has no fixed name in UnityEngine.XR, so look for it among what the controller offers
                aim.NextLook = Time.unscaledTime + 2f;
                usages.Clear();
                if (d.TryGetFeatureUsages(usages))
                {
                    bool hasPosition = false, hasRotation = false;
                    for (int i = 0; i < usages.Count; i++)
                    {
                        string name = usages[i].name;
                        if (string.IsNullOrEmpty(name)) continue;
                        string low = name.ToLowerInvariant();
                        if (low == "pointerposition" && usages[i].type == typeof(Vector3))
                        {
                            aim.Position = new InputFeatureUsage<Vector3>(name);
                            hasPosition = true;
                        }
                        else if (low == "pointerrotation" && usages[i].type == typeof(Quaternion))
                        {
                            aim.Rotation = new InputFeatureUsage<Quaternion>(name);
                            hasRotation = true;
                        }
                    }
                    aim.Found = hasPosition && hasRotation;
                }
            }
            if (!aim.Found) return false;
            if (!d.TryGetFeatureValue(aim.Position, out p) || !d.TryGetFeatureValue(aim.Rotation, out q)) return false;
            // an all-zero rotation means the pose is not tracked yet
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 0.5f) return false;

            if (!poseKnown)
            {
                // the two poses nearly agree: the tracked pose already points forwards.
                // They differ by a good angle: it is the grip pose (the handle's own axes).
                Quaternion device;
                if (d.TryGetFeatureValue(CommonUsages.deviceRotation, out device) &&
                    device.x * device.x + device.y * device.y + device.z * device.z + device.w * device.w > 0.5f)
                {
                    poseIsGrip = Vector3.Angle(device * Vector3.forward, q * Vector3.forward) > 20f;
                    poseKnown = true;
                }
            }
            return true;
        }

        /// <summary>A short buzz on one controller.</summary>
        public static void Buzz(bool rightSide, float strength, float seconds)
        {
            InputDevice d = InputDevices.GetDeviceAtXRNode(rightSide ? XRNode.RightHand : XRNode.LeftHand);
            if (d.isValid) d.SendHapticImpulse(0u, Mathf.Clamp01(strength), seconds);
        }

        // ------------------------------------------------------------------ moving
        /// <summary>Back to where you started, facing the spacecraft.</summary>
        public void ReturnHome()
        {
            transform.SetPositionAndRotation(homePosition, homeRotation);
            if (jetpack != null) jetpack.Stop();
        }

        void Locomotion()
        {
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            float dt = Time.unscaledDeltaTime;
            Vector2 stick;
            Vector2 move = Vector2.zero;
            float climb = 0f;
            if (left.isValid && left.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick) && stick.magnitude > deadZone)
                move = stick;
            if (right.isValid && right.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick))
            {
                if (Mathf.Abs(stick.x) > 0.7f)
                {
                    if (!turnLatched && head != null)
                    {
                        transform.RotateAround(head.position, Vector3.up, Mathf.Sign(stick.x) * snapTurnDegrees);
                        turnLatched = true;
                    }
                }
                else if (Mathf.Abs(stick.x) < 0.3f)
                {
                    turnLatched = false;
                }
                if (Mathf.Abs(stick.y) > deadZone && Mathf.Abs(stick.x) < 0.5f) climb = stick.y;
            }
            if (head == null) return;

            if (jetpack != null)
            {
                jetpack.Fly(move, climb, dt);
                return;
            }
            // no jetpack (the main menu room): plain walking
            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Vector3 pos = transform.position + (fwd * move.y + side * move.x) * moveSpeed * dt;
            if (verticalSpeed > 0f) pos.y = Mathf.Clamp(pos.y + climb * verticalSpeed * dt, -3f, 3f);
            transform.position = pos;
        }

        // ------------------------------------------------------------------ buttons
        void Buttons()
        {
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool a = Pressed(right, CommonUsages.primaryButton, ref prevA);
            bool b = Pressed(right, CommonUsages.secondaryButton, ref prevB);
            bool x = Pressed(left, CommonUsages.primaryButton, ref prevX);
            bool y = Pressed(left, CommonUsages.secondaryButton, ref prevY);
            bool triggerR = Pressed(right, CommonUsages.triggerButton, ref prevTrigR);
            bool triggerL = Pressed(left, CommonUsages.triggerButton, ref prevTrigL);
            bool menuButton = Pressed(left, CommonUsages.menuButton, ref prevMenu);
            bool gripL = Pressed(left, CommonUsages.gripButton, ref prevGripL);
            bool gripR = Pressed(right, CommonUsages.gripButton, ref prevGripR);
            bool stickR = Pressed(right, CommonUsages.primary2DAxisClick, ref prevStickR);
            bool stickL = Pressed(left, CommonUsages.primary2DAxisClick, ref prevStickL);

            if (menuButton && menu != null) menu.Toggle();
            if (menu != null && menu.IsOpen)
            {
                // the triggers press the button you point at
                if (triggerR) menu.Click(true);
                else if (triggerL) menu.Click(false);
            }
            else if (app != null)
            {
                if (triggerR)
                {
                    if (views != null) views.ToggleBodyAxes();
                    else app.ToggleAxes();
                }
                if (triggerL)
                {
                    if (views != null) views.ToggleCameraLine();
                    else app.ToggleBoresight();
                }
            }

            if (a && app != null) app.DeployNextWing();
            if (b && app != null) app.DeployAntennaOrClearJam();
            if (x && infoPanel != null)
            {
                infoPanel.Toggle();
                if (app != null) app.Say(infoPanel.showOnScreen ? "Status panel: on your left controller" : "Status panel hidden");
            }
            if (y && app != null) app.CycleSpeedCoarse();
            if (gripL && app != null) app.TogglePause();
            if (gripR && views != null && !SpeedSlider.HoldsRightGrip) views.ToggleOverview();   // not while sliding the speed bar
            if (stickR && views != null) views.CycleSatCamera();
            if (stickL && views != null) views.TogglePath();
        }

        static bool Pressed(InputDevice d, InputFeatureUsage<bool> usage, ref bool previous)
        {
            bool now = false;
            bool value;
            if (d.isValid && d.TryGetFeatureValue(usage, out value)) now = value;
            bool edge = now && !previous;
            previous = now;
            if (edge) d.SendHapticImpulse(0u, 0.35f, 0.05f);
            return edge;
        }
    }
}
