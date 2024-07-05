using FreeTypeSharp;
using System;
using System.Diagnostics;
using static FreeTypeSharp.FT;

namespace FreeType
{
    public unsafe struct FreeTypeFont : IDisposable
    {
        private nint value;

        public readonly bool IsDisposed => value == 0;
        public readonly uint GlyphCout
        {
            get
            {
                FT_FaceRec_* face = (FT_FaceRec_*)value;
                return (uint)face->num_glyphs;
            }
        }

        public readonly FreeTypeMetrics Metrics
        {
            get
            {
                FT_FaceRec_* face = (FT_FaceRec_*)value;
                return new FreeTypeMetrics(face->size->metrics);
            }
        }

        internal FreeTypeFont(nint address)
        {
            value = address;
        }

        [Conditional("DEBUG")]
        private readonly void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(FreeTypeFont));
            }
        }

        public readonly void Dispose()
        {
            ThrowIfDisposed();
            FT_Done_Face((FT_FaceRec_*)value);
        }

        public readonly int CopyFamilyName(Span<char> buffer)
        {
            ThrowIfDisposed();
            FT_FaceRec_* face = (FT_FaceRec_*)value;
            int length = 0;
            byte* source = face->family_name;
            fixed (char* destination = buffer)
            {
                while (length < buffer.Length)
                {
                    byte value = *source;
                    if (value == 0)
                    {
                        break;
                    }

                    destination[length++] = (char)value;
                    source++;
                }
            }

            return length;
        }

        public readonly int CopyStyleName(Span<char> buffer)
        {
            ThrowIfDisposed();
            FT_FaceRec_* face = (FT_FaceRec_*)value;
            int length = 0;
            byte* source = face->style_name;
            fixed (char* destination = buffer)
            {
                while (length < buffer.Length)
                {
                    byte value = *source;
                    if (value == 0)
                    {
                        break;
                    }

                    destination[length++] = (char)value;
                    source++;
                }
            }

            return length;
        }

        public readonly void SetPixelSize(uint width, uint height)
        {
            ThrowIfDisposed();
            FT_FaceRec_* face = (FT_FaceRec_*)value;
            FT_Error error = FT_Set_Pixel_Sizes(face, width, height);
            if (error != FT_Error.FT_Err_Ok)
            {
                throw new Exception($"Failed to set pixel size: {error}");
            }
        }

        public readonly uint GetCharIndex(char charcode)
        {
            ThrowIfDisposed();
            FT_FaceRec_* face = (FT_FaceRec_*)value;
            return FT_Get_Char_Index(face, charcode);
        }

        public readonly FreeTypeGlyph LoadGlyph(uint index)
        {
            ThrowIfDisposed();
            FT_Error error = FT_Load_Glyph((FT_FaceRec_*)value, index, FT_LOAD.FT_LOAD_DEFAULT);
            if (error != FT_Error.FT_Err_Ok)
            {
                throw new Exception($"Failed to load glyph: {error}");
            }

            FT_FaceRec_* face = (FT_FaceRec_*)value;
            FT_Glyph_Format_ format = face->glyph->format;
            if (format != FT_Glyph_Format_.FT_GLYPH_FORMAT_BITMAP)
            {
                error = FT_Render_Glyph(face->glyph, FT_Render_Mode_.FT_RENDER_MODE_NORMAL);
                if (error != FT_Error.FT_Err_Ok)
                {
                    throw new Exception($"Failed to render glyph: {error}");
                }
            }

            return new FreeTypeGlyph((nint)face->glyph);
        }

        public readonly (int x, int y) GetKerning(char left, char right)
        {
            ThrowIfDisposed();
            FT_Vector_ kerning;
            FT_Kerning_Mode_ mode = FT_Kerning_Mode_.FT_KERNING_DEFAULT;
            FT_Error error = FT_Get_Kerning((FT_FaceRec_*)value, left, right, mode, &kerning);
            if (error != FT_Error.FT_Err_Ok)
            {
                throw new Exception($"Failed to get kerning: {error}");
            }

            return ((int)kerning.x, (int)kerning.y);
        }
    }
}