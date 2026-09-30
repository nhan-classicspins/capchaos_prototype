using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    public sealed class JsonReaderTests
    {
        [Test]
        public void Parses_nested_values_in_order()
        {
            var v = JsonReader.Parse("{ \"a\": [1, -2.5e1, true, null], \"b\": { \"c\": \"x\\ny\\u0041\" } }");
            Assert.That(v.Kind, Is.EqualTo(JsonKind.Object));
            Assert.That(v.Members[0].Key, Is.EqualTo("a"));
            Assert.That(v.TryGet("a", out var a), Is.True);
            Assert.That(a.Items[1].Number, Is.EqualTo(-25.0));
            Assert.That(a.Items[2].Bool, Is.True);
            Assert.That(a.Items[3].Kind, Is.EqualTo(JsonKind.Null));
            v.TryGet("b", out var b); b.TryGet("c", out var c);
            Assert.That(c.String, Is.EqualTo("x\nyA"));
        }

        [TestCase("{\"a\": 1,}", "expected a property name")]
        [TestCase("[1, 2,]", "unexpected character")]
        [TestCase("{\"a\": 1, \"a\": 2}", "duplicate property 'a'")]
        [TestCase("// c\n{}", "unexpected character")]
        [TestCase("{\"a\": \"open}", "unterminated string")]
        [TestCase("{} {}", "trailing content")]
        [TestCase("01", "trailing content")]
        public void Rejects_non_strict_json(string text, string fragment)
        {
            var e = Assert.Throws<JsonParseException>(() => JsonReader.Parse(text));
            Assert.That(e!.Message, Does.Contain(fragment));
        }

        [Test]
        public void Reports_line_and_column()
        {
            var e = Assert.Throws<JsonParseException>(() => JsonReader.Parse("{\n  \"a\": x\n}"));
            Assert.That(e!.Line, Is.EqualTo(2));
            Assert.That(e.Column, Is.EqualTo(8));
        }
    }
}
