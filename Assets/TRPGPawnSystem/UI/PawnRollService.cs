using System;
using System.Collections.Generic;
using System.Text;

namespace Trpg.Pawns
{
    public enum CheckRollGrade
    {
        Critical,
        ExtremeSuccess,
        HardSuccess,
        Success,
        Failure,
        Fumble
    }

    public readonly struct D100CheckResult
    {
        public D100CheckResult(
            int roll,
            int target,
            CheckRollGrade grade)
        {
            Roll = roll;
            Target = target;
            Grade = grade;
        }

        public int Roll { get; }
        public int Target { get; }
        public CheckRollGrade Grade { get; }
        public bool IsSuccess =>
            Grade != CheckRollGrade.Failure &&
            Grade != CheckRollGrade.Fumble;
    }

    public readonly struct D100CheckThresholds
    {
        internal D100CheckThresholds(int target)
        {
            Target = target;
            ExtremeMaximum = Math.Max(1, target / 5);
            HardMaximum = Math.Max(
                ExtremeMaximum,
                target / 2);
            FumbleMinimum = target < 50
                ? 96
                : 100;
        }

        public int Target { get; }
        public int ExtremeMaximum { get; }
        public int HardMaximum { get; }
        public int FumbleMinimum { get; }

        public CheckRollGrade GetGrade(int roll)
        {
            if (roll < 1 || roll > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roll),
                    roll,
                    "d100 결과는 1~100이어야 합니다.");
            }

            if (roll == 1)
            {
                return CheckRollGrade.Critical;
            }

            if (roll >= FumbleMinimum)
            {
                return CheckRollGrade.Fumble;
            }

            if (roll <= ExtremeMaximum)
            {
                return CheckRollGrade.ExtremeSuccess;
            }

            if (roll <= HardMaximum)
            {
                return CheckRollGrade.HardSuccess;
            }

            return roll <= Target
                ? CheckRollGrade.Success
                : CheckRollGrade.Failure;
        }
    }

    public sealed class EffectRollResult
    {
        private readonly int[] _individualResults;

        internal EffectRollResult(
            int diceCount,
            int sides,
            int modifier,
            int[] individualResults)
        {
            DiceCount = diceCount;
            Sides = sides;
            Modifier = modifier;
            _individualResults = individualResults;

            var total = modifier;
            for (var i = 0; i < individualResults.Length; i++)
            {
                total += individualResults[i];
            }

            Total = total;
        }

        public int DiceCount { get; }
        public int Sides { get; }
        public int Modifier { get; }
        public IReadOnlyList<int> IndividualResults => _individualResults;
        public int Total { get; }
        public int MinimumTotal => DiceCount + Modifier;
        public int MaximumTotal => DiceCount * Sides + Modifier;
        public string Expression =>
            PawnRollService.FormatExpression(
                DiceCount,
                Sides,
                Modifier);

        public string GetBreakdownLabel()
        {
            var builder = new StringBuilder();
            builder.Append('[');
            for (var i = 0; i < _individualResults.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(_individualResults[i]);
            }

            builder.Append(']');
            if (Modifier > 0)
            {
                builder.Append(" + ");
                builder.Append(Modifier);
            }
            else if (Modifier < 0)
            {
                builder.Append(" - ");
                builder.Append(-(long)Modifier);
            }

            builder.Append(" = ");
            builder.Append(Total);
            return builder.ToString();
        }
    }

    public sealed class PawnRollService
    {
        public const int MaximumDiceCount = 1000;
        public const int MaximumDiceSides = 1000000;

        private const int MinimumD100Value = 1;
        private const int MaximumD100Value = 100;
        private readonly Random _random;

        public PawnRollService(int seed)
        {
            _random = new Random(seed);
        }

        public D100CheckResult RollD100(int target)
        {
            var roll = _random.Next(
                MinimumD100Value,
                MaximumD100Value + 1);
            return EvaluateD100(roll, target);
        }

        public D100CheckResult EvaluateD100(int roll, int target)
        {
            if (roll < MinimumD100Value || roll > MaximumD100Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roll),
                    roll,
                    "d100 결과는 1~100이어야 합니다.");
            }

            var clampedTarget = Math.Max(
                MinimumD100Value,
                Math.Min(MaximumD100Value, target));
            var thresholds = GetD100Thresholds(clampedTarget);
            return new D100CheckResult(
                roll,
                clampedTarget,
                thresholds.GetGrade(roll));
        }

        public static D100CheckThresholds GetD100Thresholds(
            int target)
        {
            var clampedTarget = Math.Max(
                MinimumD100Value,
                Math.Min(MaximumD100Value, target));
            return new D100CheckThresholds(clampedTarget);
        }

        public EffectRollResult RollEffect(
            int diceCount,
            int sides,
            int modifier = 0)
        {
            ValidateExpression(diceCount, sides);

            var individualResults = new int[diceCount];
            for (var i = 0; i < diceCount; i++)
            {
                individualResults[i] = _random.Next(1, sides + 1);
            }

            return new EffectRollResult(
                diceCount,
                sides,
                modifier,
                individualResults);
        }

        public static string FormatExpression(
            int diceCount,
            int sides,
            int modifier = 0)
        {
            ValidateExpression(diceCount, sides);

            if (modifier > 0)
            {
                return $"{diceCount}d{sides}+{modifier}";
            }

            return modifier < 0
                ? $"{diceCount}d{sides}{modifier}"
                : $"{diceCount}d{sides}";
        }

        private static void ValidateExpression(
            int diceCount,
            int sides)
        {
            if (diceCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(diceCount),
                    diceCount,
                    "주사위 개수는 1 이상이어야 합니다.");
            }

            if (diceCount > MaximumDiceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(diceCount),
                    diceCount,
                    $"주사위 개수는 {MaximumDiceCount} 이하여야 합니다.");
            }

            if (sides <= 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sides),
                    sides,
                    "주사위 면수는 2 이상이어야 합니다.");
            }

            if (sides > MaximumDiceSides)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sides),
                    sides,
                    $"주사위 면수는 {MaximumDiceSides} 이하여야 합니다.");
            }
        }
    }
}
