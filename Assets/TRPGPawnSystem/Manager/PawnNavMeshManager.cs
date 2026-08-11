using System;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using NavMeshPlus.Extensions;
using UnityEngine;
using UnityEngine.AI;

namespace Trpg.Pawns
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshSurface))]
    [RequireComponent(typeof(CollectSources2d))]
    public sealed class PawnNavMeshManager : MonoBehaviour
    {
        [SerializeField, Tooltip("경로 계산에 사용할 NavMesh Area Mask")]
        private int _areaMask = NavMesh.AllAreas;

        [SerializeField, Tooltip("2D NavMesh가 놓인 Z 좌표")]
        private float _navigationPlaneZ;

        [SerializeField, Tooltip("씬 시작 직후 FieldPawn을 조회하고 자동 Bake")]
        private bool _buildOnStart = true;

        private readonly List<FieldPawn> _fieldPawns =
            new List<FieldPawn>();
        private readonly List<Collider2D>
            _temporarilyDisabledInteractiveColliders =
                new List<Collider2D>();

        private NavMeshSurface _surface;
        private CollectSources2d _sources2d;

        public bool IsRuntimeBakeReady { get; private set; }
        public float NavigationPlaneZ => _navigationPlaneZ;
        public event Action RuntimeBakeCompleted;

        public void Rebuild()
        {
            IsRuntimeBakeReady = false;
            EnsureNavigationComponents();
            ConfigureFor2D();
            PrepareFieldPawns();

            // NavMesh에는 정적 FieldPawn만 반영하고, 이동하는 Pawn은
            // PawnMovementManager의 동적 점유 판정으로 별도 처리한다.
            DisableInteractivePawnCollidersForBake();
            try
            {
                Physics2D.SyncTransforms();
                _surface.BuildNavMesh();
                IsRuntimeBakeReady = true;
            }
            finally
            {
                RestoreInteractivePawnCollidersAfterBake();
                Physics2D.SyncTransforms();
            }

            LogBakeResult();
            RuntimeBakeCompleted?.Invoke();
        }

        public bool TryProject(
            Vector2 worldPosition,
            float maxProjectionMeters,
            out Vector2 projected)
        {
            var source = new Vector3(
                worldPosition.x,
                worldPosition.y,
                _navigationPlaneZ);

            if (NavMesh.SamplePosition(
                    source,
                    out var hit,
                    maxProjectionMeters,
                    _areaMask))
            {
                projected = hit.position;
                return true;
            }

            projected = default;
            return false;
        }

        public bool TryCalculatePath(
            Vector2 from,
            Vector2 to,
            float maxProjectionMeters,
            out Vector3[] corners,
            out float pathLength)
        {
            corners = System.Array.Empty<Vector3>();
            pathLength = 0f;

            if (!TryProject(from, maxProjectionMeters, out var start) ||
                !TryProject(to, maxProjectionMeters, out var destination))
            {
                return false;
            }

            var path = new NavMeshPath();
            var startWorld = new Vector3(
                start.x,
                start.y,
                _navigationPlaneZ);
            var destinationWorld = new Vector3(
                destination.x,
                destination.y,
                _navigationPlaneZ);

            if (!NavMesh.CalculatePath(
                    startWorld,
                    destinationWorld,
                    _areaMask,
                    path) ||
                path.status != NavMeshPathStatus.PathComplete ||
                path.corners.Length == 0)
            {
                return false;
            }

            corners = path.corners;
            for (var index = 1; index < corners.Length; index++)
            {
                pathLength += Vector3.Distance(
                    corners[index - 1],
                    corners[index]);
            }

            return true;
        }

        public bool IsDirectlyWalkable(Vector2 from, Vector2 to)
        {
            var start = new Vector3(
                from.x,
                from.y,
                _navigationPlaneZ);
            var end = new Vector3(
                to.x,
                to.y,
                _navigationPlaneZ);

            return !NavMesh.Raycast(
                start,
                end,
                out _,
                _areaMask);
        }

        private void Awake()
        {
            EnsureNavigationComponents();
            ConfigureFor2D();
        }

        private IEnumerator Start()
        {
            if (!_buildOnStart)
            {
                yield break;
            }

            yield return new WaitForFixedUpdate();
            Rebuild();
        }

        private void Reset()
        {
            EnsureNavigationComponents();
            ConfigureFor2D();
        }

        private void EnsureNavigationComponents()
        {
            if (!TryGetComponent(out _surface))
            {
                _surface = gameObject.AddComponent<NavMeshSurface>();
            }

            if (!TryGetComponent(out _sources2d))
            {
                _sources2d = gameObject.AddComponent<CollectSources2d>();
            }
        }

        private void ConfigureFor2D()
        {
            transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            _surface.collectObjects = CollectObjects.All;
            _surface.useGeometry =
                NavMeshCollectGeometry.PhysicsColliders;

            var walkableArea = NavMesh.GetAreaFromName("Walkable");
            if (walkableArea >= 0)
            {
                _surface.defaultArea = walkableArea;
            }
        }

        private void PrepareFieldPawns()
        {
            _fieldPawns.Clear();

#if UNITY_2022_2_OR_NEWER
            var found = FindObjectsByType<FieldPawn>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            var found = FindObjectsOfType<FieldPawn>(false);
#endif

            for (var index = 0; index < found.Length; index++)
            {
                var fieldPawn = found[index];
                if (fieldPawn == null)
                {
                    continue;
                }

                fieldPawn.PrepareNavigation();
                _fieldPawns.Add(fieldPawn);
            }

            if (_fieldPawns.Count == 0)
            {
                Debug.LogWarning(
                    $"[{name}] 활성화된 FieldPawn이 하나도 없습니다.",
                    this);
            }
        }

        private void DisableInteractivePawnCollidersForBake()
        {
            _temporarilyDisabledInteractiveColliders.Clear();

#if UNITY_2022_2_OR_NEWER
            var pawns = FindObjectsByType<InteractivePawn>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            var pawns = FindObjectsOfType<InteractivePawn>(false);
#endif

            for (var pawnIndex = 0; pawnIndex < pawns.Length; pawnIndex++)
            {
                var pawn = pawns[pawnIndex];
                if (pawn == null)
                    continue;

                var colliders =
                    pawn.GetComponentsInChildren<Collider2D>(true);
                for (var colliderIndex = 0;
                     colliderIndex < colliders.Length;
                     colliderIndex++)
                {
                    var collider = colliders[colliderIndex];
                    if (collider == null || !collider.enabled)
                        continue;

                    collider.enabled = false;
                    _temporarilyDisabledInteractiveColliders.Add(collider);
                }
            }
        }

        private void RestoreInteractivePawnCollidersAfterBake()
        {
            for (var index = 0;
                 index < _temporarilyDisabledInteractiveColliders.Count;
                 index++)
            {
                var collider =
                    _temporarilyDisabledInteractiveColliders[index];
                if (collider != null)
                    collider.enabled = true;
            }

            _temporarilyDisabledInteractiveColliders.Clear();
        }

        private void LogBakeResult()
        {
            var floorCount = 0;
            var obstacleCount = 0;

            for (var index = 0; index < _fieldPawns.Count; index++)
            {
                if (_fieldPawns[index].IsFloor)
                {
                    floorCount++;
                }
                else if (_fieldPawns[index].IsObstacle)
                {
                    obstacleCount++;
                }
            }

            Debug.Log(
                $"[{name}] 런타임 NavMesh Bake 완료. " +
                $"Floor {floorCount}, Obstacle {obstacleCount}",
                this);
        }
    }
}
