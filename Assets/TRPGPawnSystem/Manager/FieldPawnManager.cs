using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Trpg.Pawns
{
    /// <summary>
    /// 기본 Y 정렬 이후, Layer Setter가 켜진 모든 FieldPawn의
    /// Sort Anchor Line을 기준으로 InteractivePawn의 앞/뒤 관계를 보정합니다.
    ///
    /// 단일 Anchor는 수평선, 다중 Anchor는 Pawn의 X 위치에서 보간된 선 높이를 사용합니다.
    /// Pawn이 선보다 아래면 FieldPawn 앞, 위면 FieldPawn 뒤가 되도록 보정합니다.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class FieldPawnManager : MonoBehaviour
    {
        [SerializeField] private PawnManager _pawnManager;


        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            var pawnManager = FindFirst<PawnManager>();
            if (pawnManager == null)
                return;

            var manager = pawnManager.GetComponent<FieldPawnManager>();
            if (manager == null)
            {
                manager = pawnManager.gameObject.AddComponent<
                    FieldPawnManager>();
            }

            manager.Configure(pawnManager);
        }

        public void Configure(PawnManager pawnManager)
        {
            _pawnManager = pawnManager;
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
            ClearAllPawnSortOverrides();
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

                RefreshPawnSorting(pawn);
            }
        }

        private void RefreshPawnSorting(InteractivePawn pawn)
        {
            if (pawn == null)
                return;

            // 점프 연출의 높이가 아니라 지면을 따라가는 Presentation 위치를 사용합니다.
            var pawnPosition = pawn.PresentationWorldPosition;

            FieldPawn nearestField = null;
            var nearestAnchorY = 0f;
            var nearestDistance = float.PositiveInfinity;

            foreach (var fieldPawn in FieldPawn.ActiveInstances)
            {
                if (fieldPawn == null ||
                    !fieldPawn.isActiveAndEnabled ||
                    !fieldPawn.UsesLayerSetter)
                {
                    continue;
                }

                if (!fieldPawn.TryGetSortAnchorWorldY(
                        pawnPosition.x,
                        out var anchorY))
                {
                    continue;
                }

                var distance = Mathf.Abs(pawnPosition.y - anchorY);
                if (distance >= nearestDistance)
                    continue;

                nearestField = fieldPawn;
                nearestAnchorY = anchorY;
                nearestDistance = distance;
            }

            if (nearestField == null)
            {
                pawn.SetWorldSortOverride(false, 0, 0, true);
                return;
            }

            var fieldOrder = nearestField.GetLayerSortOrder();
            var fieldSortingLayerId =
                nearestField.GetLayerSortingLayerId();

            // 화면 아래쪽(-Y)에 있는 Pawn은 건물 앞,
            // 위쪽(+Y)에 있는 Pawn은 건물 뒤입니다.
            var pawnInFront = pawnPosition.y <= nearestAnchorY;
            pawn.SetWorldSortOverride(
                true,
                fieldSortingLayerId,
                fieldOrder,
                pawnInFront);
        }

        private void ClearAllPawnSortOverrides()
        {
            if (_pawnManager == null)
                return;

            var pawns = _pawnManager.InteractivePawns;
            if (pawns == null)
                return;

            for (var index = 0; index < pawns.Count; index++)
            {
                var pawn = pawns[index];
                if (pawn != null)
                    pawn.SetWorldSortOverride(false, 0, 0, true);
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

#if UNITY_EDITOR
    /// <summary>
    /// FieldPawn의 Sort Anchor Line을 Scene View에서만 표시/편집합니다.
    /// Play Mode에서는 표시하지 않습니다.
    ///
    /// 파일은 Manager 폴더에 두되 UnityEditor 의존부 전체를
    /// UNITY_EDITOR 조건으로 감싸 Player Build에는 포함되지 않게 합니다.
    /// </summary>
    [CustomEditor(typeof(FieldPawn))]
    internal sealed class FieldPawnSceneInspector : UnityEditor.Editor
    {
        private SerializedProperty _layerSetter;
        private SerializedProperty _sortAnchorLocalY;
        private SerializedProperty _useMultiPointSortAnchor;
        private SerializedProperty _sortAnchorPoints;

        private void OnEnable()
        {
            _layerSetter = serializedObject.FindProperty("_layerSetter");
            _sortAnchorLocalY = serializedObject.FindProperty(
                "_sortAnchorLocalY");
            _useMultiPointSortAnchor = serializedObject.FindProperty(
                "_useMultiPointSortAnchor");
            _sortAnchorPoints = serializedObject.FindProperty(
                "_sortAnchorPoints");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(
                serializedObject,
                "_sortAnchorLocalY",
                "_useMultiPointSortAnchor",
                "_sortAnchorPoints");

            if (_layerSetter != null && _layerSetter.boolValue)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "Sort Anchor Line",
                    EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(
                    _useMultiPointSortAnchor,
                    new GUIContent("Multi Point Sort Anchor"));

                if (_useMultiPointSortAnchor.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Scene View에서 각 점을 드래그하면 선의 모양을 " +
                        "직접 조절할 수 있습니다. Pawn은 자신의 X 위치에서 " +
                        "이 선보다 아래면 건물 앞, 위면 건물 뒤로 정렬됩니다.",
                        MessageType.Info);

                    EditorGUILayout.PropertyField(
                        _sortAnchorPoints,
                        new GUIContent("Sort Anchor Points"),
                        true);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("포인트 추가"))
                            AddPoint();

                        if (GUILayout.Button("선 초기화"))
                            ResetPoints();
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(
                        _sortAnchorLocalY,
                        new GUIContent("Sort Anchor Local Y"));

                    EditorGUILayout.HelpBox(
                        "Scene View에서 파란 기준선 중앙의 점을 드래그해 " +
                        "건물의 앞/뒤 경계 높이를 조절합니다.",
                        MessageType.None);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (Application.isPlaying)
                return;

            var fieldPawn = target as FieldPawn;
            if (fieldPawn == null || !fieldPawn.UsesLayerSetter)
                return;

            serializedObject.Update();

            if (_useMultiPointSortAnchor != null &&
                _useMultiPointSortAnchor.boolValue)
            {
                DrawMultiPointAnchor(fieldPawn);
            }
            else
            {
                DrawSingleAnchor(fieldPawn);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSingleAnchor(FieldPawn fieldPawn)
        {
            if (!fieldPawn.TryGetSortLineLocalXRange(
                    out var localMinX,
                    out var localMaxX))
            {
                localMinX = -1f;
                localMaxX = 1f;
            }

            var localY = _sortAnchorLocalY.floatValue;
            var left = fieldPawn.transform.TransformPoint(
                new Vector3(localMinX, localY, 0f));
            var right = fieldPawn.transform.TransformPoint(
                new Vector3(localMaxX, localY, 0f));
            var center = (left + right) * 0.5f;

            Handles.color = new Color(0.15f, 0.75f, 1f, 1f);
            Handles.DrawLine(left, right, 3f);

            var size = HandleUtility.GetHandleSize(center) * 0.08f;
            EditorGUI.BeginChangeCheck();
            var moved = Handles.FreeMoveHandle(
                center,
                size,
                Vector3.zero,
                Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(fieldPawn, "Move Sort Anchor Line");
                var localMoved =
                    fieldPawn.transform.InverseTransformPoint(moved);
                _sortAnchorLocalY.floatValue = localMoved.y;
                serializedObject.ApplyModifiedProperties();
                fieldPawn.RefreshLayerLevel();
                EditorUtility.SetDirty(fieldPawn);
            }

            Handles.Label(
                center + Vector3.up * size * 1.8f,
                "SORT ANCHOR  |  위: 뒤 / 아래: 앞");
        }

        private void DrawMultiPointAnchor(FieldPawn fieldPawn)
        {
            if (_sortAnchorPoints == null ||
                _sortAnchorPoints.arraySize < 2)
            {
                return;
            }

            Handles.color = new Color(0.15f, 0.75f, 1f, 1f);

            var worldPoints = new Vector3[_sortAnchorPoints.arraySize];
            for (var index = 0;
                 index < _sortAnchorPoints.arraySize;
                 index++)
            {
                var local = _sortAnchorPoints
                    .GetArrayElementAtIndex(index)
                    .vector2Value;
                worldPoints[index] = fieldPawn.transform.TransformPoint(
                    new Vector3(local.x, local.y, 0f));
            }

            for (var index = 0; index < worldPoints.Length - 1; index++)
            {
                Handles.DrawLine(
                    worldPoints[index],
                    worldPoints[index + 1],
                    3f);
            }

            for (var index = 0;
                 index < _sortAnchorPoints.arraySize;
                 index++)
            {
                var pointProperty =
                    _sortAnchorPoints.GetArrayElementAtIndex(index);
                var world = worldPoints[index];
                var size = HandleUtility.GetHandleSize(world) * 0.07f;

                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(
                    world,
                    size,
                    Vector3.zero,
                    Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(fieldPawn, "Move Sort Anchor Point");
                    var localMoved =
                        fieldPawn.transform.InverseTransformPoint(moved);
                    pointProperty.vector2Value = new Vector2(
                        localMoved.x,
                        localMoved.y);
                    serializedObject.ApplyModifiedProperties();
                    fieldPawn.RefreshLayerLevel();
                    EditorUtility.SetDirty(fieldPawn);
                    serializedObject.Update();
                }

                Handles.Label(
                    world + Vector3.up * size * 1.7f,
                    $"P{index}");
            }

            var labelPoint = worldPoints[worldPoints.Length / 2];
            var labelSize = HandleUtility.GetHandleSize(labelPoint) * 0.08f;
            Handles.Label(
                labelPoint + Vector3.up * labelSize * 2.8f,
                "MULTI SORT ANCHOR  |  위: 뒤 / 아래: 앞");
        }

        private void AddPoint()
        {
            if (_sortAnchorPoints == null)
                return;

            serializedObject.Update();

            if (_sortAnchorPoints.arraySize < 2)
            {
                ResetPoints();
                return;
            }

            var bestIndex = 0;
            var bestWidth = float.NegativeInfinity;
            for (var index = 0;
                 index < _sortAnchorPoints.arraySize - 1;
                 index++)
            {
                var a = _sortAnchorPoints
                    .GetArrayElementAtIndex(index)
                    .vector2Value;
                var b = _sortAnchorPoints
                    .GetArrayElementAtIndex(index + 1)
                    .vector2Value;
                var width = Mathf.Abs(b.x - a.x);
                if (width <= bestWidth)
                    continue;

                bestWidth = width;
                bestIndex = index;
            }

            var left = _sortAnchorPoints
                .GetArrayElementAtIndex(bestIndex)
                .vector2Value;
            var right = _sortAnchorPoints
                .GetArrayElementAtIndex(bestIndex + 1)
                .vector2Value;
            var middle = Vector2.Lerp(left, right, 0.5f);

            Undo.RecordObject(target, "Add Sort Anchor Point");
            _sortAnchorPoints.InsertArrayElementAtIndex(bestIndex + 1);
            _sortAnchorPoints
                .GetArrayElementAtIndex(bestIndex + 1)
                .vector2Value = middle;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private void ResetPoints()
        {
            var fieldPawn = target as FieldPawn;
            if (fieldPawn == null || _sortAnchorPoints == null)
                return;

            if (!fieldPawn.TryGetSortLineLocalXRange(
                    out var localMinX,
                    out var localMaxX))
            {
                localMinX = -1f;
                localMaxX = 1f;
            }

            var y = _sortAnchorLocalY != null
                ? _sortAnchorLocalY.floatValue
                : 0f;
            var centerX = (localMinX + localMaxX) * 0.5f;

            Undo.RecordObject(fieldPawn, "Reset Sort Anchor Points");
            _sortAnchorPoints.ClearArray();
            _sortAnchorPoints.arraySize = 3;
            _sortAnchorPoints.GetArrayElementAtIndex(0).vector2Value =
                new Vector2(localMinX, y);
            _sortAnchorPoints.GetArrayElementAtIndex(1).vector2Value =
                new Vector2(centerX, y);
            _sortAnchorPoints.GetArrayElementAtIndex(2).vector2Value =
                new Vector2(localMaxX, y);
            serializedObject.ApplyModifiedProperties();
            fieldPawn.RefreshLayerLevel();
            EditorUtility.SetDirty(fieldPawn);
        }
    }
#endif
}
