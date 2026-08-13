using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    [DisallowMultipleComponent]
    public sealed class PawnDoorManager : MonoBehaviour
    {
        private readonly List<InteractivePawn> _interactivePawns =
            new List<InteractivePawn>();

        private readonly HashSet<InteractivePawn> _registeredPawns =
            new HashSet<InteractivePawn>();

        private readonly Dictionary<string, InteractivePawn> _doorsById =
            new Dictionary<string, InteractivePawn>(
                StringComparer.Ordinal);

        private readonly HashSet<InteractivePawn> _doorGuards =
            new HashSet<InteractivePawn>();

        [Header("Door Interaction Icon")]
        [SerializeField, Min(0.1f), Tooltip(
            "선택된/활성 Pawn이 Door Collider 외곽에서 이 거리(m) 안으로 접근하면 " +
            "Door 아이콘을 표시합니다.")]
        private float _doorInteractionRangeMeters = 1.25f;

        [SerializeField, Tooltip(
            "Door 아이콘에 사용할 Sprite. 비워두면 단순 Door 모양의 런타임 아이콘을 사용합니다.")]
        private Sprite _doorIconSprite;

        [SerializeField, Min(24f)]
        private float _doorIconSizePixels = 54f;

        [SerializeField]
        private Vector2 _doorIconScreenOffset =
            new Vector2(0f, 16f);

        [Header("Door Confirmation")]
        [SerializeField]
        private string _doorPromptMessage =
            "문을 여시겠습니까?";

        [SerializeField]
        private string _confirmButtonLabel = "예";

        [SerializeField]
        private string _cancelButtonLabel = "아니오";

        [SerializeField, Min(240f)]
        private float _promptWidth = 380f;

        [SerializeField, Min(120f)]
        private float _promptHeight = 180f;

        [Header("Door Landing")]
        [SerializeField, Min(0.02f), Tooltip(
            "Destination Door Collider 바로 바깥에서 확보할 최소 간격(m). " +
            "Door에서 멀리 떨어뜨리는 값이 아니라 Trigger 밖으로만 빼기 위한 값입니다.")]
        private float _doorLandingClearanceMeters = 0.10f;

        [SerializeField, Min(0.1f), Tooltip(
            "Door 가장자리에서 바깥쪽으로 Walkable NavMesh를 찾을 최대 거리(m). " +
            "기본값은 가까운 출입구만 찾도록 1.5m로 제한합니다.")]
        private float _doorLandingSearchMaxMeters = 1.5f;

        [SerializeField, Min(0.02f), Tooltip(
            "Door 가장자리에서 바깥쪽으로 전진하며 검사하는 간격(m). " +
            "작을수록 Door 바로 앞의 NavMesh를 더 정확히 찾습니다.")]
        private float _doorLandingSearchStepMeters = 0.10f;

        [SerializeField, Range(3, 9), Tooltip(
            "BoxCollider2D 각 변에서 검사할 지점 수. " +
            "중앙뿐 아니라 넓은 Door의 좌우 끝부분도 함께 검사합니다.")]
        private int _doorLandingEdgeSamples = 5;

        [SerializeField, Min(0.02f), Tooltip(
            "각 Door 가장자리 Probe에서 NavMesh로 스냅할 작은 반경(m). " +
            "멀리 있는 NavMesh를 끌어오지 않도록 작게 유지합니다.")]
        private float _doorLandingSampleRadiusMeters = 0.12f;

        [SerializeField, Min(0f), Tooltip(
            "다른 이동 Pawn의 Collider와 이 거리보다 가까운 후보는 착지점에서 제외합니다.")]
        private float _doorLandingPawnClearanceMeters = 0.20f;

        [SerializeField, Min(0f), Tooltip(
            "Obstacle FieldPawn과 이 거리보다 가까운 후보는 제외합니다. " +
            "NavMesh 경계 바로 옆에서 Pawn Collider가 벽에 겹치는 문제를 줄입니다.")]
        private float _doorLandingObstacleClearanceMeters = 0.08f;

        [SerializeField, Tooltip(
            "가까운 Walkable 지점을 찾지 못했을 때 NavMesh를 한 번 Rebuild하고 다시 탐색합니다.")]
        private bool _rebuildNavMeshOnLandingFailure = true;

        private PawnSystemSettings _settings;
        private PawnNavMeshManager _navMeshManager;
        private PawnManager _pawnManager;
        private Camera _boardCamera;

        private InteractivePawn _iconDoor;
        private InteractivePawn _iconMoveable;
        private InteractivePawn _iconDestinationDoor;

        private InteractivePawn _pendingSourceDoor;
        private InteractivePawn _pendingMoveable;
        private InteractivePawn _pendingDestinationDoor;

        private GameObject _ownedDoorCanvas;
        private RectTransform _doorCanvasRect;
        private Button _doorIconButton;
        private RectTransform _doorIconRect;
        private Image _customDoorIconImage;
        private GameObject _fallbackDoorGlyph;

        private GUIStyle _promptTitleStyle;
        private GUIStyle _promptDoorStyle;

        public event Action<InteractivePawn, Vector2>
            TransferResolved;

        public void Bind(
            IReadOnlyList<InteractivePawn> interactivePawns,
            PawnSystemSettings settings,
            PawnNavMeshManager navMeshManager)
        {
            Unbind();

            _settings = settings;
            _navMeshManager = navMeshManager;
            ResolveViewReferences();

            if (interactivePawns != null)
            {
                for (var index = 0;
                     index < interactivePawns.Count;
                     index++)
                {
                    RegisterPawn(
                        interactivePawns[index]);
                }
            }

            // Room Bake Door는 PawnRoot 밖에 있을 수 있으므로 Scene 전체에서 추가 등록합니다.
            RegisterSceneDoors();
            EnsureDoorIconUi();
            HideDoorIcon();
        }

        public void Unbind()
        {
            StopAllCoroutines();

            ClearPendingDoorRequest(true);
            ClearDoorIconCandidate();

            _interactivePawns.Clear();
            _registeredPawns.Clear();
            _doorsById.Clear();
            _doorGuards.Clear();

            _settings = null;
            _navMeshManager = null;
        }

        [ContextMenu("Refresh Scene Doors")]
        public void RefreshSceneDoors()
        {
            RegisterSceneDoors();
        }

        private void Update()
        {
            RefreshDoorIconCandidate();
        }

        private void RegisterSceneDoors()
        {
            var scenePawns =
                FindObjectsByType<InteractivePawn>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            for (var index = 0;
                 index < scenePawns.Length;
                 index++)
            {
                var pawn = scenePawns[index];

                if (pawn == null ||
                    !pawn.IsDoor)
                {
                    continue;
                }

                RegisterPawn(pawn);
            }
        }

        private void RegisterPawn(
            InteractivePawn pawn)
        {
            if (pawn == null ||
                !_registeredPawns.Add(pawn))
            {
                return;
            }

            _interactivePawns.Add(pawn);
            IndexDoor(pawn);
        }

        private void IndexDoor(
            InteractivePawn pawn)
        {
            if (pawn == null ||
                !pawn.IsDoor ||
                string.IsNullOrWhiteSpace(
                    pawn.InstanceId))
            {
                return;
            }

            if (_doorsById.TryGetValue(
                    pawn.InstanceId,
                    out var existing))
            {
                if (existing == pawn)
                    return;

                Debug.LogError(
                    $"Door Instance Id '{pawn.InstanceId}'가 중복되었습니다.",
                    pawn);
                return;
            }

            _doorsById.Add(
                pawn.InstanceId,
                pawn);
        }

        private void RefreshDoorIconCandidate()
        {
            if (_pendingMoveable != null)
            {
                HideDoorIcon();
                return;
            }

            ResolveViewReferences();

            var activePawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;

            if (activePawn == null ||
                !activePawn.IsMoveable ||
                _doorGuards.Contains(activePawn))
            {
                ClearDoorIconCandidate();
                return;
            }

            InteractivePawn bestDoor = null;
            InteractivePawn bestDestination = null;
            var bestDistance = float.PositiveInfinity;
            var pawnPosition =
                activePawn.WorldPosition;

            for (var index = 0;
                 index < _interactivePawns.Count;
                 index++)
            {
                var door = _interactivePawns[index];
                if (door == null ||
                    !door.IsDoor ||
                    !door.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!TryResolveDestinationDoor(
                        door,
                        activePawn,
                        out var destinationDoor))
                {
                    continue;
                }

                var distance = GetDistanceToDoor(
                    door,
                    pawnPosition);

                if (distance >
                        Mathf.Max(
                            0.1f,
                            _doorInteractionRangeMeters) ||
                    distance >= bestDistance)
                {
                    continue;
                }

                bestDoor = door;
                bestDestination = destinationDoor;
                bestDistance = distance;
            }

            if (bestDoor == null)
            {
                ClearDoorIconCandidate();
                return;
            }

            _iconDoor = bestDoor;
            _iconMoveable = activePawn;
            _iconDestinationDoor = bestDestination;

            UpdateDoorIconPosition();
        }

        private void UpdateDoorIconPosition()
        {
            if (_iconDoor == null ||
                _iconMoveable == null ||
                _iconDestinationDoor == null)
            {
                HideDoorIcon();
                return;
            }

            EnsureDoorIconUi();
            ResolveViewReferences();

            if (_doorIconButton == null ||
                _doorIconRect == null ||
                _doorCanvasRect == null ||
                _boardCamera == null)
            {
                HideDoorIcon();
                return;
            }

            var worldCenter = GetDoorWorldCenter(_iconDoor);
            var screenPoint3 =
                _boardCamera.WorldToScreenPoint(worldCenter);

            if (screenPoint3.z <= 0f ||
                screenPoint3.x < -32f ||
                screenPoint3.y < -32f ||
                screenPoint3.x > Screen.width + 32f ||
                screenPoint3.y > Screen.height + 32f)
            {
                HideDoorIcon();
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _doorCanvasRect,
                    new Vector2(
                        screenPoint3.x,
                        screenPoint3.y),
                    null,
                    out var localPoint))
            {
                HideDoorIcon();
                return;
            }

            _doorIconRect.anchoredPosition =
                localPoint + _doorIconScreenOffset;

            _doorIconButton.gameObject.SetActive(true);
        }

        private void HandleDoorIconClicked()
        {
            if (_pendingMoveable != null ||
                _iconDoor == null ||
                _iconMoveable == null)
            {
                return;
            }

            var pawnPosition =
                _iconMoveable.WorldPosition;

            if (GetDistanceToDoor(
                    _iconDoor,
                    pawnPosition) >
                Mathf.Max(
                    0.1f,
                    _doorInteractionRangeMeters))
            {
                ClearDoorIconCandidate();
                return;
            }

            if (!TryResolveDestinationDoor(
                    _iconDoor,
                    _iconMoveable,
                    out var destinationDoor))
            {
                ClearDoorIconCandidate();
                return;
            }

            _doorGuards.Add(_iconMoveable);

            _pendingSourceDoor = _iconDoor;
            _pendingMoveable = _iconMoveable;
            _pendingDestinationDoor = destinationDoor;

            HideDoorIcon();
        }

        private static float GetDistanceToDoor(
            InteractivePawn door,
            Vector2 worldPosition)
        {
            if (door == null)
                return float.PositiveInfinity;

            var colliders =
                door.GetComponentsInChildren<Collider2D>(true);

            var best = float.PositiveInfinity;
            for (var index = 0;
                 index < colliders.Length;
                 index++)
            {
                var collider = colliders[index];
                if (collider == null ||
                    !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var closest = collider.ClosestPoint(worldPosition);
                best = Mathf.Min(
                    best,
                    Vector2.Distance(
                        worldPosition,
                        closest));
            }

            if (!float.IsPositiveInfinity(best))
                return best;

            return Vector2.Distance(
                worldPosition,
                door.transform.position);
        }

        private static Vector3 GetDoorWorldCenter(
            InteractivePawn door)
        {
            if (door == null)
                return Vector3.zero;

            var colliders =
                door.GetComponentsInChildren<Collider2D>(true);

            Collider2D fallback = null;
            for (var index = 0;
                 index < colliders.Length;
                 index++)
            {
                var collider = colliders[index];
                if (collider == null ||
                    !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (fallback == null)
                    fallback = collider;

                if (collider.isTrigger)
                    return collider.bounds.center;
            }

            return fallback != null
                ? fallback.bounds.center
                : door.transform.position;
        }

        private void ResolveViewReferences()
        {
            if (_pawnManager == null)
            {
                _pawnManager =
                    FindFirstObjectByType<PawnManager>(
                        FindObjectsInactive.Include);
            }

            if (_boardCamera == null &&
                _pawnManager != null)
            {
                _boardCamera = _pawnManager.BoardCamera;
            }

            if (_boardCamera == null)
                _boardCamera = Camera.main;
        }

        private void EnsureDoorIconUi()
        {
            if (_ownedDoorCanvas != null &&
                _doorIconButton != null &&
                _doorIconRect != null)
            {
                RefreshDoorIconGraphic();
                return;
            }

            _ownedDoorCanvas =
                new GameObject(
                    "DoorInteractionCanvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));

            _ownedDoorCanvas.transform.SetParent(
                transform,
                false);

            var canvas =
                _ownedDoorCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            var scaler =
                _ownedDoorCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _doorCanvasRect =
                _ownedDoorCanvas.GetComponent<RectTransform>();

            var buttonObject =
                new GameObject(
                    "DoorIconButton",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Button));

            buttonObject.transform.SetParent(
                _ownedDoorCanvas.transform,
                false);

            _doorIconRect =
                buttonObject.GetComponent<RectTransform>();
            _doorIconRect.anchorMin =
                new Vector2(0.5f, 0.5f);
            _doorIconRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            _doorIconRect.pivot =
                new Vector2(0.5f, 0.5f);

            var size =
                Mathf.Max(24f, _doorIconSizePixels);
            _doorIconRect.sizeDelta =
                new Vector2(size, size);

            var background =
                buttonObject.GetComponent<Image>();
            background.color =
                new Color(0.08f, 0.08f, 0.08f, 0.92f);

            _doorIconButton =
                buttonObject.GetComponent<Button>();
            _doorIconButton.targetGraphic = background;
            _doorIconButton.onClick.AddListener(
                HandleDoorIconClicked);

            var customIconObject =
                new GameObject(
                    "CustomDoorIcon",
                    typeof(RectTransform),
                    typeof(Image));
            customIconObject.transform.SetParent(
                buttonObject.transform,
                false);

            var customRect =
                customIconObject.GetComponent<RectTransform>();
            customRect.anchorMin =
                new Vector2(0.5f, 0.5f);
            customRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            customRect.pivot =
                new Vector2(0.5f, 0.5f);
            customRect.sizeDelta =
                new Vector2(size * 0.68f, size * 0.68f);

            _customDoorIconImage =
                customIconObject.GetComponent<Image>();
            _customDoorIconImage.raycastTarget = false;
            _customDoorIconImage.preserveAspect = true;

            _fallbackDoorGlyph =
                CreateFallbackDoorGlyph(
                    buttonObject.transform,
                    size);

            RefreshDoorIconGraphic();
            buttonObject.SetActive(false);
        }

        private static GameObject CreateFallbackDoorGlyph(
            Transform parent,
            float buttonSize)
        {
            var doorObject =
                new GameObject(
                    "FallbackDoorGlyph",
                    typeof(RectTransform),
                    typeof(Image));
            doorObject.transform.SetParent(parent, false);

            var doorRect =
                doorObject.GetComponent<RectTransform>();
            doorRect.anchorMin =
                new Vector2(0.5f, 0.5f);
            doorRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            doorRect.pivot =
                new Vector2(0.5f, 0.5f);
            doorRect.sizeDelta =
                new Vector2(
                    buttonSize * 0.42f,
                    buttonSize * 0.58f);

            var doorImage =
                doorObject.GetComponent<Image>();
            doorImage.color = Color.white;
            doorImage.raycastTarget = false;

            var knobObject =
                new GameObject(
                    "Knob",
                    typeof(RectTransform),
                    typeof(Image));
            knobObject.transform.SetParent(
                doorObject.transform,
                false);

            var knobRect =
                knobObject.GetComponent<RectTransform>();
            knobRect.anchorMin =
                new Vector2(0.76f, 0.5f);
            knobRect.anchorMax =
                new Vector2(0.76f, 0.5f);
            knobRect.pivot =
                new Vector2(0.5f, 0.5f);
            knobRect.sizeDelta =
                new Vector2(
                    Mathf.Max(3f, buttonSize * 0.08f),
                    Mathf.Max(3f, buttonSize * 0.08f));

            var knobImage =
                knobObject.GetComponent<Image>();
            knobImage.color =
                new Color(0.08f, 0.08f, 0.08f, 1f);
            knobImage.raycastTarget = false;

            return doorObject;
        }

        private void RefreshDoorIconGraphic()
        {
            if (_doorIconRect != null)
            {
                var size =
                    Mathf.Max(24f, _doorIconSizePixels);
                _doorIconRect.sizeDelta =
                    new Vector2(size, size);
            }

            if (_customDoorIconImage != null)
            {
                _customDoorIconImage.sprite =
                    _doorIconSprite;
                _customDoorIconImage.gameObject.SetActive(
                    _doorIconSprite != null);
            }

            if (_fallbackDoorGlyph != null)
            {
                _fallbackDoorGlyph.SetActive(
                    _doorIconSprite == null);
            }
        }

        private void ClearDoorIconCandidate()
        {
            _iconDoor = null;
            _iconMoveable = null;
            _iconDestinationDoor = null;
            HideDoorIcon();
        }

        private void HideDoorIcon()
        {
            if (_doorIconButton != null)
                _doorIconButton.gameObject.SetActive(false);
        }

        private void OnGUI()
        {
            if (_pendingMoveable == null ||
                _pendingSourceDoor == null ||
                _pendingDestinationDoor == null)
            {
                return;
            }

            EnsureGuiStyles();

            var width =
                Mathf.Min(
                    Mathf.Max(
                        240f,
                        _promptWidth),
                    Mathf.Max(
                        240f,
                        Screen.width - 32f));

            var height =
                Mathf.Min(
                    Mathf.Max(
                        120f,
                        _promptHeight),
                    Mathf.Max(
                        120f,
                        Screen.height - 32f));

            var rect =
                new Rect(
                    (Screen.width - width) * 0.5f,
                    (Screen.height - height) * 0.5f,
                    width,
                    height);

            var previousDepth =
                GUI.depth;

            GUI.depth = -10000;

            GUI.Box(
                rect,
                GUIContent.none);

            GUILayout.BeginArea(rect);

            GUILayout.Space(22f);

            GUILayout.Label(
                string.IsNullOrWhiteSpace(
                    _doorPromptMessage)
                    ? "문을 여시겠습니까?"
                    : _doorPromptMessage,
                _promptTitleStyle);

            GUILayout.Space(8f);

            GUILayout.Label(
                BuildDoorRouteLabel(),
                _promptDoorStyle);

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();

            GUILayout.Space(24f);

            if (GUILayout.Button(
                    string.IsNullOrWhiteSpace(
                        _confirmButtonLabel)
                        ? "예"
                        : _confirmButtonLabel,
                    GUILayout.Height(42f)))
            {
                ConfirmPendingDoor();
            }

            GUILayout.Space(12f);

            if (GUILayout.Button(
                    string.IsNullOrWhiteSpace(
                        _cancelButtonLabel)
                        ? "아니오"
                        : _cancelButtonLabel,
                    GUILayout.Height(42f)))
            {
                CancelPendingDoor();
            }

            GUILayout.Space(24f);

            GUILayout.EndHorizontal();

            GUILayout.Space(18f);

            GUILayout.EndArea();

            GUI.depth =
                previousDepth;
        }

        private string BuildDoorRouteLabel()
        {
            if (_pendingSourceDoor == null ||
                _pendingDestinationDoor == null)
            {
                return string.Empty;
            }

            return
                $"{_pendingSourceDoor.name}  →  " +
                $"{_pendingDestinationDoor.name}";
        }

        private void EnsureGuiStyles()
        {
            if (_promptTitleStyle == null)
            {
                _promptTitleStyle =
                    new GUIStyle(
                        GUI.skin.label)
                    {
                        alignment =
                            TextAnchor.MiddleCenter,
                        fontSize = 20,
                        fontStyle =
                            FontStyle.Bold,
                        wordWrap = true
                    };
            }

            if (_promptDoorStyle == null)
            {
                _promptDoorStyle =
                    new GUIStyle(
                        GUI.skin.label)
                    {
                        alignment =
                            TextAnchor.MiddleCenter,
                        fontSize = 13,
                        wordWrap = true
                    };
            }
        }

        private void ConfirmPendingDoor()
        {
            var moveable =
                _pendingMoveable;

            var destinationDoor =
                _pendingDestinationDoor;

            if (moveable == null ||
                destinationDoor == null ||
                _settings == null ||
                _navMeshManager == null)
            {
                CancelPendingDoor();
                return;
            }

            if (!TryResolveSafeDoorLanding(
                    destinationDoor,
                    moveable,
                    out var destination))
            {
                if (_rebuildNavMeshOnLandingFailure)
                {
                    Debug.LogWarning(
                        $"[{destinationDoor.name}] Door 주변 Walkable NavMesh를 찾지 못해 " +
                        "NavMesh를 1회 Rebuild한 뒤 다시 탐색합니다.",
                        destinationDoor);

                    _navMeshManager.Rebuild();
                }

                if (!TryResolveSafeDoorLanding(
                        destinationDoor,
                        moveable,
                        out destination))
                {
                    Debug.LogError(
                        $"[{destinationDoor.name}] Door 주변 " +
                        $"{Mathf.Max(0.1f, _doorLandingSearchMaxMeters):0.##}m 안에 " +
                        "안전한 Walkable NavMesh 착지점을 찾지 못했습니다. " +
                        "Door를 Walkable 영역 가까이에 배치했는지 확인하십시오.",
                        destinationDoor);

                    _doorGuards.Remove(
                        moveable);

                    ClearPendingDoorRequest(
                        false);

                    return;
                }
            }

            TransferResolved?.Invoke(
                moveable,
                destination);

            var guardSeconds =
                Mathf.Max(
                    0f,
                    _settings.DoorGuardSeconds);

            ClearPendingDoorRequest(
                false);

            StartCoroutine(
                ReleaseDoorGuard(
                    moveable,
                    guardSeconds));
        }

        private bool TryResolveSafeDoorLanding(
            InteractivePawn destinationDoor,
            InteractivePawn moveable,
            out Vector2 destination)
        {
            destination = default;

            if (destinationDoor == null ||
                _navMeshManager == null)
            {
                return false;
            }

            Physics2D.SyncTransforms();

            var clearance =
                Mathf.Max(
                    0.02f,
                    _doorLandingClearanceMeters);

            var sampleRadius =
                Mathf.Max(
                    0.02f,
                    _doorLandingSampleRadiusMeters);

            var maximumSearch =
                Mathf.Max(
                    clearance,
                    _doorLandingSearchMaxMeters);

            var step =
                Mathf.Max(
                    0.02f,
                    _doorLandingSearchStepMeters);

            // ArrivalPoint가 이미 Door Trigger 바깥의 아주 가까운 Walkable이라면
            // 사용자가 의도적으로 만든 지점일 수 있으므로 우선 허용합니다.
            // 단, Door에서 Search Max보다 멀면 사용하지 않습니다.
            var arrival =
                destinationDoor.ArrivalPosition;

            if (GetDistanceToDoor(
                    destinationDoor,
                    arrival) <= maximumSearch &&
                TryAcceptLandingCandidate(
                    arrival,
                    destinationDoor,
                    moveable,
                    sampleRadius,
                    clearance,
                    out destination))
            {
                return true;
            }

            var primaryCollider =
                GetPrimaryDoorCollider(
                    destinationDoor);

            if (primaryCollider is BoxCollider2D box)
            {
                // 핵심:
                // Door 중심에서 큰 Ring을 만드는 대신,
                // 실제 BoxCollider의 네 변 "바로 바깥"부터 0.1m씩 전진합니다.
                // 첫 번째로 Walkable 후보가 발견되는 거리층에서 끝내므로
                // Door 바로 앞 NavMesh가 있으면 그 지점이 항상 우선됩니다.
                return TryResolveNearestBoxEdgeLanding(
                    box,
                    destinationDoor,
                    moveable,
                    clearance,
                    step,
                    maximumSearch,
                    sampleRadius,
                    out destination);
            }

            // 비-Box Collider가 들어오더라도 먼 Ring 대신
            // Collider ClosestPoint 기반의 짧은 방향 Probe만 사용합니다.
            return TryResolveNearestGenericLanding(
                destinationDoor,
                moveable,
                clearance,
                step,
                maximumSearch,
                sampleRadius,
                out destination);
        }

        private bool TryResolveNearestBoxEdgeLanding(
            BoxCollider2D box,
            InteractivePawn destinationDoor,
            InteractivePawn moveable,
            float clearance,
            float step,
            float maximumSearch,
            float sampleRadius,
            out Vector2 destination)
        {
            destination = default;

            if (box == null)
                return false;

            var boxTransform =
                box.transform;

            var center =
                (Vector2)boxTransform.TransformPoint(
                    box.offset);

            var scale =
                boxTransform.lossyScale;

            var halfWidth =
                Mathf.Abs(
                    box.size.x *
                    scale.x) *
                0.5f;

            var halfHeight =
                Mathf.Abs(
                    box.size.y *
                    scale.y) *
                0.5f;

            var right =
                (Vector2)boxTransform.right;

            var up =
                (Vector2)boxTransform.up;

            if (right.sqrMagnitude <= 0.0001f)
                right = Vector2.right;
            else
                right.Normalize();

            if (up.sqrMagnitude <= 0.0001f)
                up = Vector2.up;
            else
                up.Normalize();

            var sampleCount =
                Mathf.Clamp(
                    _doorLandingEdgeSamples,
                    3,
                    9);

            // 홀수 개를 유지해 각 변 중앙이 반드시 후보가 되게 합니다.
            if ((sampleCount & 1) == 0)
                sampleCount++;

            for (var outside = clearance;
                 outside <= maximumSearch + 0.0001f;
                 outside += step)
            {
                var found =
                    false;

                var best =
                    Vector2.zero;

                var bestScore =
                    float.PositiveInfinity;

                // 0: Door의 +Up 방향(기본 Front)
                // 1: -Up
                // 2: +Right
                // 3: -Right
                //
                // 같은 거리층에서 둘 다 Walkable이면 +Up을 약간 우선하되,
                // 실제 거리 차이가 있으면 더 가까운 후보가 이깁니다.
                EvaluateBoxEdge(
                    center,
                    up,
                    right,
                    halfHeight,
                    halfWidth,
                    outside,
                    0,
                    sampleCount,
                    destinationDoor,
                    moveable,
                    sampleRadius,
                    clearance,
                    ref found,
                    ref best,
                    ref bestScore);

                EvaluateBoxEdge(
                    center,
                    -up,
                    right,
                    halfHeight,
                    halfWidth,
                    outside,
                    1,
                    sampleCount,
                    destinationDoor,
                    moveable,
                    sampleRadius,
                    clearance,
                    ref found,
                    ref best,
                    ref bestScore);

                EvaluateBoxEdge(
                    center,
                    right,
                    up,
                    halfWidth,
                    halfHeight,
                    outside,
                    2,
                    sampleCount,
                    destinationDoor,
                    moveable,
                    sampleRadius,
                    clearance,
                    ref found,
                    ref best,
                    ref bestScore);

                EvaluateBoxEdge(
                    center,
                    -right,
                    up,
                    halfWidth,
                    halfHeight,
                    outside,
                    3,
                    sampleCount,
                    destinationDoor,
                    moveable,
                    sampleRadius,
                    clearance,
                    ref found,
                    ref best,
                    ref bestScore);

                // 가장 가까운 거리층에서 하나라도 발견되면 즉시 종료합니다.
                // 더 먼 1m/1.5m 후보는 아예 보지 않습니다.
                if (found)
                {
                    destination = best;
                    return true;
                }
            }

            return false;
        }

        private void EvaluateBoxEdge(
            Vector2 center,
            Vector2 outward,
            Vector2 tangent,
            float halfNormalExtent,
            float halfTangentExtent,
            float outside,
            int sidePriority,
            int sampleCount,
            InteractivePawn destinationDoor,
            InteractivePawn moveable,
            float sampleRadius,
            float clearance,
            ref bool found,
            ref Vector2 best,
            ref float bestScore)
        {
            var edgeCenter =
                center +
                outward *
                (
                    halfNormalExtent +
                    outside
                );

            for (var sampleIndex = 0;
                 sampleIndex < sampleCount;
                 sampleIndex++)
            {
                var normalized =
                    sampleCount <= 1
                        ? 0f
                        : Mathf.Lerp(
                            -1f,
                            1f,
                            sampleIndex /
                            (float)(sampleCount - 1));

                var query =
                    edgeCenter +
                    tangent *
                    (
                        halfTangentExtent *
                        normalized
                    );

                if (!TryAcceptLandingCandidate(
                        query,
                        destinationDoor,
                        moveable,
                        sampleRadius,
                        clearance,
                        out var candidate))
                {
                    continue;
                }

                // 첫 기준은 실제 Door Collider에서의 거리.
                // 그 다음은 Query와 Projection 결과의 오차.
                // 같은 조건이면 +Up(Front) -> -Up -> Right -> Left 순으로 아주 약하게 우선.
                var doorDistance =
                    GetDistanceToDoor(
                        destinationDoor,
                        candidate);

                var projectionError =
                    Vector2.Distance(
                        query,
                        candidate);

                var centerBias =
                    Mathf.Abs(normalized) *
                    0.001f;

                var sideBias =
                    sidePriority *
                    0.0001f;

                var score =
                    doorDistance +
                    projectionError * 0.10f +
                    centerBias +
                    sideBias;

                if (found &&
                    score >= bestScore)
                {
                    continue;
                }

                found = true;
                bestScore = score;
                best = candidate;
            }
        }

        private bool TryResolveNearestGenericLanding(
            InteractivePawn destinationDoor,
            InteractivePawn moveable,
            float clearance,
            float step,
            float maximumSearch,
            float sampleRadius,
            out Vector2 destination)
        {
            destination = default;

            var center =
                (Vector2)GetDoorWorldCenter(
                    destinationDoor);

            var up =
                (Vector2)destinationDoor.transform.up;

            var right =
                (Vector2)destinationDoor.transform.right;

            if (up.sqrMagnitude <= 0.0001f)
                up = Vector2.up;
            else
                up.Normalize();

            if (right.sqrMagnitude <= 0.0001f)
                right = Vector2.right;
            else
                right.Normalize();

            var directions =
                new[]
                {
                    up,
                    -up,
                    right,
                    -right
                };

            for (var distance = clearance;
                 distance <= maximumSearch + 0.0001f;
                 distance += step)
            {
                var found =
                    false;

                var best =
                    Vector2.zero;

                var bestScore =
                    float.PositiveInfinity;

                for (var index = 0;
                     index < directions.Length;
                     index++)
                {
                    var raw =
                        center +
                        directions[index] *
                        distance;

                    var closest =
                        GetClosestPointOnDoor(
                            destinationDoor,
                            raw);

                    var outward =
                        raw - closest;

                    if (outward.sqrMagnitude <= 0.0001f)
                        outward = directions[index];
                    else
                        outward.Normalize();

                    var query =
                        closest +
                        outward *
                        distance;

                    if (!TryAcceptLandingCandidate(
                            query,
                            destinationDoor,
                            moveable,
                            sampleRadius,
                            clearance,
                            out var candidate))
                    {
                        continue;
                    }

                    var score =
                        GetDistanceToDoor(
                            destinationDoor,
                            candidate) +
                        Vector2.Distance(
                            query,
                            candidate) *
                        0.10f +
                        index *
                        0.0001f;

                    if (found &&
                        score >= bestScore)
                    {
                        continue;
                    }

                    found = true;
                    bestScore = score;
                    best = candidate;
                }

                if (found)
                {
                    destination = best;
                    return true;
                }
            }

            return false;
        }

        private static Collider2D GetPrimaryDoorCollider(
            InteractivePawn door)
        {
            if (door == null)
                return null;

            var colliders =
                door.GetComponentsInChildren<Collider2D>(
                    true);

            Collider2D fallback = null;

            for (var index = 0;
                 index < colliders.Length;
                 index++)
            {
                var collider =
                    colliders[index];

                if (collider == null ||
                    !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (fallback == null)
                    fallback = collider;

                if (collider.isTrigger)
                    return collider;
            }

            return fallback;
        }

        private static Vector2 GetClosestPointOnDoor(
            InteractivePawn door,
            Vector2 worldPosition)
        {
            if (door == null)
                return worldPosition;

            var colliders =
                door.GetComponentsInChildren<Collider2D>(
                    true);

            var found =
                false;

            var best =
                worldPosition;

            var bestDistance =
                float.PositiveInfinity;

            for (var index = 0;
                 index < colliders.Length;
                 index++)
            {
                var collider =
                    colliders[index];

                if (collider == null ||
                    !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var closest =
                    collider.ClosestPoint(
                        worldPosition);

                var distance =
                    Vector2.SqrMagnitude(
                        worldPosition -
                        closest);

                if (found &&
                    distance >= bestDistance)
                {
                    continue;
                }

                found = true;
                bestDistance = distance;
                best = closest;
            }

            return found
                ? best
                : (Vector2)door.transform.position;
        }

        private bool TryAcceptLandingCandidate(
            Vector2 query,
            InteractivePawn destinationDoor,
            InteractivePawn moveable,
            float sampleRadius,
            float doorClearance,
            out Vector2 projected)
        {
            projected = default;

            if (!_navMeshManager.TryProject(
                    query,
                    sampleRadius,
                    out var candidate))
            {
                return false;
            }

            // Door Trigger 안 또는 바로 붙은 위치는
            // Teleport 직후 다시 Door 상호작용이 잡힐 수 있으므로 제외합니다.
            if (GetDistanceToDoor(
                    destinationDoor,
                    candidate) <
                doorClearance)
            {
                return false;
            }

            // 다른 이동 Pawn 위로 겹쳐서 Teleport되는 후보도 제외합니다.
            if (IsLandingBlockedByPawn(
                    candidate,
                    moveable,
                    destinationDoor))
            {
                return false;
            }

            // NavMesh 경계는 점 기준으로는 유효해도 Pawn Collider가 Wall에 살짝
            // 겹칠 수 있으므로 Obstacle FieldPawn과의 최소 여백도 검사합니다.
            if (IsLandingTooCloseToObstacle(
                    candidate))
            {
                return false;
            }

            projected = candidate;
            return true;
        }

        private bool IsLandingTooCloseToObstacle(
            Vector2 position)
        {
            var radius =
                Mathf.Max(
                    0f,
                    _doorLandingObstacleClearanceMeters);

            if (radius <= 0f)
                return false;

            var overlaps =
                Physics2D.OverlapCircleAll(
                    position,
                    radius);

            for (var index = 0;
                 index < overlaps.Length;
                 index++)
            {
                var collider =
                    overlaps[index];

                if (collider == null ||
                    !collider.enabled ||
                    collider.isTrigger ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var fieldPawn =
                    collider.GetComponentInParent<FieldPawn>();

                if (fieldPawn != null &&
                    fieldPawn.IsObstacle)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsLandingBlockedByPawn(
            Vector2 position,
            InteractivePawn moveable,
            InteractivePawn destinationDoor)
        {
            var clearance =
                Mathf.Max(
                    0f,
                    _doorLandingPawnClearanceMeters);

            for (var pawnIndex = 0;
                 pawnIndex < _interactivePawns.Count;
                 pawnIndex++)
            {
                var pawn =
                    _interactivePawns[pawnIndex];

                if (pawn == null ||
                    pawn == moveable ||
                    pawn == destinationDoor ||
                    pawn.IsDoor ||
                    !pawn.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var colliders =
                    pawn.GetComponentsInChildren<Collider2D>(
                        true);

                for (var colliderIndex = 0;
                     colliderIndex < colliders.Length;
                     colliderIndex++)
                {
                    var collider =
                        colliders[colliderIndex];

                    if (collider == null ||
                        !collider.enabled ||
                        collider.isTrigger ||
                        !collider.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    var closest =
                        collider.ClosestPoint(
                            position);

                    if (Vector2.Distance(
                            position,
                            closest) <= clearance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CancelPendingDoor()
        {
            var moveable =
                _pendingMoveable;

            if (moveable != null)
            {
                _doorGuards.Remove(
                    moveable);
            }

            ClearPendingDoorRequest(
                false);
        }

        private void ClearPendingDoorRequest(
            bool removeGuard)
        {
            if (removeGuard &&
                _pendingMoveable != null)
            {
                _doorGuards.Remove(
                    _pendingMoveable);
            }

            _pendingSourceDoor = null;
            _pendingMoveable = null;
            _pendingDestinationDoor = null;
        }

        private bool TryResolveDestinationDoor(
            InteractivePawn sourceDoor,
            InteractivePawn moveable,
            out InteractivePawn destinationDoor)
        {
            destinationDoor = null;

            return
                _settings != null &&
                _navMeshManager != null &&
                sourceDoor != null &&
                moveable != null &&
                sourceDoor.IsDoor &&
                moveable.IsMoveable &&
                !_doorGuards.Contains(
                    moveable) &&
                !string.IsNullOrWhiteSpace(
                    sourceDoor.LinkedDoorInstanceId) &&
                _doorsById.TryGetValue(
                    sourceDoor.LinkedDoorInstanceId,
                    out destinationDoor) &&
                destinationDoor != null &&
                destinationDoor != sourceDoor;
        }

        private IEnumerator ReleaseDoorGuard(
            InteractivePawn pawn,
            float seconds)
        {
            if (seconds > 0f)
            {
                yield return
                    new WaitForSecondsRealtime(
                        seconds);
            }
            else
            {
                yield return null;
            }

            _doorGuards.Remove(pawn);
        }

        private void OnValidate()
        {
            _doorInteractionRangeMeters =
                Mathf.Max(
                    0.1f,
                    _doorInteractionRangeMeters);

            _doorIconSizePixels =
                Mathf.Max(
                    24f,
                    _doorIconSizePixels);

            _promptWidth =
                Mathf.Max(
                    240f,
                    _promptWidth);

            _promptHeight =
                Mathf.Max(
                    120f,
                    _promptHeight);

            _doorLandingClearanceMeters =
                Mathf.Max(
                    0.02f,
                    _doorLandingClearanceMeters);

            _doorLandingSearchMaxMeters =
                Mathf.Max(
                    0.1f,
                    _doorLandingSearchMaxMeters);

            _doorLandingSearchStepMeters =
                Mathf.Max(
                    0.02f,
                    _doorLandingSearchStepMeters);

            _doorLandingEdgeSamples =
                Mathf.Clamp(
                    _doorLandingEdgeSamples,
                    3,
                    9);

            if ((_doorLandingEdgeSamples & 1) == 0)
                _doorLandingEdgeSamples++;

            _doorLandingSampleRadiusMeters =
                Mathf.Max(
                    0.02f,
                    _doorLandingSampleRadiusMeters);

            _doorLandingPawnClearanceMeters =
                Mathf.Max(
                    0f,
                    _doorLandingPawnClearanceMeters);

            _doorLandingObstacleClearanceMeters =
                Mathf.Max(
                    0f,
                    _doorLandingObstacleClearanceMeters);
        }

        private void OnDestroy()
        {
            Unbind();

            if (_doorIconButton != null)
            {
                _doorIconButton.onClick.RemoveListener(
                    HandleDoorIconClicked);
            }

            if (_ownedDoorCanvas != null)
            {
                Destroy(_ownedDoorCanvas);
                _ownedDoorCanvas = null;
            }

            TransferResolved = null;
        }
    }
}
