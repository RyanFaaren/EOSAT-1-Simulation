// EOSAT-1 VR - what the spacecraft's metal reflects.
//
// Metal shows its surroundings. The scene has no skybox, so before this the gold foil and
// the camera hoods mirrored plain black and looked like paint. This adds a reflection probe
// at the spacecraft that sees only the sky: the Earth (day side, clouds, night side and city
// lights), the atmosphere's glow, the Moon, the Sun and the stars. URP's Lit shader then
// mirrors that sky on every metal surface, sharper or blurrier depending on how glossy the
// spot is (the gold foil's gloss map, mli_gold_mask.png).
//
// The probe redraws the sky a few times a second (more often when time is sped up, and at
// once when the view changes), so reflections follow the Earth moving below, sunrise,
// sunset and eclipses. Turning the spacecraft needs no redraw: each surface looks up the
// direction it faces. The spacecraft, the player and the helpers (orbit path, axes, menus)
// are not in the probe's picture.
//
// The solar panels keep the look they had: their materials have Environment Reflections
// turned off.
//
// EosatApp adds this component by itself at start, so an existing scene needs no rebuild.
// Real-time reflection probes must be allowed in Quality settings; this switches them on.
using UnityEngine;
using UnityEngine.Rendering;

namespace Eosat1
{
    [DefaultExecutionOrder(100)]   // after EosatApp and SpaceScene have placed the sky this frame
    public sealed class SpaceReflections : MonoBehaviour
    {
        /// <summary>
        /// The sky objects (Earth with its atmosphere, Moon, Sun disc, stars) are moved to this
        /// layer, the only one the probe draws. Every camera still draws it.
        /// </summary>
        public const int SkyLayer = 29;

        public EosatApp app;
        public SpaceScene space;
        [Tooltip("Pixels along each side of the probe's six pictures. 256 is sharp enough for the foil.")]
        public int resolution = 256;
        [Tooltip("Seconds between redraws at normal speed.")]
        public float interval = 0.5f;
        [Tooltip("Shortest time between redraws when time is sped up.")]
        public float fastestInterval = 0.05f;
        [Tooltip("Colour of empty sky in the reflections. Not quite black: it stands in for starlight and for the " +
                 "spacecraft's parts reflecting each other, so metal that mirrors empty space stays faintly visible.")]
        public Color emptySky = new Color(0.11f, 0.115f, 0.13f, 1f);
        [Tooltip("Size of the box (metres, centred on the spacecraft) inside which surfaces use these reflections.")]
        public float influenceSize = 8f;

        public ReflectionProbe Probe { get { return probe; } }

        ReflectionProbe probe;
        float nextRender, firstRender = -1f;
        bool rendered, lastOverview, checkedResult;
        SpaceScene.ViewFrame lastFrame;

        void Start()
        {
            if (app == null) app = GetComponent<EosatApp>();
            if (space == null && app != null) space = app.space;
            if (space == null) space = FindAnyObjectByType<SpaceScene>();
            if (space == null)
            {
                enabled = false;
                return;
            }
            if (!QualitySettings.realtimeReflectionProbes) QualitySettings.realtimeReflectionProbes = true;

            MoveToSkyLayer(space.earth);
            MoveToSkyLayer(space.moon);
            MoveToSkyLayer(space.stars);
            MoveToSkyLayer(space.sunDisc);

            // a root object: a reflection probe's box should not be scaled or turned with its parent
            var go = new GameObject("Spacecraft Reflections");
            go.transform.position = SpacecraftPosition();
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 16, 2048));
            probe.hdr = true;
            probe.boxProjection = false;             // the sky is at infinity, not on the box's walls
            probe.size = Vector3.one * Mathf.Max(2f, influenceSize);
            probe.center = Vector3.zero;
            probe.blendDistance = 0f;
            probe.importance = 100;
            probe.intensity = 1f;
            probe.cullingMask = 1 << SkyLayer;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = emptySky;
            probe.shadowDistance = 0f;
            probe.nearClipPlane = 1f;
            probe.farClipPlane = Mathf.Max(1000f, space.FarClip);
        }

        void OnDestroy()
        {
            if (probe != null) Destroy(probe.gameObject);
        }

        Vector3 SpacecraftPosition()
        {
            if (space.satellite != null) return space.satellite.position;
            if (app != null && app.satelliteView != null) return app.satelliteView.transform.position;
            return space.transform.position;
        }

        /// <summary>Put a sky object (and whatever hangs under it on the default layer) on the probe's layer.</summary>
        static void MoveToSkyLayer(Transform t)
        {
            if (t == null) return;
            Transform[] all = t.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].gameObject.layer == 0) all[i].gameObject.layer = SkyLayer;
        }

        void LateUpdate()
        {
            if (probe == null) return;
            bool overview = space.overview;
            if (overview)
            {
                // whole-Earth view: the spacecraft is drawn tiny; keep the last reflections
                lastOverview = true;
                return;
            }
            Vector3 at = SpacecraftPosition();
            if ((probe.transform.position - at).sqrMagnitude > 1e-8f) probe.transform.position = at;

            bool changed = !rendered || lastOverview || lastFrame != space.viewFrame;
            lastOverview = false;
            lastFrame = space.viewFrame;
            float now = Time.unscaledTime;
            if (!changed && now < nextRender) return;

            // the Earth below slides past about a degree a second at 1x; faster speeds need more redraws
            float speed = app != null && !app.paused ? Mathf.Max(1f, app.Speed) : 1f;
            nextRender = now + Mathf.Clamp(interval / Mathf.Sqrt(speed), Mathf.Max(0f, fastestInterval), Mathf.Max(0.01f, interval));
            float far = Mathf.Max(1000f, space.FarClip);
            if (!Mathf.Approximately(probe.farClipPlane, far)) probe.farClipPlane = far;
            if (probe.backgroundColor != emptySky) probe.backgroundColor = emptySky;
            probe.RenderProbe();
            if (!rendered) firstRender = now;
            rendered = true;
            if (!checkedResult && now - firstRender > 3f)
            {
                // say so once if nothing came out, rather than leave the metal quietly dark
                checkedResult = true;
                if (probe.texture == null && probe.realtimeTexture == null)
                    Debug.LogWarning("EOSAT-1: the spacecraft's reflections were not drawn, so its metal will look dark. " +
                                     "Tick Edit > Project Settings > Quality > Realtime Reflection Probes for the quality level in use.");
            }
        }
    }
}
