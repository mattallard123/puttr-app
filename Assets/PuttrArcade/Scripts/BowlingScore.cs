// Ten-pin scoring for one Player. Plain C#, no Unity types.
using System.Collections.Generic;

namespace PuttrArcade
{
    public sealed class BowlingScore
    {
        public struct Frame
        {
            /// <summary>Up to three marks: "X", "/", "-" or a digit.</summary>
            public string[] Marks;
            /// <summary>Running total, or null while the frame still depends on balls to come.</summary>
            public int? Total;
        }

        readonly List<int> _rolls = new List<int>();

        public IReadOnlyList<int> Rolls => _rolls;
        /// <summary>The frame the next ball belongs to, 0 to 9.</summary>
        public int FrameIndex { get; private set; }
        /// <summary>0 for the first ball of the frame, 1 for the second, 2 for the bonus ball in the tenth.</summary>
        public int BallInFrame { get; private set; }
        /// <summary>Pins standing for the next ball.</summary>
        public int PinsStanding { get; private set; } = 10;
        public bool Complete { get; private set; }

        public int Total
        {
            get
            {
                int total = 0;
                foreach (Frame f in Frames()) if (f.Total.HasValue) total = f.Total.Value;
                return total;
            }
        }

        public void Add(int pins)
        {
            if (Complete) return;
            if (pins < 0) pins = 0;
            if (pins > PinsStanding) pins = PinsStanding;
            _rolls.Add(pins);
            Advance(pins);
        }

        void Advance(int pins)
        {
            if (FrameIndex < 9)
            {
                if (BallInFrame == 0 && pins < 10)
                {
                    BallInFrame = 1;
                    PinsStanding = 10 - pins;
                }
                else
                {
                    FrameIndex++;
                    BallInFrame = 0;
                    PinsStanding = 10;
                }
                return;
            }

            // Tenth frame: a strike or spare earns a third ball.
            int n = _rolls.Count;
            int first = _rolls[n - 1 - BallInFrame];
            if (BallInFrame == 0)
            {
                BallInFrame = 1;
                PinsStanding = pins == 10 ? 10 : 10 - pins;
            }
            else if (BallInFrame == 1)
            {
                bool strike = first == 10;
                bool spare = !strike && first + pins == 10;
                if (!strike && !spare) { Complete = true; return; }
                BallInFrame = 2;
                PinsStanding = strike && pins < 10 ? 10 - pins : 10;
            }
            else Complete = true;
        }

        public Frame[] Frames()
        {
            var frames = new Frame[10];
            int i = 0, running = 0;
            int n = _rolls.Count;
            for (int f = 0; f < 10; f++)
            {
                var marks = new List<string>();
                int? total = null;
                if (f < 9)
                {
                    if (i < n && _rolls[i] == 10)
                    {
                        marks.Add("X");
                        if (i + 2 < n) total = running += 10 + _rolls[i + 1] + _rolls[i + 2];
                        i += 1;
                    }
                    else if (i < n)
                    {
                        marks.Add(Mark(_rolls[i]));
                        if (i + 1 < n)
                        {
                            int sum = _rolls[i] + _rolls[i + 1];
                            if (sum == 10)
                            {
                                marks.Add("/");
                                if (i + 2 < n) total = running += 10 + _rolls[i + 2];
                            }
                            else
                            {
                                marks.Add(Mark(_rolls[i + 1]));
                                total = running += sum;
                            }
                        }
                        i += 2;
                    }
                }
                else
                {
                    int sum = 0;
                    for (int k = 0; k < 3 && i + k < n; k++)
                    {
                        int r = _rolls[i + k];
                        sum += r;
                        int prev = k > 0 ? _rolls[i + k - 1] : -1;
                        bool freshRack = k == 0 || prev == 10 || (k == 2 && _rolls[i] != 10 && _rolls[i] + _rolls[i + 1] == 10);
                        if (r == 10 && freshRack) marks.Add("X");
                        else if (!freshRack && prev + r == 10) marks.Add("/");
                        else marks.Add(Mark(r));
                    }
                    if (Complete) total = running += sum;
                }
                frames[f] = new Frame { Marks = marks.ToArray(), Total = total };
            }
            return frames;
        }

        static string Mark(int pins) => pins == 0 ? "-" : pins.ToString();
    }
}
