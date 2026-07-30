using System;

namespace Trpg.Domain.Dice
{
    public enum CoCCheckOutcome
    {
        Invalid,
        Fumble,
        Failure,
        Success,
        HardSuccess,
        ExtremeSuccess,
        CriticalSuccess
    }

    public enum CoCOpposedResult
    {
        None,
        Win,
        Lose,
        Draw,
        NoWinner
    }

    public static class CoCCheckRules
    {
        public static CoCCheckOutcome Evaluate(int target, int roll)
        {
            if (target < 1 || target > 100 ||
                roll < 1 || roll > 100)
            {
                return CoCCheckOutcome.Invalid;
            }

            if (roll == 100 || (target < 50 && roll >= 96))
            {
                return CoCCheckOutcome.Fumble;
            }

            if (roll == 1)
            {
                return CoCCheckOutcome.CriticalSuccess;
            }

            if (roll <= target / 5)
            {
                return CoCCheckOutcome.ExtremeSuccess;
            }

            if (roll <= target / 2)
            {
                return CoCCheckOutcome.HardSuccess;
            }

            return roll <= target
                ? CoCCheckOutcome.Success
                : CoCCheckOutcome.Failure;
        }

        public static bool IsSuccess(CoCCheckOutcome outcome)
        {
            return outcome >= CoCCheckOutcome.Success;
        }

        public static bool CanPush(CoCCheckOutcome outcome)
        {
            return outcome == CoCCheckOutcome.Failure ||
                   outcome == CoCCheckOutcome.Fumble;
        }

        public static int GetSuggestedLuckSpend(
            int target,
            int finalRoll,
            CoCCheckOutcome outcome)
        {
            if (target < 1 ||
                finalRoll <= 1 ||
                outcome == CoCCheckOutcome.Invalid ||
                outcome == CoCCheckOutcome.Fumble ||
                outcome == CoCCheckOutcome.CriticalSuccess)
            {
                return 0;
            }

            int nextThreshold;
            switch (outcome)
            {
                case CoCCheckOutcome.Failure:
                    nextThreshold = target;
                    break;
                case CoCCheckOutcome.Success:
                    nextThreshold = target / 2;
                    break;
                case CoCCheckOutcome.HardSuccess:
                    nextThreshold = target / 5;
                    break;
                case CoCCheckOutcome.ExtremeSuccess:
                    return 0;
                default:
                    return 0;
            }

            return Math.Max(0, finalRoll - nextThreshold);
        }

        public static CoCOpposedResult CompareOpposed(
            int leftTarget,
            int leftRoll,
            CoCCheckOutcome leftOutcome,
            int rightTarget,
            int rightRoll,
            CoCCheckOutcome rightOutcome)
        {
            var leftSuccess = IsSuccess(leftOutcome);
            var rightSuccess = IsSuccess(rightOutcome);
            if (!leftSuccess && !rightSuccess)
            {
                return CoCOpposedResult.NoWinner;
            }

            if (leftSuccess != rightSuccess)
            {
                return leftSuccess
                    ? CoCOpposedResult.Win
                    : CoCOpposedResult.Lose;
            }

            var outcomeComparison =
                leftOutcome.CompareTo(rightOutcome);
            if (outcomeComparison != 0)
            {
                return outcomeComparison > 0
                    ? CoCOpposedResult.Win
                    : CoCOpposedResult.Lose;
            }

            if (leftTarget != rightTarget)
            {
                return leftTarget > rightTarget
                    ? CoCOpposedResult.Win
                    : CoCOpposedResult.Lose;
            }

            if (leftRoll != rightRoll)
            {
                return leftRoll < rightRoll
                    ? CoCOpposedResult.Win
                    : CoCOpposedResult.Lose;
            }

            return CoCOpposedResult.Draw;
        }

        public static CoCOpposedResult Invert(
            CoCOpposedResult result)
        {
            switch (result)
            {
                case CoCOpposedResult.Win:
                    return CoCOpposedResult.Lose;
                case CoCOpposedResult.Lose:
                    return CoCOpposedResult.Win;
                default:
                    return result;
            }
        }
    }
}
