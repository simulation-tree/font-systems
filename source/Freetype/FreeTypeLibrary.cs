using FreeTypeSharp;
using System;
using System.Diagnostics;
using static FreeTypeSharp.FT;

namespace FreeType
{
    public unsafe struct FreeTypeLibrary : IDisposable
    {
        private nint value;

        public readonly bool IsDisposed => value == 0;

        public FreeTypeLibrary()
        {
            FT_LibraryRec_* library;
            FT_Error error = FT_Init_FreeType(&library);
            if (error != FT_Error.FT_Err_Ok)
            {
                throw new Exception($"Failed to initialize FreeType library: {error}");
            }

            this.value = (nint)library;
        }

        [Conditional("DEBUG")]
        private readonly void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(FreeTypeLibrary));
            }
        }

        public readonly void Dispose()
        {
            ThrowIfDisposed();
            FT_Done_FreeType((FT_LibraryRec_*)value);
        }

        public readonly FreeTypeFont Load(ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = bytes)
            {
                FT_FaceRec_* face;
                FT_Error error = FT_New_Memory_Face((FT_LibraryRec_*)value, ptr, bytes.Length, 0, &face);
                if (error != FT_Error.FT_Err_Ok)
                {
                    throw new Exception($"Failed to load font: {error}");
                }

                return new((nint)face);
            }
        }
    }
}