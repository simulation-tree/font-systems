using Collections.Generic;
using Data.Messages;
using Fonts.Components;
using FreeType;
using Simulation;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unmanaged;
using Worlds;

namespace Fonts.Systems
{
    [SkipLocalsInit]
    public class FontImportSystem : ISystem, IDisposable
    {
        public const int GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly Library freeType;
        private readonly Dictionary<uint, Face> fontFaces;
        private readonly Operation operation;
        private readonly int requestType;
        private readonly int fontType;
        private readonly int glyphArrayType;

        public FontImportSystem(Simulator simulator)
        {
            freeType = new();
            fontFaces = new(4);
            operation = new();

            Schema schema = simulator.world.Schema;
            requestType = schema.GetComponentType<IsFontRequest>();
            fontType = schema.GetComponentType<IsFont>();
            glyphArrayType = schema.GetArrayType<FontGlyph>();
        }

        public void Dispose()
        {
            operation.Dispose();
            foreach (Face face in fontFaces.Values)
            {
                face.Dispose();
            }

            fontFaces.Dispose();
            freeType.Dispose();
        }

        void ISystem.Update(Simulator simulator, double deltaTime)
        {
            World world = simulator.world;
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.ContainsComponent(requestType))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsFontRequest> components = chunk.GetComponents<IsFontRequest>(requestType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsFontRequest request = ref components[i];
                        uint font = entities[i];
                        if (request.status == IsFontRequest.Status.Submitted)
                        {
                            request.status = IsFontRequest.Status.Loading;
                            Trace.WriteLine($"Started searching data for font `{font}` with address `{request.address}`");
                        }

                        if (request.status == IsFontRequest.Status.Loading)
                        {
                            if (TryLoadFont(world, font, request, simulator))
                            {
                                Trace.WriteLine($"Font `{font}` has been loaded");
                                request.status = IsFontRequest.Status.Loaded;
                            }
                            else
                            {
                                request.duration += deltaTime;
                                if (request.duration >= request.timeout)
                                {
                                    Trace.TraceError($"Font `{font}` could not be loaded");
                                    request.status = IsFontRequest.Status.NotFound;
                                }
                            }
                        }
                    }
                }
            }

            if (operation.Count > 0)
            {
                operation.Perform(world);
                operation.Reset();
            }
        }

        /// <summary>
        /// Makes sure that the entity has the latest info about the font.
        /// </summary>
        private bool TryLoadFont(World world, uint font, IsFontRequest request, Simulator simulator)
        {
            if (!fontFaces.TryGetValue(font, out Face face))
            {
                LoadData message = new(world, request.address);
                simulator.Broadcast(ref message);
                if (message.TryConsume(out ByteReader data))
                {
                    face = freeType.Load(data.GetBytes());
                    fontFaces.Add(font, face);
                    data.Dispose();
                }
                else
                {
                    return false;
                }
            }

            uint pixelSize = request.pixelSize;
            face.SetPixelSize(pixelSize, pixelSize);

            operation.SetSelectedEntity(font);

            //set metrics
            operation.AddOrSetComponent(new FontMetrics(face.Height));

            //set family name
            Span<char> familyName = stackalloc char[128];
            int length = face.CopyFamilyName(familyName);
            operation.AddOrSetComponent(new FontName(familyName[..length]));

            world.TryGetComponent(font, fontType, out IsFont component);
            operation.AddOrSetComponent(new IsFont(component.version + 1, pixelSize));

            LoadGlyphs(world, font, face);
            return true;
        }

        private void LoadGlyphs(World world, uint entity, Face face)
        {
            if (world.TryGetArray(entity, glyphArrayType, out Values<FontGlyph> existingList))
            {
                //get glyph collection and reset to empty
                foreach (FontGlyph oldGlyph in existingList)
                {
                    operation.RemoveReference(oldGlyph.value);
                }

                operation.ClearSelection();

                //todo: maybe have an operation that removes referenced entities and destroyed them at the same time?
                foreach (FontGlyph oldGlyph in existingList)
                {
                    uint glyphEntity = world.GetReference(entity, oldGlyph.value);
                    operation.SelectEntity(glyphEntity);
                }

                operation.DestroySelected();
            }

            //collect glyph textures for each char
            int referenceCount = world.GetReferenceCount(entity);
            Span<Kerning> kerningBuffer = stackalloc Kerning[96];
            int kerningCount = 0;
            using Array<FontGlyph> glyphsBuffer = new(GlyphCount);
            for (int i = 0; i < GlyphCount; i++)
            {
                char c = (char)i;
                GlyphSlot loadedGlyph = face.LoadGlyph(face.GetCharIndex(c));
                GlyphMetrics metrics = loadedGlyph.Metrics;
                (int x, int y) glyphOffset = (loadedGlyph.Left, loadedGlyph.Top);
                //todo: fault: fonts only have information about horizontal bearing, never vertical, assuming that all text
                //will be laid out horizontally

                //create glyph entity
                operation.ClearSelection();
                operation.CreateEntityAndSelect();
                operation.AddComponent(new IsGlyph(c, metrics.Advance, metrics.HorizontalBearing, glyphOffset, metrics.Size));
                operation.SetParent(entity);

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
                operation.SetSelectedEntity(entity);
                operation.AddReferenceTowardsPreviouslyCreatedEntity(0);
                glyphsBuffer[i] = new(glyphReference);
            }

            operation.CreateOrSetArray(glyphsBuffer.AsSpan());
        }
    }
}
