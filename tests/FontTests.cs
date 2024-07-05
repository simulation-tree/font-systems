using Data.Systems;
using Microsoft.VisualBasic;
using Simulation;
using System;
using System.Numerics;
using System.Text;
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

        [Test]
        public void ImportArialFont()
        {
            using World world = new();
            using DataImportSystem dataImports = new(world);
            using FontImportSystem fonts = new(world);

            Font font = new(world, "Fonts Systems Tests/Arial.otf");
            Assert.That(font.FamilyName.ToString(), Is.EqualTo("Arial"));
            Assert.That(font.GlyphCount, Is.GreaterThan(0));

            //also check the `a` character
            Glyph a = font['a'];
            Assert.That(a.Character, Is.EqualTo('a'));
            Assert.That(a.Advance, Is.EqualTo(new Vector2(18, 0)));
            Assert.That(a.Offset, Is.EqualTo(new Vector2(1, 17)));
            Assert.That(a.Size, Is.EqualTo(new Vector2(16, 17)));

            StringBuilder sb = new();
            uint width = font.Atlas.Width;
            uint height = font.Atlas.Height;
            Vector4 uv = a.Region;
            int minX = (int)(uv.X * width);
            int minY = (int)(uv.Y * height);
            int maxX = (int)(uv.Z * width);
            int maxY = (int)(uv.W * height);
            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    Textures.Pixel pixel = font.Atlas.Get((uint)x, (uint)y);
                    if (pixel.r > 128)
                    {
                        sb.Append('O');
                    }
                    else if (pixel.r > 64)
                    {
                        sb.Append('o');
                    }
                    else if (pixel.r > 32)
                    {
                        sb.Append('.');
                    }
                    else
                    {
                        sb.Append(' ');
                    }
                }

                sb.Append('\n');
            }

            Console.WriteLine(sb.ToString());
        }
    }
}
