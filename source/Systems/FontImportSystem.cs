using Collections;
using Fonts.Components;
using FreeType;
using Simulation;
using Simulation.Functions;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unmanaged;

namespace Fonts.Systems
{
    public readonly struct FontImportSystem : ISystem
    {
        public const uint PixelSize = 32;
        public const uint GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly ComponentQuery<IsFontRequest> fontQuery;
        private readonly Library freeType;
        private readonly Dictionary<Entity, uint> fontVersions;
        private readonly Dictionary<Entity, Face> fontFaces;
        private readonly List<Operation> operations;

        readonly unsafe InitializeFunction ISystem.Initialize => new(&Initialize);
        readonly unsafe IterateFunction ISystem.Update => new(&Update);
        readonly unsafe FinalizeFunction ISystem.Finalize => new(&Finalize);

        [UnmanagedCallersOnly]
        private static void Initialize(SystemContainer container, World world)
        {
        }

        [UnmanagedCallersOnly]
        private static void Update(SystemContainer container, World world, TimeSpan delta)
        {
            ref FontImportSystem system = ref container.Read<FontImportSystem>();
            system.Update(world);
        }

        [UnmanagedCallersOnly]
        private static void Finalize(SystemContainer container, World world)
        {
            if (container.World == world)
            {
                ref FontImportSystem system = ref container.Read<FontImportSystem>();
                system.CleanUp();
            }
        }

        public FontImportSystem()
        {
            freeType = new();
            fontQuery = new();
            fontVersions = new();
            fontFaces = new();
            operations = new();
        }

        private void CleanUp()
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
            fontQuery.Dispose();
            freeType.Dispose();
        }

        private void Update(World world)
        {
            UpdateFontRequests(world);
            PerformOperations(world);
        }

        private void UpdateFontRequests(World world)
        {
            fontQuery.Update(world);
            foreach (var x in fontQuery)
            {
                IsFontRequest request = x.Component1;
                bool sourceChanged = false;
                Entity font = new(world, x.entity);
                if (!fontVersions.ContainsKey(font))
                {
                    sourceChanged = true;
                }
                else
                {
                    sourceChanged = fontVersions[font] != request.version;
                }

                if (sourceChanged)
                {
                    if (TryFinishFontRequest((font, request)))
                    {
                        fontVersions.AddOrSet(font, request.version);
                    }
                    else
                    {
                        Debug.WriteLine($"Font request for `{font}` failed");
                    }
                }
            }
        }

        private void PerformOperations(World world)
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
        private unsafe bool TryFinishFontRequest((Entity font, IsFontRequest request) input)
        {
            Entity font = input.font;
            World world = font.GetWorld();
            if (!font.ContainsArray<byte>())
            {
                //wait for bytes to become available
                Debug.WriteLine($"Font data for `{font}` not available yet, skipping");
                return false;
            }

            if (!fontFaces.TryGetValue(font, out Face face))
            {
                USpan<byte> bytes = font.GetArray<byte>();
                face = freeType.Load(bytes.Address, bytes.Length);
                fontFaces.Add(font, face);
            }

            face.SetPixelSize(PixelSize, PixelSize);

            Operation operation = new();
            LoadGlyphs(font, face, ref operation);

            //set metrics
            if (!font.ContainsComponent<FontMetrics>())
            {
                operation.AddComponent(new FontMetrics(face.Height));
            }
            else
            {
                operation.SetComponent(new FontMetrics(face.Height));
            }

            //set family name
            Span<char> familyName = stackalloc char[128];
            int length = face.CopyFamilyName(familyName);
            if (!font.ContainsComponent<FontName>())
            {
                operation.AddComponent(new FontName(familyName[..length]));
            }
            else
            {
                operation.SetComponent(new FontName(familyName[..length]));
            }

            if (font.TryGetComponent(out IsFont component))
            {
                component.version++;
                operation.SetComponent(component);
            }
            else
            {
                operation.AddComponent(new IsFont());
            }

            operations.Add(operation);
            return true;
        }

        private void LoadGlyphs(Entity font, Face face, ref Operation operation)
        {
            operation.SelectEntity(font);
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
                operation.ClearSelection();
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

                operation.CreateArray<Kerning>(kerningBuffer.Slice(0, kerningCount));

                rint glyphReference = (rint)(referenceCount + i + 1);
                operation.ClearSelection();
                operation.SelectEntity(font);
                operation.AddReferenceTowardsPreviouslyCreatedEntity(0);
                glyphsBuffer[i] = new(glyphReference);
            }

            if (createGlyphs)
            {
                operation.CreateArray<FontGlyph>(glyphsBuffer.AsSpan());
            }
            else
            {
                operation.ResizeArray<FontGlyph>(GlyphCount);
                operation.SetArrayElements(0, glyphsBuffer.AsSpan());
            }
        }
    }
}
