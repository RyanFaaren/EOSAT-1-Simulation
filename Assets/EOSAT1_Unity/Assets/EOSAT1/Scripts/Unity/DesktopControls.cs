// EOSAT-1 VR - mouse and keyboard, for working without a headset (and for the
// instructor's screen). Orbits the camera around the spacecraft. While a headset is
// active the headset moves the camera, but the keyboard shortcuts still work, and V / F
// move the person in the headset to the same viewpoints (VrRig.GoToViewpoint).
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public sealed class DesktopControls : MonoBehaviour
    {
        public EosatApp app;
        public InfoPanel infoPanel;
        public Transform cameraTransform;
        [Tooltip("What the camera orbits (the spacecraft).")]
        public Transform target;

        public float distance = 3.4f;
        public float minDistance = 1.0f;
        public float maxDistance = 20f;
        public float degreesPerPixel = 0.25f;

        // preset views around the 1.4 m EOS SAT-1: yaw, pitch (degrees), distance (m).
        // In the default "Earth below" frame with the camera pointed at Earth, the solar
        // panels face you from behind (-Z) and the cameras look down.
        static readonly Vector3[] Views =
        {
            new Vector3(-22f, -28f, 3.4f),  // like the reference picture: from behind and below
            new Vector3(-30f, 14f, 3.6f),   // behind and to the left, Earth below
            new Vector3(90f, 6f, 3.6f),     // from the side: the wings span towards you
            new Vector3(0f, 78f, 4.0f),     // from above: the separation ring and star cameras
            new Vector3(28f, -62f, 3.0f),   // from below: the two DragonEye cameras
            new Vector3(180f, 10f, 3.6f),   // from ahead
            new Vector3(35f, 22f, 8.0f),    // wide
        };
        static readonly string[] ViewNames =
        {
            "like the reference picture, from behind and below",
            "behind and to the left",
            "from the side",
            "from above",
            "from below, under the two cameras",
            "from ahead",
            "wide",
        };

        /// <summary>How many preset views V steps through.</summary>
        public static int ViewCount { get { return Views.Length; } }

        /// <summary>Preset view i: x = yaw, y = pitch (degrees, the camera's), z = distance from the spacecraft (m).</summary>
        public static Vector3 ViewPreset(int i)
        {
            return Views[((i % Views.Length) + Views.Length) % Views.Length];
        }

        public static string ViewName(int i)
        {
            return ViewNames[((i % ViewNames.Length) + ViewNames.Length) % ViewNames.Length];
        }

        VrRig rig;

        float yaw, pitch;
        int view;
        bool dragging;   // a drag that began on the picture, not on a panel or button

        // whole-Earth view: the camera orbits the Earth globe instead of the spacecraft
        bool overview;
        Vector3 overviewFocus;
        float overviewRadius = 0.5f;
        float overviewYaw = -35f, overviewPitch = 20f, overviewDistance;

        /// <summary>Set while something else uses the mouse wheel (the satellite camera's zoom).</summary>
        public bool WheelBusy { get; set; }

        /// <summary>Switch between orbiting the spacecraft and orbiting the whole Earth (a globe of the given radius).</summary>
        public void SetOverview(bool on, Vector3 focusWorld, float earthRadius)
        {
            bool entering = on && !overview;
            overview = on;
            overviewFocus = focusWorld;
            overviewRadius = Mathf.Max(0.01f, earthRadius);
            // far enough that the Earth and the whole orbit fit on screen with room to spare
            if (entering || overviewDistance <= 0f) overviewDistance = overviewRadius * 3.6f;
        }

        void Start()
        {
            SetView(0);
            rig = FindAnyObjectByType<VrRig>();
        }

        void SetView(int i)
        {
            view = (i % Views.Length + Views.Length) % Views.Length;
            yaw = Views[view].x;
            pitch = Views[view].y;
            distance = Views[view].z;
        }

        void LateUpdate()
        {
            // camera (the headset owns the camera when one is active)
            if (!VrPreview.Headset && cameraTransform != null && target != null)
            {
                bool blocked = HudStyle.PointerBlocked;
                float scroll = WheelBusy || blocked ? 0f : InputShim.Scroll();
                if (InputShim.MouseDown(0) || InputShim.MouseDown(1)) dragging = !blocked;
                else if (!InputShim.MouseHeld(0) && !InputShim.MouseHeld(1)) dragging = false;
                if (overview)
                {
                    if (dragging)
                    {
                        Vector2 d = InputShim.MouseDelta();
                        overviewYaw += d.x * degreesPerPixel;
                        overviewPitch = Mathf.Clamp(overviewPitch - d.y * degreesPerPixel, -89f, 89f);
                    }
                    // from just above the surface out to beyond the Moon (60 Earth radii away)
                    if (Mathf.Abs(scroll) > 0.001f)
                        overviewDistance = Mathf.Clamp(overviewDistance * Mathf.Pow(0.88f, scroll), overviewRadius * 1.25f, overviewRadius * 90f);
                    Quaternion orot = Quaternion.Euler(overviewPitch, overviewYaw, 0f);
                    cameraTransform.position = overviewFocus - orot * Vector3.forward * overviewDistance;
                    cameraTransform.rotation = orot;
                }
                else
                {
                if (dragging)
                {
                    Vector2 d = InputShim.MouseDelta();
                    yaw += d.x * degreesPerPixel;
                    pitch = Mathf.Clamp(pitch - d.y * degreesPerPixel, -89f, 89f);
                }
                if (Mathf.Abs(scroll) > 0.001f) distance = Mathf.Clamp(distance * Mathf.Pow(0.88f, scroll), minDistance, maxDistance);
                if (InputShim.Held(KeyCode.LeftArrow) && InputShim.Held(KeyCode.LeftShift)) yaw -= 60f * Time.unscaledDeltaTime;
                if (InputShim.Held(KeyCode.RightArrow) && InputShim.Held(KeyCode.LeftShift)) yaw += 60f * Time.unscaledDeltaTime;
                if (InputShim.Down(KeyCode.V)) SetView(view + 1);
                if (InputShim.Down(KeyCode.F)) SetView(0);

                Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
                cameraTransform.position = target.position - rot * Vector3.forward * distance;
                cameraTransform.rotation = rot;
                }
            }

            // with someone in the headset, V and F take them to the same viewpoints
            if (VrPreview.Headset && rig != null && !HudStyle.ModalActive)
            {
                if (InputShim.Down(KeyCode.V)) rig.NextViewpoint();
                if (InputShim.Down(KeyCode.F)) rig.FirstViewpoint();
            }

            // back to the main menu (when the menu scene is in the build list)
            if (InputShim.Down(KeyCode.Escape) && DesktopHud.EscapeFrame != Time.frameCount
                && Application.CanStreamedLevelBeLoaded(MenuSettings.MenuSceneName))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(MenuSettings.MenuSceneName);
                return;
            }

            // keyboard shortcuts work with or without a headset (instructor at the PC)
            if (app == null || HudStyle.ModalActive) return;   // the help card is open: keys wait
            bool shift = InputShim.Held(KeyCode.LeftShift) || InputShim.Held(KeyCode.RightShift);

            // time
            if (InputShim.Down(KeyCode.Space)) app.TogglePause();
            if (InputShim.Down(KeyCode.Equals) || InputShim.Down(KeyCode.KeypadPlus) || InputShim.Down(KeyCode.Period)) app.SpeedUp();
            if (InputShim.Down(KeyCode.Minus) || InputShim.Down(KeyCode.KeypadMinus) || InputShim.Down(KeyCode.Comma)) app.SlowDown();
            bool ctrl = InputShim.Ctrl;   // Ctrl + arrows move the laptop drawn in the headset (LaptopMarker)
            if (!shift && !ctrl && InputShim.Down(KeyCode.RightArrow)) app.JumpSeconds(60);
            if (!shift && !ctrl && InputShim.Down(KeyCode.LeftArrow)) app.JumpSeconds(-60);
            if (InputShim.Down(KeyCode.Backspace) || InputShim.Down(KeyCode.Home)) app.Restart();

            // commands (sandbox)
            if (InputShim.Down(KeyCode.Alpha1)) app.Command(SandboxCommand.DeployWingMinusY);
            if (InputShim.Down(KeyCode.Alpha2)) app.Command(SandboxCommand.DeployWingPlusY);
            if (InputShim.Down(KeyCode.Alpha3)) app.Command(SandboxCommand.DeployAntenna);
            if (InputShim.Down(KeyCode.Alpha4)) app.Command(SandboxCommand.ToggleAntennaJam);
            if (InputShim.Down(KeyCode.Alpha5)) app.Command(SandboxCommand.ToggleWingJam);
            if (InputShim.Down(KeyCode.Alpha6)) app.Command(SandboxCommand.StowWings);
            if (InputShim.Down(KeyCode.Alpha7)) app.Command(SandboxCommand.StowAntenna);
            if (InputShim.Down(KeyCode.N)) app.Command(SandboxCommand.AttitudeNadir);
            if (InputShim.Down(KeyCode.S)) app.Command(SandboxCommand.AttitudeSun);
            if (InputShim.Down(KeyCode.R)) app.Command(SandboxCommand.AttitudeRollImaging);
            if (InputShim.Down(KeyCode.T)) app.Command(SandboxCommand.AttitudeTumble);

            // overlays and settings
            if (InputShim.Down(KeyCode.G)) app.ToggleAxes();
            if (InputShim.Down(KeyCode.B)) app.ToggleBoresight();
            if (InputShim.Down(KeyCode.L)) app.ToggleViewFrame();
            if (InputShim.Down(KeyCode.M)) app.CycleFeed();
            if (InputShim.Down(KeyCode.K))
            {
                MenuSettings.UseControllers = !MenuSettings.UseControllers;
                MenuSettings.Save();
                app.Say(MenuSettings.UseControllers ? "Headset controllers ON"
                                                    : "Headset controllers OFF: the headset only looks around, use the mouse and keyboard (K turns them back on)");
            }
            if (InputShim.Down(KeyCode.H) && infoPanel != null) infoPanel.Toggle();   // F1 = help card, Tab = control bar (DesktopHud)
        }
    }
}
