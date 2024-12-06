using Collections;
using Data.Components;
using Fonts.Components;
using FreeType;
using Simulation;
using System;
using System.Diagnostics;
using Unmanaged;
using Worlds;

namespace Fonts.Systems
{
    public readonly partial struct FontImportSystem : ISystem
    {
        public const uint PixelSize = 32;
        public const uint GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly Library freeType;
        private readonly Dictionary<Entity, uint> fontVersions;
        private readonly Dictionary<Entity, Face> fontFaces;
        private readonly List<Operation> operations;

        public FontImportSystem()
        {
            freeType = new();
            fontVersions = new();
            fontFaces = new();
            operations = new();
        }
        void ISystem.Start(in SystemContainer systemContainer, in World world)
        {
        }

        void ISystem.Update(in SystemContainer systemContainer, in World world, in TimeSpan delta)
        {
            ComponentQuery<IsFontRequest> requestQuery = new(world);
            foreach (var r in requestQuery)
            {
                Entity font = new(world, r.entity);
                ref IsFontRequest component = ref r.component1;
                bool sourceChanged;
                if (!fontVersions.ContainsKey(font))
                {
                    sourceChanged = true;
                }
                else
                {
                    sourceChanged = fontVersions[font] != component.version;
                }

                if (sourceChanged)
                {
                    if (TryLoadFontData(font, component))
                    {
                        fontVersions.AddOrSet(font, component.version);
                    }
                    else
                    {
                        Trace.WriteLine($"Font request for `{font}` failed");
                    }
                }
            }

            PerformOperations(world);
        }

        void ISystem.Finish(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                while (operations.Count > 0)
                {
                    Operation operation = operations.RemoveAt(0);
                    operation.Dispose();
                }

                operations.Dispose();
                foreach (Entity font in fontFaces.Keys)
                {
                    fontFaces[font].Dispose();
                }

                fontFaces.Dispose();
                fontVersions.Dispose();
                freeType.Dispose();
            }
        }

        private readonly void PerformOperations(World world)
        {
            while (operations.Count > 0)
            {
                Operation operation = operations.RemoveAt(0);
                world.Perform(operation);
                operation.Dispose();
            }
        }

        /// <summary>
        /// Makes sure that the entity has the latest info about the font.
        /// </summary>
        private readonly bool TryLoadFontData(Entity font, IsFontRequest request)
        {
            World world = font.GetWorld();
            if (!font.ContainsArray<BinaryData>())
            {
                //wait for bytes to become available
                Trace.WriteLine($"Font data for `{font}` not available yet, skipping");
                return false;
            }

            if (!fontFaces.TryGetValue(font, out Face face))
            {
                USpan<BinaryData> bytes = font.GetArray<BinaryData>();
                face = freeType.Load(bytes.Address, bytes.Length);
                fontFaces.Add(font, face);
            }

            face.SetPixelSize(PixelSize, PixelSize);

            Operation operation = new();
            Operation.SelectedEntity selectedEntity = operation.SelectEntity(font);

            //set metrics
            if (!font.ContainsComponent<FontMetrics>())
            {
                selectedEntity.AddComponent(new FontMetrics(face.Height));
            }
            else
            {
                selectedEntity.SetComponent(new FontMetrics(face.Height));
            }

            //set family name
            Span<char> familyName = stackalloc char[128];
            int length = face.CopyFamilyName(familyName);
            if (!font.ContainsComponent<FontName>())
            {
                selectedEntity.AddComponent(new FontName(familyName[..length]));
            }
            else
            {
                selectedEntity.SetComponent(new FontName(familyName[..length]));
            }

            ref IsFont component = ref font.TryGetComponent<IsFont>(out bool contains);
            if (contains)
            {
                selectedEntity.SetComponent(new IsFont(component.version + 1));
            }
            else
            {
                selectedEntity.AddComponent(new IsFont());
            }

            LoadGlyphs(font, face, ref operation);
            operations.Add(operation);
            return true;
        }

        private readonly void LoadGlyphs(Entity font, Face face, ref Operation operation)
        {
            bool createGlyphs = false;
            if (font.TryGetArray(out USpan<FontGlyph> existingList))
            {
                //get glyph collection and reset to empty
                foreach (FontGlyph oldGlyph in existingList)
                {
                    operation.RemoveReference(oldGlyph.value);
                }

                operation.ClearSelection();
                foreach (FontGlyph oldGlyph in existingList)
                {
                    uint glyphEntity = font.GetReference(oldGlyph.value);
                    operation.SelectEntity(glyphEntity);
                }

                operation.DestroySelected();
            }
            else
            {
                createGlyphs = true;
            }

            //collect glyph textures for each char
            Span<char> nameBuffer = stackalloc char[4];
            uint referenceCount = font.GetReferenceCount();
            USpan<Kerning> kerningBuffer = stackalloc Kerning[96];
            uint kerningCount = 0;
            using Array<FontGlyph> glyphsBuffer = new(GlyphCount);
            for (uint i = 0; i < GlyphCount; i++)
            {
                char c = (char)i;
                GlyphSlot loadedGlyph = face.LoadGlyph(face.GetCharIndex(c));
                GlyphMetrics metrics = loadedGlyph.Metrics;
                (int x, int y) glyphOffset = (loadedGlyph.Left, loadedGlyph.Top);
                //todo: fault: fonts only have information about horizontal bearing, never vertical, assuming that all text
                //will be laid out horizontally

                nameBuffer[0] = '\'';
                nameBuffer[1] = c;
                nameBuffer[2] = '\'';

                //create glyph entity
                operation.CreateEntity();
                operation.AddComponent(new IsGlyph(c, metrics.Advance, metrics.HorizontalBearing, glyphOffset, metrics.Size));
                operation.SetParent(font);

                kerningCount = 0;
                for (uint n = 32; n < GlyphCount; n++)
                {
                    (int x, int y) kerning = face.GetKerning(c, (char)n);
                    if (kerning != default)
                    {
                        kerningBuffer[kerningCount++] = new((char)n, new(kerning.x, kerning.y));
                    }
                }

                operation.CreateArray(kerningBuffer.Slice(0, kerningCount));

                rint glyphReference = (rint)(referenceCount + i + 1);
                operation.ClearSelection();
                operation.SelectEntity(font);
                operation.AddReferenceTowardsPreviouslyCreatedEntity(0);
                glyphsBuffer[i] = new(glyphReference);
            }

            if (createGlyphs)
            {
                operation.CreateArray(glyphsBuffer.AsSpan());
            }
            else
            {
                operation.ResizeArray<FontGlyph>(GlyphCount);
                operation.SetArrayElements(0, glyphsBuffer.AsSpan());
            }
        }
    }
}
