using FreeTypeSharp;

namespace FreeType
{
    public readonly struct FreeTypeMetrics
    {
        public readonly ushort xppem;
        public readonly ushort yppem;
        public readonly int xScale;
        public readonly int yScale;
        public readonly int ascender;
        public readonly int descender;
        public readonly int height;
        public readonly int maxAdvance;

        internal unsafe FreeTypeMetrics(FT_Size_Metrics_ metrics)
        {
            xppem = metrics.x_ppem;
            yppem = metrics.y_ppem;
            xScale = (int)metrics.x_scale;
            yScale = (int)metrics.y_scale;
            ascender = (int)metrics.ascender;
            descender = (int)metrics.descender;
            height = (int)metrics.height;
            maxAdvance = (int)metrics.max_advance;
        }
    }
}