using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
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
            string detail)
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

        public string ToDisplayString()
        {
            var owner = string.IsNullOrWhiteSpace(PawnName)
                ? "시스템"
                : PawnName;

            if (Kind == PawnRollLogKind.Chat)
                return $"[{Sequence:0000}] {owner}: {Detail}";

            if (Kind == PawnRollLogKind.System)
                return $"[{Sequence:0000}] {owner} · {Title} · {Detail}";

            return $"[{Sequence:0000}] {owner} · {Title} · " +
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
        }

        public static PawnRollLogEntry RecordRoll(
            PawnRollLogKind kind,
            InteractivePawn pawn,
            string title,
            string expression,
            int value,
            string result,
            string detail)
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
                detail);
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
            Debug.Log($"[ROLL/CHAT LOG] {entry.ToDisplayString()}", entry.Pawn);
            EntryAdded?.Invoke(entry);
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
