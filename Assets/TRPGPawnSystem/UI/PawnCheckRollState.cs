using UnityEngine;

namespace Trpg.Pawns
{
    public enum PawnCheckRollSessionPhase
    {
        Empty,
        Waiting,
        SourceSelected,
        Rolling,
        FailureDecision,
        Finalized
    }

    /// <summary>
    /// 캐릭터 Pawn 하나가 현재 턴 동안 보유하는 판정 세션입니다.
    /// 선택 변경이나 UI 닫기에는 지워지지 않고, 턴 리셋에서만 초기화됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnCheckRollState : MonoBehaviour
    {
        private static long _nextSessionId;

        public long SessionId { get; private set; }
        public PawnCheckRollSessionPhase Phase { get; private set; }
        public bool HasSource { get; private set; }
        public PawnCheckSourceData Source { get; private set; }
        public bool HasDifficulty { get; private set; }
        public PawnCheckDifficulty Difficulty { get; private set; }
        public bool HasEvaluation { get; private set; }
        public PawnCheckEvaluation Evaluation { get; private set; }
        public bool ChallengeUsed { get; private set; }
        public bool LastRollWasChallenge { get; private set; }
        public bool LuckApplied { get; private set; }
        public int LuckSpent { get; private set; }
        public int RemainingLuck { get; private set; }

        public bool HasLastPresentation { get; private set; }
        public string LastTitle { get; private set; }
        public string LastExpression { get; private set; }
        public int LastValue { get; private set; }
        public int LastMinimum { get; private set; }
        public int LastMaximum { get; private set; }
        public string LastResultLabel { get; private set; }
        public string LastDetailLabel { get; private set; }
        public Color LastResultColor { get; private set; }
        public PawnRollResultTone LastResultTone { get; private set; }

        public bool HasConfigWindowPosition { get; private set; }
        public Vector2 ConfigWindowPosition { get; private set; }
        public bool HasResultWindowPosition { get; private set; }
        public Vector2 ResultWindowPosition { get; private set; }

        public void EnsureSession()
        {
            if (SessionId == 0)
                SessionId = ++_nextSessionId;

            if (Phase == PawnCheckRollSessionPhase.Empty)
                Phase = PawnCheckRollSessionPhase.Waiting;
        }

        public void SelectSource(in PawnCheckSourceData source)
        {
            EnsureSession();
            Source = source;
            HasSource = source.IsValid;
            HasDifficulty = false;
            HasEvaluation = false;
            ChallengeUsed = false;
            LastRollWasChallenge = false;
            LuckApplied = false;
            LuckSpent = 0;
            RemainingLuck = 0;
            ClearLastPresentation();
            Phase = HasSource
                ? PawnCheckRollSessionPhase.SourceSelected
                : PawnCheckRollSessionPhase.Waiting;
        }

        public void ClearSource()
        {
            EnsureSession();
            Source = default;
            HasSource = false;
            HasDifficulty = false;
            HasEvaluation = false;
            ChallengeUsed = false;
            LastRollWasChallenge = false;
            LuckApplied = false;
            LuckSpent = 0;
            RemainingLuck = 0;
            ClearLastPresentation();
            Phase = PawnCheckRollSessionPhase.Waiting;
        }

        public void BeginCheckRoll(
            PawnCheckDifficulty difficulty,
            in PawnCheckEvaluation evaluation,
            bool isChallenge,
            in PawnRollWindowData presentation)
        {
            EnsureSession();
            Difficulty = difficulty;
            HasDifficulty = true;
            Evaluation = evaluation;
            HasEvaluation = true;
            LastRollWasChallenge = isChallenge;
            if (isChallenge)
                ChallengeUsed = true;
            LuckApplied = false;
            LuckSpent = 0;
            RemainingLuck = 0;
            SavePresentation(presentation);
            Phase = PawnCheckRollSessionPhase.Rolling;
        }

        public void RecordPureRoll(in PawnRollWindowData presentation)
        {
            EnsureSession();
            HasEvaluation = false;
            LastRollWasChallenge = false;
            LuckApplied = false;
            LuckSpent = 0;
            RemainingLuck = 0;
            SavePresentation(presentation);
            Phase = PawnCheckRollSessionPhase.Rolling;
        }

        public void MarkFailureDecision()
        {
            EnsureSession();
            Phase = PawnCheckRollSessionPhase.FailureDecision;
        }

        public void MarkFinalized()
        {
            EnsureSession();
            Phase = PawnCheckRollSessionPhase.Finalized;
        }

        public void MarkLuckApplied(int spent, int remaining)
        {
            EnsureSession();
            ChallengeUsed = true;
            LuckApplied = true;
            LuckSpent = Mathf.Max(0, spent);
            RemainingLuck = Mathf.Max(0, remaining);
            Phase = PawnCheckRollSessionPhase.Finalized;
        }

        public void MarkLuckApplied(
            int spent,
            int remaining,
            in PawnRollWindowData presentation)
        {
            MarkLuckApplied(spent, remaining);
            SavePresentation(presentation);
        }

        public PawnRollWindowData GetLastPresentation()
        {
            return new PawnRollWindowData(
                LastTitle,
                LastExpression,
                LastValue,
                LastMinimum,
                LastMaximum,
                LastResultLabel,
                LastDetailLabel,
                LastResultColor,
                1.55f,
                LastResultTone);
        }

        public void SetConfigWindowPosition(Vector2 position)
        {
            ConfigWindowPosition = position;
            HasConfigWindowPosition = true;
        }

        public void SetResultWindowPosition(Vector2 position)
        {
            ResultWindowPosition = position;
            HasResultWindowPosition = true;
        }

        public void ResetForTurn()
        {
            SessionId = 0;
            Phase = PawnCheckRollSessionPhase.Empty;
            Source = default;
            HasSource = false;
            HasDifficulty = false;
            HasEvaluation = false;
            ChallengeUsed = false;
            LastRollWasChallenge = false;
            LuckApplied = false;
            LuckSpent = 0;
            RemainingLuck = 0;
            ClearLastPresentation();
            HasConfigWindowPosition = false;
            ConfigWindowPosition = Vector2.zero;
            HasResultWindowPosition = false;
            ResultWindowPosition = Vector2.zero;
        }

        private void SavePresentation(in PawnRollWindowData presentation)
        {
            HasLastPresentation = true;
            LastTitle = presentation.Title;
            LastExpression = presentation.Expression;
            LastValue = presentation.FinalValue;
            LastMinimum = presentation.MinimumValue;
            LastMaximum = presentation.MaximumValue;
            LastResultLabel = presentation.ResultLabel;
            LastDetailLabel = presentation.DetailLabel;
            LastResultColor = presentation.ResultColor;
            LastResultTone = presentation.ResultTone;
        }

        private void ClearLastPresentation()
        {
            HasLastPresentation = false;
            LastTitle = string.Empty;
            LastExpression = string.Empty;
            LastValue = 0;
            LastMinimum = 0;
            LastMaximum = 0;
            LastResultLabel = string.Empty;
            LastDetailLabel = string.Empty;
            LastResultColor = Color.white;
            LastResultTone = PawnRollResultTone.Standard;
        }
    }
}
