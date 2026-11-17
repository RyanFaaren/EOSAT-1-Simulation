// EOSAT-1 VR - a slider for the simulation speed, while the simulation runs.
// It snaps to the same steps as the - and = keys (0.5x ... 300x), so keys and slider agree.
//
// Desktop:  a bar at the bottom right of the screen; click or drag it (the camera doesn't
//           move while you do).
// Headset:  a bar on the left controller, above the info panel. Point the right controller
//           at it and hold the right GRIP, then slide (the right trigger is already "body axes").
//
// Nothing to set up: this script adds itself to the simulation scene when it loads.
// When the time comes from the live simulator (Live link), the slider is greyed out.
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Eosat1
{
    [DefaultExecutionOrder(20)]   // after VrRig, so the controller poses are this frame's
    public sealed class SpeedSlider : MonoBehaviour
    {
        public EosatApp app;
        public float vrBarWidth = 0.3f;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Color Accent = new Color(0.25f, 0.8f, 1f, 1f);
        static readonly Color Track = new Color(0.12f, 0.17f, 0.24f, 1f);
        static readonly Color Back = new Color(0.02f, 0.04f, 0.07f, 1f);
        static readonly Color Greyed = new Color(0.35f, 0.38f, 0.42f, 1f);

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
            EosatApp a = FindAnyObjectByType<EosatApp>();
            if (a == null || a.GetComponent<SpeedSlider>() != null) return;
            a.gameObject.AddComponent<SpeedSlider>().app = a;
        }

        // ------------------------------------------------------------------ shared
        DesktopControls desktopControls;
        VrRig rig;
        int lastIndex = -1;

        int Steps { get { return app != null && app.speedSteps != null ? app.speedSteps.Length : 0; } }
        bool CanChange { get { return app != null && app.CanControlTime && Steps > 1; } }

        int IndexAt(float t)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(t) * (Steps - 1)), 0, Steps - 1);
        }

        float PositionOf(int index)
        {
            return Steps > 1 ? index / (float)(Steps - 1) : 0f;
        }

        static string SpeedText(float s)
        {
            return (s < 1f ? s.ToString("0.0", Inv) : s.ToString("0", Inv)) + "x";
        }

        string StatusText()
        {
            if (app == null) return "";
            if (!app.CanControlTime) return "SPEED   set by the simulator";
            return "SPEED   " + SpeedText(app.Speed) + (app.paused ? "   (PAUSED)" : "");
        }

        void Start()
        {
            if (app == null) app = GetComponent<EosatApp>();
            desktopControls = FindAnyObjectByType<DesktopControls>();
            rig = FindAnyObjectByType<VrRig>();
            BuildVrBar();
        }

        void OnDisable()
        {
            if (desktopControls != null) desktopControls.enabled = true;
            mouseDragging = false;
        }

        void Update()
        {
            if (app == null) return;
            bool headset = XRSettings.isDeviceActive;
            if (headset) UpdateVr();
            else UpdateDesktop();
            if (vrRoot != null && vrRoot.activeSelf != headset) vrRoot.SetActive(headset);
            if (headset) RefreshVrBar();
        }

        void SetIndex(int index)
        {
            if (!CanChange || index == app.speedIndex) return;
            app.SetSpeedIndex(index);
        }

        // ------------------------------------------------------------------ desktop
        Rect panelRect, trackRect;
        bool mouseDragging;
        Texture2D white;
        GUIStyle labelStyle, tickStyle;

        void LayoutDesktop()
        {
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            float w = Mathf.Min(Screen.width * 0.4f, 620f * scale);
            float h = 78f * scale;
            float helpHeight = app.infoPanel != null && app.infoPanel.showHelp ? Mathf.Max(11, Screen.height / 72) * 1.4f + 26f : 0f;
            panelRect = new Rect(Screen.width - w - 10f, Screen.height - helpHeight - h - 12f, w, h);   // right edge, clear of the panels on the left
            float pad = 22f * scale;
            trackRect = new Rect(panelRect.x + pad, panelRect.y + h * 0.5f - 3f * scale, w - 2f * pad, 6f * scale);
        }

        void UpdateDesktop()
        {
            LayoutDesktop();
            Vector2 m = InputShim.MousePosition();
            var gui = new Vector2(m.x, Screen.height - m.y);   // GUI space has y going down
            bool over = panelRect.Contains(gui);

            if (!mouseDragging && over && CanChange && InputShim.MouseDown(0)) mouseDragging = true;
            if (mouseDragging && !InputShim.MouseHeld(0)) mouseDragging = false;
            if (mouseDragging) SetIndex(IndexAt((gui.x - trackRect.x) / Mathf.Max(1f, trackRect.width)));

            // keep the camera still while the mouse is on the slider
            bool block = mouseDragging || (over && (InputShim.MouseHeld(0) || InputShim.MouseHeld(1)));
            if (desktopControls != null && desktopControls.enabled == block) desktopControls.enabled = !block;
        }

        void OnGUI()
        {
            if (app == null || XRSettings.isDeviceActive) return;
            if (white == null)
            {
                white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
                white.hideFlags = HideFlags.HideAndDontSave;
            }
            LayoutDesktop();
            float scale = panelRect.height / 78f;
            if (labelStyle == null || labelStyle.fontSize != Mathf.RoundToInt(15f * scale))
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15f * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                tickStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(11f * scale), alignment = TextAnchor.UpperCenter };
            }
            bool enabledLook = CanChange;
            Color on = enabledLook ? Accent : Greyed;

            Fill(panelRect, new Color(Back.r, Back.g, Back.b, 0.82f));
            Fill(new Rect(panelRect.x, panelRect.y, panelRect.width, 2f), new Color(on.r, on.g, on.b, 0.7f));

            labelStyle.normal.textColor = enabledLook ? new Color(0.93f, 0.96f, 1f) : Greyed;
            GUI.Label(new Rect(trackRect.x, panelRect.y + 4f * scale, trackRect.width, 22f * scale), StatusText(), labelStyle);

            // track, filled part, notches with their speeds, and the knob
            Fill(trackRect, Track);
            float t = PositionOf(Mathf.Clamp(app.speedIndex, 0, Mathf.Max(0, Steps - 1)));
            Fill(new Rect(trackRect.x, trackRect.y, trackRect.width * t, trackRect.height), on);
            tickStyle.normal.textColor = new Color(0.6f, 0.7f, 0.8f);
            for (int i = 0; i < Steps; i++)
            {
                float x = trackRect.x + trackRect.width * PositionOf(i);
                Fill(new Rect(x - 1f, trackRect.y - 4f * scale, 2f, trackRect.height + 8f * scale), new Color(0.6f, 0.7f, 0.8f, 0.6f));
                GUI.Label(new Rect(x - 30f * scale, trackRect.yMax + 6f * scale, 60f * scale, 18f * scale), SpeedText(app.speedSteps[i]), tickStyle);
            }
            float knob = 16f * scale;
            float kx = trackRect.x + trackRect.width * t;
            Fill(new Rect(kx - knob * 0.5f, trackRect.center.y - knob * 0.5f, knob, knob), mouseDragging ? Color.white : on);
        }

        void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ headset
        GameObject vrRoot;
        Transform vrTrack, vrFill, vrKnob;
        TextMesh vrLabel;
        LineRenderer vrLaser;
        Material fillMaterial, knobMaterial, textMaterial;
        Font font;
        bool vrDragging, prevGrip;

        void BuildVrBar()
        {
            Transform left = rig != null ? rig.leftHand : null;
            if (left == null) return;
            Shader unlit = Shader.Find("EOSAT1/UnlitColor");
            Shader textShader = Shader.Find("EOSAT1/HudText");
            if (unlit == null || textShader == null) return;

            // above the info panel, tilted the same way so you read both at once
            vrRoot = new GameObject("Speed Slider");
            vrRoot.transform.SetParent(left, false);
            vrRoot.transform.localPosition = new Vector3(0f, 0.18f, 0.125f);
            vrRoot.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            Transform r = vrRoot.transform;

            float w = vrBarWidth;
            Quad("Backing", r, NewMat(unlit, Back), new Vector3(0f, 0f, 0.002f), new Vector2(w + 0.03f, 0.075f));
            vrTrack = Quad("Track", r, NewMat(unlit, Track), new Vector3(0f, -0.004f, 0.001f), new Vector2(w, 0.008f));
            fillMaterial = NewMat(unlit, Accent);
            vrFill = Quad("Fill", r, fillMaterial, new Vector3(0f, -0.004f, 0.0005f), new Vector2(w, 0.008f));
            Material notch = NewMat(unlit, new Color(0.6f, 0.7f, 0.8f, 1f));
            knobMaterial = NewMat(unlit, Accent);
            vrKnob = Quad("Knob", r, knobMaterial, new Vector3(0f, -0.004f, -0.0005f), new Vector2(0.016f, 0.016f));

            font = LoadFont();
            textMaterial = NewMat(textShader, Color.white);
            if (font != null && font.material != null) textMaterial.SetTexture("_MainTex", font.material.mainTexture);
            Font.textureRebuilt += OnFontRebuilt;

            vrLabel = Text("Label", r, new Vector3(-w * 0.5f, 0.027f, 0f), "", 0.011f, Color.white, TextAnchor.MiddleLeft);
            Text("How", r, new Vector3(w * 0.5f, 0.027f, 0f), "point + hold right grip", 0.0075f, new Color(0.6f, 0.7f, 0.8f), TextAnchor.MiddleRight);
            for (int i = 0; i < Steps; i++)
            {
                float x = -w * 0.5f + w * PositionOf(i);
                Quad("Notch", r, notch, new Vector3(x, -0.004f, 0.0008f), new Vector2(0.0015f, 0.014f));
                Text("Tick", r, new Vector3(x, -0.016f, 0f), SpeedText(app.speedSteps[i]), 0.0075f, new Color(0.6f, 0.7f, 0.8f), TextAnchor.UpperCenter);
            }

            // a short laser from the right controller while you aim at the bar
            if (rig.rightHand != null)
            {
                var laserGo = new GameObject("Speed Slider Laser");
                laserGo.transform.SetParent(rig.rightHand, false);
                vrLaser = laserGo.AddComponent<LineRenderer>();
                vrLaser.sharedMaterial = NewMat(unlit, Color.Lerp(Accent, Color.white, 0.35f));
                vrLaser.widthMultiplier = 0.002f;
                vrLaser.positionCount = 2;
                vrLaser.useWorldSpace = true;
                vrLaser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                vrLaser.receiveShadows = false;
                vrLaser.enabled = false;
            }
            vrRoot.SetActive(false);
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
        }

        void OnFontRebuilt(Font f)
        {
            if (f == font && textMaterial != null && f.material != null) textMaterial.SetTexture("_MainTex", f.material.mainTexture);
        }

        void UpdateVr()
        {
            if (desktopControls != null && !desktopControls.enabled) desktopControls.enabled = true;
            if (vrRoot == null || rig == null || rig.rightHand == null || !rig.rightHand.gameObject.activeInHierarchy)
            {
                if (vrLaser != null) vrLaser.enabled = false;
                return;
            }

            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool grip = false;
            bool value;
            if (right.isValid && right.TryGetFeatureValue(CommonUsages.gripButton, out value)) grip = value;
            bool gripDown = grip && !prevGrip;
            prevGrip = grip;

            var ray = new Ray(rig.rightHand.position, rig.rightHand.forward);
            Vector3 local;
            float distance;
            float w = vrBarWidth;
            bool hits = MenuKit.RayToLocalPlane(ray, vrRoot.transform, out local, out distance) && distance < 1.5f;
            bool onBar = hits && Mathf.Abs(local.x) <= w * 0.5f + 0.02f && Mathf.Abs(local.y) <= 0.04f;

            if (gripDown && onBar && CanChange) vrDragging = true;
            if (!grip) vrDragging = false;
            if (vrDragging && hits) SetIndex(IndexAt((local.x + w * 0.5f) / w));

            if (vrLaser != null)
            {
                bool show = onBar || vrDragging;
                vrLaser.enabled = show;
                if (show)
                {
                    vrLaser.SetPosition(0, ray.origin);
                    vrLaser.SetPosition(1, ray.origin + ray.direction * (hits ? distance : 0.3f));
                }
            }

            // a little buzz at every step
            if (app.speedIndex != lastIndex)
            {
                if (lastIndex >= 0 && right.isValid) right.SendHapticImpulse(0u, 0.3f, 0.03f);
                lastIndex = app.speedIndex;
            }
        }

        void RefreshVrBar()
        {
            if (vrRoot == null) return;
            float w = vrBarWidth;
            float t = PositionOf(Mathf.Clamp(app.speedIndex, 0, Mathf.Max(0, Steps - 1)));
            Color on = CanChange ? Accent : Greyed;
            vrFill.localScale = new Vector3(Mathf.Max(0.0001f, w * t), 0.008f, 1f);
            vrFill.localPosition = new Vector3(-w * 0.5f + w * t * 0.5f, -0.004f, 0.0005f);
            vrKnob.localPosition = new Vector3(-w * 0.5f + w * t, -0.004f, -0.0005f);
            fillMaterial.SetColor("_Color", on);
            knobMaterial.SetColor("_Color", vrDragging ? Color.white : on);
            string s = StatusText();
            if (vrLabel.text != s) vrLabel.text = s;
        }

        // ------------------------------------------------------------------ small helpers
        static Material NewMat(Shader shader, Color color)
        {
            var m = new Material(shader);
            m.hideFlags = HideFlags.DontSave;
            m.SetColor("_Color", color);
            return m;
        }

        static Transform Quad(string name, Transform parent, Material mat, Vector3 pos, Vector2 size)
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Collider c = q.GetComponent<Collider>();
            if (c != null) Destroy(c);
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q.transform;
        }

        TextMesh Text(string name, Transform parent, Vector3 pos, string text, float lineHeight, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            const int fontSize = 64;
            tm.font = font;
            tm.fontSize = fontSize;
            tm.characterSize = lineHeight * 10f / fontSize;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.MiddleRight ? TextAlignment.Right
                         : anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = textMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tm;
        }

        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (Exception) { f = null; }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch (Exception) { f = null; }
            }
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 64);
            return f;
        }
    }
}
