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
                (Vector2)activePawn.PresentationWorldPosition;

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
                (Vector2)_iconMoveable.PresentationWorldPosition;

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

            if (!_navMeshManager.TryProject(
                    destinationDoor.ArrivalPosition,
                    _settings.MaxProjectionMeters,
                    out var destination))
            {
                Debug.LogWarning(
                    $"[{destinationDoor.name}] Arrival Point 주변에 NavMesh가 없습니다.",
                    destinationDoor);

                _doorGuards.Remove(
                    moveable);

                ClearPendingDoorRequest(
                    false);

                return;
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
