using System;
using System.Collections.Generic;
using System.Globalization;

namespace Trpg.Domain.Dice
{
    public enum CoCCheckKind
    {
        Standard,
        Pushed,
        Opposed
    }

    [Serializable]
    public sealed class CoCCheckRecord
    {
        public string Id;
        public int Sequence;
        public string OccurredAtUtc;
        public CoCCheckKind Kind;
        public string PawnId;
        public string PawnName;
        public string StatId;
        public string StatName;
        public int Target;
        public int OriginalRoll;
        public int FinalRoll;
        public CoCCheckOutcome Outcome;
        public bool IsLuckCheck;
        public string ParentRecordId;
        public string PushedRecordId;
        public string OpposedRecordId;
        public string OpposedGroupId;
        public CoCOpposedResult OpposedResult;
        public int LuckSpent;
        public int LuckBefore = -1;
        public int LuckAfter = -1;

        public CoCCheckRecord Clone()
        {
            return (CoCCheckRecord)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class CoCCheckHistorySnapshot
    {
        public int NextSequence = 1;
        public List<CoCCheckRecord> Records =
            new List<CoCCheckRecord>();
    }

    public sealed class CoCCheckHistoryService
    {
        private readonly List<CoCCheckRecord> _records =
            new List<CoCCheckRecord>();
        private readonly Dictionary<string, CoCCheckRecord> _byId =
            new Dictionary<string, CoCCheckRecord>(
                StringComparer.Ordinal);
        private readonly int _capacity;
        private int _nextSequence = 1;

        public CoCCheckHistoryService(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity));
            }

            _capacity = capacity;
        }

        public event Action Changed;

        public IReadOnlyList<CoCCheckRecord> Records => _records;

        public CoCCheckRecord AddStandard(
            string pawnId,
            string pawnName,
            string statId,
            string statName,
            int target,
            int roll,
            bool isLuckCheck)
        {
            var record = CreateRecord(
                CoCCheckKind.Standard,
                pawnId,
                pawnName,
                statId,
                statName,
                target,
                roll,
                isLuckCheck);
            AddRecord(record);
            return record;
        }

        public bool TryAddPushed(
            string sourceRecordId,
            int roll,
            out CoCCheckRecord pushed,
            out string error)
        {
            pushed = null;
            if (!TryGetRecord(sourceRecordId, out var source))
            {
                error = "강행할 원본 판정 기록을 찾지 못했습니다.";
                return false;
            }

            if (source.Kind == CoCCheckKind.Pushed ||
                !string.IsNullOrWhiteSpace(source.PushedRecordId))
            {
                error = "이미 강행했거나 강행으로 생성된 판정입니다.";
                return false;
            }

            if (source.IsLuckCheck)
            {
                error = "Luck 판정은 강행할 수 없습니다.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(source.OpposedRecordId))
            {
                error = "대항 판정에 연결된 기록은 강행할 수 없습니다.";
                return false;
            }

            if (!CoCCheckRules.CanPush(source.Outcome))
            {
                error = "실패한 판정만 강행할 수 있습니다.";
                return false;
            }

            pushed = CreateRecord(
                CoCCheckKind.Pushed,
                source.PawnId,
                source.PawnName,
                source.StatId,
                source.StatName,
                source.Target,
                roll,
                source.IsLuckCheck);
            pushed.ParentRecordId = source.Id;
            source.PushedRecordId = pushed.Id;
            AddRecord(pushed);
            error = string.Empty;
            return true;
        }

        public bool TryAddOpposed(
            string sourceRecordId,
            string opponentPawnId,
            string opponentPawnName,
            string opponentStatId,
            string opponentStatName,
            int opponentTarget,
            int opponentRoll,
            bool opponentIsLuckCheck,
            out CoCCheckRecord opponent,
            out string error)
        {
            opponent = null;
            if (!TryGetRecord(sourceRecordId, out var source))
            {
                error = "대항할 원본 판정 기록을 찾지 못했습니다.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(source.OpposedRecordId))
            {
                error = "이미 대항 판정에 연결된 기록입니다.";
                return false;
            }

            if (string.Equals(
                    source.PawnId,
                    opponentPawnId,
                    StringComparison.Ordinal))
            {
                error = "같은 Pawn끼리는 대항 판정을 할 수 없습니다.";
                return false;
            }

            opponent = CreateRecord(
                CoCCheckKind.Opposed,
                opponentPawnId,
                opponentPawnName,
                opponentStatId,
                opponentStatName,
                opponentTarget,
                opponentRoll,
                opponentIsLuckCheck);

            var groupId = Guid.NewGuid().ToString("N");
            source.OpposedRecordId = opponent.Id;
            source.OpposedGroupId = groupId;
            opponent.OpposedRecordId = source.Id;
            opponent.OpposedGroupId = groupId;
            opponent.ParentRecordId = source.Id;

            source.OpposedResult = CoCCheckRules.CompareOpposed(
                source.Target,
                source.FinalRoll,
                source.Outcome,
                opponent.Target,
                opponent.FinalRoll,
                opponent.Outcome);
            opponent.OpposedResult =
                CoCCheckRules.Invert(source.OpposedResult);

            AddRecord(opponent);
            error = string.Empty;
            return true;
        }

        public bool TryPreviewLuckSpend(
            string recordId,
            int amount,
            int availableLuck,
            out int changedRoll,
            out CoCCheckOutcome changedOutcome,
            out string error)
        {
            changedRoll = 0;
            changedOutcome = CoCCheckOutcome.Invalid;
            if (!TryGetRecord(recordId, out var record))
            {
                error = "Luck을 적용할 판정 기록을 찾지 못했습니다.";
                return false;
            }

            if (record.IsLuckCheck)
            {
                error = "Luck 판정에는 Luck을 소비할 수 없습니다.";
                return false;
            }

            if (record.Outcome == CoCCheckOutcome.Fumble ||
                record.Outcome == CoCCheckOutcome.CriticalSuccess)
            {
                error = "대실패와 대성공 결과에는 Luck을 적용할 수 없습니다.";
                return false;
            }

            if (amount < 1)
            {
                error = "소비할 Luck을 1 이상 입력해 주세요.";
                return false;
            }

            if (amount > availableLuck)
            {
                error = "현재 Luck보다 많이 소비할 수 없습니다.";
                return false;
            }

            if (amount >= record.FinalRoll)
            {
                error = "판정값을 1보다 낮게 만들 수 없습니다.";
                return false;
            }

            changedRoll = record.FinalRoll - amount;
            changedOutcome = CoCCheckRules.Evaluate(
                record.Target,
                changedRoll);
            if (changedOutcome ==
                CoCCheckOutcome.CriticalSuccess)
            {
                changedRoll = 0;
                changedOutcome = CoCCheckOutcome.Invalid;
                error = "Luck 소비로 대성공을 만들 수 없습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryCommitLuckSpend(
            string recordId,
            int amount,
            int luckBefore,
            int luckAfter,
            int changedRoll,
            CoCCheckOutcome changedOutcome,
            out string error)
        {
            if (!TryGetRecord(recordId, out var record))
            {
                error = "Luck을 적용할 판정 기록을 찾지 못했습니다.";
                return false;
            }

            if (amount < 1 ||
                luckBefore < amount ||
                luckAfter != luckBefore - amount ||
                changedRoll != record.FinalRoll - amount ||
                changedOutcome != CoCCheckRules.Evaluate(
                    record.Target,
                    changedRoll))
            {
                error = "Luck 소비 결과의 무결성 검증에 실패했습니다.";
                return false;
            }

            record.FinalRoll = changedRoll;
            record.Outcome = changedOutcome;
            record.LuckSpent += amount;
            record.LuckBefore =
                record.LuckBefore < 0
                    ? luckBefore
                    : record.LuckBefore;
            record.LuckAfter = luckAfter;

            RecalculateOpposed(record);
            Changed?.Invoke();
            error = string.Empty;
            return true;
        }

        public int GetSuggestedLuckSpend(string recordId)
        {
            return TryGetRecord(recordId, out var record) &&
                   !record.IsLuckCheck
                ? CoCCheckRules.GetSuggestedLuckSpend(
                    record.Target,
                    record.FinalRoll,
                    record.Outcome)
                : 0;
        }

        public bool TryGetRecord(
            string recordId,
            out CoCCheckRecord record)
        {
            record = null;
            return !string.IsNullOrWhiteSpace(recordId) &&
                   _byId.TryGetValue(recordId, out record);
        }

        public CoCCheckHistorySnapshot CreateSnapshot()
        {
            var snapshot = new CoCCheckHistorySnapshot
            {
                NextSequence = _nextSequence
            };
            for (var index = 0; index < _records.Count; index++)
            {
                snapshot.Records.Add(_records[index].Clone());
            }

            return snapshot;
        }

        public bool TryRestore(
            CoCCheckHistorySnapshot snapshot,
            out string error)
        {
            if (snapshot == null)
            {
                _records.Clear();
                _byId.Clear();
                _nextSequence = 1;
                Changed?.Invoke();
                error = string.Empty;
                return true;
            }

            var restored = new List<CoCCheckRecord>();
            var restoredById =
                new Dictionary<string, CoCCheckRecord>(
                    StringComparer.Ordinal);
            var highestSequence = 0;
            var source = snapshot.Records ??
                         new List<CoCCheckRecord>();
            var start = Math.Max(0, source.Count - _capacity);
            for (var index = start; index < source.Count; index++)
            {
                var record = source[index];
                if (!IsValidRecord(record) ||
                    restoredById.ContainsKey(record.Id))
                {
                    error = "판정 기록에 비어 있거나 중복된 ID가 있습니다.";
                    return false;
                }

                var copy = record.Clone();
                restored.Add(copy);
                restoredById.Add(copy.Id, copy);
                highestSequence =
                    Math.Max(highestSequence, copy.Sequence);
            }

            _records.Clear();
            _records.AddRange(restored);
            _byId.Clear();
            foreach (var pair in restoredById)
            {
                _byId.Add(pair.Key, pair.Value);
            }

            _nextSequence = Math.Max(
                highestSequence + 1,
                Math.Max(1, snapshot.NextSequence));
            Changed?.Invoke();
            error = string.Empty;
            return true;
        }

        private CoCCheckRecord CreateRecord(
            CoCCheckKind kind,
            string pawnId,
            string pawnName,
            string statId,
            string statName,
            int target,
            int roll,
            bool isLuckCheck)
        {
            return new CoCCheckRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Sequence = _nextSequence++,
                OccurredAtUtc = DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                Kind = kind,
                PawnId = pawnId ?? string.Empty,
                PawnName = pawnName ?? string.Empty,
                StatId = statId ?? string.Empty,
                StatName = statName ?? string.Empty,
                Target = target,
                OriginalRoll = roll,
                FinalRoll = roll,
                Outcome = CoCCheckRules.Evaluate(target, roll),
                IsLuckCheck = isLuckCheck
            };
        }

        private void AddRecord(CoCCheckRecord record)
        {
            _records.Add(record);
            _byId.Add(record.Id, record);
            TrimOldest();
            Changed?.Invoke();
        }

        private void TrimOldest()
        {
            while (_records.Count > _capacity)
            {
                var oldest = _records[0];
                _records.RemoveAt(0);
                _byId.Remove(oldest.Id);
            }
        }

        private void RecalculateOpposed(CoCCheckRecord changed)
        {
            if (string.IsNullOrWhiteSpace(changed.OpposedRecordId) ||
                !TryGetRecord(
                    changed.OpposedRecordId,
                    out var other))
            {
                return;
            }

            changed.OpposedResult = CoCCheckRules.CompareOpposed(
                changed.Target,
                changed.FinalRoll,
                changed.Outcome,
                other.Target,
                other.FinalRoll,
                other.Outcome);
            other.OpposedResult =
                CoCCheckRules.Invert(changed.OpposedResult);
        }

        private static bool IsValidRecord(CoCCheckRecord record)
        {
            return record != null &&
                   !string.IsNullOrWhiteSpace(record.Id) &&
                   record.Sequence > 0 &&
                   record.Target >= 1 &&
                   record.Target <= 100 &&
                   record.OriginalRoll >= 1 &&
                   record.OriginalRoll <= 100 &&
                   record.FinalRoll >= 1 &&
                   record.FinalRoll <= 100 &&
                   record.Outcome == CoCCheckRules.Evaluate(
                       record.Target,
                       record.FinalRoll);
        }
    }
}
