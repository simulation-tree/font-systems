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
        static FontTests()
        {
            TypeLayout.Register<IsFont>("IsFont");
            TypeLayout.Register<IsFontRequest>("IsFontRequest");
            TypeLayout.Register<IsGlyph>("IsGlyph");
            TypeLayout.Register<IsDataRequest>("IsDataRequest");
            TypeLayout.Register<IsDataSource>("IsDataSource");
            TypeLayout.Register<IsData>("IsData");
            TypeLayout.Register<FontMetrics>("FontMetrics");
            TypeLayout.Register<FontName>("FontName");
            TypeLayout.Register<BinaryData>("BinaryData");
            TypeLayout.Register<Kerning>("Kerning");
            TypeLayout.Register<FontGlyph>("FontGlyph");
        }

        protected override void SetUp()
        {
            base.SetUp();
            world.Schema.RegisterComponent<IsFont>();
            world.Schema.RegisterComponent<IsFontRequest>();
            world.Schema.RegisterComponent<IsGlyph>();
            world.Schema.RegisterComponent<IsDataRequest>();
            world.Schema.RegisterComponent<IsDataSource>();
            world.Schema.RegisterComponent<IsData>();
            world.Schema.RegisterComponent<FontMetrics>();
            world.Schema.RegisterComponent<FontName>();
            world.Schema.RegisterArrayElement<BinaryData>();
            world.Schema.RegisterArrayElement<Kerning>();
            world.Schema.RegisterArrayElement<FontGlyph>();
            simulator.AddSystem<DataImportSystem>();
            simulator.AddSystem<FontImportSystem>();
        }

        [Test, CancelAfter(8000)]
        public async Task ImportArialFont(CancellationToken cancellation)
        {
            Font font = new(world, "Arial.otf");
            await font.UntilCompliant(Simulate, cancellation);

            Assert.That(font.FamilyName.ToString(), Is.EqualTo("Arial"));
            Assert.That(font.GlyphCount, Is.GreaterThan(0));
            Assert.That(font.LineHeight, Is.EqualTo(2355));

            //also check the `a` character
            Glyph a = font['a'];
            Assert.That(a.Character, Is.EqualTo('a'));
            Assert.That(a.Advance.x / (float)font.PixelSize, Is.EqualTo(36));
            Assert.That(a.Advance.y / (float)font.PixelSize, Is.EqualTo(60));
            Assert.That(a.Offset.x / (float)font.PixelSize, Is.EqualTo(0.03125f));
            Assert.That(a.Offset.y / (float)font.PixelSize, Is.EqualTo(0.53125f));
            Assert.That(a.Size.x / (float)font.PixelSize, Is.EqualTo(32));
            Assert.That(a.Size.y / (float)font.PixelSize, Is.EqualTo(34));
        }
    }
}
