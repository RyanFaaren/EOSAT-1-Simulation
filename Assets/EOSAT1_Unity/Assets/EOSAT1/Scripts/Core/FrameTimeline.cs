// EOSAT-1 VR - turns a ~1 Hz stream of frames into smooth motion.
//
// Course rules this implements (slide 24):
//   * One frame a second: interpolate between frames.
//   * When time is sped up: jump instead (frames far apart in sim time are not interpolated).
//   * Time jumps: the clock can go backwards when a saved point is reloaded -> start over.
//   * Duplicates and stale frames are dropped.
//
// The display runs about one frame interval behind the newest frame, so there is
// always a frame on each side to interpolate between. If the stream stalls, the
// picture holds on the last frame instead of guessing.
using System;
using System.Collections.Generic;

namespace Eosat1.Core
{
    /// <summary>Everything the scene needs for one rendered frame.</summary>
    public sealed class DisplayState
    {
        public bool Valid;
        public double SimTime;
        public long Seq = -1;
        public Vec3d PosEciKm;
        public bool HasVelocity;
        /// <summary>True when the velocity was worked out from positions rather than sent by the source.</summary>
        public bool VelocityEstimated;
        public Vec3d VelEciKmS;
        public QuatD QBodyToEci = QuatD.Identity;
        public bool HasSun;
        public Vec3d SunDirEci;
        public bool InEclipse;
        public double SunlitFraction = double.NaN;
        public DeployInfo WingMinusY;
        public DeployInfo WingPlusY;
        public DeployInfo Antenna;
        public bool HasSada;
        public double SadaDeg;
        public double BodyRateDegS = double.NaN;

        // what the simulator, instructor and flight director are doing (see StateFrame)
        public double Speed = double.NaN;
        public double MetS = double.NaN;
        public string AocsMode, Pointing, ScMode, Phase, GroundStation, AntennaSensor;
        public bool HasContact, InContact, HasAntennaTruth, AntennaDeployedTruth;
        /// <summary>
        /// The instructor froze the simulator: speed 0, or the server is repeating its last frame
        /// (the link is alive but simulation time is standing still).
        /// </summary>
        public bool Frozen;

        /// <summary>
        /// The antenna's microswitch says something different from the truth (a failure the
        /// instructor injected): e.g. sensor JAMMED/STOWED while the elements are out, or the reverse.
        /// </summary>
        public bool AntennaSensorDisagrees
        {
            get
            {
                if (!HasAntennaTruth || string.IsNullOrEmpty(AntennaSensor) || AntennaSensor == "UNKNOWN") return false;
                bool sensorOut = AntennaSensor == "DEPLOYED";
                return sensorOut != AntennaDeployedTruth;
            }
        }

        /// <summary>Changes whenever the picture should snap instead of animate (time jump, restart).</summary>
        public int Discontinuity;
        public bool Interpolating;
        /// <summary>Wall-clock seconds since the newest frame arrived.</summary>
        public double DataAge;
        /// <summary>Sim seconds per wall second, as observed.</summary>
        public double SimRate = 1.0;
        /// <summary>
        /// True while no new frame has come in and the spacecraft is being carried on along its
        /// orbit and its spin from the last data (Live link only). PredictedS is how far ahead, in sim seconds.
        /// </summary>
        public bool Predicting;
        public double PredictedS;
        /// <summary>The prediction reached its limit and the picture is holding still.</summary>
        public bool PredictionCapped;

        public void CopyFrom(StateFrame f)
        {
            Valid = true;
            SimTime = f.SimTime;
            Seq = f.Seq;
            PosEciKm = f.PosEciKm;
            HasVelocity = f.HasVelocity;
            VelocityEstimated = false;
            VelEciKmS = f.VelEciKmS;
            QBodyToEci = f.QBodyToEci;
            HasSun = f.HasSun;
            SunDirEci = f.SunDirEci;
            InEclipse = f.InEclipse;
            SunlitFraction = f.SunlitFraction;
            WingMinusY = f.WingMinusY;
            WingPlusY = f.WingPlusY;
            Antenna = f.Antenna;
            Predicting = false;
            PredictedS = 0;
            PredictionCapped = false;
            HasSada = f.HasSada;
            SadaDeg = f.SadaDeg;
            BodyRateDegS = f.BodyRateDegS;
            CopyMission(f);
        }

        /// <summary>The discrete fields (modes, contact, speed, antenna sensor): no interpolating these.</summary>
        public void CopyMission(StateFrame f)
        {
            Speed = f.Speed;
            MetS = f.MetS;
            AocsMode = f.AocsMode;
            Pointing = f.Pointing;
            ScMode = f.ScMode;
            Phase = f.Phase;
            HasContact = f.HasContact;
            InContact = f.InContact;
            GroundStation = f.GroundStation;
            HasAntennaTruth = f.HasAntennaTruth;
            AntennaDeployedTruth = f.AntennaDeployedTruth;
            AntennaSensor = f.AntennaSensor;
        }
    }

    public sealed class FrameTimeline
    {
        /// <summary>Frames further apart than this (sim seconds) are shown as a jump, not interpolated.</summary>
        public double MaxInterpolationGap = 6.0;
        /// <summary>Sim time going back by more than this is treated as a reload.</summary>
        public double BackwardJumpTolerance = 0.5;
        public int Capacity = 32;
        /// <summary>When frames stop coming, carry the spacecraft on from the last data (dead reckoning). Live link turns this on.</summary>
        public bool PredictWhenStale;
        /// <summary>Wall seconds without data before the link counts as weak (for warnings).</summary>
        public double StaleAfterS = 2.5;
        /// <summary>Never predict further ahead than this (sim seconds); after that the picture holds.</summary>
        public double MaxPredictS = 120.0;
        bool keepalive;   // the server is repeating its last frame: the link is fine, the simulation is frozen
        double coast;     // sim seconds the clock has run on past the newest frame (the prediction)

        readonly List<StateFrame> frames = new List<StateFrame>();
        double displayT;
        bool hasDisplay;
        double simRate = 1.0;
        double lastArrival = double.NaN;
        StateFrame lastAccepted;

        public long Received, Duplicates, OutOfOrder, BackwardJumps, ForwardJumps, Resyncs;
        public int Discontinuity { get; private set; }
        public double SimRate { get { return simRate; } }
        public double LastArrival { get { return lastArrival; } }
        public int Count { get { return frames.Count; } }
        public double DisplayTime { get { return displayT; } }
        /// <summary>The server is repeating its last frame (the instructor froze the simulation).</summary>
        public bool Frozen { get { return frames.Count > 0 && IsFrozen(frames[frames.Count - 1]); } }

        /// <summary>Speed 0 when the frame says its speed; otherwise, the server repeating itself.</summary>
        bool IsFrozen(StateFrame newest)
        {
            if (!double.IsNaN(newest.Speed)) return newest.Speed == 0.0;
            return keepalive;
        }
        public StateFrame Newest { get { return frames.Count > 0 ? frames[frames.Count - 1] : null; } }

        public void Clear()
        {
            keepalive = false;
            coast = 0;
            frames.Clear();
            hasDisplay = false;
            lastAccepted = null;
            lastArrival = double.NaN;
            simRate = 1.0;
            Discontinuity++;
        }

        public void Push(StateFrame f)
        {
            if (f == null) return;
            Received++;
            StateFrame last = lastAccepted;
            if (last != null)
            {
                double dt = f.SimTime - last.SimTime;
                if (f.Seq >= 0 && f.Seq == last.Seq && Math.Abs(dt) < 1e-6)
                {
                    Duplicates++;
                    lastArrival = f.ArrivalWall;   // a repeat while frozen still proves the link is alive
                    keepalive = true;
                    Refresh(last, f);
                    return;
                }
                if (dt < -BackwardJumpTolerance)
                {
                    // Saved point reloaded (or the recording looped): forget the past.
                    BackwardJumps++;
                    frames.Clear();
                    CarryOver(last, f);
                    frames.Add(f);
                    lastAccepted = f;
                    lastArrival = f.ArrivalWall;
                    displayT = f.SimTime;
                    hasDisplay = true;
                    simRate = 1.0;
                    coast = 0;
                    Discontinuity++;
                    return;
                }
                if (dt <= 1e-6)
                {
                    if (dt > -1e-6)
                    {
                        Duplicates++;
                        lastArrival = f.ArrivalWall;
                        keepalive = true;
                        Refresh(last, f);
                    }
                    else OutOfOrder++;
                    return;
                }
                CarryOver(last, f);
                double wallGap = f.ArrivalWall - lastArrival;
                if (!double.IsNaN(lastArrival) && wallGap > 0.02)
                {
                    double r = dt / wallGap;
                    if (r > simRate * 3.0 || r < simRate / 3.0) simRate = r;
                    else simRate += (r - simRate) * 0.3;
                    simRate = MathUtil.Clamp(simRate, 0.01, 100000.0);
                }
                if (dt > MaxInterpolationGap)
                {
                    f.JumpFromPrevious = true;
                    ForwardJumps++;
                }
            }
            frames.Add(f);
            while (frames.Count > Capacity) frames.RemoveAt(0);
            keepalive = false;
            if (coast > 0 && hasDisplay && !f.JumpFromPrevious)
                displayT = Math.Min(f.SimTime, displayT + coast);   // pick up where the prediction had got to
            coast = 0;
            lastAccepted = f;
            lastArrival = f.ArrivalWall;
            if (!hasDisplay)
            {
                displayT = f.SimTime;
                hasDisplay = true;
                Discontinuity++;
            }
            else if (f.JumpFromPrevious)
            {
                displayT = f.SimTime;
                Discontinuity++;
            }
        }

        /// <summary>Fields a frame left out keep their previous values.</summary>
        /// <summary>
        /// A repeat of the same instant can still bring news: the simulator re-sends when a command
        /// lands (a phase change), and while frozen the instructor can still inject a failure or the
        /// flight director deploy something. Take its discrete fields into the frame already held.
        /// </summary>
        static void Refresh(StateFrame held, StateFrame f)
        {
            if (f.WingMinusY.Present) held.WingMinusY = f.WingMinusY;
            if (f.WingPlusY.Present) held.WingPlusY = f.WingPlusY;
            if (f.Antenna.Present) held.Antenna = f.Antenna;
            if (!double.IsNaN(f.Speed)) held.Speed = f.Speed;
            if (f.AocsMode != null) held.AocsMode = f.AocsMode;
            if (f.Pointing != null) held.Pointing = f.Pointing;
            if (f.ScMode != null) held.ScMode = f.ScMode;
            if (f.Phase != null) held.Phase = f.Phase;
            if (f.HasContact)
            {
                held.HasContact = true;
                held.InContact = f.InContact;
                held.GroundStation = f.GroundStation;
            }
            if (f.HasAntennaTruth)
            {
                held.HasAntennaTruth = true;
                held.AntennaDeployedTruth = f.AntennaDeployedTruth;
            }
            if (f.AntennaSensor != null) held.AntennaSensor = f.AntennaSensor;
        }

        static void CarryOver(StateFrame prev, StateFrame f)
        {
            if (!f.WingMinusY.Present) f.WingMinusY = prev.WingMinusY;
            if (!f.WingPlusY.Present) f.WingPlusY = prev.WingPlusY;
            if (!f.Antenna.Present) f.Antenna = prev.Antenna;
            if (f.AocsMode == null) f.AocsMode = prev.AocsMode;
            if (f.Pointing == null) f.Pointing = prev.Pointing;
            if (f.ScMode == null) f.ScMode = prev.ScMode;
            if (f.Phase == null) f.Phase = prev.Phase;
            if (f.AntennaSensor == null) f.AntennaSensor = prev.AntennaSensor;
        }

        /// <summary>Advance the display clock by a wall-clock step.</summary>
        public void Advance(double dtWall)
        {
            int n = frames.Count;
            if (n == 0 || !hasDisplay) return;
            StateFrame newest = frames[n - 1];
            if (n == 1 || newest.JumpFromPrevious)
            {
                displayT = newest.SimTime;
                if (PredictWhenStale && !keepalive) coast += dtWall * simRate;
                return;
            }
            double gap = newest.SimTime - frames[n - 2].SimTime;
            double lag = MathUtil.Clamp(gap, 0.0, MaxInterpolationGap);
            double target = newest.SimTime - lag;
            displayT += dtWall * simRate;
            double err = target - displayT;
            if (Math.Abs(err) > Math.Max(2.0, 3.0 * lag))
            {
                displayT = target;          // way off (e.g. after a stall): resync
                Resyncs++;
            }
            else
            {
                displayT += err * Math.Min(1.0, dtWall * 0.8);
            }
            if (displayT >= newest.SimTime)
            {
                // caught up with the data: hold the frame clock there, and (live) let the prediction run on
                displayT = newest.SimTime;
                if (PredictWhenStale && !keepalive) coast += dtWall * simRate;
            }
            else coast = 0;
            if (displayT < frames[0].SimTime) displayT = frames[0].SimTime;
        }

        /// <summary>Fill <paramref name="s"/> with the interpolated state at the display time.</summary>
        public bool Sample(DisplayState s, double wallNow)
        {
            int n = frames.Count;
            if (n == 0)
            {
                s.Valid = false;
                return false;
            }
            int j = 0;
            for (int k = n - 1; k >= 0; k--)
            {
                if (frames[k].SimTime <= displayT)
                {
                    j = k;
                    break;
                }
            }
            StateFrame a = frames[j];
            StateFrame b = (j + 1 < n && !frames[j + 1].JumpFromPrevious) ? frames[j + 1] : null;
            s.CopyFrom(a);
            s.Interpolating = false;
            if (b != null && displayT > a.SimTime)
            {
                double span = b.SimTime - a.SimTime;
                double t = span > 1e-9 ? MathUtil.Clamp01((displayT - a.SimTime) / span) : 0.0;
                s.Interpolating = t > 0 && t < 1;
                s.SimTime = a.SimTime + span * t;
                s.PosEciKm = Vec3d.Lerp(a.PosEciKm, b.PosEciKm, t);
                s.QBodyToEci = QuatD.Slerp(a.QBodyToEci, b.QBodyToEci, t);
                if (a.HasSun && b.HasSun)
                {
                    s.SunDirEci = Vec3d.Lerp(a.SunDirEci, b.SunDirEci, t).Normalized;
                    s.HasSun = true;
                }
                if (t >= 0.5)
                {
                    s.InEclipse = b.InEclipse;
                    s.Seq = b.Seq;
                    s.Antenna = b.Antenna;
                    s.CopyMission(b);
                }
                if (!double.IsNaN(a.MetS) && !double.IsNaN(b.MetS)) s.MetS = a.MetS + (b.MetS - a.MetS) * t;
                if (!double.IsNaN(a.SunlitFraction) && !double.IsNaN(b.SunlitFraction))
                    s.SunlitFraction = a.SunlitFraction + (b.SunlitFraction - a.SunlitFraction) * t;
                s.WingMinusY = LerpDeploy(a.WingMinusY, b.WingMinusY, t);
                s.WingPlusY = LerpDeploy(a.WingPlusY, b.WingPlusY, t);
                if (a.HasSada && b.HasSada)
                {
                    // the short way round, so 359 -> 1 doesn't spin the long way
                    double delta = ((b.SadaDeg - a.SadaDeg) % 360.0 + 540.0) % 360.0 - 180.0;
                    s.SadaDeg = a.SadaDeg + delta * t;
                    s.HasSada = true;
                }
                if (a.HasVelocity && b.HasVelocity)
                {
                    s.VelEciKmS = Vec3d.Lerp(a.VelEciKmS, b.VelEciKmS, t);
                    s.HasVelocity = true;
                }
                else if (span > 0 && span <= 120.0)
                {
                    s.VelEciKmS = (b.PosEciKm - a.PosEciKm) / span;
                    s.HasVelocity = true;
                    s.VelocityEstimated = true;
                }
                if (double.IsNaN(s.BodyRateDegS) && span > 0 && span <= 3.0)
                    s.BodyRateDegS = QuatD.Angle(a.QBodyToEci, b.QBodyToEci) * MathUtil.Rad2Deg / span;
            }
            else if (j > 0 && !a.JumpFromPrevious)
            {
                StateFrame p = frames[j - 1];
                double span = a.SimTime - p.SimTime;
                if (!s.HasVelocity && span > 0 && span <= 120.0)
                {
                    s.VelEciKmS = (a.PosEciKm - p.PosEciKm) / span;
                    s.HasVelocity = true;
                    s.VelocityEstimated = true;
                }
                if (double.IsNaN(s.BodyRateDegS) && span > 0 && span <= 3.0)
                    s.BodyRateDegS = QuatD.Angle(p.QBodyToEci, a.QBodyToEci) * MathUtil.Rad2Deg / span;
            }
            s.Discontinuity = Discontinuity;
            s.DataAge = double.IsNaN(lastArrival) ? double.PositiveInfinity : Math.Max(0.0, wallNow - lastArrival);
            s.SimRate = simRate;
            // frozen: the instructor set speed 0, or the server keeps repeating the last frame
            s.Frozen = IsFrozen(frames[n - 1]);
            s.Predicting = false;
            s.PredictedS = 0;
            s.PredictionCapped = false;
            Predict(s, wallNow);
            return true;
        }

        /// <summary>
        /// Dead reckoning for a weak link: once the display has caught up with the newest frame and
        /// no new one has come, the clock keeps running (coast) and the spacecraft is carried on from the last data:
        /// round its orbit (at the speed it had) and on with the spin it had, up to MaxPredictS.
        /// When a real frame arrives, Advance() snaps back onto it.
        /// </summary>
        void Predict(DisplayState s, double wallNow)
        {
            int n = frames.Count;
            if (!PredictWhenStale || keepalive || n == 0 || double.IsNaN(lastArrival)) return;
            StateFrame last = frames[n - 1];
            if (displayT < last.SimTime - 1e-6) return;   // still playing frames we have
            double ahead = coast;                          // how far the clock has run on past the newest frame
            if (ahead <= 1e-6) return;
            if (ahead > MaxPredictS)
            {
                ahead = MaxPredictS;
                s.PredictionCapped = true;
            }

            // position: round the orbit (a circle through the last position, at the last speed)
            Vec3d r = last.PosEciKm;
            if (s.HasVelocity)
            {
                Vec3d v = s.VelEciKmS;
                Vec3d h = Vec3d.Cross(r, v);
                double hl = h.Length, rl = r.Length;
                if (hl > 1e-6 && rl > 1.0)
                {
                    Vec3d k = h / hl;
                    double theta = v.Length / rl * ahead;
                    s.PosEciKm = RotateAbout(r, k, theta);
                    s.VelEciKmS = RotateAbout(v, k, theta);
                }
                else s.PosEciKm = r + v * ahead;
            }
            else s.PosEciKm = r;

            // attitude: keep turning the way it was turning between the last two frames
            QuatD q = last.QBodyToEci;
            if (n >= 2 && !last.JumpFromPrevious)
            {
                StateFrame prev = frames[n - 2];
                double span = last.SimTime - prev.SimTime;
                if (span > 1e-6 && span <= 30.0)
                {
                    QuatD dq = (prev.QBodyToEci.Conjugate * last.QBodyToEci).Normalized;
                    if (dq.w < 0) dq = new QuatD(-dq.x, -dq.y, -dq.z, -dq.w);
                    double half = Math.Acos(MathUtil.Clamp(dq.w, -1.0, 1.0));
                    double sinHalf = Math.Sin(half);
                    if (sinHalf > 1e-9)
                    {
                        var axis = new Vec3d(dq.x / sinHalf, dq.y / sinHalf, dq.z / sinHalf);
                        q = (last.QBodyToEci * QuatD.AxisAngle(axis, 2.0 * half * ahead / span)).Normalized;
                    }
                }
            }
            s.QBodyToEci = q;
            s.SimTime = last.SimTime + ahead;
            s.Interpolating = false;
            s.Predicting = true;
            s.PredictedS = ahead;
        }

        /// <summary>Rodrigues: turn x about the unit axis k by theta radians.</summary>
        static Vec3d RotateAbout(Vec3d x, Vec3d k, double theta)
        {
            double c = Math.Cos(theta), sn = Math.Sin(theta);
            return x * c + Vec3d.Cross(k, x) * sn + k * (Vec3d.Dot(k, x) * (1.0 - c));
        }

        static DeployInfo LerpDeploy(DeployInfo a, DeployInfo b, double t)
        {
            if (!a.Present) return b;
            if (!b.Present) return a;
            DeployInfo r = t >= 0.5 ? b : a;
            r.Fraction = a.EffectiveFraction + (b.EffectiveFraction - a.EffectiveFraction) * t;
            if (r.State == DeployState.Stowed && r.Fraction > 0.0) r.State = DeployState.Deploying;
            if (r.State == DeployState.Deployed && r.Fraction < 1.0) r.State = DeployState.Deploying;
            return r;
        }
    }
}
