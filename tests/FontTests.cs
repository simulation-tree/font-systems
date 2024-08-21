using Data.Events;
using Data.Systems;
using Fonts.Events;
using Simulation;
using System.Threading;
using System.Threading.Tasks;
using Unmanaged;

namespace Fonts.Systems.Tests
{
    public class FontTests
    {
        [TearDown]
        public void CleanUp()
        {
            Allocations.ThrowIfAny();
        }

        private async Task Simulate(World world, CancellationToken cancellation)
        {
            world.Submit(new DataUpdate());
            world.Submit(new FontUpdate());
            world.Poll();
            await Task.Delay(1, cancellation);
        }

        [Test, CancelAfter(4000)]
        public async Task ImportArialFont(CancellationToken cancellation)
        {
            using World world = new();
            using DataImportSystem dataImports = new(world);
            using FontImportSystem fonts = new(world);

            Font font = new(world, "*/Arial.otf");
            await font.UntilIs(Simulate, cancellation);

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
