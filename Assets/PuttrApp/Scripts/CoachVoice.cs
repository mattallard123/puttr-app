// The coach's voice, as puttr-app's coachVoice.js: a line is spoken in the Player's coach's voice
// through ai.puttr.co/coach-tts (ElevenLabs), cached on the phone so each line is fetched once,
// and never spoken over another.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrApp
{
    public static class Coaches
    {
        public sealed class Persona { public string Id, Name, Style, VoiceId; }

        // puttr-app/constants/CoachPersonas.js
        public static readonly Persona[] All =
        {
            new Persona { Id = "maya", Name = "Maya", Style = "Warm", VoiceId = "rIm7vOh2RhWWu1SKjMYv" },
            new Persona { Id = "theo", Name = "Theo", Style = "Analytical", VoiceId = "l0IbsHmWEqO1wCRB1lgr" },
            new Persona { Id = "riley", Name = "Riley", Style = "Tough love", VoiceId = "COPpRsIi6nKnxiMA3ElC" },
            new Persona { Id = "jax", Name = "Jax", Style = "Hype", VoiceId = "v6OEdxzPU9lTVAYr1BI8" },
        };

        /// <summary>The coach's face in an avatar circle (Resources/PuttrApp/Coaches/{Name}.png), their initial if missing.</summary>
        public static UnityEngine.UIElements.VisualElement Avatar(Persona p, bool big = false)
        {
            UnityEngine.UIElements.VisualElement a = Ui.Avatar(p.Name, null, big);
            Texture2D face = Resources.Load<Texture2D>("PuttrApp/Coaches/" + p.Name);
            if (face != null) { a.style.backgroundImage = new UnityEngine.UIElements.StyleBackground(face); a.Clear(); }
            return a;
        }

        public static Persona Of(string id)
        {
            foreach (Persona p in All) if (p.Id == id) return p;
            return All[0];
        }
    }

    public sealed class CoachVoice : MonoBehaviour
    {
        const string Service = "https://ai.puttr.co/coach-tts";
        static CoachVoice _one;
        AudioSource _source;
        readonly Queue<string> _lines = new Queue<string>();
        bool _speaking;

        public static Coaches.Persona Persona = Coaches.All[0];
        public static bool On = true;

        static CoachVoice One
        {
            get
            {
                if (_one != null) return _one;
                var go = new GameObject("Coach voice");
                DontDestroyOnLoad(go);
                _one = go.AddComponent<CoachVoice>();
                _one._source = go.AddComponent<AudioSource>();
                return _one;
            }
        }

        /// <summary>The Player's coach and whether the voice is on, from GET /coach/settings.</summary>
        public static void Load(object settings)
        {
            Persona = Coaches.Of(J.Str(settings, "personaId", "maya"));
            On = J.Get(settings, "voiceOn") == null || J.Flag(settings, "voiceOn");
        }

        /// <summary>
        /// Raised once Coach settings (CoachSettingsSheet) has saved a new coach, voice, daily goal or reminder, so screens
        /// that show them (Home's goal ring, Improve's session) can read them again.
        /// </summary>
        public static event System.Action SettingsChanged;

        public static void Changed() => SettingsChanged?.Invoke();

        /// <summary>Says a line after whatever is being said now.</summary>
        public static void Say(string text)
        {
            if (!On || string.IsNullOrEmpty(text)) return;
            Perf.Note("coach: " + text);
            One._lines.Enqueue(text);
            if (!One._speaking) One.StartCoroutine(One.Speak());
        }

        public static bool Speaking => _one != null && (_one._speaking || _one._lines.Count > 0);

        /// <summary>Fetches lines ahead of time, so they play at once when wanted.</summary>
        public static void Warm(IEnumerable<string> lines)
        {
            if (!On) return;
            foreach (string line in lines) One.StartCoroutine(One.Fetch(line, null));
        }

        public static void Hush()
        {
            if (_one == null) return;
            _one._said++;
            _one._lines.Clear();
            _one._source.Stop();
        }

        int _said;   // goes up on every Hush, so a line still being fetched is dropped rather than spoken over what follows

        /// <summary>
        /// A coach introducing themselves (coachVoice.js speakCoachIntro): the bundled {id}_intro mp3, at once and offline,
        /// cutting off whatever was being said. Streams the words through coach-tts if the file is missing.
        /// Played even with the voice off, as the app: it is how a Player hears who they are picking.
        /// </summary>
        public static void Intro(Coaches.Persona persona, string words)
        {
            Hush();
            Persona = persona;
            AudioClip clip = Resources.Load<AudioClip>("PuttrApp/Coaches/" + persona.Id + "_intro");
            if (clip == null) { bool on = On; On = true; Say(words); On = on; return; }
            Perf.Note("coach: " + persona.Id + " intro");
            One._source.clip = clip;
            One._source.Play();
            Perf.Wake(clip.length + 0.5f);
        }

        IEnumerator Speak()
        {
            _speaking = true;
            while (_lines.Count > 0)
            {
                string line = _lines.Dequeue();
                int said = _said;
                AudioClip clip = null;
                yield return Fetch(line, c => clip = c);
                if (clip == null || said != _said) continue;
                _source.clip = clip;
                _source.Play();
                Perf.Wake(clip.length + 0.5f);
                // By the wall clock too: with the phone's sound clock stopped a clip never ends, and whatever waits
                // for the coach (the review's next moment, the video) would wait for ever.
                float until = Time.realtimeSinceStartup + clip.length + 0.75f;
                while (_source.isPlaying && Time.realtimeSinceStartup < until) yield return null;
                if (_source.isPlaying) { _source.Stop(); Perf.Note("coach: that line did not finish playing (the sound clock is stopped)"); }
            }
            _speaking = false;
        }

        static string CachePath(string voiceId, string text)
        {
            using (MD5 md5 = MD5.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(voiceId + "|" + text))) sb.Append(b.ToString("x2"));
                return Path.Combine(Application.temporaryCachePath, "coach_v4_" + sb + ".mp3");
            }
        }

        IEnumerator Fetch(string text, System.Action<AudioClip> done) => Clip(Persona.VoiceId, text, false, done);

        /// <summary>
        /// A sentence in any voice through the same service and the same cache as the coach's lines (the Tracks caddie
        /// speaks this way: TracksVoice). done gets the clip, or null if it could not be had; with no done the sentence is
        /// only fetched into the cache. cachedOnly never touches the network: a sentence not on the phone yet is null.
        /// </summary>
        public static IEnumerator Clip(string voiceId, string text, bool cachedOnly, System.Action<AudioClip> done)
        {
            string file = CachePath(voiceId, text);
            if (!File.Exists(file))
            {
                if (cachedOnly) { done?.Invoke(null); yield break; }
                string url = Service + "?voiceId=" + UnityWebRequest.EscapeURL(voiceId) + "&text=" + UnityWebRequest.EscapeURL(text).Replace("+", "%20");
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.timeout = 15;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success || req.downloadHandler.data == null || req.downloadHandler.data.Length < 200)
                    {
                        Perf.Note("coach: voice unavailable (" + req.responseCode + ")");
                        done?.Invoke(null);
                        yield break;
                    }
                    try { File.WriteAllBytes(file, req.downloadHandler.data); } catch (System.Exception) { done?.Invoke(null); yield break; }
                }
            }
            if (done == null) yield break;
            using (UnityWebRequest load = UnityWebRequestMultimedia.GetAudioClip("file://" + file, AudioType.MPEG))
            {
                yield return load.SendWebRequest();
                done(load.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(load) : null);
            }
        }
    }
}
