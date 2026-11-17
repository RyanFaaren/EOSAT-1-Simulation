// EOSAT-1 VR - the jetpack: fly around the spacecraft to see it from every side, including
// from above and below.
//
//   Left stick        fly forwards / back / sideways (where you look)
//   Right stick up/down   rise / sink
//
// It behaves like a gas-jet backpack: it speeds up and slows down smoothly instead of
// starting and stopping dead, which is also easier on the stomach. You hear the jets hiss
// and feel them in the controllers.
//
// The flight zone keeps you near the spacecraft: a cylinder around it, 6 m in radius, from
// 3 m below your starting height to 3.5 m above it. Close to the edge a cyan grid appears,
// like the headset's own room boundary, and the jets will not push you through it. Walking
// with your own legs is never blocked. The VR menu has a "Back to start" button.
//
// VrRig adds this component by itself in the simulation. To change the numbers for good,
// add the component to the Player Rig yourself and set them in the Inspector.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace Eosat1
{
    public sealed class VrJetpack : MonoBehaviour
    {
        public VrRig rig;

        [Header("Flying")]
        [Tooltip("Top speed forwards, back and sideways, m/s.")]
        public float flySpeed = 1.4f;
        [Tooltip("Top speed up and down, m/s.")]
        public float climbSpeed = 1f;
        [Tooltip("How quickly the jets bring you up to speed, m/s per second.")]
        public float acceleration = 3f;
        [Tooltip("How quickly you stop when you let go of the sticks, m/s per second.")]
        public float braking = 4f;

        [Header("Flight zone")]
        [Tooltip("How far from the spacecraft you can fly, in metres (measured sideways from its centre to your head).")]
        public float flightRadius = 6f;
        [Tooltip("How far below the starting height the jetpack can take you, in metres.")]
        public float flightFloor = -3f;
        [Tooltip("How far above the starting height the jetpack can take you, in metres.")]
        public float flightCeiling = 3.5f;
        [Tooltip("The grid at the edge of the flight zone starts to show this many metres before the edge.")]
        public float edgeWarning = 1.2f;
        public Color edgeColor = new Color(0.25f, 0.8f, 1f);

        [Header("Feel")]
        public bool sound = true;
        [Range(0f, 1f)] public float soundVolume = 0.2f;
        public bool rumble = true;

        /// <summary>How hard the jets are working, 0 to 1.</summary>
        public float Thrust { get; private set; }
        /// <summary>True while the jets are being held back by the edge of the flight zone.</summary>
        public bool AtEdge { get; private set; }

        Vector3 velocity;
        Vector3 centre = new Vector3(0f, 1.45f, 0f);
        GameObject edge;
        Material edgeMaterial;
        Mesh edgeMesh;
        AudioSource hiss;
        AudioClip hissClip;
        float edgeShown, nextRumble, lastThrustTime = -100f, edgeBump;
        string lastLine;
        bool wasAtEdge;

        // ------------------------------------------------------------------ setting up
        void Start()
        {
            if (rig == null) rig = GetComponent<VrRig>();
            if (rig != null && rig.app != null && rig.app.space != null) centre = rig.app.space.transform.position;
            BuildEdge();
            if (sound) BuildSound();
        }

        void OnDestroy()
        {
            if (edge != null) Destroy(edge);
            if (edgeMaterial != null) Destroy(edgeMaterial);
            if (edgeMesh != null) Destroy(edgeMesh);
            if (hissClip != null) Destroy(hissClip);
        }

        public void Stop()
        {
            velocity = Vector3.zero;
        }

        void Update()
        {
            // headset taken off: VrRig stops calling Fly, so quieten the jets here
            if (XRSettings.isDeviceActive) return;
            Thrust = 0f;
            if (hiss != null) hiss.volume = 0f;
            if (edge != null && edge.activeSelf) edge.SetActive(false);
        }

        // ------------------------------------------------------------------ flying (called by VrRig every frame)
        public void Fly(Vector2 move, float climb, float dt)
        {
            if (rig == null || rig.head == null) return;
            Transform body = rig.transform;
            Transform head = rig.head;

            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = body.forward;
            fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Vector3 wish = (fwd * move.y + side * move.x) * flySpeed + Vector3.up * (climb * climbSpeed);
            bool pushing = wish.sqrMagnitude > 1e-4f;
            velocity = Vector3.MoveTowards(velocity, wish, (pushing ? acceleration : braking) * dt);
            Thrust = Mathf.Clamp01(Mathf.Max(move.magnitude, Mathf.Abs(climb)));
            if (pushing) lastThrustTime = Time.unscaledTime;

            // the jets stop at the edge of the flight zone
            bool blocked = false;
            Vector3 step = velocity * dt;
            Vector3 pos = body.position;
            float y = pos.y + step.y;
            if (y > flightCeiling && step.y > 0f)
            {
                y = Mathf.Max(pos.y, flightCeiling);
                velocity.y = 0f;
                blocked = true;
            }
            else if (y < flightFloor && step.y < 0f)
            {
                y = Mathf.Min(pos.y, flightFloor);
                velocity.y = 0f;
                blocked = true;
            }
            var flat = new Vector3(step.x, 0f, step.z);
            Vector3 fromCentre = head.position + flat - centre;
            fromCentre.y = 0f;
            float distance = fromCentre.magnitude;
            if (distance > flightRadius && distance > 1e-4f)
            {
                Vector3 outDir = fromCentre / distance;
                float outward = Vector3.Dot(flat, outDir);
                if (outward > 0f)
                {
                    flat -= outDir * outward;
                    float speedOut = Vector3.Dot(velocity, outDir);
                    if (speedOut > 0f) velocity -= outDir * speedOut;
                    blocked = true;
                }
            }
            body.position = new Vector3(pos.x + flat.x, y, pos.z + flat.z);
            AtEdge = blocked && pushing;

            Feedback(move, climb, dt);
        }

        void Feedback(Vector2 move, float climb, float dt)
        {
            Transform head = rig.head;
            float height = rig.transform.position.y;
            Vector3 fromCentre = head.position - centre;
            float above = fromCentre.y;
            fromCentre.y = 0f;

            // how close to the edge, 0 (far) to 1 (at it)
            float warn = Mathf.Max(0.1f, edgeWarning);
            float near = Mathf.InverseLerp(flightRadius - warn, flightRadius, fromCentre.magnitude);
            near = Mathf.Max(near, Mathf.InverseLerp(flightCeiling - warn, flightCeiling, height));
            near = Mathf.Max(near, Mathf.InverseLerp(flightFloor + warn, flightFloor, height));
            if (AtEdge && !wasAtEdge)
            {
                edgeBump = 1f;
                if (rumble)
                {
                    VrRig.Buzz(false, 0.7f, 0.12f);
                    VrRig.Buzz(true, 0.7f, 0.12f);
                }
            }
            wasAtEdge = AtEdge;
            edgeBump = Mathf.MoveTowards(edgeBump, 0f, dt * 2f);
            edgeShown = Mathf.MoveTowards(edgeShown, Mathf.Clamp01(near), dt * 3f);
            if (edge != null)
            {
                bool show = edgeShown > 0.01f;
                if (edge.activeSelf != show) edge.SetActive(show);
                if (show && edgeMaterial != null)
                {
                    float k = edgeShown * (2f + 2.5f * edgeBump);
                    edgeMaterial.SetColor("_Color", new Color(edgeColor.r * k, edgeColor.g * k, edgeColor.b * k, 1f));
                }
            }

            // the jets: a hiss, and a light rumble in the hand that is steering
            if (hiss != null)
            {
                float want = sound ? Thrust * soundVolume : 0f;
                hiss.volume = Mathf.MoveTowards(hiss.volume, want, dt * 1.5f);
                hiss.pitch = 0.9f + 0.25f * Thrust;
            }
            if (rumble && Thrust > 0.05f && Time.unscaledTime >= nextRumble)
            {
                nextRumble = Time.unscaledTime + 0.1f;
                if (move.sqrMagnitude > 0.01f) VrRig.Buzz(false, 0.05f + 0.1f * move.magnitude, 0.06f);
                if (Mathf.Abs(climb) > 0.05f) VrRig.Buzz(true, 0.05f + 0.1f * Mathf.Abs(climb), 0.06f);
            }

            // a line of text while flying: how high you are compared with the spacecraft
            VrMenu menu = rig.Menu;
            if (menu == null) return;
            string line = null;
            if (Time.unscaledTime - lastThrustTime < 1.5f)
            {
                float tenths = Mathf.Round(Mathf.Abs(above) * 10f) / 10f;
                if (tenths < 0.15f) line = "JETPACK   level with the satellite";
                else line = "JETPACK   " + tenths.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m "
                            + (above > 0f ? "above" : "below") + " the satellite";
                if (AtEdge) line += "   <color=" + HudStyle.WarnHex + ">edge of the flight zone</color>";
            }
            if (line != lastLine)
            {
                lastLine = line;
                menu.SetFlightLine(line);
            }
        }

        // ------------------------------------------------------------------ the edge of the flight zone
        void BuildEdge()
        {
            Shader sh = Shader.Find("EOSAT1/GlowAdditive");
            if (sh == null) return;
            edgeMaterial = new Material(sh);
            edgeMaterial.name = "EOSAT1 Flight Zone Edge (runtime)";
            edgeMaterial.SetColor("_Color", Color.black);
            if (edgeMaterial.HasProperty("_Power")) edgeMaterial.SetFloat("_Power", 2f);

            // the limit is on your head, so the grid stands a little further out than the limit
            float radius = flightRadius + 0.5f;
            float bottom = flightFloor - 0.2f, top = flightCeiling + 2.4f;
            edgeMesh = GridCylinder(radius, bottom, top, 1f, 24, 0.012f);
            edge = new GameObject("Flight Zone Edge");
            edge.transform.position = new Vector3(centre.x, 0f, centre.z);
            edge.layer = ViewDirector.HiddenFromSatCameraLayer;
            edge.AddComponent<MeshFilter>().sharedMesh = edgeMesh;
            var r = edge.AddComponent<MeshRenderer>();
            r.sharedMaterial = edgeMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            edge.SetActive(false);
        }

        /// <summary>
        /// A cage seen from inside: hoops every 'spacing' metres of height and upright bars,
        /// as thin strips facing the middle.
        /// </summary>
        static Mesh GridCylinder(float radius, float bottom, float top, float spacing, int bars, float halfWidth)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            const int segments = 72;
            // hoops
            for (float y = bottom; y <= top + 1e-3f; y += Mathf.Max(0.25f, spacing))
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = 2f * Mathf.PI * i / segments, a1 = 2f * Mathf.PI * (i + 1) / segments;
                    var p0 = new Vector3(Mathf.Cos(a0) * radius, y, Mathf.Sin(a0) * radius);
                    var p1 = new Vector3(Mathf.Cos(a1) * radius, y, Mathf.Sin(a1) * radius);
                    Strip(v, n, t, p0 - Vector3.up * halfWidth, p1 - Vector3.up * halfWidth, p0 + Vector3.up * halfWidth, p1 + Vector3.up * halfWidth);
                }
            }
            // bars
            float da = halfWidth / Mathf.Max(0.1f, radius);
            for (int i = 0; i < bars; i++)
            {
                float a = 2f * Mathf.PI * i / bars;
                var q0 = new Vector3(Mathf.Cos(a - da) * radius, 0f, Mathf.Sin(a - da) * radius);
                var q1 = new Vector3(Mathf.Cos(a + da) * radius, 0f, Mathf.Sin(a + da) * radius);
                Strip(v, n, t, q0 + Vector3.up * bottom, q1 + Vector3.up * bottom, q0 + Vector3.up * top, q1 + Vector3.up * top);
            }
            var m = new Mesh();
            m.name = "EOSAT1 Flight Zone Edge";
            m.hideFlags = HideFlags.HideAndDontSave;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A four-cornered strip on the cylinder, facing its axis.</summary>
        static void Strip(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 inward = -(a + b + c + d) * 0.25f;
            inward.y = 0f;
            inward.Normalize();
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(inward); n.Add(inward); n.Add(inward); n.Add(inward);
            // wound so that the side facing the axis is the front
            if (Vector3.Dot(Vector3.Cross(c - a, b - a), inward) > 0f)
            {
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i + 1); t.Add(i + 2); t.Add(i + 3);
            }
            else
            {
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i + 1); t.Add(i + 3); t.Add(i + 2);
            }
        }

        // ------------------------------------------------------------------ the jets' hiss
        void BuildSound()
        {
            if (rig == null || rig.head == null) return;
            const int rate = 22050;
            int total = rate * 2;
            int fade = rate / 5;
            var raw = new float[total];
            var random = new System.Random(7);
            float low = 0f, lower = 0f;
            for (int i = 0; i < total; i++)
            {
                // white noise with the harsh top taken off: a soft rush of gas
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (white - low) * 0.35f;
                lower += (low - lower) * 0.5f;
                raw[i] = lower * 1.6f;
            }
            // blend the end into the start so the loop has no click
            int length = total - fade;
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                if (i < fade)
                {
                    float k = i / (float)fade;
                    data[i] = raw[i] * k + raw[length + i] * (1f - k);
                }
                else data[i] = raw[i];
            }
            hissClip = AudioClip.Create("EOSAT1 Jetpack", length, 1, rate, false);
            hissClip.SetData(data, 0);
            hiss = rig.head.gameObject.AddComponent<AudioSource>();
            hiss.clip = hissClip;
            hiss.loop = true;
            hiss.playOnAwake = false;
            hiss.spatialBlend = 0f;
            hiss.volume = 0f;
            hiss.Play();
        }
    }
}
