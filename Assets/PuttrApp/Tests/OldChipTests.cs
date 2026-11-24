// Which graphics chips get the plain picture in games (no HDR, no post-processing): Apple's A9 and A10 families.
using NUnit.Framework;
using PuttrArcade;

namespace PuttrApp.Tests
{
    public class OldChipTests
    {
        [TestCase("Apple A9X GPU")]
        [TestCase("Apple A9 GPU")]
        [TestCase("Apple A10 GPU")]
        [TestCase("Apple A10X GPU")]
        public void ChipsBeforeTheA11ArePlain(string gpu) => Assert.IsTrue(Arcade.OldChip(gpu));

        [TestCase("Apple A11 GPU")]
        [TestCase("Apple A12Z GPU")]
        [TestCase("Apple A19 Pro GPU")]
        [TestCase("Apple M1")]
        [TestCase("Apple M4 Max")]
        [TestCase("Apple Paravirtual device")]
        [TestCase("Adreno (TM) 740")]
        [TestCase("")]
        [TestCase(null)]
        public void EverythingElseIsNot(string gpu) => Assert.IsFalse(Arcade.OldChip(gpu));

        [TestCase("iPad6,7")]     // the first 12.9 in iPad Pro (A9X): the one the white world was seen on
        [TestCase("iPad6,11")]    // iPad, 5th generation (A9)
        [TestCase("iPad7,12")]    // iPad, 7th generation (A10)
        [TestCase("iPhone8,1")]   // iPhone 6s
        [TestCase("iPhone9,3")]   // iPhone 7
        public void ModelsBeforeTheA11AreOld(string model) => Assert.IsTrue(Arcade.OldModel(model));

        [TestCase("iPhone10,1")]  // iPhone 8 (A11)
        [TestCase("iPhone18,1")]
        [TestCase("iPad8,1")]     // 2018 iPad Pro (A12X)
        [TestCase("iPad11,6")]    // iPad, 8th generation (A12)
        [TestCase("iPad13,16")]
        [TestCase("AppleTV11,1")] // Apple TV 4K, 2nd generation (A12)
        [TestCase("AppleTV6,2")]  // the first Apple TV 4K (A10X): it drew the full picture correctly
        [TestCase("arm64")]
        [TestCase("Mac15,6")]
        [TestCase("")]
        [TestCase(null)]
        public void NewerModelsAreNot(string model) => Assert.IsFalse(Arcade.OldModel(model));

        // A spot drawn as a directional light (PlainLights): as bright as it was where its axis meets the floor.
        [Test]
        public void ASpotIsAsBrightAsItWasOnTheFloor() =>
            Assert.AreEqual(0.5f, PlainLights.Flat(200f, new UnityEngine.Vector3(0f, 20f, 0f), UnityEngine.Vector3.down, 70f), 1e-4f);

        [Test]
        public void TheConsolesHoodLightIsHeldToTheBrightest() =>
            Assert.AreEqual(PlainLights.Brightest, PlainLights.Flat(900f, new UnityEngine.Vector3(0f, 12.9f, 0.5f), UnityEngine.Vector3.down, 70f), 1e-4f);

        [Test]
        public void ASpotThatDoesNotPointDownIsMeasuredAtHalfItsReach() =>
            Assert.AreEqual(1f, PlainLights.Flat(400f, new UnityEngine.Vector3(0f, 5f, 0f), UnityEngine.Vector3.forward, 40f), 1e-4f);
    }
}
