using UnityEngine;

namespace Trpg.Pawns
{
    public sealed class PawnUIManager : MonoBehaviour
    {
        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private PawnSystemSettings _settings;
        [SerializeField, Tooltip("비어 있으면 런타임에 하단 정보 바를 자동 생성")]
        private PawnInfoBarWidget _infoBar;

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
                _infoBar.Hide();
            }
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
                _infoBar.Unbind();
                return;
            }

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
    }
}
