using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    public sealed class CapColorTests
    {
        [Test]
        public void Every_colour_has_exactly_one_code_and_round_trips()
        {
            Assert.That(CapColorCodes.All.Count, Is.EqualTo(CapColorCodes.Codes.Length));
            Assert.That(CapColorCodes.All, Does.Not.Contain(CapColor.None));
            foreach (var c in CapColorCodes.All)
                Assert.That(CapColorCodes.Parse(CapColorCodes.ToCode(c)), Is.EqualTo(c));
            Assert.That(CapColorCodes.ToCodes(CapColorCodes.All), Is.EqualTo(CapColorCodes.Codes));
        }

        [Test]
        public void Codes_follow_the_art_spec_order()
        {
            Assert.That(CapColorCodes.ParseList("ROBGPYCN"), Is.EqualTo(new[]
            {
                CapColor.Red, CapColor.Orange, CapColor.Blue, CapColor.Green,
                CapColor.Purple, CapColor.Yellow, CapColor.Cyan, CapColor.Brown,
            }));
        }

        [TestCase('r')]
        [TestCase('.')]
        [TestCase('X')]
        public void Only_an_uppercase_code_is_a_colour(char code)
        {
            Assert.That(CapColorCodes.TryParse(code, out var c), Is.False);
            Assert.That(c, Is.EqualTo(CapColor.None));
        }
    }
}
