using System;
using System.Collections.Generic;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Trpg.Pawns
{
    public sealed class FieldPawn : Pawn
    {
        private static readonly HashSet<FieldPawn> ActiveFieldPawns =
            new HashSet<FieldPawn>();

        [SerializeField, Tooltip("Floor 또는 Obstacle 데이터. Portrait 데이터는 포함하지 않음")]
        private FieldPawnDefinition _definition;

        [SerializeField, Tooltip("Floor 이동 가능 범위를 표시할 SpriteRenderer")]
        private SpriteRenderer _rangeOverlay;

        [SerializeField, Tooltip("NavMesh 형상과 클릭 판정에 사용할 Collider2D")]
        private Collider2D _navigationCollider;

        [SerializeField, Tooltip("Walkable 또는 Not Walkable을 적용할 Modifier")]
        private NavMeshModifier _navigationModifier;

        [Header("Floor Reachable Overlay")]
        [SerializeField, Tooltip(
            "Floor를 이동 목적지로 사용할 수 있는지 여부. " +
            "FieldPawnDefinition과 분리된 인스턴스 설정입니다.")]
        private bool _destinationEnabled = true;

        [SerializeField, Tooltip("이동 가능 범위 표시 색상")]
        private Color _reachableColor = new Color(0.2f, 0.85f, 1f, 0.35f);

        [SerializeField, Min(0f), Tooltip("이동 가능 범위 표시 페이드 시간(초)")]
        private float _overlayFadeSeconds = 0.15f;

        [Header("World Sorting")]
        [SerializeField, Tooltip(
            "켜면 이 FieldPawn의 Sort Anchor를 기준으로 " +
            "SpriteRenderer의 Order in Layer를 자동 설정합니다. " +
            "Ground처럼 깊이 정렬이 필요 없는 오브젝트는 끕니다.")]
        private bool _layerSetter;

        [SerializeField, Tooltip(
            "Layer Setter가 조절할 SpriteRenderer. " +
            "비어 있으면 RangeOverlay를 제외한 첫 SpriteRenderer를 자동 사용합니다.")]
        private SpriteRenderer _layerRenderer;

        [SerializeField, Tooltip(
            "Layer Renderer가 SortingGroup 안에 있을 경우 실제 월드 정렬을 조절할 그룹. " +
            "비어 있으면 FieldPawn 내부에서 자동으로 찾습니다.")]
        private SortingGroup _layerSortingGroup;

        [SerializeField, Tooltip(
            "단일 Sort Anchor Line의 로컬 Y 위치. " +
            "Scene View에서 FieldPawn을 선택하면 직접 드래그할 수 있습니다.")]
        private float _sortAnchorLocalY;

        [SerializeField, Tooltip(
            "켜면 수평선 하나 대신 여러 점으로 이루어진 Sort Anchor Line을 사용합니다. " +
            "복잡하거나 꺾인 건물의 앞/뒤 경계를 지정할 때 사용합니다.")]
        private bool _useMultiPointSortAnchor;

        [SerializeField, Tooltip(
            "다중 Sort Anchor Line의 로컬 좌표 포인트. " +
            "X 순서대로 연결되며 Scene View에서 각 점을 직접 이동할 수 있습니다.")]
        private List<Vector2> _sortAnchorPoints = new List<Vector2>();

        private Tween _overlayTween;

        public static IReadOnlyCollection<FieldPawn> ActiveInstances =>
            ActiveFieldPawns;

        public FieldPawnDefinition Definition => _definition;
        public FieldPawnKind Kind =>
            _definition != null ? _definition.Kind : FieldPawnKind.Floor;
        public bool IsFloor => Kind == FieldPawnKind.Floor;
        public bool IsObstacle => Kind == FieldPawnKind.Obstacle;
        public bool UsesLayerSetter => _layerSetter;
        public bool UsesMultiPointSortAnchor =>
            _layerSetter &&
            _useMultiPointSortAnchor &&
            _sortAnchorPoints != null &&
            _sortAnchorPoints.Count >= 2;
        public float SortAnchorLocalY => _sortAnchorLocalY;
        public IReadOnlyList<Vector2> SortAnchorPoints => _sortAnchorPoints;
        public SpriteRenderer LayerRenderer => _layerRenderer;
        public bool IsReachable { get; private set; }
        public bool IsDestinationEnabled =>
            IsFloor &&
            _definition != null &&
            _destinationEnabled;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveRegistry()
        {
            ActiveFieldPawns.Clear();
        }

        public override void Bind()
        {
            EnsureNavigationComponents();
            EnsureSortAnchorPoints();
            RefreshLayerLevel();
            base.Bind();
            SetReachable(false);
        }

        public override void Unbind()
        {
            KillOverlayTween();
            IsReachable = false;
            base.Unbind();
        }

        public void PrepareNavigation()
        {
            EnsureNavigationComponents();
        }

        /// <summary>
        /// Layer Setter가 켜진 FieldPawn의 Sort Anchor를
        /// Order in Layer에 즉시 반영합니다.
        /// 단일 선은 _sortAnchorLocalY를 사용하고,
        /// 다중 선은 로컬 X=0 위치의 선 높이를 대표 Order로 사용합니다.
        /// </summary>
        public void RefreshLayerLevel()
        {
            if (!_layerSetter)
                return;

            EnsureLayerRenderer();
            EnsureLayerSortingGroup();

            if (_layerRenderer == null &&
                _layerSortingGroup == null)
            {
                return;
            }

            var anchorWorldY = GetPrimarySortAnchorWorldY();
            var layerLevel = WorldSortOrder.FromWorldY(anchorWorldY);

            // SortingGroup 내부의 SpriteRenderer는 외부 Renderer/Pawn과
            // 비교할 때 Group의 sortingOrder가 우선합니다.
            if (_layerSortingGroup != null)
            {
                _layerSortingGroup.sortingOrder = layerLevel;
            }
            else if (_layerRenderer != null)
            {
                _layerRenderer.sortingOrder = layerLevel;
            }

            if (_rangeOverlay != null &&
                _layerRenderer != null)
            {
                _rangeOverlay.sortingLayerID =
                    _layerRenderer.sortingLayerID;
                _rangeOverlay.sortingOrder = layerLevel + 1;
            }
        }

        /// <summary>
        /// 현재 FieldPawn Renderer의 실제 sortingOrder를 반환합니다.
        /// Renderer를 아직 못 찾았으면 Sort Anchor 기준 예상값을 반환합니다.
        /// </summary>
        public int GetLayerSortOrder()
        {
            EnsureLayerRenderer();
            EnsureLayerSortingGroup();

            if (_layerSortingGroup != null)
                return _layerSortingGroup.sortingOrder;

            if (_layerRenderer != null)
                return _layerRenderer.sortingOrder;

            return WorldSortOrder.FromWorldY(
                GetPrimarySortAnchorWorldY());
        }

        public int GetLayerSortingLayerId()
        {
            EnsureLayerRenderer();
            EnsureLayerSortingGroup();

            if (_layerSortingGroup != null)
                return _layerSortingGroup.sortingLayerID;

            if (_layerRenderer != null)
                return _layerRenderer.sortingLayerID;

            return 0;
        }

        /// <summary>
        /// worldX 위치에서 Sort Anchor Line의 worldY를 계산합니다.
        /// 다중 선일 때는 포인트 X 범위 안에서만 true를 반환합니다.
        /// 단일 선일 때는 Renderer의 가로 범위 안에서만 true를 반환합니다.
        /// </summary>
        public bool TryGetSortAnchorWorldY(
            float worldX,
            out float worldY)
        {
            worldY = 0f;

            if (!_layerSetter)
                return false;

            EnsureLayerRenderer();

            if (UsesMultiPointSortAnchor)
            {
                return TryEvaluateMultiPointWorldY(
                    worldX,
                    false,
                    out worldY);
            }

            if (!TryGetSortLineLocalXRange(
                    out var localMinX,
                    out var localMaxX))
            {
                localMinX = -1f;
                localMaxX = 1f;
            }

            var leftWorld = transform.TransformPoint(
                new Vector3(localMinX, _sortAnchorLocalY, 0f));
            var rightWorld = transform.TransformPoint(
                new Vector3(localMaxX, _sortAnchorLocalY, 0f));

            var worldMinX = Mathf.Min(leftWorld.x, rightWorld.x);
            var worldMaxX = Mathf.Max(leftWorld.x, rightWorld.x);
            if (worldX < worldMinX || worldX > worldMaxX)
                return false;

            if (Mathf.Abs(rightWorld.x - leftWorld.x) <= 0.0001f)
            {
                worldY = (leftWorld.y + rightWorld.y) * 0.5f;
                return true;
            }

            var t = Mathf.InverseLerp(
                leftWorld.x,
                rightWorld.x,
                worldX);
            worldY = Mathf.Lerp(leftWorld.y, rightWorld.y, t);
            return true;
        }

        /// <summary>
        /// Scene 편집용 기본 가로 범위를 FieldPawn 로컬 좌표로 반환합니다.
        /// Layer Renderer가 있으면 Renderer Bounds를 기준으로 계산합니다.
        /// </summary>
        public bool TryGetSortLineLocalXRange(
            out float localMinX,
            out float localMaxX)
        {
            localMinX = -1f;
            localMaxX = 1f;

            EnsureLayerRenderer();
            if (_layerRenderer == null)
                return false;

            var bounds = _layerRenderer.bounds;
            var z = transform.position.z;
            var corners = new[]
            {
                new Vector3(bounds.min.x, bounds.min.y, z),
                new Vector3(bounds.min.x, bounds.max.y, z),
                new Vector3(bounds.max.x, bounds.min.y, z),
                new Vector3(bounds.max.x, bounds.max.y, z)
            };

            localMinX = float.PositiveInfinity;
            localMaxX = float.NegativeInfinity;

            for (var index = 0; index < corners.Length; index++)
            {
                var local = transform.InverseTransformPoint(corners[index]);
                localMinX = Mathf.Min(localMinX, local.x);
                localMaxX = Mathf.Max(localMaxX, local.x);
            }

            if (float.IsInfinity(localMinX) ||
                float.IsInfinity(localMaxX))
            {
                localMinX = -1f;
                localMaxX = 1f;
                return false;
            }

            return true;
        }

        public void SetReachable(bool reachable)
        {
            IsReachable = reachable && IsDestinationEnabled;
            KillOverlayTween();

            if (_rangeOverlay == null)
                return;

            if (!IsReachable)
            {
                _rangeOverlay.enabled = false;
                return;
            }

            _rangeOverlay.enabled = true;
            var targetColor = _reachableColor;
            var startColor = targetColor;
            startColor.a = 0f;
            _rangeOverlay.color = startColor;

            _overlayTween = DOTween.To(
                () => _rangeOverlay.color,
                value => _rangeOverlay.color = value,
                targetColor,
                _overlayFadeSeconds);
        }

        private void Awake()
        {
            EnsureNavigationComponents();
            EnsureSortAnchorPoints();
            RefreshLayerLevel();
        }

        private void OnEnable()
        {
            ActiveFieldPawns.Add(this);
        }

        private void OnDisable()
        {
            ActiveFieldPawns.Remove(this);
        }

        private void Reset()
        {
            EnsureNavigationComponents();
            EnsureSortAnchorPoints();
            RefreshLayerLevel();
        }

        private float GetPrimarySortAnchorWorldY()
        {
            if (UsesMultiPointSortAnchor &&
                TryEvaluateMultiPointLocalY(
                    0f,
                    true,
                    out var multiLocalY))
            {
                return transform.TransformPoint(
                    new Vector3(0f, multiLocalY, 0f)).y;
            }

            return transform.TransformPoint(
                new Vector3(0f, _sortAnchorLocalY, 0f)).y;
        }

        private bool TryEvaluateMultiPointWorldY(
            float worldX,
            bool clampOutside,
            out float worldY)
        {
            worldY = 0f;

            if (_sortAnchorPoints == null ||
                _sortAnchorPoints.Count < 2)
            {
                return false;
            }

            var firstWorld = transform.TransformPoint(
                ToVector3(_sortAnchorPoints[0]));
            var lastWorld = transform.TransformPoint(
                ToVector3(_sortAnchorPoints[_sortAnchorPoints.Count - 1]));

            var minWorldX = Mathf.Min(firstWorld.x, lastWorld.x);
            var maxWorldX = Mathf.Max(firstWorld.x, lastWorld.x);
            if (!clampOutside &&
                (worldX < minWorldX || worldX > maxWorldX))
            {
                return false;
            }

            if (worldX <= minWorldX)
            {
                worldY = firstWorld.x <= lastWorld.x
                    ? firstWorld.y
                    : lastWorld.y;
                return true;
            }

            if (worldX >= maxWorldX)
            {
                worldY = firstWorld.x >= lastWorld.x
                    ? firstWorld.y
                    : lastWorld.y;
                return true;
            }

            for (var index = 0;
                 index < _sortAnchorPoints.Count - 1;
                 index++)
            {
                var a = transform.TransformPoint(
                    ToVector3(_sortAnchorPoints[index]));
                var b = transform.TransformPoint(
                    ToVector3(_sortAnchorPoints[index + 1]));

                var segmentMinX = Mathf.Min(a.x, b.x);
                var segmentMaxX = Mathf.Max(a.x, b.x);
                if (worldX < segmentMinX || worldX > segmentMaxX)
                    continue;

                var deltaX = b.x - a.x;
                if (Mathf.Abs(deltaX) <= 0.0001f)
                {
                    worldY = (a.y + b.y) * 0.5f;
                    return true;
                }

                var t = Mathf.InverseLerp(a.x, b.x, worldX);
                worldY = Mathf.Lerp(a.y, b.y, t);
                return true;
            }

            return false;
        }

        private bool TryEvaluateMultiPointLocalY(
            float localX,
            bool clampOutside,
            out float localY)
        {
            localY = 0f;

            if (_sortAnchorPoints == null ||
                _sortAnchorPoints.Count < 2)
            {
                return false;
            }

            var first = _sortAnchorPoints[0];
            var last = _sortAnchorPoints[_sortAnchorPoints.Count - 1];

            if (!clampOutside &&
                (localX < first.x || localX > last.x))
            {
                return false;
            }

            if (localX <= first.x)
            {
                localY = first.y;
                return true;
            }

            if (localX >= last.x)
            {
                localY = last.y;
                return true;
            }

            for (var index = 0;
                 index < _sortAnchorPoints.Count - 1;
                 index++)
            {
                var a = _sortAnchorPoints[index];
                var b = _sortAnchorPoints[index + 1];
                if (localX < a.x || localX > b.x)
                    continue;

                var deltaX = b.x - a.x;
                if (Mathf.Abs(deltaX) <= 0.0001f)
                {
                    localY = (a.y + b.y) * 0.5f;
                    return true;
                }

                var t = Mathf.InverseLerp(a.x, b.x, localX);
                localY = Mathf.Lerp(a.y, b.y, t);
                return true;
            }

            return false;
        }

        private void EnsureSortAnchorPoints()
        {
            if (!_useMultiPointSortAnchor)
                return;

            if (_sortAnchorPoints == null)
                _sortAnchorPoints = new List<Vector2>();

            if (_sortAnchorPoints.Count < 2)
                ResetSortAnchorPointsToRenderer();

            _sortAnchorPoints.Sort(
                (left, right) => left.x.CompareTo(right.x));

            for (var index = 1;
                 index < _sortAnchorPoints.Count;
                 index++)
            {
                var previous = _sortAnchorPoints[index - 1];
                var current = _sortAnchorPoints[index];
                if (current.x - previous.x >= 0.001f)
                    continue;

                current.x = previous.x + 0.001f;
                _sortAnchorPoints[index] = current;
            }
        }

        private void ResetSortAnchorPointsToRenderer()
        {
            if (_sortAnchorPoints == null)
                _sortAnchorPoints = new List<Vector2>();

            _sortAnchorPoints.Clear();

            if (!TryGetSortLineLocalXRange(
                    out var localMinX,
                    out var localMaxX))
            {
                localMinX = -1f;
                localMaxX = 1f;
            }

            var centerX = (localMinX + localMaxX) * 0.5f;
            _sortAnchorPoints.Add(
                new Vector2(localMinX, _sortAnchorLocalY));
            _sortAnchorPoints.Add(
                new Vector2(centerX, _sortAnchorLocalY));
            _sortAnchorPoints.Add(
                new Vector2(localMaxX, _sortAnchorLocalY));
        }

        private void EnsureNavigationComponents()
        {
            if (_definition == null)
                return;

            if (_navigationCollider == null)
            {
                _navigationCollider =
                    GetComponentInChildren<Collider2D>(true);
            }

            if (_navigationCollider == null)
            {
                _navigationCollider =
                    gameObject.AddComponent<BoxCollider2D>();
            }

            if (_navigationModifier == null)
            {
                _navigationModifier = GetComponent<NavMeshModifier>();
            }

            if (_navigationModifier == null)
            {
                _navigationModifier =
                    gameObject.AddComponent<NavMeshModifier>();
            }

            var areaName = IsObstacle ? "Not Walkable" : "Walkable";
            var fallbackArea = IsObstacle ? 1 : 0;
            var area = NavMesh.GetAreaFromName(areaName);
            _navigationModifier.overrideArea = true;
            _navigationModifier.area = area >= 0 ? area : fallbackArea;

            if (IsFloor)
                EnsureRangeOverlay();
        }

        private void EnsureRangeOverlay()
        {
            if (_rangeOverlay != null)
            {
                _rangeOverlay.enabled = false;
                return;
            }

            var source = GetComponentInChildren<SpriteRenderer>(true);
            if (source == null || source.sprite == null)
                return;

            var overlayObject = new GameObject("RangeOverlay");
            overlayObject.transform.SetParent(transform, false);
            _rangeOverlay = overlayObject.AddComponent<SpriteRenderer>();
            _rangeOverlay.sprite = source.sprite;
            _rangeOverlay.sortingLayerID = source.sortingLayerID;
            _rangeOverlay.sortingOrder = source.sortingOrder + 1;
            _rangeOverlay.color = Color.clear;
            _rangeOverlay.enabled = false;
        }

        private void EnsureLayerRenderer()
        {
            if (_layerRenderer != null &&
                _layerRenderer != _rangeOverlay)
            {
                return;
            }

            var renderers =
                GetComponentsInChildren<SpriteRenderer>(true);

            for (var index = 0; index < renderers.Length; index++)
            {
                var candidate = renderers[index];
                if (candidate == null ||
                    candidate == _rangeOverlay)
                {
                    continue;
                }

                _layerRenderer = candidate;
                return;
            }

            _layerRenderer = null;
        }

        private void EnsureLayerSortingGroup()
        {
            if (_layerSortingGroup != null)
                return;

            // FieldPawn 루트에 SortingGroup이 있으면 가장 우선합니다.
            _layerSortingGroup = GetComponent<SortingGroup>();
            if (_layerSortingGroup != null)
                return;

            EnsureLayerRenderer();
            if (_layerRenderer == null)
                return;

            // Renderer가 자식 SortingGroup 안에 있을 경우 그 Group을 사용합니다.
            var candidate =
                _layerRenderer.GetComponentInParent<SortingGroup>();

            if (candidate == null)
                return;

            if (candidate.transform == transform ||
                candidate.transform.IsChildOf(transform))
            {
                _layerSortingGroup = candidate;
            }
        }

        private void KillOverlayTween()
        {
            _overlayTween?.Kill();
            _overlayTween = null;
        }

        private static Vector3 ToVector3(Vector2 value)
        {
            return new Vector3(value.x, value.y, 0f);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureNavigationComponents();
            EnsureSortAnchorPoints();
            RefreshLayerLevel();

            if (_definition == null)
            {
                Debug.LogError(
                    $"[{name}] Field Definition이 비어 있습니다.",
                    this);
            }
        }
#endif
    }
}
