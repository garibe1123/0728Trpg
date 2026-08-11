using UnityEngine;

namespace Trpg.Pawns
{
    public static class PixelSnap
    {
        public const int DefaultPixelsPerUnit = 16;

        public static int NormalizePixelsPerUnit(int pixelsPerUnit)
        {
            return Mathf.Max(1, pixelsPerUnit);
        }

        public static float PixelsToUnits(
            int pixels,
            int pixelsPerUnit = DefaultPixelsPerUnit)
        {
            return pixels / (float)NormalizePixelsPerUnit(pixelsPerUnit);
        }

        public static Vector2 PixelsToUnits(
            Vector2Int pixels,
            int pixelsPerUnit = DefaultPixelsPerUnit)
        {
            var ppu = NormalizePixelsPerUnit(pixelsPerUnit);
            return new Vector2(
                pixels.x / (float)ppu,
                pixels.y / (float)ppu);
        }

        public static float SnapUnit(
            float value,
            int pixelsPerUnit = DefaultPixelsPerUnit)
        {
            var ppu = NormalizePixelsPerUnit(pixelsPerUnit);
            return Mathf.Round(value * ppu) / ppu;
        }

        public static Vector2 SnapWorld(
            Vector2 value,
            int pixelsPerUnit = DefaultPixelsPerUnit)
        {
            return new Vector2(
                SnapUnit(value.x, pixelsPerUnit),
                SnapUnit(value.y, pixelsPerUnit));
        }

        public static Vector3 SnapWorld(
            Vector3 value,
            int pixelsPerUnit = DefaultPixelsPerUnit)
        {
            return new Vector3(
                SnapUnit(value.x, pixelsPerUnit),
                SnapUnit(value.y, pixelsPerUnit),
                0f);
        }
    }
}
