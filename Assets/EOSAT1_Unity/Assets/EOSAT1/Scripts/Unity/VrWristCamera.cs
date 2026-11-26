// EOSAT-1 VR - the satellite camera on the right wrist.
//
// A second wrist computer, on the right sleeve, to match the info pad on the left. While the
// satellite camera is on (right stick click, VIEWS > SAT CAMERA in the VR menu, or C / the
// Views panel on the laptop), its little screen shows what the DragonEye imagers see. Turn
// the right wrist towards your face, as if checking a watch, and the picture pops out of it,
// big, upright in front of the wrist so the glove never covers it: how wide the view is and
// how much ground that covers, how far off nadir it points, a cross at the boresight and, while
// zoomed out, the real 44 km instrument view as a box. Lower the arm or turn the wrist away and
// it folds back in. CAMERA ZOOM IN / OUT on the VR menu's VIEWS page zooms it.
//
// It replaces the screen that used to float over the right controller and cut through the
// glove (ViewDirector still shows that one if this can't be built). VrRig adds this component
// by itself in the simulation.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Eosat1
{
    [DefaultExecutionOrder(30)]   // after VrRig (poses) and ViewDirector (the camera)
    public sealed class VrWristCamera : MonoBehaviour
    {
        public VrRig rig;

        [Header("Check your watch (as on the left wrist)")]
        [Tooltip("How squarely the wrist screen must face your eyes to open the picture (1 = straight at them).")]
        [Range(0f, 1f)] public float openFacing = 0.7f;
        [Tooltip("The picture folds away once the screen turns further than this from your eyes.")]
        [Range(0f, 1f)] public float closeFacing = 0.4f;
        [Tooltip("How far from the middle of your view the wrist may be when the picture opens, in degrees.")]
        public float openViewAngle = 40f;
        [Tooltip("How far from the middle of your view the wrist may go before the picture folds, in degrees.")]
        public float closeViewAngle = 60f;
        [Tooltip("The furthest the wrist can be from your eyes, in metres.")]
        public float maxDistance = 0.75f;
        public float openDelay = 0.12f;
        public float closeDelay = 0.35f;

        [Header("Look")]
        public float popSeconds = 0.22f;
        [Tooltip("How far the bottom of the picture stands out of the wrist screen, towards your eyes, in metres.")]
        public float standOff = 0.035f;
        [Tooltip("Size of the pop-out. 1 = a 22 cm picture.")]
        public float padScale = 0.85f;
        public Color accent = new Color(0.25f, 0.8f, 1f, 1f);

        public bool IsOpen { get { return open; } }

        // the pop-out's layout, metres before padScale; origin at the bottom centre, x right, y up
        const float W = 0.24f;
        const float Pad = 0.012f;
        const float Picture = W - 2f * Pad;
        const float HeaderH = 0.016f;
        const float FooterH = 0.014f;
        const float PictureY = Pad + FooterH + 0.005f + Picture * 0.5f;
        const float H = PictureY + Picture * 0.5f + 0.004f + HeaderH + Pad;

        static readonly Color Amber = new Color(1f, 0.82f, 0.4f, 1f);
        static readonly Color TextBright = new Color(0.93f, 0.96f, 1f, 1f);

        ViewDirector views;
        Font font;
        Shader panelShader, unlitShader, screenShader;
        Material textMaterial, beamMaterial, rimMaterial, wristPictureMaterial, padPictureMaterial;
        readonly List<Material> ownMaterials = new List<Material>();
        readonly List<Mesh> ownMeshes = new List<Mesh>();

        Transform armFrame, wrist, screen, wristPicture;
        bool ownArmFrame;
        Transform padRoot, box;
        readonly Transform[] boxSides = new Transform[4];
        readonly Transform[] beams = new Transform[2];
        TextMesh headerRight, footer;

        bool built, open;
        float shown, pending, nextText;
        int waitFrames;

        // ------------------------------------------------------------------ setting up
        void Start()
        {
            if (rig == null) rig = GetComponent<VrRig>();
            if (rig == null || rig.app == null)
            {
                enabled = false;
                return;
            }
            Shader textShader = Shader.Find("EOSAT1/HudText");
            panelShader = Shader.Find("EOSAT1/UiPanel");
            unlitShader = Shader.Find("EOSAT1/UnlitColor");
            screenShader = Resources.Load<Shader>("EosatScreen");
            if (screenShader == null) screenShader = Shader.Find("EOSAT1/Screen");
            if (textShader == null || panelShader == null || screenShader == null)
            {
                Debug.LogWarning("EOSAT-1: the satellite camera on the right wrist needs the shaders EOSAT1/HudText, EOSAT1/UiPanel " +
                                 "and EOSAT1/Screen; the screen over the right controller is used instead.");
                enabled = false;
                return;
            }
            if (unlitShader == null) unlitShader = panelShader;
            font = VrWristPad.LoadFont();
            textMaterial = MenuKit.NewMaterial(textShader, "Wrist Camera Text");
            ownMaterials.Add(textMaterial);
            AssignFontTexture();
        }

        void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
            Font.textureRebuilt += OnFontRebuilt;
            if (built && views != null) views.wristCameraTakesOver = true;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            Font.textureRebuilt -= OnFontRebuilt;
            // switched off in the Inspector: the screen over the right controller comes back
            if (views != null) views.wristCameraTakesOver = false;
            open = false;
            shown = 0f;
            ShowPad(false);
        }

        void OnDestroy()
        {
            for (int i = 0; i < ownMaterials.Count; i++) if (ownMaterials[i] != null) Destroy(ownMaterials[i]);
            for (int i = 0; i < ownMeshes.Count; i++) if (ownMeshes[i] != null) Destroy(ownMeshes[i]);
        }

        void OnFontRebuilt(Font f)
        {
            if (f == font) AssignFontTexture();
        }

        void AssignFontTexture()
        {
            if (font != null && font.material != null && textMaterial != null)
                textMaterial.SetTexture("_MainTex", font.material.mainTexture);
        }

        /// <summary>Built once the right glove and the satellite camera (ViewDirector) exist.</summary>
        void TryBuild()
        {
            if (rig.rightHand == null || rig.head == null) return;
            if (views == null) views = rig.views != null ? rig.views : FindAnyObjectByType<ViewDirector>();
            VrHands hands = rig.GetComponent<VrHands>();
            Transform glove = hands != null ? hands.RightGlove : null;
            bool waitForGlove = glove == null && hands != null && hands.isActiveAndEnabled;
            if ((views == null || waitForGlove) && waitFrames < 120)
            {
                waitFrames++;
                return;
            }
            if (views == null)
            {
                enabled = false;   // no satellite camera in this scene
                return;
            }
            if (glove != null) armFrame = glove;
            else
            {
                // no gloves: hang the wrist computer where the glove's sleeve would be
                armFrame = MenuKit.Group("Wrist Camera Arm", rig.rightHand, Vector3.zero);
                ownArmFrame = true;
                PlaceOwnArmFrame();
            }
            BuildWrist(glove != null);
            BuildPad();
            views.wristCameraTakesOver = true;
            built = true;
        }

        void PlaceOwnArmFrame()
        {
            Quaternion mount = VrHands.GloveMounting(rig, false);
            armFrame.localRotation = mount;
            armFrame.localPosition = -(mount * VrHands.GripPoint);
        }

        /// <summary>
        /// The same dark block as on the left sleeve, on a strap, with a screen that shows the live
        /// picture. In the glove's axes: fingers +Z, back of the hand +Y, the sleeve back along -Z.
        /// </summary>
        void BuildWrist(bool onGlove)
        {
            wrist = MenuKit.Group("Wrist Camera Computer", armFrame, new Vector3(0f, 0.0525f, -0.112f));
            wrist.localRotation = Quaternion.Euler(6.5f, 0f, 0f);   // lies along the sleeve, which narrows towards the hand
            Shader suit = VrHands.SuitShader();
            if (suit != null)
            {
                Material caseMat = VrHands.NewMaterial(suit, "EOSAT1 Wrist Camera Case", new Color(0.16f, 0.18f, 0.21f), 0.35f);
                ownMaterials.Add(caseMat);
                Mesh body = VrHands.RoundedBox(0.025f, 0.0075f, 0.031f, 0.006f, "EOSAT1 Wrist Camera Case");
                ownMeshes.Add(body);
                MenuKit.MeshObject("Case", wrist, body, caseMat, Vector3.zero, Vector3.one);
                if (onGlove)
                {
                    Material strapMat = VrHands.NewMaterial(suit, "EOSAT1 Wrist Camera Strap", new Color(0.1f, 0.11f, 0.13f), 0.15f);
                    ownMaterials.Add(strapMat);
                    Mesh strap = VrHands.Tube(0.0528f, 0.0528f, 0.016f, 32, "EOSAT1 Wrist Camera Strap");
                    ownMeshes.Add(strap);
                    MeshRenderer s = MenuKit.MeshObject("Wrist Camera Strap", armFrame, strap, strapMat, new Vector3(0f, 0f, -0.12f), Vector3.one);
                    VrWristPad.SetLayer(s.gameObject);
                }
            }

            // the screen's rim faces out of the back of the wrist (the quad's front is -Z; turned, it faces +Y)
            MeshRenderer rim = MenuKit.Panel("Screen", wrist, panelShader, new Vector3(0f, 0.0077f, 0f), new Vector2(0.036f, 0.046f),
                                             new Color(0.02f, 0.03f, 0.05f, 1f), new Color(0.01f, 0.02f, 0.04f, 1f),
                                             VrWristPad.WithAlpha(accent, 0.6f), 0.04f, 0.16f, MenuKit.OrderPanel);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            screen = rim.transform;
            rimMaterial = rim.sharedMaterial;
            ownMaterials.Add(rimMaterial);

            // the live picture on it, square, with its top towards the far side of the forearm, so it
            // reads the right way up when the wrist is turned towards you
            wristPictureMaterial = new Material(screenShader) { name = "EOSAT1 Wrist Camera Picture" };
            ownMaterials.Add(wristPictureMaterial);
            MeshRenderer picture = MenuKit.MeshObject("Picture", wrist, MenuKit.Quad, wristPictureMaterial, new Vector3(0f, 0.0079f, 0f),
                                                      new Vector3(0.03f, 0.03f, 1f));
            picture.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.right);
            wristPicture = picture.transform;
            wristPicture.gameObject.SetActive(false);
            VrWristPad.SetLayer(wrist.gameObject);
        }

        void BuildPad()
        {
            padRoot = MenuKit.Group("Wrist Camera Picture", rig.transform, Vector3.zero);
            Color top = new Color(0.03f, 0.065f, 0.115f, 0.96f), bottom = new Color(0.012f, 0.025f, 0.05f, 0.96f);
            MeshRenderer back = MenuKit.Panel("Background", padRoot, panelShader, new Vector3(0f, H * 0.5f, 0f), new Vector2(W, H), top, bottom,
                                              VrWristPad.WithAlpha(accent, 0.85f), 0.0018f / H, 0.014f / H, MenuKit.OrderPanel);
            ownMaterials.Add(back.sharedMaterial);

            // the heading: the instrument's name, then how wide the view is
            float headerY = H - Pad - HeaderH * 0.5f;
            Label("Title", new Vector3(-W * 0.5f + Pad, headerY, -0.003f), "<b>DRAGONEYE</b>", 0.011f, accent, TextAnchor.MiddleLeft);
            headerRight = Label("View", new Vector3(W * 0.5f - Pad, headerY, -0.003f), "", 0.0085f, TextBright, TextAnchor.MiddleRight);

            // the picture itself (opaque, in front of the background)
            padPictureMaterial = new Material(screenShader) { name = "EOSAT1 Wrist Camera Picture (big)" };
            ownMaterials.Add(padPictureMaterial);
            MenuKit.MeshObject("Picture", padRoot, MenuKit.Quad, padPictureMaterial, new Vector3(0f, PictureY, -0.002f),
                               new Vector3(Picture, Picture, 1f));

            // a cross at the boresight, and the real 44 km view as a box while zoomed out
            Material mark = MenuKit.NewMaterial(unlitShader, "Wrist Camera Marks");
            mark.SetColor("_Color", Amber);
            ownMaterials.Add(mark);
            var centre = new Vector3(0f, PictureY, -0.003f);
            MenuKit.MeshObject("Cross Across", padRoot, MenuKit.Quad, mark, centre, new Vector3(0.012f, 0.0008f, 1f));
            MenuKit.MeshObject("Cross Up", padRoot, MenuKit.Quad, mark, centre, new Vector3(0.0008f, 0.012f, 1f));
            box = MenuKit.Group("Real View", padRoot, centre);
            for (int i = 0; i < boxSides.Length; i++)
                boxSides[i] = MenuKit.MeshObject("Side", box, MenuKit.Quad, mark, Vector3.zero, Vector3.one).transform;
            box.gameObject.SetActive(false);

            footer = Label("Figures", new Vector3(0f, Pad + FooterH * 0.5f, -0.003f), "", 0.0085f, TextBright, TextAnchor.MiddleCenter);

            // two faint beams from the wrist screen to the bottom corners while it is out
            beamMaterial = MenuKit.NewMaterial(panelShader, "Wrist Camera Beam");
            beamMaterial.SetColor("_Color", VrWristPad.WithAlpha(accent, 0.35f));
            beamMaterial.SetColor("_Color2", VrWristPad.WithAlpha(accent, 0.05f));
            beamMaterial.SetColor("_BorderColor", new Color(0f, 0f, 0f, 0f));
            beamMaterial.SetFloat("_Border", 0f);
            beamMaterial.SetFloat("_Radius", 0f);
            beamMaterial.SetFloat("_Aspect", 0.02f);
            ownMaterials.Add(beamMaterial);
            for (int i = 0; i < beams.Length; i++)
            {
                MeshRenderer b = MenuKit.MeshObject("Wrist Camera Beam", rig.transform, MenuKit.Quad, beamMaterial, Vector3.zero, Vector3.one);
                b.sortingOrder = MenuKit.OrderPanel;
                beams[i] = b.transform;
                VrWristPad.SetLayer(b.gameObject);
                b.gameObject.SetActive(false);
            }

            VrWristPad.SetLayer(padRoot.gameObject);
            padRoot.gameObject.SetActive(false);
        }

        TextMesh Label(string name, Vector3 pos, string text, float lineHeight, Color color, TextAnchor anchor)
        {
            return MenuKit.Text(name, padRoot, pos, text, lineHeight, color, anchor, font, textMaterial);
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!built)
            {
                TryBuild();
                if (!built) return;
            }
            float dt = Time.unscaledDeltaTime;
            if (ownArmFrame) PlaceOwnArmFrame();   // follows the controller's pose type once it is known

            // the wrist screen shows the picture while the camera is on
            bool live = views.CameraLive;
            if (wristPicture.gameObject.activeSelf != live) wristPicture.gameObject.SetActive(live);
            if (live)
            {
                SetPicture(wristPictureMaterial);
                SetPicture(padPictureMaterial);
            }

            bool allowed = XRSettings.isDeviceActive && live
                           && (rig.Menu == null || !rig.Menu.IsOpen)
                           && rig.rightHand.gameObject.activeInHierarchy;
            bool want = allowed && Looking(open);
            if (want != open)
            {
                pending += dt;
                if (!allowed || pending >= (want ? openDelay : closeDelay)) SetOpen(want);
            }
            else pending = 0f;

            float speed = 1f / Mathf.Max(0.05f, open ? popSeconds : popSeconds * 0.7f);
            shown = Mathf.MoveTowards(shown, open ? 1f : 0f, dt * speed);
            ShowPad(shown > 0.001f);
            if (padRoot.gameObject.activeSelf && Time.unscaledTime >= nextText)
            {
                nextText = Time.unscaledTime + 0.25f;
                RefreshText();
            }
            rimMaterial.SetColor("_BorderColor", VrWristPad.WithAlpha(accent, live ? (open ? 0.95f : 0.75f) : 0.2f));
        }

        void SetPicture(Material m)
        {
            RenderTexture picture = views.CameraPicture;
            if (m != null && m.mainTexture != picture) m.mainTexture = picture;
        }

        /// <summary>Is the right wrist screen turned towards your eyes, in front of you, like a watch you are reading?</summary>
        bool Looking(bool alreadyOpen)
        {
            Transform head = rig.head;
            if (head == null || screen == null) return false;
            Vector3 toEye = head.position - screen.position;
            float distance = toEye.magnitude;
            if (distance < 0.08f || distance > maxDistance + (alreadyOpen ? 0.1f : 0f)) return false;
            toEye /= distance;
            float facing = Vector3.Dot(wrist.up, toEye);
            float offCentre = Vector3.Angle(head.forward, -toEye);
            if (alreadyOpen) return facing > closeFacing && offCentre < closeViewAngle;
            return facing > openFacing && offCentre < openViewAngle;
        }

        void SetOpen(bool value)
        {
            pending = 0f;
            if (open == value) return;
            open = value;
            if (open)
            {
                RefreshText();
                VrRig.Buzz(true, 0.18f, 0.03f);
            }
        }

        void ShowPad(bool visible)
        {
            if (padRoot != null && padRoot.gameObject.activeSelf != visible) padRoot.gameObject.SetActive(visible);
            for (int i = 0; i < beams.Length; i++)
                if (beams[i] != null && beams[i].gameObject.activeSelf != visible) beams[i].gameObject.SetActive(visible);
        }

        [BeforeRenderOrder(110)]   // after VrRig has read this frame's head and hand poses (100)
        void OnBeforeRender()
        {
            if (built && padRoot != null && padRoot.gameObject.activeSelf) Place();
        }

        /// <summary>Stand the picture on the wrist, facing your eyes, as far popped out as 'shown' says.</summary>
        void Place()
        {
            Transform head = rig.head;
            if (head == null || screen == null) return;
            Vector3 watch = screen.position;
            Vector3 anchor = watch + wrist.up * standOff;
            Vector3 fwd = anchor - head.position;
            if (fwd.sqrMagnitude < 1e-6f) fwd = head.forward;
            Vector3 up = Vector3.ProjectOnPlane(head.up, fwd);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            Quaternion facing = Quaternion.LookRotation(fwd, up);

            float t = Mathf.Clamp01(shown);
            float grow = open ? VrWristPad.EaseOutBack(t) : t * t * (3f - 2f * t);
            float s = Mathf.Max(0.02f, grow) * Mathf.Max(0.1f, padScale);
            float move = 1f - (1f - t) * (1f - t);
            padRoot.SetPositionAndRotation(Vector3.Lerp(watch, anchor, move), facing);
            padRoot.localScale = new Vector3(s, s, s);

            // the beams: from the two ends of the wrist screen to the bottom corners, left to left
            Vector3 endA = wrist.TransformPoint(new Vector3(0f, 0.008f, -0.02f));
            Vector3 endB = wrist.TransformPoint(new Vector3(0f, 0.008f, 0.02f));
            if (Vector3.Dot(endA - endB, facing * Vector3.right) > 0f)
            {
                Vector3 swap = endA;
                endA = endB;
                endB = swap;
            }
            Vector3 eye = head.position;
            VrWristPad.PlaceBeam(beams[0], endA, padRoot.TransformPoint(new Vector3(-W * 0.5f, 0f, 0f)), eye);
            VrWristPad.PlaceBeam(beams[1], endB, padRoot.TransformPoint(new Vector3(W * 0.5f, 0f, 0f)), eye);
            beamMaterial.SetColor("_Color", VrWristPad.WithAlpha(accent, 0.35f * t));
            beamMaterial.SetColor("_Color2", VrWristPad.WithAlpha(accent, 0.05f * t));
        }

        // ------------------------------------------------------------------ the figures
        void RefreshText()
        {
            if (views == null) return;
            if (headerRight != null && headerRight.text != views.HeadsetHeader) headerRight.text = views.HeadsetHeader;
            if (footer != null && footer.text != views.HeadsetFooter) footer.text = views.HeadsetFooter;

            // the real instrument's view, as a box, while zoomed out (as on the laptop's picture)
            float fov = Mathf.Clamp(views.fieldOfViewDeg, 0.1f, 179f);
            float fraction = Mathf.Tan(0.5f * ViewDirector.TrueFieldOfViewDeg * Mathf.Deg2Rad) / Mathf.Tan(0.5f * fov * Mathf.Deg2Rad);
            bool showBox = fraction < 0.7f;
            if (box.gameObject.activeSelf != showBox) box.gameObject.SetActive(showBox);
            if (!showBox) return;
            float b = Mathf.Max(0.004f, Picture * fraction);
            const float line = 0.0008f;
            boxSides[0].localPosition = new Vector3(0f, b * 0.5f, 0f);
            boxSides[0].localScale = new Vector3(b + line, line, 1f);
            boxSides[1].localPosition = new Vector3(0f, -b * 0.5f, 0f);
            boxSides[1].localScale = new Vector3(b + line, line, 1f);
            boxSides[2].localPosition = new Vector3(-b * 0.5f, 0f, 0f);
            boxSides[2].localScale = new Vector3(line, b + line, 1f);
            boxSides[3].localPosition = new Vector3(b * 0.5f, 0f, 0f);
            boxSides[3].localScale = new Vector3(line, b + line, 1f);
        }
    }
}
