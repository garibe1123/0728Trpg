using System;
using UnityEngine;

namespace Trpg.Pawns
{
    /// <summary>
    /// Call of Cthulhu 7판 조사자 기본 능력치 생성 규칙입니다.
    /// 판정용 D100 굴림과 분리하며, 스탯 재굴림에만 사용합니다.
    /// </summary>
    public static class CoCStatGenerationRules
    {
        public const string RerollPointsStatId =
            "coc.reroll.remaining";
        public const int DefaultPlayerRerollPoints = 5;
        public const int MaximumPlayerRerollPoints = 5;

        public static bool TryGetFormula(
            string statId,
            out string expression,
            out int minimum,
            out int maximum)
        {
            expression = string.Empty;
            minimum = 0;
            maximum = 0;

            switch (Normalize(statId))
            {
                case "coc.str":
                case "coc.con":
                case "coc.dex":
                case "coc.app":
                case "coc.pow":
                case "coc.luck":
                    expression = "3D6 × 5";
                    minimum = 15;
                    maximum = 90;
                    return true;

                case "coc.siz":
                case "coc.int":
                case "coc.edu":
                    expression = "(2D6 + 6) × 5";
                    minimum = 40;
                    maximum = 90;
                    return true;

                default:
                    return false;
            }
        }

        public static bool TryRoll(
            string statId,
            out int result,
            out string expression,
            out int minimum,
            out int maximum)
        {
            result = 0;
            if (!TryGetFormula(
                    statId,
                    out expression,
                    out minimum,
                    out maximum))
            {
                return false;
            }

            var normalized = Normalize(statId);
            if (normalized == "coc.siz" ||
                normalized == "coc.int" ||
                normalized == "coc.edu")
            {
                result = (RollD6() + RollD6() + 6) * 5;
                return true;
            }

            result = (RollD6() + RollD6() + RollD6()) * 5;
            return true;
        }

        public static string GetAbbreviation(string statId)
        {
            var normalized = Normalize(statId);
            const string prefix = "coc.";
            if (normalized.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                return normalized.Substring(prefix.Length)
                    .ToUpperInvariant();
            }

            return normalized.ToUpperInvariant();
        }

        public static int ClampPlayerRerollPoints(double value)
        {
            return Mathf.Clamp(
                Mathf.RoundToInt((float)value),
                0,
                MaximumPlayerRerollPoints);
        }

        private static int RollD6()
        {
            return UnityEngine.Random.Range(1, 7);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }
}
