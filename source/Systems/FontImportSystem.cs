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
    public partial class FontImportSystem : SystemBase, IListener<DataUpdate>
    {
        public const int GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly World world;
        private readonly Library freeType;
        private readonly Dictionary<uint, Face> fontFaces;
        private readonly Operation operation;
        private readonly int requestType;
        private readonly int fontType;
        private readonly int glyphArrayType;
        private readonly int metricsType;
        private readonly int nameType;
        private readonly int glyphType;
        private readonly int kerningArrayType;

        public FontImportSystem(Simulator simulator, World world) : base(simulator)
        {
            this.world = world;
            freeType = new();
            fontFaces = new(4);
            operation = new(world);

            Schema schema = world.Schema;
            requestType = schema.GetComponentType<IsFontRequest>();
            fontType = schema.GetComponentType<IsFont>();
            glyphArrayType = schema.GetArrayType<FontGlyph>();
            metricsType = schema.GetComponentType<FontMetrics>();
            nameType = schema.GetComponentType<FontName>();
            glyphType = schema.GetComponentType<IsGlyph>();
            kerningArrayType = schema.GetArrayType<Kerning>();
        }

        public override void Dispose()
        {
            operation.Dispose();
            foreach (Face face in fontFaces.Values)
            {
                face.Dispose();
            }

            fontFaces.Dispose();
            freeType.Dispose();
        }

        void IListener<DataUpdate>.Receive(ref DataUpdate message)
        {
            ReadOnlySpan<Chunk> chunks = world.Chunks;
            for (int c = 0; c < chunks.Length; c++)
            {
                Chunk chunk = chunks[c];
                if (chunk.componentTypes.Contains(requestType))
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
                            if (TryLoadFont(font, request))
                            {
                                Trace.WriteLine($"Font `{font}` has been loaded");
                                request.status = IsFontRequest.Status.Loaded;
                            }
                            else
                            {
                                request.duration += message.deltaTime;
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

            if (operation.TryPerform())
            {
                operation.Reset();
            }
        }

        /// <summary>
        /// Makes sure that the entity has the latest info about the font.
        /// </summary>
        private bool TryLoadFont(uint fontEntity, IsFontRequest request)
        {
            if (!fontFaces.TryGetValue(fontEntity, out Face face))
            {
                LoadData message = new(request.address);
                simulator.Broadcast(ref message);
                if (message.TryConsume(out ByteReader data))
                {
                    face = freeType.Load(data.GetBytes());
                    fontFaces.Add(fontEntity, face);
                    data.Dispose();
                }
                else
                {
                    return false;
                }
            }

            uint pixelSize = request.pixelSize;
            face.SetPixelSize(pixelSize, pixelSize);

            operation.SetSelectedEntity(fontEntity);

            //set metrics
            operation.AddOrSetComponent(new FontMetrics(face.Height), metricsType);

            //set family name
            Span<char> familyName = stackalloc char[128];
            int length = face.CopyFamilyName(familyName);
            operation.AddOrSetComponent(new FontName(familyName[..length]), nameType);

            world.TryGetComponent(fontEntity, fontType, out IsFont font);
            font.version++;
            font.pixelSize = pixelSize;
            operation.AddOrSetComponent(font, fontType);

            LoadGlyphs(fontEntity, face);
            return true;
        }

        private void LoadGlyphs(uint fontEntity, Face face)
        {
            if (world.TryGetArray(fontEntity, glyphArrayType, out Values<FontGlyph> existingList))
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
                    uint glyphEntity = world.GetReference(fontEntity, oldGlyph.value);
                    operation.AppendEntityToSelection(glyphEntity);
                }

                operation.DestroySelectedEntities();
            }

            //collect glyph textures for each char
            int referenceCount = world.GetReferenceCount(fontEntity);
            Span<Kerning> kerningBuffer = stackalloc Kerning[96];
            int kerningCount = 0;
            Span<FontGlyph> glyphsBuffer = stackalloc FontGlyph[GlyphCount];
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
                operation.CreateSingleEntityAndSelect();
                operation.AddComponent(new IsGlyph(c, metrics.Advance, metrics.HorizontalBearing, glyphOffset, metrics.Size), glyphType);
                operation.SetParent(fontEntity);

                kerningCount = 0;
                for (uint n = 32; n < GlyphCount; n++)
                {
                    (int x, int y) kerning = face.GetKerning(c, (char)n);
                    if (kerning != default)
                    {
                        kerningBuffer[kerningCount++] = new((char)n, new(kerning.x, kerning.y));
                    }
                }

                operation.CreateArray(kerningBuffer.Slice(0, kerningCount), kerningArrayType);

                rint glyphReference = (rint)(referenceCount + i + 1);
                operation.SetSelectedEntity(fontEntity);
                operation.AddReferenceTowardsPreviouslyCreatedEntity(0);
                glyphsBuffer[i] = new(glyphReference);
            }

            operation.CreateOrSetArray(glyphsBuffer, glyphArrayType);
        }
    }
}
