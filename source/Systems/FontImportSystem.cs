using Collections;
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
    public readonly partial struct FontImportSystem : ISystem
    {
        public const uint GlyphCount = 128;
        public const uint AtlasPadding = 4;

        private readonly Library freeType;
        private readonly Dictionary<Entity, uint> fontVersions;
        private readonly Dictionary<Entity, Face> fontFaces;
        private readonly Stack<Operation> operations;

        private FontImportSystem(Library freeType, Dictionary<Entity, uint> fontVersions, Dictionary<Entity, Face> fontFaces, Stack<Operation> operations)
        {
            this.freeType = freeType;
            this.fontVersions = fontVersions;
            this.fontFaces = fontFaces;
            this.operations = operations;
        }

        void ISystem.Start(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                Library freeType = new();
                Dictionary<Entity, uint> fontVersions = new();
                Dictionary<Entity, Face> fontFaces = new();
                Stack<Operation> operations = new();
                systemContainer.Write(new FontImportSystem(freeType, fontVersions, fontFaces, operations));
            }
        }

        void ISystem.Update(in SystemContainer systemContainer, in World world, in TimeSpan delta)
        {
            Simulator simulator = systemContainer.simulator;
            ComponentType componentType = world.Schema.GetComponent<IsFontRequest>();
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.Contains(componentType))
                {
                    USpan<uint> entities = chunk.Entities;
                    USpan<IsFontRequest> components = chunk.GetComponents<IsFontRequest>(componentType);
                    for (uint i = 0; i < entities.Length; i++)
                    {
                        ref IsFontRequest request = ref components[i];
                        Entity font = new(world, entities[i]);
                        if (request.status == IsFontRequest.Status.Submitted)
                        {
                            request.status = IsFontRequest.Status.Loading;
                            Trace.WriteLine($"Started searching data for font `{font}` with address `{request.address}`");
                        }

                        if (request.status == IsFontRequest.Status.Loading)
                        {
                            IsFontRequest dataRequest = request;
                            if (TryLoadFont(font, dataRequest, simulator))
                            {
                                Trace.WriteLine($"Font `{font}` has been loaded");

                                //todo: being done this way because reference to the request may have shifted
                                font.SetComponent(dataRequest.BecomeLoaded());
                            }
                            else
                            {
                                request.duration += delta;
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

            PerformOperations(world);
        }

        void ISystem.Finish(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                while (operations.TryPop(out Operation operation))
                {
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
            while (operations.TryPop(out Operation operation))
            {
                operation.Perform(world);
                operation.Dispose();
            }
        }

        /// <summary>
        /// Makes sure that the entity has the latest info about the font.
        /// </summary>
        private readonly bool TryLoadFont(Entity font, IsFontRequest request, Simulator simulator)
        {
            if (!fontFaces.TryGetValue(font, out Face face))
            {
                LoadData message = new(font.world, request.address);
                if (simulator.TryHandleMessage(ref message) != default)
                {
                    if (message.IsLoaded)
                    {
                        USpan<byte> bytes = message.Bytes;
                        face = freeType.Load(bytes);
                        fontFaces.Add(font, face);
                        message.Dispose();
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            uint pixelSize = request.pixelSize;
            face.SetPixelSize(pixelSize, pixelSize);

            Operation operation = new();
            operation.SelectEntity(font);

            //set metrics
            operation.AddOrSetComponent(new FontMetrics(face.Height));

            //set family name
            Span<char> familyName = stackalloc char[128];
            int length = face.CopyFamilyName(familyName);
            operation.AddOrSetComponent(new FontName(familyName[..length]));

            font.TryGetComponent(out IsFont component);
            operation.AddOrSetComponent(new IsFont(component.version + 1, pixelSize));

            LoadGlyphs(font, face, ref operation);
            operations.Push(operation);
            return true;
        }

        private readonly void LoadGlyphs(Entity font, Face face, ref Operation operation)
        {
            if (font.TryGetArray(out USpan<FontGlyph> existingList))
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
                    uint glyphEntity = font.GetReference(oldGlyph.value);
                    operation.SelectEntity(glyphEntity);
                }

                operation.DestroySelected();
            }

            //collect glyph textures for each char
            uint referenceCount = font.References;
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

                operation.CreateArray(kerningBuffer.Slice(0, kerningCount));

                rint glyphReference = (rint)(referenceCount + i + 1);
                operation.ClearSelection();
                operation.SelectEntity(font);
                operation.AddReferenceTowardsPreviouslyCreatedEntity(0);
                glyphsBuffer[i] = new(glyphReference);
            }

            operation.CreateOrSetArray(glyphsBuffer.AsSpan());
        }
    }
}
