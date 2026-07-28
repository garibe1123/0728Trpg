using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    [DisallowMultipleComponent]
    public sealed class PawnDoorManager : MonoBehaviour
    {
        private readonly List<InteractivePawn> _interactivePawns =
            new List<InteractivePawn>();
        private readonly Dictionary<string, InteractivePawn> _doorsById =
            new Dictionary<string, InteractivePawn>(StringComparer.Ordinal);
        private readonly HashSet<InteractivePawn> _doorGuards =
            new HashSet<InteractivePawn>();

        private PawnSystemSettings _settings;
        private PawnNavMeshManager _navMeshManager;

        public event Action<InteractivePawn, Vector2> TransferResolved;

        public void Bind(
            IReadOnlyList<InteractivePawn> interactivePawns,
            PawnSystemSettings settings,
            PawnNavMeshManager navMeshManager)
        {
            Unbind();
            _settings = settings;
            _navMeshManager = navMeshManager;

            for (var index = 0; index < interactivePawns.Count; index++)
            {
                var pawn = interactivePawns[index];
                if (pawn == null)
                {
                    continue;
                }

                _interactivePawns.Add(pawn);
                pawn.DoorEntered += HandleDoorEntered;
                IndexDoor(pawn);
            }
        }

        public void Unbind()
        {
            StopAllCoroutines();
            for (var index = 0; index < _interactivePawns.Count; index++)
            {
                var pawn = _interactivePawns[index];
                if (pawn != null)
                {
                    pawn.DoorEntered -= HandleDoorEntered;
                }
            }

            _interactivePawns.Clear();
            _doorsById.Clear();
            _doorGuards.Clear();
            _settings = null;
            _navMeshManager = null;
        }

        private void IndexDoor(InteractivePawn pawn)
        {
            if (!pawn.IsDoor ||
                string.IsNullOrWhiteSpace(pawn.InstanceId))
            {
                return;
            }

            if (_doorsById.ContainsKey(pawn.InstanceId))
            {
                Debug.LogError(
                    $"Door Instance Id '{pawn.InstanceId}'가 중복되었습니다.",
                    pawn);
                return;
            }

            _doorsById.Add(pawn.InstanceId, pawn);
        }

        private void HandleDoorEntered(
            InteractivePawn sourceDoor,
            InteractivePawn moveable)
        {
            if (!TryResolveDestinationDoor(
                    sourceDoor,
                    moveable,
                    out var destinationDoor))
            {
                return;
            }

            if (!_navMeshManager.TryProject(
                    destinationDoor.ArrivalPosition,
                    _settings.MaxProjectionMeters,
                    out var destination))
            {
                Debug.LogWarning(
                    $"[{destinationDoor.name}] Arrival Point 주변에 NavMesh가 없습니다.",
                    destinationDoor);
                return;
            }

            _doorGuards.Add(moveable);
            TransferResolved?.Invoke(moveable, destination);
            StartCoroutine(ReleaseDoorGuard(moveable));
        }

        private bool TryResolveDestinationDoor(
            InteractivePawn sourceDoor,
            InteractivePawn moveable,
            out InteractivePawn destinationDoor)
        {
            destinationDoor = null;
            return _settings != null &&
                   _navMeshManager != null &&
                   sourceDoor != null &&
                   moveable != null &&
                   sourceDoor.IsDoor &&
                   moveable.IsMoveable &&
                   !_doorGuards.Contains(moveable) &&
                   !string.IsNullOrWhiteSpace(
                       sourceDoor.LinkedDoorInstanceId) &&
                   _doorsById.TryGetValue(
                       sourceDoor.LinkedDoorInstanceId,
                       out destinationDoor) &&
                   destinationDoor != sourceDoor;
        }

        private IEnumerator ReleaseDoorGuard(InteractivePawn pawn)
        {
            yield return new WaitForSecondsRealtime(
                _settings.DoorGuardSeconds);
            _doorGuards.Remove(pawn);
        }

        private void OnDestroy()
        {
            Unbind();
            TransferResolved = null;
        }
    }
}
