// EOSAT-1 VR - space-suit gloves in place of the plain controller blocks. Each glove is
// built in code (no model files): a padded palm, four fingers and a thumb with rubber
// fingertips, a metal wrist ring and the suit's sleeve with a red stripe.
//
// The fingers move with your real ones: squeeze the grip and three fingers close, pull the
// trigger and the index finger curls, rest your thumb on a button or the stick and the
// thumb comes down.
//
// VrRig adds this component by itself, in the simulation and in the main menu room, so an
// existing scene needs no rebuild. In the simulation each sleeve also gets a wrist computer:
// the info pad on the left (VrWristPad) and the satellite camera on the right (VrWristCamera).
//
// Where the glove sits on the controller: OpenXR reports the "grip" pose, whose axes are
// fixed by the standard (palm on the X axis, the handle along Z). Other runtimes report a
// pose that points forwards. VrRig tells the two apart by comparing with the aim pose, and
// both are handled; if a glove still sits at an odd angle on some headset, turn it with
// Left Tweak / Right Tweak in the Inspector while playing.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace Eosat1
{
    public sealed class VrHands : MonoBehaviour
    {
        [Tooltip("The rig that tracks the hands; it knows which kind of pose the controllers report.")]
        public VrRig rig;
        public Transform leftHand;
        public Transform rightHand;
        [Tooltip("Extra turn of the left glove, in degrees, on top of the built-in mounting.")]
        public Vector3 leftTweak;
        [Tooltip("Extra turn of the right glove, in degrees, on top of the built-in mounting.")]
        public Vector3 rightTweak;
        [Tooltip("How fast the fingers follow the grip and trigger.")]
        public float fingerSpeed = 16f;
        [Tooltip("1 = a real glove's size. Suit gloves are bulky, so a little more looks right.")]
        public float size = 1.08f;

        public Color fabric = new Color(0.9f, 0.9f, 0.88f);
        public Color padding = new Color(0.6f, 0.62f, 0.65f);
        public Color rubber = new Color(0.2f, 0.22f, 0.25f);
        public Color metal = new Color(0.42f, 0.55f, 0.72f);
        public Color stripe = new Color(0.8f, 0.14f, 0.1f);

        /// <summary>The point of the glove that sits on the controller's grip: inside the curled fingers.</summary>
        public static readonly Vector3 GripPoint = new Vector3(0f, -0.035f, 0.015f);

        sealed class Finger
        {
            public Transform[] Joints;
            public Quaternion[] Base;
            public float[] Open, Closed;
        }

        sealed class Glove
        {
            public Transform Root;
            public XRNode Node;
            public bool Left;
            public Finger Thumb, Index;
            public Finger[] Others;
            public float Grip, Trigger, ThumbDown;
        }

        Glove left, right;

        /// <summary>
        /// The left glove once it is built (null before, or without gloves). Its axes: fingers along
        /// +Z, back of the hand +Y, the sleeve running back along -Z; scaled by 'size'.
        /// </summary>
        public Transform LeftGlove { get { return left != null ? left.Root : null; } }
        /// <summary>The right glove once it is built (VrWristCamera hangs the satellite camera on it); same axes as the left.</summary>
        public Transform RightGlove { get { return right != null ? right.Root : null; } }
        Material fabricMat, paddingMat, rubberMat, metalMat, stripeMat;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Dictionary<int, Mesh> capsules = new Dictionary<int, Mesh>();
        Mesh palmMesh, padMesh, ringMesh, sleeveMesh, stripeMesh;
        bool built;

        // ------------------------------------------------------------------ setting up
        void Start()
        {
            Build();
        }

        void Build()
        {
            if (built || (leftHand == null && rightHand == null)) return;
            Shader sh = SuitShader();
            if (sh == null) return;
            fabricMat = NewMaterial(sh, "EOSAT1 Glove Fabric", fabric, 0.1f);
            paddingMat = NewMaterial(sh, "EOSAT1 Glove Padding", padding, 0.05f);
            rubberMat = NewMaterial(sh, "EOSAT1 Glove Rubber", rubber, 0.25f);
            metalMat = NewMaterial(sh, "EOSAT1 Glove Ring", metal, 0.9f);
            stripeMat = NewMaterial(sh, "EOSAT1 Glove Stripe", stripe, 0.1f);

            palmMesh = Keep(RoundedBox(0.047f, 0.019f, 0.05f, 0.016f, "EOSAT1 Glove Palm"));
            padMesh = Keep(RoundedBox(0.036f, 0.007f, 0.034f, 0.006f, "EOSAT1 Glove Pad"));
            ringMesh = Keep(Tube(0.048f, 0.048f, 0.024f, 24, "EOSAT1 Glove Ring"));
            sleeveMesh = Keep(Tube(0.061f, 0.046f, 0.13f, 24, "EOSAT1 Glove Sleeve"));
            stripeMesh = Keep(Tube(0.0577f, 0.0555f, 0.02f, 24, "EOSAT1 Glove Stripe"));

            if (leftHand != null) left = BuildGlove(leftHand, true);
            if (rightHand != null) right = BuildGlove(rightHand, false);
            built = true;
        }

        void OnDestroy()
        {
            for (int i = 0; i < meshes.Count; i++) if (meshes[i] != null) Destroy(meshes[i]);
            Material[] mats = { fabricMat, paddingMat, rubberMat, metalMat, stripeMat };
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null) Destroy(mats[i]);
        }

        Mesh Keep(Mesh m)
        {
            meshes.Add(m);
            return m;
        }

        /// <summary>The shader the suit is drawn with (lit, works in both eyes), or a fallback.</summary>
        public static Shader SuitShader()
        {
            Shader sh = Resources.Load<Shader>("EosatSuit");
            if (sh == null) sh = Shader.Find("EOSAT1/Suit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("EOSAT1/UnlitColor");
            return sh;
        }

        public static Material NewMaterial(Shader sh, string name, Color c, float shine)
        {
            var m = new Material(sh);
            m.name = name;
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Shine")) m.SetFloat("_Shine", shine);
            return m;
        }

        Mesh CapsuleFor(float radius, float length)
        {
            int key = Mathf.RoundToInt(radius * 10000f) * 100000 + Mathf.RoundToInt(length * 10000f);
            Mesh m;
            if (capsules.TryGetValue(key, out m)) return m;
            m = Keep(Capsule(radius, length, 12, 4, "EOSAT1 Glove Finger"));
            capsules[key] = m;
            return m;
        }

        /// <summary>How the glove (fingers along +Z, back of the hand up, thumb on the inside) sits on the tracked pose.</summary>
        Quaternion Mounting(bool isLeft)
        {
            return GloveMounting(rig, isLeft);
        }

        /// <summary>The same, for anything else that hangs on a hand the way the glove does (rig may be null).</summary>
        public static Quaternion GloveMounting(VrRig rig, bool isLeft)
        {
            bool gripPose;
            if (rig != null) gripPose = rig.DevicePoseIsGrip;
            else
            {
                string device = XRSettings.loadedDeviceName;
                gripPose = string.IsNullOrEmpty(device) || device.ToLowerInvariant().Contains("openxr");
            }
            if (gripPose)
            {
                // OpenXR grip pose in Unity axes: the handle runs along Z (little finger to
                // thumb), the knuckles point along -Y, the right palm faces -X and the left +X
                return Quaternion.LookRotation(Vector3.down, isLeft ? Vector3.left : Vector3.right);
            }
            // a pose that points forwards along the controller: fingers forwards, palms facing each other
            return Quaternion.LookRotation(Vector3.forward, isLeft ? Vector3.left : Vector3.right);
        }

        Glove BuildGlove(Transform hand, bool isLeft)
        {
            // the plain controller blocks are replaced by the glove
            for (int i = 0; i < hand.childCount; i++)
            {
                Transform c = hand.GetChild(i);
                if (c.name == "Grip" || c.name == "Ring" || c.name == "Body") c.gameObject.SetActive(false);
            }

            float m = isLeft ? -1f : 1f;   // the left glove is the right one mirrored in X
            var g = new Glove();
            g.Left = isLeft;
            g.Node = isLeft ? XRNode.LeftHand : XRNode.RightHand;
            var rootGo = new GameObject(isLeft ? "Space Glove (left)" : "Space Glove (right)");
            g.Root = rootGo.transform;
            g.Root.SetParent(hand, false);
            Place(g);

            Part("Palm", g.Root, palmMesh, fabricMat, Vector3.zero);
            Part("Back Pad", g.Root, padMesh, paddingMat, new Vector3(0f, 0.0165f, 0.004f));
            Part("Wrist Ring", g.Root, ringMesh, metalMat, new Vector3(0f, 0f, -0.074f));
            Part("Sleeve", g.Root, sleeveMesh, fabricMat, new Vector3(0f, 0f, -0.2f));
            Part("Stripe", g.Root, stripeMesh, stripeMat, new Vector3(0f, 0f, -0.165f));

            // index, middle, ring, little: x across the knuckles (the index is on the thumb's side),
            // the three bone lengths, thickness, and how far the finger splays sideways
            g.Index = BuildFinger("Index", g.Root, new Vector3(-0.0335f * m, 0f, 0.046f), Quaternion.Euler(0f, -5f * m, 0f),
                                  new[] { 0.036f, 0.025f, 0.022f }, 0.0118f,
                                  new[] { 24f, 34f, 22f }, new[] { 50f, 66f, 42f });
            g.Others = new[]
            {
                BuildFinger("Middle", g.Root, new Vector3(-0.0112f * m, 0f, 0.047f), Quaternion.Euler(0f, -1f * m, 0f),
                            new[] { 0.04f, 0.028f, 0.023f }, 0.012f,
                            new[] { 40f, 46f, 30f }, new[] { 70f, 82f, 52f }),
                BuildFinger("Ring", g.Root, new Vector3(0.0112f * m, 0f, 0.046f), Quaternion.Euler(0f, 3f * m, 0f),
                            new[] { 0.037f, 0.026f, 0.022f }, 0.0116f,
                            new[] { 42f, 48f, 30f }, new[] { 72f, 84f, 52f }),
                BuildFinger("Little", g.Root, new Vector3(0.0335f * m, 0f, 0.043f), Quaternion.Euler(0f, 8f * m, 0f),
                            new[] { 0.03f, 0.021f, 0.02f }, 0.0105f,
                            new[] { 44f, 50f, 30f }, new[] { 74f, 86f, 52f }),
            };
            // the thumb starts at the side of the palm, lies forwards and inwards, and bends towards the fingers
            Quaternion thumbBase = Quaternion.Euler(15f, -35f * m, 0f) * Quaternion.Euler(0f, 0f, 50f * m);
            g.Thumb = BuildFinger("Thumb", g.Root, new Vector3(-0.04f * m, -0.004f, -0.02f), thumbBase,
                                  new[] { 0.036f, 0.03f }, 0.0135f,
                                  new[] { 8f, 18f }, new[] { 22f, 40f });

            SetLayer(rootGo, ViewDirector.HiddenFromSatCameraLayer);
            Pose(g);
            return g;
        }

        void Place(Glove g)
        {
            Quaternion mount = Mounting(g.Left) * Quaternion.Euler(g.Left ? leftTweak : rightTweak);
            float s = Mathf.Max(0.5f, size);
            g.Root.localRotation = mount;
            g.Root.localScale = new Vector3(s, s, s);
            g.Root.localPosition = -(mount * (GripPoint * s));
        }

        Finger BuildFinger(string name, Transform parent, Vector3 basePos, Quaternion baseRot, float[] lengths, float radius,
                           float[] open, float[] closed)
        {
            var f = new Finger();
            f.Joints = new Transform[lengths.Length];
            f.Base = new Quaternion[lengths.Length];
            f.Open = open;
            f.Closed = closed;
            Transform at = parent;
            for (int i = 0; i < lengths.Length; i++)
            {
                var jg = new GameObject(name + " " + (i + 1));
                Transform j = jg.transform;
                j.SetParent(at, false);
                j.localPosition = i == 0 ? basePos : new Vector3(0f, 0f, lengths[i - 1]);
                f.Base[i] = i == 0 ? baseRot : Quaternion.identity;
                j.localRotation = f.Base[i];
                // the last bone is the rubber fingertip, a touch thinner
                bool tip = i == lengths.Length - 1;
                Part("Skin", j, CapsuleFor(tip ? radius * 0.94f : radius, lengths[i]), tip ? rubberMat : fabricMat, Vector3.zero);
                f.Joints[i] = j;
                at = j;
            }
            return f;
        }

        static void Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static void SetLayer(GameObject go, int layer)
        {
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = layer;
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!built) return;
            float k = 1f - Mathf.Exp(-Mathf.Max(1f, fingerSpeed) * Time.unscaledDeltaTime);
            Follow(left, k);
            Follow(right, k);
        }

        void Follow(Glove g, float k)
        {
            if (g == null || !g.Root.gameObject.activeInHierarchy) return;
            float grip = 0f, trigger = 0f;
            bool thumb = false;
            InputDevice d = InputDevices.GetDeviceAtXRNode(g.Node);
            if (d.isValid)
            {
                float f;
                bool b;
                if (d.TryGetFeatureValue(CommonUsages.grip, out f)) grip = Mathf.Clamp01(f);
                if (d.TryGetFeatureValue(CommonUsages.trigger, out f)) trigger = Mathf.Clamp01(f);
                if (d.TryGetFeatureValue(CommonUsages.primaryTouch, out b) && b) thumb = true;
                if (d.TryGetFeatureValue(CommonUsages.secondaryTouch, out b) && b) thumb = true;
                if (d.TryGetFeatureValue(CommonUsages.primary2DAxisTouch, out b) && b) thumb = true;
                if (d.TryGetFeatureValue(CommonUsages.primaryButton, out b) && b) thumb = true;
                if (d.TryGetFeatureValue(CommonUsages.secondaryButton, out b) && b) thumb = true;
                if (d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out b) && b) thumb = true;
            }
            g.Grip = Mathf.Lerp(g.Grip, grip, k);
            g.Trigger = Mathf.Lerp(g.Trigger, trigger, k);
            g.ThumbDown = Mathf.Lerp(g.ThumbDown, thumb ? 1f : 0f, k);
            Place(g);   // follows the tweak fields while playing
            Pose(g);
        }

        static void Pose(Glove g)
        {
            Curl(g.Index, g.Trigger);
            for (int i = 0; i < g.Others.Length; i++) Curl(g.Others[i], g.Grip);
            Curl(g.Thumb, g.ThumbDown);
        }

        static void Curl(Finger f, float amount)
        {
            for (int i = 0; i < f.Joints.Length; i++)
                f.Joints[i].localRotation = f.Base[i] * Quaternion.Euler(Mathf.Lerp(f.Open[i], f.Closed[i], amount), 0f, 0f);
        }

        // ------------------------------------------------------------------ meshes
        /// <summary>Add a triangle, turned so that it faces the way 'outward' points.</summary>
        static void Tri(List<int> t, List<Vector3> v, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) < 0f)
            {
                int k = b;
                b = c;
                c = k;
            }
            t.Add(a);
            t.Add(b);
            t.Add(c);
        }

        static Mesh Finish(List<Vector3> v, List<Vector3> n, List<int> t, string name)
        {
            var m = new Mesh();
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A rod along +Z from z = 0 to z = length, with a rounded cap beyond each end.</summary>
        static Mesh Capsule(float radius, float length, int sides, int rings, string name)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            int rows = 2 * (rings + 1);
            for (int row = 0; row < rows; row++)
            {
                bool front = row > rings;
                int i = front ? row - rings - 1 : row;
                float phi = (front ? 0f : -Mathf.PI * 0.5f) + Mathf.PI * 0.5f * i / rings;
                float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
                for (int k = 0; k < sides; k++)
                {
                    float a = 2f * Mathf.PI * k / sides;
                    var dir = new Vector3(cp * Mathf.Cos(a), cp * Mathf.Sin(a), sp);
                    n.Add(dir);
                    v.Add(new Vector3(0f, 0f, front ? length : 0f) + dir * radius);
                }
            }
            for (int row = 0; row + 1 < rows; row++)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a0 = row * sides + k, a1 = row * sides + (k + 1) % sides;
                    int b0 = a0 + sides, b1 = a1 + sides;
                    Vector3 mid = (v[a0] + v[a1] + v[b0] + v[b1]) * 0.25f;
                    Vector3 outward = mid - new Vector3(0f, 0f, Mathf.Clamp(mid.z, 0f, length));
                    Tri(t, v, a0, b0, a1, outward);
                    Tri(t, v, a1, b0, b1, outward);
                }
            }
            return Finish(v, n, t, name);
        }

        static float Side(float x)
        {
            return x > 1e-4f ? 1f : (x < -1e-4f ? -1f : 0f);
        }

        /// <summary>A box with rounded edges and corners, centred, of the given half sizes.</summary>
        public static Mesh RoundedBox(float hx, float hy, float hz, float radius, string name)
        {
            const int lon = 24, lat = 12;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            radius = Mathf.Min(radius, Mathf.Min(hx, Mathf.Min(hy, hz)));
            for (int row = 0; row <= lat; row++)
            {
                float phi = -Mathf.PI * 0.5f + Mathf.PI * row / lat;
                float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
                for (int k = 0; k < lon; k++)
                {
                    float a = 2f * Mathf.PI * k / lon;
                    var dir = new Vector3(cp * Mathf.Cos(a), sp, cp * Mathf.Sin(a));
                    // a sphere, pulled apart to the box's corners
                    var corner = new Vector3(Side(dir.x) * (hx - radius), Side(dir.y) * (hy - radius), Side(dir.z) * (hz - radius));
                    n.Add(dir);
                    v.Add(corner + dir * radius);
                }
            }
            for (int row = 0; row < lat; row++)
            {
                for (int k = 0; k < lon; k++)
                {
                    int a0 = row * lon + k, a1 = row * lon + (k + 1) % lon;
                    int b0 = a0 + lon, b1 = a1 + lon;
                    Vector3 outward = (v[a0] + v[a1] + v[b0] + v[b1]) * 0.25f;
                    Tri(t, v, a0, b0, a1, outward);
                    Tri(t, v, a1, b0, b1, outward);
                }
            }
            return Finish(v, n, t, name);
        }

        /// <summary>A round tube along +Z from z = 0 (radius r0) to z = length (radius r1), closed at both ends.</summary>
        public static Mesh Tube(float r0, float r1, float length, int sides, string name)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            for (int k = 0; k < sides; k++)
            {
                float a = 2f * Mathf.PI * k / sides;
                var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(radial * r0);
                n.Add(radial);
                v.Add(radial * r1 + new Vector3(0f, 0f, length));
                n.Add(radial);
            }
            for (int k = 0; k < sides; k++)
            {
                int a0 = k * 2, a1 = ((k + 1) % sides) * 2;
                Vector3 outward = v[a0] + v[a1];
                outward.z = 0f;
                Tri(t, v, a0, a0 + 1, a1, outward);
                Tri(t, v, a1, a0 + 1, a1 + 1, outward);
            }
            // the two flat ends, each with its own vertices so the edge stays sharp
            for (int end = 0; end < 2; end++)
            {
                float z = end == 0 ? 0f : length;
                float r = end == 0 ? r0 : r1;
                Vector3 normal = end == 0 ? Vector3.back : Vector3.forward;
                int centre = v.Count;
                v.Add(new Vector3(0f, 0f, z));
                n.Add(normal);
                for (int k = 0; k < sides; k++)
                {
                    float a = 2f * Mathf.PI * k / sides;
                    v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
                    n.Add(normal);
                }
                for (int k = 0; k < sides; k++)
                    Tri(t, v, centre, centre + 1 + k, centre + 1 + (k + 1) % sides, normal);
            }
            return Finish(v, n, t, name);
        }
    }
}
