// EOSAT-1 VR - a radio beam from the spacecraft down to the ground station it is talking to.
//
// The simulator says when a station can see the spacecraft (mission.in_contact) and which one
// (mission.ground_station). The ICD suggests drawing a link beam for it; this does: a beam
// from the satellite, widening to the station, with a marker on the ground there. It pulses
// gently so it reads as "talking". Station positions are the simulator's own list
// (C2O sim/configs/eosat1/orbit.yaml: ground_stations). An unknown station gets no beam,
// only the "IN CONTACT" label on the model.
//
// Adds itself to the app at runtime, like the speed slider; nothing to set up in the scene.
using System;
using Eosat1.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eosat1
{
    public sealed class GroundLinkBeam : MonoBehaviour
    {
        public EosatApp app;
        public Color beamColor = new Color(0.35f, 1f, 0.55f, 1f);
        [Tooltip("Beam width at the spacecraft, metres.")]
        public float startWidth = 0.05f;
        [Tooltip("Beam width at the ground, as a fraction of the distance to it.")]
        public float endWidthFraction = 0.02f;

        struct Station
        {
            public string Name;
            public double LatDeg, LonDeg, AltKm;
            public Station(string n, double lat, double lon, double alt) { Name = n; LatDeg = lat; LonDeg = lon; AltKm = alt; }
        }

        // C2O sim/configs/eosat1/orbit.yaml, ground_stations
        static readonly Station[] Stations =
        {
            new Station("Iqaluit", 63.747, -68.518, 0.034),
            new Station("Troll", -72.012, 2.535, 1.275),
            new Station("Svalbard", 78.2297, 15.3975, 0.458),
            new Station("Inuvik", 68.3195, -133.5495, 0.102),
            new Station("O'Higgins", -63.3211, -57.9008, 0.032),
        };

        LineRenderer beam;
        Transform marker;
        Material mat;

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
            if (a == null || a.GetComponent<GroundLinkBeam>() != null) return;
            a.gameObject.AddComponent<GroundLinkBeam>().app = a;
        }

        // ------------------------------------------------------------------ drawing
        void Build()
        {
            Shader sh = Shader.Find("EOSAT1/UnlitColor");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) return;
            mat = new Material(sh) { name = "EOSAT1 Ground Link (runtime)" };
            SetColor(mat, beamColor);

            var go = new GameObject("Ground Link Beam");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.sharedMaterial = mat;
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.numCapVertices = 2;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.enabled = false;

            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.name = "Ground Station Marker";
            Destroy(m.GetComponent<Collider>());
            m.transform.SetParent(transform, false);
            Renderer mr = m.GetComponent<Renderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker = m.transform;
            m.SetActive(false);
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void LateUpdate()
        {
            if (app == null) return;
            if (beam == null)
            {
                Build();
                if (beam == null) return;
            }
            DisplayState s = app.State;
            SpaceScene space = app.space;
            Station st;
            bool show = s != null && s.Valid && s.HasContact && s.InContact && space != null &&
                        space.earth != null && space.satellite != null && TryFind(s.GroundStation, out st);
            if (!show)
            {
                if (beam.enabled) beam.enabled = false;
                if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                return;
            }
            TryFind(s.GroundStation, out st);

            // the station on the Earth model: a unit sphere in Earth-fixed axes (lon 0 on +X,
            // north on +Y), so the body-axes swap of an ECEF unit vector, a little above the surface
            double lat = st.LatDeg * MathUtil.Deg2Rad, lon = st.LonDeg * MathUtil.Deg2Rad;
            var ecef = new Vec3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
            double r = 1.0 + (st.AltKm + 2.0) / Ephemeris.EarthRadiusKm;
            Vector3 ground = space.earth.TransformPoint(SimToUnity.Vec(ecef * r));
            Vector3 sat = space.satellite.position;
            float dist = Vector3.Distance(sat, ground);

            beam.SetPosition(0, sat);
            beam.SetPosition(1, ground);
            beam.startWidth = Mathf.Min(startWidth, dist * 0.05f);   // thin at the spacecraft (and in the overview)
            beam.endWidth = dist * endWidthFraction;
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 4f);
            SetColor(mat, beamColor * pulse);
            if (!beam.enabled) beam.enabled = true;

            marker.position = ground;
            marker.localScale = Vector3.one * Mathf.Max(0.01f, dist * endWidthFraction * 1.6f);
            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
        }

        static bool TryFind(string name, out Station st)
        {
            st = default(Station);
            if (string.IsNullOrEmpty(name)) return false;
            string n = Simplify(name);
            foreach (Station s in Stations)
            {
                string k = Simplify(s.Name);
                if (n == k || n.Contains(k) || k.Contains(n))
                {
                    st = s;
                    return true;
                }
            }
            return false;
        }

        static string Simplify(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetter(c)) b.Append(c);
            return b.ToString();
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
