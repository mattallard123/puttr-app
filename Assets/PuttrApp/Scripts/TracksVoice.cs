// The Tracks booth, as puttr-app's components/games/tracks/audio.js. Two recorded commentators (an American
// man, "usa", and a Scottish woman, "scot") trade the call so a round never sounds the same twice; the American also
// calls every shot ("Seven feet, center tee."). The clips were rendered once with ElevenLabs
// (puttr-api/scripts/generate-tracks-audio.js) and are served from the games bucket; nothing is bundled. A clip is
// downloaded the first time it is wanted (Preload fetches them ahead) and kept in the phone's cache directory under a
// name that carries its content hash, so a replaced recording never plays from a stale copy.
//
// The booth's rules are the app's: one voice at a time. A shot call is information, so it waits for the line that is
// playing and then goes first. Hole scores and round calls ("big" lines) wait their turn, one at most; shot chatter
// that arrives while the booth is speaking is dropped. After a good or a bad moment the other voice reacts half the time.
//
// puttr-app-2 plays the v5 recut (2026-11-19): more takes and eleven_v4 audio tags (gasps, laughs, sighs) from
// puttr-api/scripts/tracks-commentary-lines-v5.js, rendered by generate-tracks-audio-v5.js into games/tracks/audio/v5/
// (the legacy app keeps the set one folder up). That script prints these tables (tracks-audio-v5/tables.cs.txt).
//
// The table below is audioManifest.json boiled down to what is needed to build a URL: the takes of every category for
// each voice, and each file's content hash. When recordings are added or replaced, bring the table over again.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrApp
{
    public sealed class TracksVoice : MonoBehaviour
    {
        /// <summary>audioManifest.json "base".</summary>
        public const string Base = "https://storage.googleapis.com/images.puttr.co/games/tracks/audio/v5/";
        public static readonly string[] Voices = { "usa", "scot" };

        const float Late = 4f;        // a clip that took longer than this to arrive is about a moment that has passed: it is kept for next time, not played
        const float Ducked = 0.5f;    // the course's bed under a voice

        // category | usa hashes | scot hashes: take n of a category is vo_{voice}_{category}_{n}.mp3.
        static readonly string[] Table =
        {
            "round_start|98fdb67b,b230c253,e79a22af,1ee32ce3,d079a578|39d92cf0,f553863f,18565bad,8cb52de6,caa802d8",
            "round_under|fdf37633,d9367fdd,ceeae9a0|bd1e825d,d976ce1a,4e375a6d",
            "round_even|1d2f2670,db910402|219518a9,7c50de94",
            "round_over|1d3ea55a,7340a408,4aff7b93|aab676f7,03d06808,eb6b5d31",
            "tee_fairway|d9477ccb,210d716e,055ca2ae,cd411275,ea6ddeb2|0e073f5c,c8f0cfc9,c732a6c9,4d89e926,a89335a5",
            "tee_rough|28afb67a,d49a08f5,58de9f84,27a93386|79c7e3f2,4ef02384,8235917a,7583100e",
            "tee_sand|ec6ac5ee,bf3c29e3,792d72b8,44b24aa4|7555c3a6,3e5c8469,9751affa,56a6dda8",
            "tee_water|f08d7833,fd846007,d9ab7a5e,8b4d26fa|1b838a65,66a801fd,d687d20b,866038e4",
            "tee_trees|2d68089e,243e2448,b0bce986|4317d744,2b1b954c,c2625bfc",
            "shot_fairway|64a169c3,10a50889,2c736f95|9a4481eb,f37c3614,5b7e52ba",
            "shot_rough|5da77d60,e3437fc2,96c24989|ae02e599,a0d8b638,be5bf6f3",
            "approach_tight|c18061d8,1a4daaf3,161bdba1,bd9e4cb4,f97120fd|308b10a5,10f88425,66fe2cbf,8a001ec0,1e75da38",
            "approach_on|6e4a8537,8fae7e42,f3b652c9,e26e83e7|480625f2,26305ddd,4e565f6c,b27b074e",
            "approach_miss|d422035b,575b86bb,5aa70f3a,d14359c6|af24fc71,d9f18db4,ea8402da,f652aa30",
            "approach_sand|23ec0b6f,aa5fd4c9,52aac7df|70af08d0,2516a54a,7e182da9",
            "approach_water|0997c868,095cae61,8b1130a5|e396c9b0,1f082551,54aad4e4",
            "chip_tight|d7efbfa1,bf0cb0fb,d03a627d|007e265b,9d803143,391c345e",
            "chip_on|bf3cc9dc,f7967ea2,f407e78f|7ea9094b,bdade0d3,e869f575",
            "putt_made_long|64cc81f7,d87efe2d,4369cc21,d0a104fd,19426468|b0a34f48,eb0e7962,9ac8c203,29bba19a,4a2f3e8d",
            "putt_made|b687b15c,47976f69,b0447b21,307a9729,91913007|30bb00d4,8564d16e,b3c0ebb9,00ef998e,d7844acc",
            "putt_miss_short|59bf4fdd,10f2130e,f8a20544,e4916373|fba7d489,2e9987b6,96f9634f,82c53d87",
            "putt_miss_long|8247b4dd,0403a8d5,74a9d5e4,51202970|6a846688,f43fdf21,5dbf4c55,d79c5659",
            "putt_miss_line|08b562b5,4c79ebcb,440707ad,4ecd2e54|85ec2e83,9d1f0269,7176febf,010a7c19",
            "putt_lip|fe5ecda4,ff680e50,d46b324c,e0180a12|d6f607eb,49021902,d1287e36,c1a9b1af",
            "lag_good|92f0caf4,55c135f5,f99da24b|d8a5517e,7140a418,eb041108",
            "gimme|442f4fc3,612df0d3,711be61f|02f7bd38,8597d05c,a6d0ddc7",
            "score_good|1d0c764d,32d3937d,853e8441,d9a40db4,9727a5b3,ba78e32a|1ae33408,7741d771,7742187a,9a40f882,994ad5f7,cc649bb4",
            "score_bad|45ef773a,131189e6,9bcfd0b1,4aa813f7,1159762a|d503cd3f,1d5c9902,cf67bf5b,3d690d9b,70fe50bd",
            "score_pickup|95771bcb,fefe601a,b8d57d3b|7eb44310,8486bfb3,eb08d774",
            "react_good|866e3922,90cf636c,5829eb7e,46a08bf8,a161ed65,6beec74a|c159c37e,14154deb,bcf0a6bc,59c6aefe,8c026d4c,b7adf3f6",
            "react_bad|19a652e7,bba4280f,ee19d0ca,20a18bcc,00298df8|46eae7da,8817a5d1,ca14aad0,f247907f,36fe2e11"
        };

        // {feet}_{tee} = hash: the 27 shot calls, vo_usa_call_{feet}_{tee}.mp3, 3 to 11 ft from tee 1 (left), 2 (center) or 3 (right).
        const string CallTable =
            "3_1=0d14ae31;3_2=0a441c6b;3_3=15e1dec5;4_1=f8b04b48;4_2=104d743c;4_3=efcaf7d5;5_1=fb9c686d;5_2=2e3d41d9;5_3=d0bd7d59;6_1=3d2c1dcb;6_2=cc53f16a;6_3=2ec2efeb;7_1=1ec908d8;7_2=79751b95;7_3=e92f5b39;8_1=9f9606b6;8_2=9bdb5c84;8_3=3db65c59;9_1=ed8d3cff;9_2=8f6c73e5;9_3=d2bd9aaa;10_1=3134e2ba;10_2=214dca27;10_3=2a4dea20;11_1=012e5fdf;11_2=5917fca7;11_3=d82ef71c";

        // audio.js GOOD, BAD and BIG.
        static readonly HashSet<string> Good = new HashSet<string> { "tee_fairway", "approach_tight", "approach_on", "chip_tight", "putt_made_long", "putt_made", "lag_good", "score_good" };
        static readonly HashSet<string> Bad = new HashSet<string> { "tee_sand", "tee_water", "tee_trees", "approach_sand", "approach_water", "putt_miss_short", "putt_miss_long", "putt_lip", "score_bad", "score_pickup" };
        static readonly HashSet<string> Big = new HashSet<string> { "round_start", "round_under", "round_even", "round_over", "score_good", "score_bad", "score_pickup", "putt_made_long" };

        static readonly List<string> Names = new List<string>();
        static readonly Dictionary<string, int[]> TakeCounts = new Dictionary<string, int[]>();          // category -> takes for usa, scot
        static readonly Dictionary<string, string> Versions = new Dictionary<string, string>();          // file -> content hash
        static readonly Dictionary<string, List<string>> Recent = new Dictionary<string, List<string>>();   // "category:voice" -> the last file heard, the one before

        static TracksVoice()
        {
            foreach (string row in Table)
            {
                string[] part = row.Split('|');
                var counts = new int[Voices.Length];
                for (int v = 0; v < Voices.Length; v++)
                {
                    string[] hashes = part[v + 1].Split(',');
                    counts[v] = hashes.Length;
                    for (int n = 0; n < hashes.Length; n++) Versions["vo_" + Voices[v] + "_" + part[0] + "_" + (n + 1) + ".mp3"] = hashes[n];
                }
                Names.Add(part[0]);
                TakeCounts[part[0]] = counts;
            }
            foreach (string call in CallTable.Split(';'))
            {
                string[] kv = call.Split('=');
                Versions["vo_usa_call_" + kv[0] + ".mp3"] = kv[1];
            }
        }

        // ── The switches ──

        const string CommentaryPref = "app2.tracks.commentary";
        static bool? _commentaryOn;

        /// <summary>The Commentary switch (the app's props.game.commentary, on by default). Unlike the app it is remembered between launches.</summary>
        public static bool Commentary
        {
            get { if (_commentaryOn == null) _commentaryOn = PlayerPrefs.GetInt(CommentaryPref, 1) == 1; return _commentaryOn.Value; }
            set
            {
                _commentaryOn = value;
                PlayerPrefs.SetInt(CommentaryPref, value ? 1 : 0);
                if (!value && _one != null) _one._pending = null;   // as setCommentaryEnabled: the line that is playing finishes, nothing follows it
            }
        }


        // ── The table, as plain functions (TracksVoiceTests) ──

        /// <summary>Every category the booth has lines for, in the manifest's order.</summary>
        public static IReadOnlyList<string> Categories => Names;

        public static bool IsCategory(string category) => !string.IsNullOrEmpty(category) && TakeCounts.ContainsKey(category);

        /// <summary>How many takes a voice ("usa" or "scot") has of a category; 0 if there are none.</summary>
        public static int Takes(string category, string voice)
        {
            int v = System.Array.IndexOf(Voices, voice);
            return v >= 0 && IsCategory(category) ? TakeCounts[category][v] : 0;
        }

        /// <summary>The file of a line: take 1 of tee_fairway in the American voice is vo_usa_tee_fairway_1.mp3. Null if there is no such take.</summary>
        public static string LineFile(string category, string voice, int take) => take >= 1 && take <= Takes(category, voice) ? "vo_" + voice + "_" + category + "_" + take + ".mp3" : null;

        /// <summary>
        /// The file of a shot call. Feet are held to the 3 to 11 the mat allows; the tee is numbered as everywhere else
        /// in this app and as the recordings are (1 left, 2 center, 3 right), and anything else is the center tee, as callShot.
        /// </summary>
        public static string CallFile(int feet, int tee) => "vo_usa_call_" + Mathf.Clamp(feet, 3, 11) + "_" + (tee >= 1 && tee <= 3 ? tee : 2) + ".mp3";

        /// <summary>
        /// Where a clip is served from. The objects have a year's max-age and the address does not change when a clip is
        /// replaced, so every request carries the clip's hash: no cache between here and the bucket can hand back the old one.
        /// </summary>
        public static string Url(string file) => Base + file + "?v=" + Version(file);

        /// <summary>A clip's name in the cache: its hash, then its file name.</summary>
        public static string CacheName(string file) => Version(file) + "_" + file;

        static string Version(string file) => Versions.TryGetValue(file, out string v) ? v : "0";

        /// <summary>A hole score or a round call: it waits for the booth to finish, where shot chatter is dropped.</summary>
        public static bool IsBig(string category) => Big.Contains(category);

        /// <summary>What the other voice may add after a line of this category: "react_good", "react_bad" or null.</summary>
        public static string Reaction(string category) => Good.Contains(category) ? "react_good" : Bad.Contains(category) ? "react_bad" : null;

        public static string Other(string voice) => voice == Voices[0] ? Voices[1] : Voices[0];

        /// <summary>
        /// Who takes the next line (audio.js chooseVoice): seven times in ten the commentator who did not speak last,
        /// otherwise either. lean is a number from 0 to 1 and roll(n) a whole number under n, both random in play.
        /// </summary>
        public static string ChooseVoice(string last, float lean, System.Func<int, int> roll) => lean < 0.7f && last != null ? Other(last) : Voices[roll(Voices.Length)];

        /// <summary>
        /// One take of a category for a voice (audio.js pick): at random among those not in the last two heard for that
        /// category and voice. Where the app fell back to any take once all had been heard (which, with two takes, could
        /// play the last one again), this leaves out the one heard last, so a clip is never said twice running.
        /// </summary>
        public static string Pick(string category, string voice, System.Func<int, int> roll)
        {
            int n = Takes(category, voice);
            if (n == 0) return null;
            string key = category + ":" + voice;
            if (!Recent.TryGetValue(key, out List<string> heard)) Recent[key] = heard = new List<string>();
            var fresh = new List<string>();
            for (int t = 1; t <= n; t++) { string f = LineFile(category, voice, t); if (!heard.Contains(f)) fresh.Add(f); }
            if (fresh.Count == 0) for (int t = 1; t <= n; t++) { string f = LineFile(category, voice, t); if (n == 1 || f != heard[0]) fresh.Add(f); }
            string file = fresh[Mathf.Clamp(roll(fresh.Count), 0, fresh.Count - 1)];
            string before = heard.Count > 0 ? heard[0] : null;
            heard.Clear();
            heard.Add(file);
            if (before != null && before != file) heard.Add(before);
            return file;
        }

        /// <summary>
        /// What Preload fetches: the 27 shot calls, then (with lines) every take of every category for the voice, or for
        /// both voices when it is not one of them.
        /// </summary>
        public static List<string> PreloadList(string voice, bool lines)
        {
            var files = new List<string>();
            for (int ft = 3; ft <= 11; ft++) for (int tee = 1; tee <= 3; tee++) files.Add(CallFile(ft, tee));
            if (!lines) return files;
            bool one = System.Array.IndexOf(Voices, voice) >= 0;
            foreach (string category in Names)
                foreach (string v in Voices)
                {
                    if (one && v != voice) continue;
                    for (int t = 1; t <= Takes(category, v); t++) files.Add(LineFile(category, v, t));
                }
            return files;
        }

        /// <summary>Forgets who spoke last and which takes were heard (a test starts from nothing).</summary>
        public static void Forget()
        {
            Recent.Clear();
            if (_one != null) { _one._lastVoice = null; _one._lastFile = null; }
        }

        // ── The booth ──

                static TracksVoice _one;
        AudioSource _source;
        TracksWorld[] _worlds = new TracksWorld[0];
        bool _speaking, _warming;
        string _callQueued, _pending, _lastVoice, _lastFile;
        readonly HashSet<string> _inflight = new HashSet<string>();   // files being downloaded, so a clip is never fetched twice at once
        readonly Queue<string> _toWarm = new Queue<string>();
        int _stop;     // goes up on every Stop: whatever was on its way then is dropped, not played over what follows
        int _line;     // goes up at a new hole: the line that is playing finishes, its reaction does not come
        float _duck = 1f;

        static TracksVoice One
        {
            get
            {
                if (_one != null) return _one;
                var go = new GameObject("Tracks voice");
                DontDestroyOnLoad(go);
                _one = go.AddComponent<TracksVoice>();
                _one._source = go.AddComponent<AudioSource>();
                _one._source.playOnAwake = false;
                _one._source.spatialBlend = 0f;
                return _one;
            }
        }

        static int Roll(int n) => Random.Range(0, n);

        /// <summary>
        /// "Seven feet, center tee." in the American voice, for the putt about to be played (tee 1 left, 2 center, 3 right).
        /// Said when the commentary is on. It is information, so it is never dropped: if the booth
        /// is speaking it waits and goes next, ahead of anything else waiting (a second call replaces a waiting one).
        /// </summary>
        public static void Call(int feet, int tee)
        {
            if (!Commentary) return;
            TracksVoice one = One;
            string file = CallFile(feet, tee);
            if (one._speaking) { one._callQueued = file; return; }
            one.StartCoroutine(one.PlayCall(file));
        }

        /// <summary>
        /// One line of a category from the booth (audio.js commentate), when the commentary is on; a category the booth
        /// has no lines for says nothing. If the booth is speaking, a big line (IsBig) waits as the one line pending and
        /// anything else is dropped. Every line but round_start follows a putt, so it also clears a shot call still
        /// waiting to be said: the shot they were about has been played (audio.js dropQueuedCall).
        /// </summary>
        public static void Event(string category)
        {
            if (!IsCategory(category)) return;
            if (category != "round_start") ShotPlayed();
            if (!Commentary) return;
            TracksVoice one = One;
            if (one._speaking) { if (IsBig(category)) one._pending = category; return; }
            one.StartCoroutine(one.Speak(category));
        }


        /// <summary>
        /// Fetches the clips ahead of a round so they play at once: the shot calls and, with the commentary on, the lines
        /// of one voice ("usa" or "scot") or of both (anything else). One at a time, as the app's prefetch; about 2 MB in
        /// all. Nothing in fixture mode.
        /// </summary>
        public static void Preload(string voice = null)
        {
            if (Fixtures.On || !Commentary) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            // The web build keeps no files: the browser's own cache holds a clip once it has played.
#else
            TracksVoice one = One;
            foreach (string file in PreloadList(voice, Commentary)) if (!one._toWarm.Contains(file)) one._toWarm.Enqueue(file);
            if (!one._warming) one.StartCoroutine(one.Warm());
#endif
        }

        /// <summary>Silence, now: what is playing stops and nothing waiting is said (a round ended, or the Player left). As audio.js resetBooth.</summary>
        public static void Stop()
        {
            if (_one == null) return;
            _one._stop++; _one._line++;
            _one._callQueued = null; _one._pending = null;
                        _one._speaking = false;
            _one._source.Stop();
        }

        /// <summary>
        /// A new hole (audio.js dropPendingLines): anything the booth still owed about the last one is late now, so the
        /// call for the new tee is not followed by an old reaction. The line that is playing finishes.
        /// </summary>
        public static void NewHole()
        {
            if (_one == null) return;
            _one._pending = null;
            _one._line++;
        }

        /// <summary>
        /// The putt is away (audio.js dropQueuedCall): a call for it that has not had its turn is stale
        /// and must not be said after the fact. Event does this by itself for every line that follows a putt.
        /// </summary>
        public static void ShotPlayed()
        {
            if (_one == null) return;
            _one._callQueued = null;
                    }

        /// <summary>A clip is playing or about to.</summary>
        public static bool Speaking => _one != null && _one._speaking;


        // After any clip: a waiting call first, then a waiting score line.
        void Drain()
        {
            if (_callQueued != null) { string file = _callQueued; _callQueued = null; StartCoroutine(PlayCall(file)); return; }
            if (_pending != null) { string category = _pending; _pending = null; StartCoroutine(Speak(category)); }
        }

        IEnumerator PlayCall(string file)
        {
            int stop = _stop;
            _speaking = true;
            yield return Play(file, stop);
            if (stop != _stop) yield break;   // Stop has cleared the booth, and someone else may hold it by now
            _speaking = false;
            Drain();
        }

        IEnumerator Speak(string category)
        {
            int stop = _stop, line = _line;
            string voice = ChooseVoice(_lastVoice, Random.value, Roll);
            string file = Pick(category, voice, Roll);
            if (file == null) yield break;
            if (file == _lastFile)
            {
                // A category with a single take, asked for twice running: the other commentator says theirs.
                string theirs = Pick(category, Other(voice), Roll);
                if (theirs != null && theirs != _lastFile) { voice = Other(voice); file = theirs; }
            }
            _lastVoice = voice;
            _speaking = true;
            yield return Play(file, stop);
            if (stop != _stop) yield break;
            // The other voice chimes in on the good and the bad moments, half the time.
            string tone = Reaction(category);
            if (tone != null && line == _line && Commentary && Random.value < 0.5f)
            {
                string other = Other(voice);
                string reaction = Pick(tone, other, Roll);
                if (reaction != null)
                {
                    yield return new WaitForSecondsRealtime(0.25f);
                    if (stop != _stop) yield break;
                    if (line == _line) { _lastVoice = other; yield return Play(reaction, stop); if (stop != _stop) yield break; }
                }
            }
            _speaking = false;
            Drain();
        }


        // One recorded clip, start to end.
        IEnumerator Play(string file, int stop)
        {
            float asked = Time.realtimeSinceStartup;
            AudioClip clip = null;
            yield return Load(file, c => clip = c);
            if (clip == null) yield break;
            if (stop != _stop || Time.realtimeSinceStartup - asked > Late) { Destroy(clip); yield break; }
            _lastFile = file;
            Perf.Note("tracks voice: " + file);
            yield return Sound(clip, stop);
        }

        // Through the booth's own source, flat (not placed in the world), at full volume over the course's bed, which is
        // turned down while a voice is speaking (Update). The app's audio session (Plugins/iOS/PuttrAudio.m) already
        // plays through the silent switch, as the app's Sound.setCategory('Playback') did. The clip is freed afterwards.
        IEnumerator Sound(AudioClip clip, int stop)
        {
            // The arcade's listener is switched off during Tracks; the world carries one, and makes it when first asked.
            _worlds = FindObjectsByType<TracksWorld>();
            if (_worlds.Length > 0) _worlds[0].EnsureListener();
            _source.clip = clip;
            _source.volume = 1f;
            _source.Play();
            Perf.Wake(clip.length + 0.5f);
            float until = Time.realtimeSinceStartup + clip.length + 0.5f;   // never left waiting on a clip that stalls
            while (stop == _stop && _source.clip == clip && _source.isPlaying && Time.realtimeSinceStartup < until) yield return null;
            if (_source.clip == clip) { _source.Stop(); _source.clip = null; }
            Destroy(clip);
        }

        void Update()
        {
            float want = _source != null && _source.isPlaying ? Ducked : 1f;
            if (Mathf.Approximately(_duck, want)) return;
            _duck = Mathf.MoveTowards(_duck, want, Time.unscaledDeltaTime * 2.5f);
            foreach (TracksWorld world in _worlds) if (world != null) world.Duck(_duck);
        }

        // ── Fetching and the cache ──

        static string _dir;
        // The cache directory, as CoachVoice's lines and the app's Dirs.CacheDir: the clips can always be fetched again,
        // so they do not belong in the Player's backed-up documents.
        static string CacheDir => _dir ?? (_dir = Path.Combine(Application.temporaryCachePath, "tracks-audio"));
        static string CachePath(string file) => Path.Combine(CacheDir, CacheName(file));

        /// <summary>The clip is on the phone already.</summary>
        public static bool Cached(string file) => File.Exists(CachePath(file));

        // A clip into the cache, once even when asked twice at the same time. Fails silently (offline, a bad download):
        // the file is simply not there afterwards. Never in fixture mode, which stays off the network.
        IEnumerator Download(string file)
        {
            while (_inflight.Contains(file)) yield return null;
            string path = CachePath(file);
            if (Fixtures.On || File.Exists(path)) yield break;
            _inflight.Add(file);
            using (UnityWebRequest req = UnityWebRequest.Get(Url(file)))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                byte[] data = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
                // Under 500 bytes is an error page, not a recording (audio.js fetchOne). Written beside its name and then
                // moved, so a download cut short is never taken for a clip.
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
            _inflight.Remove(file);
        }

        IEnumerator Load(string file, System.Action<AudioClip> done)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string address = Fixtures.On ? null : Url(file);   // no files on the web: straight from the bucket
            string path = null;
#else
            yield return Download(file);
            string path = CachePath(file);
            string address = File.Exists(path) ? "file://" + path : null;
#endif
            if (address == null) { done(null); yield break; }
            using (UnityWebRequest load = UnityWebRequestMultimedia.GetAudioClip(address, AudioType.MPEG))
            {
                yield return load.SendWebRequest();
                AudioClip clip = load.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(load) : null;
                if (clip != null && clip.length <= 0f) { Destroy(clip); clip = null; }
                // A cached copy that will not play is thrown away, so the next time it is fetched afresh.
                if (clip == null && path != null) { try { File.Delete(path); } catch (System.Exception) { /* it stays; nothing more to do */ } }
                done(clip);
            }
        }

        IEnumerator Warm()
        {
            _warming = true;
            while (_toWarm.Count > 0) yield return Download(_toWarm.Dequeue());
            _warming = false;
        }
    }
}
