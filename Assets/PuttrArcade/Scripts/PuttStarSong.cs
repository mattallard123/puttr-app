// Putt Star's song (Matt, 2026-11-23): a full instrumental in the genre picked on the game page, made with ElevenLabs Music
// and cut to a loop on a whole number of bars (Resources/PuttrArcade/Audio/Music/puttstar_*.wav; the 60 s originals are in
// SoundDrop/_sources/puttstar_music). The band speeds up and slows down with the crowd as a turntable would, within a few
// percent so it still sounds like the same song. Beat is where the song is, for the lights to hit with it.
using UnityEngine;

namespace PuttrArcade
{
    public enum PuttStarGenre { Rock, HipHop, Edm }

    public sealed class PuttStarSong
    {
        public static readonly string[] GenreNames = { "Rock", "Hip Hop", "EDM" };
        static readonly string[] Files = { "puttstar_rock", "puttstar_hiphop", "puttstar_edm" };
        /// <summary>The tempo each recording was measured at.</summary>
        public static readonly float[] Recorded = { RockBpm, HipHopBpm, EdmBpm };
        public const float RockBpm = 149.95f, HipHopBpm = 91.99f, EdmBpm = 128.01f;
        public const float SlowestShare = 0.95f, FastestShare = 1.07f;
        /// <summary>How many putts in a row keep pushing the tempo, and the share of the genre's range each is worth.</summary>
        const int FormCap = 3;
        const float FormStep = 0.1f;

        /// <summary>The tempo for a crowd of 0 to 100 and the current run (makes in a row above zero, misses below):
        /// the crowd sets the level and each putt of a run is a step you can hear, always inside the genre's range.</summary>
        public static float Tempo(PuttStarGenre genre, float crowd, int form)
        {
            float slow = Recorded[(int)genre] * SlowestShare, fast = Recorded[(int)genre] * FastestShare, span = fast - slow;
            float bpm = slow + span * Mathf.Clamp01(crowd / 100f) + span * FormStep * Mathf.Clamp(form, -FormCap, FormCap);
            return Mathf.Clamp(bpm, slow, fast);
        }

        readonly AudioSource _source;
        PuttStarGenre _genre = PuttStarGenre.Rock;
        float _bpm = RockBpm;

        public PuttStarSong(Transform parent)
        {
            var go = new GameObject("Putt Star song");
            go.transform.SetParent(parent, false);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.loop = true;
            _source.volume = 0.55f;
        }

        /// <summary>Beats a minute; eased into by the caller.</summary>
        public float Bpm
        {
            get => _bpm;
            set { _bpm = value; if (_source != null) _source.pitch = Mathf.Clamp(value / Recorded[(int)_genre], SlowestShare, FastestShare); }
        }
        /// <summary>Which song the band plays. Set before Play.</summary>
        public PuttStarGenre Genre { get => _genre; set => _genre = value; }
        /// <summary>The song's own beat count since it started.</summary>
        public float Beat => _source != null && _source.isPlaying ? _source.time * Recorded[(int)_genre] / 60f : 0f;
        public bool Playing => _source != null && _source.isPlaying;
        public float Volume { get => _source.volume; set => _source.volume = Mathf.Clamp01(value); }

        public void Play()
        {
            if (_source == null || _source.isPlaying) return;
            _source.clip = Resources.Load<AudioClip>("PuttrArcade/Audio/Music/" + Files[(int)_genre]);
            if (_source.clip != null) _source.Play();
        }
        public void Stop() { if (_source != null) _source.Stop(); }

        /// <summary>Gone for good: only one song ever plays, so a new pick is a new PuttStarSong.</summary>
        public void Dispose()
        {
            if (_source == null) return;
            _source.Stop();
            _source.clip = null;
            Object.Destroy(_source.gameObject);
        }
    }
}
