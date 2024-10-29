using Data.Systems;
using Simulation.Tests;
using System.Threading;
using System.Threading.Tasks;

namespace Fonts.Systems.Tests
{
    public class FontTests : SimulationTests
    {
        protected override void SetUp()
        {
            base.SetUp();
            Simulator.AddSystem<DataImportSystem>();
            Simulator.AddSystem<FontImportSystem>();
        }

        [Test, CancelAfter(8000)]
        public async Task ImportArialFont(CancellationToken cancellation)
        {
            Font font = new(World, "*/Arial.otf");
            await font.UntilCompliant(Simulate, cancellation);

            Assert.That(font.FamilyName.ToString(), Is.EqualTo("Arial"));
            Assert.That(font.GlyphCount, Is.GreaterThan(0));
            Assert.That(font.LineHeight, Is.EqualTo(2355));

            //also check the `a` character
            Glyph a = font['a'];
            Assert.That(a.Character, Is.EqualTo('a'));
            Assert.That(a.Advance, Is.EqualTo((1152, 1920)));
            Assert.That(a.Offset, Is.EqualTo((1, 17)));
            Assert.That(a.Size, Is.EqualTo((1024, 1088)));
        }
    }
}
