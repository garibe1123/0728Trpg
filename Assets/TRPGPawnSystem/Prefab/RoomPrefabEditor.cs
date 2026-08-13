#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Trpg.Pawns.Editor
{
    [CustomEditor(typeof(RoomPrefab))]
    public sealed class RoomPrefabEditor : UnityEditor.Editor
    {
        private const string PreviewRootName = "__RoomPrefabPreview";
        private const string PreviewMapName = "MapPreview";
        private const string PreviewWallName = "WallPreview";
        private const float DeletePointPixelRadius = 12f;
        private const float InsertEdgePixelDistance = 18f;

        private int _selectedRoomIndex = -1;
        private bool _editSelectedRoom = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_prefabId"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "Map Source",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_mapSprite"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_wallSprite"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_mapWidthMeters"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "Alpha Navigation Mask",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_mapWalkableAlphaThreshold"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_wallObstacleAlphaThreshold"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_alphaColliderSimplifyPixels"));

            EditorGUILayout.HelpBox(
                "Map: Alpha > Threshold인 실제 픽셀 영역만 Walkable.\n" +
                "Wall: Alpha > Threshold인 실제 픽셀 영역만 Not Walkable.\n" +
                "Sprite Editor Physics Shape는 사용하지 않습니다.",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "Field Definition Override",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_floorDefinitionOverride"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_obstacleDefinitionOverride"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "World Rendering",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_mapSortingLayerName"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_mapSortingOrder"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_wallSortingLayerName"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "_wallSortingOrder"));

            serializedObject.ApplyModifiedProperties();

            var source = (RoomPrefab)target;

            EditorGUILayout.Space(10f);

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorGUILayout.HelpBox(
                    "현재는 PREFAB MODE입니다. 이 화면에서는 Unity AI Navigation의 " +
                    "NavMesh 시각화가 지원되지 않습니다. 여기서는 Map/Wall/Room 원본만 편집합니다. " +
                    "실제 Map_Floor / Wall_Obstacle / NavMesh는 GameScene의 RoomManager가 생성합니다.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "RoomPrefab은 원본 데이터입니다. " +
                "Map/Wall과 Fog Room Polygon은 여기 저장되고, " +
                "RoomManager의 Re-Bake가 이 데이터를 삭제하지 않습니다.",
                MessageType.Info);

            EditorGUILayout.LabelField(
                "Map Size",
                $"{source.MapSizeMeters.x:0.###}m × " +
                $"{source.MapSizeMeters.y:0.###}m");

            if (GUILayout.Button(
                    "Sync / Create Map Preview",
                    GUILayout.Height(28f)))
            {
                SyncPreview(source);
            }

            DrawRoomList(source);

            EditorGUILayout.Space(6f);
            if (GUILayout.Button(
                    "+ Draw New Fog Room",
                    GUILayout.Height(30f)))
            {
                RoomPrefabScenePainter.Begin(source);
            }

            _editSelectedRoom = GUILayout.Toggle(
                _editSelectedRoom,
                _editSelectedRoom
                    ? "Selected Room Point Edit: ON"
                    : "Selected Room Point Edit: OFF",
                "Button");

            if (source.Rooms.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Fog Room이 아직 없습니다. Map Preview를 만든 뒤 " +
                    "Draw New Fog Room으로 Scene View에서 영역을 그리십시오.",
                    MessageType.Warning);
            }
        }

        private void OnSceneGUI()
        {
            var source = (RoomPrefab)target;
            if (source == null)
                return;

            DrawAllRooms(source);

            if (!_editSelectedRoom ||
                _selectedRoomIndex < 0 ||
                _selectedRoomIndex >= source.Rooms.Count)
            {
                return;
            }

            var room = source.Rooms[_selectedRoomIndex];
            if (room == null || room.Points == null)
                return;

            HandleSelectedRoomPoints(
                source,
                _selectedRoomIndex,
                room);
        }

        private void DrawRoomList(RoomPrefab source)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                $"Fog Rooms ({source.Rooms.Count})",
                EditorStyles.boldLabel);

            for (var index = 0;
                 index < source.Rooms.Count;
                 index++)
            {
                var room = source.Rooms[index];
                if (room == null)
                    continue;

                EditorGUILayout.BeginHorizontal();

                var selected = _selectedRoomIndex == index;
                if (GUILayout.Toggle(
                        selected,
                        $"{room.RoomId} · {room.Points.Count} points",
                        "Button"))
                {
                    _selectedRoomIndex = index;
                }

                if (GUILayout.Button("X", GUILayout.Width(28f)))
                {
                    Undo.RecordObject(
                        source,
                        "Remove Fog Room");

                    source.EditorRemoveRoom(index);
                    EditorUtility.SetDirty(source);

                    if (_selectedRoomIndex >= source.Rooms.Count)
                        _selectedRoomIndex = source.Rooms.Count - 1;

                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawAllRooms(RoomPrefab source)
        {
            for (var roomIndex = 0;
                 roomIndex < source.Rooms.Count;
                 roomIndex++)
            {
                var room = source.Rooms[roomIndex];
                if (room == null ||
                    room.Points == null ||
                    room.Points.Count < 2)
                {
                    continue;
                }

                Handles.color =
                    new Color(0.1f, 0.9f, 1f, 0.95f);

                for (var pointIndex = 0;
                     pointIndex < room.Points.Count;
                     pointIndex++)
                {
                    var next =
                        (pointIndex + 1) % room.Points.Count;

                    var a = source.transform.TransformPoint(
                        room.Points[pointIndex]);
                    var b = source.transform.TransformPoint(
                        room.Points[next]);

                    Handles.DrawAAPolyLine(3f, a, b);
                }

                var center = GetRoomCenter(source, room);
                Handles.Label(
                    center,
                    room.RoomId,
                    EditorStyles.boldLabel);
            }
        }

        private static Vector3 GetRoomCenter(
            RoomPrefab source,
            FogRoomDefinition room)
        {
            if (room.Points.Count == 0)
                return source.transform.position;

            var sum = Vector3.zero;
            for (var index = 0;
                 index < room.Points.Count;
                 index++)
            {
                sum += source.transform.TransformPoint(
                    room.Points[index]);
            }

            return sum / room.Points.Count;
        }

        private static void HandleSelectedRoomPoints(
            RoomPrefab source,
            int roomIndex,
            FogRoomDefinition room)
        {
            for (var pointIndex = 0;
                 pointIndex < room.Points.Count;
                 pointIndex++)
            {
                var world = source.transform.TransformPoint(
                    room.Points[pointIndex]);

                var size =
                    HandleUtility.GetHandleSize(world) *
                    0.07f;

                EditorGUI.BeginChangeCheck();

                var moved = Handles.FreeMoveHandle(
                    world,
                    size,
                    Vector3.zero,
                    Handles.CircleHandleCap);

                if (!EditorGUI.EndChangeCheck())
                    continue;

                Undo.RecordObject(
                    source,
                    "Move Fog Room Point");

                var local =
                    source.transform.InverseTransformPoint(moved);

                source.EditorSetRoomPoint(
                    roomIndex,
                    pointIndex,
                    new Vector2(local.x, local.y));

                EditorUtility.SetDirty(source);
            }

            HandleInsertDelete(
                source,
                roomIndex,
                room);
        }

        private static void HandleInsertDelete(
            RoomPrefab source,
            int roomIndex,
            FogRoomDefinition room)
        {
            var current = Event.current;

            if (current == null ||
                current.type != EventType.MouseDown ||
                current.button != 0 ||
                current.alt)
            {
                return;
            }

            if (current.control || current.command)
            {
                if (room.Points.Count <= 3)
                    return;

                var pointIndex =
                    FindNearestPoint(
                        source,
                        room,
                        current.mousePosition,
                        out var distance);

                if (pointIndex < 0 ||
                    distance > DeletePointPixelRadius)
                {
                    return;
                }

                Undo.RecordObject(
                    source,
                    "Delete Fog Room Point");

                source.EditorRemoveRoomPoint(
                    roomIndex,
                    pointIndex);

                EditorUtility.SetDirty(source);
                current.Use();
                return;
            }

            if (!current.shift)
                return;

            var edge =
                FindNearestEdge(
                    source,
                    room,
                    current.mousePosition,
                    out var edgeDistance);

            if (edge < 0 ||
                edgeDistance > InsertEdgePixelDistance ||
                !TryGetMouseWorld(
                    source.transform.position.z,
                    out var world))
            {
                return;
            }

            var local =
                source.transform.InverseTransformPoint(world);

            Undo.RecordObject(
                source,
                "Insert Fog Room Point");

            source.EditorInsertRoomPoint(
                roomIndex,
                edge + 1,
                new Vector2(local.x, local.y));

            EditorUtility.SetDirty(source);
            current.Use();
        }

        private static int FindNearestPoint(
            RoomPrefab source,
            FogRoomDefinition room,
            Vector2 mouseGui,
            out float distance)
        {
            var bestIndex = -1;
            distance = float.PositiveInfinity;

            for (var index = 0;
                 index < room.Points.Count;
                 index++)
            {
                var world = source.transform.TransformPoint(
                    room.Points[index]);

                var gui = HandleUtility.WorldToGUIPoint(world);
                var candidate =
                    Vector2.Distance(gui, mouseGui);

                if (candidate >= distance)
                    continue;

                distance = candidate;
                bestIndex = index;
            }

            return bestIndex;
        }

        private static int FindNearestEdge(
            RoomPrefab source,
            FogRoomDefinition room,
            Vector2 mouseGui,
            out float distance)
        {
            var bestIndex = -1;
            distance = float.PositiveInfinity;

            for (var index = 0;
                 index < room.Points.Count;
                 index++)
            {
                var next =
                    (index + 1) % room.Points.Count;

                var a = HandleUtility.WorldToGUIPoint(
                    source.transform.TransformPoint(
                        room.Points[index]));

                var b = HandleUtility.WorldToGUIPoint(
                    source.transform.TransformPoint(
                        room.Points[next]));

                var candidate =
                    DistanceToSegment(
                        mouseGui,
                        a,
                        b);

                if (candidate >= distance)
                    continue;

                distance = candidate;
                bestIndex = index;
            }

            return bestIndex;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 a,
            Vector2 b)
        {
            var ab = b - a;
            if (ab.sqrMagnitude <= 0.0001f)
                return Vector2.Distance(point, a);

            var t = Mathf.Clamp01(
                Vector2.Dot(point - a, ab) /
                ab.sqrMagnitude);

            return Vector2.Distance(
                point,
                a + ab * t);
        }

        private static void SyncPreview(RoomPrefab source)
        {
            var root = source.transform.Find(PreviewRootName);

            if (root == null)
            {
                var rootObject =
                    new GameObject(PreviewRootName);

                Undo.RegisterCreatedObjectUndo(
                    rootObject,
                    "Create Room Prefab Preview");

                root = rootObject.transform;
                root.SetParent(source.transform, false);
            }

            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            var mapRenderer = EnsurePreviewRenderer(
                root,
                PreviewMapName);

            var wallRenderer = EnsurePreviewRenderer(
                root,
                PreviewWallName);

            var scale = source.MapScale;

            mapRenderer.sprite = source.MapSprite;
            mapRenderer.sortingLayerName =
                source.MapSortingLayerName;
            mapRenderer.sortingOrder =
                source.MapSortingOrder;
            mapRenderer.transform.localPosition =
                Vector3.zero;
            mapRenderer.transform.localRotation =
                Quaternion.identity;
            mapRenderer.transform.localScale =
                new Vector3(scale, scale, 1f);

            wallRenderer.sprite = source.WallSprite;
            wallRenderer.sortingLayerName =
                source.WallSortingLayerName;
            wallRenderer.sortingOrder =
                source.WallSortingOrder;
            wallRenderer.transform.localPosition =
                Vector3.zero;
            wallRenderer.transform.localRotation =
                Quaternion.identity;
            wallRenderer.transform.localScale =
                new Vector3(scale, scale, 1f);

            EditorUtility.SetDirty(source);
            SceneView.RepaintAll();
        }

        private static SpriteRenderer EnsurePreviewRenderer(
            Transform parent,
            string childName)
        {
            var child = parent.Find(childName);

            if (child == null)
            {
                var childObject =
                    new GameObject(childName);

                Undo.RegisterCreatedObjectUndo(
                    childObject,
                    "Create Room Preview Renderer");

                child = childObject.transform;
                child.SetParent(parent, false);
            }

            var renderer =
                child.GetComponent<SpriteRenderer>();

            if (renderer == null)
            {
                renderer =
                    Undo.AddComponent<SpriteRenderer>(
                        child.gameObject);
            }

            return renderer;
        }

        private static bool TryGetMouseWorld(
            float planeZ,
            out Vector3 world)
        {
            var ray = HandleUtility.GUIPointToWorldRay(
                Event.current.mousePosition);

            if (Mathf.Abs(ray.direction.z) <= 0.00001f)
            {
                world = default;
                return false;
            }

            var distance =
                (planeZ - ray.origin.z) /
                ray.direction.z;

            if (distance < 0f)
            {
                world = default;
                return false;
            }

            world =
                ray.origin +
                ray.direction * distance;
            world.z = planeZ;
            return true;
        }
    }

    [CustomEditor(typeof(RoomManager))]
    public sealed class RoomManagerEditor : UnityEditor.Editor
    {
        private static bool _isBaking;

        private bool _showDoorSceneEditor = true;
        private int _selectedPlacementIndex = -1;
        private int _selectedDoorIndex = -1;

        public override void OnInspectorGUI()
        {
            var manager = (RoomManager)target;

            DrawDefaultInspector();

            EditorGUILayout.HelpBox(
                "Door는 별도 Doors 리스트나 RoomPrefab Asset에 저장하지 않습니다.\n" +
                "각 Room Prefabs Element 내부의 Door Links에 직접 저장합니다.\n" +
                "RoomDoorPosition = 해당 Room 기준 로컬 좌표 / " +
                "ArriveDoorPosition = Scene 글로벌 좌표.\n" +
                "Door Links를 직접 추가한 경우에만 Bake 시 Door Pair가 생성됩니다.",
                MessageType.Info);

            _showDoorSceneEditor =
                GUILayout.Toggle(
                    _showDoorSceneEditor,
                    _showDoorSceneEditor
                        ? "Door Scene Editor: ON"
                        : "Door Scene Editor: OFF",
                    "Button");

            if (_showDoorSceneEditor)
            {
                EditorGUILayout.HelpBox(
                    "Scene View에서 같은 색의 ROOM / ARRIVE 사각형이 한 Door Pair입니다. " +
                    "중앙 핸들=위치 이동, 가로/세로 핸들=Collider 크기 조절. " +
                    "연결선과 라벨도 같은 색으로 표시됩니다.",
                    MessageType.Info);
            }

            if (manager.LayoutMode == RoomLayoutMode.Packed)
            {
                var rowLabel =
                    manager.PackedRows <= 0
                        ? "N (Auto)"
                        : manager.PackedRows.ToString();

                EditorGUILayout.HelpBox(
                    $"Packed Layout: {manager.PackedColumns} x {rowLabel}\n" +
                    "각 Cell은 등록된 RoomPrefab 중 가장 큰 Map Width/Height + Gap으로 계산됩니다. " +
                    "따라서 서로 크기가 다른 맵도 겹치지 않습니다.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                "Room Map Bake",
                EditorStyles.boldLabel);

            if (manager.BakedRoot != null)
            {
                EditorGUILayout.HelpBox(
                    $"BAKED · Fog Rooms {manager.BakedFogRooms.Count}\n" +
                    $"Root: {manager.BakedRoot.name}",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "아직 Scene에 Baked Map이 없습니다.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Room Prefabs 목록이나 설정을 수정해도 자동 Bake하지 않습니다.\n" +
                "Bake는 BAKE / REBUILD NOW 버튼을 누르거나, " +
                "Bake On Play Start가 켜진 상태에서 Play를 시작할 때만 1회 실행됩니다.",
                MessageType.Info);

            if (GUILayout.Button(
                    "BAKE / REBUILD NOW",
                    GUILayout.Height(36f)))
            {
                BakeAll(manager);
            }

            if (GUILayout.Button(
                    "CLEAR BAKED MAP",
                    GUILayout.Height(26f)))
            {
                ClearBaked(manager);
            }

            SceneView.RepaintAll();
        }

        private void OnSceneGUI()
        {
            if (!_showDoorSceneEditor)
                return;

            var manager =
                (RoomManager)target;

            if (manager == null ||
                manager.RoomPrefabs == null)
            {
                return;
            }

            // 신규 DoorLink에 DoorId/표시색이 아직 없으면 이 시점에 한 번 보정합니다.
            manager.EditorEnsurePlacementIds();

            CalculateEditorPlacementTransforms(
                manager,
                out var placementMatrices);

            for (var placementIndex = 0;
                 placementIndex < manager.RoomPrefabs.Count;
                 placementIndex++)
            {
                var placement =
                    manager.RoomPrefabs[placementIndex];

                if (placement == null ||
                    !placement.Enabled ||
                    placement.DoorLinks == null ||
                    placementIndex >= placementMatrices.Count)
                {
                    continue;
                }

                var roomMatrix =
                    placementMatrices[placementIndex];

                for (var doorIndex = 0;
                     doorIndex < placement.DoorLinks.Count;
                     doorIndex++)
                {
                    var door =
                        placement.DoorLinks[doorIndex];

                    if (door == null ||
                        !door.Enabled)
                    {
                        continue;
                    }

                    DrawDoorLinkSceneEditor(
                        manager,
                        placement,
                        placementIndex,
                        door,
                        doorIndex,
                        roomMatrix);
                }
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(manager);
                SceneView.RepaintAll();
            }
        }

        private void DrawDoorLinkSceneEditor(
            RoomManager manager,
            RoomPlacement placement,
            int placementIndex,
            RoomDoorLinkDefinition door,
            int doorIndex,
            Matrix4x4 roomMatrix)
        {
            var baseColor =
                door.EditorColor;

            var selected =
                _selectedPlacementIndex == placementIndex &&
                _selectedDoorIndex == doorIndex;

            var roomWorld =
                roomMatrix.MultiplyPoint3x4(
                    new Vector3(
                        door.RoomDoorPosition.x,
                        door.RoomDoorPosition.y,
                        0f));

            var roomRotation =
                manager.transform.rotation *
                Quaternion.Euler(
                    0f,
                    0f,
                    placement.RotationDegrees +
                    door.RoomDoorRotation);

            var arriveWorld =
                new Vector3(
                    door.ArriveDoorPosition.x,
                    door.ArriveDoorPosition.y,
                    manager.transform.position.z);

            var arriveRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    door.ArriveDoorRotation);

            DrawDoorPairLine(
                roomWorld,
                arriveWorld,
                baseColor,
                placement.DisplayName,
                door.DisplayName);

            var roomClicked =
                DrawDoorColliderEditor(
                    manager,
                    roomWorld,
                    roomRotation,
                    door.RoomDoorColliderSize,
                    baseColor,
                    "ROOM",
                    selected,
                    out var movedRoomWorld,
                    out var roomSize);

            var arriveClicked =
                DrawDoorColliderEditor(
                    manager,
                    arriveWorld,
                    arriveRotation,
                    door.ArriveDoorColliderSize,
                    baseColor,
                    "ARRIVE",
                    selected,
                    out var movedArriveWorld,
                    out var arriveSize);

            if (roomClicked ||
                arriveClicked)
            {
                _selectedPlacementIndex =
                    placementIndex;
                _selectedDoorIndex =
                    doorIndex;
            }

            var roomPositionChanged =
                Vector3.Distance(
                    movedRoomWorld,
                    roomWorld) >
                0.0001f;

            var roomSizeChanged =
                Vector2.Distance(
                    roomSize,
                    door.RoomDoorColliderSize) >
                0.0001f;

            var arrivePositionChanged =
                Vector3.Distance(
                    movedArriveWorld,
                    arriveWorld) >
                0.0001f;

            var arriveSizeChanged =
                Vector2.Distance(
                    arriveSize,
                    door.ArriveDoorColliderSize) >
                0.0001f;

            if (!roomPositionChanged &&
                !roomSizeChanged &&
                !arrivePositionChanged &&
                !arriveSizeChanged)
            {
                return;
            }

            Undo.RecordObject(
                manager,
                "Edit Room Door Link");

            if (roomPositionChanged)
            {
                var roomLocal =
                    roomMatrix.inverse
                        .MultiplyPoint3x4(
                            movedRoomWorld);

                door.EditorSetRoomDoorPosition(
                    new Vector2(
                        roomLocal.x,
                        roomLocal.y));
            }

            if (roomSizeChanged)
            {
                door.EditorSetRoomDoorColliderSize(
                    roomSize);
            }

            if (arrivePositionChanged)
            {
                door.EditorSetArriveDoorPosition(
                    new Vector2(
                        movedArriveWorld.x,
                        movedArriveWorld.y));
            }

            if (arriveSizeChanged)
            {
                door.EditorSetArriveDoorColliderSize(
                    arriveSize);
            }

            EditorUtility.SetDirty(manager);
        }

        private static void DrawDoorPairLine(
            Vector3 roomWorld,
            Vector3 arriveWorld,
            Color color,
            string roomName,
            string doorName)
        {
            var lineColor =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    0.9f);

            Handles.color = lineColor;

            Handles.DrawAAPolyLine(
                4f,
                roomWorld,
                arriveWorld);

            var middle =
                Vector3.Lerp(
                    roomWorld,
                    arriveWorld,
                    0.5f);

            var labelStyle =
                new GUIStyle(
                    EditorStyles.boldLabel);

            labelStyle.normal.textColor =
                color;

            Handles.Label(
                middle,
                $"{roomName} / {doorName}",
                labelStyle);
        }

        private static bool DrawDoorColliderEditor(
            RoomManager manager,
            Vector3 center,
            Quaternion rotation,
            Vector2 size,
            Color color,
            string sideLabel,
            bool selected,
            out Vector3 movedCenter,
            out Vector2 editedSize)
        {
            size =
                new Vector2(
                    Mathf.Max(0.05f, size.x),
                    Mathf.Max(0.05f, size.y));

            movedCenter = center;
            editedSize = size;

            var right =
                rotation * Vector3.right;
            var up =
                rotation * Vector3.up;

            var halfX =
                right * (size.x * 0.5f);

            var halfY =
                up * (size.y * 0.5f);

            var corners =
                new[]
                {
                    center - halfX - halfY,
                    center + halfX - halfY,
                    center + halfX + halfY,
                    center - halfX + halfY
                };

            var fill =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    selected ? 0.18f : 0.08f);

            var outline =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    selected ? 1f : 0.72f);

            Handles.DrawSolidRectangleWithOutline(
                corners,
                fill,
                outline);

            var handleSize =
                HandleUtility.GetHandleSize(center) *
                (selected ? 0.085f : 0.065f);

            Handles.color = outline;

            EditorGUI.BeginChangeCheck();

            var newCenter =
                Handles.FreeMoveHandle(
                    center,
                    handleSize,
                    Vector3.zero,
                    Handles.RectangleHandleCap);

            var centerChanged =
                EditorGUI.EndChangeCheck();

            if (centerChanged)
            {
                movedCenter =
                    new Vector3(
                        newCenter.x,
                        newCenter.y,
                        center.z);
            }

            // X 크기: 오른쪽 변 중앙 핸들.
            var xHandle =
                center +
                right * (size.x * 0.5f);

            EditorGUI.BeginChangeCheck();

            var movedX =
                Handles.Slider(
                    xHandle,
                    right,
                    handleSize * 0.75f,
                    Handles.CubeHandleCap,
                    0f);

            if (EditorGUI.EndChangeCheck())
            {
                var halfWidth =
                    Mathf.Abs(
                        Vector3.Dot(
                            movedX - center,
                            right));

                editedSize.x =
                    Mathf.Max(
                        0.05f,
                        halfWidth * 2f);
            }

            // Y 크기: 위쪽 변 중앙 핸들.
            var yHandle =
                center +
                up * (size.y * 0.5f);

            EditorGUI.BeginChangeCheck();

            var movedY =
                Handles.Slider(
                    yHandle,
                    up,
                    handleSize * 0.75f,
                    Handles.CubeHandleCap,
                    0f);

            if (EditorGUI.EndChangeCheck())
            {
                var halfHeight =
                    Mathf.Abs(
                        Vector3.Dot(
                            movedY - center,
                            up));

                editedSize.y =
                    Mathf.Max(
                        0.05f,
                        halfHeight * 2f);
            }

            var labelStyle =
                new GUIStyle(
                    EditorStyles.boldLabel);

            labelStyle.normal.textColor =
                color;

            Handles.Label(
                center +
                up *
                (
                    size.y * 0.5f +
                    HandleUtility.GetHandleSize(center) *
                    0.08f
                ),
                $"{sideLabel}  {size.x:0.##} × {size.y:0.##}m",
                labelStyle);

            // 클릭 선택 판정은 중앙 Move Handle 변경/클릭을 이용한다.
            var current =
                Event.current;

            var clicked =
                centerChanged;

            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                !current.alt)
            {
                var mouse =
                    current.mousePosition;

                var guiCenter =
                    HandleUtility.WorldToGUIPoint(
                        center);

                if (Vector2.Distance(
                        mouse,
                        guiCenter) <= 18f)
                {
                    clicked = true;
                }
            }

            return clicked;
        }

        private static void CalculateEditorPlacementTransforms(
            RoomManager manager,
            out List<Matrix4x4> matrices)
        {
            matrices =
                new List<Matrix4x4>();

            var placements =
                manager.RoomPrefabs;

            if (placements == null)
                return;

            CalculatePackedCellSize(
                manager,
                placements,
                out var packedCellWidth,
                out var packedCellHeight);

            var layoutCursor = 0f;
            var previousHalfSize = 0f;
            var packedItemIndex = 0;

            for (var index = 0;
                 index < placements.Count;
                 index++)
            {
                var placement =
                    placements[index];

                if (placement == null ||
                    !placement.Enabled ||
                    placement.Prefab == null)
                {
                    matrices.Add(
                        manager.transform.localToWorldMatrix);
                    continue;
                }

                var position =
                    ResolvePlacementPosition(
                        manager,
                        placement,
                        placement.Prefab,
                        ref layoutCursor,
                        ref previousHalfSize,
                        packedItemIndex,
                        packedCellWidth,
                        packedCellHeight);

                if (manager.LayoutMode ==
                    RoomLayoutMode.Packed)
                {
                    packedItemIndex++;
                }

                var localMatrix =
                    Matrix4x4.TRS(
                        new Vector3(
                            position.x,
                            position.y,
                            0f),
                        Quaternion.Euler(
                            0f,
                            0f,
                            placement.RotationDegrees),
                        Vector3.one);

                matrices.Add(
                    manager.transform.localToWorldMatrix *
                    localMatrix);
            }
        }

        internal static void BakeAll(RoomManager manager)
        {
            if (manager == null ||
                _isBaking ||
                Application.isPlaying)
            {
                return;
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                Debug.LogWarning(
                    "[RoomManager] Prefab Mode에서는 Scene Map/NavMesh Bake를 실행하지 않습니다. " +
                    "GameScene으로 돌아간 뒤 RoomManager에서 Bake하십시오.",
                    manager);
                return;
            }

            Undo.RecordObject(
                manager,
                "Ensure Room Placement Ids");

            manager.EditorEnsurePlacementIds();
            EditorUtility.SetDirty(manager);

            if (!ValidateManager(manager))
                return;

            _isBaking = true;

            try
            {
                ClearBaked(manager);

            var bakedRootObject =
                new GameObject("__BakedRooms");

            Undo.RegisterCreatedObjectUndo(
                bakedRootObject,
                "Bake Room Maps");

            var bakedRoot = bakedRootObject.transform;
            bakedRoot.SetParent(manager.transform, false);
            bakedRoot.localPosition = Vector3.zero;
            bakedRoot.localRotation = Quaternion.identity;
            bakedRoot.localScale = Vector3.one;

            var fogRooms =
                new List<BakedFogRoom>();

            var placements = manager.RoomPrefabs;
            var layoutCursor = 0f;
            var previousHalfSize = 0f;

            var arriveDoorsRoot =
                CreateArriveDoorsRoot(
                    bakedRoot);

            CalculatePackedCellSize(
                manager,
                placements,
                out var packedCellWidth,
                out var packedCellHeight);

            var packedItemIndex = 0;

            for (var index = 0;
                 index < placements.Count;
                 index++)
            {
                var placement = placements[index];
                if (placement == null ||
                    !placement.Enabled ||
                    placement.Prefab == null)
                {
                    continue;
                }

                var source = placement.Prefab;
                var position = ResolvePlacementPosition(
                    manager,
                    placement,
                    source,
                    ref layoutCursor,
                    ref previousHalfSize,
                    packedItemIndex,
                    packedCellWidth,
                    packedCellHeight);

                if (manager.LayoutMode == RoomLayoutMode.Packed)
                    packedItemIndex++;

                BakeOne(
                    manager,
                    placement,
                    source,
                    index,
                    position,
                    placement.RotationDegrees,
                    bakedRoot,
                    arriveDoorsRoot,
                    fogRooms);
            }

            Undo.RecordObject(
                manager,
                "Store Baked Room Result");

            manager.EditorSetBakedResult(
                bakedRoot,
                fogRooms);

            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(
                manager.gameObject.scene);

            var navMeshRebuilt = false;

            if (manager.RebuildNavMeshAfterEditorBake)
            {
                var navMeshManager =
                    FindOrCreateNavMeshManager(manager);

                if (navMeshManager != null)
                {
                    navMeshManager.Rebuild();
                    navMeshRebuilt = true;

                    EditorUtility.SetDirty(navMeshManager);
                    EditorSceneManager.MarkSceneDirty(
                        manager.gameObject.scene);
                }
            }

            Debug.Log(
                $"[RoomManager] Bake 완료 · " +
                $"Map {CountEnabledPlacements(placements)}개 · " +
                $"Fog Room {fogRooms.Count}개 · " +
                $"NavMesh {(navMeshRebuilt ? "Rebuilt" : "Not Rebuilt")}",
                manager);
            }
            finally
            {
                _isBaking = false;
            }
        }

        private static Vector2 ResolvePlacementPosition(
            RoomManager manager,
            RoomPlacement placement,
            RoomPrefab source,
            ref float cursor,
            ref float previousHalfSize,
            int packedItemIndex,
            float packedCellWidth,
            float packedCellHeight)
        {
            if (manager.LayoutMode == RoomLayoutMode.Manual)
                return placement.LocalPosition;

            if (manager.LayoutMode == RoomLayoutMode.Packed)
            {
                var columns =
                    Mathf.Max(1, manager.PackedColumns);

                var requestedRows =
                    Mathf.Max(0, manager.PackedRows);

                // Rows=0이면 4 x N 같은 자동 세로 확장.
                // Rows>0이면 지정한 행 수를 한 Grid Page로 간주합니다.
                // 등록 수가 capacity를 넘으면 다음 Page를 오른쪽에 이어 붙여
                // 어떤 Prefab도 누락하지 않습니다.
                var capacity =
                    requestedRows > 0
                        ? columns * requestedRows
                        : int.MaxValue;

                var page =
                    capacity == int.MaxValue
                        ? 0
                        : packedItemIndex / capacity;

                var indexInPage =
                    capacity == int.MaxValue
                        ? packedItemIndex
                        : packedItemIndex % capacity;

                var column =
                    indexInPage % columns;

                var row =
                    indexInPage / columns;

                var pageWidth =
                    requestedRows > 0
                        ? columns * packedCellWidth
                        : 0f;

                var x =
                    column * packedCellWidth +
                    page * (
                        pageWidth +
                        manager.AutoLayoutGapMeters);

                var y =
                    -row * packedCellHeight;

                return new Vector2(x, y);
            }

            var size = source.MapSizeMeters;

            var currentSize =
                manager.LayoutMode == RoomLayoutMode.Horizontal
                    ? size.x
                    : size.y;

            var half = currentSize * 0.5f;

            if (cursor == 0f && previousHalfSize == 0f)
            {
                cursor = 0f;
            }
            else
            {
                cursor +=
                    previousHalfSize +
                    manager.AutoLayoutGapMeters +
                    half;
            }

            previousHalfSize = half;

            return manager.LayoutMode == RoomLayoutMode.Horizontal
                ? new Vector2(cursor, 0f)
                : new Vector2(0f, -cursor);
        }

        private static void CalculatePackedCellSize(
            RoomManager manager,
            IReadOnlyList<RoomPlacement> placements,
            out float cellWidth,
            out float cellHeight)
        {
            var maximumWidth = 0f;
            var maximumHeight = 0f;

            if (placements != null)
            {
                for (var index = 0;
                     index < placements.Count;
                     index++)
                {
                    var placement = placements[index];

                    if (placement == null ||
                        !placement.Enabled ||
                        placement.Prefab == null)
                    {
                        continue;
                    }

                    var size =
                        placement.Prefab.MapSizeMeters;

                    maximumWidth =
                        Mathf.Max(
                            maximumWidth,
                            size.x);

                    maximumHeight =
                        Mathf.Max(
                            maximumHeight,
                            size.y);
                }
            }

            cellWidth =
                Mathf.Max(0.01f, maximumWidth) +
                manager.AutoLayoutGapMeters;

            cellHeight =
                Mathf.Max(0.01f, maximumHeight) +
                manager.AutoLayoutGapMeters;
        }

        private static void BakeOne(
            RoomManager manager,
            RoomPlacement placement,
            RoomPrefab source,
            int placementIndex,
            Vector2 position,
            float rotationDegrees,
            Transform bakedRoot,
            Transform arriveDoorsRoot,
            List<BakedFogRoom> fogRooms)
        {
            var instanceId =
                !string.IsNullOrWhiteSpace(
                    placement.DisplayName)
                    ? placement.DisplayName
                    : $"{source.PrefabId}_{placementIndex:00}";

            var mapRootObject =
                new GameObject(instanceId);

            Undo.RegisterCreatedObjectUndo(
                mapRootObject,
                "Bake Room Map");

            var mapRoot = mapRootObject.transform;
            mapRoot.SetParent(bakedRoot, false);
            mapRoot.localPosition =
                new Vector3(position.x, position.y, 0f);
            mapRoot.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    rotationDegrees);
            mapRoot.localScale = Vector3.one;

            var scale = source.MapScale;

            if (source.MapSprite != null)
            {
                var floorDefinition =
                    source.FloorDefinitionOverride != null
                        ? source.FloorDefinitionOverride
                        : manager.DefaultFloorDefinition;

                CreateField(
                    mapRoot,
                    "Map_Floor",
                    source.MapSprite,
                    scale,
                    source.MapSortingLayerName,
                    source.MapSortingOrder,
                    floorDefinition,
                    instanceId + "_floor",
                    source.MapWalkableAlphaThreshold,
                    source.AlphaColliderSimplifyPixels,
                    "Walkable");
            }

            if (source.WallSprite != null)
            {
                var obstacleDefinition =
                    source.ObstacleDefinitionOverride != null
                        ? source.ObstacleDefinitionOverride
                        : manager.DefaultObstacleDefinition;

                CreateField(
                    mapRoot,
                    "Wall_Obstacle",
                    source.WallSprite,
                    scale,
                    source.WallSortingLayerName,
                    source.WallSortingOrder,
                    obstacleDefinition,
                    instanceId + "_wall",
                    source.WallObstacleAlphaThreshold,
                    source.AlphaColliderSimplifyPixels,
                    "Not Walkable");
            }

            var fogRootObject =
                new GameObject("Fog");

            Undo.RegisterCreatedObjectUndo(
                fogRootObject,
                "Create Room Fog Root");

            var fogRoot = fogRootObject.transform;
            fogRoot.SetParent(mapRoot, false);

            for (var roomIndex = 0;
                 roomIndex < source.Rooms.Count;
                 roomIndex++)
            {
                var definition = source.Rooms[roomIndex];
                if (definition == null ||
                    !definition.IsValid)
                {
                    continue;
                }

                var fogObject =
                    new GameObject(
                        definition.RoomId + "_Fog");

                Undo.RegisterCreatedObjectUndo(
                    fogObject,
                    "Create Room Fog");

                var fogTransform = fogObject.transform;
                fogTransform.SetParent(fogRoot, false);
                fogTransform.localPosition = Vector3.zero;
                fogTransform.localRotation = Quaternion.identity;
                fogTransform.localScale = Vector3.one;

                var filter =
                    Undo.AddComponent<MeshFilter>(
                        fogObject);

                var renderer =
                    Undo.AddComponent<MeshRenderer>(
                        fogObject);

                renderer.sortingLayerName =
                    manager.FogSortingLayerName;
                renderer.sortingOrder =
                    manager.FogSortingOrder;
                renderer.enabled = false;

                var record = new BakedFogRoom();
                record.EditorConfigure(
                    instanceId,
                    definition.RoomId,
                    mapRoot,
                    definition.Points,
                    filter,
                    renderer);

                fogRooms.Add(record);
            }
            CreateDoorPairs(
                manager,
                placement,
                mapRoot,
                arriveDoorsRoot);
        }

        private static Transform CreateArriveDoorsRoot(
            Transform bakedRoot)
        {
            var rootObject =
                new GameObject("__ArriveDoors");

            Undo.RegisterCreatedObjectUndo(
                rootObject,
                "Create Arrive Doors Root");

            var root =
                rootObject.transform;

            root.SetParent(
                bakedRoot,
                false);

            root.localPosition =
                Vector3.zero;
            root.localRotation =
                Quaternion.identity;
            root.localScale =
                Vector3.one;

            return root;
        }

        private static void CreateDoorPairs(
            RoomManager manager,
            RoomPlacement placement,
            Transform roomRoot,
            Transform arriveDoorsRoot)
        {
            if (manager == null ||
                placement == null ||
                roomRoot == null ||
                arriveDoorsRoot == null ||
                placement.DoorLinks == null ||
                placement.DoorLinks.Count == 0)
            {
                return;
            }

            Transform roomDoorsRoot = null;

            for (var index = 0;
                 index < placement.DoorLinks.Count;
                 index++)
            {
                var definition =
                    placement.DoorLinks[index];

                if (definition == null ||
                    !definition.Enabled)
                {
                    continue;
                }

                if (roomDoorsRoot == null)
                {
                    var roomDoorsObject =
                        new GameObject("Doors");

                    Undo.RegisterCreatedObjectUndo(
                        roomDoorsObject,
                        "Create Room Doors");

                    roomDoorsRoot =
                        roomDoorsObject.transform;

                    roomDoorsRoot.SetParent(
                        roomRoot,
                        false);
                }

                CreateDoorPair(
                    manager,
                    placement,
                    definition,
                    roomDoorsRoot,
                    arriveDoorsRoot);
            }
        }

        private static void CreateDoorPair(
            RoomManager manager,
            RoomPlacement placement,
            RoomDoorLinkDefinition definition,
            Transform roomDoorsRoot,
            Transform arriveDoorsRoot)
        {
            var roomInstanceId =
                RoomManager.BuildRoomDoorInstanceId(
                    placement.PlacementId,
                    definition.DoorId);

            var arriveInstanceId =
                RoomManager.BuildArriveDoorInstanceId(
                    placement.PlacementId,
                    definition.DoorId);

            var roomDoor =
                CreateDoorObject(
                    definition.DisplayName + "_Room",
                    roomDoorsRoot,
                    true,
                    definition.RoomDoorPosition,
                    definition.RoomDoorRotation,
                    definition.RoomDoorColliderSize,
                    manager.DoorDefinition,
                    roomInstanceId,
                    arriveInstanceId);

            var arriveDoor =
                CreateDoorObject(
                    definition.DisplayName + "_Arrive",
                    arriveDoorsRoot,
                    false,
                    definition.ArriveDoorPosition,
                    definition.ArriveDoorRotation,
                    definition.ArriveDoorColliderSize,
                    manager.DoorDefinition,
                    arriveInstanceId,
                    roomInstanceId);

            Debug.Log(
                $"[RoomManager] Door Pair Bake · " +
                $"{placement.DisplayName}/{definition.DisplayName} · " +
                $"Room={roomInstanceId} · " +
                $"Arrive={arriveInstanceId}",
                roomDoor != null
                    ? roomDoor
                    : arriveDoor);
        }

        private static InteractivePawn CreateDoorObject(
            string objectName,
            Transform parent,
            bool localToRoom,
            Vector2 position,
            float rotationDegrees,
            Vector2 colliderSize,
            InteractivePawnDefinition doorDefinition,
            string instanceId,
            string linkedInstanceId)
        {
            var doorObject =
                new GameObject(objectName);

            Undo.RegisterCreatedObjectUndo(
                doorObject,
                "Create Door Object");

            var doorTransform =
                doorObject.transform;

            if (localToRoom)
            {
                doorTransform.SetParent(
                    parent,
                    false);

                doorTransform.localPosition =
                    new Vector3(
                        position.x,
                        position.y,
                        0f);

                doorTransform.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        rotationDegrees);
            }
            else
            {
                doorTransform.SetParent(
                    parent,
                    true);

                doorTransform.position =
                    new Vector3(
                        position.x,
                        position.y,
                        parent.position.z);

                doorTransform.rotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        rotationDegrees);
            }

            doorTransform.localScale =
                Vector3.one;

            var trigger =
                Undo.AddComponent<BoxCollider2D>(
                    doorObject);

            trigger.isTrigger = true;
            trigger.size =
                new Vector2(
                    Mathf.Max(0.05f, colliderSize.x),
                    Mathf.Max(0.05f, colliderSize.y));

            var body =
                Undo.AddComponent<Rigidbody2D>(
                    doorObject);

            body.bodyType =
                RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;

            var arrivalObject =
                new GameObject("ArrivalPoint");

            Undo.RegisterCreatedObjectUndo(
                arrivalObject,
                "Create Door Arrival Point");

            var arrival =
                arrivalObject.transform;

            arrival.SetParent(
                doorTransform,
                false);

            // RoomDoorPosition / ArriveDoorPosition 자체를
            // 각 Door 쌍의 도착 위치로 사용합니다.
            arrival.localPosition =
                Vector3.zero;
            arrival.localRotation =
                Quaternion.identity;
            arrival.localScale =
                Vector3.one;

            var pawn =
                Undo.AddComponent<InteractivePawn>(
                    doorObject);

            ConfigureDoorPawn(
                pawn,
                doorDefinition,
                instanceId,
                linkedInstanceId,
                arrival,
                trigger);

            EditorUtility.SetDirty(pawn);

            return pawn;
        }

        private static void ConfigureDoorPawn(
            InteractivePawn pawn,
            InteractivePawnDefinition definition,
            string instanceId,
            string linkedInstanceId,
            Transform arrivalPoint,
            Collider2D trigger)
        {
            if (pawn == null)
                return;

            var serialized =
                new SerializedObject(pawn);

            SetObjectReference(
                serialized,
                "_definition",
                definition);

            SetString(
                serialized,
                "_instanceId",
                instanceId);

            SetString(
                serialized,
                "_linkedDoorInstanceId",
                linkedInstanceId);

            SetObjectReference(
                serialized,
                "_arrivalPoint",
                arrivalPoint);

            SetObjectReference(
                serialized,
                "_doorTrigger",
                trigger);

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectReference(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value)
        {
            var property =
                serialized.FindProperty(
                    propertyName);

            if (property != null)
                property.objectReferenceValue = value;
        }

        private static void SetString(
            SerializedObject serialized,
            string propertyName,
            string value)
        {
            var property =
                serialized.FindProperty(
                    propertyName);

            if (property != null)
            {
                property.stringValue =
                    value ?? string.Empty;
            }
        }

        private static void CreateField(
            Transform parent,
            string objectName,
            Sprite sprite,
            float mapScale,
            string sortingLayerName,
            int sortingOrder,
            FieldPawnDefinition definition,
            string instanceId,
            float alphaThreshold,
            float simplifyPixels,
            string navigationLabel)
        {
            var fieldObject =
                new GameObject(objectName);

            Undo.RegisterCreatedObjectUndo(
                fieldObject,
                "Create Baked Field");

            var fieldTransform = fieldObject.transform;
            fieldTransform.SetParent(parent, false);
            fieldTransform.localPosition = Vector3.zero;
            fieldTransform.localRotation = Quaternion.identity;
            fieldTransform.localScale =
                new Vector3(
                    mapScale,
                    mapScale,
                    1f);

            var renderer =
                Undo.AddComponent<SpriteRenderer>(
                    fieldObject);

            renderer.sprite = sprite;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;

            var collider =
                Undo.AddComponent<PolygonCollider2D>(
                    fieldObject);

            if (!ApplySpriteAlphaShape(
                    sprite,
                    collider,
                    alphaThreshold,
                    simplifyPixels,
                    out var opaquePixelCount,
                    out var generatedPathCount,
                    out var alphaError))
            {
                Debug.LogError(
                    $"[{objectName}] {navigationLabel} Alpha Collider 생성 실패: " +
                    alphaError,
                    fieldObject);
            }
            else
            {
                Debug.Log(
                    $"[{objectName}] {navigationLabel} Alpha Collider 완료 · " +
                    $"Opaque {opaquePixelCount} px · Paths {generatedPathCount}",
                    fieldObject);
            }

            var fieldPawn =
                Undo.AddComponent<FieldPawn>(
                    fieldObject);

            ConfigureFieldPawn(
                fieldPawn,
                definition,
                collider,
                instanceId);

            fieldPawn.PrepareNavigation();

            Debug.Log(
                $"[RoomManager] Generated {objectName} · " +
                $"Sprite={sprite.name} · AlphaThreshold={alphaThreshold:0.###} · " +
                $"Scale={mapScale:0.###}",
                fieldObject);
        }

        private static void ConfigureFieldPawn(
            FieldPawn fieldPawn,
            FieldPawnDefinition definition,
            Collider2D collider,
            string instanceId)
        {
            var serialized =
                new SerializedObject(fieldPawn);

            var definitionProperty =
                serialized.FindProperty("_definition");
            var colliderProperty =
                serialized.FindProperty(
                    "_navigationCollider");
            var instanceProperty =
                serialized.FindProperty("_instanceId");

            if (definitionProperty != null)
                definitionProperty.objectReferenceValue =
                    definition;

            if (colliderProperty != null)
                colliderProperty.objectReferenceValue =
                    collider;

            if (instanceProperty != null)
                instanceProperty.stringValue =
                    instanceId;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private struct AlphaBoundaryEdge
        {
            public Vector2Int From;
            public Vector2Int To;
            public bool Used;

            public AlphaBoundaryEdge(
                Vector2Int from,
                Vector2Int to)
            {
                From = from;
                To = to;
                Used = false;
            }
        }

        /// <summary>
        /// Sprite Editor Physics Shape를 전혀 사용하지 않고,
        /// Sprite 원본 텍스처의 Alpha 픽셀을 직접 읽어 PolygonCollider2D를 만듭니다.
        ///
        /// Map에 사용하면 Alpha > threshold = Walkable,
        /// Wall에 사용하면 Alpha > threshold = Not Walkable이 됩니다.
        ///
        /// 외곽과 내부 Hole은 서로 다른 path로 생성되며,
        /// 투명 영역은 Collider에 포함되지 않습니다.
        /// </summary>
        private static bool ApplySpriteAlphaShape(
            Sprite sprite,
            PolygonCollider2D collider,
            float alphaThreshold,
            float simplifyPixels,
            out int opaquePixelCount,
            out int generatedPathCount,
            out string error)
        {
            opaquePixelCount = 0;
            generatedPathCount = 0;
            error = string.Empty;

            if (sprite == null)
            {
                error = "Sprite가 비어 있습니다.";
                return false;
            }

            if (collider == null)
            {
                error = "PolygonCollider2D가 비어 있습니다.";
                return false;
            }

            if (!TryBuildAlphaPolygonPaths(
                    sprite,
                    alphaThreshold,
                    simplifyPixels,
                    out var paths,
                    out opaquePixelCount,
                    out error))
            {
                collider.pathCount = 0;
                return false;
            }

            if (paths.Count == 0)
            {
                collider.pathCount = 0;
                generatedPathCount = 0;
                error =
                    $"'{sprite.name}'에서 Alpha > " +
                    $"{Mathf.Clamp01(alphaThreshold):0.###} 픽셀이 없습니다.";
                return false;
            }

            collider.pathCount = paths.Count;

            for (var pathIndex = 0;
                 pathIndex < paths.Count;
                 pathIndex++)
            {
                collider.SetPath(
                    pathIndex,
                    paths[pathIndex]);
            }

            generatedPathCount = paths.Count;
            return true;
        }

        private static bool TryBuildAlphaPolygonPaths(
            Sprite sprite,
            float alphaThreshold,
            float simplifyPixels,
            out List<Vector2[]> localPaths,
            out int opaquePixelCount,
            out string error)
        {
            localPaths = new List<Vector2[]>();
            opaquePixelCount = 0;
            error = string.Empty;

            // Reimport 전 Sprite 정보를 먼저 복사해 둡니다.
            var spriteRect = sprite.rect;
            var spritePivot = sprite.pivot;
            var pixelsPerUnit =
                Mathf.Max(0.00001f, sprite.pixelsPerUnit);
            var spriteName = sprite.name;
            var assetPath = AssetDatabase.GetAssetPath(sprite);

            var rectX = Mathf.RoundToInt(spriteRect.x);
            var rectY = Mathf.RoundToInt(spriteRect.y);
            var width = Mathf.RoundToInt(spriteRect.width);
            var height = Mathf.RoundToInt(spriteRect.height);

            if (width <= 0 || height <= 0)
            {
                error =
                    $"'{spriteName}' Sprite Rect 크기가 올바르지 않습니다.";
                return false;
            }

            Texture2D texture = null;
            TextureImporter importer = null;
            var restoreReadable = false;

            try
            {
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    importer =
                        AssetImporter.GetAtPath(assetPath)
                        as TextureImporter;

                    if (importer != null &&
                        !importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        restoreReadable = true;
                    }

                    texture =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            assetPath);
                }

                if (texture == null)
                    texture = sprite.texture;

                if (texture == null)
                {
                    error =
                        $"'{spriteName}'의 Texture2D를 찾지 못했습니다.";
                    return false;
                }

                if (!texture.isReadable)
                {
                    error =
                        $"'{spriteName}' Texture를 CPU에서 읽을 수 없습니다. " +
                        "자동 Read/Write 전환에도 실패했습니다.";
                    return false;
                }

                var allPixels = texture.GetPixels32();

                if (rectX < 0 ||
                    rectY < 0 ||
                    rectX + width > texture.width ||
                    rectY + height > texture.height)
                {
                    error =
                        $"'{spriteName}' Sprite Rect가 Texture 범위를 벗어납니다. " +
                        $"Rect=({rectX},{rectY},{width},{height}), " +
                        $"Texture={texture.width}x{texture.height}";
                    return false;
                }

                var thresholdByte =
                    Mathf.Clamp(
                        Mathf.RoundToInt(
                            Mathf.Clamp01(alphaThreshold) * 255f),
                        0,
                        255);

                var opaque =
                    new bool[width * height];

                for (var y = 0; y < height; y++)
                {
                    var textureRow =
                        (rectY + y) * texture.width;

                    var maskRow = y * width;

                    for (var x = 0; x < width; x++)
                    {
                        var alpha =
                            allPixels[
                                textureRow +
                                rectX +
                                x].a;

                        // threshold=0이면 alpha가 단 1이라도 있는 픽셀을 포함합니다.
                        var isOpaque =
                            alpha > thresholdByte;

                        opaque[maskRow + x] =
                            isOpaque;

                        if (isOpaque)
                            opaquePixelCount++;
                    }
                }

                if (opaquePixelCount == 0)
                    return true;

                var gridLoops =
                    TraceOpaquePixelBoundaries(
                        opaque,
                        width,
                        height);

                for (var loopIndex = 0;
                     loopIndex < gridLoops.Count;
                     loopIndex++)
                {
                    var loop = gridLoops[loopIndex];

                    RemoveDuplicateAndCollinearPoints(loop);

                    if (simplifyPixels > 0f)
                    {
                        SimplifyClosedPixelLoop(
                            loop,
                            simplifyPixels);
                    }

                    RemoveDuplicateAndCollinearPoints(loop);

                    if (loop.Count < 3)
                        continue;

                    var local =
                        new Vector2[loop.Count];

                    for (var pointIndex = 0;
                         pointIndex < loop.Count;
                         pointIndex++)
                    {
                        var point = loop[pointIndex];

                        local[pointIndex] =
                            new Vector2(
                                (point.x - spritePivot.x) /
                                pixelsPerUnit,
                                (point.y - spritePivot.y) /
                                pixelsPerUnit);
                    }

                    localPaths.Add(local);
                }

                return true;
            }
            catch (Exception exception)
            {
                error =
                    $"'{spriteName}' Alpha 읽기/외곽선 생성 중 예외: " +
                    exception.Message;
                return false;
            }
            finally
            {
                if (restoreReadable &&
                    !string.IsNullOrWhiteSpace(assetPath))
                {
                    var restoreImporter =
                        AssetImporter.GetAtPath(assetPath)
                        as TextureImporter;

                    if (restoreImporter != null)
                    {
                        restoreImporter.isReadable = false;
                        restoreImporter.SaveAndReimport();
                    }
                }
            }
        }

        /// <summary>
        /// 각각의 불투명 픽셀 셀에서 투명 픽셀과 맞닿는 변만 수집합니다.
        /// 변의 방향은 항상 불투명 영역이 왼쪽에 오도록 하므로
        /// 외곽과 Hole의 winding이 자연스럽게 반대가 됩니다.
        /// </summary>
        private static List<List<Vector2Int>>
            TraceOpaquePixelBoundaries(
                bool[] opaque,
                int width,
                int height)
        {
            var edges =
                new List<AlphaBoundaryEdge>();

            var outgoing =
                new Dictionary<
                    Vector2Int,
                    List<int>>();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!IsOpaque(
                            opaque,
                            width,
                            height,
                            x,
                            y))
                    {
                        continue;
                    }

                    // Bottom: left -> right
                    if (!IsOpaque(
                            opaque,
                            width,
                            height,
                            x,
                            y - 1))
                    {
                        AddBoundaryEdge(
                            edges,
                            outgoing,
                            new Vector2Int(x, y),
                            new Vector2Int(x + 1, y));
                    }

                    // Right: bottom -> top
                    if (!IsOpaque(
                            opaque,
                            width,
                            height,
                            x + 1,
                            y))
                    {
                        AddBoundaryEdge(
                            edges,
                            outgoing,
                            new Vector2Int(x + 1, y),
                            new Vector2Int(x + 1, y + 1));
                    }

                    // Top: right -> left
                    if (!IsOpaque(
                            opaque,
                            width,
                            height,
                            x,
                            y + 1))
                    {
                        AddBoundaryEdge(
                            edges,
                            outgoing,
                            new Vector2Int(x + 1, y + 1),
                            new Vector2Int(x, y + 1));
                    }

                    // Left: top -> bottom
                    if (!IsOpaque(
                            opaque,
                            width,
                            height,
                            x - 1,
                            y))
                    {
                        AddBoundaryEdge(
                            edges,
                            outgoing,
                            new Vector2Int(x, y + 1),
                            new Vector2Int(x, y));
                    }
                }
            }

            var loops =
                new List<List<Vector2Int>>();

            for (var startEdgeIndex = 0;
                 startEdgeIndex < edges.Count;
                 startEdgeIndex++)
            {
                if (edges[startEdgeIndex].Used)
                    continue;

                var startEdge =
                    edges[startEdgeIndex];

                var startPoint =
                    startEdge.From;

                var loop =
                    new List<Vector2Int>
                    {
                        startPoint
                    };

                var currentEdgeIndex =
                    startEdgeIndex;

                var incomingDirection =
                    startEdge.To -
                    startEdge.From;

                var guard = 0;
                var maximumGuard =
                    Mathf.Max(16, edges.Count + 4);

                while (guard < maximumGuard)
                {
                    var currentEdge =
                        edges[currentEdgeIndex];

                    currentEdge.Used = true;
                    edges[currentEdgeIndex] =
                        currentEdge;

                    var currentPoint =
                        currentEdge.To;

                    if (currentPoint == startPoint)
                        break;

                    loop.Add(currentPoint);

                    if (!outgoing.TryGetValue(
                            currentPoint,
                            out var candidates))
                    {
                        // 정상적인 Alpha Mask 외곽은 항상 닫혀 있어야 합니다.
                        break;
                    }

                    var nextEdgeIndex =
                        SelectNextBoundaryEdge(
                            edges,
                            candidates,
                            incomingDirection);

                    if (nextEdgeIndex < 0)
                        break;

                    var nextEdge =
                        edges[nextEdgeIndex];

                    incomingDirection =
                        nextEdge.To -
                        nextEdge.From;

                    currentEdgeIndex =
                        nextEdgeIndex;

                    guard++;
                }

                if (loop.Count >= 3)
                    loops.Add(loop);
            }

            return loops;
        }

        private static bool IsOpaque(
            bool[] opaque,
            int width,
            int height,
            int x,
            int y)
        {
            if (x < 0 ||
                y < 0 ||
                x >= width ||
                y >= height)
            {
                return false;
            }

            return opaque[y * width + x];
        }

        private static void AddBoundaryEdge(
            List<AlphaBoundaryEdge> edges,
            Dictionary<Vector2Int, List<int>> outgoing,
            Vector2Int from,
            Vector2Int to)
        {
            var index = edges.Count;

            edges.Add(
                new AlphaBoundaryEdge(
                    from,
                    to));

            if (!outgoing.TryGetValue(
                    from,
                    out var list))
            {
                list = new List<int>(2);
                outgoing.Add(from, list);
            }

            list.Add(index);
        }

        /// <summary>
        /// 대각선으로 픽셀 두 개가 꼭짓점 하나만 공유하는 경우에도
        /// 서로 다른 Island가 한 Polygon으로 합쳐지지 않도록
        /// 가능한 진행 중 가장 왼쪽 회전을 우선합니다.
        /// </summary>
        private static int SelectNextBoundaryEdge(
            List<AlphaBoundaryEdge> edges,
            List<int> candidates,
            Vector2Int incomingDirection)
        {
            var bestIndex = -1;
            var bestScore = int.MinValue;

            for (var index = 0;
                 index < candidates.Count;
                 index++)
            {
                var candidateIndex =
                    candidates[index];

                var candidate =
                    edges[candidateIndex];

                if (candidate.Used)
                    continue;

                var outgoingDirection =
                    candidate.To -
                    candidate.From;

                var score =
                    GetTurnScore(
                        incomingDirection,
                        outgoingDirection);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestIndex = candidateIndex;
            }

            return bestIndex;
        }

        private static int GetTurnScore(
            Vector2Int incoming,
            Vector2Int outgoing)
        {
            var cross =
                incoming.x * outgoing.y -
                incoming.y * outgoing.x;

            var dot =
                incoming.x * outgoing.x +
                incoming.y * outgoing.y;

            if (cross > 0)
                return 30; // left

            if (dot > 0)
                return 20; // straight

            if (cross < 0)
                return 10; // right

            return 0; // reverse
        }

        private static void RemoveDuplicateAndCollinearPoints(
            List<Vector2Int> points)
        {
            if (points == null || points.Count < 3)
                return;

            for (var index = points.Count - 1;
                 index >= 0;
                 index--)
            {
                var next =
                    (index + 1) % points.Count;

                if (points[index] == points[next] &&
                    points.Count > 3)
                {
                    points.RemoveAt(index);
                }
            }

            var changed = true;

            while (changed && points.Count > 3)
            {
                changed = false;

                for (var index = 0;
                     index < points.Count;
                     index++)
                {
                    var previous =
                        points[
                            (index - 1 + points.Count) %
                            points.Count];

                    var current =
                        points[index];

                    var next =
                        points[
                            (index + 1) %
                            points.Count];

                    var a = current - previous;
                    var b = next - current;

                    var cross =
                        a.x * b.y -
                        a.y * b.x;

                    if (cross != 0)
                        continue;

                    points.RemoveAt(index);
                    changed = true;
                    break;
                }
            }
        }

        private static void SimplifyClosedPixelLoop(
            List<Vector2Int> points,
            float tolerancePixels)
        {
            if (points == null ||
                points.Count <= 3 ||
                tolerancePixels <= 0f)
            {
                return;
            }

            var toleranceSquared =
                tolerancePixels *
                tolerancePixels;

            var changed = true;
            var guard = 0;
            var maximumGuard =
                points.Count * points.Count;

            while (changed &&
                   points.Count > 3 &&
                   guard < maximumGuard)
            {
                changed = false;

                for (var index = 0;
                     index < points.Count;
                     index++)
                {
                    var previous =
                        points[
                            (index - 1 + points.Count) %
                            points.Count];

                    var current =
                        points[index];

                    var next =
                        points[
                            (index + 1) %
                            points.Count];

                    if (DistanceToSegmentSquared(
                            current,
                            previous,
                            next) >
                        toleranceSquared)
                    {
                        continue;
                    }

                    points.RemoveAt(index);
                    changed = true;
                    break;
                }

                guard++;
            }
        }

        private static float DistanceToSegmentSquared(
            Vector2Int point,
            Vector2Int a,
            Vector2Int b)
        {
            var pointF =
                new Vector2(point.x, point.y);

            var aF =
                new Vector2(a.x, a.y);

            var bF =
                new Vector2(b.x, b.y);

            var ab = bF - aF;
            if (ab.sqrMagnitude <= 0.00001f)
                return (pointF - aF).sqrMagnitude;

            var t = Mathf.Clamp01(
                Vector2.Dot(
                    pointF - aF,
                    ab) /
                ab.sqrMagnitude);

            var closest =
                aF + ab * t;

            return
                (pointF - closest).sqrMagnitude;
        }

        private static bool ValidateManager(
            RoomManager manager)
        {
            if (manager.RoomPrefabs == null ||
                manager.RoomPrefabs.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Room Bake",
                    "Room Prefabs 목록이 비어 있습니다.",
                    "확인");
                return false;
            }

            var hasEnabled = false;

            for (var index = 0;
                 index < manager.RoomPrefabs.Count;
                 index++)
            {
                var placement =
                    manager.RoomPrefabs[index];

                if (placement == null ||
                    !placement.Enabled)
                {
                    continue;
                }

                if (placement.Prefab == null)
                {
                    EditorUtility.DisplayDialog(
                        "Room Bake",
                        $"Room Prefabs Element {index}의 Prefab이 비어 있습니다.",
                        "확인");
                    return false;
                }

                if (placement.Prefab.MapSprite == null)
                {
                    EditorUtility.DisplayDialog(
                        "Room Bake",
                        $"{placement.Prefab.name}: Map Sprite가 비어 있습니다.",
                        "확인");
                    return false;
                }

                var floorDefinition =
                    placement.Prefab.FloorDefinitionOverride != null
                        ? placement.Prefab.FloorDefinitionOverride
                        : manager.DefaultFloorDefinition;

                if (floorDefinition == null)
                {
                    EditorUtility.DisplayDialog(
                        "Room Bake",
                        $"{placement.Prefab.name}: Floor Definition이 없습니다.",
                        "확인");
                    return false;
                }

                if (floorDefinition.Kind != FieldPawnKind.Floor)
                {
                    EditorUtility.DisplayDialog(
                        "Room Bake",
                        $"{placement.Prefab.name}: Floor Definition의 Kind가 Floor가 아닙니다.",
                        "확인");
                    return false;
                }

                if (placement.Prefab.WallSprite != null)
                {
                    var obstacleDefinition =
                        placement.Prefab.ObstacleDefinitionOverride != null
                            ? placement.Prefab.ObstacleDefinitionOverride
                            : manager.DefaultObstacleDefinition;

                    if (obstacleDefinition == null)
                    {
                        EditorUtility.DisplayDialog(
                            "Room Bake",
                            $"{placement.Prefab.name}: Obstacle Definition이 없습니다.",
                            "확인");
                        return false;
                    }

                    if (obstacleDefinition.Kind != FieldPawnKind.Obstacle)
                    {
                        EditorUtility.DisplayDialog(
                            "Room Bake",
                            $"{placement.Prefab.name}: Obstacle Definition의 Kind가 Obstacle이 아닙니다.",
                            "확인");
                        return false;
                    }
                }

                hasEnabled = true;
            }

            if (hasEnabled &&
                HasAnyDoorLinks(manager))
            {
                if (manager.DoorDefinition == null)
                {
                    EditorUtility.DisplayDialog(
                        "Room Bake",
                        "Room Prefabs의 Door Links가 있지만 RoomManager의 Door Definition이 비어 있습니다.",
                        "확인");
                    return false;
                }

                if (!manager.DoorDefinition.IsDoor)
                {
                    EditorUtility.DisplayDialog(
                        "Room Bake",
                        "Door Definition은 Door 역할의 InteractivePawnDefinition이어야 합니다.",
                        "확인");
                    return false;
                }

                if (!ValidateDoorLinks(
                        manager,
                        out var doorError))
                {
                    EditorUtility.DisplayDialog(
                        "Room Bake",
                        doorError,
                        "확인");
                    return false;
                }
            }

            return hasEnabled;
        }

        private static bool HasAnyDoorLinks(
            RoomManager manager)
        {
            if (manager?.RoomPrefabs == null)
                return false;

            for (var index = 0;
                 index < manager.RoomPrefabs.Count;
                 index++)
            {
                var placement =
                    manager.RoomPrefabs[index];

                if (placement != null &&
                    placement.Enabled &&
                    placement.DoorLinks != null)
                {
                    for (var doorIndex = 0;
                         doorIndex < placement.DoorLinks.Count;
                         doorIndex++)
                    {
                        if (placement.DoorLinks[doorIndex] != null &&
                            placement.DoorLinks[doorIndex].Enabled)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool ValidateDoorLinks(
            RoomManager manager,
            out string error)
        {
            error = string.Empty;

            if (manager?.RoomPrefabs == null)
                return true;

            for (var placementIndex = 0;
                 placementIndex < manager.RoomPrefabs.Count;
                 placementIndex++)
            {
                var placement =
                    manager.RoomPrefabs[placementIndex];

                if (placement == null ||
                    !placement.Enabled ||
                    placement.DoorLinks == null)
                {
                    continue;
                }

                var ids =
                    new HashSet<string>(
                        StringComparer.Ordinal);

                for (var doorIndex = 0;
                     doorIndex < placement.DoorLinks.Count;
                     doorIndex++)
                {
                    var door =
                        placement.DoorLinks[doorIndex];

                    if (door == null ||
                        !door.Enabled)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(
                            door.DoorId))
                    {
                        error =
                            $"{placement.DisplayName}: Door Links {doorIndex + 1}의 내부 ID가 비어 있습니다.";
                        return false;
                    }

                    if (!ids.Add(door.DoorId))
                    {
                        error =
                            $"{placement.DisplayName}: Door ID가 중복되었습니다.";
                        return false;
                    }

                    if (door.RoomDoorColliderSize.x <= 0f ||
                        door.RoomDoorColliderSize.y <= 0f ||
                        door.ArriveDoorColliderSize.x <= 0f ||
                        door.ArriveDoorColliderSize.y <= 0f)
                    {
                        error =
                            $"{placement.DisplayName}/{door.DisplayName}: Collider Size는 0보다 커야 합니다.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static PawnNavMeshManager
            FindOrCreateNavMeshManager(RoomManager manager)
        {
            var existing =
                FindSceneObject<PawnNavMeshManager>();

            if (existing != null)
                return existing;

            var navigationObject =
                new GameObject("PawnNavMeshManager");

            Undo.RegisterCreatedObjectUndo(
                navigationObject,
                "Create PawnNavMeshManager");

            if (manager != null &&
                manager.gameObject.scene.IsValid() &&
                navigationObject.scene != manager.gameObject.scene)
            {
                UnityEngine.SceneManagement.SceneManager
                    .MoveGameObjectToScene(
                        navigationObject,
                        manager.gameObject.scene);
            }

            navigationObject.transform.position =
                Vector3.zero;
            navigationObject.transform.rotation =
                Quaternion.identity;
            navigationObject.transform.localScale =
                Vector3.one;

            var created =
                Undo.AddComponent<PawnNavMeshManager>(
                    navigationObject);

            Debug.Log(
                "[RoomManager] PawnNavMeshManager가 없어 Scene Root에 자동 생성했습니다. " +
                "NavMeshSurface / CollectSources2d는 RequireComponent로 함께 생성됩니다.",
                created);

            return created;
        }

        private static T FindSceneObject<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }

        private static int CountEnabledPlacements(
            IReadOnlyList<RoomPlacement> placements)
        {
            var count = 0;

            for (var index = 0;
                 index < placements.Count;
                 index++)
            {
                if (placements[index] != null &&
                    placements[index].Enabled &&
                    placements[index].Prefab != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static void ClearBaked(
            RoomManager manager)
        {
            if (manager == null)
                return;

            if (manager.BakedRoot != null)
            {
                Undo.DestroyObjectImmediate(
                    manager.BakedRoot.gameObject);
            }
            else
            {
                var existing =
                    manager.transform.Find(
                        "__BakedRooms");

                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(
                        existing.gameObject);
                }
            }

            Undo.RecordObject(
                manager,
                "Clear Baked Room Result");

            manager.EditorClearBakedResult();
            EditorUtility.SetDirty(manager);
        }
    }

    /// <summary>
    /// 리스트 변경만으로는 Bake하지 않습니다.
    /// Play 진입 직전(ExitingEditMode)에 BakeOnPlayStart가 켜진
    /// RoomManager만 딱 1회 Bake합니다.
    /// </summary>
    [InitializeOnLoad]
    internal static class RoomManagerPlayStartBake
    {
        private static bool _handlingPlayStart;

        static RoomManagerPlayStartBake()
        {
            EditorApplication.playModeStateChanged -=
                OnPlayModeStateChanged;

            EditorApplication.playModeStateChanged +=
                OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode ||
                _handlingPlayStart)
            {
                return;
            }

            _handlingPlayStart = true;

            try
            {
#if UNITY_2022_2_OR_NEWER
                var managers =
                    UnityEngine.Object.FindObjectsByType<RoomManager>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);
#else
                var managers =
                    UnityEngine.Object.FindObjectsOfType<RoomManager>(true);
#endif

                for (var index = 0;
                     index < managers.Length;
                     index++)
                {
                    var manager = managers[index];

                    if (manager == null ||
                        !manager.isActiveAndEnabled ||
                        !manager.BakeOnPlayStart)
                    {
                        continue;
                    }

                    RoomManagerEditor.BakeAll(manager);
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                _handlingPlayStart = false;
            }
        }
    }

    [InitializeOnLoad]
    internal static class RoomPrefabScenePainter
    {
        private static readonly List<Vector3> WorldPoints =
            new List<Vector3>();

        private static RoomPrefab _target;
        private static bool _isDrawing;

        static RoomPrefabScenePainter()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        public static void Begin(RoomPrefab target)
        {
            if (target == null)
                return;

            _target = target;
            WorldPoints.Clear();
            _isDrawing = true;
            Tools.current = Tool.None;
            SceneView.RepaintAll();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!_isDrawing || _target == null)
                return;

            var current = Event.current;
            if (current == null)
                return;

            var controlId =
                GUIUtility.GetControlID(
                    "TRPGRoomPrefabPainter".GetHashCode(),
                    FocusType.Passive);

            if (current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            DrawOverlay();
            DrawPreview();

            if (current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.Escape)
                {
                    Cancel();
                    current.Use();
                    return;
                }

                if (current.keyCode == KeyCode.Backspace)
                {
                    RemoveLast();
                    current.Use();
                    return;
                }

                if (current.keyCode == KeyCode.Return ||
                    current.keyCode == KeyCode.KeypadEnter)
                {
                    Finish();
                    current.Use();
                    return;
                }
            }

            if (current.type != EventType.MouseDown ||
                current.alt)
            {
                return;
            }

            if (current.button == 1)
            {
                RemoveLast();
                current.Use();
                return;
            }

            if (current.button != 0)
                return;

            if (!TryGetMouseWorld(
                    _target.transform.position.z,
                    out var world))
            {
                return;
            }

            WorldPoints.Add(world);
            current.Use();
            SceneView.RepaintAll();
        }

        private static void DrawOverlay()
        {
            Handles.BeginGUI();

            GUILayout.BeginArea(
                new Rect(12f, 12f, 340f, 112f),
                EditorStyles.helpBox);

            GUILayout.Label(
                "RoomPrefab · Fog Room Drawing",
                EditorStyles.boldLabel);

            GUILayout.Label(
                $"Points: {WorldPoints.Count} · " +
                "좌클릭 추가 / Enter 확정 / 우클릭·Backspace 취소 / Esc 종료",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.BeginHorizontal();

            GUI.enabled = WorldPoints.Count >= 3;
            if (GUILayout.Button("확정"))
                Finish();

            GUI.enabled = true;

            if (GUILayout.Button("마지막 점 취소"))
                RemoveLast();

            if (GUILayout.Button("종료"))
                Cancel();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            Handles.EndGUI();
        }

        private static void DrawPreview()
        {
            if (WorldPoints.Count == 0)
                return;

            Handles.color =
                new Color(1f, 0.75f, 0.1f, 1f);

            for (var index = 0;
                 index < WorldPoints.Count;
                 index++)
            {
                var world = WorldPoints[index];
                var size =
                    HandleUtility.GetHandleSize(world) *
                    0.065f;

                Handles.SphereHandleCap(
                    0,
                    world,
                    Quaternion.identity,
                    size,
                    EventType.Repaint);

                if (index > 0)
                {
                    Handles.DrawAAPolyLine(
                        4f,
                        WorldPoints[index - 1],
                        world);
                }
            }

            if (WorldPoints.Count >= 3)
            {
                Handles.DrawDottedLine(
                    WorldPoints[
                        WorldPoints.Count - 1],
                    WorldPoints[0],
                    4f);
            }
        }

        private static void Finish()
        {
            if (_target == null ||
                WorldPoints.Count < 3)
            {
                return;
            }

            var points =
                new List<Vector2>(
                    WorldPoints.Count);

            for (var index = 0;
                 index < WorldPoints.Count;
                 index++)
            {
                var local =
                    _target.transform.InverseTransformPoint(
                        WorldPoints[index]);

                points.Add(
                    new Vector2(local.x, local.y));
            }

            Undo.RecordObject(
                _target,
                "Add Fog Room");

            _target.EditorAddRoom(
                _target.EditorGenerateNextRoomId(),
                points);

            EditorUtility.SetDirty(_target);

            WorldPoints.Clear();
            _isDrawing = false;
            SceneView.RepaintAll();
        }

        private static void RemoveLast()
        {
            if (WorldPoints.Count == 0)
                return;

            WorldPoints.RemoveAt(
                WorldPoints.Count - 1);

            SceneView.RepaintAll();
        }

        private static void Cancel()
        {
            WorldPoints.Clear();
            _isDrawing = false;
            _target = null;
            SceneView.RepaintAll();
        }

        private static bool TryGetMouseWorld(
            float planeZ,
            out Vector3 world)
        {
            var ray = HandleUtility.GUIPointToWorldRay(
                Event.current.mousePosition);

            if (Mathf.Abs(ray.direction.z) <= 0.00001f)
            {
                world = default;
                return false;
            }

            var distance =
                (planeZ - ray.origin.z) /
                ray.direction.z;

            if (distance < 0f)
            {
                world = default;
                return false;
            }

            world =
                ray.origin +
                ray.direction * distance;
            world.z = planeZ;
            return true;
        }
    }
}
#endif
