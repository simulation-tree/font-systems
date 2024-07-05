using Fonts.Components;
using Fonts.Events;
using FreeType;
using Simulation;
using System;
using System.Diagnostics;
using System.Numerics;
using Textures;
using Unmanaged.Collections;

namespace Fonts.Systems
{
    public class FontImportSystem : SystemBase
    {
        public const uint PixelSize = 32;
        public const uint GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly Query<IsFont> fontQuery;
        private readonly FreeTypeLibrary freeType;

        public FontImportSystem(World world) : base(world)
        {
            freeType = new();
            fontQuery = new(world);
            Subscribe<FontUpdate>(Update);
        }

        public override void Dispose()
        {
            fontQuery.Dispose();
            freeType.Dispose();
            base.Dispose();
        }

        private void Update(FontUpdate e)
        {
            ImportFonts();
        }

        private void ImportFonts()
        {
            fontQuery.Fill();
            foreach (Query<IsFont>.Result result in fontQuery)
            {
                ref IsFont font = ref result.Component1;
                if (font.changed)
                {
                    font.changed = false;
                    ImportFont(result.entity);
                }
            }
        }

        /// <summary>
        /// Makes sure that the entity has the latest info about the font.
        /// </summary>
        private void ImportFont(EntityID entity)
        {
            UnmanagedList<byte> bytes = world.GetCollection<byte>(entity);
            using FreeTypeFont face = freeType.Load(bytes.AsSpan());
            face.SetPixelSize(PixelSize, PixelSize);

            UpdateAtlas(face, entity);
            float lineHeight = face.Metrics.height >> 6;
            lineHeight /= PixelSize;

            //set metrics
            if (!world.ContainsComponent<FontMetrics>(entity))
            {
                world.AddComponent(entity, new FontMetrics(lineHeight));
            }
            else
            {
                world.SetComponent(entity, new FontMetrics(lineHeight));
            }

            //set family name
            Span<char> familyName = stackalloc char[128];
            int length = face.CopyFamilyName(familyName);
            if (!world.ContainsComponent<FontName>(entity))
            {
                world.AddComponent(entity, new FontName(familyName[..length]));
            }
            else
            {
                world.SetComponent(entity, new FontName(familyName[..length]));
            }
        }

        private uint UpdateAtlas(FreeTypeFont font, EntityID entity)
        {
            //get glyph collection and reset to empty
            if (!world.ContainsCollection<FontGlyph>(entity))
            {
                world.CreateCollection<FontGlyph>(entity);
            }

            UnmanagedList<FontGlyph> glyphEntities = world.GetCollection<FontGlyph>(entity);

            //collect glyph textures for each char
            Vector2 maxGlyphSize = default;
            using UnmanagedArray<AtlasTexture.InputSprite> glyphTextures = new(GlyphCount);
            Span<char> nameBuffer = stackalloc char[4];
            for (uint i = 0; i < glyphTextures.Length; i++)
            {
                char c = (char)i;
                FreeTypeGlyph loadedGlyph = font.LoadGlyph(font.GetCharIndex(c));
                Vector2 glyphOffset = new(loadedGlyph.Left, loadedGlyph.Top);
                uint glyphWidth = loadedGlyph.Width;
                uint glyphHeight = loadedGlyph.Height;
                Vector2 glyphSize = new(glyphWidth, glyphHeight);
                Vector2 advance = new(loadedGlyph.HorizontalAdvance >> 6, loadedGlyph.VerticalAdvance >> 6);
                maxGlyphSize = Vector2.Max(maxGlyphSize, glyphSize);

                ReadOnlySpan<byte> bitmap = loadedGlyph.Bitmap;
                nameBuffer[0] = '\'';
                nameBuffer[1] = c;
                nameBuffer[2] = '\'';

                Channels channel = Channels.Red;
                if (bitmap != default)
                {
                    glyphTextures[i] = new(nameBuffer[..3], new(glyphWidth, glyphHeight), bitmap, channel);
                }
                else
                {
                    glyphTextures[i] = new(nameBuffer[..3], new(1, 1), [0], channel);
                }

                //get or create glyph
                FontGlyph glyphEntity;
                if (i < glyphEntities.Count)
                {
                    glyphEntity = new(glyphEntities[i].value);
                }
                else
                {
                    Glyph newGlyph = new(world, c, advance, glyphOffset, glyphSize, default, []);
                    newGlyph.entity.Parent = entity;
                    glyphEntity = new(newGlyph.entity);
                    glyphEntities.Add(glyphEntity);
                }

                Glyph glyph = new(world, glyphEntity.value);
                glyph.ClearKernings();
                for (uint n = 32; n < GlyphCount; n++)
                {
                    (int x, int y) = font.GetKerning(c, (char)n);
                    if (x != 0 || y != 0)
                    {
                        glyph.AddKerning((char)n, new(x, y));
                    }
                }
            }

            //set atlas
            AtlasTexture atlasTexture;
            if (!world.ContainsComponent<FontAtlas>(entity))
            {
                atlasTexture = new(world, glyphTextures.AsSpan(), AtlasPadding);
                world.AddComponent(entity, new FontAtlas(atlasTexture.texture.entity));
            }
            else
            {
                ref FontAtlas atlas = ref world.GetComponentRef<FontAtlas>(entity);
                UnmanagedList<Pixel> existingPixels = world.GetCollection<Pixel>(atlas.value);
                existingPixels.Clear();

                using AtlasTexture tempAtlasTexture = new(world, glyphTextures.AsSpan(), AtlasPadding);
                Span<Pixel> newPixels = tempAtlasTexture.Pixels;
                existingPixels.AddRange(newPixels);
                atlasTexture = new(world, atlas.value);
            }

            //update uv region of all glyphs after packing
            for (uint i = 0; i < glyphEntities.Count; i++)
            {
                FontGlyph glyphEntity = glyphEntities[i];
                IsGlyph glyph = world.GetComponent<IsGlyph>(glyphEntity.value);
                Vector4 region = atlasTexture.Sprites[(int)i].region;
                world.SetComponent(glyphEntity.value, new IsGlyph(glyph.character, glyph.advance, glyph.offset, glyph.size, region));
            }

            return glyphTextures.Length;
        }
    }
}
