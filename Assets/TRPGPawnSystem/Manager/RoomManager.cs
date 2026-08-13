using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    public enum RoomLayoutMode
    {
        Manual,
        Horizontal,
        Vertical,
        Packed
    }

    [Serializable]
    public sealed class RoomDoorLinkDefinition
    {
        [SerializeField]
        private bool _enabled = true;

        [SerializeField, HideInInspector]
        private string _doorId;

        [SerializeField, Tooltip(
            "Inspector와 Bake Hierarchy에 표시할 이름")]
        private string _displayName = "Door";

        [SerializeField, Tooltip(
            "Scene View에서 이 Door Pair를 구분하는 색상. Door 생성 시 자동으로 서로 다른 색이 지정됩니다.")]
        private Color _editorColor =
            new Color(0f, 0f, 0f, 0f);

        [Header("Room Door - Room Local")]
        [SerializeField, Tooltip(
            "이 RoomPlacement의 Baked Room 원점 기준 로컬 Door 위치(m). " +
            "Room Layout이 바뀌면 Room과 함께 이동합니다.")]
        private Vector2 _roomDoorPosition;

        [SerializeField, Tooltip(
            "Room Door의 Z 회전 각도")]
        private float _roomDoorRotation;

        [SerializeField, Tooltip(
            "Room Door Trigger Collider 크기(m). X/Y 자유 조절.")]
        private Vector2 _roomDoorColliderSize =
            new Vector2(1f, 1f);

        [Header("Arrive Door - Global World")]
        [SerializeField, Tooltip(
            "Room 밖에 생성될 Arrive Door의 Scene World 좌표(m). " +
            "Room Layout이 바뀌어도 이 값은 글로벌 좌표로 유지됩니다.")]
        private Vector2 _arriveDoorPosition;

        [SerializeField, Tooltip(
            "Arrive Door의 Z 회전 각도")]
        private float _arriveDoorRotation;

        [SerializeField, Tooltip(
            "Arrive Door Trigger Collider 크기(m). X/Y 자유 조절.")]
        private Vector2 _arriveDoorColliderSize =
            new Vector2(1f, 1f);

        public bool Enabled => _enabled;
        public string DoorId => _doorId;
        public string DisplayName =>
            string.IsNullOrWhiteSpace(_displayName)
                ? "Door"
                : _displayName;
        public Color EditorColor =>
            _editorColor.a > 0.001f
                ? _editorColor
                : Color.white;
        public Vector2 RoomDoorPosition => _roomDoorPosition;
        public float RoomDoorRotation => _roomDoorRotation;
        public Vector2 RoomDoorColliderSize =>
            ClampColliderSize(_roomDoorColliderSize);
        public Vector2 ArriveDoorPosition => _arriveDoorPosition;
        public float ArriveDoorRotation => _arriveDoorRotation;
        public Vector2 ArriveDoorColliderSize =>
            ClampColliderSize(_arriveDoorColliderSize);

#if UNITY_EDITOR
        public void EditorEnsureIdentity(int index)
        {
            if (string.IsNullOrWhiteSpace(_doorId))
            {
                _doorId =
                    Guid.NewGuid()
                        .ToString("N");
            }

            if (string.IsNullOrWhiteSpace(_displayName))
                _displayName = $"Door_{index + 1:00}";

            if (_editorColor.a <= 0.001f)
            {
                _editorColor =
                    CreateEditorColor(
                        _doorId);
            }

            _roomDoorColliderSize =
                ClampColliderSize(
                    _roomDoorColliderSize);

            _arriveDoorColliderSize =
                ClampColliderSize(
                    _arriveDoorColliderSize);
        }

        public void EditorSetRoomDoorPosition(Vector2 value)
        {
            _roomDoorPosition = value;
        }

        public void EditorSetRoomDoorColliderSize(Vector2 value)
        {
            _roomDoorColliderSize =
                ClampColliderSize(value);
        }

        public void EditorSetArriveDoorPosition(Vector2 value)
        {
            _arriveDoorPosition = value;
        }

        public void EditorSetArriveDoorColliderSize(Vector2 value)
        {
            _arriveDoorColliderSize =
                ClampColliderSize(value);
        }

        public void EditorSetEditorColor(Color value)
        {
            value.a = 1f;
            _editorColor = value;
        }

        private static Color CreateEditorColor(string seed)
        {
            unchecked
            {
                var hash = 17;

                if (!string.IsNullOrEmpty(seed))
                {
                    for (var index = 0;
                         index < seed.Length;
                         index++)
                    {
                        hash =
                            hash * 31 +
                            seed[index];
                    }
                }

                var positive =
                    hash & 0x7fffffff;

                var hue =
                    (positive % 10000) /
                    10000f;

                return Color.HSVToRGB(
                    hue,
                    0.68f,
                    1f);
            }
        }
#endif

        private static Vector2 ClampColliderSize(Vector2 value)
        {
            return new Vector2(
                Mathf.Max(0.05f, value.x),
                Mathf.Max(0.05f, value.y));
        }
    }

    [Serializable]
    public sealed class RoomPlacement
    {
        [SerializeField]
        private bool _enabled = true;

        [SerializeField, HideInInspector]
        private string _placementId;

        [SerializeField, Tooltip(
            "Inspector/Hierarchy 구분용 이름")]
        private string _displayName;

        [SerializeField]
        private RoomPrefab _prefab;

        [SerializeField, Tooltip(
            "Manual Layout일 때 RoomManager 기준 로컬 위치(m)")]
        private Vector2 _localPosition;

        [SerializeField]
        private float _rotationDegrees;

        [SerializeField, Tooltip(
            "이 RoomPlacement에 직접 저장되는 Door Pair 목록. " +
            "RoomDoorPosition은 이 Room 기준 로컬값, " +
            "ArriveDoorPosition은 Scene 글로벌값입니다. " +
            "RoomPrefab Asset에는 Door 데이터가 저장되지 않습니다.")]
        private List<RoomDoorLinkDefinition> _doorLinks =
            new List<RoomDoorLinkDefinition>();

        public bool Enabled => _enabled;
        public string PlacementId => _placementId;
        public string DisplayName =>
            !string.IsNullOrWhiteSpace(_displayName)
                ? _displayName
                : _prefab != null
                    ? _prefab.name
                    : "Room";
        public RoomPrefab Prefab => _prefab;
        public Vector2 LocalPosition => _localPosition;
        public float RotationDegrees => _rotationDegrees;
        public IReadOnlyList<RoomDoorLinkDefinition> DoorLinks =>
            _doorLinks;

#if UNITY_EDITOR
        public void EditorEnsureIdentity(
            HashSet<string> usedIds,
            int index)
        {
            var needsNewId =
                string.IsNullOrWhiteSpace(_placementId) ||
                (usedIds != null &&
                 usedIds.Contains(_placementId));

            if (needsNewId)
            {
                _placementId =
                    Guid.NewGuid()
                        .ToString("N");
            }

            usedIds?.Add(_placementId);

            if (string.IsNullOrWhiteSpace(_displayName))
            {
                _displayName =
                    _prefab != null
                        ? _prefab.name
                        : $"Room_{index + 1:00}";
            }

            if (_doorLinks == null)
            {
                _doorLinks =
                    new List<RoomDoorLinkDefinition>();
            }

            for (var doorIndex = 0;
                 doorIndex < _doorLinks.Count;
                 doorIndex++)
            {
                _doorLinks[doorIndex]?.EditorEnsureIdentity(
                    doorIndex);
            }
        }

        public RoomDoorLinkDefinition EditorGetDoorLink(int index)
        {
            if (_doorLinks == null ||
                index < 0 ||
                index >= _doorLinks.Count)
            {
                return null;
            }

            return _doorLinks[index];
        }
#endif
    }

    [Serializable]
    public sealed class BakedFogRoom
    {
        [SerializeField]
        private string _mapInstanceId;

        [SerializeField]
        private string _roomId;

        [SerializeField]
        private Transform _mapRoot;

        [SerializeField]
        private List<Vector2> _localPoints =
            new List<Vector2>();

        [SerializeField]
        private MeshFilter _fogMeshFilter;

        [SerializeField]
        private MeshRenderer _fogRenderer;

        [NonSerialized]
        private Mesh _runtimeMesh;

        public string MapInstanceId => _mapInstanceId;
        public string RoomId => _roomId;
        public Transform MapRoot => _mapRoot;
        public IReadOnlyList<Vector2> LocalPoints => _localPoints;
        public MeshFilter FogMeshFilter => _fogMeshFilter;
        public MeshRenderer FogRenderer => _fogRenderer;

        public bool IsUsable =>
            _mapRoot != null &&
            _localPoints != null &&
            _localPoints.Count >= 3 &&
            _fogMeshFilter != null &&
            _fogRenderer != null;

        public bool Contains(Vector2 worldPosition)
        {
            if (!IsUsable)
                return false;

            var local3 = _mapRoot.InverseTransformPoint(worldPosition);
            var local = new Vector2(local3.x, local3.y);

            if (IsOnBoundary(local, _localPoints))
                return true;

            var inside = false;
            var previous = _localPoints.Count - 1;

            for (var index = 0;
                 index < _localPoints.Count;
                 index++)
            {
                var a = _localPoints[index];
                var b = _localPoints[previous];

                if ((a.y > local.y) != (b.y > local.y))
                {
                    var denominator = b.y - a.y;
                    if (Mathf.Abs(denominator) > 0.00001f)
                    {
                        var crossX =
                            (b.x - a.x) *
                            (local.y - a.y) /
                            denominator +
                            a.x;

                        if (local.x < crossX)
                            inside = !inside;
                    }
                }

                previous = index;
            }

            return inside;
        }

        public float GetWorldArea()
        {
            if (_mapRoot == null ||
                _localPoints == null ||
                _localPoints.Count < 3)
            {
                return 0f;
            }

            var sum = 0f;
            for (var index = 0;
                 index < _localPoints.Count;
                 index++)
            {
                var next = (index + 1) % _localPoints.Count;
                var a = _mapRoot.TransformPoint(_localPoints[index]);
                var b = _mapRoot.TransformPoint(_localPoints[next]);
                sum += a.x * b.y - b.x * a.y;
            }

            return Mathf.Abs(sum * 0.5f);
        }

        public bool TryGetWorldBounds(out Bounds bounds)
        {
            bounds = default;

            if (_mapRoot == null ||
                _localPoints == null ||
                _localPoints.Count < 3)
            {
                return false;
            }

            var first =
                _mapRoot.TransformPoint(_localPoints[0]);

            bounds =
                new Bounds(
                    first,
                    Vector3.zero);

            for (var index = 1;
                 index < _localPoints.Count;
                 index++)
            {
                bounds.Encapsulate(
                    _mapRoot.TransformPoint(
                        _localPoints[index]));
            }

            return true;
        }

        public void SetFogVisible(bool visible)
        {
            if (_fogRenderer != null)
                _fogRenderer.enabled = visible;
        }

        public void BuildRuntimeMesh(Material material)
        {
            ReleaseRuntimeMesh();

            if (!IsUsable ||
                !RoomManager.TryTriangulate(
                    _localPoints,
                    out var vertices,
                    out var triangles))
            {
                if (_fogRenderer != null)
                    _fogRenderer.enabled = false;
                return;
            }

            _runtimeMesh = new Mesh
            {
                name = $"{_mapInstanceId}_{_roomId}_FogMesh"
            };

            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            for (var index = 0; index < colors.Length; index++)
                colors[index] = Color.white;

            _runtimeMesh.vertices = vertices;
            _runtimeMesh.triangles = triangles;
            _runtimeMesh.uv = uv;
            _runtimeMesh.colors = colors;
            _runtimeMesh.RecalculateBounds();

            _fogMeshFilter.sharedMesh = _runtimeMesh;
            if (material != null)
                _fogRenderer.sharedMaterial = material;
        }

        public void ReleaseRuntimeMesh()
        {
            if (_runtimeMesh == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_runtimeMesh);
            else
                UnityEngine.Object.DestroyImmediate(_runtimeMesh);

            _runtimeMesh = null;
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            string mapInstanceId,
            string roomId,
            Transform mapRoot,
            IReadOnlyList<Vector2> localPoints,
            MeshFilter filter,
            MeshRenderer renderer)
        {
            _mapInstanceId = mapInstanceId ?? string.Empty;
            _roomId = roomId ?? string.Empty;
            _mapRoot = mapRoot;
            _fogMeshFilter = filter;
            _fogRenderer = renderer;

            if (_localPoints == null)
                _localPoints = new List<Vector2>();
            else
                _localPoints.Clear();

            if (localPoints == null)
                return;

            for (var index = 0;
                 index < localPoints.Count;
                 index++)
            {
                _localPoints.Add(localPoints[index]);
            }
        }
#endif

        private static bool IsOnBoundary(
            Vector2 point,
            IReadOnlyList<Vector2> polygon)
        {
            const float epsilon = 0.00001f;

            for (var index = 0;
                 index < polygon.Count;
                 index++)
            {
                var next = (index + 1) % polygon.Count;
                if (DistanceToSegmentSquared(
                        point,
                        polygon[index],
                        polygon[next]) <= epsilon * epsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private static float DistanceToSegmentSquared(
            Vector2 point,
            Vector2 a,
            Vector2 b)
        {
            var ab = b - a;
            if (ab.sqrMagnitude <= 0.00001f)
                return (point - a).sqrMagnitude;

            var t = Mathf.Clamp01(
                Vector2.Dot(point - a, ab) /
                ab.sqrMagnitude);

            var closest = a + ab * t;
            return (point - closest).sqrMagnitude;
        }
    }

    /// <summary>
    /// Scene에 하나 두는 Room/Map Manager입니다.
    ///
    /// Editor:
    /// - RoomPrefab 목록을 읽어 __BakedRooms 생성
    /// - Map/Wall을 1 Unity Unit = 1m 크기로 배치
    /// - 기존 FieldPawn + PolygonCollider2D를 생성해 기존 NavMesh 시스템에 연결
    /// - RoomPrefab의 Fog Polygon은 원본에 남기고, Bake 결과만 재생성
    ///
    /// Runtime:
    /// - GM은 모든 Fog OFF
    /// - Player는 자기 Pawn이 위치한 Fog Room만 OFF
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomManager : MonoBehaviour
    {
        [Header("Existing System References")]
        [SerializeField]
        private PawnManager _pawnManager;

        [SerializeField]
        private TRPGSessionAuthority _sessionAuthority;

        [Header("Default Field Definitions")]
        [SerializeField]
        private FieldPawnDefinition _defaultFloorDefinition;

        [SerializeField]
        private FieldPawnDefinition _defaultObstacleDefinition;

        [Header("Room Prefab Placement")]
        [SerializeField]
        private RoomLayoutMode _layoutMode = RoomLayoutMode.Manual;

        [SerializeField, Min(0f)]
        private float _autoLayoutGapMeters = 2f;

        [SerializeField, Min(1), Tooltip(
            "Packed Layout의 가로 슬롯 수. 예: 4면 4 x N 형태로 배치합니다.")]
        private int _packedColumns = 4;

        [SerializeField, Min(0), Tooltip(
            "Packed Layout의 세로 슬롯 수. 0이면 필요한 만큼 자동(N)으로 늘어납니다. " +
            "예: Columns=4, Rows=4면 4 x 4 기준으로 배치합니다.")]
        private int _packedRows;

        [SerializeField]
        private List<RoomPlacement> _roomPrefabs =
            new List<RoomPlacement>();

        [Header("Bake")]
        [SerializeField, Tooltip(
            "Play를 시작하기 직전에 RoomPrefab 전체를 1회 Bake합니다. " +
            "Inspector에서 리스트나 값을 수정하는 것만으로는 Bake하지 않습니다.")]
        private bool _bakeOnPlayStart = true;

        [SerializeField, Tooltip(
            "수동 Bake 또는 Play 시작 전 Bake 직후 PawnNavMeshManager.Rebuild()를 호출합니다. " +
            "PawnNavMeshManager가 없으면 Editor Bake 과정에서 자동 생성합니다.")]
        private bool _rebuildNavMeshAfterEditorBake = true;

        [Header("Door Bake")]
        [SerializeField, Tooltip(
            "각 Room Prefabs Element의 Door Links를 Bake할 때 공통으로 사용할 " +
            "InteractivePawnDefinition. 반드시 Door 역할(IsDoor=true)이어야 합니다.")]
        private InteractivePawnDefinition _doorDefinition;

        [Header("Fog")]
        [SerializeField, Tooltip(
            "모든 월드 Sprite보다 위에 있는 전용 Sorting Layer를 권장합니다.")]
        private string _fogSortingLayerName = "RoomFog";

        [SerializeField]
        private int _fogSortingOrder = 30000;

        [SerializeField, Range(0f, 1f)]
        private float _fogAlpha = 1f;

        [SerializeField, Tooltip(
            "비워두면 런타임에 URP 2D Unlit 기반 검은 Material을 생성합니다.")]
        private Material _fogMaterial;

        [Header("Room Camera Limit")]
        [SerializeField, Tooltip(
            "선택된/활성 Pawn이 Fog Room 안에 있을 때 Board Camera의 이동 범위를 " +
            "그 Room의 월드 Bounds로 제한합니다. Pawn 선택을 해제하면 제한도 해제됩니다.")]
        private bool _confineCameraToActivePawnRoom = true;

        [SerializeField, Tooltip(
            "Door 이동 완료 직후 활성 Pawn 위치로 Board Camera를 즉시 이동합니다.")]
        private bool _snapCameraToActivePawnAfterDoor = true;

        [SerializeField, Min(0f), Tooltip(
            "Camera가 Room 경계에 완전히 붙지 않도록 추가로 안쪽에 두는 여백(m).")]
        private float _cameraRoomEdgePaddingMeters = 0.10f;

        [Header("Offline Test")]
        [SerializeField]
        private bool _offlineAsGameMaster = true;

        [SerializeField, Min(0.02f)]
        private float _visibilityRefreshSeconds = 0.10f;

        [Header("Baked Result - Do Not Edit")]
        [SerializeField, HideInInspector]
        private Transform _bakedRoot;

        [SerializeField, HideInInspector]
        private List<BakedFogRoom> _bakedFogRooms =
            new List<BakedFogRoom>();

        private readonly HashSet<BakedFogRoom> _visibleRooms =
            new HashSet<BakedFogRoom>();

        private Material _runtimeFogMaterial;
        private float _nextVisibilityRefresh;
        private PawnMovementManager _boundMovementManager;

        public IReadOnlyList<RoomPlacement> RoomPrefabs => _roomPrefabs;
        public IReadOnlyList<BakedFogRoom> BakedFogRooms => _bakedFogRooms;
        public RoomLayoutMode LayoutMode => _layoutMode;
        public float AutoLayoutGapMeters => _autoLayoutGapMeters;
        public int PackedColumns => Mathf.Max(1, _packedColumns);
        public int PackedRows => Mathf.Max(0, _packedRows);
        public bool BakeOnPlayStart =>
            _bakeOnPlayStart;
        public bool RebuildNavMeshAfterEditorBake =>
            _rebuildNavMeshAfterEditorBake;
        public InteractivePawnDefinition DoorDefinition =>
            _doorDefinition;
        public FieldPawnDefinition DefaultFloorDefinition =>
            _defaultFloorDefinition;
        public FieldPawnDefinition DefaultObstacleDefinition =>
            _defaultObstacleDefinition;
        public string FogSortingLayerName => _fogSortingLayerName;
        public int FogSortingOrder => _fogSortingOrder;
        public float FogAlpha => _fogAlpha;
        public Transform BakedRoot => _bakedRoot;

#if UNITY_EDITOR
        private void OnValidate()
        {
            _autoLayoutGapMeters =
                Mathf.Max(0f, _autoLayoutGapMeters);
            _packedColumns =
                Mathf.Max(1, _packedColumns);
            _packedRows =
                Mathf.Max(0, _packedRows);

            if (_roomPrefabs == null)
                _roomPrefabs = new List<RoomPlacement>();

            var usedIds =
                new HashSet<string>(
                    StringComparer.Ordinal);

            for (var index = 0;
                 index < _roomPrefabs.Count;
                 index++)
            {
                _roomPrefabs[index]?.EditorEnsureIdentity(
                    usedIds,
                    index);
            }
        }

        public void EditorEnsurePlacementIds()
        {
            OnValidate();
        }
#endif

        private void Awake()
        {
            ResolveReferences();
            BindMovementCameraEvents();
            BuildRuntimeFog();
            RefreshVisibility();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindMovementCameraEvents();
        }

        private void OnDisable()
        {
            UnbindMovementCameraEvents();
        }

        private void Update()
        {
            ResolveReferences();
            BindMovementCameraEvents();

            if (Time.unscaledTime < _nextVisibilityRefresh)
                return;

            _nextVisibilityRefresh =
                Time.unscaledTime +
                Mathf.Max(0.02f, _visibilityRefreshSeconds);

            RefreshVisibility();
        }

        private void LateUpdate()
        {
            ApplyActivePawnRoomCameraLimit();
        }

        private void OnDestroy()
        {
            UnbindMovementCameraEvents();
            ReleaseRuntimeFog();
        }

        private void BindMovementCameraEvents()
        {
            var movementManager =
                _pawnManager != null
                    ? _pawnManager.MovementManager
                    : null;

            if (_boundMovementManager == movementManager)
                return;

            UnbindMovementCameraEvents();

            _boundMovementManager = movementManager;
            if (_boundMovementManager != null)
            {
                _boundMovementManager.DoorTransferred +=
                    HandleDoorTransferredForCamera;
            }
        }

        private void UnbindMovementCameraEvents()
        {
            if (_boundMovementManager != null)
            {
                _boundMovementManager.DoorTransferred -=
                    HandleDoorTransferredForCamera;
            }

            _boundMovementManager = null;
        }

        private void HandleDoorTransferredForCamera(
            InteractivePawn pawn,
            Vector2 destination)
        {
            if (!_snapCameraToActivePawnAfterDoor ||
                pawn == null ||
                _pawnManager == null ||
                _pawnManager.SelectedInteractive != pawn)
            {
                return;
            }

            SnapBoardCameraTo(
                pawn.PresentationWorldPosition);

            ApplyActivePawnRoomCameraLimit();
        }

        private void SnapBoardCameraTo(
            Vector3 pawnWorldPosition)
        {
            var boardCamera =
                _pawnManager != null
                    ? _pawnManager.BoardCamera
                    : null;

            if (boardCamera == null)
                return;

            var cameraTransform =
                boardCamera.transform;

            cameraTransform.position =
                new Vector3(
                    pawnWorldPosition.x,
                    pawnWorldPosition.y,
                    cameraTransform.position.z);
        }

        private void ApplyActivePawnRoomCameraLimit()
        {
            if (!_confineCameraToActivePawnRoom ||
                _pawnManager == null)
            {
                return;
            }

            var activePawn =
                _pawnManager.SelectedInteractive;
            var boardCamera =
                _pawnManager.BoardCamera;

            if (activePawn == null ||
                boardCamera == null ||
                !boardCamera.orthographic)
            {
                return;
            }

            var room =
                FindRoomAt(
                    activePawn.PresentationWorldPosition);

            if (room == null ||
                !room.TryGetWorldBounds(
                    out var bounds))
            {
                return;
            }

            ClampOrthographicCameraToRoomBounds(
                boardCamera,
                bounds,
                Mathf.Max(
                    0f,
                    _cameraRoomEdgePaddingMeters));
        }

        private static void ClampOrthographicCameraToRoomBounds(
            Camera boardCamera,
            Bounds roomBounds,
            float paddingMeters)
        {
            if (boardCamera == null ||
                !boardCamera.orthographic)
            {
                return;
            }

            var halfHeight =
                Mathf.Max(
                    0.0001f,
                    boardCamera.orthographicSize);
            var halfWidth =
                halfHeight *
                Mathf.Max(
                    0.0001f,
                    boardCamera.aspect);

            var minX =
                roomBounds.min.x +
                halfWidth +
                paddingMeters;
            var maxX =
                roomBounds.max.x -
                halfWidth -
                paddingMeters;
            var minY =
                roomBounds.min.y +
                halfHeight +
                paddingMeters;
            var maxY =
                roomBounds.max.y -
                halfHeight -
                paddingMeters;

            var cameraTransform =
                boardCamera.transform;
            var position =
                cameraTransform.position;

            position.x = minX <= maxX
                ? Mathf.Clamp(position.x, minX, maxX)
                : roomBounds.center.x;

            position.y = minY <= maxY
                ? Mathf.Clamp(position.y, minY, maxY)
                : roomBounds.center.y;

            cameraTransform.position = position;
        }

        public BakedFogRoom FindRoomAt(Vector2 worldPosition)
        {
            BakedFogRoom best = null;
            var bestArea = float.PositiveInfinity;

            for (var index = 0;
                 index < _bakedFogRooms.Count;
                 index++)
            {
                var room = _bakedFogRooms[index];
                if (room == null ||
                    !room.Contains(worldPosition))
                {
                    continue;
                }

                var area = room.GetWorldArea();
                if (area <= 0f)
                    continue;

                if (best != null && area >= bestArea)
                    continue;

                best = room;
                bestArea = area;
            }

            return best;
        }

        [ContextMenu("Refresh Fog Visibility")]
        public void RefreshVisibility()
        {
            if (!Application.isPlaying)
                return;

            ResolveReferences();

            if (IsLocalGameMaster())
            {
                SetAllFog(false);
                return;
            }

            _visibleRooms.Clear();

            if (_sessionAuthority != null &&
                _sessionAuthority.IsOnline)
            {
                if (_sessionAuthority.TryGetLocalControlledPawn(
                        out var localPawn) &&
                    localPawn != null)
                {
                    AddPawnRoom(localPawn);
                }
            }
            else if (_pawnManager != null)
            {
                var players = _pawnManager.PlayerPawns;
                if (players != null)
                {
                    for (var index = 0;
                         index < players.Count;
                         index++)
                    {
                        AddPawnRoom(players[index]);
                    }
                }
            }

            for (var index = 0;
                 index < _bakedFogRooms.Count;
                 index++)
            {
                var room = _bakedFogRooms[index];
                if (room == null)
                    continue;

                room.SetFogVisible(
                    !_visibleRooms.Contains(room));
            }
        }

        private void AddPawnRoom(InteractivePawn pawn)
        {
            if (pawn == null)
                return;

            var room = FindRoomAt(pawn.WorldPosition);
            if (room != null)
                _visibleRooms.Add(room);
        }

        private bool IsLocalGameMaster()
        {
            if (_sessionAuthority != null &&
                _sessionAuthority.IsOnline)
            {
                return _sessionAuthority.IsLocalGameMaster;
            }

            return _offlineAsGameMaster;
        }

        private void ResolveReferences()
        {
            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();

            if (_sessionAuthority == null)
            {
                _sessionAuthority = TRPGSessionAuthority.Instance;
                if (_sessionAuthority == null)
                    _sessionAuthority =
                        FindFirst<TRPGSessionAuthority>();
            }
        }

        private void BuildRuntimeFog()
        {
            ReleaseRuntimeFog();

            var material = ResolveFogMaterial();

            for (var index = 0;
                 index < _bakedFogRooms.Count;
                 index++)
            {
                var room = _bakedFogRooms[index];
                if (room == null)
                    continue;

                if (room.FogRenderer != null)
                {
                    room.FogRenderer.sortingLayerName =
                        _fogSortingLayerName;
                    room.FogRenderer.sortingOrder =
                        _fogSortingOrder;
                }

                room.BuildRuntimeMesh(material);
            }
        }

        private Material ResolveFogMaterial()
        {
            if (_fogMaterial != null)
                return _fogMaterial;

            if (_runtimeFogMaterial != null)
                return _runtimeFogMaterial;

            var shader = Shader.Find(
                "Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogError(
                    "[RoomManager] Fog용 Unlit Sprite Shader를 찾지 못했습니다.",
                    this);
                return null;
            }

            _runtimeFogMaterial = new Material(shader)
            {
                name = "RoomFog_RuntimeMaterial"
            };

            var color = new Color(0f, 0f, 0f, _fogAlpha);

            if (_runtimeFogMaterial.HasProperty("_Color"))
                _runtimeFogMaterial.SetColor("_Color", color);

            if (_runtimeFogMaterial.HasProperty("_BaseColor"))
                _runtimeFogMaterial.SetColor("_BaseColor", color);

            if (_runtimeFogMaterial.HasProperty("_MainTex"))
            {
                _runtimeFogMaterial.SetTexture(
                    "_MainTex",
                    Texture2D.whiteTexture);
            }

            return _runtimeFogMaterial;
        }

        private void ReleaseRuntimeFog()
        {
            if (_bakedFogRooms != null)
            {
                for (var index = 0;
                     index < _bakedFogRooms.Count;
                     index++)
                {
                    _bakedFogRooms[index]?.ReleaseRuntimeMesh();
                }
            }

            if (_runtimeFogMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeFogMaterial);
                else
                    DestroyImmediate(_runtimeFogMaterial);

                _runtimeFogMaterial = null;
            }
        }

        private void SetAllFog(bool visible)
        {
            for (var index = 0;
                 index < _bakedFogRooms.Count;
                 index++)
            {
                _bakedFogRooms[index]?.SetFogVisible(visible);
            }
        }

#if UNITY_EDITOR
        public void EditorSetBakedResult(
            Transform bakedRoot,
            List<BakedFogRoom> bakedFogRooms)
        {
            _bakedRoot = bakedRoot;
            _bakedFogRooms =
                bakedFogRooms ?? new List<BakedFogRoom>();
        }

        public void EditorClearBakedResult()
        {
            _bakedRoot = null;
            _bakedFogRooms.Clear();
        }
#endif

        public static string BuildRoomDoorInstanceId(
            string placementId,
            string doorId)
        {
            return
                "roomdoor__" +
                SafeId(placementId, "placement") +
                "__" +
                SafeId(doorId, "door");
        }

        public static string BuildArriveDoorInstanceId(
            string placementId,
            string doorId)
        {
            return
                "arrivedoor__" +
                SafeId(placementId, "placement") +
                "__" +
                SafeId(doorId, "door");
        }

        private static string SafeId(
            string value,
            string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }

        internal static bool TryTriangulate(
            IReadOnlyList<Vector2> polygon,
            out Vector3[] vertices,
            out int[] triangles)
        {
            vertices = Array.Empty<Vector3>();
            triangles = Array.Empty<int>();

            if (polygon == null || polygon.Count < 3)
                return false;

            var count = polygon.Count;
            vertices = new Vector3[count];

            for (var index = 0; index < count; index++)
            {
                vertices[index] = new Vector3(
                    polygon[index].x,
                    polygon[index].y,
                    0f);
            }

            var indices = new List<int>(count);
            if (SignedArea(polygon) > 0f)
            {
                for (var index = 0; index < count; index++)
                    indices.Add(index);
            }
            else
            {
                for (var index = count - 1; index >= 0; index--)
                    indices.Add(index);
            }

            var result = new List<int>((count - 2) * 3);
            var guard = 0;
            var maximumGuard = count * count;

            while (indices.Count > 3 &&
                   guard < maximumGuard)
            {
                var earFound = false;

                for (var index = 0;
                     index < indices.Count;
                     index++)
                {
                    var previous =
                        indices[
                            (index - 1 + indices.Count) %
                            indices.Count];
                    var current = indices[index];
                    var next =
                        indices[(index + 1) % indices.Count];

                    var a = polygon[previous];
                    var b = polygon[current];
                    var c = polygon[next];

                    if (Cross(b - a, c - b) <= 0.00001f)
                        continue;

                    var containsPoint = false;

                    for (var test = 0;
                         test < indices.Count;
                         test++)
                    {
                        var testIndex = indices[test];
                        if (testIndex == previous ||
                            testIndex == current ||
                            testIndex == next)
                        {
                            continue;
                        }

                        if (PointInTriangle(
                                polygon[testIndex],
                                a,
                                b,
                                c))
                        {
                            containsPoint = true;
                            break;
                        }
                    }

                    if (containsPoint)
                        continue;

                    result.Add(previous);
                    result.Add(current);
                    result.Add(next);
                    indices.RemoveAt(index);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    return false;

                guard++;
            }

            if (indices.Count == 3)
            {
                result.Add(indices[0]);
                result.Add(indices[1]);
                result.Add(indices[2]);
            }

            triangles = result.ToArray();
            return triangles.Length >= 3;
        }

        private static float SignedArea(
            IReadOnlyList<Vector2> polygon)
        {
            var sum = 0f;

            for (var index = 0;
                 index < polygon.Count;
                 index++)
            {
                var next = (index + 1) % polygon.Count;
                sum +=
                    polygon[index].x * polygon[next].y -
                    polygon[next].x * polygon[index].y;
            }

            return sum * 0.5f;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool PointInTriangle(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c)
        {
            var ab = Cross(b - a, point - a);
            var bc = Cross(c - b, point - b);
            var ca = Cross(a - c, point - c);

            return
                ab >= -0.00001f &&
                bc >= -0.00001f &&
                ca >= -0.00001f;
        }

        private static T FindFirst<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2022_2_OR_NEWER
            return FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return FindObjectOfType<T>(true);
#endif
        }
    }
}
