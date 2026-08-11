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

    public sealed class D100ModifiedRollResult
    {
        private readonly int[] _tensDice;
        private readonly int[] _candidateRolls;

        internal D100ModifiedRollResult(
            int target,
            int unitsDie,
            int[] tensDice,
            int bonusPenaltyLevel,
            int selectedIndex,
            D100CheckResult selectedResult)
        {
            Target = target;
            UnitsDie = unitsDie;
            _tensDice = tensDice ?? Array.Empty<int>();
            BonusPenaltyLevel = Math.Max(-2, Math.Min(2, bonusPenaltyLevel));
            SelectedIndex = selectedIndex;
            SelectedResult = selectedResult;
            _candidateRolls = new int[_tensDice.Length];
            for (var index = 0; index < _tensDice.Length; index++)
                _candidateRolls[index] = ComposeRoll(_tensDice[index], unitsDie);
        }

        public int Target { get; }
        public int UnitsDie { get; }
        public IReadOnlyList<int> TensDice => _tensDice;
        public IReadOnlyList<int> CandidateRolls => _candidateRolls;
        public int BonusPenaltyLevel { get; }
        public int SelectedIndex { get; }
        public D100CheckResult SelectedResult { get; }
        public int BaseRoll => _candidateRolls.Length > 0
            ? _candidateRolls[0]
            : SelectedResult.Roll;
        public int Roll => SelectedResult.Roll;
        public bool HasAdditionalTensDice => _tensDice.Length > 1;

        public string ModifierLabel
        {
            get
            {
                if (BonusPenaltyLevel > 0)
                    return $"보너스 {BonusPenaltyLevel}";
                if (BonusPenaltyLevel < 0)
                    return $"페널티 {-BonusPenaltyLevel}";
                return "보통";
            }
        }

        public string GetCandidateLabel()
        {
            var builder = new StringBuilder();
            builder.Append("일의 자리 ");
            builder.Append(UnitsDie);
            builder.Append(" / 후보 ");
            for (var index = 0; index < _candidateRolls.Length; index++)
            {
                if (index > 0)
                    builder.Append(", ");
                if (index == SelectedIndex)
                    builder.Append('[');
                builder.Append(_candidateRolls[index]);
                if (index == SelectedIndex)
                    builder.Append(']');
            }
            builder.Append(" / ");
            builder.Append(ModifierLabel);
            return builder.ToString();
        }

        private static int ComposeRoll(int tensDie, int unitsDie)
        {
            var value = tensDie * 10 + unitsDie;
            return value == 0 ? 100 : value;
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
        public int MinimumTotal => DiceCount == 0
            ? Modifier
            : DiceCount + Modifier;
        public int MaximumTotal => DiceCount == 0
            ? Modifier
            : DiceCount * Sides + Modifier;
        public string Expression => DiceCount == 0
            ? Modifier.ToString()
            : PawnRollService.FormatExpression(
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
            return RollD100Modified(target, 0).SelectedResult;
        }

        public D100ModifiedRollResult RollD100Modified(
            int target,
            int bonusPenaltyLevel)
        {
            var clampedLevel = Math.Max(-2, Math.Min(2, bonusPenaltyLevel));
            var unitsDie = _random.Next(0, 10);
            var tensCount = 1 + Math.Abs(clampedLevel);
            var tensDice = new int[tensCount];
            var selectedIndex = 0;
            var selectedRoll = 0;

            for (var index = 0; index < tensCount; index++)
            {
                tensDice[index] = _random.Next(0, 10);
                var candidate = ComposePercentileRoll(
                    tensDice[index],
                    unitsDie);
                if (index == 0 ||
                    clampedLevel > 0 && candidate < selectedRoll ||
                    clampedLevel < 0 && candidate > selectedRoll)
                {
                    selectedIndex = index;
                    selectedRoll = candidate;
                }
            }

            var evaluated = EvaluateD100(selectedRoll, target);
            return new D100ModifiedRollResult(
                evaluated.Target,
                unitsDie,
                tensDice,
                clampedLevel,
                selectedIndex,
                evaluated);
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

        public static bool TryParseExpression(
            string expression,
            out int diceCount,
            out int sides,
            out int modifier)
        {
            diceCount = 0;
            sides = 0;
            modifier = 0;
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            var normalized = expression
                .Trim()
                .Replace(" ", string.Empty)
                .ToLowerInvariant();
            if (int.TryParse(normalized, out var constant))
            {
                if (constant < 0 || constant > 1000000)
                    return false;

                modifier = constant;
                return true;
            }

            var dIndex = normalized.IndexOf('d');
            if (dIndex <= 0 || dIndex >= normalized.Length - 1)
                return false;

            var modifierIndex = -1;
            for (var index = dIndex + 1; index < normalized.Length; index++)
            {
                if (normalized[index] == '+' || normalized[index] == '-')
                {
                    modifierIndex = index;
                    break;
                }
            }

            var countText = normalized.Substring(0, dIndex);
            var sideText = modifierIndex >= 0
                ? normalized.Substring(dIndex + 1, modifierIndex - dIndex - 1)
                : normalized.Substring(dIndex + 1);
            var modifierText = modifierIndex >= 0
                ? normalized.Substring(modifierIndex)
                : string.Empty;

            if (!int.TryParse(countText, out diceCount) ||
                !int.TryParse(sideText, out sides) ||
                (!string.IsNullOrEmpty(modifierText) &&
                 !int.TryParse(modifierText, out modifier)))
            {
                return false;
            }

            return diceCount >= 1 &&
                   diceCount <= MaximumDiceCount &&
                   sides >= 2 &&
                   sides <= MaximumDiceSides;
        }

        public EffectRollResult RollExpression(string expression)
        {
            if (!TryParseExpression(
                    expression,
                    out var diceCount,
                    out var sides,
                    out var modifier))
            {
                throw new FormatException(
                    $"유효하지 않은 주사위 식입니다: {expression}");
            }

            if (diceCount == 0)
            {
                return new EffectRollResult(
                    0,
                    2,
                    modifier,
                    Array.Empty<int>());
            }

            return RollEffect(diceCount, sides, modifier);
        }

        private static int ComposePercentileRoll(
            int tensDie,
            int unitsDie)
        {
            var value = tensDie * 10 + unitsDie;
            return value == 0 ? 100 : value;
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
