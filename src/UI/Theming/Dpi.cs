using System;
using System.Drawing;

namespace MediaDownloader
{
    // All layout constants are authored at 96 DPI. Fonts remain in points;
    // Windows converts those to device pixels using the same system DPI.
    internal static class Dpi
    {
        private static readonly float factor = ReadScale();
        internal static float Factor { get { return factor; } }
        private static float ReadScale()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                return screen.DpiX / 96f;
        }
        internal static int Px(int value) { return (int)Math.Round(value * factor); }
        internal static float Px(float value) { return value * factor; }
    }
}
