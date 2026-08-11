using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Trpg.Pawns
{
    /// <summary>
    /// InteractivePawn 전체를 Y 좌표 기준으로 월드 깊이 정렬합니다.
    /// Pawn 내부 파츠의 기존 sortingOrder는 건드리지 않고,
    /// Pawn/Visual의 SortingGroup order만 변경합니다.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PawnWorldSortingManager : MonoBehaviour
    {
        [SerializeField] private PawnManager _pawnManager;

        private readonly Dictionary<InteractivePawn, SortingGroup>
            _groups = new Dictionary<InteractivePawn, SortingGroup>();

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            var pawnManager = FindFirst<PawnManager>();
            if (pawnManager == null)
                return;

            var manager =
                pawnManager.GetComponent<PawnWorldSortingManager>();
            if (manager == null)
            {
                manager = pawnManager.gameObject.AddComponent<
                    PawnWorldSortingManager>();
            }

            manager.Configure(pawnManager);
        }

        public void Configure(PawnManager pawnManager)
        {
            _pawnManager = pawnManager;
            _groups.Clear();
            RefreshAllImmediately();
        }

        public void RefreshAllImmediately()
        {
            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();
            if (_pawnManager == null)
                return;

            var pawns = _pawnManager.InteractivePawns;
            if (pawns == null)
                return;

            for (var index = 0; index < pawns.Count; index++)
            {
                var pawn = pawns[index];
                if (pawn == null)
                    continue;

                RefreshPawn(pawn);
            }
        }

        private void Awake()
        {
            if (_pawnManager == null)
                _pawnManager = GetComponent<PawnManager>();
            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();
        }

        private void OnDisable()
        {
            _groups.Clear();
        }

        private void LateUpdate()
        {
            if (_pawnManager == null)
            {
                _pawnManager = FindFirst<PawnManager>();
                if (_pawnManager == null)
                    return;
            }

            var pawns = _pawnManager.InteractivePawns;
            if (pawns == null)
                return;

            for (var index = 0; index < pawns.Count; index++)
            {
                var pawn = pawns[index];
                if (pawn == null)
                    continue;

                RefreshPawn(pawn);
            }
        }

        private void RefreshPawn(InteractivePawn pawn)
        {
            var group = EnsureSortingGroup(pawn);
            if (group == null)
                return;

            group.sortingOrder = WorldSortOrder.FromWorldY(
                pawn.PresentationWorldPosition.y);
        }

        private SortingGroup EnsureSortingGroup(InteractivePawn pawn)
        {
            if (pawn == null)
                return null;

            if (_groups.TryGetValue(pawn, out var cached) &&
                cached != null)
            {
                return cached;
            }

            var group = pawn.GetComponent<SortingGroup>();
            if (group == null)
            {
                group = pawn.GetComponentInChildren<SortingGroup>(true);
            }

            var created = false;
            if (group == null)
            {
                group = pawn.gameObject.AddComponent<SortingGroup>();
                created = true;
            }

            if (created)
                CopySortingLayerFromVisual(pawn, group);

            _groups[pawn] = group;
            return group;
        }

        private static void CopySortingLayerFromVisual(
            InteractivePawn pawn,
            SortingGroup group)
        {
            if (pawn == null || group == null)
                return;

            var renderers =
                pawn.GetComponentsInChildren<SpriteRenderer>(true);

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                    continue;

                group.sortingLayerID = renderer.sortingLayerID;
                return;
            }
        }

        private static T FindFirst<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }
    }
}
