using System;
using System.Collections.Generic;
using UnityEngine;
namespace Trpg.Pawns
{
    [RequireComponent(typeof(PawnMovementRangeManager))]
    [RequireComponent(typeof(PawnDoorManager))]
    public sealed class PawnMovementManager : MonoBehaviour
    {
        private const float PathEpsilon = 0.001f;
        [SerializeField] private PawnSystemSettings _settings;
        [SerializeField] private PawnNavMeshManager _navMeshManager;
        private readonly List<InteractivePawn> _interactivePawns =
            new List<InteractivePawn>();
        private readonly Dictionary<InteractivePawn, PawnMovementState> _states =
            new Dictionary<InteractivePawn, PawnMovementState>();
        private readonly HashSet<InteractivePawn> _presentingMovers =
            new HashSet<InteractivePawn>();
        private InteractivePawn _selectedMover;
        private PawnMovementRangeManager _movementRangeManager;
        private PawnDoorManager _doorManager;
        public event Action<InteractivePawn, Vector2> PawnMoved;
        public event Action<InteractivePawn, Vector2> DoorTransferred;
        public event Action<InteractivePawn, float, float>
            MovementBudgetChanged;
        public event Action<PawnPathPreviewData> PathPreviewChanged;
        public event Action<PawnMovementRangeData> MovementRangeChanged;
        public InteractivePawn SelectedMover => _selectedMover;
        public void Bind(IReadOnlyList<InteractivePawn> interactivePawns)
        {
            Unbind();
            if (!HasRequiredReferences())
            {
                return;
            }
            _navMeshManager.RuntimeBakeCompleted +=
                HandleRuntimeBakeCompleted;
            for (var index = 0; index < interactivePawns.Count; index++)
            {
                var pawn = interactivePawns[index];
                if (pawn == null)
                {
                    continue;
                }
                _interactivePawns.Add(pawn);
                pawn.MovementPresentationCompleted +=
                    HandleMovementPresentationCompleted;
                if (pawn.IsMoveable)
                {
                    var maximum = GetMaximumMoveMeters(pawn);
                    _states[pawn] = new PawnMovementState(
                        pawn.WorldPosition,
                        maximum,
                        maximum);
                }
            }
            _doorManager.TransferResolved += HandleDoorTransferResolved;
            _doorManager.Bind(
                interactivePawns,
                _settings,
                _navMeshManager);
        }
        public void Unbind()
        {
            if (_navMeshManager != null)
            {
                _navMeshManager.RuntimeBakeCompleted -=
                    HandleRuntimeBakeCompleted;
            }
            for (var index = 0; index < _interactivePawns.Count; index++)
            {
                var pawn = _interactivePawns[index];
                if (pawn == null)
                {
                    continue;
                }
                pawn.MovementPresentationCompleted -=
                    HandleMovementPresentationCompleted;
            }
            if (_doorManager != null)
            {
                _doorManager.TransferResolved -=
                    HandleDoorTransferResolved;
                _doorManager.Unbind();
            }
            HideReachableArea();
            HidePathPreview();
            _interactivePawns.Clear();
            _states.Clear();
            _presentingMovers.Clear();
            _selectedMover = null;
        }
        public void SelectMover(InteractivePawn pawn)
        {
            _selectedMover =
                pawn != null && pawn.IsMoveable ? pawn : null;
            HidePathPreview();
            RefreshReachableArea();
        }
        public void ClearMover()
        {
            _selectedMover = null;
            HideReachableArea();
            HidePathPreview();
        }
        public void PreviewSelectedPath(
            Vector2 requestedPosition,
            Vector2 screenPosition)
        {
            if (_selectedMover == null ||
                _presentingMovers.Contains(_selectedMover) ||
                !_states.TryGetValue(_selectedMover, out var state))
            {
                HidePathPreview();
                return;
            }
            var destination = SnapIfEnabled(requestedPosition);
            if (!_navMeshManager.TryCalculatePath(
                    state.Position,
                    destination,
                    _settings.MaxProjectionMeters,
                    out var corners,
                    out var distance) ||
                corners.Length < 2)
            {
                PathPreviewChanged?.Invoke(
                    PawnPathPreviewData.Unreachable(screenPosition));
                return;
            }
            var measuredDistance = QuantizeDistance(distance);
            PathPreviewChanged?.Invoke(
                PawnPathPreviewData.Reachable(
                    screenPosition,
                    measuredDistance,
                    measuredDistance <=
                    state.RemainingMeters + PathEpsilon,
                    corners,
                    state.RemainingMeters));
        }
        public void HidePathPreview()
        {
            PathPreviewChanged?.Invoke(PawnPathPreviewData.Hidden);
        }
        public bool TryMoveSelectedTo(Vector2 requestedPosition)
        {
            if (_selectedMover == null ||
                _presentingMovers.Contains(_selectedMover) ||
                !_states.TryGetValue(_selectedMover, out var state))
            {
                return false;
            }
            var destination = SnapIfEnabled(requestedPosition);
            if (!_navMeshManager.TryCalculatePath(
                    state.Position,
                    destination,
                    _settings.MaxProjectionMeters,
                    out var corners,
                    out var length) ||
                length <= PathEpsilon)
            {
                return false;
            }
            var moveCost = QuantizeDistance(length);
            if (moveCost > state.RemainingMeters + PathEpsilon)
            {
                return false;
            }
            var projectedDestination = new Vector2(
                corners[corners.Length - 1].x,
                corners[corners.Length - 1].y);
            state.Position = projectedDestination;
            state.RemainingMeters = QuantizeRemainingDistance(
                state.RemainingMeters - moveCost);
            _presentingMovers.Add(_selectedMover);
            HideReachableArea();
            HidePathPreview();
            _selectedMover.PresentMovement(corners);
            MovementBudgetChanged?.Invoke(
                _selectedMover,
                state.RemainingMeters,
                state.MaximumMeters);
            PawnMoved?.Invoke(_selectedMover, state.Position);
            return true;
        }
        public void ResetMovementBudget(InteractivePawn pawn)
        {
            if (pawn == null || !_states.TryGetValue(pawn, out var state))
            {
                return;
            }
            state.RemainingMeters = state.MaximumMeters;
            MovementBudgetChanged?.Invoke(
                pawn,
                state.RemainingMeters,
                state.MaximumMeters);
            if (pawn == _selectedMover)
            {
                RefreshReachableArea();
            }
        }
        public void ResetAllMovementBudgets()
        {
            foreach (var pair in _states)
            {
                pair.Value.RemainingMeters = pair.Value.MaximumMeters;
                MovementBudgetChanged?.Invoke(
                    pair.Key,
                    pair.Value.RemainingMeters,
                    pair.Value.MaximumMeters);
            }
            RefreshReachableArea();
        }
        public float GetRemainingMoveMeters(InteractivePawn pawn)
        {
            return pawn != null && _states.TryGetValue(pawn, out var state)
                ? state.RemainingMeters
                : 0f;
        }
        public bool TryGetMovementBudget(
            InteractivePawn pawn,
            out float remainingMeters,
            out float maximumMeters)
        {
            if (pawn != null && _states.TryGetValue(pawn, out var state))
            {
                remainingMeters = state.RemainingMeters;
                maximumMeters = state.MaximumMeters;
                return true;
            }
            remainingMeters = 0f;
            maximumMeters = 0f;
            return false;
        }
        private void RefreshReachableArea()
        {
            HideReachableArea();
            if (_selectedMover == null ||
                _presentingMovers.Contains(_selectedMover) ||
                !_states.TryGetValue(_selectedMover, out var state))
            {
                return;
            }
            EnsureMovementRangeManager();
            if (_movementRangeManager.TryBuild(
                    state.Position,
                    state.RemainingMeters,
                    _settings.MaxProjectionMeters,
                    _navMeshManager,
                    out var rangeData))
            {
                MovementRangeChanged?.Invoke(rangeData);
            }
        }
        private void HideReachableArea()
        {
            MovementRangeChanged?.Invoke(PawnMovementRangeData.Hidden);
        }
        private void EnsureMovementRangeManager()
        {
            if (_movementRangeManager == null &&
                !TryGetComponent(out _movementRangeManager))
            {
                _movementRangeManager =
                    gameObject.AddComponent<PawnMovementRangeManager>();
            }
        }
        private void EnsureDoorManager()
        {
            if (_doorManager == null && !TryGetComponent(out _doorManager))
            {
                _doorManager = gameObject.AddComponent<PawnDoorManager>();
            }
        }
        private void HandleMovementPresentationCompleted(
            InteractivePawn pawn)
        {
            _presentingMovers.Remove(pawn);
            if (pawn == _selectedMover)
            {
                RefreshReachableArea();
            }
        }
        private void HandleRuntimeBakeCompleted()
        {
            RefreshReachableArea();
        }
        private void HandleDoorTransferResolved(
            InteractivePawn moveable,
            Vector2 destination)
        {
            if (moveable == null)
            {
                return;
            }
            _presentingMovers.Remove(moveable);
            HidePathPreview();
            if (_states.TryGetValue(moveable, out var state))
            {
                state.Position = destination;
            }
            moveable.TeleportTo(destination);
            DoorTransferred?.Invoke(moveable, destination);
            if (moveable == _selectedMover)
            {
                RefreshReachableArea();
            }
        }
        private float GetMaximumMoveMeters(InteractivePawn pawn)
        {
            var movementScore =
                pawn.Definition != null
                    ? pawn.Definition.MovementScore
                    : _settings.DefaultMovementScore;
            return QuantizeDistance(
                _settings.GetTurnMoveMeters(movementScore));
        }
        private Vector2 SnapIfEnabled(Vector2 position)
        {
            if (!_settings.SnapDestinationToMovementStep)
            {
                return position;
            }
            var cell = _settings.MovementStepMeters;
            return new Vector2(
                Mathf.Round(position.x / cell) * cell,
                Mathf.Round(position.y / cell) * cell);
        }
        private float QuantizeDistance(float distance)
        {
            if (distance <= PathEpsilon)
            {
                return 0f;
            }
            var step = _settings.MovementStepMeters;
            return Mathf.Max(
                step,
                Mathf.Round(distance / step) * step);
        }
        private float QuantizeRemainingDistance(float distance)
        {
            if (distance <= PathEpsilon)
            {
                return 0f;
            }
            var step = _settings.MovementStepMeters;
            return Mathf.Max(
                0f,
                Mathf.Round(distance / step) * step);
        }
        private bool HasRequiredReferences()
        {
            EnsureMovementRangeManager();
            EnsureDoorManager();
            var valid = _settings != null && _navMeshManager != null;
            if (!valid)
            {
                Debug.LogError(
                    $"[{name}] PawnMovementManager 필수 참조가 비어 있습니다.",
                    this);
            }
            return valid;
        }
    }
}
