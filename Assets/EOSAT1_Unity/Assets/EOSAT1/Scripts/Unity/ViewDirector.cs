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
//   Panel axis (P)        one long line out of the back of the spacecraft, along the direction
//                         the deployed solar array faces (body -X): the solar panels' version
//                         of the camera boresight line. Aim it at the Sun for full power.
//
// Desktop also gets a button for each at the top right (the Views panel, with the body axes
// and the camera line too); rest the mouse on one to read what it does. Headset: right grip =
// whole Earth, right stick click = camera screen, left stick click = orbit path, or the VR menu.
// The Views panel and the camera's corner picture stay on the laptop's screen while the
// headset is in use, so the person at the laptop can switch views for the one in the headset.
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

        [Header("Solar panel axis")]
        public bool panelAxesVisible = true;
        [Tooltip("Length of the line in metres. The camera boresight line is 3 m.")]
        public float panelAxisLength = 3f;
        [Tooltip("Where the line starts, in metres from the spacecraft's centre along the panel direction (the rear ridge is at 0.565).")]
        public float panelAxisStart = 0.58f;
        public Color panelAxisColor = new Color(1f, 0.6f, 0.12f);

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
        // the frame round the headset's camera picture, in the VR menu's style
        TextMesh vrCaption;
        Material vrFrameText;
        Font vrFont;
        readonly System.Collections.Generic.List<Material> vrFrameMaterials = new System.Collections.Generic.List<Material>();
        const float ScreenHalf = 0.13f;                       // the picture: a 26 cm square
        const float FrameTop = ScreenHalf + 0.032f, FrameBottom = -ScreenHalf - 0.03f, FrameHalfWidth = ScreenHalf + 0.01f;
        /// <summary>Height of the whole framed screen (title band, picture, caption band), metres at scale 1.</summary>
        public const float FrameHeight = FrameTop - FrameBottom;
        Mesh vrScreenMesh, markerMesh;
        bool ready;
        const int ButtonCount = 6;
        readonly Rect[] buttonRects = new Rect[ButtonCount];
        Rect buttonPanel;
        readonly System.Collections.Generic.List<GameObject> panelAxes = new System.Collections.Generic.List<GameObject>();
        Mesh arrowMesh;
        Material arrowMaterial;
        string cameraLabel = "", cameraLabelShort = "";
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
            BuildPanelAxes();
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
            foreach (Material m in vrFrameMaterials) if (m != null) Destroy(m);
            Font.textureRebuilt -= OnVrFontRebuilt;
            if (vrScreenMesh != null) Destroy(vrScreenMesh);
            if (markerMesh != null) Destroy(markerMesh);
            if (arrowMesh != null) Destroy(arrowMesh);
            if (arrowMaterial != null) Destroy(arrowMaterial);
        }

        void BuildPanelAxes()
        {
            if (satelliteView == null) return;
            Shader sh = Shader.Find("EOSAT1/UnlitColor");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) return;
            arrowMaterial = new Material(sh) { name = "EOSAT1 Panel Axis (runtime)" };
            if (arrowMaterial.HasProperty("_Color")) arrowMaterial.SetColor("_Color", panelAxisColor);
            if (arrowMaterial.HasProperty("_BaseColor")) arrowMaterial.SetColor("_BaseColor", panelAxisColor);
            // a thin line like the camera boresight's (8 mm), with a small head so its direction is clear
            arrowMesh = ArrowMesh(Mathf.Max(0.2f, panelAxisLength), 0.004f, 0.018f, 0.09f, 16);

            // body axes (x, y, z) -> Unity local (x, z, y), the same swap as SimToUnity
            Vector3 nb = satelliteView.deployedCellNormalBody;
            Vector3 dir = new Vector3(nb.x, nb.z, nb.y);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.left;
            dir.Normalize();

            var go = new GameObject("Solar Panel Axis");
            go.transform.SetParent(satelliteView.transform, false);
            go.transform.localPosition = dir * panelAxisStart;
            go.transform.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = arrowMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // a label at the tip, in the same text style as the body axes' labels
            TextMesh like = satelliteView.jamLabel != null ? satelliteView.jamLabel.GetComponent<TextMesh>() : null;
            MeshRenderer likeRenderer = satelliteView.jamLabel != null ? satelliteView.jamLabel.GetComponent<MeshRenderer>() : null;
            if (like != null && likeRenderer != null && like.font != null)
            {
                var lg = new GameObject("Label");
                lg.transform.SetParent(go.transform, false);
                lg.transform.localPosition = new Vector3(0f, 0f, panelAxisLength + 0.1f);
                var tm = lg.AddComponent<TextMesh>();
                tm.font = like.font;
                tm.fontSize = 48;
                tm.characterSize = 0.009f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.Lerp(panelAxisColor, Color.white, 0.35f);
                tm.text = "SOLAR PANELS";
                var lr = lg.GetComponent<MeshRenderer>();
                lr.sharedMaterial = likeRenderer.sharedMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lg.AddComponent<FaceCamera>();
            }
            SetLayer(go);
            go.SetActive(panelAxesVisible);
            panelAxes.Add(go);
        }

        /// <summary>A shaft and a cone head along +Z, starting at the origin.</summary>
        static Mesh ArrowMesh(float length, float shaftRadius, float headRadius, float headLength, int sides)
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            float neck = length - headLength;
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                v.Add(new Vector3(c * shaftRadius, sn * shaftRadius, 0f));     // 4 per side: shaft start,
                v.Add(new Vector3(c * shaftRadius, sn * shaftRadius, neck));   // shaft end,
                v.Add(new Vector3(c * headRadius, sn * headRadius, neck));     // head base,
                v.Add(new Vector3(0f, 0f, length));                            // tip
            }
            for (int i = 0; i < sides; i++)
            {
                int a0 = i * 4, b0 = ((i + 1) % sides) * 4;
                t.Add(a0); t.Add(a0 + 1); t.Add(b0);            // shaft
                t.Add(b0); t.Add(a0 + 1); t.Add(b0 + 1);
                t.Add(a0 + 1); t.Add(a0 + 2); t.Add(b0 + 1);    // the flat back of the head
                t.Add(b0 + 1); t.Add(a0 + 2); t.Add(b0 + 2);
                t.Add(a0 + 2); t.Add(a0 + 3); t.Add(b0 + 2);    // the cone
            }
            var m = new Mesh { name = "EOSAT1 Arrow" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
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
            BuildVrFrame();
            vrScreen.SetActive(false);
        }

        /// <summary>
        /// A frame round the headset's camera picture in the VR menu's style: a navy panel with a cyan
        /// rounded border, the title above, a small crosshair on the picture, and the camera's numbers
        /// below (field of view, km across, off nadir), as on the laptop's camera view.
        /// </summary>
        void BuildVrFrame()
        {
            Shader panel = Shader.Find("EOSAT1/UiPanel");
            Shader textShader = Shader.Find("EOSAT1/HudText");
            Shader unlit = Shader.Find("EOSAT1/UnlitColor");
            Transform t = vrScreen.transform;
            if (panel != null)
            {
                float h = FrameTop - FrameBottom;
                MeshRenderer back = MenuKit.Panel("Frame", t, panel, new Vector3(0f, (FrameTop + FrameBottom) * 0.5f, 0.004f),
                                                  new Vector2(FrameHalfWidth * 2f, h),
                                                  new Color(0.03f, 0.065f, 0.115f, 0.96f), new Color(0.012f, 0.025f, 0.05f, 0.96f),
                                                  HudStyle.WithAlpha(HudStyle.Accent, 0.85f), 0.004f, 0.02f, MenuKit.OrderPanel);
                back.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                vrFrameMaterials.Add(back.sharedMaterial);
            }
            if (unlit != null)
            {
                // a small crosshair with a gap in the middle, so the very centre stays clear
                var cross = new Material(unlit) { name = "EOSAT1 Camera Crosshair" };
                cross.SetColor("_Color", HudStyle.Accent * 0.9f);
                vrFrameMaterials.Add(cross);
                const float arm = 0.012f, gap = 0.006f, bar = 0.0012f;
                CrossBar(t, cross, new Vector3(-(gap + arm * 0.5f), 0f, -0.001f), new Vector2(arm, bar));
                CrossBar(t, cross, new Vector3(gap + arm * 0.5f, 0f, -0.001f), new Vector2(arm, bar));
                CrossBar(t, cross, new Vector3(0f, -(gap + arm * 0.5f), -0.001f), new Vector2(bar, arm));
                CrossBar(t, cross, new Vector3(0f, gap + arm * 0.5f, -0.001f), new Vector2(bar, arm));
            }
            if (textShader != null)
            {
                vrFont = null;
                try { vrFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (System.Exception) { }
                if (vrFont == null)
                {
                    try { vrFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                    catch (System.Exception) { }
                }
                if (vrFont == null) return;
                vrFrameText = MenuKit.NewMaterial(textShader, "EOSAT1 Camera Frame Text");
                vrFrameMaterials.Add(vrFrameText);
                if (vrFont.material != null) vrFrameText.SetTexture("_MainTex", vrFont.material.mainTexture);
                Font.textureRebuilt += OnVrFontRebuilt;
                float titleY = ScreenHalf + 0.016f;
                MenuKit.Text("Title", t, new Vector3(-ScreenHalf, titleY, -0.001f), MenuKit.Spaced("SATELLITE CAMERA"), 0.016f,
                             HudStyle.Accent, TextAnchor.MiddleLeft, vrFont, vrFrameText);
                MenuKit.Text("Camera Name", t, new Vector3(ScreenHalf, titleY, -0.001f), "DragonEye  \u00b7  C off  \u00b7  [ ] zoom", 0.011f,
                             HudStyle.TextDim, TextAnchor.MiddleRight, vrFont, vrFrameText);
                vrCaption = MenuKit.Text("Caption", t, new Vector3(0f, -ScreenHalf - 0.015f, -0.001f), "", 0.011f,
                                         HudStyle.TextBright, TextAnchor.MiddleCenter, vrFont, vrFrameText);
            }
        }

        static void CrossBar(Transform parent, Material m, Vector3 pos, Vector2 size)
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Crosshair";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            Renderer r = q.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void OnVrFontRebuilt(Font f)
        {
            if (f == vrFont && vrFrameText != null && f.material != null) vrFrameText.SetTexture("_MainTex", f.material.mainTexture);
        }

        /// <summary>
        /// The headset's camera screen hangs above the right controller. With the controllers
        /// switched off (Settings, or K) the controller isn't drawn, so the screen floats in front
        /// of you instead, low and to the right, and turns with your head.
        /// </summary>
        bool fullPlaced;
        [Tooltip("Headset, full camera view (C twice): how wide the big screen is and how far in front of you, metres.")]
        public float fullViewWidth = 2.6f, fullViewDistance = 1.6f;

        void PlaceVrScreen()
        {
            if (rig == null) return;
            if (display == SatCameraDisplay.Full)
            {
                // the whole view becomes the camera: a big framed screen standing in front of where you
                // look when it opens, at eye height, fixed in the room (it doesn't follow your head)
                Transform room = rig.cameraOffset != null ? rig.cameraOffset : rig.transform;
                if (fullPlaced && vrScreen.transform.parent == room) return;
                if (rig.head == null) return;
                Vector3 fwd = rig.head.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = rig.transform.forward;
                fwd.Normalize();
                vrScreen.transform.SetParent(room, true);
                vrScreen.transform.SetPositionAndRotation(rig.head.position + fwd * fullViewDistance, Quaternion.LookRotation(fwd, Vector3.up));
                vrScreen.transform.localScale = Vector3.one * (fullViewWidth / (2f * FrameHalfWidth));
                SetLayer(vrScreen);
                fullPlaced = true;
                return;
            }
            fullPlaced = false;
            bool onHand = MenuSettings.UseControllers && rig.rightHand != null;
            Transform laptopScreen = null, side;
            bool onLaptop = !onHand && LaptopMarker.Current != null && LaptopMarker.Current.TryGetAnchors(out laptopScreen, out side);
            Transform want = onHand ? rig.rightHand : onLaptop ? laptopScreen : rig.head;
            if (want == null || (vrScreen.transform.parent == want && vrScreen.transform.localScale.x < 3f)) return;
            vrScreen.transform.SetParent(want, false);
            vrScreen.transform.localScale = Vector3.one;
            if (onHand)
            {
                vrScreen.transform.localPosition = new Vector3(0f, 0.16f, 0.08f);
                vrScreen.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            }
            else if (onLaptop)
            {
                // on the drawn laptop's screen, as tall as its display (the picture is square)
                vrScreen.transform.localPosition = Vector3.zero;
                vrScreen.transform.localRotation = Quaternion.identity;
                vrScreen.transform.localScale = Vector3.one * (LaptopMarker.DisplaySize.y / FrameHeight);
                vrScreen.transform.localPosition = new Vector3(0f, -(FrameTop + FrameBottom) * 0.5f * vrScreen.transform.localScale.y, 0f);
            }
            else
            {
                // 26 cm square, 60 cm away: low and right of where you look, tilted to face you
                vrScreen.transform.localPosition = new Vector3(0.2f, -0.14f, 0.6f);
                vrScreen.transform.localRotation = Quaternion.Euler(12f, 18f, 0f);
            }
            SetLayer(vrScreen);   // never in the satellite camera's own picture (frame and all)
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
            display = (SatCameraDisplay)(((int)display + 1) % 3);   // off -> small screen -> full view -> off
            fullPlaced = false;
            // the camera picture needs the life-size scene around the spacecraft
            if (display != SatCameraDisplay.Off && space.overview) space.overview = false;
            ApplyState();
            if (display == SatCameraDisplay.Off) app.Say("Satellite camera view off");
            else if (headset && vrScreen == null) app.Say("Satellite camera: no screen could be made (shader EOSAT1/Screen missing)");
            else if (headset && display == SatCameraDisplay.Full) app.Say("Satellite camera: full view in front of you ([ ] to zoom, C to close)");
            else app.Say(headset ? (MenuSettings.UseControllers ? "Satellite camera: on the screen above your right hand (C again for the full view)"
                                                                : "Satellite camera: on the laptop's screen (C again for the full view)")
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

        public void TogglePanelAxes()
        {
            if (!ready) return;
            panelAxesVisible = !panelAxesVisible;
            ApplyState();
            app.Say(panelAxesVisible ? "Panel axis shown: the orange line points the way the deployed solar panels face" : "Panel axis hidden");
        }

        public void ToggleBodyAxes()
        {
            if (!ready || satelliteView == null) return;
            app.ToggleAxes();
            app.Say(satelliteView.AxesVisible ? "Body axes shown: X = flight direction, Y = solar panel axis, Z = camera" : "Body axes hidden");
        }

        public void ToggleCameraLine()
        {
            if (!ready || satelliteView == null) return;
            app.ToggleBoresight();
            app.Say(satelliteView.BoresightVisible ? "Camera line shown: where the cameras look, and the patch of Earth they see" : "Camera line hidden");
        }

        public bool PathVisible { get { return pathVisible; } }
        public bool PanelAxesVisible { get { return panelAxesVisible; } }
        public bool SatCameraOn { get { return display != SatCameraDisplay.Off; } }

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
            for (int i = 0; i < panelAxes.Count; i++)
                if (panelAxes[i] != null && panelAxes[i].activeSelf != panelAxesVisible) panelAxes[i].SetActive(panelAxesVisible);
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!ready) return;
            bool headset = UnityEngine.XR.XRSettings.isDeviceActive;

            bool keys = !HudStyle.ModalActive;   // the help card is open: keys wait
            if (keys && InputShim.Down(KeyCode.Z)) ToggleOverview();
            if (keys && InputShim.Down(KeyCode.C)) CycleSatCamera();
            if (keys && InputShim.Down(KeyCode.O)) TogglePath();
            if (keys && InputShim.Down(KeyCode.P)) TogglePanelAxes();
            if (keys && display != SatCameraDisplay.Off)
            {
                if (InputShim.Down(KeyCode.LeftBracket) && !InputShim.Ctrl) ZoomSatCamera(-1f);   // Ctrl + [ ] turn the drawn laptop
                if (InputShim.Down(KeyCode.RightBracket) && !InputShim.Ctrl) ZoomSatCamera(1f);
            }
            bool wheelZoomsCamera = !headset && display == SatCameraDisplay.Full;
            if (desktop != null) desktop.WheelBusy = wheelZoomsCamera;
            if (wheelZoomsCamera)
            {
                float scroll = InputShim.Scroll();
                if (Mathf.Abs(scroll) > 0.001f) ZoomSatCamera(scroll);
            }

            // the buttons (the laptop's screen, with or without a headset in use)
            if (!HudStyle.ModalActive && InputShim.MouseDown(0))
            {
                LayoutButtons();
                Vector2 m = InputShim.MousePosition();
                var p = new Vector2(m.x, Screen.height - m.y);
                if (buttonRects[0].Contains(p)) CycleSatCamera();
                else if (buttonRects[1].Contains(p)) ToggleOverview();
                else if (buttonRects[2].Contains(p)) TogglePath();
                else if (buttonRects[3].Contains(p)) TogglePanelAxes();
                else if (buttonRects[4].Contains(p)) ToggleBodyAxes();
                else if (buttonRects[5].Contains(p)) ToggleCameraLine();
            }

            UpdateSatCamera(headset);
        }

        void UpdateSatCamera(bool headset)
        {
            bool on = display != SatCameraDisplay.Off && !space.overview && app.State.Valid;
            if (vrScreen != null && headset) PlaceVrScreen();
            if (vrScreen != null && vrScreen.activeSelf != (on && headset)) vrScreen.SetActive(on && headset);
            if (satCamera == null) return;
            if (!on)
            {
                if (satCamera.enabled) satCamera.enabled = false;
                return;
            }

            int w, h;
            if (headset) { w = h = display == SatCameraDisplay.Full ? 1536 : 1024; }
            else if (display == SatCameraDisplay.Full) { w = Mathf.Max(16, Screen.width); h = Mathf.Max(16, Screen.height); }
            else { w = h = InsetSize(); }
            int aa = headset ? 1 : 2;   // headset: drawn at 1024 and shown smaller, smoothed by its mipmaps
            if (target == null || target.width != w || target.height != h || target.antiAliasing != aa || target.useMipMap != headset)
            {
                satCamera.targetTexture = null;
                if (target != null)
                {
                    target.Release();
                    Destroy(target);
                }
                target = new RenderTexture(w, h, 24) { name = "EOSAT1 Satellite Camera", antiAliasing = aa };
                if (headset)
                {
                    // shown much smaller than it is drawn: mipmaps keep it from shimmering
                    target.useMipMap = true;
                    target.autoGenerateMips = true;
                    target.filterMode = FilterMode.Trilinear;
                    target.anisoLevel = 4;
                }
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
            string text = "<b><color=" + HudStyle.AccentHex + ">" + HudStyle.Spaced("DRAGONEYE") + "</color></b>     " + fieldOfViewDeg.ToString("0.0", Inv) + " deg";
            if (offNadir < 60.0) text += "   about " + across.ToString("0", Inv) + " km across";
            if (atTrue) text += "   (the real field of view)";
            text += "   " + offNadir.ToString("0.0", Inv) + " deg off nadir";
            if (space.Illumination < 0.5) text += "   <color=" + HudStyle.WarnHex + ">ECLIPSE</color>";
            cameraLabel = text;
            cameraLabelShort = "<b><color=" + HudStyle.AccentHex + ">DRAGONEYE</color></b>   " + fieldOfViewDeg.ToString("0.0", Inv) + " deg"
                               + (offNadir < 60.0 ? "   " + across.ToString("0", Inv) + " km across" : "");
            if (vrCaption != null)
            {
                string c = fieldOfViewDeg.ToString("0.0", Inv) + "\u00b0 wide"
                         + (offNadir < 60.0 ? "   \u00b7   " + across.ToString("0", Inv) + " km across" : "")
                         + "   \u00b7   " + offNadir.ToString("0.0", Inv) + "\u00b0 off nadir"
                         + (space.Illumination < 0.5 ? "   <color=" + HudStyle.WarnHex + ">ECLIPSE</color>" : "");
                if (vrCaption.text != c) vrCaption.text = c;
            }
        }

        // ------------------------------------------------------------------ on screen (desktop)
        void LayoutButtons()
        {
            float u = HudStyle.Unit;
            float pad = Mathf.Round(u * 0.7f);
            float bh = Mathf.Round(u * 2f);
            float bw = Mathf.Round(u * 14.5f);
            float head = Mathf.Round(u * 1.7f);
            float gap = Mathf.Round(u * 0.45f);
            float x = Screen.width - 12f - pad - bw;
            float y = 12f + pad + head;
            for (int i = 0; i < ButtonCount; i++) buttonRects[i] = new Rect(x, y + i * (bh + gap), bw, bh);
            buttonPanel = new Rect(x - pad, 12f, bw + pad * 2f, pad * 2f + head + ButtonCount * bh + (ButtonCount - 1) * gap);
        }

        static readonly string[] Tips =
        {
            "See what the satellite's own cameras see. Click again for full screen, and once more to close.",
            "Zoom right out: the Earth as a globe with the satellite going round it and the Moon at its true distance.",
            "Show or hide the line the satellite follows around the Earth. Violet means it is in the Earth's shadow.",
            "Show or hide the orange line that points the way the open solar panels face. Aim it at the Sun for full power.",
            "Show or hide the three arrows fixed to the satellite: X = flight direction, Y = solar panel axis, Z = camera.",
            "Show or hide the line along which the cameras look, and the patch of Earth they see.",
        };

        static void DrawButton(Rect r, string label, string key, bool on, Vector2 mouse, int tip)
        {
            bool hover = r.Contains(mouse);
            HudStyle.DrawButton(r, label, key, on, hover);
            if (hover) HudStyle.SetTip(r, Tips[tip]);
        }

        void OnGUI()
        {
            if (!ready) return;   // on the laptop's screen with or without a headset in use
            if (Event.current.type != EventType.Repaint) return;
            GUI.depth = 50;   // behind the status panel and the key list
            HudStyle.Ensure();
            LayoutButtons();
            float u = HudStyle.Unit;
            Vector2 mouse = HudStyle.Mouse();

            // the satellite camera's picture
            if (display != SatCameraDisplay.Off && target != null && satCamera != null && satCamera.enabled)
            {
                Rect r;
                bool full = display == SatCameraDisplay.Full;
                if (full) r = new Rect(0f, 0f, Screen.width, Screen.height);
                else
                {
                    float size = InsetSize();
                    r = new Rect(Screen.width - size - 14f, buttonPanel.yMax + 12f, size, size);
                }
                GUI.DrawTexture(r, target, ScaleMode.StretchToFill, false);
                if (!full) HudStyle.Frame(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), HudStyle.WithAlpha(HudStyle.Accent, 0.85f), 2f);

                // cross at the boresight, and the real instrument's view when zoomed out
                Color mark = HudStyle.WithAlpha(HudStyle.Amber, 0.9f);
                Vector2 c = r.center;
                float arm = Mathf.Max(6f, r.height * 0.02f);
                HudStyle.Fill(new Rect(c.x - arm, c.y - 0.5f, arm * 2f, 1f), mark);
                HudStyle.Fill(new Rect(c.x - 0.5f, c.y - arm, 1f, arm * 2f), mark);
                float trueFraction = Mathf.Tan(0.5f * TrueFieldOfViewDeg * Mathf.Deg2Rad) / Mathf.Tan(0.5f * fieldOfViewDeg * Mathf.Deg2Rad);
                if (trueFraction < 0.7f)
                {
                    float bw = Mathf.Max(8f, r.width * trueFraction);
                    HudStyle.Frame(new Rect(c.x - bw * 0.5f, c.y - bw * 0.5f, bw, bw), mark, 1f);
                }

                string text = full ? cameraLabel : cameraLabelShort;
                if (full) text += (trueFraction < 0.7f ? "     box = the real 44 km wide view" : "")
                                  + "     <color=" + HudStyle.DimHex + ">wheel or [ ] zoom     C close</color>";
                var lc = new GUIContent(text);
                Vector2 ls = HudStyle.Small.CalcSize(lc);
                float lp = Mathf.Round(u * 0.6f);
                float lw = Mathf.Min(ls.x + lp * 2f, full ? Screen.width - 24f : r.width + 4f);
                float lh = ls.y + lp;
                Rect lr = full ? new Rect((Screen.width - lw) * 0.5f, 12f, lw, lh) : new Rect(r.xMax + 2f - lw, r.yMax + 8f, lw, lh);
                HudStyle.DrawPanel(lr);
                GUI.Label(new Rect(lr.x + lp, lr.y + lp * 0.5f, lr.width - lp * 2f, ls.y), lc, HudStyle.Small);
            }

            // the buttons
            HudStyle.DrawPanel(buttonPanel);
            HudStyle.Block(buttonPanel);
            if (HudStyle.ModalActive) mouse = new Vector2(-1e6f, -1e6f);
            GUI.Label(new Rect(buttonRects[0].x, buttonPanel.y + Mathf.Round(u * 0.7f), buttonRects[0].width, u * 1.5f),
                      HudStyle.Spaced("VIEWS"), HudStyle.Header);
            bool overview = space.overview;
            string camText = display == SatCameraDisplay.Off ? "Satellite camera"
                : (display == SatCameraDisplay.Inset ? "Camera: corner" : "Camera: full screen");
            DrawButton(buttonRects[0], camText, "C", display != SatCameraDisplay.Off, mouse, 0);
            DrawButton(buttonRects[1], overview ? "Back to the satellite" : "Whole Earth", "Z", overview, mouse, 1);
            DrawButton(buttonRects[2], "Orbit path", "O", pathVisible, mouse, 2);
            DrawButton(buttonRects[3], "Solar panel line", "P", panelAxesVisible, mouse, 3);
            DrawButton(buttonRects[4], "Body axes", "G", satelliteView != null && satelliteView.AxesVisible, mouse, 4);
            DrawButton(buttonRects[5], "Camera line", "B", satelliteView != null && satelliteView.BoresightVisible, mouse, 5);
        }
    }
}
