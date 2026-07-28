using System.Linq;
using NUnit.Framework;
using Trpg.Pawns;

namespace Trpg.Pawns.Tests
{
    public sealed class PawnRollServiceTests
    {
        [TestCase(1, 50, CheckRollGrade.Critical)]
        [TestCase(10, 50, CheckRollGrade.ExtremeSuccess)]
        [TestCase(25, 50, CheckRollGrade.HardSuccess)]
        [TestCase(50, 50, CheckRollGrade.Success)]
        [TestCase(51, 50, CheckRollGrade.Failure)]
        [TestCase(100, 50, CheckRollGrade.Fumble)]
        [TestCase(96, 49, CheckRollGrade.Fumble)]
        public void EvaluateD100_ReturnsExpectedGrade(
            int roll,
            int target,
            CheckRollGrade expected)
        {
            var service = new PawnRollService(seed: 17);

            var result = service.EvaluateD100(roll, target);

            Assert.That(result.Grade, Is.EqualTo(expected));
        }

        [Test]
        public void GetD100Thresholds_Target50_ReturnsVisibleBands()
        {
            var thresholds =
                PawnRollService.GetD100Thresholds(50);

            Assert.That(thresholds.Target, Is.EqualTo(50));
            Assert.That(
                thresholds.ExtremeMaximum,
                Is.EqualTo(10));
            Assert.That(
                thresholds.HardMaximum,
                Is.EqualTo(25));
            Assert.That(
                thresholds.FumbleMinimum,
                Is.EqualTo(100));
            Assert.That(
                thresholds.GetGrade(51),
                Is.EqualTo(CheckRollGrade.Failure));
            Assert.That(
                thresholds.GetGrade(100),
                Is.EqualTo(CheckRollGrade.Fumble));
        }

        [Test]
        public void GetD100Thresholds_LowTargetWidensFumbleBand()
        {
            var thresholds =
                PawnRollService.GetD100Thresholds(49);

            Assert.That(
                thresholds.FumbleMinimum,
                Is.EqualTo(96));
            Assert.That(
                thresholds.GetGrade(95),
                Is.EqualTo(CheckRollGrade.Failure));
            Assert.That(
                thresholds.GetGrade(96),
                Is.EqualTo(CheckRollGrade.Fumble));
        }

        [Test]
        public void RollEffect_RollsEveryDieAndAddsModifier()
        {
            var service = new PawnRollService(seed: 12345);

            var result = service.RollEffect(
                diceCount: 3,
                sides: 6,
                modifier: 2);

            Assert.That(result.IndividualResults.Count, Is.EqualTo(3));
            Assert.That(
                result.IndividualResults,
                Has.All.InRange(1, 6));
            Assert.That(
                result.Total,
                Is.EqualTo(
                    result.IndividualResults.Sum() + 2));
            Assert.That(result.MinimumTotal, Is.EqualTo(5));
            Assert.That(result.MaximumTotal, Is.EqualTo(20));
        }

        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var first = new PawnRollService(seed: 777);
            var second = new PawnRollService(seed: 777);

            var firstResult = first.RollEffect(4, 8, -1);
            var secondResult = second.RollEffect(4, 8, -1);

            Assert.That(
                firstResult.IndividualResults,
                Is.EqualTo(secondResult.IndividualResults));
            Assert.That(
                firstResult.Total,
                Is.EqualTo(secondResult.Total));
        }
    }
}
