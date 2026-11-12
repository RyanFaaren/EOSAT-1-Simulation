// EOSAT-1 VR - draws the path the spacecraft follows: one full orbit, worked out from
// where it is and how fast it is going right now (the osculating ellipse), so the line
// always passes exactly through the spacecraft.
//
//   bright  = the part still ahead
//   dim     = the part already flown
//   violet  = the part inside Earth's shadow (where the eclipse happens)
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

        [Tooltip("Width of the line as an angle seen by the viewer (radians). 0.0025 is about 3 pixels.")]
        public float angularWidth = 0.0025f;
        [Tooltip("Close view: the line starts this far (metres) from the spacecraft so it does not run through the model.")]
        public float gapMetres = 6f;
        [Tooltip("Whole-Earth view: the gap around the spacecraft, as a fraction of the Earth globe's radius.")]
        public float overviewGapFraction = 0.05f;
        [Tooltip("Largest step between points, in degrees of orbit.")]
        public float maxStepDegrees = 1.5f;

        public Color aheadColor = new Color(0.40f, 0.92f, 1.00f);
        public Color behindColor = new Color(0.16f, 0.40f, 0.52f);
        public Color shadowColor = new Color(0.50f, 0.34f, 0.92f);

        Mesh mesh;
        MeshRenderer meshRenderer;
        readonly List<Vector3> verts = new List<Vector3>(1400);
        readonly List<int> trisAhead = new List<int>(2400);
        readonly List<int> trisBehind = new List<int>(2400);
        readonly List<int> trisShadow = new List<int>(2400);
        readonly List<double> offsets = new List<double>(400);
        readonly List<Vector3> pts = new List<Vector3>(400);
        readonly List<bool> lit = new List<bool>(400);

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
            meshRenderer.sharedMaterials = new[] { MakeMaterial(aheadColor), MakeMaterial(behindColor), MakeMaterial(shadowColor) };
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        static Material MakeMaterial(Color c)
        {
            // the same flat-colour shader as the axis arrows, so it is already part of every build
            Shader sh = Shader.Find("EOSAT1/UnlitColor");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "EOSAT1 Orbit Path (runtime)" };
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
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
            trisAhead.Clear();
            trisBehind.Clear();
            trisShadow.Clear();
            AddStrip(+1, eHat, qHat, p, ecc, nu0, rSun, camLocal, trisAhead);
            AddStrip(-1, eHat, qHat, p, ecc, nu0, rSun, camLocal, trisBehind);

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(trisAhead, 0, false);
            mesh.SetTriangles(trisBehind, 1, false);
            mesh.SetTriangles(trisShadow, 2, false);
            // never cull it: the ribbon surrounds the whole scene
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }

        /// <summary>One half of the orbit, from next to the spacecraft round to the far side.</summary>
        void AddStrip(int direction, Vec3d eHat, Vec3d qHat, double p, double ecc, double nu0, Vec3d rSun, Vector3 camLocal, List<int> sunlitTris)
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
                lit.Add(Ephemeris.SunlitFraction(pos, rSun) > 0.5);
            }

            int n = pts.Count;
            if (n < 2) return;
            int baseIndex = verts.Count;
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
                side *= 0.5f * angularWidth * Mathf.Max(dist, 0.05f) / sm;
                verts.Add(pts[i] - side);
                verts.Add(pts[i] + side);
            }
            for (int i = 0; i < n - 1; i++)
            {
                List<int> t = lit[i] && lit[i + 1] ? sunlitTris : trisShadow;
                int v0 = baseIndex + i * 2;
                t.Add(v0); t.Add(v0 + 1); t.Add(v0 + 2);
                t.Add(v0 + 1); t.Add(v0 + 3); t.Add(v0 + 2);
            }
        }
    }
}
