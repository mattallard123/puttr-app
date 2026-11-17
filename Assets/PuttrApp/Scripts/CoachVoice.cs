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
            _one._lines.Clear();
            _one._source.Stop();
        }

        IEnumerator Speak()
        {
            _speaking = true;
            while (_lines.Count > 0)
            {
                string line = _lines.Dequeue();
                AudioClip clip = null;
                yield return Fetch(line, c => clip = c);
                if (clip == null) continue;
                _source.clip = clip;
                _source.Play();
                Perf.Wake(clip.length + 0.5f);
                while (_source.isPlaying) yield return null;
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

        IEnumerator Fetch(string text, System.Action<AudioClip> done)
        {
            string file = CachePath(Persona.VoiceId, text);
            if (!File.Exists(file))
            {
                string url = Service + "?voiceId=" + UnityWebRequest.EscapeURL(Persona.VoiceId) + "&text=" + UnityWebRequest.EscapeURL(text).Replace("+", "%20");
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
