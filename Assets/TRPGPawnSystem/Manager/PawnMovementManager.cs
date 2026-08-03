using System;
using System.Collections.Generic;
using Trpg.Domain.Stats;
using UnityEngine;
namespace Trpg.Pawns
{
    public readonly struct PawnMovementCommitData
    {
        private readonly Vector3[] _corners;

        public PawnMovementCommitData(
            InteractivePawn pawn,
            Vector3[] corners,
            Vector2 destination,
            float moveCost,
            float remainingMeters,
            float maximumMeters)
        {
            Pawn = pawn;
            _corners = corners != null
                ? (Vector3[])corners.Clone()
                : Array.Empty<Vector3>();
            Destination = destination;
            MoveCost = moveCost;
            RemainingMeters = remainingMeters;
            MaximumMeters = maximumMeters;
        }

        public InteractivePawn Pawn { get; }
        public Vector2 Destination { get; }
        public float MoveCost { get; }
        public float RemainingMeters { get; }
        public float MaximumMeters { get; }
        public int CornerCount => _corners != null ? _corners.Length : 0;

        public Vector3 GetCorner(int index)
        {
            if (_corners == null ||
                index < 0 ||
                index >= _corners.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _corners[index];
        }

        public Vector3[] CopyCorners()
        {
            return _corners != null
                ? (Vector3[])_corners.Clone()
                : Array.Empty<Vector3>();
        }
    }

    [RequireComponent(typeof(PawnMovementRangeManager))]
    [RequireComponent(typeof(PawnDoorManager))]
    public sealed class PawnMovementManager : MonoBehaviour
    {
        private const float PathEpsilon = 0.001f;
        [SerializeField] private PawnSystemSettings _settings;
        [SerializeField] private PawnNavMeshManager _navMeshManager;

        [SerializeField, Min(0f), Tooltip(
            "다른 InteractivePawn Collider 외곽에서 목적지로 지정할 수 없는 거리(m)")]
        private float _interactiveDestinationClearanceMeters = 0.25f;
        private readonly List<InteractivePawn> _interactivePawns =
            new List<InteractivePawn>();
        private readonly Dictionary<InteractivePawn, PawnMovementState> _states =
            new Dictionary<InteractivePawn, PawnMovementState>();
        private readonly Dictionary<InteractivePawn, Collider2D[]>
            _interactiveColliders =
                new Dictionary<InteractivePawn, Collider2D[]>();
        private readonly HashSet<InteractivePawn> _presentingMovers =
            new HashSet<InteractivePawn>();
        private InteractivePawn _selectedMover;
        private PawnMovementRangeManager _movementRangeManager;
        private PawnDoorManager _doorManager;
        public event Action<InteractivePawn, Vector2> PawnMoved;
        public event Action<PawnMovementCommitData> MovementCommitted;
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
                _interactiveColliders[pawn] =
                    pawn.GetComponentsInChildren<Collider2D>(true);
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
            _interactiveColliders.Clear();
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
            if (!TryResolveValidPath(
                    _selectedMover,
                    state.Position,
                    destination,
                    out var corners,
                    out var distance,
                    out _) ||
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
            return TryMovePawnTo(
                _selectedMover,
                requestedPosition,
                out _);
        }

        public bool TryMovePawnTo(
            InteractivePawn pawn,
            Vector2 requestedPosition,
            out PawnMovementCommitData commit)
        {
            commit = default;

            if (pawn == null ||
                !pawn.IsMoveable ||
                _presentingMovers.Contains(pawn) ||
                !_states.TryGetValue(pawn, out var state))
            {
                return false;
            }

            var destination = SnapIfEnabled(requestedPosition);
            if (!TryResolveValidPath(
                    pawn,
                    state.Position,
                    destination,
                    out var corners,
                    out var length,
                    out var projectedDestination) ||
                length <= PathEpsilon)
            {
                return false;
            }

            var moveCost = QuantizeDistance(length);
            if (moveCost > state.RemainingMeters + PathEpsilon)
            {
                return false;
            }

            state.Position = projectedDestination;
            state.RemainingMeters = QuantizeRemainingDistance(
                state.RemainingMeters - moveCost);

            _presentingMovers.Add(pawn);
            HideReachableArea();
            HidePathPreview();
            pawn.PresentMovement(corners);

            MovementBudgetChanged?.Invoke(
                pawn,
                state.RemainingMeters,
                state.MaximumMeters);
            PawnMoved?.Invoke(pawn, state.Position);

            commit = new PawnMovementCommitData(
                pawn,
                corners,
                state.Position,
                moveCost,
                state.RemainingMeters,
                state.MaximumMeters);
            MovementCommitted?.Invoke(commit);
            return true;
        }

        public bool ApplyReplicatedMove(
            InteractivePawn pawn,
            Vector3[] corners,
            Vector2 destination,
            float remainingMeters,
            float maximumMeters)
        {
            if (pawn == null || !pawn.IsMoveable)
                return false;

            var resolvedCorners = corners != null && corners.Length >= 2
                ? (Vector3[])corners.Clone()
                : new[]
                {
                    pawn.PresentationWorldPosition,
                    new Vector3(destination.x, destination.y, 0f)
                };

            _states[pawn] = new PawnMovementState(
                destination,
                Mathf.Max(0f, remainingMeters),
                Mathf.Max(0f, maximumMeters));
            _presentingMovers.Remove(pawn);
            _presentingMovers.Add(pawn);
            HidePathPreview();
            pawn.PresentMovement(resolvedCorners);

            MovementBudgetChanged?.Invoke(
                pawn,
                Mathf.Max(0f, remainingMeters),
                Mathf.Max(0f, maximumMeters));
            PawnMoved?.Invoke(pawn, destination);
            return true;
        }

        public bool ApplyReplicatedSnapshot(
            InteractivePawn pawn,
            Vector2 position,
            float remainingMeters,
            float maximumMeters)
        {
            if (pawn == null)
                return false;

            _presentingMovers.Remove(pawn);
            if (pawn.IsMoveable)
            {
                _states[pawn] = new PawnMovementState(
                    position,
                    Mathf.Max(0f, remainingMeters),
                    Mathf.Max(0f, maximumMeters));

                MovementBudgetChanged?.Invoke(
                    pawn,
                    Mathf.Max(0f, remainingMeters),
                    Mathf.Max(0f, maximumMeters));
            }

            pawn.TeleportTo(position);
            PawnMoved?.Invoke(pawn, position);

            if (pawn == _selectedMover)
                RefreshReachableArea();

            return true;
        }

        public bool ApplyReplicatedDoorTransfer(
            InteractivePawn pawn,
            Vector2 destination)
        {
            if (pawn == null)
                return false;

            _presentingMovers.Remove(pawn);
            if (_states.TryGetValue(pawn, out var state))
                state.Position = destination;

            pawn.TeleportTo(destination);
            DoorTransferred?.Invoke(pawn, destination);

            if (pawn == _selectedMover)
                RefreshReachableArea();

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

        public void RefreshMovementBudgetFromStats(
            InteractivePawn pawn,
            bool preserveSpentDistance = true)
        {
            if (pawn == null ||
                !_states.TryGetValue(pawn, out var state))
            {
                return;
            }

            var maximum = GetMaximumMoveMeters(pawn);
            var spent = Mathf.Max(
                0f,
                state.MaximumMeters - state.RemainingMeters);
            var remaining = preserveSpentDistance
                ? QuantizeRemainingDistance(maximum - spent)
                : maximum;
            var refreshedState = new PawnMovementState(
                state.Position,
                remaining,
                maximum);
            _states[pawn] = refreshedState;

            MovementBudgetChanged?.Invoke(
                pawn,
                refreshedState.RemainingMeters,
                refreshedState.MaximumMeters);

            if (pawn == _selectedMover)
            {
                RefreshReachableArea();
            }
        }
        public bool RestorePawnPosition(
            InteractivePawn pawn,
            Vector2 restoredPosition)
        {
            if (pawn == null)
            {
                return false;
            }

            _presentingMovers.Remove(pawn);
            HidePathPreview();

            if (pawn.IsMoveable)
            {
                if (_states.TryGetValue(pawn, out var state))
                {
                    var restoredState = new PawnMovementState(
                        restoredPosition,
                        state.RemainingMeters,
                        state.MaximumMeters);
                    _states[pawn] = restoredState;
                    MovementBudgetChanged?.Invoke(
                        pawn,
                        restoredState.RemainingMeters,
                        restoredState.MaximumMeters);
                }
                else
                {
                    var maximum = GetMaximumMoveMeters(pawn);
                    var restoredState = new PawnMovementState(
                        restoredPosition,
                        maximum,
                        maximum);
                    _states[pawn] = restoredState;
                    MovementBudgetChanged?.Invoke(
                        pawn,
                        restoredState.RemainingMeters,
                        restoredState.MaximumMeters);
                }
            }

            pawn.TeleportTo(restoredPosition);
            if (pawn == _selectedMover)
            {
                RefreshReachableArea();
            }

            return true;
        }

        public bool TryGetMovementPosition(
            InteractivePawn pawn,
            out Vector2 position)
        {
            if (pawn != null && _states.TryGetValue(pawn, out var state))
            {
                position = state.Position;
                return true;
            }

            position = pawn != null ? pawn.WorldPosition : default;
            return pawn != null;
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
            var definition = pawn.Definition;
            var movementScore = definition != null
                ? definition.MovementScore
                : _settings.DefaultMovementScore;

            if (definition != null &&
                TryGetStatProvider(pawn, out var statProvider))
            {
                movementScore = definition.ResolveMovementScore(
                    statProvider,
                    movementScore);
            }

            return QuantizeDistance(
                _settings.GetTurnMoveMeters(movementScore));
        }

        private static bool TryGetStatProvider(
            InteractivePawn pawn,
            out IStatValueProvider provider)
        {
            provider = null;
            if (pawn == null)
            {
                return false;
            }

            var components =
                pawn.GetComponents<MonoBehaviour>();
            for (var index = 0;
                 index < components.Length;
                 index++)
            {
                if (components[index] is IStatValueProvider candidate)
                {
                    provider = candidate;
                    return true;
                }
            }

            components =
                pawn.GetComponentsInChildren<MonoBehaviour>(true);
            for (var index = 0;
                 index < components.Length;
                 index++)
            {
                if (components[index] is IStatValueProvider candidate)
                {
                    provider = candidate;
                    return true;
                }
            }

            components =
                pawn.GetComponentsInParent<MonoBehaviour>(true);
            for (var index = 0;
                 index < components.Length;
                 index++)
            {
                if (components[index] is IStatValueProvider candidate)
                {
                    provider = candidate;
                    return true;
                }
            }

            return false;
        }
        private bool TryResolveValidPath(
            InteractivePawn movingPawn,
            Vector2 origin,
            Vector2 requestedDestination,
            out Vector3[] corners,
            out float pathLength,
            out Vector2 projectedDestination)
        {
            corners = Array.Empty<Vector3>();
            pathLength = 0f;
            projectedDestination = default;

            if (!_navMeshManager.TryCalculatePath(
                    origin,
                    requestedDestination,
                    _settings.MaxProjectionMeters,
                    out corners,
                    out pathLength) ||
                corners == null ||
                corners.Length == 0)
            {
                return false;
            }

            var finalCorner = corners[corners.Length - 1];
            projectedDestination = new Vector2(
                finalCorner.x,
                finalCorner.y);

            return !IsDestinationBlockedByInteractivePawn(
                movingPawn,
                projectedDestination);
        }

        private bool IsDestinationBlockedByInteractivePawn(
            InteractivePawn movingPawn,
            Vector2 destination)
        {
            var clearance = Mathf.Max(
                0f,
                _interactiveDestinationClearanceMeters);

            for (var pawnIndex = 0;
                 pawnIndex < _interactivePawns.Count;
                 pawnIndex++)
            {
                var pawn = _interactivePawns[pawnIndex];
                if (pawn == null ||
                    pawn == movingPawn ||
                    !pawn.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!_interactiveColliders.TryGetValue(
                        pawn,
                        out var colliders) ||
                    colliders == null)
                {
                    colliders =
                        pawn.GetComponentsInChildren<Collider2D>(true);
                    _interactiveColliders[pawn] = colliders;
                }

                for (var colliderIndex = 0;
                     colliderIndex < colliders.Length;
                     colliderIndex++)
                {
                    var collider = colliders[colliderIndex];
                    if (collider == null ||
                        !collider.enabled ||
                        !collider.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    var closest = collider.ClosestPoint(destination);
                    var distance = Vector2.Distance(
                        destination,
                        closest);
                    if (distance <= clearance + PathEpsilon)
                    {
                        return true;
                    }
                }
            }

            return false;
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
