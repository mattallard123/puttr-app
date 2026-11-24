// The games' commentary, as puttr-app's common/SoundFunctionsEnglish.js (and its Caddie and Metal twins) and the calls the
// games make into them: the announcer starts the game ("Let's get it on"), calls each putt in two recordings ("eight
// feet ... left tee"), reacts to a make or a miss, names whose turn it is ("Player two") and who won. The recordings are
// the app's own (android/app/src/main/res/raw), three voices of them:
//
//   english   the announcer: most games, On Fire, Beer Pong
//   scottish  the caddie: Practice, Reducer, Full Workout, Assessment, a custom Straight-style game
//   metal     Metal Play's
//
// Nothing is bundled: a clip is fetched from the games bucket the first time it is wanted (Warm fetches a voice ahead
// of a game), kept in the phone's cache directory under a name that carries its content hash, and played from there.
// Offline, and in fixture mode, only what is already on the phone is said; anything else is silence. A recording
// dropped into Resources/PuttrArcade/Audio/Voice under its own name plays from there first.
//
// One voice at a time: lines are queued and never talk over each other. The call for the next putt replaces a call
// still waiting, and a call still waiting when the putt is played is dropped (it is about a putt that has gone). A
// line that took too long to fetch is about a moment that has passed, so it is kept for next time, not played.
//
// The Commentary switch (GameIntro.js renderCommentary: microphone, "Commentary", on by default) is remembered for
// each Player on this device. As in the app, a game that never offered the switch (Horse's calls and letters, the next
// spot in Oh-One, Full Workout, Assessment, Compare) still speaks the lines that tell the Player what to do.
//
// The table below is the recordings' content hashes (md5, first 8): the n-th hash of a row is {voice}_{kind}_{n}.mp3,
// distance runs 3 to 11 feet ({voice}_distance_{n}_feet.mp3) and tee is left, center, right ({voice}_tee_{word}.mp3).
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class GameVoice : MonoBehaviour
    {
        /// <summary>Where the recordings are served from: the games bucket, beside the Tracks booth's.</summary>
        public const string Base = "https://storage.googleapis.com/images.puttr.co/games/commentary/";
        public const string English = "english", Scottish = "scottish", Metal = "metal";

        // voice | kind | hashes
        static readonly string[] Table =
        {
            "english|start|e2b6f119,e0431930,f522dfb6,cfbca829,4c480846,0112264b,a8b48371,7335a529",
            "english|made|a1ce03d6,d7e562c7,cf44ab6f,9913a74c,a05eb061,c1bae822,88a48351,2dde4af5,8d94577b,fdcbb20f,45bf646a,3eb72649,37998555,5a8248dd,b22e4cac,6dcc71f2,e17ac1cf,dd7c2530,49be5f02",
            "english|miss|d60388b2,3e406f66,f5afe994,f868d7a5,54fcdcca,4288d85d,94dae64c,e22e1c86,13471a6d,14c24f63,fa3c4532,24040b86,567cc522,a5d99703,6391291d,1981a143,59ca8f6e,9e260ae9,fc088c9a,d92cc577",
            "english|player|e394246e,aa221022,a4f6fd85,3bd8d231",
            "english|game_over_player|fdc6e02f,80b3e54b,35a5ab2d,b3907931",
            "english|streak|d70ee1d7,475ac133,fbfe5d61,07dce45e,a3db63ff,21d14606,7bc52d38",
            "english|distance|54aac70f,c1784d20,99ed3ff9,28317493,287100c7,e90b459d,9f116e77,8a109f0d,a5fd1cd3",
            "english|tee|8f582221,8305e54a,dfe9110c",
            "english|call_your_shot|96c9151c",
            "english|horse_gets_letter|01504943",
            "english|horse_avoids_letter|f3c7ce75",
            "scottish|start|a12c3bae,ad7cb51c,6d7cb1ce,2648c0b0,0c32150a",
            "scottish|made|ab780c72,080fad55,20af95d4,49cc2246,630de510,de5885a4,495e9eae,8086f225,cedab4a1,c8449b5f,409383a1,cf1391e8,d84a3e98,6f25acc7,d3bc1d0b,c9cebb98,fd0d0c37,5c1422dd,e0f6d8e0,b79e301c,75eb4854,368b49fa,a67870ed,12a27dec,e57b5365,ce5491c7,3d12eee2,c96e1c53,40b34178,eb97b199,e87f4ad1,cb53cc29,325da1a7,3af6937a,a1922976,cdef68c6,5edc1c17,2316b4c7,4c58fede,5c2b41d7",
            "scottish|miss|e037dad7,4801b1a9,b4049acf,f8528996,b229c8f9,ea0ab360,173f694b,fbda46e9,1747be2f,ed89b9c4,e27d6df2,37dd7a8f,522ba856,6ea5338d,23f05b70,39e88301,6fd89541,83b75dec,c0bd45b2,6caeef99,4753faa3,ecc524d1,9b82350d,6d811a9b,7cf6906e,bc6ad442,60f8606e,3d764d58,8d41f975,10e63efb,0993a5fd,027219d0,bccf1592,f1e44319,06d934b3,714356bd,251bf5b0,5b6c2c31,186579b9,6220699f",
            "scottish|player|9249bf6a,00c5b2e1,4bcfe3d6,131c7b6a",
            "scottish|game_over_player|fbb27803,a3e1510a,28159e6b,ae6e61b5",
            "scottish|streak|9806d514,4311c90d,35b787aa,a4434e69,b11b13ae,4d871b29,60c47914,7a5bdfd4",
            "scottish|distance|142c66ff,1184bcac,d5faf950,fe736c80,34f57ec4,62024a87,92e2eba4,e05ca3e4,176587c1",
            "scottish|tee|b31d99dd,52cd0031,36b963b9",
            "metal|start|be0a15eb,22b63cee,343aedac,cf7c196f,4eb090ef,9bf01f73",
            "metal|made|8f232b6c,edd51c3d,72539b80,35b13096,6f9b5e13,33585f40,c7ff29ae,325a12d6,a0b26e98,b0ca140b,9d1edf2b,63fa2bae,c087dc03,80b56424,eabcdddd,b42e67a1,67949e4a,830d4e7c,04e041db,9b08bd9c,be3881b5,3a98efba,a9676123,e96ce13b,00f444cb,53cbb7d3",
            "metal|miss|fe91c7d0,94805945,eca6909a,397691d2,c215659f,d2ccfffe,e5a691c7,00079018,9a08d739,e7a4b3de,00cfc622,2ab4ca96,ca987d9b,4ccbb106,d266c258,05a1883f,4e08c753,cf4ce995,1365dbd3,f9bf5f23,1ba876db,8f15f4aa,c3ae8782,80309f75,7707f52e,046ca84d,45535466,79b0e08e,aaef473d",
            "metal|player|d5b77753,b95b9bd4,6389df6c,71ce7478",
            "metal|game_over_player|93f87d1a,8bfe2e1d,211ffd24,cc91c194",
            "metal|streak|00fd7e24,320d4cee,d6fd7c12,b7b93751,fc4c8f0a,536ea878,2e4b0bb6",
            "metal|distance|613c4461,2bd39da1,e4e0edc5,69f3181a,22cca95c,541fce4a,0b98a5a5,053507ce,82b6912e",
            "metal|tee|2e6b8c62,da3cfeee,24822c9a",
        };

        static readonly string[] TeeWords = { "left", "center", "right" };
        static readonly Dictionary<string, string> Versions = new Dictionary<string, string>();   // clip name (no .mp3) -> content hash
        static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();           // "voice|kind" -> takes
        static readonly Dictionary<string, List<string>> ByVoice = new Dictionary<string, List<string>>();

        static GameVoice()
        {
            foreach (string row in Table)
            {
                string[] part = row.Split('|');
                string voice = part[0], kind = part[1];
                string[] hashes = part[2].Split(',');
                Counts[voice + "|" + kind] = hashes.Length;
                if (!ByVoice.TryGetValue(voice, out List<string> all)) ByVoice[voice] = all = new List<string>();
                // Horse's lines are single recordings with no take number (english_call_your_shot).
                bool single = kind == "call_your_shot" || kind.StartsWith("horse_");
                for (int n = 0; n < hashes.Length; n++)
                {
                    string name = kind == "distance" ? voice + "_distance_" + (n + 3) + "_feet"
                        : kind == "tee" ? voice + "_tee_" + TeeWords[n]
                        : single ? voice + "_" + kind
                        : voice + "_" + kind + "_" + (n + 1);
                    Versions[name] = hashes[n];
                    all.Add(name);
                }
            }
        }

        // ── The table, as plain functions (GameVoiceTests) ──

        /// <summary>A recording the app has, by name (english_made_3, scottish_tee_left).</summary>
        public static bool Known(string name) => !string.IsNullOrEmpty(name) && Versions.ContainsKey(name);

        /// <summary>How many takes a voice has of a kind ("made", "miss", "start", "streak", "player"...); 0 for none.</summary>
        public static int Takes(string voice, string kind) => Counts.TryGetValue(voice + "|" + kind, out int n) ? n : 0;

        /// <summary>Where a recording is served from: its hash rides along, so no cache on the way hands back an old take.</summary>
        public static string Url(string name) => Base + name + ".mp3?v=" + Version(name);

        /// <summary>A recording's name in the phone's cache: its hash, then its file name.</summary>
        public static string CacheName(string name) => Version(name) + "_" + name + ".mp3";

        static string Version(string name) => Versions.TryGetValue(name, out string v) ? v : "0";

        /// <summary>Every recording of a voice, for Warm.</summary>
        public static IReadOnlyList<string> All(string voice) => ByVoice.TryGetValue(voice, out List<string> all) ? all : new List<string>();

        /// <summary>The distance call, held to the mat's 3 to 11 feet (playTeeSound's dArr[distance - 3]).</summary>
        public static string DistanceClip(string voice, int feet) => voice + "_distance_" + Mathf.Clamp(feet, 3, 11) + "_feet";

        /// <summary>The tee call: 1 left, 2 center, 3 right; anything else is the center.</summary>
        public static string TeeClip(string voice, int tee) => voice + "_tee_" + TeeWords[tee >= 1 && tee <= 3 ? tee - 1 : 1];

        /// <summary>"Player two" for seat 1 (playPlayerSound's fileArr[player]); null past the fourth seat.</summary>
        public static string PlayerClip(string voice, int seat) => seat >= 0 && seat < Takes(voice, "player") ? voice + "_player_" + (seat + 1) : null;

        /// <summary>"Player two wins" for seat 1 (playGameOverSound); null for a tie or past the fourth seat.</summary>
        public static string WinnerClip(string voice, int seat) => seat >= 0 && seat < Takes(voice, "game_over_player") ? voice + "_game_over_player_" + (seat + 1) : null;

        /// <summary>One take of a kind at random, as the app's Math.floor(Math.random() * fileArr.length); null for none.</summary>
        public static string Pick(string voice, string kind, System.Func<int, int> roll)
        {
            int n = Takes(voice, kind);
            return n == 0 ? null : voice + "_" + kind + "_" + (Mathf.Clamp(roll(n), 0, n - 1) + 1);
        }

        /// <summary>Lab.js spokenRollout: "3 inches past the hole", "dead center", "unreadable".</summary>
        public static string SpokenRollout(double? v)
        {
            if (v == null || double.IsNaN(v.Value) || double.IsInfinity(v.Value)) return "unreadable";
            if (System.Math.Abs(v.Value) < 0.5) return "dead center";
            double inches = AppPutt.JsRound(System.Math.Abs(v.Value));
            return inches.ToString("0", CultureInfo.InvariantCulture) + (inches == 1 ? " inch " : " inches ") + (v.Value > 0 ? "past the hole" : "short of the hole");
        }

        /// <summary>Fitting.js's call: "6 footer, Center tee".</summary>
        public static string Footer(int feet, int tee) => feet + " footer, " + PracticeSession.TeeName(tee) + " tee";

        // ── The switch ──

        const string Pref = "app2.game.commentary.";
        static string _prefFor;
        static bool _on = true;

        /// <summary>
        /// The Commentary switch (the app's props.game.commentary, on by default), for the Player signed in on this device.
        /// Turned off, what is being said stops and nothing waiting follows.
        /// </summary>
        public static bool On
        {
            get
            {
                string key = Pref + (string.IsNullOrEmpty(Auth.Uid) ? "guest" : Auth.Uid);
                if (_prefFor != key) { _prefFor = key; _on = PlayerPrefs.GetInt(key, 1) == 1; }
                return _on;
            }
            set
            {
                string key = Pref + (string.IsNullOrEmpty(Auth.Uid) ? "guest" : Auth.Uid);
                _prefFor = key;
                _on = value;
                PlayerPrefs.SetInt(key, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value) Hush();
            }
        }

        /// <summary>
        /// The game page's row (GameIntro.js renderCommentary): the microphone, "Commentary", and its switch. Remembered for
        /// the Player; flipping it in one game page sets it for every game.
        /// </summary>
        public static VisualElement SwitchRow()
        {
            VisualElement row = Rows.Row("Microphone", "Commentary", null, Rows.Switch(On, on => On = on));
            row.style.borderBottomWidth = 0;
            VisualElement card = Ui.Box("glass set-card", row);
            card.style.marginTop = 14;
            return card;
        }

        /// <summary>
        /// Whether a game's page offers the switch: every game with something the switch silences (its announcer, its
        /// distance and tee calls, an arcade game's caller). Not a game that says nothing (Dead Center, the sensor games),
        /// nor one whose every word tells the Player what to do and so is spoken anyway (Full Workout, Assessment, Compare).
        /// </summary>
        public static bool Offered(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (GameBridge.ArcadeNameFor(game) != null) return true;
            Style st = StyleFor(type);
            return !st.Silent && !(st.AlwaysCalls && (st.AlwaysReacts || st.Kind == "ASSESSMENT" || st.Kind == "FITTING"));
        }

        // ── What a game says ──
        //
        // always: the line is one the app says with the switch off too (see the top of the file).

        /// <summary>Recordings by name, in turn (GameAudio.Say: On Fire). A distance followed by a tee is a call.</summary>
        public static void Say(string[] names) => Say(names, false);

        public static void Say(string[] names, bool always)
        {
            if (names == null || names.Length == 0) return;
            var list = new List<string>();
            foreach (string n in names) if (!string.IsNullOrEmpty(n)) list.Add(n);
            if (list.Count == 0) return;
            bool call = list[0].Contains("_distance_");
            Enqueue(new Line { Names = list.ToArray(), Call = call }, always);
        }

        /// <summary>The game starts (playStartGameSound): one of the voice's openers.</summary>
        public static void Opener(string voice, bool always = false) => Say(new[] { Pick(voice, "start", Roll) }, always);

        /// <summary>The putt to play (playTeeSound): the distance, then the tee. Replaces a call still waiting.</summary>
        public static void Call(string voice, int feet, int tee, bool always = false) => Say(new[] { DistanceClip(voice, feet), TeeClip(voice, tee) }, always);

        /// <summary>The distance alone (playDistanceSound: High Striker).</summary>
        public static void Distance(string voice, int feet, bool always = false) => Say(new[] { DistanceClip(voice, feet) }, always);

        /// <summary>Whose turn it is (playPlayerSound), seat 0 to 3.</summary>
        public static void Player(string voice, int seat, bool always = false) => Say(new[] { PlayerClip(voice, seat) }, always);

        /// <summary>A make (playMakeSound), a miss (playMissSound), a run of makes (playStreakSound).</summary>
        public static void Made(string voice, bool always = false) => Say(new[] { Pick(voice, "made", Roll) }, always);
        public static void Missed(string voice, bool always = false) => Say(new[] { Pick(voice, "miss", Roll) }, always);
        public static void Streak(string voice, bool always = false) => Say(new[] { Pick(voice, "streak", Roll) }, always);

        /// <summary>Who won (playGameOverSound). A tie says nothing.</summary>
        public static void Winner(string voice, int seat, bool always = false) => Say(new[] { WinnerClip(voice, seat) }, always);

        /// <summary>
        /// A sentence (the app's speakText: The Lab's read, Compare's calls) in the announcer's voice, spoken through the
        /// coach's text-to-speech service and kept on the phone once fetched. Not said if it is not ready in a few seconds.
        /// </summary>
        public static void Sentence(string text, bool always = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Enqueue(new Line { Text = text.Trim(), Call = false }, always);
        }

        /// <summary>The putt has been played: a call or sentence still waiting is about it, and is dropped.</summary>
        public static void ShotPlayed()
        {
            if (_one == null) return;
            var keep = new Queue<Line>();
            foreach (Line l in _one._queue) if (!l.Call) keep.Enqueue(l);
            _one._queue.Clear();
            foreach (Line l in keep) _one._queue.Enqueue(l);
        }

        /// <summary>Silence, now: what is playing stops and nothing waiting is said (a game left part way).</summary>
        public static void Hush()
        {
            if (_one == null) return;
            _one._stop++;
            _one._queue.Clear();
            foreach (AudioSource s in _one._sources) if (s != null) s.Stop();
            _one._speaking = false;
        }

        /// <summary>A finished game is left: its last word (who won) is let finish, but no call for a putt that will not come.</summary>
        public static void Leave() => ShotPlayed();

        /// <summary>A line is being said, or waits.</summary>
        public static bool Speaking => _one != null && (_one._speaking || _one._queue.Count > 0);

        // ── A rule game's commentary (puttr-app components/games/*.js) ──

        /// <summary>How a game speaks: its voice, and which of the app's calls it makes.</summary>
        public sealed class Style
        {
            public string Voice = English;
            public bool Silent;
            public bool CallEveryTurn;   // the putt is called on every turn (the putt-list games), else when the spot changes
            public bool Streaks;         // after more than five scoring putts in a row, a streak line in place of a make
            public bool Winner;          // "Player two wins" at the end of a game for more than one
            public bool Buzzer = true;   // a back-door make sounds the buzzer (else it is a miss)
            public bool AlwaysCalls;     // the game speaks its calls with commentary off (it never offered the switch)
            public bool AlwaysReacts;
            public string Kind = "";     // a game with lines of its own: HORSE, LAB, FITTING, HIGHSTRIKER, ASSESSMENT
        }

        /// <summary>The app's commentary for each rule game's gameType. A game the app gave no voice is Silent.</summary>
        public static Style StyleFor(string gameType)
        {
            switch ((gameType ?? "").ToUpperInvariant())
            {
                // TourJuice.js, SpeedDemon.js, CenterCut.js: start, a call every turn, streak / make / buzzer / miss, the winner.
                case "TOURJUICE": case "SPEEDDEMON": case "CENTERCUT": return new Style { CallEveryTurn = true, Streaks = true, Winner = true };
                case "METAL": return new Style { Voice = Metal, CallEveryTurn = true, Streaks = true, Winner = true, Buzzer = false };
                // Reducer.js: the caddie; its start and calls are not behind the switch.
                case "REDUCER": return new Style { Voice = Scottish, CallEveryTurn = true, Streaks = true, Winner = true, Buzzer = false, AlwaysCalls = true };
                case "MATCH": return new Style { CallEveryTurn = true, Winner = true };
                case "BEERPONG": return new Style { CallEveryTurn = true, Winner = true };
                // Doors.js, Gears.js, Groove.js, HighSide.js, Leapfrog.js, AroundTheClock.js: make or miss, the call when the
                // spot changes. Switchback has no commentary (Matt, 2026-11-23: "missed it" over a putt that was holed
                // off the line): its own sounds say what the putt did.
                case "SWITCHBACK": return new Style { Silent = true };
                case "DOORS": case "GEARS": case "GROOVE": case "HIGHSIDE": case "LEAPFROG": return new Style { Buzzer = false };
                case "CLOCK": case "AROUNDTHECLOCK": return new Style { Silent = true };   // no commentary (Matt, 2026-11-23)
                // OhOne.js: the next spot is always called.
                case "OHONE": return new Style { CallEveryTurn = true, AlwaysCalls = true };
                case "HORSE": return new Style { Kind = "HORSE" };
                case "LAB": return new Style { Kind = "LAB" };
                case "HIGHSTRIKER": return new Style { Kind = "HIGHSTRIKER" };
                // FullWorkout.js and Assessment.js: the caddie, never behind a switch.
                case "FULLWORKOUT": return new Style { Voice = Scottish, CallEveryTurn = true, AlwaysCalls = true, AlwaysReacts = true, Buzzer = false };
                case "ASSESSMENT": return new Style { Voice = Scottish, Kind = "ASSESSMENT", CallEveryTurn = true, AlwaysCalls = true };
                case "FITTING": return new Style { Kind = "FITTING", AlwaysCalls = true };
                // A custom putt-list game: Standard.js and Streak.js speak with the caddie.
                case "STANDARD": case "STREAK": return new Style { Voice = Scottish, CallEveryTurn = true, Streaks = true };
                case "OPEN": return new Style { Voice = Scottish };
                default: return new Style { Silent = true };
            }
        }

        static int _calledFeet, _calledTee;

        /// <summary>A rule game has started (GameBridge.PlayRule): the start line and the first call.</summary>
        public static void RuleStart(RuleGame g)
        {
            Style st = StyleFor(g.GameType);
            if (st.Silent) return;
            Warm(st.Voice);
            _calledFeet = g.Feet; _calledTee = g.Tee;
            switch (st.Kind)
            {
                case "HORSE": Say(new[] { "english_call_your_shot" }, true); return;
                case "LAB": return;
                case "FITTING": Sentence(Footer(g.Feet, g.Tee), true); return;
                case "HIGHSTRIKER": Distance(st.Voice, g.Feet); return;
                case "ASSESSMENT": Call(st.Voice, g.Feet, g.Tee, true); return;
            }
            if (g.GameType.ToUpperInvariant() != "FULLWORKOUT") Opener(st.Voice, st.AlwaysCalls);
            if (g.Players > 1 && st.CallEveryTurn) Player(st.Voice, g.P, st.AlwaysCalls);
            Call(st.Voice, g.Feet, g.Tee, st.AlwaysCalls);
        }

        /// <summary>
        /// The spot changed without a putt (Horse's caller picking it on the court): it is called once the caller has
        /// settled on it, as the app does when the spot is confirmed.
        /// </summary>
        public static void RuleChanged(RuleGame g)
        {
            if (StyleFor(g.GameType).Kind != "HORSE" || g.Over || g.Progress != "CALL THE SHOT") return;
            if (g.Feet == _calledFeet && g.Tee == _calledTee) return;
            One._spotAsked = Time.realtimeSinceStartup;
            One._spotGame = g;
        }

        /// <summary>
        /// A rule game took a putt (GameBridge.OnPutt, after RuleGame.Putt): the reaction, then whose turn and where from.
        /// before is the seat that putted. True when the back-door buzzer should sound (the arcade plays it).
        /// </summary>
        public static bool RuleShot(RuleGame g, Shot s, int before)
        {
            Style st = StyleFor(g.GameType);
            if (st.Silent || s == null) return false;
            bool buzzer = false;
            bool turned = g.P != before;
            switch (st.Kind)
            {
                case "HORSE":
                    if (s.Title == "SHOT SET") Made(English);
                    else if (s.Title == "BACK DOOR") buzzer = On;
                    else if (s.Title == "MATCHED") Say(new[] { "english_horse_avoids_letter" }, true);
                    else if (s.Title == "LETTER") Say(new[] { "english_horse_gets_letter" }, true);
                    if (g.Over) { Winner(English, g.Winner, true); return buzzer; }
                    if (g.Progress == "CALL THE SHOT") { if (turned) Player(English, g.P, true); Say(new[] { "english_call_your_shot" }, true); _calledFeet = _calledTee = 0; }
                    else if (s.Title == "SHOT SET") Player(English, g.P, true);
                    return buzzer;
                case "LAB":
                    if (s.V != null)
                    {
                        string pace = SpokenRollout(s.V.Rollout);
                        if (s.V.Made)
                        {
                            // Lab.js: the entry against the tee's own line, in whole degrees off its center.
                            double perfect = AppPutt.JsRound(System.Math.Atan(AppPutt.TeeEntry(s.Feet, s.Tee) / (s.Feet * 12.0)) * (180 / System.Math.PI));
                            double off = System.Math.Abs(perfect - s.V.Entry);
                            Sentence("pace was " + pace + " and entry was " + off.ToString("0.#", CultureInfo.InvariantCulture) + " degrees off center");
                        }
                        else Sentence("pace was " + pace + " and you missed the putt");
                    }
                    return false;
                case "FITTING":
                    if (g.Over) return false;
                    if (turned) Sentence("putter " + (g.P + 1), true);
                    Sentence(Footer(g.Feet, g.Tee), true);
                    return false;
                case "HIGHSTRIKER":
                    if (!g.Over) Distance(st.Voice, g.Feet);
                    return false;
                case "ASSESSMENT":
                    if (!g.Over) Call(st.Voice, g.Feet, g.Tee, true);
                    return false;
            }

            bool backDoor = s.Title != null && s.Title.StartsWith("BACK DOOR");
            if (backDoor && st.Buzzer) buzzer = st.AlwaysReacts || On;
            else if ((s.Holed ?? s.Good) && !backDoor) { if (st.Streaks && MakesInARow(g, s.Player) > 5) Streak(st.Voice, st.AlwaysReacts); else Made(st.Voice, st.AlwaysReacts); }
            else Missed(st.Voice, st.AlwaysReacts);

            if (g.Over)
            {
                if (st.Winner && g.Players > 1) Winner(st.Voice, g.Winner);
                return buzzer;
            }
            // Whose turn (with more than one playing), then the putt: every turn in the putt-list games, else only when the
            // spot has changed (the app's "cp === 0": the others putt the same spot).
            bool moved = g.Feet != _calledFeet || g.Tee != _calledTee;
            if (g.Players > 1 && turned) Player(st.Voice, g.P, st.AlwaysCalls);
            if (st.CallEveryTurn || moved) Call(st.Voice, g.Feet, g.Tee, st.AlwaysCalls);
            _calledFeet = g.Feet; _calledTee = g.Tee;
            return buzzer;
        }

        /// <summary>How many scoring putts in a row a seat has, ending with its last (the games' calculateStreak).</summary>
        public static int MakesInARow(RuleGame g, int seat)
        {
            int n = 0;
            for (int i = g.Shots.Count - 1; i >= 0; i--)
            {
                if (g.Shots[i].Player != seat) continue;
                if (!g.Shots[i].Good) break;
                n++;
            }
            return n;
        }

        // ── A putt-list round, Practice and the arcade games ──

        static int _run;

        /// <summary>A catalogue putt-list game starts (Standard.js, Streak.js, CenterCut.js): the start line and the first call.</summary>
        public static void RoundStart(string gameType, int feet, int tee)
        {
            Style st = StyleFor(gameType);
            _run = 0;
            if (st.Silent) return;
            Warm(st.Voice);
            Opener(st.Voice);
            Call(st.Voice, feet, tee);
        }

        /// <summary>A putt in that round: streak / make, the buzzer for a back-door make, or a miss; then the next call.
        /// next is null when the round is over. True when the buzzer should sound.</summary>
        public static bool RoundPutt(string gameType, AppPutt.Verdict v, int feet, int tee, TournamentMath.Dealt? next)
        {
            Style st = StyleFor(gameType);
            if (st.Silent || v == null) return false;
            bool back = v.Made && Scoring.BackDoor(v, feet, tee);
            bool buzzer = false;
            if (v.Made && !back) { _run++; if (st.Streaks && _run > 5) Streak(st.Voice); else Made(st.Voice); }
            else { _run = 0; if (back) buzzer = On; else Missed(st.Voice); }
            if (next.HasValue) Call(st.Voice, next.Value.Feet, next.Value.Tee);
            return buzzer;
        }

        /// <summary>Practice (Open.js, the caddie): its opener when the session starts.</summary>
        public static void PracticeStart() { Warm(Scottish); Opener(Scottish); }

        /// <summary>A Practice putt (Open.js processBallPath): a make through the front, the buzzer for the back door, or a
        /// miss. True when the buzzer should sound.</summary>
        public static bool PracticePutt(AppPutt.Verdict v, int feet, int tee)
        {
            if (v == null) return false;
            if (!v.Made) { Missed(Scottish); return false; }
            if (Scoring.BackDoor(v, feet, tee)) return On;
            Made(Scottish);
            return false;
        }

        // ── The speaker ──

        sealed class Line { public string[] Names; public string Text; public bool Call; public float Asked; public int Stop; }

        const float Late = 4f;          // a recording that took longer than this to arrive is about a moment that has passed
        const float LateSentence = 6f;  // the same for a sentence, which the service has to speak first
        const float Overlap = 0.15f;    // playTeeSound starts the tee 0.15 s before the distance ends
        // The voice the Tracks booth calls shots in (TracksVoice), for the sentences the app spoke with the phone's own voice.
        const string SentenceVoiceId = "cX1hqOWxczku078qEPZi";

        static GameVoice _one;
        readonly AudioSource[] _sources = new AudioSource[2];
        int _next;
        readonly Queue<Line> _queue = new Queue<Line>();
        readonly HashSet<string> _inflight = new HashSet<string>();
        readonly HashSet<string> _missing = new HashSet<string>();   // not on the bucket this session: not asked for again
        readonly HashSet<string> _warmed = new HashSet<string>();
        readonly Queue<string> _toWarm = new Queue<string>();
        bool _speaking, _warming;
        int _stop;
        float _spotAsked = -1f;
        RuleGame _spotGame;

        static GameVoice One
        {
            get
            {
                if (_one != null) return _one;
                var go = new GameObject("Game voice");
                DontDestroyOnLoad(go);
                _one = go.AddComponent<GameVoice>();
                for (int i = 0; i < _one._sources.Length; i++)
                {
                    AudioSource s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f;
                    _one._sources[i] = s;
                }
                return _one;
            }
        }

        static int Roll(int n) => Random.Range(0, n);

        static void Enqueue(Line line, bool always)
        {
            if (!always && !On) return;
            if (line.Names != null) foreach (string n in line.Names) if (n == null) return;
            GameVoice one = One;
            line.Asked = Time.realtimeSinceStartup;
            line.Stop = one._stop;
            if (line.Call)
            {
                // A newer call replaces one still waiting: the spot it named has changed.
                var keep = new Queue<Line>();
                foreach (Line l in one._queue) if (!l.Call) keep.Enqueue(l);
                one._queue.Clear();
                foreach (Line l in keep) one._queue.Enqueue(l);
            }
            // Never a backlog: the oldest chatter goes first when too much is waiting.
            while (one._queue.Count >= 4) one._queue.Dequeue();
            one._queue.Enqueue(line);
            if (!one._speaking) one.StartCoroutine(one.Run());
        }

        void Update()
        {
            // Horse: the caller's spot is called once it has stood for a moment.
            if (_spotAsked > 0f && Time.realtimeSinceStartup - _spotAsked > 0.9f)
            {
                _spotAsked = -1f;
                RuleGame g = _spotGame;
                _spotGame = null;
                if (g != null && !g.Over && g.Progress == "CALL THE SHOT" && (g.Feet != _calledFeet || g.Tee != _calledTee))
                {
                    _calledFeet = g.Feet; _calledTee = g.Tee;
                    Call(English, g.Feet, g.Tee, true);
                }
            }
        }

        IEnumerator Run()
        {
            _speaking = true;
            int stop = _stop;
            while (_queue.Count > 0 && stop == _stop)
            {
                Line line = _queue.Dequeue();
                if (line.Stop != _stop) continue;
                if (line.Text != null) { yield return SayText(line); continue; }
                for (int i = 0; i < line.Names.Length && stop == _stop; i++)
                {
                    AudioClip clip = null;
                    bool owned = false;
                    yield return Load(line.Names[i], (c, o) => { clip = c; owned = o; });
                    if (clip == null || stop != _stop) { if (clip != null && owned) Destroy(clip); break; }
                    if (i == 0 && Time.realtimeSinceStartup - line.Asked > Late) { if (owned) Destroy(clip); break; }
                    bool more = i < line.Names.Length - 1;
                    yield return Play(clip, owned, more && line.Call ? Overlap : 0f, stop);
                }
            }
            if (stop == _stop) _speaking = false;
        }

        IEnumerator SayText(Line line)
        {
            AudioClip clip = null;
            yield return CoachVoice.Clip(SentenceVoiceId, line.Text, Fixtures.On, c => clip = c);
            if (clip == null) yield break;
            if (line.Stop != _stop || Time.realtimeSinceStartup - line.Asked > LateSentence) { Destroy(clip); yield break; }
            Perf.Note("game voice: " + line.Text);
            yield return Play(clip, true, 0f, _stop);
        }

        // One clip, through the next of the two sources (so a tee can start under the end of its distance). Waits until
        // the clip is done, less early seconds; a clip this class made is freed once it has finished.
        IEnumerator Play(AudioClip clip, bool owned, float early, int stop)
        {
            AudioSource source = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            source.clip = clip;
            source.volume = 1f;
            source.Play();
            Perf.Wake(clip.length + 0.5f);
            // Timed by the clock, not by isPlaying, so a clip that stalls never leaves the queue waiting.
            float until = Time.realtimeSinceStartup + Mathf.Max(0.1f, clip.length - early);
            while (stop == _stop && Time.realtimeSinceStartup < until) yield return null;
            if (owned) StartCoroutine(FreeAfter(source, clip));
        }

        // The clip may still be sounding (the overlap): it is freed once its source has let go of it.
        IEnumerator FreeAfter(AudioSource source, AudioClip clip)
        {
            float cap = Time.realtimeSinceStartup + 2f;
            while (source != null && source.clip == clip && source.isPlaying && Time.realtimeSinceStartup < cap) yield return null;
            if (source != null && source.clip == clip) { source.Stop(); source.clip = null; }
            Destroy(clip);
        }

        // ── Fetching and the cache ──

        static string _dir;
        // The cache directory, as the Tracks booth's: the clips can always be fetched again, so they are not backed up.
        static string CacheDir => _dir ?? (_dir = Path.Combine(Application.temporaryCachePath, "game-voice"));
        static string CachePath(string name) => Path.Combine(CacheDir, CacheName(name));

        /// <summary>The recording is on the phone already.</summary>
        public static bool Cached(string name) => File.Exists(CachePath(name));

        /// <summary>
        /// Fetches a voice's recordings ahead of a game so they play at once (the app loaded them all with loadAllSounds),
        /// one at a time, when the commentary is on. Nothing in fixture mode.
        /// </summary>
        public static void Warm(string voice)
        {
            if (Fixtures.On || !On || string.IsNullOrEmpty(voice)) return;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            GameVoice one = One;
            if (!one._warmed.Add(voice)) return;
            foreach (string name in All(voice)) if (!Bundled(name)) one._toWarm.Enqueue(name);
            if (!one._warming) one.StartCoroutine(one.WarmAll());
#endif
        }

        IEnumerator WarmAll()
        {
            _warming = true;
            while (_toWarm.Count > 0) yield return Download(_toWarm.Dequeue());
            _warming = false;
        }

        static bool Bundled(string name) => Resources.Load<AudioClip>("PuttrArcade/Audio/Voice/" + name) != null;

        // A recording into the cache, once even when asked twice at the same time. Fails silently (offline, not on the
        // bucket): the file is simply not there afterwards. Never in fixture mode, which stays off the network.
        IEnumerator Download(string name)
        {
            while (_inflight.Contains(name)) yield return null;
            string path = CachePath(name);
            if (Fixtures.On || !Known(name) || _missing.Contains(name) || File.Exists(path)) yield break;
            _inflight.Add(name);
            using (UnityWebRequest req = UnityWebRequest.Get(Url(name)))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.responseCode == 404 || req.responseCode == 403) _missing.Add(name);
                byte[] data = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
                // Under 500 bytes is an error page, not a recording. Written beside its name and then moved, so a
                // download cut short is never taken for a clip.
                if (data != null && data.Length >= 500)
                {
                    try
                    {
                        Directory.CreateDirectory(CacheDir);
                        File.WriteAllBytes(path + ".part", data);
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(path + ".part", path);
                    }
                    catch (System.Exception) { /* a full or read-only disk: the clip is fetched again next time */ }
                }
            }
            _inflight.Remove(name);
        }

        // done(clip, owned): owned is true for a clip made here from a file, which is freed after playing; a bundled one
        // belongs to Resources.
        IEnumerator Load(string name, System.Action<AudioClip, bool> done)
        {
            AudioClip bundled = Resources.Load<AudioClip>("PuttrArcade/Audio/Voice/" + name);
            if (bundled != null) { done(bundled, false); yield break; }
#if UNITY_WEBGL && !UNITY_EDITOR
            string address = Fixtures.On || !Known(name) ? null : Url(name);   // no files on the web: straight from the bucket
            string path = null;
#else
            yield return Download(name);
            string path = CachePath(name);
            string address = File.Exists(path) ? "file://" + path : null;
#endif
            if (address == null) { done(null, false); yield break; }
            using (UnityWebRequest load = UnityWebRequestMultimedia.GetAudioClip(address, AudioType.MPEG))
            {
                yield return load.SendWebRequest();
                AudioClip clip = load.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(load) : null;
                if (clip != null && clip.length <= 0f) { Destroy(clip); clip = null; }
                // A cached copy that will not play is thrown away, so the next time it is fetched afresh.
                if (clip == null && path != null) { try { File.Delete(path); } catch (System.Exception) { /* it stays */ } }
                done(clip, clip != null);
            }
        }
    }
}
