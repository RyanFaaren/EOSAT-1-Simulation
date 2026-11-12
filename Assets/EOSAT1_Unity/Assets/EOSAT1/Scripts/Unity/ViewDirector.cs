// EOSAT-1 VR - three extra ways of looking at the mission:
//
//   Satellite camera (C)  what the DragonEye imagers see: a camera on the camera face
//                         looking along the boresight (body +Z), flight direction at the top
//                         of the picture. Desktop: off -> small picture in the corner ->
//                         full screen. Headset: a screen on the right controller.
//                         Mouse wheel (full screen) or [ ] zooms, down to the real field of
//                         view: the two imagers together cover a 44 km wide strip from 500 km.
//   Whole Earth (Z)       zooms out: the Earth as a globe with the spacecraft going round it
//                         and the Moon at its true distance. Everything is to scale except
//                         the spacecraft itself, which is drawn bigger so it can be seen.
//   Orbit path (O)        the line the spacecraft follows (see OrbitPathView).
//
// Desktop also gets three buttons at the top right. Headset: right grip = whole Earth,
// right stick click = camera screen, left stick click = orbit path.
//
// EosatApp adds this component by itself at start, and it builds what it needs (camera,
// path, screen, marker) at run time, so an existing scene needs no rebuild.
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public enum SatCameraDisplay { Off = 0, Inset = 1, Full = 2 }

    [DefaultExecutionOrder(-40)]
    public sealed class ViewDirector : MonoBehaviour
    {
        /// <summary>Objects on this layer are not drawn by the satellite camera (overlays, floor, controllers).</summary>
        public const int HiddenFromSatCameraLayer = 30;
        /// <summary>The two DragonEye imagers side by side: 2 x 22.2 km from 500 km (EOS Data Analytics, eoPortal).</summary>
        public const float SwathKm = 44.4f;
        public const float SwathAltitudeKm = 500f;

        public EosatApp app;
        public SpaceScene space;
        public SatelliteView satelliteView;
        public DesktopControls desktop;
        public VrRig rig;

        [Header("Satellite camera")]
        [Tooltip("Where the view camera sits, in body axes (mm): on the camera face between the two imagers, just clear of the hoods.")]
        public Vector3 cameraPositionBodyMm = new Vector3(-70f, -20f, 600f);
        [Tooltip("Width of the view in degrees. The real instrument sees about 5 degrees (44 km from 500 km).")]
        public float fieldOfViewDeg = 45f;
        public float maxFieldOfViewDeg = 100f;
        public SatCameraDisplay display = SatCameraDisplay.Off;

        [Header("Orbit path")]
        public bool pathVisible = true;

        public bool Overview { get { return space != null && space.overview; } }
        public static float TrueFieldOfViewDeg
        {
            get { return 2f * Mathf.Atan(0.5f * SwathKm / SwathAltitudeKm) * Mathf.Rad2Deg; }
        }

        Camera satCamera;
        RenderTexture target;
        OrbitPathView path;
        GameObject vrScreen;
        Material vrScreenMaterial;
        Mesh vrScreenMesh, markerMesh;
        bool ready;
        GUIStyle buttonStyle, buttonOnStyle, labelStyle;
        int styleScreenHeight;
        readonly Rect[] buttonRects = new Rect[3];
        string cameraLabel = "";
        float nextLabel;

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ setting up
        void Start()
        {
            if (app == null) app = GetComponent<EosatApp>();
            if (app == null) app = FindAnyObjectByType<EosatApp>();
            if (app == null) return;
            if (space == null) space = app.space;
            if (satelliteView == null) satelliteView = app.satelliteView;
            if (desktop == null) desktop = GetComponent<DesktopControls>();
            if (desktop == null) desktop = FindAnyObjectByType<DesktopControls>();
            if (rig == null) rig = FindAnyObjectByType<VrRig>();
            if (space == null || space.satellite == null) return;
            if (rig != null && rig.views == null) rig.views = this;

            BuildPath();
            BuildMarker();
            BuildSatCamera();
            BuildVrScreen();
            HideOverlaysFromSatCamera();
            ready = true;
            ApplyState();
        }

        void OnDestroy()
        {
            if (satCamera != null) satCamera.targetTexture = null;
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }
            if (vrScreenMaterial != null) Destroy(vrScreenMaterial);
            if (vrScreenMesh != null) Destroy(vrScreenMesh);
            if (markerMesh != null) Destroy(markerMesh);
        }

        void BuildPath()
        {
            var go = new GameObject("Orbit Path");
            go.transform.SetParent(space.transform, false);
            go.layer = HiddenFromSatCameraLayer;
            path = go.AddComponent<OrbitPathView>();
            path.app = app;
            path.space = space;
        }

        void BuildMarker()
        {
            // a bright dot on the spacecraft in the whole-Earth view, so it can be found in eclipse too
            if (space.overviewMarker != null) return;
            var go = new GameObject("Spacecraft Marker");
            go.transform.SetParent(space.transform, false);
            go.layer = HiddenFromSatCameraLayer;
            markerMesh = ProceduralMeshes.UvSphere(16, 8, "EOSAT1 Marker");
            go.AddComponent<MeshFilter>().sharedMesh = markerMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Shader sh = Shader.Find("EOSAT1/UnlitColor");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh) { name = "EOSAT1 Marker (runtime)" };
            Color c = new Color(1f, 0.85f, 0.25f);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            mr.sharedMaterial = mat;
            go.transform.localScale = Vector3.one * (0.012f * Mathf.Max(0.05f, space.overviewEarthRadius));
            go.SetActive(false);
            space.overviewMarker = go.transform;
        }

        void BuildSatCamera()
        {
            var go = new GameObject("DragonEye View Camera");
            go.transform.SetParent(space.satellite, false);
            // body (x, y, z) mm -> Unity local (x, z, y) m, the same swap as SimToUnity
            go.transform.localPosition = new Vector3(cameraPositionBodyMm.x, cameraPositionBodyMm.z, cameraPositionBodyMm.y) * 0.001f;
            // look along body +Z (Unity local +Y) with the flight direction, body +X, at the top
            go.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.right);
            satCamera = go.AddComponent<Camera>();
            satCamera.clearFlags = CameraClearFlags.SolidColor;
            satCamera.backgroundColor = Color.black;
            satCamera.nearClipPlane = 0.3f;
            satCamera.farClipPlane = 6000f;
            satCamera.cullingMask = ~(1 << HiddenFromSatCameraLayer);
            satCamera.stereoTargetEye = StereoTargetEyeMask.None;
            satCamera.depth = -10f;
            satCamera.enabled = false;
        }

        void BuildVrScreen()
        {
            if (rig == null || rig.rightHand == null) return;
            Shader sh = Resources.Load<Shader>("EosatScreen");
            if (sh == null) sh = Shader.Find("EOSAT1/Screen");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) return;
            vrScreen = new GameObject("Satellite Camera Screen");
            vrScreen.transform.SetParent(rig.rightHand, false);
            vrScreen.transform.localPosition = new Vector3(0f, 0.16f, 0.08f);
            vrScreen.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            vrScreenMesh = new Mesh { name = "EOSAT1 Screen" };
            const float h = 0.13f;   // half size: a 26 cm square
            vrScreenMesh.vertices = new[] { new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(-h, h, 0f), new Vector3(h, h, 0f) };
            vrScreenMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            vrScreenMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            vrScreenMesh.RecalculateNormals();
            vrScreenMesh.RecalculateBounds();
            vrScreen.AddComponent<MeshFilter>().sharedMesh = vrScreenMesh;
            var mr = vrScreen.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            vrScreenMaterial = new Material(sh) { name = "EOSAT1 Screen (runtime)" };
            mr.sharedMaterial = vrScreenMaterial;
            vrScreen.SetActive(false);
        }

        void HideOverlaysFromSatCamera()
        {
            // the picture should show the Earth, not the helpers drawn around the spacecraft
            if (satelliteView != null)
            {
                SetLayer(satelliteView.bodyAxes);
                SetLayer(satelliteView.boresight);
                SetLayer(satelliteView.jamLabel);
            }
            if (space.footprint != null) SetLayer(space.footprint.gameObject);
            if (rig != null)
            {
                if (rig.leftHand != null) SetLayer(rig.leftHand.gameObject);
                if (rig.rightHand != null) SetLayer(rig.rightHand.gameObject);
            }
            SetLayer(GameObject.Find("Comfort Floor"));
        }

        static void SetLayer(GameObject go)
        {
            if (go == null) return;
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = HiddenFromSatCameraLayer;
        }

        // ------------------------------------------------------------------ the three switches
        /// <summary>Zoom out to the whole Earth, or back to the spacecraft.</summary>
        public void ToggleOverview()
        {
            if (!ready) return;
            space.overview = !space.overview;
            ApplyState();
            app.Say(space.overview
                ? "Whole Earth: to scale, except the spacecraft, drawn bigger so you can see it"
                : "Back at the spacecraft (life size)");
        }

        /// <summary>Desktop: off -> corner picture -> full screen -> off. Headset: screen on / off.</summary>
        public void CycleSatCamera()
        {
            if (!ready) return;
            bool headset = UnityEngine.XR.XRSettings.isDeviceActive;
            if (headset) display = display == SatCameraDisplay.Off ? SatCameraDisplay.Inset : SatCameraDisplay.Off;
            else display = (SatCameraDisplay)(((int)display + 1) % 3);
            // the camera picture needs the life-size scene around the spacecraft
            if (display != SatCameraDisplay.Off && space.overview) space.overview = false;
            ApplyState();
            if (display == SatCameraDisplay.Off) app.Say("Satellite camera view off");
            else if (headset && vrScreen == null) app.Say("Satellite camera: no screen could be made (shader EOSAT1/Screen missing)");
            else app.Say(headset ? "Satellite camera: on the screen above your right hand"
                                 : (display == SatCameraDisplay.Inset ? "Satellite camera: corner picture (C again for full screen)"
                                                                       : "Satellite camera: full screen (wheel or [ ] to zoom)"));
        }

        public void TogglePath()
        {
            if (!ready) return;
            pathVisible = !pathVisible;
            ApplyState();
            app.Say(pathVisible ? "Orbit path shown: bright = ahead, dim = behind, violet = in Earth's shadow" : "Orbit path hidden");
        }

        public void ZoomSatCamera(float notches)
        {
            fieldOfViewDeg = Mathf.Clamp(fieldOfViewDeg * Mathf.Pow(0.85f, notches), TrueFieldOfViewDeg, maxFieldOfViewDeg);
        }

        void ApplyState()
        {
            if (!ready) return;
            if (space.overview && display != SatCameraDisplay.Off) display = SatCameraDisplay.Off;
            if (desktop != null) desktop.SetOverview(space.overview, space.transform.position, space.overviewEarthRadius * space.transform.lossyScale.x);
            if (path != null && path.gameObject.activeSelf != pathVisible) path.gameObject.SetActive(pathVisible);
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!ready) return;
            bool headset = UnityEngine.XR.XRSettings.isDeviceActive;

            if (InputShim.Down(KeyCode.Z)) ToggleOverview();
            if (InputShim.Down(KeyCode.C)) CycleSatCamera();
            if (InputShim.Down(KeyCode.O)) TogglePath();
            if (display != SatCameraDisplay.Off)
            {
                if (InputShim.Down(KeyCode.LeftBracket)) ZoomSatCamera(-1f);
                if (InputShim.Down(KeyCode.RightBracket)) ZoomSatCamera(1f);
            }
            bool wheelZoomsCamera = !headset && display == SatCameraDisplay.Full;
            if (desktop != null) desktop.WheelBusy = wheelZoomsCamera;
            if (wheelZoomsCamera)
            {
                float scroll = InputShim.Scroll();
                if (Mathf.Abs(scroll) > 0.001f) ZoomSatCamera(scroll);
            }

            // the buttons (desktop)
            if (!headset && InputShim.MouseDown(0))
            {
                LayoutButtons();
                Vector2 m = InputShim.MousePosition();
                var p = new Vector2(m.x, Screen.height - m.y);
                if (buttonRects[0].Contains(p)) CycleSatCamera();
                else if (buttonRects[1].Contains(p)) ToggleOverview();
                else if (buttonRects[2].Contains(p)) TogglePath();
            }

            UpdateSatCamera(headset);
        }

        void UpdateSatCamera(bool headset)
        {
            bool on = display != SatCameraDisplay.Off && !space.overview && app.State.Valid;
            if (vrScreen != null && vrScreen.activeSelf != (on && headset)) vrScreen.SetActive(on && headset);
            if (satCamera == null) return;
            if (!on)
            {
                if (satCamera.enabled) satCamera.enabled = false;
                return;
            }

            int w, h;
            if (headset) { w = 1024; h = 1024; }
            else if (display == SatCameraDisplay.Full) { w = Mathf.Max(16, Screen.width); h = Mathf.Max(16, Screen.height); }
            else { w = h = InsetSize(); }
            if (target == null || target.width != w || target.height != h)
            {
                satCamera.targetTexture = null;
                if (target != null)
                {
                    target.Release();
                    Destroy(target);
                }
                target = new RenderTexture(w, h, 24) { name = "EOSAT1 Satellite Camera" };
                target.Create();
            }
            if (satCamera.targetTexture != target) satCamera.targetTexture = target;
            if (vrScreenMaterial != null && vrScreenMaterial.mainTexture != target) vrScreenMaterial.mainTexture = target;

            // fieldOfViewDeg is the width of the view; Unity wants the height
            float aspect = (float)w / h;
            float halfWidth = Mathf.Tan(0.5f * fieldOfViewDeg * Mathf.Deg2Rad);
            satCamera.aspect = aspect;
            satCamera.fieldOfView = 2f * Mathf.Atan(halfWidth / aspect) * Mathf.Rad2Deg;
            satCamera.farClipPlane = Mathf.Max(100f, space.FarClip);
            if (!satCamera.enabled) satCamera.enabled = true;

            if (Time.unscaledTime >= nextLabel)
            {
                nextLabel = Time.unscaledTime + 0.25f;
                BuildCameraLabel();
            }
        }

        static int InsetSize()
        {
            return Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.38f), 160, 720);
        }

        void BuildCameraLabel()
        {
            DisplayState s = app.State;
            double alt = s.PosEciKm.Length - Ephemeris.EarthRadiusKm;
            Vec3d boresight = s.QBodyToEci.Rotate(Vec3d.UnitZ);
            double offNadir = Vec3d.Angle(boresight, -s.PosEciKm) * MathUtil.Rad2Deg;
            double across = 2.0 * alt * System.Math.Tan(0.5 * fieldOfViewDeg * MathUtil.Deg2Rad);
            bool atTrue = fieldOfViewDeg <= TrueFieldOfViewDeg * 1.01f;
            string text = "DragonEye view   " + fieldOfViewDeg.ToString("0.0", Inv) + " deg";
            if (offNadir < 60.0) text += "   about " + across.ToString("0", Inv) + " km across";
            if (atTrue) text += "   (the real field of view)";
            text += "   " + offNadir.ToString("0.0", Inv) + " deg off nadir";
            if (space.Illumination < 0.5) text += "   ECLIPSE";
            cameraLabel = text;
        }

        // ------------------------------------------------------------------ on screen (desktop)
        void LayoutButtons()
        {
            float bh = Mathf.Max(26f, Screen.height / 30f);
            float bw = bh * 6.4f;
            float x = Screen.width - bw - 10f;
            for (int i = 0; i < 3; i++) buttonRects[i] = new Rect(x, 10f + i * (bh + 6f), bw, bh);
        }

        void EnsureStyles()
        {
            if (buttonStyle != null && styleScreenHeight == Screen.height) return;
            styleScreenHeight = Screen.height;
            buttonStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(12, Screen.height / 64),
                wordWrap = false,
            };
            buttonStyle.normal.textColor = new Color(0.92f, 0.94f, 0.97f);
            buttonOnStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold };
            buttonOnStyle.normal.textColor = new Color(0.49f, 0.99f, 0.60f);
            labelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(11, Screen.height / 72),
                wordWrap = false,
                padding = new RectOffset(8, 8, 4, 4),
            };
            labelStyle.normal.textColor = new Color(0.92f, 0.94f, 0.97f);
        }

        void OnGUI()
        {
            if (!ready || UnityEngine.XR.XRSettings.isDeviceActive) return;
            if (Event.current.type != EventType.Repaint) return;
            GUI.depth = 50;   // behind the info panel and the help line
            EnsureStyles();
            LayoutButtons();

            // the satellite camera's picture
            if (display != SatCameraDisplay.Off && target != null && satCamera != null && satCamera.enabled)
            {
                Rect r;
                if (display == SatCameraDisplay.Full) r = new Rect(0f, 0f, Screen.width, Screen.height);
                else
                {
                    float size = InsetSize();
                    r = new Rect(Screen.width - size - 10f, buttonRects[2].yMax + 10f, size, size);
                    Frame(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), new Color(0.75f, 0.8f, 0.9f, 0.9f), 2f);
                }
                GUI.DrawTexture(r, target, ScaleMode.StretchToFill, false);

                // cross at the boresight, and the real instrument's strip when zoomed out
                Color mark = new Color(1f, 0.82f, 0.4f, 0.9f);
                Vector2 c = r.center;
                float arm = Mathf.Max(6f, r.height * 0.02f);
                Fill(new Rect(c.x - arm, c.y - 0.5f, arm * 2f, 1f), mark);
                Fill(new Rect(c.x - 0.5f, c.y - arm, 1f, arm * 2f), mark);
                float trueFraction = Mathf.Tan(0.5f * TrueFieldOfViewDeg * Mathf.Deg2Rad) / Mathf.Tan(0.5f * fieldOfViewDeg * Mathf.Deg2Rad);
                if (trueFraction < 0.7f)
                {
                    float bw = Mathf.Max(8f, r.width * trueFraction);
                    Frame(new Rect(c.x - bw * 0.5f, c.y - bw * 0.5f, bw, bw), mark, 1f);
                }

                var lc = new GUIContent(display == SatCameraDisplay.Full
                    ? cameraLabel + (trueFraction < 0.7f ? "   box = the real 44 km wide view" : "") + "   |   wheel or [ ] zoom   C close"
                    : cameraLabel);
                Vector2 ls = labelStyle.CalcSize(lc);
                float lw = Mathf.Min(ls.x, display == SatCameraDisplay.Full ? Screen.width - 20f : r.width);
                Rect lr = display == SatCameraDisplay.Full
                    ? new Rect((Screen.width - lw) * 0.5f, 10f, lw, ls.y)
                    : new Rect(r.x, r.yMax + 4f, lw, ls.y);
                GUI.Box(lr, lc, labelStyle);
            }

            // the buttons
            bool overview = space.overview;
            string camText = display == SatCameraDisplay.Off ? "Satellite camera  (C)"
                : (display == SatCameraDisplay.Inset ? "Satellite camera: corner  (C)" : "Satellite camera: full  (C)");
            GUI.Box(buttonRects[0], camText, display != SatCameraDisplay.Off ? buttonOnStyle : buttonStyle);
            GUI.Box(buttonRects[1], overview ? "Back to the satellite  (Z)" : "Zoom out: whole Earth  (Z)", overview ? buttonOnStyle : buttonStyle);
            GUI.Box(buttonRects[2], pathVisible ? "Orbit path: on  (O)" : "Orbit path: off  (O)", pathVisible ? buttonOnStyle : buttonStyle);
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Frame(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
    }
}
