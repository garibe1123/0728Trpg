using System;
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
        public bool IsPlayerCameraLocked => IsPlayerPawn(_selection);

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
            _pointAction.performed += HandlePointerMoved;

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
            UnbindAndDisable(
                _pointAction,
                HandlePointerMoved);
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
            var scrollY = context.ReadValue<Vector2>().y;
            if (Mathf.Abs(scrollY) <= 0.001f ||
                !TryGetPointer(out var pointer) ||
                IsPointerOverUi())
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

        private void HandleLeftPressed(
            InputAction.CallbackContext context)
        {
            if (_allowLeftDrag)
                TryBeginDrag(DragButton.Left);
        }

        private void HandleLeftReleased(
            InputAction.CallbackContext context)
        {
            if (_dragButton == DragButton.Left)
                CancelDrag();
        }

        private void HandleMiddlePressed(
            InputAction.CallbackContext context)
        {
            if (_allowMiddleDrag)
                TryBeginDrag(DragButton.Middle);
        }

        private void HandleMiddleReleased(
            InputAction.CallbackContext context)
        {
            if (_dragButton == DragButton.Middle)
                CancelDrag();
        }

        private void HandlePointerMoved(
            InputAction.CallbackContext context)
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

            var pointer = context.ReadValue<Vector2>();
            if (!_dragStarted)
            {
                if ((pointer - _dragStart).sqrMagnitude <
                    _dragThresholdPixels * _dragThresholdPixels)
                {
                    _lastPointer = pointer;
                    return;
                }
                _dragStarted = true;
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
                WasPlayerSelectedAtPress() ||
                !TryGetPointer(out var pointer) ||
                IsPointerOverUi())
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
            transform.position -= movement;
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

        private bool IsPointerOverUi()
        {
            return _blockInputOverUi &&
                   EventSystem.current != null &&
                   EventSystem.current.IsPointerOverGameObject();
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
                0f,
                _dragThresholdPixels);
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
