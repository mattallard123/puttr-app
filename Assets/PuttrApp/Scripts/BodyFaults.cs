// The setup faults the coach calls out in a 3D review (puttr-app lab/LabFunctions.js analyzePuttingStroke +
// getBiomechFaults, CoachedReview.js FAULT_LINE). From the pose file's image landmarks (0 to 1 across the picture):
//
//   the putt's window (3 + 5j + 2 to 3 + 5j + 5 s), its camera angle (wrists and hips spread more in depth than
//   across: down the line), the setup frame (the last high before the backstroke, on a smoothed wrist signal),
//   then the measures at setup against fixed thresholds.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public static class BodyFaults
    {
        public sealed class Fault { public string Key, Label; public double Severity; }
        public sealed class Result { public bool DownTheLine; public double SetupTime; public List<Fault> Faults = new List<Fault>(); }

        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "shoulder_closed", "Shoulders closed to the line" }, { "shoulder_open", "Shoulders open to the line" },
            { "eye_line_inside", "Eyes inside the ball" }, { "eye_line_outside", "Eyes outside the ball" },
            { "shoulder_over_toe", "Shoulders out over the toes" }, { "shoulder_inside_toe", "Shoulders inside the toes" },
            { "knees_straight", "Knees too straight at setup" }, { "ball_forward", "Ball too far forward" }, { "ball_back", "Ball too far back" },
            { "weight_left", "Weight favoring the left side" }, { "weight_right", "Weight favoring the right side" },
            { "stance_narrow", "Stance too narrow" }, { "stance_wide", "Stance too wide" },
        };

        public static readonly Dictionary<string, string> Lines = new Dictionary<string, string>
        {
            { "shoulder_open", "Here's the main thing to tidy up. At setup your shoulders are open to the line, aiming a touch right. Square them up and your start line cleans right up." },
            { "shoulder_closed", "At setup your shoulders are closed, aiming left of your line. Get them parallel to the target and your putts will start truer." },
            { "eye_line_inside", "Notice your eyes are set inside the ball here. Settling them right over the ball helps you see the real line." },
            { "eye_line_outside", "Your eyes are a little outside the ball at setup. Bring them back over the ball and the line gets easier to trust." },
            { "shoulder_over_toe", "Your shoulders are reaching out over your toes. Stack them back over your stance for a steadier base." },
            { "shoulder_inside_toe", "Your shoulders are tucked inside your toe line here. A touch more room gives you a steadier base." },
            { "knees_straight", "Your knees are pretty locked at setup. A soft bend helps you stay level all the way through the stroke." },
            { "ball_forward", "Your ball position is a little forward in your stance. Nudge it back toward center to catch it cleanly." },
            { "ball_back", "Your ball is set back in your stance. Move it forward to center so it starts rolling sooner." },
            { "weight_left", "Your weight is leaning left at setup. Even it out so you're balanced through the stroke." },
            { "weight_right", "Your weight is favoring your right side. Center it up for a more level strike." },
            { "stance_narrow", "Your stance is a little narrow here. Widen it slightly for a more stable base." },
            { "stance_wide", "Your stance is quite wide. Bring your feet in a touch so you can move freely." },
        };

        public const string CleanDtl = "From down the line your setup looks clean. Shoulders, eyes, and posture are all where I want them. Keep that.";
        public const string CleanFaceOn = "Face on, your setup is solid. Ball position, weight, and stance all check out. Nice.";

        sealed class P { public double X, Y, Z, Vis; }
        sealed class Frame { public double T; public Dictionary<string, P> J = new Dictionary<string, P>(); }

        static List<Frame> Read(object blaze)
        {
            var frames = new List<Frame>();
            var map = J.Obj(blaze);
            if (map == null) return frames;
            foreach (KeyValuePair<string, object> e in map)
            {
                object lm = J.Get(e.Value, "landmarks");
                if (lm == null || J.Get(e.Value, "timestamp") == null) continue;
                var f = new Frame { T = J.Num(e.Value, "timestamp") };
                foreach (KeyValuePair<string, object> p in J.Obj(lm) ?? new Dictionary<string, object>())
                    f.J[p.Key] = new P { X = J.Num(p.Value, "x"), Y = J.Num(p.Value, "y"), Z = J.Num(p.Value, "z"), Vis = J.Num(p.Value, "visibility", 1) };
                frames.Add(f);
            }
            frames.Sort((a, b) => a.T.CompareTo(b.T));
            return frames;
        }

        /// <summary>Putt j's setup faults, worst first; null when the window has no usable frames.</summary>
        public static Result Analyze(object blaze, int putt, bool lefty)
        {
            double slot = 3 + 5 * putt, start = slot + 2, end = slot + 5;
            List<Frame> whole = Read(blaze).FindAll(f => f.T >= slot && f.T <= end && f.J.ContainsKey("left_wrist") && f.J.ContainsKey("right_wrist"));
            List<Frame> all = whole.FindAll(f => f.T >= start);
            if (all.Count < 6) return null;
            double z = 0, x = 0;
            foreach (Frame f in all)
            {
                P lw = f.J["left_wrist"], rw = f.J["right_wrist"];
                f.J.TryGetValue("left_hip", out P lh); f.J.TryGetValue("right_hip", out P rh);
                z += Math.Abs(lw.Z - rw.Z) + (lh != null && rh != null ? Math.Abs(lh.Z - rh.Z) : 0);
                x += Math.Abs(lw.X - rw.X) + (lh != null && rh != null ? Math.Abs(lh.X - rh.X) : 0);
            }
            bool dtl = z > x;
            var used = new List<Frame>(); var signal = new List<double>();
            foreach (Frame f in all)
            {
                P lw = f.J["left_wrist"], rw = f.J["right_wrist"];
                if (lw.Vis < 0.6 && rw.Vis < 0.6) continue;
                used.Add(f);
                signal.Add(dtl ? (lefty ? lw.Z : rw.Z) : (lefty ? -1 : 1) * (lw.X + rw.X) / 2);
            }
            if (used.Count < 6) return null;
            int w = dtl ? 10 : 5;
            var smooth = new double[signal.Count];
            for (int i = 0; i < signal.Count; i++)
            {
                int a = Math.Max(0, i - w / 2), b = Math.Min(signal.Count - 1, i + w / 2);
                double s = 0; for (int k = a; k <= b; k++) s += signal[k];
                smooth[i] = s / (b - a + 1);
            }
            int back = 0;
            for (int i = 1; i < smooth.Length; i++) if (smooth[i] < smooth[back]) back = i;
            int setup = 0;
            for (int i = 1; i < back; i++) if (smooth[i] >= smooth[i - 1] && (i + 1 >= smooth.Length || smooth[i] >= smooth[i + 1])) setup = i;
            // The stroke was over before the window began. The window is the last 3 s before the putt's result, and a
            // slow putt or a miss is reported later than that: what is left in it is the follow through and the hands
            // coming back, so the "setup" found was the follow through (Matt, 2026-11-26: the coach froze on it). The
            // sign of it: the follow through's peak comes before the lowest point taken for the backstroke. The whole
            // 5 s clip is read then: the backstroke is the lowest point before that peak, the setup the last high before it.
            int peak = 0;
            for (int i = 1; i < smooth.Length; i++) if (smooth[i] > smooth[peak]) peak = i;
            if (peak < back && whole.Count > all.Count)
            {
                var wide = new List<Frame>(); var wideSignal = new List<double>();
                foreach (Frame f in whole)
                {
                    P lw = f.J["left_wrist"], rw = f.J["right_wrist"];
                    if (lw.Vis < 0.6 && rw.Vis < 0.6) continue;
                    wide.Add(f);
                    wideSignal.Add(dtl ? (lefty ? lw.Z : rw.Z) : (lefty ? -1 : 1) * (lw.X + rw.X) / 2);
                }
                var wideSmooth = new double[wideSignal.Count];
                for (int i = 0; i < wideSignal.Count; i++)
                {
                    int a = Math.Max(0, i - w / 2), b = Math.Min(wideSignal.Count - 1, i + w / 2);
                    double s = 0; for (int k = a; k <= b; k++) s += wideSignal[k];
                    wideSmooth[i] = s / (b - a + 1);
                }
                int until = wide.IndexOf(used[peak]);
                if (until > 2)
                {
                    int wideBack = 0;
                    for (int i = 1; i < until; i++) if (wideSmooth[i] < wideSmooth[wideBack]) wideBack = i;
                    int wideSetup = 0;
                    for (int i = 1; i < wideBack; i++) if (wideSmooth[i] >= wideSmooth[i - 1] && wideSmooth[i] >= wideSmooth[i + 1]) wideSetup = i;
                    used = wide; setup = wideSetup;
                }
            }
            Dictionary<string, P> body = used[setup].J;
            var result = new Result { DownTheLine = dtl, SetupTime = used[setup].T };
            P Get(string k) => body.TryGetValue(k, out P p) ? p : null;
            void Add(string key, double sev) => result.Faults.Add(new Fault { Key = key, Label = Labels[key], Severity = sev });
            P ls = Get("left_shoulder"), rs = Get("right_shoulder"), lf = Get("left_foot_index"), rf = Get("right_foot_index"), le = Get("left_eye"), re = Get("right_eye");
            if (dtl)
            {
                if (ls != null && rs != null && lf != null && rf != null)
                {
                    double sa = lefty ? (ls.X - rs.X) - (lf.X - rf.X) : (rs.X - ls.X) - (rf.X - lf.X);
                    if (sa > 0.05) Add("shoulder_closed", sa - 0.05); else if (sa < -0.05) Add("shoulder_open", -0.05 - sa);
                    double toe = lefty ? rs.X - rf.X : ls.X - lf.X;
                    if (toe > 0.03) Add("shoulder_over_toe", toe - 0.03); else if (toe < -0.03) Add("shoulder_inside_toe", -0.03 - toe);
                }
                P eye = lefty ? le : re;
                if (eye != null)
                {
                    bool inside = lefty ? eye.X > 0.55 : eye.X < 0.45, outside = lefty ? eye.X < 0.45 : eye.X > 0.55;
                    if (inside) Add("eye_line_inside", Math.Abs(eye.X - 0.5)); else if (outside) Add("eye_line_outside", Math.Abs(eye.X - 0.5));
                }
                P hip = Get(lefty ? "left_hip" : "right_hip"), knee = Get(lefty ? "left_knee" : "right_knee"), ankle = Get(lefty ? "left_ankle" : "right_ankle");
                if (hip != null && knee != null && ankle != null)
                {
                    double ux = knee.X - hip.X, uy = knee.Y - hip.Y, vx = ankle.X - knee.X, vy = ankle.Y - knee.Y, mu = Math.Sqrt(ux * ux + uy * uy), mv = Math.Sqrt(vx * vx + vy * vy);
                    if (mu > 0 && mv > 0)
                    {
                        double bend = Math.Acos(Math.Max(-1, Math.Min(1, (ux * vx + uy * vy) / (mu * mv)))) * 180 / Math.PI;
                        if (bend <= 5) Add("knees_straight", 5 - bend);
                    }
                }
            }
            else if (ls != null && rs != null && lf != null && rf != null)
            {
                double ball = lefty ? (rf.X - lf.X) / 2 + lf.X : (lf.X - rf.X) / 2 + rf.X;
                if (ball < 0.45) Add("ball_forward", 0.45 - ball); else if (ball > 0.55) Add("ball_back", ball - 0.55);
                double weight = ls.Y - rs.Y;
                if (weight > 0.03) Add("weight_left", weight - 0.03); else if (weight < -0.03) Add("weight_right", -0.03 - weight);
                double stance = lefty ? (rf.X - lf.X) - (rs.X - ls.X) : (lf.X - rf.X) - (ls.X - rs.X);
                if (stance < -0.03) Add("stance_narrow", -0.03 - stance); else if (stance > 0.03) Add("stance_wide", stance - 0.03);
            }
            result.Faults = result.Faults.OrderByDescending(f => f.Severity).ToList();
            return result;
        }

        // ── The body reads of a session (LabFunctions.analyzePuttingStroke + generateDataFromAnalysis) ─────────
        //
        // The session page, the manual replay and the coach's analysis all read the same numbers: for each putt's
        // stroke window, the camera angle, the four phases (setup, top of the backstroke, impact, end of the follow
        // through) and four setup measures from the image landmarks. Ported as the app has them, quirks kept.

        /// <summary>A landmark on the picture: x and y 0 to 1, z the detector's depth, visibility 0 to 1.</summary>
        public sealed class Mark { public double X, Y, Z, Vis; }

        /// <summary>The pose file read once: frames by time, each with its image landmarks.</summary>
        public sealed class Pose
        {
            public readonly List<(double T, Dictionary<string, Mark> J)> Frames = new List<(double, Dictionary<string, Mark>)>();
            public bool Empty => Frames.Count == 0;

            public static Pose Read(object blaze)
            {
                var pose = new Pose();
                foreach (Frame f in BodyFaults.Read(blaze))
                {
                    var j = new Dictionary<string, Mark>();
                    foreach (KeyValuePair<string, P> p in f.J) j[p.Key] = new Mark { X = p.Value.X, Y = p.Value.Y, Z = p.Value.Z, Vis = p.Value.Vis };
                    pose.Frames.Add((f.T, j));
                }
                return pose;
            }
        }

        /// <summary>analyzePuttingStroke's answer: the camera angle and the phase times (NaN when the app's index ran off the end).</summary>
        public sealed class Stroke
        {
            public bool DownTheLine;
            public double Setup = double.NaN, Back = double.NaN, Impact = double.NaN, Follow = double.NaN;
            public Dictionary<string, Mark> AtSetup;
        }

        /// <summary>generateDataFromAnalysis: the four setup measures for the putt's camera angle.</summary>
        public sealed class Reading
        {
            public bool DownTheLine;
            public double ShoulderAlignment, EyeLine, ShoulderToe, KneeBend;            // down the line
            public double BallPosition, WeightBias, StanceWidth, StrokeLength;         // face on
            public Stroke Stroke;
        }

        /// <summary>Putt j's stroke window on the replay's clock (LabReplayTiming puttStrokeWindow): its slot start + 2 to + 5.</summary>
        public static (double start, double end) Window(int putt) => (3 + 5 * putt + 2, 3 + 5 * putt + 5);

        /// <summary>analyzePuttingStroke for one window; null where the app throws (no frames, no hips, no wrist data).</summary>
        public static Stroke Analyze(Pose pose, double start, double end, bool lefty)
        {
            if (pose == null) return null;
            var frames = new List<Dictionary<string, Mark>>();
            var times = new List<double>();
            foreach (var f in pose.Frames)
                if (f.T >= start && f.T <= end && f.J.ContainsKey("left_wrist") && f.J.ContainsKey("right_wrist")) { frames.Add(f.J); times.Add(f.T); }
            if (frames.Count == 0) return null;
            Dictionary<string, Mark> sample = frames.Find(k => k.ContainsKey("left_hip") && k.ContainsKey("right_hip"));
            if (sample == null) return null;
            Mark lw0 = sample["left_wrist"], rw0 = sample["right_wrist"], lh0 = sample["left_hip"], rh0 = sample["right_hip"];
            bool dtl = Math.Abs(rw0.Z - lw0.Z) + Math.Abs(rh0.Z - lh0.Z) > Math.Abs(rw0.X - lw0.X) + Math.Abs(rh0.X - lh0.X);

            var deltas = new List<double>();
            var stamps = new List<double>();
            for (int i = 0; i < frames.Count; i++)
            {
                Mark lw = frames[i]["left_wrist"], rw = frames[i]["right_wrist"];
                if (rw.Vis < 0.6 && lw.Vis < 0.6) continue;
                deltas.Add(dtl ? (lefty ? lw.Z : rw.Z) : (lw.X + rw.X) / 2 * (lefty ? -1 : 1));
                stamps.Add(times[i]);
            }
            int n = deltas.Count;
            if (n == 0) return null;

            // A moving average: from i - floor(w / 2) up to (not including) i + ceil(w / 2).
            int w = dtl ? 10 : 5;
            var d = new double[n];
            for (int i = 0; i < n; i++)
            {
                int s = Math.Max(0, i - w / 2), e = Math.Min(n, i + (w + 1) / 2);
                double sum = 0; for (int k = s; k < e; k++) sum += deltas[k];
                d[i] = sum / (e - s);
            }
            double base0 = d[0];
            for (int i = 0; i < n; i++) d[i] -= base0;

            int back = 0;
            for (int i = 1; i < n; i++) if (d[i] < d[back]) back = i;
            int setup = 0;
            for (int i = back - 1; i > 0; i--) if (d[i] > d[i - 1] && d[i] > d[i + 1]) { setup = i; break; }
            double setupDelta = d[setup];
            int impact = back + 1;
            double minDiff = double.PositiveInfinity;
            for (int i = back + 1; i < n; i++)
            {
                double diff = Math.Abs(d[i] - setupDelta);
                if (diff < minDiff) { minDiff = diff; impact = i; }
                else if (i + 1 < n && minDiff < Math.Abs(d[i + 1] - setupDelta)) break;
            }
            int follow = -1;
            for (int i = impact + 2; i < n - 1; i++) if (d[i] > d[i - 1] && d[i] > d[i + 1]) { follow = i; break; }
            if (follow == -1)
            {
                double max = double.NegativeInfinity;
                for (int i = impact + 1; i < n; i++) if (d[i] > max) { max = d[i]; follow = i; }
                if (follow == -1) follow = Math.Min(impact + 1, n - 1);
            }
            double At(int i) => i >= 0 && i < n ? stamps[i] : double.NaN;
            return new Stroke
            {
                DownTheLine = dtl,
                Setup = At(setup), Back = At(back), Impact = At(impact), Follow = At(follow),
                // APP QUIRK: the body at setup is frames[setupIndex], indexed in the unfiltered frames while the
                // index counts only the frames with a visible wrist. Kept, so the numbers match the app's.
                AtSetup = setup < frames.Count ? frames[setup] : null,
            };
        }

        /// <summary>generateDataFromAnalysis; null where the app gives INVALID or throws (a landmark it needs is missing).</summary>
        public static Reading Read(Stroke a, bool lefty)
        {
            if (a == null || a.AtSetup == null) return null;
            Dictionary<string, Mark> b = a.AtSetup;
            Mark M(string k) => b.TryGetValue(k, out Mark m) ? m : null;
            Mark ls = M("left_shoulder"), rs = M("right_shoulder"), lf = M("left_foot_index"), rf = M("right_foot_index");
            var r = new Reading { DownTheLine = a.DownTheLine, Stroke = a };
            if (a.DownTheLine)
            {
                Mark eye = lefty ? M("left_eye") : M("right_eye");
                Mark hip = lefty ? M("left_hip") : M("right_hip"), knee = lefty ? M("left_knee") : M("right_knee"), ankle = lefty ? M("left_ankle") : M("right_ankle");
                if (ls == null || rs == null || lf == null || rf == null || eye == null || hip == null || knee == null || ankle == null) return null;
                r.ShoulderAlignment = lefty ? (ls.X - rs.X) - (lf.X - rf.X) : (rs.X - ls.X) - (rf.X - lf.X);
                r.EyeLine = eye.X;
                r.ShoulderToe = lefty ? rs.X - rf.X : ls.X - lf.X;
                double ux = knee.X - hip.X, uy = knee.Y - hip.Y, vx = ankle.X - knee.X, vy = ankle.Y - knee.Y;
                double mu = Math.Sqrt(ux * ux + uy * uy), mv = Math.Sqrt(vx * vx + vy * vy);
                if (mu == 0 || mv == 0) return null; // the app throws on a zero-length limb
                r.KneeBend = Math.Acos(Math.Max(-1, Math.Min(1, (ux * vx + uy * vy) / (mu * mv)))) * 180 / Math.PI;
                return r;
            }
            if (ls == null || rs == null || lf == null || rf == null) return null;
            r.BallPosition = lefty ? (rf.X - lf.X) / 2 + lf.X : (lf.X - rf.X) / 2 + rf.X;
            r.WeightBias = ls.Y - rs.Y;
            r.StanceWidth = lefty ? (rf.X - lf.X) - (rs.X - ls.X) : (lf.X - rf.X) - (ls.X - rs.X);
            r.StrokeLength = a.Follow - a.Back;
            return r;
        }

        /// <summary>Putt j's reading (its stroke window), or null.</summary>
        public static Reading ReadPutt(Pose pose, int putt, bool lefty)
        {
            (double s, double e) = Window(putt);
            return Read(Analyze(pose, s, e, lefty), lefty);
        }

        /// <summary>One camera angle's session averages (LabSessionView computeBodyMetrics: NaN values skipped per measure).</summary>
        public sealed class Average
        {
            public int Count;
            public double ShoulderAlignment = double.NaN, EyeLine = double.NaN, ShoulderToe = double.NaN, KneeBend = double.NaN;
            public double BallPosition = double.NaN, WeightBias = double.NaN, StanceWidth = double.NaN, StrokeLength = double.NaN;
        }

        /// <summary>Every putt's reading (null where it cannot be read) and the averages for each camera angle (null for an angle never filmed).</summary>
        public sealed class Session
        {
            public readonly List<Reading> Putts = new List<Reading>();
            public Average DownTheLine, FaceOn;
            public bool Any => DownTheLine != null || FaceOn != null;
        }

        /// <summary>The session's reads: the app reads six windows (5, 10 ... 30 s) whatever the putt count.</summary>
        public static Session ReadSession(Pose pose, bool lefty, int putts = 6)
        {
            var s = new Session();
            for (int j = 0; j < Math.Max(6, putts); j++) s.Putts.Add(ReadPutt(pose, j, lefty));
            s.DownTheLine = Mean(s.Putts.FindAll(r => r != null && r.DownTheLine));
            s.FaceOn = Mean(s.Putts.FindAll(r => r != null && !r.DownTheLine));
            return s;
        }

        static Average Mean(List<Reading> list)
        {
            if (list.Count == 0) return null;
            double Avg(Func<Reading, double> pick)
            {
                double sum = 0; int n = 0;
                foreach (Reading r in list) { double v = pick(r); if (!double.IsNaN(v) && !double.IsInfinity(v)) { sum += v; n++; } }
                return n > 0 ? sum / n : double.NaN;
            }
            return new Average
            {
                Count = list.Count,
                ShoulderAlignment = Avg(r => r.ShoulderAlignment), EyeLine = Avg(r => r.EyeLine), ShoulderToe = Avg(r => r.ShoulderToe), KneeBend = Avg(r => r.KneeBend),
                BallPosition = Avg(r => r.BallPosition), WeightBias = Avg(r => r.WeightBias), StanceWidth = Avg(r => r.StanceWidth), StrokeLength = Avg(r => r.StrokeLength),
            };
        }

        /// <summary>getBiomechFaults on the session averages: fault keys, worst first.</summary>
        public static List<Fault> SessionFaults(Session s, bool lefty)
        {
            var faults = new List<Fault>();
            if (s == null) return faults;
            void Add(string key, double sev) => faults.Add(new Fault { Key = key, Label = Labels[key], Severity = sev });
            Average dtl = s.DownTheLine, fo = s.FaceOn;
            if (dtl != null)
            {
                if (dtl.ShoulderAlignment > 0.05) Add("shoulder_closed", dtl.ShoulderAlignment - 0.05);
                else if (dtl.ShoulderAlignment < -0.05) Add("shoulder_open", -0.05 - dtl.ShoulderAlignment);
                bool inside = lefty ? dtl.EyeLine > 0.55 : dtl.EyeLine < 0.45, outside = lefty ? dtl.EyeLine < 0.45 : dtl.EyeLine > 0.55;
                double eye = double.IsNaN(dtl.EyeLine) || dtl.EyeLine == 0 ? 0.5 : dtl.EyeLine;
                if (inside) Add("eye_line_inside", Math.Abs(eye - 0.5)); else if (outside) Add("eye_line_outside", Math.Abs(eye - 0.5));
                if (dtl.ShoulderToe > 0.03) Add("shoulder_over_toe", dtl.ShoulderToe - 0.03);
                else if (dtl.ShoulderToe < -0.03) Add("shoulder_inside_toe", -0.03 - dtl.ShoulderToe);
                if (!double.IsNaN(dtl.KneeBend) && dtl.KneeBend <= 5) Add("knees_straight", 5 - dtl.KneeBend);
            }
            if (fo != null)
            {
                if (fo.BallPosition < 0.45) Add("ball_forward", 0.45 - fo.BallPosition);
                else if (fo.BallPosition > 0.55) Add("ball_back", fo.BallPosition - 0.55);
                if (fo.WeightBias > 0.03) Add("weight_left", fo.WeightBias - 0.03);
                else if (fo.WeightBias < -0.03) Add("weight_right", -0.03 - fo.WeightBias);
                if (fo.StanceWidth < -0.03) Add("stance_narrow", -0.03 - fo.StanceWidth);
                else if (fo.StanceWidth > 0.03) Add("stance_wide", fo.StanceWidth - 0.03);
            }
            return faults.OrderByDescending(f => f.Severity).ToList();
        }

        // ── The words for each measure ─────────────────────────────────────────────────────────────────────
        // The session page (LabSessionView) and the replay and coach screens (LabSessionReplay, BodyAnalysis) word the
        // same thresholds a little differently; coach is true for the second set.

        public static string ShoulderWords(double v) => v > 0.05 ? "Closed" : v > 0.015 ? "Slightly closed" : v < -0.05 ? "Open" : v < -0.015 ? "Slightly open" : "Square";

        public static string EyeWords(double v, bool lefty, bool coach)
        {
            string inside = lefty ? "outside" : "inside", outside = lefty ? "inside" : "outside";
            if (v < 0.45) return "Well " + inside;
            if (v < 0.49) return "Slightly " + inside;
            if (v > 0.55) return "Well " + outside;
            if (v > 0.51) return "Slightly " + outside;
            return coach ? "Over the ball" : "Over ball";
        }

        public static string ToeWords(double v, bool coach) => v > 0.015 ? (coach ? "Over front toe" : "Over toe") : v < -0.015 ? (coach ? "Inside front toe" : "Inside toe") : "In line";
        public static string KneeWords(double v, bool coach) => v <= 5 ? "Straight" : v <= 20 ? (coach ? "Slightly flexed" : "Slight flex") : (coach ? "Deeply bent" : "Deep bend");
        public static string BallWords(double v) => v < 0.45 ? "Well forward" : v < 0.49 ? "Forward" : v > 0.55 ? "Well back" : v > 0.51 ? "Back" : "Centered";
        public static string WeightWords(double v, bool coach) => v > 0.03 ? (coach ? "Left side" : "Left heavy") : v < -0.03 ? (coach ? "Right side" : "Right heavy") : "Even";
        public static string StanceWords(double v) => v < -0.03 ? "Narrow" : v > 0.03 ? "Wide" : "Shoulder width";

        /// <summary>"0.69s", or the app's dash when the stroke time could not be read.</summary>
        public static string Seconds(double v, string none = "—") => double.IsNaN(v) || double.IsInfinity(v) || v == 0 ? none : v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s";

        /// <summary>"Slight flex 15°": the knee words with the rounded angle, when there is one.</summary>
        public static string KneeValue(double v, bool coach) => KneeWords(v, coach) + (double.IsNaN(v) || v == 0 ? "" : " " + Math.Round(v, MidpointRounding.AwayFromZero) + "°");

        /// <summary>A measure's grade for its colour: 0 good, 1 a little off, 2 well off (LabSessionView getMetricColor, by the words).</summary>
        public static int Grade(string words)
        {
            string[] good = { "Square", "Over ball", "Over the ball", "In line", "Slight flex", "Slightly flexed", "Centered", "Even", "Shoulder width" };
            string[] ok = { "Slightly closed", "Slightly open", "Slightly inside", "Slightly outside", "Forward", "Back" };
            if (Array.IndexOf(good, words) >= 0) return 0;
            foreach (string o in ok) if (words == o || words.Contains(o)) return 1;
            return 2;
        }

        /// <summary>The replay and the coach's per-putt tiles: good or not, by the numbers (LabSessionReplay bodyPills).</summary>
        public static bool Good(string measure, double v)
        {
            switch (measure)
            {
                case "shoulders": return Math.Abs(v) <= 0.015;
                case "eye": case "ball": return v >= 0.49 && v <= 0.51;
                case "toe": return Math.Abs(v) <= 0.015;
                case "knees": return v > 5 && v <= 20;
                case "weight": case "stance": return Math.Abs(v) <= 0.03;
                default: return true;
            }
        }

        /// <summary>The spoken fix: the result's lead, the worst fault's line, then "Also, ..." for the next.</summary>
        public static string Line(bool made, string missSide, List<Fault> faults)
        {
            string lead = made ? "This one dropped, but watch this. " : missSide != null ? "This one leaked " + missSide.ToLowerInvariant() + ", and here's why. " : "Look at why this one missed. ";
            string words = lead + Lines[faults[0].Key];
            if (faults.Count > 1) { string next = Lines[faults[1].Key]; words += " Also, " + char.ToLowerInvariant(next[0]) + next.Substring(1); }
            return words;
        }
    }
}
