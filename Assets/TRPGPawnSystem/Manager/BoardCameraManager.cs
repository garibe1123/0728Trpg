using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Trpg.Pawns
{
    [Serializable]
    public struct BoardCameraState
    {
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float ZoomMultiplier;

        public BoardCameraState(Vector3 position, float zoomMultiplier)
        {
            PositionX = position.x;
            PositionY = position.y;
            PositionZ = position.z;
            ZoomMultiplier = zoomMultiplier;
        }

        public Vector3 Position =>
            new Vector3(PositionX, PositionY, PositionZ);
    }

    [DisallowMultipleComponent]
    public sealed class BoardCameraManager : MonoBehaviour
    {
        private enum DragButton
        {
            None,
            Left,
            Middle
        }

        [Header("References")]
        [SerializeField] private Camera _boardCamera;
        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private TRPGSessionAuthority _sessionAuthority;

        [Header("Zoom")]
        [SerializeField, Range(0.1f, 1f)]
        private float _minimumZoomMultiplier = 0.5f;
        [SerializeField, Min(1f)]
        private float _maximumZoomMultiplier = 2f;
        [SerializeField, Range(0.01f, 0.5f)]
        private float _zoomStepPerNotch = 0.1f;
        [SerializeField]
        private bool _zoomTowardPointerWhenUnlocked = true;
        [SerializeField]
        private float _boardPlaneZ;

        [Header("Pan")]
        [SerializeField] private bool _allowLeftDrag = true;
        [SerializeField] private bool _allowMiddleDrag = true;
        [SerializeField, Min(0f)]
        private float _dragThresholdPixels = 5f;
        [SerializeField, Min(0f)]
        private float _playerPanRadius = 4f;
        [SerializeField]
        private bool _blockInputOverUi = true;

        private InputAction _scrollAction;
        private InputAction _leftAction;
        private InputAction _middleAction;
        private InputAction _pointAction;
        private DragButton _dragButton;
        private Vector2 _dragStart;
        private Vector2 _lastPointer;
        private bool _dragStarted;
        private Vector3 _initialPosition;
        private float _baseOrthographicSize;
        private float _zoomMultiplier = 1f;
        private InteractivePawn _selection;
        private InteractivePawn _previousSelection;
        private int _selectionChangedFrame = -1;
        private bool _leftPressPending;
        private bool _leftReleasePending;
        private bool _middlePressPending;
        private bool _middleReleasePending;
        private float _pendingScrollY;
        private bool _pointerOverUi;
        private Vector2 _lastUiRaycastPointer;
        private int _lastUiRaycastFrame = -1;
        private EventSystem _cachedEventSystem;
        private PointerEventData _pointerEventData;
        private readonly List<RaycastResult> _uiRaycastResults =
            new List<RaycastResult>(16);

        public event Action<BoardCameraState> CameraStateChanged;

        public Vector3 CameraPosition => _boardCamera != null
            ? _boardCamera.transform.position
            : Vector3.zero;
        public float ZoomMultiplier => _zoomMultiplier;
        public float OrthographicSize => _boardCamera != null
            ? _boardCamera.orthographicSize
            : 0f;
        public bool IsPanning =>
            _dragButton != DragButton.None && _dragStarted;
        public bool IsPlayerCameraLocked =>
            _sessionAuthority != null &&
            _sessionAuthority.IsOnline &&
            !_sessionAuthority.CanLocalControlCamera;

        private void Awake()
        {
            NormalizeValues();
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            _initialPosition = _boardCamera.transform.position;
            _baseOrthographicSize = Mathf.Max(
                0.01f,
                _boardCamera.orthographicSize);
            CreateInputActions();
        }

        private void OnEnable()
        {
            if (_scrollAction == null)
                return;

            _selection = _pawnManager.SelectedInteractive;
            _pawnManager.InteractiveSelectionChanged +=
                HandleSelectionChanged;

            _scrollAction.performed += HandleScroll;
            _leftAction.performed += HandleLeftPressed;
            _leftAction.canceled += HandleLeftReleased;
            _middleAction.performed += HandleMiddlePressed;
            _middleAction.canceled += HandleMiddleReleased;

            _scrollAction.Enable();
            _leftAction.Enable();
            _middleAction.Enable();
            _pointAction.Enable();
        }

        private void OnDisable()
        {
            CancelDrag();
            if (_pawnManager != null)
            {
                _pawnManager.InteractiveSelectionChanged -=
                    HandleSelectionChanged;
            }

            UnbindAndDisable(
                _scrollAction,
                HandleScroll);
            if (_leftAction != null)
            {
                _leftAction.performed -= HandleLeftPressed;
                _leftAction.canceled -= HandleLeftReleased;
                _leftAction.Disable();
            }
            if (_middleAction != null)
            {
                _middleAction.performed -= HandleMiddlePressed;
                _middleAction.canceled -= HandleMiddleReleased;
                _middleAction.Disable();
            }
            if (_pointAction != null)
                _pointAction.Disable();
            ClearPendingInput();
        }

        private void OnDestroy()
        {
            _scrollAction?.Dispose();
            _leftAction?.Dispose();
            _middleAction?.Dispose();
            _pointAction?.Dispose();
            CameraStateChanged = null;
        }

        private void OnValidate()
        {
            NormalizeValues();
        }

        private void Update()
        {
            if (!enabled || _boardCamera == null)
                return;

            RefreshPointerOverUiCache();
            ProcessPendingInput();
            ProcessDrag();
        }

        public BoardCameraState CaptureState()
        {
            return new BoardCameraState(
                CameraPosition,
                _zoomMultiplier);
        }

        public bool ApplyState(BoardCameraState state)
        {
            if (_boardCamera == null ||
                !IsFinite(state.PositionX) ||
                !IsFinite(state.PositionY) ||
                !IsFinite(state.PositionZ) ||
                !IsFinite(state.ZoomMultiplier))
            {
                return false;
            }

            CancelDrag();
            _boardCamera.transform.position = state.Position;
            ApplyZoom(state.ZoomMultiplier, false, default);
            PublishState();
            return true;
        }

        public bool SetCameraPosition(Vector3 position)
        {
            if (_boardCamera == null || !IsFinite(position))
                return false;

            CancelDrag();
            _boardCamera.transform.position = position;
            PublishState();
            return true;
        }

        public bool SetZoomMultiplier(float multiplier)
        {
            if (_boardCamera == null || !IsFinite(multiplier))
                return false;

            ApplyZoom(multiplier, false, default);
            PublishState();
            return true;
        }

        [ContextMenu("Reset Board Camera")]
        public void ResetToInitialState()
        {
            if (_boardCamera == null)
                return;

            CancelDrag();
            _boardCamera.transform.position = _initialPosition;
            ApplyZoom(1f, false, default);
            PublishState();
        }

        private void CreateInputActions()
        {
            _scrollAction = new InputAction(
                "BoardCameraZoom",
                InputActionType.PassThrough,
                "<Mouse>/scroll");
            _leftAction = new InputAction(
                "BoardCameraLeftDrag",
                InputActionType.Button,
                "<Mouse>/leftButton");
            _middleAction = new InputAction(
                "BoardCameraMiddleDrag",
                InputActionType.Button,
                "<Mouse>/middleButton");
            _pointAction = new InputAction(
                "BoardCameraPointer",
                InputActionType.PassThrough,
                "<Pointer>/position");
        }

        private void HandleScroll(InputAction.CallbackContext context)
        {
            _pendingScrollY += context.ReadValue<Vector2>().y;
        }

        private void HandleLeftPressed(
            InputAction.CallbackContext context)
        {
            if (_allowLeftDrag)
                _leftPressPending = true;
        }

        private void HandleLeftReleased(
            InputAction.CallbackContext context)
        {
            _leftReleasePending = true;
        }

        private void HandleMiddlePressed(
            InputAction.CallbackContext context)
        {
            if (_allowMiddleDrag)
                _middlePressPending = true;
        }

        private void HandleMiddleReleased(
            InputAction.CallbackContext context)
        {
            _middleReleasePending = true;
        }

        private void ProcessPendingInput()
        {
            if (_leftReleasePending)
            {
                _leftReleasePending = false;
                if (_dragButton == DragButton.Left)
                    CancelDrag();
            }

            if (_middleReleasePending)
            {
                _middleReleasePending = false;
                if (_dragButton == DragButton.Middle)
                    CancelDrag();
            }

            if (_leftPressPending)
            {
                _leftPressPending = false;
                TryBeginDrag(DragButton.Left);
            }

            if (_middlePressPending)
            {
                _middlePressPending = false;
                TryBeginDrag(DragButton.Middle);
            }

            var scrollY = _pendingScrollY;
            _pendingScrollY = 0f;
            if (Mathf.Abs(scrollY) <= 0.001f ||
                IsPlayerCameraLocked ||
                _pointerOverUi ||
                !TryGetPointer(out var pointer))
            {
                return;
            }

            var notches = Mathf.Abs(scrollY) >= 10f
                ? scrollY / 120f
                : scrollY;
            notches = Mathf.Clamp(notches, -4f, 4f);
            ApplyZoom(
                _zoomMultiplier - notches * _zoomStepPerNotch,
                _zoomTowardPointerWhenUnlocked &&
                !IsPlayerCameraLocked,
                pointer);
            PublishState();
        }

        private void ProcessDrag()
        {
            if (_dragButton == DragButton.None)
                return;

            if (!IsDragButtonPressed() || IsPlayerCameraLocked)
            {
                CancelDrag();
                return;
            }

            if (_dragButton == DragButton.Left && _selection != null)
            {
                CancelDrag();
                return;
            }

            if (!TryGetPointer(out var pointer))
            {
                CancelDrag();
                return;
            }

            if (!_dragStarted)
            {
                if ((pointer - _dragStart).sqrMagnitude <
                    _dragThresholdPixels * _dragThresholdPixels)
                {
                    _lastPointer = pointer;
                    return;
                }

                // The threshold-crossing frame establishes a new origin.
                // Do not apply the accumulated pre-drag delta as a camera jump.
                _dragStarted = true;
                _lastPointer = pointer;
                return;
            }

            var delta = pointer - _lastPointer;
            _lastPointer = pointer;
            if (delta.sqrMagnitude <= 0.0001f)
                return;

            Pan(delta);
            PublishState();
        }

        private void TryBeginDrag(DragButton button)
        {
            if (_dragButton != DragButton.None ||
                IsPlayerCameraLocked ||
                !TryGetPointer(out var pointer) ||
                _pointerOverUi)
            {
                return;
            }

            if (button == DragButton.Left && HadSelectionAtPress())
                return;

            _dragButton = button;
            _dragStart = pointer;
            _lastPointer = pointer;
            _dragStarted = false;
        }

        private void Pan(Vector2 screenDelta)
        {
            var transform = _boardCamera.transform;
            var unitsPerPixel =
                _boardCamera.orthographicSize * 2f /
                Mathf.Max(1, _boardCamera.pixelHeight);
            var movement =
                transform.right * (screenDelta.x * unitsPerPixel) +
                transform.up * (screenDelta.y * unitsPerPixel);
            movement.z = 0f;
            var candidate = transform.position - movement;
            transform.position = ClampToControlledPawn(candidate);
        }


        private Vector3 ClampToControlledPawn(Vector3 candidate)
        {
            if (_sessionAuthority == null ||
                !_sessionAuthority.IsOnline ||
                _sessionAuthority.IsLocalGameMaster ||
                !_sessionAuthority.TryGetLocalControlledPawn(out var pawn))
            {
                return candidate;
            }

            var radius = Mathf.Max(0f, _playerPanRadius);
            var anchor = pawn.WorldPosition;
            var offset = new Vector2(
                candidate.x - anchor.x,
                candidate.y - anchor.y);
            if (offset.sqrMagnitude <= radius * radius)
                return candidate;

            var clamped = offset.normalized * radius;
            candidate.x = anchor.x + clamped.x;
            candidate.y = anchor.y + clamped.y;
            return candidate;
        }

        private void ApplyZoom(
            float multiplier,
            bool anchorToPointer,
            Vector2 pointer)
        {
            var next = Mathf.Clamp(
                multiplier,
                _minimumZoomMultiplier,
                _maximumZoomMultiplier);
            if (Mathf.Approximately(next, _zoomMultiplier))
                return;

            // 포인터 기준 보정이 필요하지 않으면 크기만 변경한다.
            if (!anchorToPointer)
            {
                SetZoomSize(next);
                return;
            }

            // before는 이 분기에서 성공적으로 할당된 경우에만 아래에서 사용한다.
            Vector3 before = Vector3.zero;
            if (!TryGetBoardPoint(pointer, out before))
            {
                SetZoomSize(next);
                return;
            }

            SetZoomSize(next);

            Vector3 after = Vector3.zero;
            if (!TryGetBoardPoint(pointer, out after))
                return;

            var correction = before - after;
            correction.z = 0f;
            _boardCamera.transform.position += correction;
        }

        private void SetZoomSize(float multiplier)
        {
            _zoomMultiplier = multiplier;
            _boardCamera.orthographicSize =
                _baseOrthographicSize * _zoomMultiplier;
        }

        private bool TryGetBoardPoint(
            Vector2 screenPosition,
            out Vector3 worldPosition)
        {
            var ray = _boardCamera.ScreenPointToRay(screenPosition);
            var plane = new Plane(
                Vector3.forward,
                new Vector3(0f, 0f, _boardPlaneZ));
            if (plane.Raycast(ray, out var distance))
            {
                worldPosition = ray.GetPoint(distance);
                return true;
            }

            worldPosition = default;
            return false;
        }

        private void HandleSelectionChanged(InteractivePawn pawn)
        {
            _previousSelection = _selection;
            _selection = pawn;
            _selectionChangedFrame = Time.frameCount;
            if (IsPlayerPawn(pawn))
                CancelDrag();
        }

        private bool WasPlayerSelectedAtPress()
        {
            return IsPlayerPawn(_selection) ||
                (_selectionChangedFrame == Time.frameCount &&
                 IsPlayerPawn(_previousSelection));
        }

        private bool HadSelectionAtPress()
        {
            return _selection != null ||
                (_selectionChangedFrame == Time.frameCount &&
                 _previousSelection != null);
        }

        private bool IsPlayerPawn(InteractivePawn pawn)
        {
            if (pawn == null || _pawnManager == null)
                return false;

            var players = _pawnManager.PlayerPawns;
            for (var index = 0; index < players.Count; index++)
            {
                if (players[index] == pawn)
                    return true;
            }
            return false;
        }

        private bool IsDragButtonPressed()
        {
            return _dragButton == DragButton.Left
                ? _leftAction.IsPressed()
                : _dragButton == DragButton.Middle &&
                  _middleAction.IsPressed();
        }

        private bool TryGetPointer(out Vector2 pointer)
        {
            if (_pointAction != null && _pointAction.enabled)
            {
                pointer = _pointAction.ReadValue<Vector2>();
                return true;
            }

            if (Pointer.current != null)
            {
                pointer = Pointer.current.position.ReadValue();
                return true;
            }

            pointer = default;
            return false;
        }

        private void RefreshPointerOverUiCache()
        {
            if (!_blockInputOverUi ||
                !TryGetPointer(out var pointer))
            {
                _pointerOverUi = false;
                return;
            }

            var requiresFreshRaycast =
                _lastUiRaycastFrame != Time.frameCount &&
                ((_lastUiRaycastPointer - pointer).sqrMagnitude > 0.01f ||
                 _leftPressPending ||
                 _middlePressPending ||
                 Mathf.Abs(_pendingScrollY) > 0.001f ||
                 _dragButton != DragButton.None);
            if (!requiresFreshRaycast)
                return;

            _lastUiRaycastFrame = Time.frameCount;
            _lastUiRaycastPointer = pointer;
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                _pointerOverUi = false;
                return;
            }

            if (_cachedEventSystem != eventSystem ||
                _pointerEventData == null)
            {
                _cachedEventSystem = eventSystem;
                _pointerEventData = new PointerEventData(eventSystem);
            }

            _pointerEventData.Reset();
            _pointerEventData.position = pointer;
            _uiRaycastResults.Clear();
            eventSystem.RaycastAll(
                _pointerEventData,
                _uiRaycastResults);
            _pointerOverUi = _uiRaycastResults.Count > 0;
        }

        private void ClearPendingInput()
        {
            _leftPressPending = false;
            _leftReleasePending = false;
            _middlePressPending = false;
            _middleReleasePending = false;
            _pendingScrollY = 0f;
            _pointerOverUi = false;
            _uiRaycastResults.Clear();
        }

        private bool ValidateReferences()
        {
            if (_boardCamera == null)
            {
                Debug.LogError(
                    $"[{name}] Board Camera가 연결되지 않았습니다.",
                    this);
                return false;
            }
            if (!_boardCamera.orthographic)
            {
                Debug.LogError(
                    $"[{name}] Board Camera는 Orthographic이어야 합니다.",
                    _boardCamera);
                return false;
            }
            if (_pawnManager == null)
            {
                Debug.LogError(
                    $"[{name}] PawnManager가 연결되지 않았습니다.",
                    this);
                return false;
            }
            return true;
        }

        private void NormalizeValues()
        {
            _minimumZoomMultiplier = Mathf.Clamp(
                _minimumZoomMultiplier,
                0.1f,
                1f);
            _maximumZoomMultiplier = Mathf.Max(
                1f,
                _maximumZoomMultiplier);
            _zoomStepPerNotch = Mathf.Clamp(
                _zoomStepPerNotch,
                0.01f,
                0.5f);
            _dragThresholdPixels = Mathf.Max(
                8f,
                _dragThresholdPixels);
            _playerPanRadius = Mathf.Max(0f, _playerPanRadius);
        }

        private void CancelDrag()
        {
            _dragButton = DragButton.None;
            _dragStarted = false;
        }

        private void PublishState()
        {
            CameraStateChanged?.Invoke(CaptureState());
        }

        private static void UnbindAndDisable(
            InputAction action,
            Action<InputAction.CallbackContext> handler)
        {
            if (action == null)
                return;

            action.performed -= handler;
            action.Disable();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }
    }
}