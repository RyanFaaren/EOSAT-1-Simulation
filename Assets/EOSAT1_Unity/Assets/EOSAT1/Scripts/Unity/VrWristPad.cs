// EOSAT-1 VR - the info pad: a wrist computer on the left sleeve of the space suit.
//
// Turn your left wrist towards your face, as if you were checking a watch, and the pad pops
// out of it: slide 17's four questions as four lit tiles (Is it tumbling? Is the camera on
// Earth? Are the panels in the sun? Did the antenna come out?), the time, the data source and
// where the satellite is, and the time-speed bar along the bottom. Lower your arm or turn the
// wrist away and it folds back in.
//
// The pad stands a few centimetres off the wrist, on the side facing you and upright for your
// eyes, so the glove never covers it (the old panel sat on the controller, behind the hand).
// While it is folded, the four small lights on the wrist screen still show the answers, and
// the screen's rim blinks amber (red when lost) while the live link is weak, with a buzz when
// that starts.
//
// X turns the pad off and on (Settings > Info panel sets it at start). It also stays folded
// while the VR menu is open. VrRig adds this component by itself in the simulation; the
// numbers below can be tried out in the Inspector while playing.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace Eosat1
{
    [DefaultExecutionOrder(30)]   // after VrRig (poses), VrMenu and SpeedSlider
    public sealed class VrWristPad : MonoBehaviour
    {
        public VrRig rig;

        [Header("Check your watch")]
        [Tooltip("How squarely the wrist screen must face your eyes to open the pad (1 = straight at them, 0.7 = within about 45 degrees).")]
        [Range(0f, 1f)] public float openFacing = 0.7f;
        [Tooltip("The pad folds away once the screen turns further than this from your eyes (looser than opening, so it doesn't flicker).")]
        [Range(0f, 1f)] public float closeFacing = 0.4f;
        [Tooltip("How far from the middle of your view the wrist may be when the pad opens, in degrees.")]
        public float openViewAngle = 40f;
        [Tooltip("How far from the middle of your view the wrist may go before the pad folds, in degrees.")]
        public float closeViewAngle = 60f;
        [Tooltip("The furthest the wrist can be from your eyes, in metres.")]
        public float maxDistance = 0.75f;
        [Tooltip("Seconds the wrist must be held towards you before the pad opens.")]
        public float openDelay = 0.12f;
        [Tooltip("Seconds after the wrist turns away before the pad folds.")]
        public float closeDelay = 0.35f;

        [Header("Look")]
        [Tooltip("Seconds the pad takes to pop out.")]
        public float popSeconds = 0.22f;
        [Tooltip("How far the bottom of the pad stands out of the wrist screen, towards your eyes, in metres.")]
        public float standOff = 0.035f;
        [Tooltip("Size of the pad. 1 = 26 cm wide.")]
        public float padScale = 0.85f;
        public Color accent = new Color(0.25f, 0.8f, 1f, 1f);

        public bool IsOpen { get { return open; } }

        // the pad's layout, in metres before padScale; x to the right, y up, origin at the top left
        const float W = 0.26f;
        const float Pad = 0.011f;
        const float HeaderH = 0.017f;
        const float TileGap = 0.006f;
        const float TileW = (W - 2f * Pad - TileGap) * 0.5f;
        const float TileH = 0.047f;
        const float TilesTop = Pad + HeaderH + 0.008f;
        const float RowsTop = TilesTop + 2f * TileH + TileGap + 0.008f;
        const float RowLine = 0.0076f;
        const float RowSpacing = 1.4f;
        const float RowPitch = RowLine * RowSpacing;
        const float LabelW = 0.052f;
        const float ColGap = 0.007f;
        const float SliderGap = 0.007f;
        const int MaxRows = 7;
        // how many letters fit in the value column (a letter is about 0.56 of the line height wide)
        static readonly int MaxChars = Mathf.FloorToInt((W - 2f * Pad - LabelW - ColGap) / (0.56f * RowLine));

        static readonly Color TextBright = new Color(0.93f, 0.96f, 1f, 1f);
        static readonly Color TextDim = new Color(0.55f, 0.66f, 0.76f, 1f);
        static readonly Color GoodColor = new Color(0.49f, 0.99f, 0.6f, 1f);
        static readonly Color WarnColor = new Color(1f, 0.82f, 0.4f, 1f);
        static readonly Color BadColor = new Color(1f, 0.42f, 0.42f, 1f);
        static readonly Color UnknownColor = new Color(0.5f, 0.57f, 0.66f, 1f);
        static readonly char[] TagStops = { '=', ' ', '>' };

        sealed class Tile
        {
            public Material Back, Light;
            public TextMesh Value, Detail;
        }

        InfoPanel info;
        Font font;
        Material textMaterial, beamMaterial, screenMaterial;
        Shader panelShader, unlitShader;
        readonly List<Material> ownMaterials = new List<Material>();
        readonly List<Mesh> ownMeshes = new List<Mesh>();

        // on the arm
        Transform armFrame, wrist, screen;
        bool ownArmFrame;
        readonly Material[] lights = new Material[4];

        // the pad
        Transform padRoot, content, sliderMount, dockedBar;
        Vector2 dockedBarSize;
        MeshRenderer background;
        TextMesh headerRight, rowLabels, rowValues;
        readonly Tile[] tiles = new Tile[4];
        readonly Transform[] beams = new Transform[2];
        readonly StringBuilder labelText = new StringBuilder(256);
        readonly StringBuilder valueText = new StringBuilder(1024);

        bool built, open, warned;
        float shown, pending, nextDockTry, stopDocking;
        int waitFrames, lastVersion = -1, shownRows = -1;

        // ------------------------------------------------------------------ setting up
        void Start()
        {
            if (rig == null) rig = GetComponent<VrRig>();
            if (rig == null || rig.app == null)
            {
                enabled = false;
                return;
            }
            info = rig.infoPanel != null ? rig.infoPanel : rig.app.infoPanel;
            Shader textShader = Shader.Find("EOSAT1/HudText");
            panelShader = Shader.Find("EOSAT1/UiPanel");
            unlitShader = Shader.Find("EOSAT1/UnlitColor");
            if (info == null || textShader == null || panelShader == null)
            {
                Debug.LogWarning("EOSAT-1: the wrist info pad needs the InfoPanel and the shaders EOSAT1/HudText and " +
                                 "EOSAT1/UiPanel (EOSAT1/Shaders folder); the old panel on the left controller is used instead.");
                enabled = false;
                return;
            }
            if (unlitShader == null) unlitShader = panelShader;
            font = LoadFont();
            textMaterial = MenuKit.NewMaterial(textShader, "Info Pad Text");
            ownMaterials.Add(textMaterial);
            AssignFontTexture();
            stopDocking = Time.unscaledTime + 20f;
        }

        void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
            Font.textureRebuilt += OnFontRebuilt;
            if (built && info != null) info.wristPadTakesOver = true;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            Font.textureRebuilt -= OnFontRebuilt;
            // switched off in the Inspector: the old panel on the left controller comes back
            if (info != null) info.wristPadTakesOver = false;
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

        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (Exception) { f = null; }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }   // Unity 2021 and older
                catch (Exception) { f = null; }
            }
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 64);
            return f;
        }

        /// <summary>Built once the left glove exists (VrHands makes it in its own Start).</summary>
        void TryBuild()
        {
            if (rig.leftHand == null || rig.head == null) return;
            VrHands hands = rig.GetComponent<VrHands>();
            Transform glove = hands != null ? hands.LeftGlove : null;
            if (glove == null && hands != null && hands.isActiveAndEnabled && waitFrames < 60)
            {
                waitFrames++;
                return;
            }
            if (glove != null) armFrame = glove;
            else
            {
                // no gloves: hang the wrist computer where the glove's sleeve would be
                armFrame = MenuKit.Group("Info Pad Arm", rig.leftHand, Vector3.zero);
                ownArmFrame = true;
                PlaceOwnArmFrame();
            }
            BuildWrist(glove != null);
            BuildPad();
            info.wristPadTakesOver = true;
            if (info.worldPanel != null && info.worldPanel.activeSelf) info.worldPanel.SetActive(false);
            built = true;
            RefreshWrist();
        }

        void PlaceOwnArmFrame()
        {
            Quaternion mount = VrHands.GloveMounting(rig, true);
            armFrame.localRotation = mount;
            armFrame.localPosition = -(mount * VrHands.GripPoint);
        }

        /// <summary>
        /// A dark block on the back of the forearm, between the glove's wrist ring and the red stripe,
        /// on a strap round the sleeve, with a screen and four small lights on top.
        /// In the glove's axes: fingers +Z, back of the hand +Y, the sleeve runs back along -Z.
        /// </summary>
        void BuildWrist(bool onGlove)
        {
            wrist = MenuKit.Group("Info Pad Wrist Computer", armFrame, new Vector3(0f, 0.0525f, -0.112f));
            wrist.localRotation = Quaternion.Euler(6.5f, 0f, 0f);   // lies along the sleeve, which narrows towards the hand
            Shader suit = VrHands.SuitShader();
            if (suit != null)
            {
                Material caseMat = VrHands.NewMaterial(suit, "EOSAT1 Wrist Computer", new Color(0.16f, 0.18f, 0.21f), 0.35f);
                ownMaterials.Add(caseMat);
                Mesh body = VrHands.RoundedBox(0.025f, 0.0075f, 0.031f, 0.006f, "EOSAT1 Wrist Computer");
                ownMeshes.Add(body);
                MenuKit.MeshObject("Case", wrist, body, caseMat, Vector3.zero, Vector3.one);
                if (onGlove)
                {
                    Material strapMat = VrHands.NewMaterial(suit, "EOSAT1 Wrist Strap", new Color(0.1f, 0.11f, 0.13f), 0.15f);
                    ownMaterials.Add(strapMat);
                    Mesh strap = VrHands.Tube(0.0528f, 0.0528f, 0.016f, 32, "EOSAT1 Wrist Strap");
                    ownMeshes.Add(strap);
                    MeshRenderer s = MenuKit.MeshObject("Info Pad Wrist Strap", armFrame, strap, strapMat, new Vector3(0f, 0f, -0.12f), Vector3.one);
                    SetLayer(s.gameObject);
                }
            }

            // the screen faces out of the back of the wrist (the quad's front is -Z; turned, it faces +Y)
            Quaternion faceUp = Quaternion.Euler(90f, 0f, 0f);
            MeshRenderer scr = MenuKit.Panel("Screen", wrist, panelShader, new Vector3(0f, 0.0077f, 0f), new Vector2(0.036f, 0.046f),
                                             new Color(0.02f, 0.07f, 0.12f, 1f), new Color(0.01f, 0.04f, 0.08f, 1f),
                                             WithAlpha(accent, 0.6f), 0.04f, 0.16f, MenuKit.OrderPanel);
            scr.transform.localRotation = faceUp;
            screen = scr.transform;
            screenMaterial = scr.sharedMaterial;
            ownMaterials.Add(screenMaterial);
            // one light per question, from the elbow end to the hand end (left to right as you look)
            for (int i = 0; i < lights.Length; i++)
            {
                MeshRenderer dot = MenuKit.Panel("Light " + (i + 1), wrist, panelShader, new Vector3(0f, 0.0079f, -0.0135f + 0.009f * i),
                                                 new Vector2(0.0065f, 0.0065f), UnknownColor, UnknownColor, Color.white, 0.12f, 0.5f,
                                                 MenuKit.OrderMarker);
                dot.transform.localRotation = faceUp;
                lights[i] = dot.sharedMaterial;
                ownMaterials.Add(lights[i]);
            }
            SetLayer(wrist.gameObject);
        }

        void BuildPad()
        {
            padRoot = MenuKit.Group("Info Pad", rig.transform, Vector3.zero);
            Color top = new Color(0.03f, 0.065f, 0.115f, 0.96f), bottom = new Color(0.012f, 0.025f, 0.05f, 0.96f);
            background = MenuKit.Panel("Background", padRoot, panelShader, new Vector3(0f, 0.1f, 0f), new Vector2(W, 0.2f), top, bottom,
                                       WithAlpha(accent, 0.85f), 0.01f, 0.07f, MenuKit.OrderPanel);
            ownMaterials.Add(background.sharedMaterial);
            content = MenuKit.Group("Content", padRoot, new Vector3(-W * 0.5f, 0.2f, -0.003f));

            // the header: the title, and the time with the speed or PAUSED
            float headerY = -(Pad + HeaderH * 0.5f);
            Label("Title", content, new Vector3(Pad, headerY, 0f), MenuKit.Spaced("EOSAT-1"), 0.012f, accent, TextAnchor.MiddleLeft);
            headerRight = Label("Time", content, new Vector3(W - Pad, headerY, 0f), "", 0.009f, TextBright, TextAnchor.MiddleRight);
            Bar("Divider", content, new Vector3(W * 0.5f, -(Pad + HeaderH + 0.003f), 0.0005f), new Vector2(W - 2f * Pad, 0.0012f),
                Color.Lerp(Color.black, accent, 0.5f));

            // the four questions, two by two
            for (int i = 0; i < tiles.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var topLeft = new Vector3(Pad + col * (TileW + TileGap), -(TilesTop + row * (TileH + TileGap)), 0f);
                string question = i < info.Answers.Length ? info.Answers[i].Question : "";
                tiles[i] = BuildTile(question, topLeft);
            }

            // the other rows: labels in one column, values in the next
            rowLabels = Label("Row Labels", content, new Vector3(Pad, -RowsTop, 0f), "", RowLine, TextDim, TextAnchor.UpperLeft);
            rowLabels.lineSpacing = RowSpacing;
            rowValues = Label("Row Values", content, new Vector3(Pad + LabelW + ColGap, -RowsTop, 0f), "", RowLine, TextBright, TextAnchor.UpperLeft);
            rowValues.lineSpacing = RowSpacing;
            sliderMount = MenuKit.Group("Speed Bar", content, new Vector3(W * 0.5f, -RowsTop, -0.001f));

            // two faint beams from the wrist screen to the pad's bottom corners while it is out
            beamMaterial = MenuKit.NewMaterial(panelShader, "Info Pad Beam");
            beamMaterial.SetColor("_Color", WithAlpha(accent, 0.35f));
            beamMaterial.SetColor("_Color2", WithAlpha(accent, 0.05f));
            beamMaterial.SetColor("_BorderColor", new Color(0f, 0f, 0f, 0f));
            beamMaterial.SetFloat("_Border", 0f);
            beamMaterial.SetFloat("_Radius", 0f);
            beamMaterial.SetFloat("_Aspect", 0.02f);
            ownMaterials.Add(beamMaterial);
            for (int i = 0; i < beams.Length; i++)
            {
                MeshRenderer b = MenuKit.MeshObject("Info Pad Beam", rig.transform, MenuKit.Quad, beamMaterial, Vector3.zero, Vector3.one);
                b.sortingOrder = MenuKit.OrderPanel;
                beams[i] = b.transform;
                SetLayer(b.gameObject);
                b.gameObject.SetActive(false);
            }

            SetLayer(padRoot.gameObject);
            padRoot.gameObject.SetActive(false);
            Layout(0);
        }

        Tile BuildTile(string question, Vector3 topLeft)
        {
            var t = new Tile();
            Transform g = MenuKit.Group("Tile " + question, content, topLeft);
            Color top = new Color(0.06f, 0.1f, 0.16f, 0.95f), bottom = new Color(0.035f, 0.06f, 0.1f, 0.95f);
            MeshRenderer back = MenuKit.Panel("Background", g, panelShader, new Vector3(TileW * 0.5f, -TileH * 0.5f, 0.001f),
                                              new Vector2(TileW, TileH), top, bottom, WithAlpha(UnknownColor, 0.4f), 0.035f, 0.18f,
                                              MenuKit.OrderButton);
            t.Back = back.sharedMaterial;
            ownMaterials.Add(t.Back);
            MeshRenderer lamp = MenuKit.Panel("Light", g, panelShader, new Vector3(0.0115f, -0.0095f, -0.0005f), new Vector2(0.0075f, 0.0075f),
                                              UnknownColor, UnknownColor, Color.white, 0.14f, 0.5f, MenuKit.OrderMarker);
            t.Light = lamp.sharedMaterial;
            ownMaterials.Add(t.Light);
            Label("Question", g, new Vector3(0.019f, -0.0095f, 0f), question, 0.0072f, TextDim, TextAnchor.MiddleLeft);
            t.Value = Label("Answer", g, new Vector3(0.008f, -0.0255f, 0f), "", 0.0135f, UnknownColor, TextAnchor.MiddleLeft);
            t.Detail = Label("Detail", g, new Vector3(0.008f, -0.0395f, 0f), "", 0.0068f, TextDim, TextAnchor.MiddleLeft);
            return t;
        }

        TextMesh Label(string name, Transform parent, Vector3 pos, string text, float lineHeight, Color color, TextAnchor anchor)
        {
            return MenuKit.Text(name, parent, pos, text, lineHeight, color, anchor, font, textMaterial);
        }

        void Bar(string name, Transform parent, Vector3 pos, Vector2 size, Color color)
        {
            Material m = MenuKit.NewMaterial(unlitShader, name);
            m.SetColor("_Color", color);
            ownMaterials.Add(m);
            MenuKit.MeshObject(name, parent, MenuKit.Quad, m, pos, new Vector3(size.x, size.y, 1f));
        }

        /// <summary>Move the speed slider's headset bar (SpeedSlider builds it) onto the bottom of the pad.</summary>
        void TryDock()
        {
            float now = Time.unscaledTime;
            if (dockedBar != null || now < nextDockTry || now > stopDocking) return;
            nextDockTry = now + 0.5f;
            SpeedSlider slider = FindAnyObjectByType<SpeedSlider>();
            if (slider == null || slider.VrBar == null) return;
            dockedBar = slider.VrBar;
            dockedBarSize = slider.VrBarSize;
            dockedBar.SetParent(sliderMount, false);
            dockedBar.localPosition = Vector3.zero;
            dockedBar.localRotation = Quaternion.identity;
            SetLayer(dockedBar.gameObject);
            Layout(Mathf.Max(0, shownRows));
        }

        /// <summary>Size the pad for this many rows, plus the speed bar once it is docked.</summary>
        void Layout(int rows)
        {
            float y = RowsTop + rows * RowPitch;
            if (dockedBar != null)
            {
                float k = (W - 2f * Pad) / Mathf.Max(0.01f, dockedBarSize.x);
                float barH = dockedBarSize.y * k;
                if (rows > 0) y += SliderGap;
                sliderMount.localPosition = new Vector3(W * 0.5f, -(y + barH * 0.5f), -0.001f);
                dockedBar.localScale = new Vector3(k, k, k);
                y += barH;
            }
            float h = y + Pad;
            background.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            background.transform.localScale = new Vector3(W, h, 1f);
            Material m = background.sharedMaterial;
            m.SetFloat("_Aspect", W / h);
            m.SetFloat("_Border", 0.0018f / h);
            m.SetFloat("_Radius", 0.014f / h);
            content.localPosition = new Vector3(-W * 0.5f, h, -0.003f);
        }

        static void SetLayer(GameObject go)
        {
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = ViewDirector.HiddenFromSatCameraLayer;
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!built)
            {
                TryBuild();
                if (!built) return;
            }
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            if (ownArmFrame) PlaceOwnArmFrame();   // follows the controller's pose type once it is known
            TryDock();

            bool allowed = XRSettings.isDeviceActive
                           && (info.showOnScreen || info.HasLinkWarning)
                           && (rig.Menu == null || !rig.Menu.IsOpen)
                           && rig.leftHand.gameObject.activeInHierarchy;
            // while the right hand works the speed bar, the pad stays out
            bool want = allowed && (Looking(open) || (open && SpeedSlider.HoldsRightGrip));
            if (want != open)
            {
                pending += dt;
                if (!allowed || pending >= (want ? openDelay : closeDelay)) SetOpen(want);
            }
            else pending = 0f;

            float speed = 1f / Mathf.Max(0.05f, open ? popSeconds : popSeconds * 0.7f);
            shown = Mathf.MoveTowards(shown, open ? 1f : 0f, dt * speed);
            ShowPad(shown > 0.001f);

            if (info.Version != lastVersion)
            {
                lastVersion = info.Version;
                RefreshWrist();
                if (padRoot.gameObject.activeSelf) RefreshPad();
            }
            UpdateScreenRim(now);
        }

        /// <summary>Is the wrist screen turned towards your eyes, in front of you, like a watch you are reading?</summary>
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
                RefreshPad();
                VrRig.Buzz(false, 0.18f, 0.03f);
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

        /// <summary>Stand the pad on the wrist, facing your eyes, as far popped out as 'shown' says.</summary>
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
            float grow = open ? EaseOutBack(t) : t * t * (3f - 2f * t);
            float s = Mathf.Max(0.02f, grow) * Mathf.Max(0.1f, padScale);
            float move = 1f - (1f - t) * (1f - t);
            padRoot.SetPositionAndRotation(Vector3.Lerp(watch, anchor, move), facing);
            padRoot.localScale = new Vector3(s, s, s);

            // the beams: from the two ends of the wrist screen to the pad's bottom corners, left to left
            Vector3 endA = wrist.TransformPoint(new Vector3(0f, 0.008f, -0.02f));
            Vector3 endB = wrist.TransformPoint(new Vector3(0f, 0.008f, 0.02f));
            if (Vector3.Dot(endA - endB, facing * Vector3.right) > 0f)
            {
                Vector3 swap = endA;
                endA = endB;
                endB = swap;
            }
            Vector3 eye = head.position;
            PlaceBeam(beams[0], endA, padRoot.TransformPoint(new Vector3(-W * 0.5f, 0f, 0f)), eye);
            PlaceBeam(beams[1], endB, padRoot.TransformPoint(new Vector3(W * 0.5f, 0f, 0f)), eye);
            beamMaterial.SetColor("_Color", WithAlpha(accent, 0.35f * t));
            beamMaterial.SetColor("_Color2", WithAlpha(accent, 0.05f * t));
        }

        /// <summary>A thin strip from 'from' to 'to', turned to face the eye.</summary>
        static void PlaceBeam(Transform beam, Vector3 from, Vector3 to, Vector3 eye)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1e-4f)
            {
                beam.localScale = Vector3.zero;
                return;
            }
            Vector3 dir = d / length;
            Vector3 mid = (from + to) * 0.5f;
            Vector3 normal = Vector3.ProjectOnPlane(eye - mid, dir);
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(dir, Vector3.up);
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(dir, Vector3.right);
            beam.SetPositionAndRotation(mid, Quaternion.LookRotation(-normal.normalized, dir));
            beam.localScale = new Vector3(0.0022f, length, 1f);
        }

        /// <summary>0 -> 1 with a small overshoot at the end: the pop.</summary>
        static float EaseOutBack(float t)
        {
            const float c1 = 1.3f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ------------------------------------------------------------------ what it shows
        void RefreshWrist()
        {
            for (int i = 0; i < lights.Length && i < info.Answers.Length; i++)
            {
                if (lights[i] == null) continue;
                Color c = LevelColor(info.Answers[i].Level);
                lights[i].SetColor("_Color", c);
                lights[i].SetColor("_Color2", c * new Color(0.75f, 0.75f, 0.75f, 1f));
            }
        }

        /// <summary>The screen's rim: steady cyan, or blinking amber (red when lost) while the live link is weak.</summary>
        void UpdateScreenRim(float now)
        {
            if (screenMaterial == null) return;
            Color rim;
            if (info.HasLinkWarning)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * 7f);
                rim = WithAlpha(info.LinkLost ? BadColor : WarnColor, 0.3f + 0.7f * pulse);
                if (!warned)
                {
                    warned = true;
                    if (XRSettings.isDeviceActive) VrRig.Buzz(false, 0.5f, 0.08f);
                }
            }
            else
            {
                warned = false;
                rim = WithAlpha(accent, open ? 0.95f : 0.6f);
            }
            screenMaterial.SetColor("_BorderColor", rim);
        }

        void RefreshPad()
        {
            // the header: PAUSED or the speed, then the date and time
            string chip = info.HeaderChip;
            string chipText = "";
            if (!string.IsNullOrEmpty(chip))
            {
                string hex = chip == "PAUSED" ? HudStyle.WarnHex : chip == "FROZEN" ? "#88CCFF" : HudStyle.AccentHex;
                chipText = "<color=" + hex + ">" + chip + "</color>   ";
            }
            SetText(headerRight, chipText + info.HeaderTime);

            // the four questions
            for (int i = 0; i < tiles.Length && i < info.Answers.Length; i++)
            {
                InfoPanel.Answer a = info.Answers[i];
                Tile t = tiles[i];
                Color c = LevelColor(a.Level);
                t.Light.SetColor("_Color", c);
                t.Light.SetColor("_Color2", c * new Color(0.75f, 0.75f, 0.75f, 1f));
                t.Light.SetColor("_BorderColor", Color.Lerp(c, Color.white, 0.5f));
                t.Back.SetColor("_BorderColor", WithAlpha(c, a.Level == InfoPanel.AnswerLevel.Unknown ? 0.4f : 0.75f));
                SetText(t.Value, "<b>" + a.Value + "</b>");
                if (t.Value.color != c) t.Value.color = c;
                SetText(t.Detail, a.Detail);
            }

            // everything else, one row each: the link warning first, then where the data comes from
            labelText.Length = 0;
            valueText.Length = 0;
            int rows = 0;
            if (info.HasLinkWarning && info.LinkWarningShort != null)
            {
                string hex = info.LinkLost ? HudStyle.BadHex : HudStyle.WarnHex;
                AddRow(rows++, "<color=" + hex + ">Link</color>", "<color=" + hex + ">" + info.LinkWarningShort + "</color>");
            }
            for (int i = 0; i < info.RowCount && rows < MaxRows; i++)
            {
                if (info.RowIsQuestion(i)) continue;
                string label = info.RowLabel(i);
                string value = label == "Source" && !string.IsNullOrEmpty(info.SourceShort) ? info.SourceShort : info.RowValue(i);
                AddRow(rows++, label, Clip(value, MaxChars));
            }
            SetText(rowLabels, labelText.ToString());
            SetText(rowValues, valueText.ToString());
            if (rows != shownRows)
            {
                shownRows = rows;
                Layout(rows);
            }
        }

        void AddRow(int index, string label, string value)
        {
            if (index > 0)
            {
                labelText.Append('\n');
                valueText.Append('\n');
            }
            labelText.Append(label);
            valueText.Append(value);
        }

        static void SetText(TextMesh t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }

        static Color LevelColor(InfoPanel.AnswerLevel level)
        {
            switch (level)
            {
                case InfoPanel.AnswerLevel.Good: return GoodColor;
                case InfoPanel.AnswerLevel.Warn: return WarnColor;
                case InfoPanel.AnswerLevel.Bad: return BadColor;
                default: return UnknownColor;
            }
        }

        static Color WithAlpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }

        /// <summary>Cut a line of rich text to at most 'max' visible letters, ending in an ellipsis, keeping its tags whole.</summary>
        static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int visible = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end > i)
                    {
                        i = end;
                        continue;
                    }
                }
                visible++;
            }
            if (visible <= max) return s;

            var sb = new StringBuilder(s.Length + 16);
            var openTags = new List<string>();
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end > i)
                    {
                        string tag = s.Substring(i, end - i + 1);
                        if (tag.Length > 2 && tag[1] == '/')
                        {
                            if (openTags.Count > 0) openTags.RemoveAt(openTags.Count - 1);
                        }
                        else
                        {
                            int stop = tag.IndexOfAny(TagStops, 1);
                            if (stop < 2) stop = tag.Length - 1;
                            openTags.Add(tag.Substring(1, stop - 1));
                        }
                        sb.Append(tag);
                        i = end;
                        continue;
                    }
                }
                if (count >= max - 1) break;
                sb.Append(c);
                count++;
            }
            sb.Append('…');
            for (int k = openTags.Count - 1; k >= 0; k--) sb.Append("</").Append(openTags[k]).Append('>');
            return sb.ToString();
        }
    }
}
