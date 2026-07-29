using System;
using Trpg.UI.Stats;
using UnityEngine;

namespace Trpg.Pawns
{
    public sealed class PawnUIManager : MonoBehaviour
    {
        private const int DefaultCheckTarget =
            PawnRollStats.FallbackCheckTarget;
        private const int MinimumCheckTarget = 1;
        private const int MaximumCheckTarget = 100;

        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private PawnSystemSettings _settings;
        [SerializeField, Tooltip("비어 있으면 런타임에 하단 정보 바를 자동 생성")]
        private PawnInfoBarWidget _infoBar;

        [Header("Roll")]
        [SerializeField, Tooltip(
            "PawnRollStats에서 조회할 능력치 ID. 비어 있거나 값이 없으면 50 사용")]
        private string _checkStatId = PawnRollStats.DefaultStatId;

        [SerializeField, Min(1), Tooltip("효과 굴림의 주사위 개수")]
        private int _effectDiceCount = 2;

        [SerializeField, Min(2), Tooltip("효과 굴림의 주사위 면수")]
        private int _effectDiceSides = 6;

        [SerializeField, Tooltip("효과 굴림 최종 합계에 더할 보정치")]
        private int _effectDiceModifier;

        [Header("Roll Result Colors")]
        [SerializeField] private Color _criticalColor =
            new Color(0.20f, 0.95f, 0.38f);
        [SerializeField] private Color _successColor =
            new Color(0.24f, 0.84f, 1f);
        [SerializeField] private Color _failureColor =
            new Color(1f, 0.36f, 0.28f);
        [SerializeField] private Color _fumbleColor = Color.black;
        [SerializeField] private Color _effectColor =
            new Color(1f, 0.78f, 0.22f);

        private PawnRollService _rollService;
        private bool _isRollInProgress;

        private void Awake()
        {
            if (_pawnManager == null || _settings == null)
            {
                Debug.LogError(
                    $"[{name}] PawnUIManager 필수 참조가 비어 있습니다.",
                    this);
                enabled = false;
                return;
            }

            if (_infoBar == null)
            {
                _infoBar = PawnInfoBarWidget.CreateRuntime(_settings);
            }

            var seed = unchecked(
                Environment.TickCount * 397 ^ GetInstanceID());
            _rollService = new PawnRollService(seed);
        }

        private void OnEnable()
        {
            if (_pawnManager == null || _infoBar == null)
            {
                return;
            }

            _pawnManager.InteractiveSelectionChanged +=
                HandleInteractiveSelectionChanged;
            _pawnManager.MovementManager.MovementBudgetChanged +=
                HandleMovementBudgetChanged;
            _pawnManager.MovementManager.PathPreviewChanged +=
                HandlePathPreviewChanged;
            _pawnManager.MovementManager.MovementRangeChanged +=
                HandleMovementRangeChanged;
            _infoBar.CloseRequested += HandleCloseRequested;
            _infoBar.MoveRequested += HandleMoveRequested;
            _infoBar.CheckRollRequested += HandleCheckRollRequested;
            _infoBar.EffectRollRequested += HandleEffectRollRequested;
            _infoBar.RollPresentationCompleted +=
                HandleRollPresentationCompleted;
            HandleInteractiveSelectionChanged(
                _pawnManager.SelectedInteractive);
        }

        private void OnDisable()
        {
            if (_pawnManager != null)
            {
                _pawnManager.InteractiveSelectionChanged -=
                    HandleInteractiveSelectionChanged;
                _pawnManager.MovementManager.MovementBudgetChanged -=
                    HandleMovementBudgetChanged;
                _pawnManager.MovementManager.PathPreviewChanged -=
                    HandlePathPreviewChanged;
                _pawnManager.MovementManager.MovementRangeChanged -=
                    HandleMovementRangeChanged;
            }

            if (_infoBar != null)
            {
                _infoBar.CloseRequested -= HandleCloseRequested;
                _infoBar.MoveRequested -= HandleMoveRequested;
                _infoBar.CheckRollRequested -= HandleCheckRollRequested;
                _infoBar.EffectRollRequested -= HandleEffectRollRequested;
                _infoBar.RollPresentationCompleted -=
                    HandleRollPresentationCompleted;
                _infoBar.Hide();
            }

            _isRollInProgress = false;
            PlayerStatState.SetActive(null);
        }

        private void HandleCloseRequested()
        {
            _pawnManager.ClearSelection();
        }

        private void HandleInteractiveSelectionChanged(
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
            {
                PlayerStatState.SetActive(null);
                _infoBar.Unbind();
                RefreshRollButtons(null);
                return;
            }

            PlayerStatState.SetActiveFrom(pawn.gameObject);

            var definition = pawn.Definition;
            var displayName =
                string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? pawn.name
                    : definition.DisplayName;
            var data = new PawnInfoBarData(
                displayName,
                definition.Description,
                definition.Portrait,
                definition.MovementScore);
            _infoBar.Bind(data);
            RefreshMovementBudget(pawn);
            RefreshMovementModeState(pawn);
            RefreshRollButtons(pawn);
        }

        private void HandleMoveRequested()
        {
            if (_pawnManager == null)
            {
                return;
            }

            var requestedActive =
                !_pawnManager.IsMovementModeActive;
            _pawnManager.SetMovementMode(requestedActive);
            RefreshMovementModeState(
                _pawnManager.SelectedInteractive);
        }

        public void SetCheckStatId(string statId)
        {
            _checkStatId = string.IsNullOrWhiteSpace(statId)
                ? PawnRollStats.DefaultStatId
                : statId.Trim();
            RefreshRollButtons(_pawnManager.SelectedInteractive);
        }

        public void SetEffectDiceExpression(
            int diceCount,
            int sides,
            int modifier = 0)
        {
            _effectDiceCount = Mathf.Clamp(
                diceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _effectDiceSides = Mathf.Clamp(
                sides,
                2,
                PawnRollService.MaximumDiceSides);
            _effectDiceModifier = modifier;
            RefreshRollButtons(_pawnManager.SelectedInteractive);
        }

        private void HandleMovementBudgetChanged(
            InteractivePawn pawn,
            float remainingMeters,
            float maximumMeters)
        {
            if (pawn == _pawnManager.SelectedInteractive)
            {
                _infoBar.SetMovementBudget(
                    remainingMeters,
                    maximumMeters);
            }
        }

        private void HandlePathPreviewChanged(PawnPathPreviewData data)
        {
            _infoBar.SetPathPreview(data, _pawnManager.BoardCamera);
        }

        private void HandleMovementRangeChanged(PawnMovementRangeData data)
        {
            _infoBar.SetMovementRange(data, _pawnManager.BoardCamera);
        }

        private void RefreshMovementBudget(InteractivePawn pawn)
        {
            if (_pawnManager.MovementManager.TryGetMovementBudget(
                    pawn,
                    out var remaining,
                    out var maximum))
            {
                _infoBar.SetMovementBudget(remaining, maximum);
            }
        }

        private void RefreshMovementModeState(InteractivePawn pawn)
        {
            var canMove = pawn != null && pawn.IsMoveable;
            _infoBar.SetMovementModeState(
                canMove,
                canMove && _pawnManager.IsMovementModeActive);
        }

        private void HandleCheckRollRequested(PawnCheckRollRequest request)
        {
            if (!TryBeginRoll(out var pawn))
            {
                return;
            }

            var target = ResolveCheckTarget(pawn);
            var result = _rollService.RollD100(target);
            var presentation = new PawnRollPresentationData(
                "판정 굴림",
                $"d100 / 목표 {target}",
                result.Roll,
                1,
                100,
                GetCheckResultLabel(result.Grade),
                $"굴림 {result.Roll} / 목표 {target}",
                GetCheckResultColor(result.Grade),
                1.55f);
            _infoBar.PlayRoll(presentation);
        }

        private void HandleEffectRollRequested(PawnEffectRollRequest request)
        {
            if (!TryBeginRoll(out _))
            {
                return;
            }

            var result = _rollService.RollEffect(
                _effectDiceCount,
                _effectDiceSides,
                _effectDiceModifier);
            var presentation = new PawnRollPresentationData(
                "효과 굴림",
                result.Expression,
                result.Total,
                result.MinimumTotal,
                result.MaximumTotal,
                $"합계 {result.Total}",
                result.GetBreakdownLabel(),
                _effectColor,
                1.35f);
            _infoBar.PlayRoll(presentation);
        }

        private void HandleRollPresentationCompleted()
        {
            _isRollInProgress = false;
            _infoBar.SetRollButtonsEnabled(
                _pawnManager.SelectedInteractive != null &&
                _pawnManager.SelectedInteractive.Definition != null);
        }

        private bool TryBeginRoll(out InteractivePawn pawn)
        {
            pawn = _pawnManager.SelectedInteractive;
            if (_isRollInProgress ||
                pawn == null ||
                pawn.Definition == null)
            {
                return false;
            }

            _isRollInProgress = true;
            _infoBar.SetRollButtonsEnabled(false);
            return true;
        }

        private int ResolveCheckTarget(InteractivePawn pawn)
        {
            if (pawn != null &&
                pawn.TryGetComponent<PawnRollStats>(out var stats))
            {
                return stats.GetCheckTarget(_checkStatId);
            }

            return DefaultCheckTarget;
        }

        private void RefreshRollButtons(InteractivePawn pawn)
        {
            if (_infoBar == null)
            {
                return;
            }

            _isRollInProgress = false;
            _infoBar.CancelRollPresentation();

            var hasPawn = pawn != null && pawn.Definition != null;
            _infoBar.SetRollButtonsEnabled(hasPawn);
            if (!hasPawn)
            {
                return;
            }

            var target = Mathf.Clamp(
                ResolveCheckTarget(pawn),
                MinimumCheckTarget,
                MaximumCheckTarget);
            var effectExpression = PawnRollService.FormatExpression(
                _effectDiceCount,
                _effectDiceSides,
                _effectDiceModifier);
            _infoBar.SetRollButtonLabels(
                $"판정 굴림\nD100 ≤ {target}",
                $"효과 굴림\n{effectExpression}");
        }

        private Color GetCheckResultColor(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return _criticalColor;
                case CheckRollGrade.Fumble:
                    return _fumbleColor;
                case CheckRollGrade.ExtremeSuccess:
                case CheckRollGrade.HardSuccess:
                case CheckRollGrade.Success:
                    return _successColor;
                default:
                    return _failureColor;
            }
        }

        private static string GetCheckResultLabel(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return "대성공";
                case CheckRollGrade.ExtremeSuccess:
                    return "극단적 성공";
                case CheckRollGrade.HardSuccess:
                    return "어려운 성공";
                case CheckRollGrade.Success:
                    return "성공";
                case CheckRollGrade.Fumble:
                    return "대실패";
                default:
                    return "실패";
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _effectDiceCount = Mathf.Clamp(
                _effectDiceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _effectDiceSides = Mathf.Clamp(
                _effectDiceSides,
                2,
                PawnRollService.MaximumDiceSides);
            if (string.IsNullOrWhiteSpace(_checkStatId))
            {
                _checkStatId = PawnRollStats.DefaultStatId;
            }
        }
#endif
    }
}
