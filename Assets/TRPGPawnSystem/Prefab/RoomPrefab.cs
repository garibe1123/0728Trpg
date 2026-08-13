using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    [Serializable]
    public sealed class FogRoomDefinition
    {
        [SerializeField]
        private string _roomId = "Room_01";

        [SerializeField]
        private List<Vector2> _points = new List<Vector2>();

        public string RoomId => _roomId;
        public IReadOnlyList<Vector2> Points => _points;
        public bool IsValid => _points != null && _points.Count >= 3;

#if UNITY_EDITOR
        public void EditorSetId(string roomId)
        {
            _roomId = string.IsNullOrWhiteSpace(roomId)
                ? "Room"
                : roomId.Trim();
        }

        public void EditorSetPoints(IReadOnlyList<Vector2> points)
        {
            if (_points == null)
                _points = new List<Vector2>();
            else
                _points.Clear();

            if (points == null)
                return;

            for (var index = 0; index < points.Count; index++)
                _points.Add(points[index]);
        }

        public void EditorSetPoint(int index, Vector2 point)
        {
            if (_points == null ||
                index < 0 ||
                index >= _points.Count)
            {
                return;
            }

            _points[index] = point;
        }

        public void EditorInsertPoint(int index, Vector2 point)
        {
            if (_points == null)
                _points = new List<Vector2>();

            index = Mathf.Clamp(index, 0, _points.Count);
            _points.Insert(index, point);
        }

        public void EditorRemovePoint(int index)
        {
            if (_points == null ||
                _points.Count <= 3 ||
                index < 0 ||
                index >= _points.Count)
            {
                return;
            }

            _points.RemoveAt(index);
        }
#endif
    }

    /// <summary>
    /// 사람이 편집하는 원본 맵 Prefab 데이터입니다.
    ///
    /// 이 컴포넌트는 Scene의 런타임 맵이 아닙니다.
    /// Map/Wall Sprite, 실제 미터 크기, Fog Room Polygon만 보존합니다.
    ///
    /// RoomManager의 Bake는 이 원본을 읽어서 __BakedRooms를 새로 만들며,
    /// 이 RoomPrefab 자체와 FogRoomDefinition은 수정/삭제하지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomPrefab : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField]
        private string _prefabId = "RoomMap_01";

        [Header("Map Source")]
        [SerializeField, Tooltip(
            "이 맵의 바닥/베이스 Sprite. Bake 시 이 이미지의 알파가 0보다 큰 픽셀을 Walkable 후보로 사용합니다.")]
        private Sprite _mapSprite;

        [SerializeField, Tooltip(
            "Map Sprite와 같은 크기/PPU/Pivot을 권장하는 벽 Sprite. Bake 시 이 이미지의 알파가 0보다 큰 픽셀을 Not Walkable 벽으로 사용합니다.")]
        private Sprite _wallSprite;

        [SerializeField, Min(0.01f), Tooltip(
            "Map Sprite 전체 가로 길이를 실제 몇 m로 사용할지 지정합니다. " +
            "Bake 결과는 1 Unity Unit = 1m가 되도록 자동 Scale됩니다.")]
        private float _mapWidthMeters = 20f;

        [Header("Alpha Navigation Mask")]
        [SerializeField, Range(0f, 1f), Tooltip(
            "Map Sprite에서 이 값보다 큰 Alpha 픽셀만 Walkable로 사용합니다. " +
            "0이면 Alpha가 1이라도 있는 모든 픽셀을 포함합니다.")]
        private float _mapWalkableAlphaThreshold;

        [SerializeField, Range(0f, 1f), Tooltip(
            "Wall Sprite에서 이 값보다 큰 Alpha 픽셀만 Not Walkable 벽으로 사용합니다. " +
            "0이면 Alpha가 1이라도 있는 모든 픽셀을 포함합니다.")]
        private float _wallObstacleAlphaThreshold;

        [SerializeField, Min(0f), Tooltip(
            "알파 외곽선 단순화 허용치(px). 0이면 픽셀 경계를 그대로 유지하고, " +
            "값을 올리면 Collider 꼭짓점 수를 줄입니다.")]
        private float _alphaColliderSimplifyPixels;

        [Header("Field Pawn Definition Override - Optional")]
        [SerializeField, Tooltip(
            "비워두면 RoomManager의 Default Floor Definition을 사용합니다.")]
        private FieldPawnDefinition _floorDefinitionOverride;

        [SerializeField, Tooltip(
            "비워두면 RoomManager의 Default Obstacle Definition을 사용합니다.")]
        private FieldPawnDefinition _obstacleDefinitionOverride;

        [Header("World Rendering")]
        [SerializeField]
        private string _mapSortingLayerName = "Default";

        [SerializeField]
        private int _mapSortingOrder;

        [SerializeField]
        private string _wallSortingLayerName = "Default";

        [SerializeField]
        private int _wallSortingOrder = 10;

        [Header("Fog Rooms")]
        [SerializeField]
        private List<FogRoomDefinition> _rooms =
            new List<FogRoomDefinition>();

        public string PrefabId => _prefabId;
        public Sprite MapSprite => _mapSprite;
        public Sprite WallSprite => _wallSprite;
        public float MapWidthMeters => Mathf.Max(0.01f, _mapWidthMeters);
        public float MapWalkableAlphaThreshold =>
            Mathf.Clamp01(_mapWalkableAlphaThreshold);
        public float WallObstacleAlphaThreshold =>
            Mathf.Clamp01(_wallObstacleAlphaThreshold);
        public float AlphaColliderSimplifyPixels =>
            Mathf.Max(0f, _alphaColliderSimplifyPixels);
        public FieldPawnDefinition FloorDefinitionOverride =>
            _floorDefinitionOverride;
        public FieldPawnDefinition ObstacleDefinitionOverride =>
            _obstacleDefinitionOverride;
        public string MapSortingLayerName => _mapSortingLayerName;
        public int MapSortingOrder => _mapSortingOrder;
        public string WallSortingLayerName => _wallSortingLayerName;
        public int WallSortingOrder => _wallSortingOrder;
        public IReadOnlyList<FogRoomDefinition> Rooms => _rooms;

        public float MapScale
        {
            get
            {
                if (_mapSprite == null ||
                    _mapSprite.bounds.size.x <= 0.00001f)
                {
                    return 1f;
                }

                return MapWidthMeters / _mapSprite.bounds.size.x;
            }
        }

        public Vector2 MapSizeMeters
        {
            get
            {
                if (_mapSprite == null)
                    return Vector2.zero;

                var scale = MapScale;
                return new Vector2(
                    _mapSprite.bounds.size.x * scale,
                    _mapSprite.bounds.size.y * scale);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_prefabId))
                _prefabId = "RoomMap";

            _mapWidthMeters = Mathf.Max(0.01f, _mapWidthMeters);
            _mapWalkableAlphaThreshold =
                Mathf.Clamp01(_mapWalkableAlphaThreshold);
            _wallObstacleAlphaThreshold =
                Mathf.Clamp01(_wallObstacleAlphaThreshold);
            _alphaColliderSimplifyPixels =
                Mathf.Max(0f, _alphaColliderSimplifyPixels);

            if (_rooms == null)
                _rooms = new List<FogRoomDefinition>();

            if (_mapSprite != null &&
                _wallSprite != null &&
                !AreSpritesAligned(_mapSprite, _wallSprite))
            {
                Debug.LogWarning(
                    $"[{name}] Map/Wall Sprite의 rect, PPU 또는 Pivot이 다릅니다. " +
                    "자동 Bake 시 완전히 겹치지 않을 수 있습니다.",
                    this);
            }
        }

        public int EditorAddRoom(
            string roomId,
            IReadOnlyList<Vector2> points)
        {
            if (_rooms == null)
                _rooms = new List<FogRoomDefinition>();

            var definition = new FogRoomDefinition();
            definition.EditorSetId(roomId);
            definition.EditorSetPoints(points);
            _rooms.Add(definition);
            return _rooms.Count - 1;
        }

        public void EditorRemoveRoom(int index)
        {
            if (_rooms == null ||
                index < 0 ||
                index >= _rooms.Count)
            {
                return;
            }

            _rooms.RemoveAt(index);
        }

        public void EditorSetRoomPoint(
            int roomIndex,
            int pointIndex,
            Vector2 point)
        {
            if (!TryGetEditorRoom(roomIndex, out var room))
                return;

            room.EditorSetPoint(pointIndex, point);
        }

        public void EditorInsertRoomPoint(
            int roomIndex,
            int pointIndex,
            Vector2 point)
        {
            if (!TryGetEditorRoom(roomIndex, out var room))
                return;

            room.EditorInsertPoint(pointIndex, point);
        }

        public void EditorRemoveRoomPoint(
            int roomIndex,
            int pointIndex)
        {
            if (!TryGetEditorRoom(roomIndex, out var room))
                return;

            room.EditorRemovePoint(pointIndex);
        }

        public string EditorGenerateNextRoomId()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (_rooms != null)
            {
                for (var index = 0; index < _rooms.Count; index++)
                {
                    var room = _rooms[index];
                    if (room != null &&
                        !string.IsNullOrWhiteSpace(room.RoomId))
                    {
                        used.Add(room.RoomId);
                    }
                }
            }

            for (var number = 1; number < 10000; number++)
            {
                var candidate = $"Room_{number:00}";
                if (!used.Contains(candidate))
                    return candidate;
            }

            return "Room_" + Guid.NewGuid().ToString("N");
        }

        private bool TryGetEditorRoom(
            int index,
            out FogRoomDefinition room)
        {
            room = null;
            if (_rooms == null ||
                index < 0 ||
                index >= _rooms.Count)
            {
                return false;
            }

            room = _rooms[index];
            return room != null;
        }

        private static bool AreSpritesAligned(Sprite map, Sprite wall)
        {
            if (map == null || wall == null)
                return true;

            return
                Mathf.Abs(map.rect.width - wall.rect.width) < 0.01f &&
                Mathf.Abs(map.rect.height - wall.rect.height) < 0.01f &&
                Mathf.Abs(map.pixelsPerUnit - wall.pixelsPerUnit) < 0.01f &&
                Vector2.Distance(map.pivot, wall.pivot) < 0.01f;
        }
#endif
    }
}
