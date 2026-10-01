using System.Linq;
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

        [TestCase('.', CapColor.None, false)]
        [TestCase('B', CapColor.Blue, false)]
        [TestCase('b', CapColor.Blue, true)]
        public void Stack_cells_round_trip_through_their_code(char code, CapColor color, bool hidden)
        {
            Assert.That(StackCell.TryParse(code, out var cell), Is.True);
            Assert.That(cell, Is.EqualTo(new StackCell(color, hidden)));
            Assert.That(cell.ToCode(), Is.EqualTo(code));
        }

        [Test]
        public void A_stack_row_survives_parse_and_write()
        {
            var st = StackDefinition.FromRows(4, 1, new[] { new[] { "Ro.N" } });
            Assert.That(st.RowCodes(0, 0), Is.EqualTo("Ro.N"));
            Assert.That(st.At(0, 0, 2).IsEmpty, Is.True);
        }
    }
}
