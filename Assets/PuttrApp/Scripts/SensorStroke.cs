// The Vertex stroke behind each putt of a sensor game (puttr-app games/sensor/SensorFunctions.js, VertexScoring.js).
//
// Pairing: the Vertex burst lands right after impact and the ball path a second or three later, when the ball stops.
// The latest burst is stashed with the time it came; the next putt takes it if it is fresh (20 s) and nobody has
// taken it, so a stroke is never used twice and a practice swing minutes ago never lands on a putt. Test putts (the
// keyboard, the simulator, -app2Fixture) with no fresh burst get a made-up stroke, as Green Testing does in the app,
// so every game can be played without the sensor. Made-up strokes are never saved with the putt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PuttrApp
{
    /// <summary>One stroke's metrics, in the units the games use (backstroke length kept in mm, shown in inches).</summary>
    public sealed class SensorStroke
    {
        public double Tempo, Rhythm, Face, Lie, LengthMm, Speed, Total, Back, Forward, BackRot, FwdRot;
        /// <summary>Made up for a test putt: never saved.</summary>
        public bool Sim;
        /// <summary>The six packets as the app saves them in sensorData ("[[..20 bytes..],...]"), or null.</summary>
        public string Raw;

        public double LengthIn => SensorMath.MmToIn(LengthMm);

        /// <summary>A metric by the app's key (tempo, rhythm, faceChange, lieAngle, backStrokeLength...).</summary>
        public double this[string key] => key switch
        {
            "tempo" => Tempo, "rhythm" => Rhythm, "faceChange" => Face, "lieAngle" => Lie, "backStrokeLength" => LengthMm,
            "clubHeadSpeed" => Speed, "totalStrokeTime" => Total, "backStrokeTime" => Back, "forwardStrokeTime" => Forward,
            "backStrokeRotation" => BackRot, "forwardStrokeRotation" => FwdRot, _ => 0,
        };

        /// <summary>stashStroke: the sensor's backstroke length comes signed (away from the target): the games use its size.</summary>
        public static SensorStroke From(VertexPutt v) => new SensorStroke
        {
            Tempo = v.Tempo, Rhythm = v.Rhythm, Face = v.FaceChange, Lie = v.Lie, LengthMm = Math.Abs(v.BackLength), Speed = v.Speed,
            Total = v.BackTime + v.ForwardTime, Back = v.BackTime, Forward = v.ForwardTime, BackRot = v.BackRotation, FwdRot = v.ForwardRotation, Raw = v.Raw,
        };

        /// <summary>makeSimStroke: around the ideals, one in four deliberately poor, so every scoring path gets played.</summary>
        public static SensorStroke Make(System.Random dice)
        {
            double R(double lo, double hi) => lo + dice.NextDouble() * (hi - lo);
            bool ugly = dice.NextDouble() < 0.25;
            double tempo = ugly ? R(52, 92) : R(63, 77);
            double rhythm = ugly ? R(1.2, 3.2) : R(1.7, 2.4);
            double face = ugly ? R(-4, 4) : R(-1.2, 1.2);
            double lie = ugly ? R(-4, 4) : R(-1.5, 1.5);
            // Real strokes on the mat run roughly 6 to 16 in (150 to 420 mm), median about 11.5 in.
            double length = Math.Round(ugly ? R(130, 480) : R(230, 350));
            double total = 60 / tempo, back = total * rhythm / (rhythm + 1);
            double backRot = R(-3, -0.5);
            double R1(double x) => Math.Round(x, 1);
            double R2(double x) => Math.Round(x, 2);
            return new SensorStroke
            {
                Tempo = R1(tempo), Rhythm = R2(rhythm), Face = R1(face), Lie = R1(lie), LengthMm = length, Speed = R2(R(1.1, 1.9)),
                Total = R2(total), Back = R2(back), Forward = R2(total - back), BackRot = R1(backRot), FwdRot = R1(face - backRot), Sim = true,
            };
        }
    }

    /// <summary>The latest burst from the sensor, waiting for its putt.</summary>
    public static class SensorStrokes
    {
        public const float FreshSeconds = 20f;
        static SensorStroke _stash;
        static float _at;
        static bool _taken, _listening;
        /// <summary>Made-up strokes for test putts (seeded, so a fixture run plays the same way every time).</summary>
        public static System.Random Dice = new System.Random(7);

        public static void Listen()
        {
            if (_listening) return;
            _listening = true;
            Vertex.Putt += p => Stash(SensorStroke.From(p));
        }

        public static void Stash(SensorStroke s) { _stash = s; _at = Time.realtimeSinceStartup; _taken = false; }
        public static void Clear() { _stash = null; _taken = true; }

        /// <summary>resolveStroke: the fresh unused burst, else a made-up one for a test putt, else null (the putt does not count).</summary>
        public static SensorStroke Take(bool testing)
        {
            if (_stash != null && !_taken && Time.realtimeSinceStartup - _at < FreshSeconds) { _taken = true; return _stash; }
            return testing ? SensorStroke.Make(Dice) : null;
        }
    }

    public static class SensorMath
    {
        /// <summary>mmToIn: one decimal, what the games show and judge.</summary>
        public static double MmToIn(double mm) => Math.Round(Math.Abs(mm) / 25.4 * 10) / 10;
        public static double BpmToSeconds(double bpm) => bpm > 0 ? 60 / bpm : 0;
        public static double Mean(IEnumerable<double> xs) { var l = xs.ToList(); return l.Count > 0 ? l.Average() : 0; }

        /// <summary>getAccuracyScore: PDF(value) / PDF(mean) x 100, rounded.</summary>
        public static int Bell(double value, double mean, double sd) => sd == 0 ? 100 : (int)Math.Round(Math.Exp(-0.5 * Math.Pow((value - mean) / sd, 2)) * 100, MidpointRounding.AwayFromZero);

        /// <summary>strokeQuality: face change (0, 1.4), rhythm (2.0, 0.7), lie (0, 2.0), and purity, their average.</summary>
        public static (int face, int rhythm, int lie, int purity) Quality(SensorStroke s)
        {
            int face = Bell(s.Face, 0, 1.4), lie = Bell(s.Lie, 0, 2.0);
            bool hasRhythm = s.Rhythm > 0;
            int rhythm = hasRhythm ? Bell(s.Rhythm, 2.0, 0.7) : 0;
            double sum = face + lie + (hasRhythm ? rhythm : 0);
            int purity = (int)Math.Round(sum / (hasRhythm ? 3 : 2), MidpointRounding.AwayFromZero);
            return (face, rhythm, lie, purity);
        }

        public static string Deg(double v) => (v > 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + "°";
        public static string In(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + " in";
        public static string Ratio(double v, int digits = 1) => v.ToString(digits == 2 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + ":1";
        public static string N(double v, string f = "0.0") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>The six packets as JSON arrays of bytes, as the app saves sensorData.</summary>
        public static string RawJson(byte[][] packets)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < packets.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[');
                byte[] p = packets[i] ?? new byte[0];
                for (int j = 0; j < p.Length; j++) { if (j > 0) sb.Append(','); sb.Append(p[j]); }
                sb.Append(']');
            }
            return sb.Append(']').ToString();
        }
    }
}
