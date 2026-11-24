// The game replay's putt clips: each putt mapped into the recording as puttr-app's GameReplay.js buildPuttMap does.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class GameReplayTests
    {
        [Test]
        public void PuttsMapIntoTheirSegment()
        {
            List<object> segments = GameReplay.Segments(J.List(J.Parse(
                "[{\"videoUrl\":\"https://v/b.mp4\",\"created_at\":\"2026-11-15T18:10:00.000Z\"},{\"videoUrl\":\"\",\"created_at\":\"2026-11-15T18:05:00.000Z\"},{\"videoUrl\":\"https://v/a.mp4\",\"created_at\":\"2026-11-15T18:00:00.000Z\"}]")));
            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual("https://v/a.mp4", J.Str(segments[0], "videoUrl"));
            List<GameReplay.Clip> clips = GameReplay.Clips(segments, J.List(J.Parse(
                "[{\"created_at\":\"2026-11-15T18:00:03.000Z\"},{\"created_at\":\"2026-11-15T18:01:00.400Z\"},{\"created_at\":\"2026-11-15T18:10:20.000Z\"},{\"distance\":5}]")));
            Assert.AreEqual(4, clips.Count);
            Assert.AreEqual(0, clips[0].Seg);
            Assert.AreEqual(4f, clips[0].At);
            Assert.AreEqual(0f, clips[0].Start, "never before the video starts");
            Assert.AreEqual(6f, clips[0].End);
            Assert.AreEqual(61f, clips[1].At);
            Assert.AreEqual(55f, clips[1].Start);
            Assert.AreEqual(1, clips[2].Seg);
            Assert.AreEqual(21f, clips[2].At);
            Assert.IsFalse(clips[3].Timed, "a putt with no time has no clip");
        }
    }
}
