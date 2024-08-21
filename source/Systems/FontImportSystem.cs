using Fonts.Components;
using Fonts.Events;
using FreeType;
using Simulation;
using System;
using System.Collections.Concurrent;
using System.Numerics;
using Unmanaged.Collections;

namespace Fonts.Systems
{
    public class FontImportSystem : SystemBase
    {
        public const uint PixelSize = 32;
        public const uint GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly Query<IsFontRequest> fontQuery;
        private readonly Library freeType;
        private readonly UnmanagedDictionary<eint, uint> fontVersions;
        private readonly UnmanagedDictionary<eint, Face> fontFaces;
        private readonly ConcurrentQueue<Operation> operations;

        public FontImportSystem(World world) : base(world)
        {
            freeType = new();
            fontQuery = new(world);
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

            foreach (eint fontEntity in fontFaces.Keys)
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
            fontQuery.Update();
            foreach (var x in fontQuery)
            {
                IsFontRequest request = x.Component1;
                bool sourceChanged = false;
                eint fontEntity = x.entity;
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
                        fontVersions[fontEntity] = request.version;
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
        private bool TryFinishFontRequest((eint entity, IsFontRequest request) input)
        {
            eint fontEntity = input.entity;
            if (!world.ContainsList<byte>(fontEntity))
            {
                //wait for bytes to become available
                return false;
            }

            if (!fontFaces.TryGetValue(fontEntity, out Face face))
            {
                UnmanagedList<byte> bytes = world.GetList<byte>(fontEntity);
                face = freeType.Load(bytes.AsSpan());
                fontFaces.Add(fontEntity, face);
            }

            face.SetPixelSize(PixelSize, PixelSize);

            Operation operation = new();
            operation.SelectEntity(fontEntity);

            LoadGlyphs(face, fontEntity, ref operation);
            float lineHeight = face.SizeMetrics.height >> 6;
            lineHeight /= PixelSize;

            //set metrics
            if (!world.ContainsComponent<FontMetrics>(fontEntity))
            {
                operation.AddComponent(new FontMetrics(lineHeight));
            }
            else
            {
                operation.SetComponent(new FontMetrics(lineHeight));
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

        private void LoadGlyphs(Face font, eint fontEntity, ref Operation operation)
        {
            if (world.TryGetList(fontEntity, out UnmanagedList<FontGlyph> existingList))
            {
                //get glyph collection and reset to empty
                foreach (FontGlyph oldGlyph in existingList)
                {
                    operation.RemoveReference(oldGlyph.value);
                }

                operation.ClearSelection();
                foreach (FontGlyph oldGlyph in existingList)
                {
                    eint glyphEntity = world.GetReference(fontEntity, oldGlyph.value);
                    operation.SelectEntity(glyphEntity);
                }

                operation.DestroySelected();
                operation.ClearSelection();
                operation.SelectEntity(fontEntity);
                operation.ClearList<FontGlyph>();
            }
            else
            {
                operation.CreateList<FontGlyph>();
            }

            //collect glyph textures for each char
            Vector2 maxGlyphSize = default;
            Span<char> nameBuffer = stackalloc char[4];
            uint referenceCount = world.GetReferenceCount(fontEntity);
            for (uint i = 0; i < GlyphCount; i++)
            {
                char c = (char)i;
                GlyphSlot loadedGlyph = font.LoadGlyph(font.GetCharIndex(c));
                GlyphMetrics metrics = loadedGlyph.Metrics;
                Vector2 glyphOffset = new(loadedGlyph.Left, loadedGlyph.Top);
                (uint x, uint y) metricsSize = metrics.Size;
                Vector2 glyphSize = new(metricsSize.x, metricsSize.y);
                //todo: fault: fonts only have information about horizontal bearing, never vertical, assuming that all text
                //will be laid out horizontally
                (int x, int y) metricsBearing = metrics.HorizontalBearing;
                Vector2 glyphBearing = new(metricsBearing.x, metricsBearing.y);
                Vector2 advance = new(metrics.HorizontalAdvance >> 6, metrics.VerticalAdvance >> 6);
                maxGlyphSize = Vector2.Max(maxGlyphSize, glyphSize);

                nameBuffer[0] = '\'';
                nameBuffer[1] = c;
                nameBuffer[2] = '\'';

                //create glyph entity
                operation.ClearSelection();
                operation.CreateEntity();
                operation.AddComponent(new IsGlyph(c, advance, glyphBearing, glyphOffset, glyphSize));
                operation.SetParent(fontEntity);
                operation.CreateList<Kerning>();
                for (uint n = 32; n < GlyphCount; n++)
                {
                    (int x, int y) kerning = font.GetKerning(c, (char)n);
                    if (kerning != default)
                    {
                        operation.AppendToList(new Kerning((char)n, new(kerning.x, kerning.y)));
                    }
                }

                rint glyphReference = (rint)(referenceCount + i + 1);
                operation.ClearSelection();
                operation.SelectEntity(fontEntity);
                operation.AddReference(0);
                operation.AppendToList(new FontGlyph(glyphReference));
            }
        }
    }
}
