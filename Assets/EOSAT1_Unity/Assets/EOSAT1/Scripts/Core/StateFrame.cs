// EOSAT-1 VR - the "state" frame from the simulator and its parser.
//
//   {"type":"state","seq":1812,"sim_time":"2026-08-20T12:30:12Z",
//    "orbit":{"pos_eci_km":[-4702,2516,4407]},
//    "attitude":{"q_body_to_eci":[0.123,-0.702,0.281,0.643]},
//    "sun":{"dir_eci":[-0.84,0.49,0.21],"in_eclipse":false},
//    "deploy":{"wing_my":{"state":"DEPLOYING","fraction":0.43},"antenna":{"state":"DEPLOYED"}}, ...}
//
// Rules from the course: skip any line you can't parse, ignore fields you don't know.
// Required: sim_time, orbit.pos_eci_km, attitude.q_body_to_eci. Everything else is optional;
// missing deploy states carry over from the previous frame, a missing Sun is computed.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Eosat1.Core
{
    public enum DeployState
    {
        Unknown = 0,
        Stowed = 1,
        Deploying = 2,
        Deployed = 3,
        Jammed = 4,
    }

    [Serializable]
    public struct DeployInfo
    {
        public bool Present;
        public DeployState State;
        public double Fraction;     // 0 stowed .. 1 fully out

        public static DeployInfo Of(DeployState s, double fraction)
        {
            return new DeployInfo { Present = true, State = s, Fraction = MathUtil.Clamp01(fraction) };
        }

        /// <summary>How far out it is, filling in a fraction when the frame only gave a state.</summary>
        public double EffectiveFraction
        {
            get
            {
                if (!Present) return 0;
                switch (State)
                {
                    case DeployState.Stowed: return 0;
                    case DeployState.Deployed: return 1;
                    default: return MathUtil.Clamp01(Fraction);
                }
            }
        }

        public string Label
        {
            get
            {
                if (!Present || State == DeployState.Unknown) return "?";
                if (State == DeployState.Deploying)
                    return "DEPLOYING " + Math.Round(EffectiveFraction * 100).ToString(CultureInfo.InvariantCulture) + "%";
                return State.ToString().ToUpperInvariant();
            }
        }

        public static DeployState ParseState(string s)
        {
            if (string.IsNullOrEmpty(s)) return DeployState.Unknown;
            switch (s.Trim().ToUpperInvariant())
            {
                case "STOWED": return DeployState.Stowed;
                case "DEPLOYING": return DeployState.Deploying;
                case "DEPLOYED": return DeployState.Deployed;
                case "JAMMED": return DeployState.Jammed;
                default: return DeployState.Unknown;
            }
        }
    }

    /// <summary>
    /// The "power" block of channel 2 (ICD 5a). Numbers the frame leaves out are NaN.
    /// wings.sada_angle_deg is DO NOT USE for the VR project and is not read.
    /// </summary>
    [Serializable]
    public sealed class PowerInfo
    {
        public double BusVoltageV = double.NaN, GeneratedW = double.NaN, ConsumedW = double.NaN, NetW = double.NaN;
        public double WingPyW = double.NaN, WingMyW = double.NaN, WingPyA = double.NaN, WingMyA = double.NaN;
        /// <summary>The body-mounted cells, all six faces together (wings excluded).</summary>
        public double BodyFacesW = double.NaN;
        public bool HasStrings, StringAEnabled = true, StringBEnabled = true;
        public double BatterySocPct = double.NaN, BatteryVoltageV = double.NaN, BatteryCurrentA = double.NaN, BatteryTempC = double.NaN;
        public bool HasCharging, Charging;
        public int LoadShedStage = -1;

        public PowerInfo Clone() { return (PowerInfo)MemberwiseClone(); }
    }

    public enum MessageKind
    {
        Unknown = 0,
        Hello = 1,
        State = 2,
        Other = 3,
    }

    /// <summary>One state frame. Times are UTC seconds since J2000.</summary>
    [Serializable]
    public sealed class StateFrame
    {
        public long Seq = -1;
        public double SimTime;
        public Vec3d PosEciKm;
        public bool HasVelocity;
        public Vec3d VelEciKmS;
        public QuatD QBodyToEci = QuatD.Identity;
        public bool HasSun;
        public Vec3d SunDirEci;
        public bool HasEclipseFlag;
        public bool InEclipse;
        /// <summary>0..1 if the source provides it (the sandbox and the sample orbit do), else NaN.</summary>
        public double SunlitFraction = double.NaN;
        public DeployInfo WingMinusY;
        public DeployInfo WingPlusY;
        public DeployInfo Antenna;
        /// <summary>Solar-array drive angle (deploy.sada_angle_deg): the wing cells face (cos a, 0, sin a) in body axes.</summary>
        public bool HasSada;
        public double SadaDeg;
        /// <summary>Body rate magnitude in deg/s if the source provides it, else NaN.</summary>
        public double BodyRateDegS = double.NaN;

        // smo-vr/1 extras: what the simulator (and the instructor / flight director) are doing.
        // Text fields are null when the frame leaves them out; numbers are NaN.
        /// <summary>Time acceleration (speed). 0 means the instructor froze the simulator.</summary>
        public double Speed = double.NaN;
        /// <summary>Mission elapsed time in seconds (met_s).</summary>
        public double MetS = double.NaN;
        /// <summary>attitude.aocs_mode: OFF SAFE_BOOT DETUMBLE COARSE_SUN NOMINAL FINE_POINT SLEW DESAT ECLIPSE INERTIAL.</summary>
        public string AocsMode;
        /// <summary>attitude.pointing: NADIR INERTIAL TARGET_TRACK SUN.</summary>
        public string Pointing;
        /// <summary>mission.sc_mode: SAFE NOMINAL SCIENCE EMERGENCY.</summary>
        public string ScMode;
        /// <summary>mission.phase: PRE_SEPARATION .. NOMINAL.</summary>
        public string Phase;
        public bool HasContact;
        public bool InContact;
        public string GroundStation;
        /// <summary>deploy.antenna.deployed: the truth.</summary>
        public bool HasAntennaTruth;
        public bool AntennaDeployedTruth;
        /// <summary>deploy.antenna.sensor: what the microswitch reports (UNKNOWN STOWED DEPLOYED JAMMED).</summary>
        public string AntennaSensor;
        /// <summary>Channel 2's power block, or null on channel 1.</summary>
        public PowerInfo Power;

        // set by the receiving side
        public double ArrivalWall;
        public bool JumpFromPrevious;

        public StateFrame Clone()
        {
            var c = (StateFrame)MemberwiseClone();
            if (Power != null) c.Power = Power.Clone();
            return c;
        }
    }

    public static class FrameParser
    {
        [ThreadStatic] static double[] buf3;
        [ThreadStatic] static double[] buf4;

        /// <summary>Parse one line. Returns false (with a reason) for anything unusable.</summary>
        public static bool TryParse(string line, out MessageKind kind, out StateFrame frame, out string error)
        {
            kind = MessageKind.Unknown;
            frame = null;
            error = null;
            if (line == null)
            {
                error = "empty line";
                return false;
            }
            object root;
            if (!MiniJson.TryParse(line, out root, out error)) return false;
            var d = root as Dictionary<string, object>;
            if (d == null)
            {
                error = "not a JSON object";
                return false;
            }
            string type;
            MiniJson.TryString(d, "type", out type);
            if (type == "hello")
            {
                kind = MessageKind.Hello;
                return true;
            }
            bool looksLikeState = type == null && d.ContainsKey("attitude") && d.ContainsKey("orbit");
            if (type != "state" && !looksLikeState)
            {
                kind = MessageKind.Other;
                return true;
            }
            kind = MessageKind.State;
            return TryParseState(d, out frame, out error);
        }

        static bool TryParseState(Dictionary<string, object> d, out StateFrame f, out string error)
        {
            if (buf3 == null) buf3 = new double[3];
            if (buf4 == null) buf4 = new double[4];
            f = new StateFrame();
            error = null;

            double seq;
            if (MiniJson.TryNumber(d, "seq", out seq) && seq >= 0 && seq < 9.0e15) f.Seq = (long)seq;

            string ts;
            double t;
            if (!MiniJson.TryString(d, "sim_time", out ts) || !Iso8601.TryParse(ts, out t))
            {
                error = "missing or unreadable sim_time";
                return false;
            }
            f.SimTime = t;

            var orbit = MiniJson.Obj(d, "orbit");
            if (!MiniJson.TryNumbers(orbit, "pos_eci_km", 3, buf3))
            {
                error = "missing orbit.pos_eci_km";
                return false;
            }
            f.PosEciKm = new Vec3d(buf3[0], buf3[1], buf3[2]);
            double rKm = f.PosEciKm.Length;
            if (rKm < 6000.0 || rKm > 500000.0)
            {
                error = "orbit.pos_eci_km is not a plausible position";
                return false;
            }
            if (MiniJson.TryNumbers(orbit, "vel_eci_km_s", 3, buf3) || MiniJson.TryNumbers(orbit, "vel_eci_kms", 3, buf3))
            {
                var v = new Vec3d(buf3[0], buf3[1], buf3[2]);
                if (v.Length > 0.1 && v.Length < 20.0)
                {
                    f.HasVelocity = true;
                    f.VelEciKmS = v;
                }
            }

            var att = MiniJson.Obj(d, "attitude");
            if (!MiniJson.TryNumbers(att, "q_body_to_eci", 4, buf4))
            {
                error = "missing attitude.q_body_to_eci";
                return false;
            }
            var q = new QuatD(buf4[0], buf4[1], buf4[2], buf4[3]);
            double qn = q.Norm;
            if (qn < 0.5 || qn > 1.5)
            {
                error = "attitude quaternion is not unit length";
                return false;
            }
            f.QBodyToEci = q.Normalized;
            double rate;
            if (MiniJson.TryNumbers(att, "rate_body_deg_s", 3, buf3))
                f.BodyRateDegS = new Vec3d(buf3[0], buf3[1], buf3[2]).Length;
            else if (MiniJson.TryNumber(att, "rate_deg_s", out rate))
                f.BodyRateDegS = Math.Abs(rate);

            var sun = MiniJson.Obj(d, "sun");
            if (MiniJson.TryNumbers(sun, "dir_eci", 3, buf3))
            {
                var s = new Vec3d(buf3[0], buf3[1], buf3[2]);
                if (s.Length > 0.1)
                {
                    f.HasSun = true;
                    f.SunDirEci = s.Normalized;
                }
            }
            bool ecl;
            if (MiniJson.TryBool(sun, "in_eclipse", out ecl))
            {
                f.HasEclipseFlag = true;
                f.InEclipse = ecl;
            }
            double frac;
            // smo-vr/1 calls it sun_fraction; sunlit_fraction is the older spelling
            if (MiniJson.TryNumber(sun, "sun_fraction", out frac) || MiniJson.TryNumber(sun, "sunlit_fraction", out frac))
                f.SunlitFraction = MathUtil.Clamp01(frac);

            var deploy = MiniJson.Obj(d, "deploy");
            f.WingMinusY = ReadDeploy(MiniJson.Obj(deploy, "wing_my"));
            f.WingPlusY = ReadDeploy(MiniJson.Obj(deploy, "wing_py"));
            f.Antenna = ReadDeploy(MiniJson.Obj(deploy, "antenna"));
            double sada;
            if (MiniJson.TryNumber(deploy, "sada_angle_deg", out sada) && !double.IsNaN(sada) && !double.IsInfinity(sada))
            {
                f.HasSada = true;
                f.SadaDeg = sada;
            }

            var antenna = MiniJson.Obj(deploy, "antenna");
            bool truth;
            if (MiniJson.TryBool(antenna, "deployed", out truth))
            {
                f.HasAntennaTruth = true;
                f.AntennaDeployedTruth = truth;
            }
            f.AntennaSensor = Word(antenna, "sensor");

            double num;
            if (MiniJson.TryNumber(d, "speed", out num) && num >= 0 && num < 1e7) f.Speed = num;
            if (MiniJson.TryNumber(d, "met_s", out num) && MathUtil.IsFinite(num)) f.MetS = num;
            f.AocsMode = Word(att, "aocs_mode");
            f.Pointing = Word(att, "pointing");

            var mission = MiniJson.Obj(d, "mission");
            f.ScMode = Word(mission, "sc_mode");
            f.Phase = Word(mission, "phase");
            bool contact;
            if (MiniJson.TryBool(mission, "in_contact", out contact))
            {
                f.HasContact = true;
                f.InContact = contact;
            }
            string gs;
            if (MiniJson.TryString(mission, "ground_station", out gs) && gs != null) f.GroundStation = gs.Trim();

            f.Power = ReadPower(MiniJson.Obj(d, "power"));
            return true;
        }

        /// <summary>The protocol this code speaks. A hello whose major version differs is refused.</summary>
        public const string SupportedSchema = "smo-vr/1";

        /// <summary>
        /// Read the "schema" of a hello line. Returns false, with the schema it found, when its major
        /// version is not the one this code speaks (smo-vr/1). A hello without a schema is accepted.
        /// </summary>
        public static bool HelloSchemaOk(string line, out string schema)
        {
            schema = null;
            object root;
            string err;
            if (!MiniJson.TryParse(line, out root, out err)) return true;
            var d = root as Dictionary<string, object>;
            if (d == null || !MiniJson.TryString(d, "schema", out schema) || string.IsNullOrEmpty(schema)) return true;
            return Major(schema) == Major(SupportedSchema);
        }

        static string Major(string schema)
        {
            string s = schema.Trim().ToLowerInvariant();
            int dot = s.IndexOf('.', Math.Max(0, s.IndexOf('/')));
            return dot >= 0 ? s.Substring(0, dot) : s;
        }

        static PowerInfo ReadPower(Dictionary<string, object> p)
        {
            if (p == null) return null;
            var w = new PowerInfo();
            w.BusVoltageV = Num(p, "bus_voltage_v");
            w.GeneratedW = Num(p, "generated_w");
            w.ConsumedW = Num(p, "consumed_w");
            w.NetW = Num(p, "net_w");
            if (double.IsNaN(w.NetW) && !double.IsNaN(w.GeneratedW) && !double.IsNaN(w.ConsumedW)) w.NetW = w.GeneratedW - w.ConsumedW;
            var wings = MiniJson.Obj(p, "wings");
            var py = MiniJson.Obj(wings, "wing_py");
            var my = MiniJson.Obj(wings, "wing_my");
            w.WingPyW = Num(py, "power_w");
            w.WingMyW = Num(my, "power_w");
            w.WingPyA = Num(py, "current_a");
            w.WingMyA = Num(my, "current_a");
            var faces = MiniJson.Obj(p, "body_faces");
            if (faces != null)
            {
                double sum = 0;
                bool any = false;
                foreach (var kv in faces)
                {
                    double fw = Num(kv.Value as Dictionary<string, object>, "power_w");
                    if (double.IsNaN(fw)) continue;
                    sum += fw;
                    any = true;
                }
                if (any) w.BodyFacesW = sum;
            }
            var strings = MiniJson.Obj(p, "strings");
            bool en;
            if (MiniJson.TryBool(MiniJson.Obj(strings, "a"), "enabled", out en)) { w.HasStrings = true; w.StringAEnabled = en; }
            if (MiniJson.TryBool(MiniJson.Obj(strings, "b"), "enabled", out en)) { w.HasStrings = true; w.StringBEnabled = en; }
            var bat = MiniJson.Obj(p, "battery");
            w.BatterySocPct = Num(bat, "soc_pct");
            w.BatteryVoltageV = Num(bat, "voltage_v");
            w.BatteryCurrentA = Num(bat, "current_a");
            w.BatteryTempC = Num(bat, "temp_c");
            bool ch;
            if (MiniJson.TryBool(bat, "charging", out ch)) { w.HasCharging = true; w.Charging = ch; }
            double shed;
            if (MiniJson.TryNumber(p, "load_shed_stage", out shed) && shed >= 0 && shed < 100) w.LoadShedStage = (int)shed;
            return w;
        }

        static double Num(Dictionary<string, object> o, string key)
        {
            double v;
            return MiniJson.TryNumber(o, key, out v) ? v : double.NaN;
        }

        /// <summary>An enum-like word, upper case, or null when absent or empty.</summary>
        static string Word(Dictionary<string, object> o, string key)
        {
            string v;
            if (!MiniJson.TryString(o, key, out v) || v == null) return null;
            v = v.Trim().ToUpperInvariant();
            if (v.Length == 0) return null;
            return v.Length > 32 ? v.Substring(0, 32) : v;
        }

        static DeployInfo ReadDeploy(Dictionary<string, object> o)
        {
            if (o == null) return new DeployInfo();
            string s;
            DeployState st = MiniJson.TryString(o, "state", out s) ? DeployInfo.ParseState(s) : DeployState.Unknown;
            double fr;
            bool hasFraction = MiniJson.TryNumber(o, "fraction", out fr);
            if (st == DeployState.Unknown && !hasFraction) return new DeployInfo();
            if (!hasFraction)
            {
                fr = st == DeployState.Deployed ? 1.0 : (st == DeployState.Deploying ? 0.5 : 0.0);
            }
            if (st == DeployState.Unknown) st = fr >= 1.0 ? DeployState.Deployed : (fr <= 0.0 ? DeployState.Stowed : DeployState.Deploying);
            return DeployInfo.Of(st, fr);
        }
    }

    /// <summary>Minimal ISO-8601 reader for "2026-08-20T12:30:12Z" (fractions and offsets allowed).</summary>
    public static class Iso8601
    {
        public static bool TryParse(string s, out double sJ2000)
        {
            sJ2000 = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            try
            {
                if (s.Length >= 19 && s[4] == '-' && s[7] == '-' && (s[10] == 'T' || s[10] == ' ' || s[10] == 't') &&
                    s[13] == ':' && s[16] == ':')
                {
                    int year = Int(s, 0, 4), month = Int(s, 5, 2), day = Int(s, 8, 2);
                    int hour = Int(s, 11, 2), minute = Int(s, 14, 2), second = Int(s, 17, 2);
                    int k = 19;
                    double frac = 0;
                    if (k < s.Length && (s[k] == '.' || s[k] == ','))
                    {
                        int start = ++k;
                        while (k < s.Length && s[k] >= '0' && s[k] <= '9') k++;
                        if (k > start)
                            frac = double.Parse("0." + s.Substring(start, Math.Min(k - start, 9)), CultureInfo.InvariantCulture);
                    }
                    double offset = 0;
                    if (k < s.Length)
                    {
                        char z = s[k];
                        if (z == 'Z' || z == 'z')
                        {
                            k++;
                        }
                        else if ((z == '+' || z == '-') && k + 3 <= s.Length)
                        {
                            int oh = Int(s, k + 1, 2);
                            int om = 0;
                            int after = k + 3;
                            if (after < s.Length && s[after] == ':') after++;
                            if (after + 2 <= s.Length) om = Int(s, after, 2);
                            offset = (z == '+' ? 1 : -1) * (oh * 3600 + om * 60);
                        }
                    }
                    if (second == 60) second = 59;   // leap second: close enough
                    var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
                    sJ2000 = AstroTime.SecondsSinceJ2000(dt) + frac - offset;
                    return MathUtil.IsFinite(sJ2000);
                }
                DateTime parsed;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                {
                    sJ2000 = AstroTime.SecondsSinceJ2000(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
                    return true;
                }
            }
            catch (Exception)
            {
                // fall through
            }
            return false;
        }

        static int Int(string s, int start, int len)
        {
            int v = 0;
            for (int i = start; i < start + len; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') throw new FormatException("digit expected");
                v = v * 10 + (c - '0');
            }
            return v;
        }
    }
}
