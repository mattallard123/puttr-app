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
        public sealed class Result { public bool DownTheLine; public List<Fault> Faults = new List<Fault>(); }

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
            double start = 3 + 5 * putt + 2, end = 3 + 5 * putt + 5;
            List<Frame> all = Read(blaze).FindAll(f => f.T >= start && f.T <= end && f.J.ContainsKey("left_wrist") && f.J.ContainsKey("right_wrist"));
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
            Dictionary<string, P> body = used[setup].J;
            var result = new Result { DownTheLine = dtl };
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
