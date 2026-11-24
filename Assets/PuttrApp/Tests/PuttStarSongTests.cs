using NUnit.Framework;
using PuttrArcade;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class PuttStarSongTests
    {
        [TestCase(PuttStarGenre.Rock)]
        [TestCase(PuttStarGenre.HipHop)]
        [TestCase(PuttStarGenre.Edm)]
        public void TempoFollowsPerformanceInsideTheGenre(PuttStarGenre genre)
        {
            float recorded = PuttStarSong.Recorded[(int)genre];
            float even = PuttStarSong.Tempo(genre, 50f, 0);
            Assert.Greater(PuttStarSong.Tempo(genre, 50f, 1), even, "a make speeds it up");
            Assert.Less(PuttStarSong.Tempo(genre, 50f, -1), even, "a miss slows it down");
            Assert.AreEqual(recorded * PuttStarSong.FastestShare, PuttStarSong.Tempo(genre, 100f, 9), 0.001f);
            Assert.AreEqual(recorded * PuttStarSong.SlowestShare, PuttStarSong.Tempo(genre, 0f, -9), 0.001f);
        }

        [TestCase("puttstar_rock")]
        [TestCase("puttstar_hiphop")]
        [TestCase("puttstar_edm")]
        public void EveryGenreHasItsRecording(string file)
        {
            AudioClip clip = Resources.Load<AudioClip>("PuttrArcade/Audio/Music/" + file);
            Assert.IsNotNull(clip, file);
            Assert.Greater(clip.length, 20f);
        }
    }
}
