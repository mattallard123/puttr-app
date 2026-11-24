// The numbers a 3D session's screens show, kept apart from the screens so they can be tested. Ported from puttr-app:
//
//   lab/captureCheckInfo.js      parseCaptureCheck, describeCapture (how the session was filmed)
//   lab/LabSessionView.js        rolloutFor, getPuttData, calculateNumbers (the Ball overview)
//   common/PuttCard.js           computePuttDisplay (a putt's card: grade, pace bar, entry)
//
// A lab putt row (lab_session_putts) has ballPath and holeBounds as JSON strings, distance, tee, rollout (may be
// null) and entry: "[x, y, degrees, quadrant]" for a make, "0" for a miss. The app reads made as entry != 0.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PuttrApp
{
    public static class LabCapture
    {
        public sealed class Info { public bool Ok; public string Short, Title, Detail; }

        /// <summary>lab_sessions.captureCheck as an object with a verdict, or null (sessions from before the check have none).</summary>
        public static object Parse(object session)
        {
            object raw = J.Get(session, "captureCheck");
            object cap = raw is string s ? J.Parse(s) : raw;
            return J.Str(cap, "verdict").Length > 0 ? cap : null;
        }

        static (string shortWords, string detail) Perspective(object halves)
        {
            string first = J.Str(halves, "first"), second = J.Str(halves, "second");
            if (first == "downTheLine" && second == "downTheLine") return ("All down the line", "it looks like the camera stayed down the line for every putt and never moved to the face on view");
            if (first == "faceOn" && second == "faceOn") return ("All face on", "it looks like the camera stayed face on for every putt instead of starting down the line");
            if (first == "faceOn" && second == "downTheLine") return ("Views swapped", "it looks like the views were swapped, face on first and down the line second");
            if (first == "unknown" && second == "unknown") return ("Camera angle off", "the camera angle was unclear for most putts");
            var parts = new List<string>();
            if (first != "downTheLine") parts.Add(first == "faceOn" ? "the first putts were face on instead of down the line" : "the camera angle for the first putts was unclear");
            if (second != "faceOn") parts.Add(second == "downTheLine" ? "the last putts stayed down the line instead of face on" : "the camera angle for the last putts was unclear");
            return ("Camera angle off", parts.Count > 0 ? string.Join(", and ", parts) : "the camera angles did not match the setup guide");
        }

        /// <summary>describeCapture: player words for the card's pill (Short) and the session page's banner (Title, Detail).</summary>
        public static Info Describe(object cap)
        {
            if (cap == null) return null;
            if (J.Str(cap, "verdict") == "ok")
                return new Info { Ok = true, Short = "Good capture", Title = "Good capture", Detail = "Both camera views were recorded correctly: down the line, then face on." };
            var reasons = new List<string>();
            foreach (object r in J.List(J.Get(cap, "reasons"))) if (r is string s) reasons.Add(s);
            var details = new List<string>();
            string shortWords = "Check setup";
            if (reasons.Contains("wrongPerspective"))
            {
                var p = Perspective(J.Get(cap, "halves"));
                shortWords = p.shortWords;
                details.Add(p.detail);
            }
            if (reasons.Contains("poorVisibility"))
            {
                if (shortWords == "Check setup") shortWords = "Not fully visible";
                details.Add("your full body was not visible for some putts");
            }
            if (reasons.Contains("poorFraming"))
            {
                if (shortWords == "Check setup") shortWords = "Out of frame";
                details.Add("you were partly out of frame");
            }
            string joined;
            if (details.Count > 0)
            {
                string both = string.Join(", and ", details.GetRange(0, Math.Min(2, details.Count)));
                joined = char.ToUpperInvariant(both[0]) + both.Substring(1) + ".";
            }
            else joined = "This session was not captured the way the analysis expects.";
            return new Info { Ok = false, Short = shortWords, Title = "Check your camera setup", Detail = joined + " Watch the setup guide and record a new session for the most accurate analysis." };
        }
    }

    public static class LabBall
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The app's made: entry != 0 (a number not 0, or a string that is not a number equal to 0, such as "[12,40,8,...]").</summary>
        public static bool Made(object putt)
        {
            object e = J.Get(putt, "entry");
            if (e == null) return false;
            if (e is double d) return d != 0;
            if (e is bool b) return b;
            if (e is string s)
            {
                string t = s.Trim();
                if (t.Length == 0) return false;
                return !double.TryParse(t, NumberStyles.Float, Inv, out double n) || n != 0;
            }
            return true;
        }

        /// <summary>The stored entry's degrees (getHoleEntry's third value), or null for a miss.</summary>
        public static double? EntryDegrees(object putt)
        {
            object e = J.Get(putt, "entry");
            object parsed = e is string s ? J.Parse(s) : e;
            List<object> list = J.List(parsed);
            if (list.Count < 3) return null;
            return Puttr.PuttrJson.Number(list[2], 0);
        }

        /// <summary>rolloutFor: the stored rollout when the row has one, else projected from the ball's path.</summary>
        public static double? Rollout(object putt)
        {
            if (J.Get(putt, "rollout") != null && double.TryParse(J.Str(putt, "rollout"), NumberStyles.Float, Inv, out double stored)) return stored;
            try
            {
                int[][] bp = PuttAssessment.Bp(putt); double[] hb = PuttAssessment.Hb(putt);
                if (bp.Length < 2 || hb == null) return null;
                // LabSessionView passes made only when the row says 0; lab rows carry no made, so the app projects without it.
                return AppPutt.ProjectRollout(AppPutt.AnalyzeRollout(bp, hb), J.Get(putt, "made") != null && J.Int(putt, "made") == 0 ? false : (bool?)null);
            }
            catch (Exception) { return null; }
        }

        /// <summary>The Ball overview's six numbers, worded as the app shows them.</summary>
        public sealed class Overview
        {
            public int Putts, Score;
            public string Percent = "0%", Accuracy = "0", Speed = "0", Total = "0.00", SpeedDiff = "0", PathDiff = "0";
            public string Grade => AppPutt.Grade(Score);
        }

        /// <summary>calculateNumbers. APP QUIRK: a miss counts as an entry angle of 0 in the path spread, as the app's does.</summary>
        public static Overview Of(List<object> putts)
        {
            var o = new Overview();
            if (putts == null || putts.Count == 0) return o;
            double acc = 0, spe = 0, totalGrade = 0, speedMin = double.PositiveInfinity, speedMax = double.NegativeInfinity, angleMin = 400, angleMax = -400;
            int made = 0, paceReads = 0, total = putts.Count;
            foreach (object p in putts)
            {
                bool m = Made(p);
                if (m) made++;
                double? rollout = Rollout(p);
                double? entry = EntryDegrees(p);
                // getPuttData: the perfect line from the tee (the app runs getTeeEntry's degrees through atan once more).
                double a = AppPutt.TeeEntry(J.Num(p, "distance"), J.Int(p, "tee", 2));
                double b = J.Num(p, "distance") * 12;
                double perfAng = b != 0 ? AppPutt.JsRound(Math.Atan(a / b) * (180 / Math.PI)) : 0;
                double es = 0, angDiff = 0;
                if (entry.HasValue) // the app's `if (entry)`: a stored hole entry, not the 0 of a miss
                {
                    angDiff = perfAng - entry.Value;
                    es = 10 - AppPutt.JsRound(Math.Abs(angDiff) / 10);
                }
                double ss = rollout == null ? 0 : AppPutt.SpeedScore(rollout, 0, entry.HasValue) / 5;
                totalGrade += AppPutt.PuttScore(angDiff, m, rollout, 0);
                spe += Math.Round(ss, 2, MidpointRounding.AwayFromZero);
                acc += es;
                if (rollout != null)
                {
                    paceReads++;
                    speedMin = Math.Min(speedMin, rollout.Value);
                    speedMax = Math.Max(speedMax, rollout.Value);
                }
                angleMin = Math.Min(angleMin, angDiff);
                angleMax = Math.Max(angleMax, angDiff);
            }
            o.Putts = total;
            o.Percent = (made > 0 ? (int)AppPutt.JsRound(100.0 * made / total) : 0) + "%";
            double speedAvg = spe > 0 ? Math.Round(spe / total, 2) : 0, accAvg = acc != 0 ? Math.Round(acc / total, 2) : 0;
            o.Speed = spe > 0 ? speedAvg.ToString("0.00", Inv) : "0";
            o.Accuracy = acc != 0 ? accAvg.ToString("0.00", Inv) : "0";
            o.Total = (speedAvg + accAvg).ToString("0.00", Inv);
            o.Score = (int)AppPutt.JsRound(totalGrade / total);
            o.SpeedDiff = paceReads > 1 ? (speedMax - speedMin).ToString("0.0", Inv) : "0";
            o.PathDiff = total > 1 ? Math.Abs(angleMax - angleMin).ToString("0.##", Inv) : "0";
            return o;
        }

        /// <summary>The help card for each Ball overview tile (getHelpDescription).</summary>
        public static readonly Dictionary<string, string> Help = new Dictionary<string, string>
        {
            { "Session grade", "Your putt grade is based on your entry angle to the hole and pace." },
            { "Accuracy score", "Calculated based on how center each putt enters the hole. Score is out of 10." },
            { "Speed score", "Pace vs dying at the hole center, from the projected rollout. Score is out of 10." },
            { "Total score", "The combination of your accuracy score and speed score. Out of 20 possible points." },
            { "Speed differential", "Your rollout spread in inches between putts. Lower is better. Shows pace consistency." },
            { "Path differential", "The dispersion of your entry angle to the hole. Lower is better. Shows ball path consistency." },
        };
    }

    /// <summary>computePuttDisplay: what a putt's card and its ball-path card show.</summary>
    public sealed class LabPuttRead
    {
        public bool Made, Tracked;
        public int[][] Bp;
        public double[] Hb;
        public double? Rollout;
        public string Pace, Grade, Break, Result;
        public double AngDiff, Score;
        public int Rounded;
        public Color GradeColour, ResultColour, PaceColour, Accent;
        public float PaceShare;

        static readonly Color Orange = new Color32(0xFF, 0x95, 0x00, 0xFF);

        public static LabPuttRead Of(object putt)
        {
            var d = new LabPuttRead { Made = LabBall.Made(putt) };
            d.Bp = PuttAssessment.Bp(putt);
            d.Hb = PuttAssessment.Hb(putt);
            d.Tracked = d.Bp.Length >= 2 && d.Hb != null;
            d.Rollout = LabBall.Rollout(putt);
            d.Pace = AppPutt.FmtRollout(d.Rollout) ?? "no read";
            double hole = d.Tracked ? AppPutt.HoleEntry(d.Bp, d.Hb, out _) : 0;
            int tee = J.Int(putt, "tee", 2);
            d.AngDiff = hole - AppPutt.TeeEntry(J.Num(putt, "distance"), tee);
            d.Score = AppPutt.PuttScore(d.AngDiff, d.Made, d.Rollout, 0);
            d.Grade = AppPutt.Grade(d.Score);
            d.Rounded = (int)AppPutt.JsRound(d.Score);
            d.Break = tee == 1 ? "Left to right" : tee == 3 ? "Right to left" : "Straight";
            if (d.Made)
            {
                string zone = AppPutt.EntryDescription(d.AngDiff);
                d.Result = zone.Substring(0, 1) + zone.Substring(1).ToLowerInvariant();
                d.ResultColour = Ui.Bright;
            }
            else
            {
                d.Result = "Missed " + (d.Tracked ? AppPutt.MissResult(d.Bp, d.Hb) : "LEFT").ToLowerInvariant();
                d.ResultColour = Ui.Red;
            }
            d.GradeColour = d.Rounded < 50 ? Ui.Red : d.Rounded < 70 ? Color.white : Ui.Bright;
            bool ideal = d.Rollout != null && d.Rollout.Value >= -2 && d.Rollout.Value <= 12;
            d.PaceColour = ideal ? Ui.Green : d.Rollout != null && d.Rollout.Value < -2 ? Ui.Blue : Orange;
            d.PaceShare = d.Rollout == null ? 0.05f : Mathf.Clamp((float)(d.Rollout.Value + 15) / 45f, 0.05f, 1f);
            d.Accent = d.Made ? Ui.Green : Ui.Red;
            return d;
        }

        /// <summary>"Left center · 23.0°" for a make, "Missed left" for a miss.</summary>
        public string ResultLine => Result + (Made ? " · " + Math.Abs(AngDiff).ToString("0.0", CultureInfo.InvariantCulture) + "°" : "");

        /// <summary>The ball-path card's entry line: "Dead center" within 5 degrees, else "8.0° right of center".</summary>
        public string EntryLine => Math.Abs(AngDiff) < 5 ? "Dead center" : Math.Abs(AngDiff).ToString("0.0", CultureInfo.InvariantCulture) + "° " + (AngDiff > 0 ? "right" : "left") + " of center";
    }
}
