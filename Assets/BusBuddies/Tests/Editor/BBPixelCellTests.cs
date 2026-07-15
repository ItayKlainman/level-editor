using Newtonsoft.Json;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BBPixelCellTests
    {
        [Test]
        public void Hidden_DefaultsFalse_AndRoundTrips()
        {
            var cell = new BBPixelCell { ColorId = "red", Hidden = true };
            var json = JsonConvert.SerializeObject(cell);
            var back = JsonConvert.DeserializeObject<BBPixelCell>(json);

            Assert.AreEqual("red", back.ColorId);
            Assert.IsTrue(back.Hidden);
            Assert.IsFalse(new BBPixelCell().Hidden, "Hidden must default to false");
        }
    }
}
