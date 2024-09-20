using Fonts.Components;
using Fonts.Events;
using FreeType;
using Simulation;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Unmanaged;
using Unmanaged.Collections;

namespace Fonts.Systems
{
    public class FontImportSystem : SystemBase
    {
        public const uint PixelSize = 32;
        public const uint GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly ComponentQuery<IsFontRequest> fontQuery;
        private readonly Library freeType;
        private readonly UnmanagedDictionary<uint, uint> fontVersions;
        private readonly UnmanagedDictionary<uint, Face> fontFaces;
        private readonly ConcurrentQueue<Operation> operations;

        public FontImportSystem(World world) : base(world)
        {
            freeType = new();
            fontQuery = new();
            fontVersions = new();
            fontFaces = new();
            operations = new();
            Subscribe<FontUpdate>(Update);
        }

        public override void Dispose()
        {
            while (operations.TryDequeue(out Operation operation))
            {
                operation.Dispose();
            }

            foreach (uint fontEntity in fontFaces.Keys)
            {
                fontFaces[fontEntity].Dispose();
            }

            fontFaces.Dispose();
            fontVersions.Dispose();
            fontQuery.Dispose();
            freeType.Dispose();
            base.Dispose();
        }

        private void Update(FontUpdate e)
        {
            UpdateFontRequests();
            PerformOperations();
        }

        private void UpdateFontRequests()
        {
            fontQuery.Update(world);
            foreach (var x in fontQuery)
            {
                IsFontRequest request = x.Component1;
                bool sourceChanged = false;
                uint fontEntity = x.entity;
                if (!fontVersions.ContainsKey(fontEntity))
                {
                    sourceChanged = true;
                }
                else
                {
                    sourceChanged = fontVersions[fontEntity] != request.version;
                }

                if (sourceChanged)
                {
                    //ThreadPool.QueueUserWorkItem(TryFinishFontRequest, (fontEntity, request), false);
                    if (TryFinishFontRequest((fontEntity, request)))
                    {
                        fontVersions.AddOrSet(fontEntity, request.version);
                    }
                    else
                    {
                        Debug.WriteLine($"Font request for `{fontEntity}` failed");
                    }
                }
            }
        }

        private void PerformOperations()
        {
            while (operations.TryDequeue(out Operation operation))
            {
                world.Perform(operation);
                operation.Dispose();
            }
        }

        /// <summary>
        /// Makes sure that the entity has the latest info about the font.
        /// </summary>
        private unsafe bool TryFinishFontRequest((uint entity, IsFontRequest request) input)
        {
            uint fontEntity = input.entity;
            if (!world.ContainsArray<byte>(fontEntity))
            {
                //wait for bytes to become available
                Console.WriteLine($"Font data for `{fontEntity}` not available yet, skipping");
                return false;
            }

            if (!fontFaces.TryGetValue(fontEntity, out Face face))
            {
                USpan<byte> bytes = world.GetArray<byte>(fontEntity);
                face = freeType.Load(bytes.pointer, bytes.Length);
                fontFaces.Add(fontEntity, face);
            }

            face.SetPixelSize(PixelSize, PixelSize);

            Operation operation = new();
            LoadGlyphs(face, fontEntity, ref operation);

            //set metrics
            if (!world.ContainsComponent<FontMetrics>(fontEntity))
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
            if (!world.ContainsComponent<FontName>(fontEntity))
            {
                operation.AddComponent(new FontName(familyName[..length]));
            }
            else
            {
                operation.SetComponent(new FontName(familyName[..length]));
            }

            if (world.TryGetComponent(fontEntity, out IsFont component))
            {
                component.version++;
                operation.SetComponent(component);
            }
            else
            {
                operation.AddComponent(new IsFont());
            }

            operations.Enqueue(operation);
            return true;
        }

        private void LoadGlyphs(Face font, uint fontEntity, ref Operation operation)
        {
            operation.SelectEntity(fontEntity);
            bool createGlyphs = false;
            if (world.TryGetArray(fontEntity, out USpan<FontGlyph> existingList))
            {
                //get glyph collection and reset to empty
                foreach (FontGlyph oldGlyph in existingList)
                {
                    operation.RemoveReference(oldGlyph.value);
                }

                operation.ClearSelection();
                foreach (FontGlyph oldGlyph in existingList)
                {
                    uint glyphEntity = world.GetReference(fontEntity, oldGlyph.value);
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
            uint referenceCount = world.GetReferenceCount(fontEntity);
            USpan<Kerning> kerningBuffer = stackalloc Kerning[96];
            uint kerningCount = 0;
            using UnmanagedArray<FontGlyph> glyphsBuffer = new(GlyphCount);
            for (uint i = 0; i < GlyphCount; i++)
            {
                char c = (char)i;
                GlyphSlot loadedGlyph = font.LoadGlyph(font.GetCharIndex(c));
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
                operation.SetParent(fontEntity);

                kerningCount = 0;
                for (uint n = 32; n < GlyphCount; n++)
                {
                    (int x, int y) kerning = font.GetKerning(c, (char)n);
                    if (kerning != default)
                    {
                        kerningBuffer[kerningCount++] = new((char)n, new(kerning.x, kerning.y));
                    }
                }

                operation.CreateArray<Kerning>(kerningBuffer.Slice(0, kerningCount));

                rint glyphReference = (rint)(referenceCount + i + 1);
                operation.ClearSelection();
                operation.SelectEntity(fontEntity);
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
