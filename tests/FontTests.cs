using Data.Components;
using Data.Systems;
using Fonts.Components;
using Simulation.Components;
using Simulation.Tests;
using System.Threading;
using System.Threading.Tasks;
using Worlds;

namespace Fonts.Systems.Tests
{
    public class FontTests : SimulationTests
    {
        protected override void SetUp()
        {
            base.SetUp();
            ComponentType.Register<IsFont>();
            ComponentType.Register<IsFontRequest>();
            ComponentType.Register<IsGlyph>();
            ComponentType.Register<IsDataRequest>();
            ComponentType.Register<IsDataSource>();
            ComponentType.Register<IsData>();
            ComponentType.Register<IsProgram>();
            ComponentType.Register<FontMetrics>();
            ComponentType.Register<FontName>();
            ArrayType.Register<BinaryData>();
            ArrayType.Register<Kerning>();
            ArrayType.Register<FontGlyph>();
            Simulator.AddSystem<DataImportSystem>();
            Simulator.AddSystem<FontImportSystem>();
        }

        [Test, CancelAfter(8000)]
        public async Task ImportArialFont(CancellationToken cancellation)
        {
            Font font = new(World, "Arial.otf");
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
