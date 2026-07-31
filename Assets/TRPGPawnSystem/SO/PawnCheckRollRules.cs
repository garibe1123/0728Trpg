using System;

namespace Trpg.Pawns
{
    public enum PawnCheckConfirmationKind
    {
        None,
        Challenge,
        Luck
    }

    public enum PawnCheckDifficulty
    {
        Regular,
        Hard,
        Extreme
    }

    public enum PawnRollSourceKind
    {
        Stat,
        Skill
    }

    public enum PawnCheckOutcomeGrade
    {
        Critical,
        ExtremeSuccess,
        HardSuccess,
        Success,
        Failure,
        Fumble
    }

    public readonly struct PawnCheckSourceData
    {
        public PawnCheckSourceData(
            string statId,
            string displayName,
            PawnRollSourceKind sourceKind,
            int regular)
            : this(
                statId,
                displayName,
                sourceKind,
                regular,
                PawnCheckRollRules.GetTarget(
                    regular,
                    PawnCheckDifficulty.Hard),
                PawnCheckRollRules.GetTarget(
                    regular,
                    PawnCheckDifficulty.Extreme))
        {
        }

        public PawnCheckSourceData(
            string statId,
            string displayName,
            PawnRollSourceKind sourceKind,
            int regular,
            int hard,
            int extreme)
        {
            StatId = statId?.Trim() ?? string.Empty;
            DisplayName = displayName?.Trim() ?? string.Empty;
            SourceKind = sourceKind;
            Regular = PawnCheckRollRules.ClampTarget(regular);
            Hard = PawnCheckRollRules.ClampTarget(hard);
            Extreme = PawnCheckRollRules.ClampTarget(extreme);
        }

        public string StatId { get; }
        public string DisplayName { get; }
        public PawnRollSourceKind SourceKind { get; }
        public int Regular { get; }
        public int Hard { get; }
        public int Extreme { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(StatId) &&
            !string.IsNullOrWhiteSpace(DisplayName) &&
            Regular >= PawnCheckRollRules.MinimumTarget;

        public int GetTarget(PawnCheckDifficulty difficulty)
        {
            switch (difficulty)
            {
                case PawnCheckDifficulty.Hard:
                    return Hard;
                case PawnCheckDifficulty.Extreme:
                    return Extreme;
                default:
                    return Regular;
            }
        }

        public PawnCheckSourceData WithRegular(int regular)
        {
            return new PawnCheckSourceData(
                StatId,
                DisplayName,
                SourceKind,
                regular);
        }
    }

    public readonly struct PawnCheckEvaluation
    {
        public PawnCheckEvaluation(
            PawnCheckSourceData source,
            PawnCheckDifficulty difficulty,
            int roll,
            int requiredTarget,
            PawnCheckOutcomeGrade grade,
            bool isSuccessForDifficulty,
            int luckCost,
            bool canChallenge,
            bool canSpendLuck)
        {
            Source = source;
            Difficulty = difficulty;
            Roll = roll;
            RequiredTarget = requiredTarget;
            Grade = grade;
            IsSuccessForDifficulty = isSuccessForDifficulty;
            LuckCost = luckCost;
            CanChallenge = canChallenge;
            CanSpendLuck = canSpendLuck;
        }

        public PawnCheckSourceData Source { get; }
        public PawnCheckDifficulty Difficulty { get; }
        public int Roll { get; }
        public int RequiredTarget { get; }
        public PawnCheckOutcomeGrade Grade { get; }
        public bool IsSuccessForDifficulty { get; }
        public int LuckCost { get; }
        public bool CanChallenge { get; }
        public bool CanSpendLuck { get; }
    }

    /// <summary>
    /// CoC D100 판정, 선택 난이도, 운 비용과 재굴림 가능 여부를 계산합니다.
    /// Unity API에 의존하지 않습니다.
    /// </summary>
    public static class PawnCheckRollRules
    {
        public const int MinimumTarget = 1;
        public const int MaximumTarget = 100;

        private const int CriticalRoll = 1;
        private const int FumbleRoll = 100;
        private const int LowSkillFumbleThreshold = 50;
        private const int LowSkillFumbleMinimum = 96;

        public static int ClampTarget(int value)
        {
            return Math.Max(
                MinimumTarget,
                Math.Min(MaximumTarget, value));
        }

        public static int GetTarget(
            int regular,
            PawnCheckDifficulty difficulty)
        {
            var clamped = ClampTarget(regular);
            switch (difficulty)
            {
                case PawnCheckDifficulty.Hard:
                    return Math.Max(MinimumTarget, clamped / 2);
                case PawnCheckDifficulty.Extreme:
                    return Math.Max(MinimumTarget, clamped / 5);
                default:
                    return clamped;
            }
        }

        public static PawnCheckEvaluation Evaluate(
            PawnCheckSourceData source,
            PawnCheckDifficulty difficulty,
            int roll,
            bool challengeAlreadyUsed)
        {
            if (!source.IsValid)
            {
                throw new ArgumentException(
                    "판정 원본이 올바르지 않습니다.",
                    nameof(source));
            }

            if (roll < MinimumTarget || roll > MaximumTarget)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roll),
                    roll,
                    "D100 결과는 1~100이어야 합니다.");
            }

            var requiredTarget = source.GetTarget(difficulty);
            var grade = EvaluateGrade(roll, source.Regular);
            var isSuccess =
                grade == PawnCheckOutcomeGrade.Critical ||
                grade != PawnCheckOutcomeGrade.Fumble &&
                roll <= requiredTarget;
            var luckCost =
                isSuccess || grade == PawnCheckOutcomeGrade.Fumble
                    ? 0
                    : Math.Max(0, roll - requiredTarget);
            var canRetry =
                !isSuccess &&
                grade != PawnCheckOutcomeGrade.Fumble &&
                !challengeAlreadyUsed;

            return new PawnCheckEvaluation(
                source,
                difficulty,
                roll,
                requiredTarget,
                grade,
                isSuccess,
                luckCost,
                canRetry,
                canRetry && luckCost > 0);
        }

        public static string GetDifficultyLabel(
            PawnCheckDifficulty difficulty)
        {
            switch (difficulty)
            {
                case PawnCheckDifficulty.Hard:
                    return "어려움";
                case PawnCheckDifficulty.Extreme:
                    return "극단적";
                default:
                    return "일반";
            }
        }

        public static string GetGradeLabel(
            PawnCheckOutcomeGrade grade)
        {
            switch (grade)
            {
                case PawnCheckOutcomeGrade.Critical:
                    return "대성공";
                case PawnCheckOutcomeGrade.ExtremeSuccess:
                    return "극단적 성공";
                case PawnCheckOutcomeGrade.HardSuccess:
                    return "어려운 성공";
                case PawnCheckOutcomeGrade.Success:
                    return "일반 성공";
                case PawnCheckOutcomeGrade.Fumble:
                    return "대실패";
                default:
                    return "실패";
            }
        }

        private static PawnCheckOutcomeGrade EvaluateGrade(
            int roll,
            int regular)
        {
            if (roll == CriticalRoll)
                return PawnCheckOutcomeGrade.Critical;

            var isFumble =
                roll == FumbleRoll ||
                regular < LowSkillFumbleThreshold &&
                roll >= LowSkillFumbleMinimum;
            if (isFumble)
                return PawnCheckOutcomeGrade.Fumble;

            if (roll <= GetTarget(
                    regular,
                    PawnCheckDifficulty.Extreme))
            {
                return PawnCheckOutcomeGrade.ExtremeSuccess;
            }

            if (roll <= GetTarget(
                    regular,
                    PawnCheckDifficulty.Hard))
            {
                return PawnCheckOutcomeGrade.HardSuccess;
            }

            return roll <= ClampTarget(regular)
                ? PawnCheckOutcomeGrade.Success
                : PawnCheckOutcomeGrade.Failure;
        }
    }
}
