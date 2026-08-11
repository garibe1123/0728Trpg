using System;

namespace Trpg.Pawns
{
    /// <summary>
    /// 월드 Y 좌표를 2D Order in Layer 값으로 변환하는 공용 Resident입니다.
    /// 화면 아래쪽(-Y)에 있을수록 더 큰 Order를 갖습니다.
    /// </summary>
    public static class WorldSortOrder
    {
        public const float LevelsPerWorldUnit = 10f;
        public const int MinimumOrder = -32000;
        public const int MaximumOrder = 32000;

        public static int FromWorldY(float worldY)
        {
            var scaled = -(double)worldY * LevelsPerWorldUnit;

            if (double.IsNaN(scaled))
                return 0;
            if (scaled <= MinimumOrder)
                return MinimumOrder;
            if (scaled >= MaximumOrder)
                return MaximumOrder;

            return (int)Math.Round(
                scaled,
                MidpointRounding.AwayFromZero);
        }
    }
}
