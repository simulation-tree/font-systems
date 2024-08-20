using Data.Events;
using Data.Systems;
using Fonts.Events;
using Simulation;
using System.Numerics;
using System.Threading;
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

        private void Simulate(World world)
        {
            world.Submit(new DataUpdate());
            world.Submit(new FontUpdate());
            world.Poll();
        }

        [Test, CancelAfter(1000)]
        public void ImportArialFont(CancellationToken cancellation)
        {
            using World world = new();
            using DataImportSystem dataImports = new(world);
            using FontImportSystem fonts = new(world);

            Font font = new(world, "Fonts Systems Tests/Arial.otf");
            while (!font.IsLoaded)
            {
                cancellation.ThrowIfCancellationRequested();
                Simulate(world);
            }

            Assert.That(font.FamilyName.ToString(), Is.EqualTo("Arial"));
            Assert.That(font.GlyphCount, Is.GreaterThan(0));

            //also check the `a` character
            Glyph a = font['a'];
            Assert.That(a.Character, Is.EqualTo('a'));
            Assert.That(a.Advance, Is.EqualTo(new Vector2(18, 0)));
            Assert.That(a.Offset, Is.EqualTo(new Vector2(1, 17)));
            Assert.That(a.Size, Is.EqualTo(new Vector2(16, 17)));
        }
    }
}
