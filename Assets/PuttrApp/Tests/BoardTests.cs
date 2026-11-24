// The club board's rows: userName is publicPlayer as a JSON string, and on many live rows (Around the
// Clock, 2026-11-17) its putterData is copied in without its quotes escaped, so the string is not JSON.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class BoardTests
    {
        static object Row(string userName) => new Dictionary<string, object> { { "userId", "u1" }, { "userName", userName }, { "score", 80.0 } };

        [Test]
        public void ReadsAPlainPlayer()
        {
            object p = GameLeaderboardScreen.Player(Row("{\"name\":\"Jason\",\"displayName\":\"Jason\",\"photoURL\":\"\",\"userId\":\"K3j\"}"));
            Assert.AreEqual("Jason", J.Str(p, "displayName"));
        }

        [Test]
        public void ReadsAPlayerWithRawPutterData()
        {
            string raw = "{\"id\":10226,\"userId\":\"d1r\",\"displayName\":\"Jrhunt3\",\"homeTown\":\"Columbia, MD\",\"photoURL\":\"https://x/p.jpg\","
                + "\"putterData\":\"{\"brand\":\"PXG\",\"model\":\"Raptor\",\"length\":\"35\"}\",\"ownsPuttr\":1,\"coachApproved\":0}";
            object p = GameLeaderboardScreen.Player(Row(raw));
            Assert.AreEqual("Jrhunt3", J.Str(p, "displayName"));
            Assert.AreEqual("https://x/p.jpg", J.Str(p, "photoURL"));
            PutterBrands.Player who = PutterBrands.Resolve(new Dictionary<string, object> { { "player", p } });
            Assert.AreEqual("Jrhunt3", who.Name);
            Assert.AreEqual("PXG", who.Brand);
        }

        [Test]
        public void NeverShowsTheRawJson()
        {
            // Broken beyond repair: the fields are read one by one, and the name is never the JSON text.
            object p = GameLeaderboardScreen.Player(Row("{\"displayName\":\"Ann\",\"bio\":\"{oops\",\"x\":[1,"));
            Assert.AreEqual("Ann", J.Str(p, "displayName"));
            object q = GameLeaderboardScreen.Player(Row("{\"photoURL\":\"\",\"bio\":\"{"));
            Assert.IsFalse(J.Str(q, "displayName").Contains("{"));
        }
    }
}
