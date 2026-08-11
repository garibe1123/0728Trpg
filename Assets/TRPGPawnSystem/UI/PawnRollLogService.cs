using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    public enum RollVisibility
    {
        Public = 0,
        RollerAndGameMaster = 1
    }

    [Serializable]
    public sealed class PawnRollLogEntrySnapshot
    {
        public long Sequence;
        public string TimestampUtc;
        public int Kind;
        public string PawnDefinitionId;
        public string PawnName;
        public string Title;
        public string Expression;
        public int Value;
        public string Result;
        public string Detail;
        public int Visibility;
    }

    [Serializable]
    public sealed class PawnRollLogSnapshot
    {
        public long NextSequence;
        public List<PawnRollLogEntrySnapshot> Entries =
            new List<PawnRollLogEntrySnapshot>();
    }

    public enum PawnRollLogKind
    {
        PureD100,
        Check,
        Challenge,
        Effect,
        Luck,
        Chat,
        System
    }

    public readonly struct PawnRollLogEntry
    {
        public PawnRollLogEntry(
            long sequence,
            DateTime timestampUtc,
            PawnRollLogKind kind,
            InteractivePawn pawn,
            string pawnName,
            string title,
            string expression,
            int value,
            string result,
            string detail,
            RollVisibility visibility = RollVisibility.Public)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            Kind = kind;
            Pawn = pawn;
            PawnName = pawnName ?? string.Empty;
            Title = title ?? string.Empty;
            Expression = expression ?? string.Empty;
            Value = value;
            Result = result ?? string.Empty;
            Detail = detail ?? string.Empty;
            Visibility = visibility;
        }

        public long Sequence { get; }
        public DateTime TimestampUtc { get; }
        public PawnRollLogKind Kind { get; }
        public InteractivePawn Pawn { get; }
        public string PawnName { get; }
        public string Title { get; }
        public string Expression { get; }
        public int Value { get; }
        public string Result { get; }
        public string Detail { get; }
        public RollVisibility Visibility { get; }
        public bool IsSecret =>
            Visibility == RollVisibility.RollerAndGameMaster;

        public string ToDisplayString()
        {
            var owner = string.IsNullOrWhiteSpace(PawnName)
                ? "시스템"
                : PawnName;

            if (Kind == PawnRollLogKind.Chat)
                return $"[{Sequence:0000}] {owner}: {Detail}";

            if (Kind == PawnRollLogKind.System)
                return $"[{Sequence:0000}] {owner} · {Title} · {Detail}";

            var visibility = IsSecret ? "[비밀] " : string.Empty;
            return $"[{Sequence:0000}] {visibility}{owner} · {Title} · " +
                   $"{Expression} → {Value} · {Result} · {Detail}";
        }
    }

    /// <summary>
    /// 굴림과 채팅 기록을 플레이 세션 단위로 보관합니다.
    /// 턴 리셋과 무관하게 기록은 유지됩니다.
    /// </summary>
    public static class PawnRollLogService
    {
        private const int MaximumEntries = 5000;
        private const int MaximumChatLength = 500;

        private static readonly List<PawnRollLogEntry> EntriesInternal =
            new List<PawnRollLogEntry>();

        private static long _nextSequence;
        private static bool _restoringSnapshot;

        public static bool IsRestoringSnapshot => _restoringSnapshot;

        public static event Action<PawnRollLogEntry> EntryAdded;
        public static event Action EntriesCleared;

        public static IReadOnlyList<PawnRollLogEntry> Entries =>
            EntriesInternal;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeSession()
        {
            EntriesInternal.Clear();
            _nextSequence = 0;
            EntryAdded = null;
            EntriesCleared = null;
            _restoringSnapshot = false;
        }

        public static PawnRollLogEntry RecordRoll(
            PawnRollLogKind kind,
            InteractivePawn pawn,
            string title,
            string expression,
            int value,
            string result,
            string detail,
            RollVisibility visibility = RollVisibility.Public)
        {
            var entry = new PawnRollLogEntry(
                ++_nextSequence,
                DateTime.UtcNow,
                kind,
                pawn,
                ResolvePawnName(pawn),
                title,
                expression,
                value,
                result,
                detail,
                visibility);
            Add(entry);
            return entry;
        }

        public static PawnRollLogEntry RecordRemote(
            PawnRollLogKind kind,
            InteractivePawn pawn,
            string pawnName,
            string title,
            string expression,
            int value,
            string result,
            string detail,
            RollVisibility visibility = RollVisibility.Public)
        {
            var entry = new PawnRollLogEntry(
                ++_nextSequence,
                DateTime.UtcNow,
                kind,
                pawn,
                pawnName,
                title,
                expression,
                value,
                result,
                detail,
                visibility);
            Add(entry);
            return entry;
        }

        public static PawnRollLogEntry RecordChat(
            InteractivePawn pawn,
            string speakerName,
            string message)
        {
            var normalized = NormalizeChat(message);
            if (string.IsNullOrWhiteSpace(normalized))
                return default;

            var resolvedSpeaker = string.IsNullOrWhiteSpace(speakerName)
                ? ResolvePawnName(pawn)
                : speakerName.Trim();
            if (string.IsNullOrWhiteSpace(resolvedSpeaker))
                resolvedSpeaker = "사용자";

            var entry = new PawnRollLogEntry(
                ++_nextSequence,
                DateTime.UtcNow,
                PawnRollLogKind.Chat,
                pawn,
                resolvedSpeaker,
                "채팅",
                string.Empty,
                0,
                string.Empty,
                normalized);
            Add(entry);
            return entry;
        }

        public static PawnRollLogEntry RecordAction(
            InteractivePawn pawn,
            string title,
            string detail)
        {
            return RecordRoll(
                PawnRollLogKind.System,
                pawn,
                title,
                string.Empty,
                0,
                string.Empty,
                detail);
        }

        public static PawnRollLogSnapshot CreateSnapshot()
        {
            var snapshot = new PawnRollLogSnapshot
            {
                NextSequence = _nextSequence
            };

            for (var index = 0; index < EntriesInternal.Count; index++)
            {
                var entry = EntriesInternal[index];
                snapshot.Entries.Add(new PawnRollLogEntrySnapshot
                {
                    Sequence = entry.Sequence,
                    TimestampUtc = entry.TimestampUtc.ToString("O"),
                    Kind = (int)entry.Kind,
                    PawnDefinitionId = entry.Pawn != null &&
                                       entry.Pawn.Definition != null
                        ? entry.Pawn.Definition.Id
                        : string.Empty,
                    PawnName = entry.PawnName,
                    Title = entry.Title,
                    Expression = entry.Expression,
                    Value = entry.Value,
                    Result = entry.Result,
                    Detail = entry.Detail,
                    Visibility = (int)entry.Visibility
                });
            }

            return snapshot;
        }

        public static bool TryApplySnapshot(
            PawnRollLogSnapshot snapshot,
            Func<string, InteractivePawn> pawnResolver,
            out string error)
        {
            error = string.Empty;
            if (snapshot == null)
            {
                error = "굴림 로그 Snapshot이 비어 있습니다.";
                return false;
            }

            var restored = new List<PawnRollLogEntry>();
            var source = snapshot.Entries ??
                         new List<PawnRollLogEntrySnapshot>();
            for (var index = 0; index < source.Count; index++)
            {
                var stored = source[index];
                if (stored == null)
                    continue;

                var timestamp = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(stored.TimestampUtc))
                    DateTime.TryParse(stored.TimestampUtc, out timestamp);
                var pawn = pawnResolver != null
                    ? pawnResolver(stored.PawnDefinitionId)
                    : null;
                var kind = Enum.IsDefined(
                    typeof(PawnRollLogKind), stored.Kind)
                    ? (PawnRollLogKind)stored.Kind
                    : PawnRollLogKind.System;
                var visibility = Enum.IsDefined(
                    typeof(RollVisibility), stored.Visibility)
                    ? (RollVisibility)stored.Visibility
                    : RollVisibility.Public;
                restored.Add(new PawnRollLogEntry(
                    Math.Max(0, stored.Sequence),
                    timestamp.ToUniversalTime(),
                    kind,
                    pawn,
                    stored.PawnName,
                    stored.Title,
                    stored.Expression,
                    stored.Value,
                    stored.Result,
                    stored.Detail,
                    visibility));
            }

            _restoringSnapshot = true;
            try
            {
                EntriesInternal.Clear();
                for (var index = 0; index < restored.Count; index++)
                {
                    if (EntriesInternal.Count >= MaximumEntries)
                        EntriesInternal.RemoveAt(0);
                    EntriesInternal.Add(restored[index]);
                }
                _nextSequence = Math.Max(snapshot.NextSequence,
                    EntriesInternal.Count > 0
                        ? EntriesInternal[EntriesInternal.Count - 1].Sequence
                        : 0);
                EntriesCleared?.Invoke();
                for (var index = 0; index < EntriesInternal.Count; index++)
                    EntryAdded?.Invoke(EntriesInternal[index]);
            }
            finally
            {
                _restoringSnapshot = false;
            }

            return true;
        }

        public static void ClearAll()
        {
            EntriesInternal.Clear();
            _nextSequence = 0;
            EntriesCleared?.Invoke();
        }

        private static void Add(in PawnRollLogEntry entry)
        {
            if (EntriesInternal.Count >= MaximumEntries)
                EntriesInternal.RemoveAt(0);

            EntriesInternal.Add(entry);
            Debug.Log(
                $"[ROLL/CHAT LOG] {entry.ToDisplayString()}",
                entry.Pawn);
            EntryAdded?.Invoke(entry);

            // 이벤트 구독 순서와 무관하게 단일 SessionAuthority로 직접 전달합니다.
            // Authority 내부에서 Sequence 중복을 제거하므로 기존 이벤트 경로와
            // 동시에 호출되어도 네트워크 로그는 한 번만 전송됩니다.
            if (!_restoringSnapshot)
                TRPGSessionAuthority.PublishLogEntryFromService(entry);
        }

        private static string NormalizeChat(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;

            var normalized = message
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

            return normalized.Length <= MaximumChatLength
                ? normalized
                : normalized.Substring(0, MaximumChatLength);
        }

        private static string ResolvePawnName(InteractivePawn pawn)
        {
            if (pawn == null)
                return string.Empty;

            var definition = pawn.Definition;
            return definition != null &&
                   !string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.DisplayName
                : pawn.name;
        }
    }
}
