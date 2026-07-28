using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public sealed class PawnManager : MonoBehaviour
    {
        public enum TurnGroup
        {
            Player,
            Monster,
            Npc
        }

        [Header("Pawn Collection")]
        [SerializeField, Tooltip(
            "Pawn을 수집할 부모. 비어 있으면 PawnManager 자신의 자식을 수집")]
        private Transform _pawnRoot;

        [SerializeField, Tooltip("2D 보드를 보여주는 Orthographic Camera")]
        private Camera _boardCamera;

        [FormerlySerializedAs("_pawnLayerMask")]
        [SerializeField, Tooltip("선택할 InteractivePawn Collider2D의 Layer")]
        private LayerMask _interactivePawnLayerMask = ~0;

        [Header("Selection Materials")]
        [SerializeField, Tooltip(
            "선택되지 않은 Interactive Pawn에 적용할 Material")]
        private Material _defaultMaterial;

        [SerializeField, Tooltip(
            "선택된 Interactive Pawn에 적용할 Material")]
        private Material _activeMaterial;

        [SerializeField]
        private PawnMovementManager _movementManager;

        [Header("Pawn Groups - Auto Collected")]
        [SerializeField, Tooltip(
            "MoveablePawnKind.Player로 자동 분류된 Pawn")]
        private List<InteractivePawn> _playerPawns =
            new List<InteractivePawn>();

        [SerializeField, Tooltip(
            "MoveablePawnKind.Monster로 자동 분류된 Pawn")]
        private List<InteractivePawn> _monsterPawns =
            new List<InteractivePawn>();

        [SerializeField, Tooltip(
            "InteractivePawnKind.Npc로 자동 분류된 Pawn")]
        private List<InteractivePawn> _npcPawns =
            new List<InteractivePawn>();

        [Header("Turn UI")]
        [SerializeField, Tooltip(
            "직접 만든 턴 넘기기 버튼. 비어 있으면 런타임에 자동 생성")]
        private Button _nextTurnButton;

        [SerializeField, Tooltip(
            "Next Turn Button이 비어 있을 때 좌측 상단 버튼 자동 생성")]
        private bool _createTurnButtonAtRuntime = true;

        [SerializeField, Tooltip("자동 생성 턴 UI의 기준 해상도")]
        private Vector2 _turnUiReferenceResolution =
            new Vector2(1920f, 1080f);

        [SerializeField, Tooltip(
            "자동 생성 턴 버튼의 좌측 상단 기준 위치")]
        private Vector2 _turnButtonOffset =
            new Vector2(24f, -24f);

        [SerializeField, Tooltip("자동 생성 턴 버튼의 크기")]
        private Vector2 _turnButtonSize =
            new Vector2(180f, 64f);

        [SerializeField, Tooltip("자동 생성 턴 버튼의 기본 색")]
        private Color _turnButtonColor =
            new Color(0.075f, 0.16f, 0.22f, 0.96f);

        [Header("Player Interaction Bar")]
        [SerializeField, Tooltip(
            "직접 만든 우측 Player 바. 비어 있으면 런타임에 자동 생성")]
        private PlayerInteractionBarWidget _playerInteractionBarWidget;

        [SerializeField, Tooltip(
            "Player Interaction Bar Widget이 비어 있을 때 런타임 생성")]
        private bool _createPlayerInteractionBarAtRuntime = true;

        [SerializeField]
        private PlayerInteractionBarStyle _playerInteractionBarStyle =
            PlayerInteractionBarStyle.Default;

        [Header("Player Camera Focus")]
        [SerializeField, Min(0f), Tooltip(
            "Player 선택 뒤 카메라 이동을 시작할 지연 시간")]
        private float _cameraStartDelay = 0.08f;

        [SerializeField, Min(0.01f), Tooltip(
            "선택한 Player 중심까지 카메라가 이동하는 시간")]
        private float _cameraFocusDuration = 0.42f;

        [SerializeField, Tooltip(
            "Player 중심에서 카메라를 추가로 옮길 월드 좌표 오프셋")]
        private Vector2 _cameraFocusOffset;

        [SerializeField, Tooltip("Player 카메라 포커스 이동 Ease")]
        private Ease _cameraFocusEase = Ease.OutCubic;

        private readonly List<Pawn> _pawns = new List<Pawn>();
        private readonly List<InteractivePawn> _interactivePawns =
            new List<InteractivePawn>();
        private readonly Dictionary<InteractivePawn, SpriteRenderer[]>
            _interactiveRenderers =
                new Dictionary<InteractivePawn, SpriteRenderer[]>();
        private readonly List<RaycastResult> _uiRaycastResults =
            new List<RaycastResult>();
        private readonly List<PlayerInteractionSlotData>
            _playerInteractionSlotData =
                new List<PlayerInteractionSlotData>();

        private InputAction _selectAction;
        private InputAction _moveAction;
        private InputAction _pointAction;
        private InteractivePawn _selectedInteractive;
        private PointerEventData _uiPointerEventData;
        private EventSystem _cachedEventSystem;
        private GameObject _ownedTurnCanvas;
        private GameObject _ownedPlayerInteractionBarCanvas;
        private Image _nextTurnButtonImage;
        private Text _nextTurnButtonLabel;
        private Sequence _cameraFocusTween;
        private InteractivePawn _interactionPlayer;
        private TurnGroup _currentTurnGroup;
        private bool _hasCurrentTurnGroup;
        private bool _isMovementModeActive;

        public event Action<InteractivePawn> InteractiveSelectionChanged;
        public event Action<InteractivePawn> InteractionPlayerChanged;
        public event Action<InteractivePawn> InteractionRequested;
        public event Action<TurnGroup, IReadOnlyList<InteractivePawn>>
            TurnGroupChanged;

        public InteractivePawn SelectedInteractive =>
            _selectedInteractive;
        public InteractivePawn InteractionPlayer =>
            _interactionPlayer;
        public bool IsMovementModeActive => _isMovementModeActive;
        public PawnMovementManager MovementManager => _movementManager;
        public Camera BoardCamera => _boardCamera;
        public IReadOnlyList<InteractivePawn> PlayerPawns =>
            _playerPawns;
        public IReadOnlyList<InteractivePawn> MonsterPawns =>
            _monsterPawns;
        public IReadOnlyList<InteractivePawn> NpcPawns => _npcPawns;
        public TurnGroup CurrentTurnGroup => _currentTurnGroup;
        public bool HasCurrentTurnGroup => _hasCurrentTurnGroup;
        public IReadOnlyList<InteractivePawn> CurrentTurnPawns =>
            _hasCurrentTurnGroup
                ? GetTurnGroupPawns(_currentTurnGroup)
                : Array.Empty<InteractivePawn>();

        public void ClearSelection()
        {
            SetMovementMode(false);
            if (_selectedInteractive == null)
            {
                return;
            }

            ApplySelectionPresentation(
                _selectedInteractive,
                _defaultMaterial,
                false);
            _selectedInteractive = null;
            InteractiveSelectionChanged?.Invoke(null);
            ApplyInteractionPlayer(null, true, false);
        }

        public bool SetMovementMode(bool enabled)
        {
            var canActivate =
                enabled &&
                _selectedInteractive != null &&
                _selectedInteractive.IsMoveable;
            _isMovementModeActive = canActivate;

            if (_movementManager == null)
            {
                return false;
            }

            if (!canActivate)
            {
                _movementManager.ClearMover();
                return false;
            }

            _movementManager.SelectMover(_selectedInteractive);
            RefreshPointerPreview();
            return true;
        }

        public bool AdvanceTurn()
        {
            if (!_hasCurrentTurnGroup)
            {
                if (!TryFindFirstTurnGroup(out var firstGroup))
                {
                    RefreshTurnButtonState();
                    return false;
                }

                _currentTurnGroup = firstGroup;
                _hasCurrentTurnGroup = true;
                ResetMovementBudgetsForGroup(firstGroup);
                RefreshTurnButtonState();
                PublishTurnGroupChanged();
                return true;
            }

            if (!TryFindNextTurnGroup(
                    _currentTurnGroup,
                    out var nextGroup))
            {
                _hasCurrentTurnGroup = false;
                RefreshTurnButtonState();
                return false;
            }

            ClearSelection();
            _currentTurnGroup = nextGroup;
            _hasCurrentTurnGroup = true;
            ResetMovementBudgetsForGroup(nextGroup);
            RefreshTurnButtonState();
            PublishTurnGroupChanged();
            return true;
        }

        [ContextMenu("Refresh Pawn Groups")]
        public void RefreshPawnGroups()
        {
            if (!Application.isPlaying)
            {
                IndexPawns(true);
                InitializeCurrentTurnGroup();
                return;
            }

            ClearSelection();
            _movementManager.Unbind();

            for (var index = 0; index < _pawns.Count; index++)
            {
                if (_pawns[index] != null)
                {
                    _pawns[index].Unbind();
                }
            }

            IndexPawns(true);

            for (var index = 0; index < _pawns.Count; index++)
            {
                if (_pawns[index] != null)
                {
                    _pawns[index].Bind();
                }
            }

            ApplyDefaultPresentations();
            _movementManager.Bind(_interactivePawns);
            InitializeCurrentTurnGroup();
            RefreshTurnButtonState();
            RefreshPlayerInteractionBar();
            PublishTurnGroupChanged();
        }

        public IReadOnlyList<InteractivePawn> GetTurnGroupPawns(
            TurnGroup group)
        {
            switch (group)
            {
                case TurnGroup.Player:
                    return _playerPawns;
                case TurnGroup.Monster:
                    return _monsterPawns;
                case TurnGroup.Npc:
                    return _npcPawns;
                default:
                    return Array.Empty<InteractivePawn>();
            }
        }

        private void Awake()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            IndexPawns(true);
            InitializeCurrentTurnGroup();
            ValidateSelectionMaterials();
            EnsureTurnButton();
            EnsurePlayerInteractionBar();

            _selectAction = new InputAction(
                "PawnSelect",
                InputActionType.Button,
                "<Pointer>/leftButton");
            _moveAction = new InputAction(
                "PawnMove",
                InputActionType.Button,
                "<Pointer>/rightButton");
            _pointAction = new InputAction(
                "PawnPoint",
                InputActionType.PassThrough,
                "<Pointer>/position");
        }

        private void OnEnable()
        {
            if (_selectAction == null ||
                _moveAction == null ||
                _pointAction == null)
            {
                return;
            }

            for (var index = 0; index < _pawns.Count; index++)
            {
                if (_pawns[index] != null)
                {
                    _pawns[index].Bind();
                }
            }

            ApplyDefaultPresentations();
            ApplySelectionPresentation(
                _selectedInteractive,
                _activeMaterial,
                true);
            _movementManager.Bind(_interactivePawns);
            _isMovementModeActive = false;
            _movementManager.ClearMover();

            BindTurnButton();
            RefreshTurnButtonState();
            BindPlayerInteractionBar();

            _selectAction.performed += HandleSelectPerformed;
            _moveAction.performed += HandleMovePerformed;
            _pointAction.performed += HandlePointPerformed;
            _selectAction.Enable();
            _moveAction.Enable();
            _pointAction.Enable();

            PublishTurnGroupChanged();
        }

        private void OnDisable()
        {
            SetMovementMode(false);
            UnbindTurnButton();
            UnbindPlayerInteractionBar();
            KillCameraFocusTween();

            if (_selectAction != null)
            {
                _selectAction.performed -= HandleSelectPerformed;
                _selectAction.Disable();
            }

            if (_moveAction != null)
            {
                _moveAction.performed -= HandleMovePerformed;
                _moveAction.Disable();
            }

            if (_pointAction != null)
            {
                _pointAction.performed -= HandlePointPerformed;
                _pointAction.Disable();
            }

            ApplySelectionPresentation(
                _selectedInteractive,
                _defaultMaterial,
                false);

            if (_movementManager != null)
            {
                _movementManager.Unbind();
            }

            for (var index = 0; index < _pawns.Count; index++)
            {
                if (_pawns[index] != null)
                {
                    _pawns[index].Unbind();
                }
            }
        }

        private void OnDestroy()
        {
            _selectAction?.Dispose();
            _moveAction?.Dispose();
            _pointAction?.Dispose();

            if (_ownedTurnCanvas != null)
            {
                Destroy(_ownedTurnCanvas);
                _ownedTurnCanvas = null;
            }

            if (_ownedPlayerInteractionBarCanvas != null)
            {
                Destroy(_ownedPlayerInteractionBarCanvas);
                _ownedPlayerInteractionBarCanvas = null;
            }

            InteractiveSelectionChanged = null;
            InteractionPlayerChanged = null;
            InteractionRequested = null;
            TurnGroupChanged = null;
        }

        private void HandleSelectPerformed(
            InputAction.CallbackContext context)
        {
            if (!TryGetPointerWorldPosition(out var worldPosition))
            {
                return;
            }

            var colliders = Physics2D.OverlapPointAll(
                worldPosition,
                _interactivePawnLayerMask);
            var target = ResolveInteractiveTarget(colliders);
            if (target != null)
            {
                SelectInteractive(target);
                return;
            }

            ClearSelection();
        }

        private void HandleMovePerformed(
            InputAction.CallbackContext context)
        {
            if (!_isMovementModeActive)
            {
                return;
            }

            if (TryGetPointerWorldPosition(out var worldPosition))
            {
                _movementManager.TryMoveSelectedTo(worldPosition);
            }
        }

        private void HandlePointPerformed(
            InputAction.CallbackContext context)
        {
            RefreshPointerPreview();
        }

        private void HandleNextTurnClicked()
        {
            AdvanceTurn();
        }

        private InteractivePawn ResolveInteractiveTarget(
            IReadOnlyList<Collider2D> colliders)
        {
            for (var index = 0; index < colliders.Count; index++)
            {
                var pawn = colliders[index].GetComponentInParent<Pawn>();
                if (pawn == null || !pawn.IsBound)
                {
                    continue;
                }

                if (pawn is InteractivePawn interactive)
                {
                    return interactive;
                }
            }

            return null;
        }

        private bool TryGetPointerWorldPosition(out Vector2 worldPosition)
        {
            return TryGetPointerWorldPosition(
                out worldPosition,
                out _);
        }

        private bool TryGetPointerWorldPosition(
            out Vector2 worldPosition,
            out Vector2 screenPosition)
        {
            worldPosition = default;
            screenPosition = default;

            var pointer = Pointer.current;
            if (pointer == null)
            {
                return false;
            }

            screenPosition = pointer.position.ReadValue();
            if (IsPointerOverUi(screenPosition))
            {
                return false;
            }

            var world = _boardCamera.ScreenToWorldPoint(
                new Vector3(
                    screenPosition.x,
                    screenPosition.y,
                    0f));
            worldPosition = new Vector2(world.x, world.y);
            return true;
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            if (_uiPointerEventData == null ||
                _cachedEventSystem != eventSystem)
            {
                _cachedEventSystem = eventSystem;
                _uiPointerEventData =
                    new PointerEventData(eventSystem);
            }

            _uiPointerEventData.Reset();
            _uiPointerEventData.position = screenPosition;
            _uiRaycastResults.Clear();
            eventSystem.RaycastAll(
                _uiPointerEventData,
                _uiRaycastResults);

            for (var index = 0;
                 index < _uiRaycastResults.Count;
                 index++)
            {
                if (_uiRaycastResults[index].module is GraphicRaycaster)
                {
                    return true;
                }
            }

            return false;
        }

        public void SelectInteractive(InteractivePawn pawn)
        {
            if (pawn == null)
            {
                ClearSelection();
                return;
            }

            if (!_interactivePawns.Contains(pawn))
            {
                Debug.LogWarning(
                    $"[{name}] 등록되지 않은 InteractivePawn은 " +
                    "선택할 수 없습니다.",
                    pawn);
                return;
            }

            SetMovementMode(false);

            if (_selectedInteractive != null &&
                _selectedInteractive != pawn)
            {
                ApplySelectionPresentation(
                    _selectedInteractive,
                    _defaultMaterial,
                    false);
            }

            _selectedInteractive = pawn;
            ApplySelectionPresentation(
                _selectedInteractive,
                _activeMaterial,
                true);
            InteractiveSelectionChanged?.Invoke(_selectedInteractive);
            ApplyInteractionPlayer(_selectedInteractive, true, true);

            if (!pawn.IsMoveable)
            {
                InteractionRequested?.Invoke(pawn);
            }
        }

        private void RefreshPointerPreview()
        {
            if (!_isMovementModeActive ||
                _selectedInteractive == null ||
                !_selectedInteractive.IsMoveable ||
                !TryGetPointerWorldPosition(
                    out var worldPosition,
                    out var screenPosition))
            {
                _movementManager.HidePathPreview();
                return;
            }

            _movementManager.PreviewSelectedPath(
                worldPosition,
                screenPosition);
        }

        private void IndexPawns(bool logValidation)
        {
            EnsurePawnGroupLists();
            _pawns.Clear();
            _interactivePawns.Clear();
            _interactiveRenderers.Clear();
            _playerPawns.Clear();
            _monsterPawns.Clear();
            _npcPawns.Clear();

            var root = _pawnRoot != null ? _pawnRoot : transform;
            var found = root.GetComponentsInChildren<Pawn>(true);
            var ids = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < found.Length; index++)
            {
                var pawn = found[index];
                if (pawn == null)
                {
                    continue;
                }

                _pawns.Add(pawn);

                var hasValidId =
                    !string.IsNullOrWhiteSpace(pawn.InstanceId);
                var isUniqueId =
                    hasValidId && ids.Add(pawn.InstanceId);

                if (logValidation && (!hasValidId || !isUniqueId))
                {
                    Debug.LogError(
                        $"[{pawn.name}] 비어 있거나 중복된 " +
                        "Pawn Instance Id입니다.",
                        pawn);
                }

                if (pawn is InteractivePawn interactive)
                {
                    _interactivePawns.Add(interactive);
                    ClassifyTurnPawn(interactive, logValidation);
                    CacheInteractiveRenderers(
                        interactive,
                        logValidation);
                }
            }
        }

        private void EnsurePawnGroupLists()
        {
            if (_playerPawns == null)
            {
                _playerPawns = new List<InteractivePawn>();
            }

            if (_monsterPawns == null)
            {
                _monsterPawns = new List<InteractivePawn>();
            }

            if (_npcPawns == null)
            {
                _npcPawns = new List<InteractivePawn>();
            }
        }

        private void ClassifyTurnPawn(
            InteractivePawn pawn,
            bool logValidation)
        {
            var definition = pawn.Definition;
            if (definition == null)
            {
                if (logValidation)
                {
                    Debug.LogError(
                        $"[{pawn.name}] Definition이 비어 있어 " +
                        "턴 그룹에 분류할 수 없습니다.",
                        pawn);
                }

                return;
            }

            if (definition.Kind == InteractivePawnKind.Npc)
            {
                _npcPawns.Add(pawn);
                return;
            }

            if (definition.Kind != InteractivePawnKind.Moveable)
            {
                return;
            }

            if (definition.MoveableKind == MoveablePawnKind.Player)
            {
                _playerPawns.Add(pawn);
            }
            else
            {
                _monsterPawns.Add(pawn);
            }
        }

        private void CacheInteractiveRenderers(
            InteractivePawn pawn,
            bool logValidation)
        {
            var renderers =
                pawn.GetComponentsInChildren<SpriteRenderer>(true);
            _interactiveRenderers[pawn] = renderers;

            if (logValidation && renderers.Length == 0)
            {
                Debug.LogWarning(
                    $"[{pawn.name}] 선택 Material을 적용할 " +
                    "SpriteRenderer가 없습니다.",
                    pawn);
            }
        }

        private void ApplyDefaultPresentations()
        {
            for (var index = 0;
                 index < _interactivePawns.Count;
                 index++)
            {
                ApplySelectionPresentation(
                    _interactivePawns[index],
                    _defaultMaterial,
                    false);
            }
        }

        private void ApplySelectionPresentation(
            InteractivePawn pawn,
            Material material,
            bool selected)
        {
            if (pawn == null)
            {
                return;
            }

            pawn.SetSelected(selected);
            if (material == null)
            {
                return;
            }

            if (!_interactiveRenderers.TryGetValue(
                    pawn,
                    out var renderers) ||
                renderers == null ||
                renderers.Length == 0)
            {
                renderers =
                    pawn.GetComponentsInChildren<SpriteRenderer>(true);
                _interactiveRenderers[pawn] = renderers;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].sharedMaterial = material;
                }
            }
        }

        private void InitializeCurrentTurnGroup()
        {
            if (_hasCurrentTurnGroup &&
                GetTurnGroupPawns(_currentTurnGroup).Count > 0)
            {
                return;
            }

            if (TryFindFirstTurnGroup(out var firstGroup))
            {
                _currentTurnGroup = firstGroup;
                _hasCurrentTurnGroup = true;
            }
            else
            {
                _currentTurnGroup = TurnGroup.Player;
                _hasCurrentTurnGroup = false;
            }
        }

        private bool TryFindFirstTurnGroup(out TurnGroup group)
        {
            for (var index = 0; index < 3; index++)
            {
                var candidate = (TurnGroup)index;
                if (GetTurnGroupPawns(candidate).Count > 0)
                {
                    group = candidate;
                    return true;
                }
            }

            group = TurnGroup.Player;
            return false;
        }

        private bool TryFindNextTurnGroup(
            TurnGroup current,
            out TurnGroup group)
        {
            var currentIndex = (int)current;

            for (var offset = 1; offset <= 3; offset++)
            {
                var candidate =
                    (TurnGroup)((currentIndex + offset) % 3);
                if (GetTurnGroupPawns(candidate).Count > 0)
                {
                    group = candidate;
                    return true;
                }
            }

            group = current;
            return false;
        }

        private void ResetMovementBudgetsForGroup(TurnGroup group)
        {
            if (_movementManager == null)
            {
                return;
            }

            var pawns = GetTurnGroupPawns(group);
            for (var index = 0; index < pawns.Count; index++)
            {
                var pawn = pawns[index];
                if (pawn != null && pawn.IsMoveable)
                {
                    _movementManager.ResetMovementBudget(pawn);
                }
            }
        }

        private void PublishTurnGroupChanged()
        {
            if (_hasCurrentTurnGroup)
            {
                TurnGroupChanged?.Invoke(
                    _currentTurnGroup,
                    GetTurnGroupPawns(_currentTurnGroup));
            }
        }

        [ContextMenu("Refresh Player Interaction Bar")]
        public void RefreshPlayerInteractionBar()
        {
            if (!Application.isPlaying ||
                _playerInteractionBarWidget == null)
            {
                return;
            }

            RebindPlayerInteractionBar();
            ApplyInteractionPlayer(
                _selectedInteractive,
                false,
                false);
        }

        private void EnsurePlayerInteractionBar()
        {
            if (_playerInteractionBarWidget != null)
            {
                return;
            }

            if (!_createPlayerInteractionBarAtRuntime)
            {
                Debug.LogWarning(
                    $"[{name}] Player Interaction Bar Widget이 " +
                    "비어 있어 우측 Player 바를 생성하지 않습니다.",
                    this);
                return;
            }

            _playerInteractionBarWidget =
                PlayerInteractionBarWidget.CreateRuntime(
                    _playerInteractionBarStyle,
                    out _ownedPlayerInteractionBarCanvas);
        }

        private void BindPlayerInteractionBar()
        {
            EnsurePlayerInteractionBar();
            if (_playerInteractionBarWidget == null)
            {
                return;
            }

            RebindPlayerInteractionBar();
            _playerInteractionBarWidget.PlayerClicked -=
                HandleInteractionBarPlayerClicked;
            _playerInteractionBarWidget.PlayerClicked +=
                HandleInteractionBarPlayerClicked;
            ApplyInteractionPlayer(
                _selectedInteractive,
                false,
                _selectedInteractive != null);
        }

        private void UnbindPlayerInteractionBar()
        {
            if (_playerInteractionBarWidget == null)
            {
                return;
            }

            _playerInteractionBarWidget.PlayerClicked -=
                HandleInteractionBarPlayerClicked;
            _playerInteractionBarWidget.Unbind();
        }

        private void RebindPlayerInteractionBar()
        {
            if (_playerInteractionBarWidget == null)
            {
                return;
            }

            BuildPlayerInteractionSlotData();
            _playerInteractionBarWidget.Bind(
                _playerInteractionSlotData,
                _playerInteractionBarStyle);
        }

        private void BuildPlayerInteractionSlotData()
        {
            _playerInteractionSlotData.Clear();
            var selectedPlayer =
                ResolveSelectedPlayer(_selectedInteractive);

            for (var index = 0;
                 index < _playerPawns.Count;
                 index++)
            {
                var pawn = _playerPawns[index];
                if (pawn == null)
                {
                    continue;
                }

                var definition = pawn.Definition;
                var displayName =
                    definition != null &&
                    !string.IsNullOrWhiteSpace(
                        definition.DisplayName)
                        ? definition.DisplayName
                        : pawn.name;
                var portrait =
                    definition != null ? definition.Portrait : null;

                _playerInteractionSlotData.Add(
                    new PlayerInteractionSlotData(
                        pawn,
                        _playerInteractionSlotData.Count + 1,
                        displayName,
                        portrait,
                        pawn == selectedPlayer));
            }
        }

        private void HandleInteractionBarPlayerClicked(
            InteractivePawn pawn)
        {
            if (pawn != null)
            {
                SelectInteractive(pawn);
            }
        }

        private void ApplyInteractionPlayer(
            InteractivePawn selectedPawn,
            bool animate,
            bool focusCamera)
        {
            var selectedPlayer =
                ResolveSelectedPlayer(selectedPawn);
            _interactionPlayer = selectedPlayer;

            if (_playerInteractionBarWidget != null)
            {
                _playerInteractionBarWidget.SetInteractionTarget(
                    selectedPlayer,
                    animate);
            }

            InteractionPlayerChanged?.Invoke(selectedPlayer);

            if (focusCamera && selectedPlayer != null)
            {
                FocusCameraOnce(selectedPlayer);
            }
        }

        private InteractivePawn ResolveSelectedPlayer(
            InteractivePawn selectedPawn)
        {
            if (selectedPawn == null)
            {
                return null;
            }

            for (var index = 0;
                 index < _playerPawns.Count;
                 index++)
            {
                if (_playerPawns[index] == selectedPawn)
                {
                    return selectedPawn;
                }
            }

            return null;
        }

        private void FocusCameraOnce(InteractivePawn pawn)
        {
            var snapshot = pawn.WorldPosition + _cameraFocusOffset;
            var current = _boardCamera.transform.position;
            var target = new Vector3(
                snapshot.x,
                snapshot.y,
                current.z);

            KillCameraFocusTween();

            var sequence = DOTween.Sequence();
            _cameraFocusTween = sequence;
            sequence.SetUpdate(true);

            if (_cameraStartDelay > 0f)
            {
                sequence.AppendInterval(_cameraStartDelay);
            }

            sequence.Append(
                _boardCamera.transform
                    .DOMove(
                        target,
                        Mathf.Max(0.01f, _cameraFocusDuration))
                    .SetEase(_cameraFocusEase));
            sequence.OnComplete(() =>
            {
                if (_cameraFocusTween == sequence)
                {
                    _cameraFocusTween = null;
                }
            });
        }

        private void KillCameraFocusTween()
        {
            _cameraFocusTween?.Kill();
            _cameraFocusTween = null;
        }

        private void EnsureTurnButton()
        {
            if (_nextTurnButton != null)
            {
                EnsureEventSystem();
                CacheTurnButtonParts();
                return;
            }

            if (!_createTurnButtonAtRuntime)
            {
                return;
            }

            EnsureEventSystem();

            _ownedTurnCanvas = new GameObject(
                "PawnTurnCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas = _ownedTurnCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5100;

            var scaler = _ownedTurnCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _turnUiReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            var buttonObject = new GameObject(
                "NextTurnButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(
                _ownedTurnCanvas.transform,
                false);

            var buttonRect =
                buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = _turnButtonOffset;
            buttonRect.sizeDelta = _turnButtonSize;

            _nextTurnButtonImage = buttonObject.GetComponent<Image>();
            _nextTurnButtonImage.color = _turnButtonColor;

            _nextTurnButton = buttonObject.GetComponent<Button>();
            _nextTurnButton.targetGraphic = _nextTurnButtonImage;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            labelObject.transform.SetParent(buttonRect, false);

            var labelRect =
                labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, -6f);

            _nextTurnButtonLabel = labelObject.GetComponent<Text>();
            _nextTurnButtonLabel.font = GetRuntimeFont();
            _nextTurnButtonLabel.text = "턴 넘기기";
            _nextTurnButtonLabel.fontSize = 24;
            _nextTurnButtonLabel.resizeTextForBestFit = true;
            _nextTurnButtonLabel.resizeTextMinSize = 14;
            _nextTurnButtonLabel.resizeTextMaxSize = 28;
            _nextTurnButtonLabel.alignment =
                TextAnchor.MiddleCenter;
            _nextTurnButtonLabel.color = Color.white;
            _nextTurnButtonLabel.raycastTarget = false;
        }

        private void CacheTurnButtonParts()
        {
            if (_nextTurnButton == null)
            {
                return;
            }

            _nextTurnButtonImage =
                _nextTurnButton.targetGraphic as Image;
            _nextTurnButtonLabel =
                _nextTurnButton.GetComponentInChildren<Text>(true);

            if (_nextTurnButtonLabel != null)
            {
                _nextTurnButtonLabel.text = "턴 넘기기";
            }
        }

        private void BindTurnButton()
        {
            EnsureTurnButton();
            if (_nextTurnButton == null)
            {
                return;
            }

            _nextTurnButton.onClick.RemoveListener(
                HandleNextTurnClicked);
            _nextTurnButton.onClick.AddListener(
                HandleNextTurnClicked);
        }

        private void UnbindTurnButton()
        {
            if (_nextTurnButton != null)
            {
                _nextTurnButton.onClick.RemoveListener(
                    HandleNextTurnClicked);
            }
        }

        private void RefreshTurnButtonState()
        {
            if (_nextTurnButton == null)
            {
                return;
            }

            _nextTurnButton.interactable = _hasCurrentTurnGroup;

            if (_nextTurnButtonImage != null)
            {
                _nextTurnButtonImage.color = _hasCurrentTurnGroup
                    ? _turnButtonColor
                    : new Color(
                        _turnButtonColor.r,
                        _turnButtonColor.g,
                        _turnButtonColor.b,
                        0.45f);
            }

            if (_nextTurnButtonLabel != null)
            {
                _nextTurnButtonLabel.text = "턴 넘기기";
            }
        }

        private static Font GetRuntimeFont()
        {
            Font font = null;

            try
            {
                font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                // Unity 배포판에 따라 내장 폰트 이름이 다를 수 있다.
            }

            return font != null
                ? font
                : Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "Arial" },
                    24);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
        }

        private void ValidateSelectionMaterials()
        {
            if (_defaultMaterial != null && _activeMaterial != null)
            {
                return;
            }

            Debug.LogWarning(
                $"[{name}] Default Material 또는 Active Material이 " +
                "비어 있어 선택 Material 표시가 적용되지 않습니다.",
                this);
        }

        private bool HasRequiredReferences()
        {
            var valid =
                _boardCamera != null &&
                _movementManager != null;

            if (!valid)
            {
                Debug.LogError(
                    $"[{name}] PawnManager 필수 참조가 비어 있습니다.",
                    this);
            }

            return valid;
        }

#if UNITY_EDITOR
        private void OnTransformChildrenChanged()
        {
            if (!Application.isPlaying)
            {
                IndexPawns(false);
                InitializeCurrentTurnGroup();
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                IndexPawns(false);
                InitializeCurrentTurnGroup();
            }
        }
#endif
    }
}
