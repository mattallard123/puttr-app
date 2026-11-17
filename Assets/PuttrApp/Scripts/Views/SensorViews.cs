// What the eight Vertex sensor games hand their 3D worlds (PuttrArcade Scenes/<Name>Scene.cs, SensorStage.cs).
//
// Every game writes the last putt's stroke (SensorStage.StrokeOf reads it back):
//   has (a stroke yet), sim, tempo (BPM), rhythm (back:through), face (face change °, + closed), lie (°), lenIn
//   (backstroke inches), speed (m/s), back / fwd (seconds), backRot / fwdRot (°), count (putts scored)
// and then its own state, listed above each game below. Per-putt values describe the putt just played.
using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public static class SensorView
    {
        public static void Stroke(Dictionary<string, object> d, SensorGame g)
        {
            d["count"] = g.Shots.Count;
            SensorStroke s = g.Stroke;
            d["has"] = s != null;
            if (s == null) return;
            d["sim"] = s.Sim;
            d["tempo"] = (float)s.Tempo; d["rhythm"] = (float)s.Rhythm; d["face"] = (float)s.Face; d["lie"] = (float)s.Lie;
            d["lenIn"] = (float)s.LengthIn; d["speed"] = (float)s.Speed; d["back"] = (float)s.Back; d["fwd"] = (float)s.Forward;
            d["backRot"] = (float)s.BackRot; d["fwdRot"] = (float)s.FwdRot;
        }
    }

    // target (BPM), pocket (±BPM), zero (BPM off for no credit), streak, mult, maxMult, inPocket, close (0..1),
    // tempos (every stroke's BPM), pockets (in the pocket, each stroke)
    public sealed partial class PendulumGame
    {
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            d["target"] = Target; d["pocket"] = (float)Pocket; d["zero"] = (float)Zero;
            d["streak"] = _streak; d["mult"] = Mult; d["maxMult"] = MaxMult;
            d["inPocket"] = Last != null && Last.Good; d["close"] = (float)LastClose;
            d["tempos"] = _tempos.Select(x => (float)x).ToArray();
            d["pockets"] = _tempos.Select(t => System.Math.Abs(t - Target) <= Pocket).ToArray();
        }
    }

    // target (ratio), clickBpm (the clicks for the next putt), gaps (seconds: back, through, rest for the next putt),
    // streak, waltzAt, inTime, waltz (this putt doubled), close, ratios (every stroke), inTimes (each stroke)
    public sealed partial class WaltzGame
    {
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            d["target"] = (float)Target;
            int bpm = ClickBpm;
            float stroke = 60f / bpm, forward = stroke / ((float)Target + 1f);
            d["clickBpm"] = bpm;
            d["gaps"] = new[] { stroke - forward, forward, System.Math.Max((float)MinRest, stroke * 1.5f) };
            d["streak"] = _streak; d["waltzAt"] = WaltzAt;
            d["inTime"] = Last != null && Last.Good; d["waltz"] = Last != null && Last.Title.StartsWith("WALTZ"); d["close"] = (float)LastClose;
            d["ratios"] = _ratios.Select(x => (float)x).ToArray();
            d["inTimes"] = _ratios.Select(x => System.Math.Abs(x - Target) <= InTime).ToArray();
            d["inTimeBand"] = (float)InTime; d["zeroAt"] = (float)ZeroAt;
        }
    }

    // qFace, qRhythm, qLie, purity (0..100, the last stroke), strike (all three 75+), strikeMin, purities (every stroke)
    public sealed partial class PureGame
    {
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            d["qFace"] = Q.face; d["qRhythm"] = Q.rhythm; d["qLie"] = Q.lie; d["purity"] = Q.purity;
            d["strike"] = LastStrike; d["strikeMin"] = StrikeMin;
            d["purities"] = _purity.ToArray();
        }
    }

    // signed (the signature is recorded), labels[4], sig[4] and mine[4] (tempo BPM, length in, rhythm, face °),
    // std[4] (the base tolerance in those units), match[4] (0..100), matchAll, copy (90+), copyMin, tol (share of the
    // base tolerance the last copy was judged at), tolNext, copies, total (14), matches (every copy)
    public sealed partial class CarbonCopyGame
    {
        static float Shown(int i, double x) => i == 1 ? (float)SensorMath.MmToIn(x) : (float)x;
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            d["signed"] = Signature != null;
            d["labels"] = Metrics.Select(m => m.label).ToArray();
            d["std"] = Metrics.Select((m, i) => i == 1 ? (float)(m.std / 25.4) : (float)m.std).ToArray();
            if (Signature != null) d["sig"] = Signature.Select((x, i) => Shown(i, x)).ToArray();
            if (Stroke != null) d["mine"] = Metrics.Select((m, i) => Shown(i, Stroke[m.key])).ToArray();
            d["match"] = Match.ToArray(); d["matchAll"] = LastMatch; d["copy"] = Last != null && Last.Good && _copies > 0 && LastMatch >= CopyMin;
            d["copyMin"] = CopyMin;
            d["tol"] = (float)TolFor(System.Math.Max(0, _copies - 1)); d["tolNext"] = (float)TolFor(System.Math.Min(_copies, Copies - 1));
            d["copies"] = _copies; d["total"] = Copies;
            d["matches"] = _matches.ToArray();
            d["justSigned"] = Signature != null && _copies == 0;
        }
    }

    // zero (true zero mode), hasCenter, center (°), calibrating, calib (putts so far), calibNeed, gate (± ° for the next
    // putt), lastGate (the gate the last putt was judged at), off (last face minus the center), inside, cleared,
    // balls, startBalls, level, inLevel, perLevel, survived, faces (every stroke's face change)
    public sealed partial class SquareUpGame
    {
        readonly List<float> _faces = new List<float>();
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            if (Shots.Count == 0) _faces.Clear();
            if (Stroke != null && _faces.Count < Shots.Count) _faces.Add((float)Stroke.Face);
            d["zero"] = Zero; d["hasCenter"] = Center != null; d["center"] = (float)(Center ?? 0);
            d["calibrating"] = Center == null; d["calib"] = Calib.Count; d["calibNeed"] = Calibrate;
            d["gate"] = (float)Gate; d["lastGate"] = (float)LastGate; d["off"] = (float)LastOff;
            d["inside"] = LastInside; d["cleared"] = Cleared;
            d["balls"] = Balls; d["startBalls"] = StartBalls; d["level"] = Level; d["inLevel"] = InLevel; d["perLevel"] = PerLevel;
            d["survived"] = Survived;
            d["faces"] = _faces.ToArray();
        }
    }

    // lo, hi, center (the band in inches for the next putt), short (the next putt is a short one), lastLo, lastHi,
    // lengthOk, hasLock, tempoLock (BPM), tempoBand (±BPM), tempoOk (-1 still locking, 0 drifted, 1 held), pace
    // (0..10), hasRollout, rollout (in, + past), elastic, rulerMin, rulerMax, lengths (every backstroke, in),
    // feetHist (each putt's distance)
    public sealed partial class StretchGame
    {
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            (double lo, double hi, double center) = BandFor(Feet);
            d["lo"] = (float)lo; d["hi"] = (float)hi; d["center"] = (float)center; d["short"] = Shots.Count % 2 == 0;
            d["lastLo"] = (float)LastLo; d["lastHi"] = (float)LastHi; d["lengthOk"] = LastLengthOk;
            d["hasLock"] = Lock != null; d["tempoLock"] = (float)(Lock ?? 0); d["tempoBand"] = (float)TempoBand; d["tempoOk"] = LastTempoOk;
            d["pace"] = LastPace; d["hasRollout"] = !double.IsNaN(LastRollout); d["rollout"] = double.IsNaN(LastRollout) ? 0f : (float)LastRollout;
            d["elastic"] = LastElastic; d["rulerMin"] = (float)RulerMin; d["rulerMax"] = (float)RulerMax;
            d["lengths"] = _lengths.ToArray();
            d["feetHist"] = Shots.Select(s => s.Feet).ToArray();
        }
        readonly List<float> _lengths = new List<float>();
    }

    // calibrating, calib (putts so far), calibNeed, labels[6], ratios[6] (each spoke's distance from your average over
    // the band's allowance: 1 is on the ring, the last stroke; empty before the first scored stroke), band (now, 1 =
    // 100%), bandBefore (the band the last stroke was judged at), tightest, combo, breakRun, endAfter, lockdowns, lock
    public sealed partial class LockdownGame
    {
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            d["calibrating"] = Centers == null; d["calib"] = Calib.Count; d["calibNeed"] = Calibrate;
            d["labels"] = Spokes.Select(s => s.label).ToArray();
            d["ratios"] = Ratios == null ? new float[0] : Ratios.Select(x => (float)x).ToArray();
            d["band"] = (float)Band; d["bandBefore"] = (float)BandBefore; d["tightest"] = (float)Tightest;
            d["combo"] = Combo; d["breakRun"] = BreakRun; d["endAfter"] = EndAfter; d["lockdowns"] = Lockdowns; d["lock"] = LastLock;
        }
    }

    // bossHp, bossMax, playerHp, playerMax, stage (now), lastStage (when the last putt was struck), dmg, taken, crit,
    // twitch, collapse, combo, purity (last stroke), won (1 beaten, -1 out of HP, 0 still fighting), window (± face °)
    public sealed partial class TheYipsGame
    {
        public override void Describe(Dictionary<string, object> d)
        {
            SensorView.Stroke(d, this);
            d["bossHp"] = BossHp; d["bossMax"] = BossMax; d["playerHp"] = PlayerHp; d["playerMax"] = PlayerMax;
            d["stage"] = Stage; d["lastStage"] = LastStage; d["dmg"] = LastDmg; d["taken"] = LastTaken;
            d["crit"] = LastCrit; d["twitch"] = LastTwitch; d["collapse"] = LastCollapse; d["combo"] = Combo; d["purity"] = LastPurity;
            d["won"] = BossHp <= 0 ? 1 : PlayerHp <= 0 ? -1 : 0;
            d["window"] = (float)StrikeFace[Stage - 1];
        }
    }
}
