// EOSAT-1 VR - the course's 6U CubeSat, built at runtime (ICD smo-vr/1 issue 2).
//
//   hello.spacecraft.bus_size_m      [0.100, 0.226, 0.366]  (X along-track, Y wing axis, Z nadir)
//   hello.spacecraft.solar_wings     on the +Y and -Y faces, 0.418 x 0.342 m deployed,
//                                    each wing = 2 folding 209 x 342 mm panels
//   hello.spacecraft.antenna         4 tape-spring UHF elements ~0.5 m on the -Z (zenith)
//                                    deck corners, flush when STOWED
//   section 6b                       the wings are FIXED FLAPS: stowed, folded down the +/-Y face;
//                                    they hinge at the top (-Z) edge, hinge angle = 90 deg x fraction,
//                                    deployed = level with the top deck, cells facing zenith (-Z).
//                                    deploy.sada_angle_deg is DO NOT USE, so nothing here reads it.
//                                    The imager is on the nadir deck (+Z).
//
// The real thing is small (37 cm tall), so it is drawn bigger (displayScale, 2.5x by default:
// about the size of the EOS SAT-1 model it replaces, so the camera line and the body axes still fit).
// Drawn by default; Settings > Satellite model switches to EOS SAT-1. While the CubeSat is
// drawn, the EOS SAT-1 parts stay in the scene, hidden.
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public sealed class CubeSatModel : MonoBehaviour
    {
        // the ICD's numbers, metres
        public static readonly Vector3 BusSizeBody = new Vector3(0.100f, 0.226f, 0.366f);
        public const float PanelOut = 0.209f;        // each of the 2 panels, out from the hinge
        public const float PanelAlongHinge = 0.342f; // along the hinge (body X)
        public const float AntennaLength = 0.5f;

        [Tooltip("How much bigger than real life it is drawn. 1 = true size (0.37 m tall).")]
        public float displayScale = 2.5f;
        [Range(0f, 1f)] public float jammedFraction = 0.3f;

        public bool Built { get; private set; }

        SatelliteView view;
        Transform root;
        readonly Transform[] innerHinge = new Transform[2];   // [0] = wing -Y, [1] = wing +Y
        readonly Transform[] outerHinge = new Transform[2];
        readonly Transform[] cells = new Transform[2];
        readonly Transform[] antenna = new Transform[4];
        Renderer jamGlow;
        GameObject jamLabel, sensorLabel, contactLabel, frozenLabel;
        readonly GameObject[] wingJamLabel = new GameObject[2];
        TextMesh sensorText, contactText, batteryText;
        // channel 2 (power): a watt label on each wing, the battery, and cells that brighten with output
        readonly GameObject[] wingPowerLabel = new GameObject[2];
        readonly TextMesh[] wingPowerText = new TextMesh[2];
        GameObject batteryLabel;
        readonly System.Collections.Generic.List<Renderer>[] cellFaces =
            { new System.Collections.Generic.List<Renderer>(), new System.Collections.Generic.List<Renderer>() };
        float peakWingW = 20f;
        readonly float[] shownGlow = { 1f, 1f };
        MaterialPropertyBlock cellBlock;
        GameObject[] hidden;
        float shownMy, shownPy, shownAntenna, shownJam;
        int lastDiscontinuity = int.MinValue;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ------------------------------------------------------------------ building

        /// <summary>Body axes (x, y, z) in metres -> this model's Unity local space, scaled.</summary>
        Vector3 U(float x, float y, float z)
        {
            return new Vector3(x, z, y) * displayScale;
        }

        public void Build(SatelliteView owner)
        {
            if (Built || owner == null) return;
            view = owner;

            // the materials of the EOS SAT-1 model, so the CubeSat is lit and textured the same way
            Material gold = Find("mli_gold"), cellsMat = Find("panel_a"), back = Find("panel_back"),
                     white = Find("white"), dark = Find("dark"), optics = Find("optics"), tape = Find("tape"),
                     rim = Find("hood_rim");

            // hide the EOS SAT-1 parts, keeping the overlays (axes, camera line, labels)
            var keep = new System.Collections.Generic.List<GameObject>();
            var hide = new System.Collections.Generic.List<GameObject>();
            foreach (Transform c in view.transform)
            {
                GameObject g = c.gameObject;
                if (g == view.bodyAxes || g == view.boresight || g == view.jamLabel) keep.Add(g);
                else if (g.activeSelf) hide.Add(g);
            }
            hidden = hide.ToArray();
            foreach (GameObject g in hidden) g.SetActive(false);
            if (view.jamLabel != null) view.jamLabel.SetActive(false);

            root = new GameObject("6U CubeSat (course ICD)").transform;
            root.SetParent(view.transform, false);

            float bx = BusSizeBody.x, by = BusSizeBody.y, bz = BusSizeBody.z;
            float hx = bx / 2f, hy = by / 2f, hz = bz / 2f;

            // bus: gold MLI box with white rails along the 4 long (Z) edges and dark top/bottom decks
            Box("Bus", root, U(0, 0, 0), U(bx, by, bz), gold);
            float rail = 0.008f;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                Box("Rail", root, U(sx * (hx - rail / 2f), sy * (hy - rail / 2f), 0), U(rail + 0.002f, rail + 0.002f, bz + 0.008f), white);
            }
            Box("Zenith deck", root, U(0, 0, -hz - 0.002f), U(bx - 0.006f, by - 0.006f, 0.004f), dark);
            Box("Nadir deck", root, U(0, 0, hz + 0.002f), U(bx - 0.006f, by - 0.006f, 0.004f), dark);

            // imager on the nadir deck (+Z), looking down the boresight
            Transform cam = Cylinder("Imager", root, U(0, 0.03f, hz + 0.02f), Vector3.up, 0.07f * displayScale, 0.04f * displayScale, rim ?? dark);
            Cylinder("Imager lens", cam, new Vector3(0f, 1f, 0f), Vector3.up, 0.75f, 0.12f, optics ?? dark, true);
            // star camera and GPS patch on the zenith deck, as the hello describes
            Box("Star camera", root, U(0.0f, -0.06f, -hz - 0.015f), U(0.04f, 0.04f, 0.026f), dark);
            Box("GPS patch", root, U(0.0f, 0.065f, -hz - 0.006f), U(0.05f, 0.05f, 0.006f), white);

            // the two wings: an inner panel on the hinge at the top (-Z) edge of the face, and an
            // outer panel folded back on it. Stowed both hang down the face; deployed they lie flat,
            // level with the top deck, one after the other, cells to zenith.
            for (int w = 0; w < 2; w++)
            {
                float side = w == 0 ? -1f : 1f;
                var h = new GameObject(w == 0 ? "Wing -Y hinge" : "Wing +Y hinge").transform;
                h.SetParent(root, false);
                h.localPosition = U(0, side * (hy + 0.004f), -hz);
                innerHinge[w] = h;
                // in the hinge's frame (stowed): the panel runs down the face (Unity +Y = body +Z),
                // and its cell side faces away from the bus (Unity +-Z = body +-Y)
                Transform inner = Panel("Inner panel", h, side, cellsMat, back, w);
                cells[w] = inner.Find("Cells");

                var h2 = new GameObject("Fold").transform;
                h2.SetParent(h, false);
                h2.localPosition = new Vector3(0f, PanelOut * displayScale, side * 0.006f * displayScale);
                outerHinge[w] = h2;
                Panel("Outer panel", h2, side, cellsMat, back, w);
            }

            // UHF antenna: 4 tape springs, one from each zenith-deck corner, out along the diagonals
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                var a = new GameObject("UHF element " + (i + 1)).transform;
                a.SetParent(root, false);
                a.localPosition = U(sx * (hx - 0.004f), sy * (hy - 0.004f), -hz - 0.006f);
                // point it outward, in the deck plane, along the corner's diagonal
                Vector3 outDir = U(sx * 0.6f, sy * 1f, 0).normalized;
                a.localRotation = Quaternion.LookRotation(outDir, Vector3.down);
                // 1 m long in this frame (scaled for display); the element's z scale then sets its real length
                Box("Tape", a, new Vector3(0f, 0f, 0.5f * displayScale), new Vector3(0.012f, 0.0012f, 1f) * displayScale, tape ?? white);
                antenna[i] = a;
            }

            // jam warning: a pulsing red glow round the jammed element, and labels
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "Jam Glow";
            Object.Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(root, false);
            glow.transform.localPosition = antenna[0].localPosition + antenna[0].localRotation * new Vector3(0f, 0f, 0.1f * displayScale);
            glow.transform.localScale = Vector3.one * 0.12f * displayScale;
            jamGlow = glow.GetComponent<Renderer>();
            jamGlow.sharedMaterial = view.jamHalo != null ? view.jamHalo.sharedMaterial : Fallback(new Color(1f, 0.12f, 0.08f));
            jamGlow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            jamGlow.enabled = false;

            float above = -hz - 0.2f;   // labels float above the zenith deck
            jamLabel = Label("Antenna Jam Label", U(0, 0, above), "ANTENNA JAMMED", new Color(1f, 0.35f, 0.3f));
            sensorLabel = Label("Antenna Sensor Label", U(0, 0, above - 0.06f), "", new Color(1f, 0.8f, 0.3f));
            sensorText = sensorLabel != null ? sensorLabel.GetComponent<TextMesh>() : null;
            frozenLabel = Label("Frozen Label", U(0, 0, above - 0.12f), "SIMULATOR FROZEN", new Color(0.55f, 0.85f, 1f));
            contactLabel = Label("Contact Label", U(0.25f, 0, hz + 0.15f), "", new Color(0.5f, 1f, 0.6f));
            contactText = contactLabel != null ? contactLabel.GetComponent<TextMesh>() : null;
            for (int w = 0; w < 2; w++)
            {
                wingJamLabel[w] = Label("Wing Jam Label", Vector3.zero, "SOLAR PANEL JAMMED", new Color(1f, 0.35f, 0.3f));
                if (wingJamLabel[w] != null)
                {
                    wingJamLabel[w].transform.SetParent(innerHinge[w], false);
                    wingJamLabel[w].transform.localPosition = new Vector3(0f, PanelOut * displayScale, (w == 0 ? -1f : 1f) * 0.25f);
                }
            }

            for (int w = 0; w < 2; w++)
            {
                float side = w == 0 ? -1f : 1f;
                wingPowerLabel[w] = Label("Wing Power Label", Vector3.zero, "", new Color(0.6f, 1f, 0.7f));
                if (wingPowerLabel[w] != null)
                {
                    wingPowerLabel[w].transform.SetParent(innerHinge[w], false);
                    // just past the outer panel's tip, on the cell side
                    wingPowerLabel[w].transform.localPosition = new Vector3(0f, PanelOut * displayScale * 2.25f, side * 0.12f);
                    wingPowerText[w] = wingPowerLabel[w].GetComponent<TextMesh>();
                }
            }
            batteryLabel = Label("Battery Label", U(-0.22f, 0, 0), "", new Color(0.6f, 1f, 0.7f));
            batteryText = batteryLabel != null ? batteryLabel.GetComponent<TextMesh>() : null;

            // tell the rest of the app about the new cells: zenith when deployed (body -Z)
            view.cellsMinusY = cells[0];
            view.cellsPlusY = cells[1];
            view.deployedCellNormalBody = new Vector3(0f, 0f, -1f);
            Built = true;
            Pose(0f, 0f, 0f, 0f);
        }

        Material Find(string key)
        {
            string want = "EOSAT1 Sat " + key;
            foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name.StartsWith(want, System.StringComparison.Ordinal)) return m;
            return null;
        }

        static Material Fallback(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = "EOSAT1 CubeSat (runtime)" };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            return m;
        }

        static Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat != null ? mat : Fallback(new Color(0.8f, 0.7f, 0.4f));
            return g.transform;
        }

        static Transform Cylinder(string name, Transform parent, Vector3 pos, Vector3 axis, float diameter, float length, Material mat, bool unitSpace = false)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name;
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            // a Unity cylinder is 2 units tall
            g.transform.localScale = unitSpace ? new Vector3(diameter, length, diameter) : new Vector3(diameter, length / 2f, diameter);
            g.GetComponent<Renderer>().sharedMaterial = mat != null ? mat : Fallback(new Color(0.1f, 0.1f, 0.12f));
            return g.transform;
        }

        /// <summary>
        /// One 209 x 342 mm panel on a hinge, drawn stowed (running down Unity +Y from the hinge).
        /// Its "Cells" child faces the cell side: away from the bus (side = -1 for -Y, +1 for +Y).
        /// </summary>
        Transform Panel(string name, Transform hinge, float side, Material cellsMat, Material backMat, int wing)
        {
            var p = new GameObject(name).transform;
            p.SetParent(hinge, false);
            float s = displayScale, t = 0.004f * s;
            float len = PanelOut * s, wid = PanelAlongHinge * s;
            Box("Substrate", p, new Vector3(0f, len / 2f, 0f), new Vector3(wid, len, t), backMat ?? Fallback(new Color(0.15f, 0.15f, 0.17f)));
            var c = new GameObject("Cells").transform;
            c.SetParent(p, false);
            c.localPosition = new Vector3(0f, len / 2f, side * (t / 2f + 0.0006f * s));
            c.localRotation = side > 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);   // +Z = cell normal
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Cell face";
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(c, false);
            // a Quad faces -Z; turn it so its front is the cells' +Z
            q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            q.transform.localScale = new Vector3(wid * 0.96f, len * 0.94f, 1f);
            q.GetComponent<Renderer>().sharedMaterial = cellsMat != null ? cellsMat : Fallback(new Color(0.08f, 0.12f, 0.35f));
            cellFaces[wing].Add(q.GetComponent<Renderer>());
            return p;
        }

        GameObject Label(string name, Vector3 pos, string text, Color color)
        {
            if (view.jamLabel == null) return null;
            GameObject g = Instantiate(view.jamLabel, root);
            g.name = name;
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.identity;
            g.layer = view.jamLabel.layer;
            TextMesh tm = g.GetComponent<TextMesh>();
            if (tm != null)
            {
                tm.text = text;
                tm.color = color;
            }
            g.SetActive(false);
            return g;
        }

        /// <summary>Put the EOS SAT-1 model back (for switching models at runtime).</summary>
        public void Remove(Vector3 oldCellNormal, Transform oldCellsMy, Transform oldCellsPy)
        {
            if (!Built) return;
            if (root != null) Destroy(root.gameObject);
            if (hidden != null) foreach (GameObject g in hidden) if (g != null) g.SetActive(true);
            view.cellsMinusY = oldCellsMy;
            view.cellsPlusY = oldCellsPy;
            view.deployedCellNormalBody = oldCellNormal;
            Built = false;
        }

        // ------------------------------------------------------------------ every frame

        public void Apply(DisplayState s, float k)
        {
            if (!Built || s == null || !s.Valid) return;
            if (s.Discontinuity != lastDiscontinuity)
            {
                lastDiscontinuity = s.Discontinuity;
                k = 1f;
            }
            shownMy = Mathf.Lerp(shownMy, (float)s.WingMinusY.EffectiveFraction, k);
            shownPy = Mathf.Lerp(shownPy, (float)s.WingPlusY.EffectiveFraction, k);

            // antenna: the deploy state, or the truth field when the frame has it
            bool jammed = s.Antenna.Present && s.Antenna.State == DeployState.Jammed;
            float target = (float)s.Antenna.EffectiveFraction;
            if (s.HasAntennaTruth && !jammed) target = s.AntennaDeployedTruth ? 1f : (s.Antenna.State == DeployState.Deploying ? target : 0f);
            if (jammed) target = 1f;
            shownAntenna = Mathf.Lerp(shownAntenna, target, k);
            shownJam = Mathf.Lerp(shownJam, jammed ? jammedFraction : target, k);

            Pose(shownMy, shownPy, shownAntenna, shownJam);

            bool jamMy = s.WingMinusY.Present && s.WingMinusY.State == DeployState.Jammed;
            bool jamPy = s.WingPlusY.Present && s.WingPlusY.State == DeployState.Jammed;
            if (jamMy || jamPy)
            {
                float shake = Mathf.Sin(Time.time * 31f) * 0.9f + Mathf.Sin(Time.time * 13f) * 0.6f;
                if (jamMy && shownMy > 0.03f) innerHinge[0].localRotation *= Quaternion.AngleAxis(shake, Vector3.right);
                if (jamPy && shownPy > 0.03f) innerHinge[1].localRotation *= Quaternion.AngleAxis(-shake, Vector3.right);
            }
            Show(wingJamLabel[0], jamMy);
            Show(wingJamLabel[1], jamPy);

            if (jammed)
            {
                float shake = Mathf.Sin(Time.time * 37f) * 2.5f + Mathf.Sin(Time.time * 11f) * 1.5f;
                antenna[0].localRotation *= Quaternion.AngleAxis(shake, Vector3.up);
            }
            Show(jamLabel, jammed);
            if (jamGlow != null)
            {
                if (jamGlow.enabled != jammed) jamGlow.enabled = jammed;
                if (jammed)
                {
                    if (mpb == null) mpb = new MaterialPropertyBlock();
                    float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 6f);
                    Color c = new Color(1f, 0.12f, 0.08f, 1f) * pulse;
                    mpb.SetColor(ColorId, c);
                    mpb.SetColor(BaseColorId, c);
                    jamGlow.SetPropertyBlock(mpb);
                }
            }

            // the deployment microswitch disagrees with the truth (an injected failure)
            bool disagree = s.AntennaSensorDisagrees;
            if (sensorText != null && disagree)
                sensorText.text = "SENSOR SAYS " + s.AntennaSensor + " - ANTENNA IS " + (s.AntennaDeployedTruth ? "OUT" : "STOWED");
            Show(sensorLabel, disagree);

            Show(frozenLabel, s.Frozen);

            bool contact = s.HasContact && s.InContact;
            if (contactText != null && contact)
                contactText.text = string.IsNullOrEmpty(s.GroundStation) ? "GROUND CONTACT" : "IN CONTACT: " + s.GroundStation;
            Show(contactLabel, contact);

            ShowPower(s.Power);
        }

        /// <summary>Channel 2: watts on each wing, the battery's charge, and the cells brighten with output.</summary>
        void ShowPower(PowerInfo p)
        {
            for (int w = 0; w < 2; w++)
            {
                double watts = p == null ? double.NaN : (w == 0 ? p.WingMyW : p.WingPyW);
                bool has = !double.IsNaN(watts);
                if (has && wingPowerText[w] != null)
                {
                    wingPowerText[w].text = (w == 0 ? "-Y " : "+Y ") + watts.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " W";
                    wingPowerText[w].color = watts > 0.5 ? new Color(0.6f, 1f, 0.7f) : new Color(0.65f, 0.68f, 0.72f);
                }
                Show(wingPowerLabel[w], has);

                // brightness 0.45 (no output) .. 1 (the most this run has seen); plain when there's no power data
                if (has) peakWingW = Mathf.Max(peakWingW, (float)watts);
                float want = has ? Mathf.Lerp(0.45f, 1f, Mathf.Clamp01((float)watts / peakWingW)) : 1f;
                shownGlow[w] = Mathf.MoveTowards(shownGlow[w], want, Time.deltaTime * 0.8f);
                if (cellBlock == null) cellBlock = new MaterialPropertyBlock();
                Color c = new Color(shownGlow[w], shownGlow[w], shownGlow[w], 1f);
                cellBlock.SetColor(BaseColorId, c);
                cellBlock.SetColor(ColorId, c);
                foreach (Renderer r in cellFaces[w]) if (r != null) r.SetPropertyBlock(cellBlock);
            }

            bool battery = p != null && !double.IsNaN(p.BatterySocPct);
            if (battery && batteryText != null)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string t = "BATTERY " + p.BatterySocPct.ToString("0", inv) + "%";
                if (p.HasCharging) t += p.Charging ? "  CHARGING" : "  DRAINING";
                if (p.LoadShedStage > 0) t += "\nLOAD SHED " + p.LoadShedStage.ToString(inv);
                batteryText.text = t;
                batteryText.color = p.BatterySocPct < 15 || p.LoadShedStage > 0 ? new Color(1f, 0.4f, 0.35f)
                                  : p.BatterySocPct < 35 ? new Color(1f, 0.82f, 0.4f) : new Color(0.6f, 1f, 0.7f);
            }
            Show(batteryLabel, battery);
        }

        static void Show(GameObject g, bool on)
        {
            if (g != null && g.activeSelf != on) g.SetActive(on);
        }

        /// <summary>Set the hinges and the antenna for given fractions (0 stowed .. 1 out).</summary>
        void Pose(float my, float py, float ant, float jam)
        {
            for (int w = 0; w < 2; w++)
            {
                float f = Mathf.Clamp01(w == 0 ? my : py);
                float side = w == 0 ? -1f : 1f;
                // ICD 6b: hinge angle = 90 deg x fraction, about body X (Unity X), swinging out and up
                innerHinge[w].localRotation = Quaternion.AngleAxis(side * 90f * f, Vector3.right);
                // the outer panel unfolds from lying back on the inner one to straight on from it
                outerHinge[w].localRotation = Quaternion.AngleAxis(side * 180f * (1f - f), Vector3.right);
            }
            for (int i = 0; i < 4; i++)
            {
                float f = Mathf.Clamp01(i == 0 ? jam : ant);
                // a tape spring unrolls: flush stub when stowed, the full ~0.5 m when out
                float len = Mathf.Lerp(0.03f, AntennaLength, f);
                antenna[i].localScale = new Vector3(1f, 1f, len);
            }
        }
    }
}
