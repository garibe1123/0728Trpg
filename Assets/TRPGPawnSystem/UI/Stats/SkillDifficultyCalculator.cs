using System;

namespace Trpg.Domain.Skills
{
    public readonly struct SkillThresholds
    {
        public SkillThresholds(int regular, int hard, int extreme)
        {
            Regular = regular;
            Hard = hard;
            Extreme = extreme;
        }

        public int Regular { get; }
        public int Hard { get; }
        public int Extreme { get; }
    }

    public static class SkillDifficultyCalculator
    {
        public static SkillThresholds Calculate(int regularValue)
        {
            var regular = Math.Max(0, regularValue);
            return new SkillThresholds(
                regular,
                regular / 2,
                regular / 5);
        }
    }
}
