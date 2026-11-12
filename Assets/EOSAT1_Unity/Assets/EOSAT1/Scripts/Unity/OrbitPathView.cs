// EOSAT-1 VR - draws the path the spacecraft follows: one full orbit, worked out from
// where it is and how fast it is going right now (the osculating ellipse), so the line
// always passes exactly through the spacecraft.
//
// The line glows: a thin bright core with a soft halo, in the menu's cyan.
//   ahead    bright, starting softly at the spacecraft and fading gently round the orbit
//   behind   a dimmer trail that fades away
//   violet   the part inside Earth's shadow (where the eclipse happens)
//   amber dots  where the spacecraft enters and leaves the shadow
//
// The line is a ribbon that always faces the viewer and keeps the same width on screen
// at any distance, so it reads as a thin line both next to the spacecraft (where the
// orbit is 43,000 m long in the scene) and on the whole-Earth globe.
using System.Collections.Generic;
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    [DefaultExecutionOrder(200)]   // after the cameras have moved
    public sealed class OrbitPathView : MonoBehaviour
    {
        public EosatApp app;
        public SpaceScene space;
        [Tooltip("The viewer the ribbon turns towards. Empty = the main camera.")]
        public Camera viewCamera;

        [Tooltip("Width of the line's bright core as an angle seen by the viewer (radians). 0.002 is about 2 pixels.")]
        public float angularWidth = 0.002f;
        [Tooltip("Size of the dots that mark where the shadow starts and ends, as an angle (radians).")]
        public float markerAngularSize = 0.03f;
        [Tooltip("Close view: the line starts this far (metres) from the spacecraft so it does not run through the model.")]
        public float gapMetres = 6f;
        [Tooltip("Whole-Earth view: the gap around the spacecraft, as a fraction of the Earth globe's radius.")]
        public float overviewGapFraction = 0.05f;
        [Tooltip("Largest step between points, in degrees of orbit.")]
        public float maxStepDegrees = 1.5f;

        public Color aheadColor = new Color(0.36f, 0.86f, 1.00f);
        public Color behindColor = new Color(0.22f, 0.56f, 0.90f);
        public Color shadowColor = new Color(0.66f, 0.46f, 1.00f);
        public Color markerColor = new Color(1.00f, 0.82f, 0.40f);

        Mesh mesh;
        MeshRenderer meshRenderer;
        readonly List<Vector3> verts = new List<Vector3>(1400);
        readonly List<int> tris = new List<int>(5200);
        readonly List<Color32> colors = new List<Color32>(1400);
        readonly List<Vector2> uvs = new List<Vector2>(1400);
        readonly List<Vector3> markers = new List<Vector3>(8);
        Material material;
        /// <summary>The ribbon is this many times wider than its core, to leave room for the halo.</summary>
        float ribbonScale = 4.5f;
        readonly List<double> offsets = new List<double>(400);
        readonly List<Vector3> pts = new List<Vector3>(400);
        readonly List<float> lit = new List<float>(400);

        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            mesh = new Mesh { name = "EOSAT1 Orbit Path" };
            mesh.MarkDynamic();
            MeshFilter mf = gameObject.GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            material = MakeMaterial();
            meshRenderer.sharedMaterial = material;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }

        Material MakeMaterial()
        {
            // The text shader draws "vertex colour x the texture's alpha", blended over the
            // scene: with a soft dot as its texture that is exactly a glowing line. It is
            // already used by the labels, so it is part of every build.
            Shader sh = Shader.Find("EOSAT1/HudText");
            if (sh != null)
            {
                var m = new Material(sh) { name = "EOSAT1 Orbit Path (runtime)" };
                m.mainTexture = HudStyle.SoftDot;
                m.SetColor(ColorId, Color.white);
                return m;
            }
            // fallback: a plain thin line in one colour
            ribbonScale = 1.2f;
            sh = Shader.Find("EOSAT1/UnlitColor");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var f = new Material(sh) { name = "EOSAT1 Orbit Path (runtime, plain)" };
            if (f.HasProperty(ColorId)) f.SetColor(ColorId, aheadColor);
            if (f.HasProperty("_BaseColor")) f.SetColor("_BaseColor", aheadColor);
            return f;
        }

        void LateUpdate()
        {
            if (app == null || space == null || mesh == null) return;
            DisplayState s = app.State;
            if (s == null || !s.Valid || !space.HasFrame)
            {
                if (meshRenderer.enabled) meshRenderer.enabled = false;
                return;
            }
            if (!meshRenderer.enabled) meshRenderer.enabled = true;
            Rebuild(s);
        }

        void Rebuild(DisplayState s)
        {
            Vec3d r = s.PosEciKm;
            double rLen = r.Length;
            if (!(rLen > Ephemeris.EarthRadiusKm) || !r.IsFinite) return;

            // ---------------------------------------------------------------- the orbit's shape
            // Points on the orbit: radius = p / (1 + e cos(nu)) along cos(nu) E + sin(nu) Q,
            // where E points at perigee and Q is 90 degrees further round the orbit.
            Vec3d eHat, qHat;
            double p, ecc, nu0;
            bool haveEllipse = false;
            eHat = Vec3d.UnitX; qHat = Vec3d.UnitY; p = rLen; ecc = 0; nu0 = 0;
            if (s.HasVelocity && !s.VelocityEstimated && s.VelEciKmS.IsFinite)
            {
                Vec3d v = s.VelEciKmS;
                Vec3d hVec = Vec3d.Cross(r, v);
                double h2 = hVec.SqrLength;
                if (h2 > 1.0)
                {
                    double mu = CircularOrbit.MuKm3s2;
                    Vec3d eVec = (r * (v.SqrLength - mu / rLen) - v * Vec3d.Dot(r, v)) / mu;
                    ecc = eVec.Length;
                    if (ecc < 0.8)
                    {
                        Vec3d hHat = hVec.Normalized;
                        if (ecc < 1e-5)
                        {
                            ecc = 0;
                            eHat = r / rLen;
                        }
                        else eHat = eVec / ecc;
                        qHat = Vec3d.Cross(hHat, eHat);
                        p = h2 / mu;
                        nu0 = System.Math.Atan2(Vec3d.Dot(r, qHat), Vec3d.Dot(r, eHat));
                        haveEllipse = true;
                    }
                }
            }
            if (!haveEllipse)
            {
                // no trustworthy velocity: a circle at the present height, in the estimated orbit plane
                Vec3d hHat = space.OrbitNormalEci;
                Vec3d e0 = Vec3d.RejectFrom(r, hHat);
                if (e0.SqrLength < 1.0) return;
                eHat = e0.Normalized;
                qHat = Vec3d.Cross(hHat, eHat);
                p = rLen;
                ecc = 0;
                nu0 = 0;
            }

            // ---------------------------------------------------------------- where to put points
            double km = space.ActiveMetresPerKm;
            double gap = space.overview ? overviewGapFraction * space.overviewEarthRadius : gapMetres;
            double u0 = MathUtil.Clamp(gap / System.Math.Max(1e-9, rLen * km), 1e-5, 0.3);
            double cap = System.Math.Max(0.2, maxStepDegrees) * MathUtil.Deg2Rad;
            offsets.Clear();
            double u = u0;
            // small steps near the spacecraft (where the viewer is), growing to the cap
            while (u < System.Math.PI && offsets.Count < 390)
            {
                offsets.Add(u);
                u += System.Math.Min(cap, System.Math.Max(u * 0.15, 1e-6));
            }
            offsets.Add(System.Math.PI);

            // ---------------------------------------------------------------- the Sun, for the shadow part
            Vec3d sunDir = s.HasSun ? s.SunDirEci.Normalized : (Ephemeris.SunEci(s.SimTime, space.eciConvention) - r).Normalized;
            Vec3d rSun = sunDir * Ephemeris.AuKm;

            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            Vector3 camLocal = cam != null ? transform.InverseTransformPoint(cam.transform.position) : Vector3.zero;

            verts.Clear();
            colors.Clear();
            uvs.Clear();
            tris.Clear();
            markers.Clear();
            AddStrip(-1, eHat, qHat, p, ecc, nu0, rSun, camLocal);   // the trail first, so the part ahead draws over it
            AddStrip(+1, eHat, qHat, p, ecc, nu0, rSun, camLocal);
            if (cam != null) AddMarkers(cam, camLocal);

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
            // never cull it: the ribbon surrounds the whole scene
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }

        /// <summary>One half of the orbit, from next to the spacecraft round to the far side.</summary>
        void AddStrip(int direction, Vec3d eHat, Vec3d qHat, double p, double ecc, double nu0, Vec3d rSun, Vector3 camLocal)
        {
            pts.Clear();
            lit.Clear();
            Transform spaceT = space.transform;
            bool sameSpace = transform.parent == spaceT || transform == spaceT;
            for (int i = 0; i < offsets.Count; i++)
            {
                double nu = nu0 + direction * offsets[i];
                double c = System.Math.Cos(nu), sn = System.Math.Sin(nu);
                double radius = p / (1.0 + ecc * c);
                Vec3d pos = (eHat * c + qHat * sn) * radius;
                Vector3 local = space.LocalFromEciKm(pos);
                if (!sameSpace) local = transform.InverseTransformPoint(spaceT.TransformPoint(local));
                pts.Add(local);
                lit.Add((float)Ephemeris.SunlitFraction(pos, rSun));
            }

            int n = pts.Count;
            if (n < 2) return;
            int baseIndex = verts.Count;
            Color main = direction > 0 ? aheadColor : behindColor;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = pts[i > 0 ? i - 1 : i];
                Vector3 b = pts[i < n - 1 ? i + 1 : i];
                Vector3 tangent = b - a;
                Vector3 toCam = camLocal - pts[i];
                float dist = toCam.magnitude;
                Vector3 side = Vector3.Cross(tangent, toCam);
                float sm = side.magnitude;
                if (sm < 1e-12f)
                {
                    // looking straight along the line: any sideways direction will do
                    side = Vector3.Cross(tangent, Vector3.up);
                    sm = side.magnitude;
                    if (sm < 1e-12f)
                    {
                        side = Vector3.right;
                        sm = 1f;
                    }
                }
                side *= 0.5f * angularWidth * ribbonScale * Mathf.Max(dist, 0.05f) / sm;
                verts.Add(pts[i] - side);
                verts.Add(pts[i] + side);
                uvs.Add(new Vector2(0f, 0.5f));
                uvs.Add(new Vector2(1f, 0.5f));

                // colour: cyan in sunlight, violet in shadow; brightness: a soft start at the
                // spacecraft, then fading with distance round the orbit
                float round = (float)(offsets[i] / System.Math.PI);          // 0 at the spacecraft, 1 on the far side
                float alpha = direction > 0 ? Mathf.Lerp(1f, 0.5f, round) : Mathf.Lerp(0.6f, 0.14f, Mathf.Sqrt(round));
                alpha *= Mathf.Clamp01(i / 4f);
                Color col = Color.Lerp(shadowColor, main, Mathf.SmoothStep(0f, 1f, lit[i]));
                if (direction > 0) col = Color.Lerp(Color.white, col, Mathf.Clamp01(0.55f + round * 6f));   // a hot start
                col.a = alpha;
                Color32 c32 = col;
                colors.Add(c32);
                colors.Add(c32);

                // where sunlight turns to shadow (or back) between this point and the next
                if (i < n - 1 && (lit[i] > 0.5f) != (lit[i + 1] > 0.5f))
                {
                    float k = Mathf.InverseLerp(lit[i], lit[i + 1], 0.5f);
                    markers.Add(Vector3.Lerp(pts[i], pts[i + 1], k));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                int v0 = baseIndex + i * 2;
                tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
                tris.Add(v0 + 1); tris.Add(v0 + 3); tris.Add(v0 + 2);
            }
        }

        /// <summary>A glowing dot, facing the viewer, at each place where the path crosses the edge of Earth's shadow.</summary>
        void AddMarkers(Camera cam, Vector3 camLocal)
        {
            Vector3 right = transform.InverseTransformDirection(cam.transform.right).normalized;
            Vector3 up = transform.InverseTransformDirection(cam.transform.up).normalized;
            Color32 c32 = markerColor;
            for (int i = 0; i < markers.Count; i++)
            {
                Vector3 m = markers[i];
                float half = 0.5f * markerAngularSize * Mathf.Max((camLocal - m).magnitude, 0.05f);
                int v0 = verts.Count;
                verts.Add(m - right * half - up * half);
                verts.Add(m + right * half - up * half);
                verts.Add(m - right * half + up * half);
                verts.Add(m + right * half + up * half);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                for (int k = 0; k < 4; k++) colors.Add(c32);
                tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 1);
                tris.Add(v0 + 2); tris.Add(v0 + 3); tris.Add(v0 + 1);
            }
        }
    }
}
