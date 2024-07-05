using FreeTypeSharp;
using System;

namespace FreeType
{
    public unsafe readonly struct FreeTypeGlyph
    {
        private readonly nint value;

        public readonly int Left
        {
            get
            {
                FT_GlyphSlotRec_* glyph = (FT_GlyphSlotRec_*)value;
                return glyph->bitmap_left;
            }
        }

        public readonly int Top
        {
            get
            {
                FT_GlyphSlotRec_* glyph = (FT_GlyphSlotRec_*)value;
                return glyph->bitmap_top;
            }
        }

        public readonly uint Width
        {
            get
            {
                FT_GlyphSlotRec_* glyph = (FT_GlyphSlotRec_*)value;
                return glyph->bitmap.width;
            }
        }

        public readonly int HorizontalAdvance
        {
            get
            {
                FT_GlyphSlotRec_* glyph = (FT_GlyphSlotRec_*)value;
                return (int)(glyph->advance.x);
            }
        }

        public readonly int VerticalAdvance
        {
            get
            {
                FT_GlyphSlotRec_* glyph = (FT_GlyphSlotRec_*)value;
                return (int)(glyph->advance.y);
            }
        }

        public readonly uint Height
        {
            get
            {
                FT_GlyphSlotRec_* glyph = (FT_GlyphSlotRec_*)value;
                return glyph->bitmap.rows;
            }
        }

        public readonly ReadOnlySpan<byte> Bitmap
        {
            get
            {
                FT_GlyphSlotRec_* glyph = (FT_GlyphSlotRec_*)value;
                return new(glyph->bitmap.buffer, (int)(glyph->bitmap.width * glyph->bitmap.rows));
            }
        }

        internal FreeTypeGlyph(nint address)
        {
            value = address;
        }
    }
}