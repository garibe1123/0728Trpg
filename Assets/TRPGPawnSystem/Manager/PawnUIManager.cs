using System;
using System.Collections.Generic;
using Trpg.Data.Handouts;
using Trpg.Data.Inventory;
using Trpg.Domain.Stats;
using Trpg.UI.Handouts;
using Trpg.UI.Inventory;
using Trpg.UI.Profile;
using Trpg.UI.Skills;
using Trpg.UI.Stats;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 선택 Pawn의 하단 정보 바, 이동, 굴림, 스탯 패널과
    /// HP/MP/SAN 리소스 UI를 연결하는 씬 단위 UI Manager입니다.
    /// </summary>
    public sealed class PawnUIManager : MonoBehaviour
    {
        private const int DefaultCheckTarget =
            PawnRollStats.FallbackCheckTarget;
        private const int MinimumCheckTarget = 1;
        private const int MaximumCheckTarget = 100;

        private static readonly string[] PreferredCocAxisIds =
        {
            "coc.str",
            "coc.con",
            "coc.siz",
            "coc.dex",
            "coc.app",
            "coc.int",
            "coc.pow",
            "coc.edu"
        };

        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private PawnSystemSettings _settings;
        [SerializeField, Tooltip(
            "비어 있으면 런타임에 하단 정보 바를 자동 생성")]
        private PawnInfoBarWidget _infoBar;

        [Header("Inventory")]
        [SerializeField, Tooltip("+ 버튼에서 선택할 공용 Item Definition 카탈로그")]
        private ItemCatalogDefinition _itemCatalog;
        [SerializeField, Tooltip("19종 아이템 종류별 슬롯 아이콘 세트")]
        private InventoryIconSetDefinition _inventoryIconSet;
        [SerializeField, Tooltip("가용 무게 계산에 사용할 스탯 ID")]
        private string _inventoryCapacityStatId = "coc.str";
        [SerializeField, Min(0f), Tooltip("가용 무게 = 스탯 × 배율")]
        private float _inventoryCapacityMultiplier = 1f;
        [SerializeField, Min(0f), Tooltip("스탯을 찾지 못했을 때 가용 무게")]
        private float _inventoryFallbackCapacity;

        [Header("Handouts")]
        [SerializeField, Tooltip("시나리오에 미리 제작한 Handout Definition 카탈로그")]
        private HandoutCatalogDefinition _handoutCatalog;

        [Header("Board Dashboard")]
        [SerializeField, Tooltip(
            "활성화하면 PawnUIManager가 BoardUiStackManager를 자동 설치합니다.")]
        private bool _enableBoardDashboard = true;

        [Header("Movement")]
        [FormerlySerializedAs("_moveButton")]
        [FormerlySerializedAs("_movementButton")]
        [FormerlySerializedAs("walkButton")]
        [SerializeField, Tooltip(
            "선택한 Pawn의 이동 모드를 시작하는 걷기 버튼")]
        private Button _walkButton;

        [Header("Roll")]
        [SerializeField, Tooltip(
            "기존 판정 굴림에서 사용할 기본 능력치 ID")]
        private string _checkStatId = PawnRollStats.DefaultStatId;

        [SerializeField, Min(1), Tooltip("효과 굴림의 주사위 개수")]
        private int _effectDiceCount = 2;

        [SerializeField, Min(2), Tooltip("효과 굴림의 주사위 면수")]
        private int _effectDiceSides = 6;

        [SerializeField, Tooltip("효과 굴림 최종 합계에 더할 보정치")]
        private int _effectDiceModifier;

        [Header("Roll Result Colors")]
        [SerializeField] private Color _criticalColor =
            new Color(0.20f, 0.95f, 0.38f);
        [SerializeField] private Color _successColor =
            new Color(0.24f, 0.84f, 1f);
        [SerializeField] private Color _failureColor =
            new Color(1f, 0.36f, 0.28f);
        [SerializeField] private Color _fumbleColor = Color.black;
        [SerializeField] private Color _effectColor =
            new Color(1f, 0.78f, 0.22f);

        private PawnRollService _rollService;
        private PlayerStatState _boundStatState;
        private PlayerSkillState _boundSkillState;
        private PlayerInventoryState _boundInventoryState;
        private PawnInventoryWidget _inventoryWidget;
        private PawnProfileState _boundProfileState;
        private PawnProfileWidget _profileWidget;
        private PawnConditionWidget _conditionWidget;
        private PawnProfileSection _conditionSection =
            PawnProfileSection.OtherNotes;
        private string _conditionTitle = "상태 관리";
        private PublicHandoutState _publicHandoutState;
        private PublicHandoutWidget _handoutWidget;
        private Button _handoutButton;
        private RectTransform _handoutButtonRect;
        private string _boundDisplayName;
        private Sprite _boundPortrait;
        private bool _isRollInProgress;
        private BoardUiStackManager _boardStackManager;
        private TRPGNetworkGameManager _networkGameManager;
        private bool _deferredStatRefresh;
        private bool _deferredProfileRefresh;
        private bool _boundCharacterCanEdit;
        private bool _boundCharacterCanRoll;
        private bool _boundCharacterCanReroll;
        private bool _boundHasFullCharacterSheet;
        private bool _boundSupportsSkills;
        private bool _boundSupportsInventory;
        private bool _boundSupportsProfile;
        private bool _hasRuntimeRuleSet;
        private CampaignRuleSet _runtimeRuleSet;

        public PawnInfoBarWidget InfoBar => _infoBar;
        public PawnManager PawnManager => _pawnManager;
        public PlayerStatState BoundStatState => _boundStatState;
        public PlayerSkillState BoundSkillState => _boundSkillState;
        public PlayerInventoryState BoundInventoryState => _boundInventoryState;
        public PawnProfileState BoundProfileState => _boundProfileState;
        public string BoundDisplayName => _boundDisplayName;
        public Sprite BoundPortrait => _boundPortrait;
        public bool HasBoardStack => _boardStackManager != null;
        public int EffectDiceCount => _effectDiceCount;
        public int EffectDiceSides => _effectDiceSides;
        public int EffectDiceModifier => _effectDiceModifier;
        public bool CanCurrentCharacterRoll =>
            _boundCharacterCanRoll;
        public CampaignRuleSet RuleSet => _hasRuntimeRuleSet
            ? _runtimeRuleSet
            : _settings != null
                ? _settings.RuleSet
                : CampaignRuleSet.Generic;
        public bool UsesCallOfCthulhuRules =>
            RuleSet == CampaignRuleSet.CallOfCthulhu7E;

        public void ApplyCampaignRuleSet(CampaignRuleSet ruleSet)
        {
            _runtimeRuleSet = ruleSet;
            _hasRuntimeRuleSet = true;
            _infoBar?.SetUsesCallOfCthulhuRules(
                UsesCallOfCthulhuRules);
            RefreshStatUi();

            var authority = TRPGSessionAuthority.Instance;
            if (authority != null &&
                authority.IsOnline &&
                authority.IsLocalGameMaster)
            {
                authority.PublishHostCampaignRuleSet(ruleSet);
            }
        }

        public void ConfigureNetworkManager(
            TRPGNetworkGameManager networkGameManager)
        {
            _networkGameManager = networkGameManager;
        }

        public void FlushPendingEdits()
        {
            _infoBar?.FlushPendingStatEdits();
            _profileWidget?.FlushPendingEdit();
        }

        public void RegisterBoardStack(BoardUiStackManager manager)
        {
            _boardStackManager = manager;
            _inventoryWidget?.Hide();
            _profileWidget?.Hide();
            _infoBar?.SetBoardStackMode(manager != null);
        }

        public void UnregisterBoardStack(BoardUiStackManager manager)
        {
            if (_boardStackManager != manager)
                return;

            HideInventoryFromBoardStack();
            HideProfileFromBoardStack();
            _boardStackManager = null;
            _infoBar?.SetBoardStackMode(false);
        }

        public bool ShowInventoryInBoardStack(RectTransform host)
        {
            if (host == null ||
                !_boundSupportsInventory ||
                _boundInventoryState == null)
            {
                return false;
            }

            EnsureInventoryWidget();
            if (_inventoryWidget == null)
                return false;

            RefreshInventoryUi();
            _inventoryWidget.SetReadOnly(
                !_boundCharacterCanEdit);
            _inventoryWidget.SetEmbeddedMode(host, true);
            _inventoryWidget.Show();
            return true;
        }

        public void HideInventoryFromBoardStack()
        {
            if (_inventoryWidget == null)
                return;
            _inventoryWidget.Hide();
            _inventoryWidget.SetEmbeddedMode(null, false);
        }

        public bool ShowProfileInBoardStack(RectTransform host)
        {
            if (host == null ||
                !_boundSupportsProfile ||
                _boundProfileState == null)
            {
                return false;
            }

            EnsureProfileWidget();
            if (_profileWidget == null)
                return false;

            _profileWidget.Bind(_boundProfileState, _boundDisplayName);
            _profileWidget.SetReadOnly(
                !_boundCharacterCanEdit);
            _profileWidget.SetEmbeddedMode(host, true);
            _profileWidget.Show(null);
            return true;
        }

        public void HideProfileFromBoardStack()
        {
            if (_profileWidget == null)
                return;
            _profileWidget.FlushPendingEdit();
            _profileWidget.Hide();
            _profileWidget.SetEmbeddedMode(null, false);
        }

        private void Awake()
        {
            if (_pawnManager == null || _settings == null)
            {
                Debug.LogError(
                    $"[{name}] PawnUIManager 필수 참조가 비어 있습니다.",
                    this);
                enabled = false;
                return;
            }

            if (_infoBar == null)
            {
                _infoBar = PawnInfoBarWidget.CreateRuntime(_settings);
            }

            _infoBar.SetUsesCallOfCthulhuRules(
                UsesCallOfCthulhuRules);

            var seed = unchecked(
                Environment.TickCount * 397 ^ GetInstanceID());
            _rollService = new PawnRollService(seed);
            _publicHandoutState = PublicHandoutState.ResolveOrCreate(
                gameObject,
                _handoutCatalog);
            EnsureBoardDashboard();
        }

        private void EnsureBoardDashboard()
        {
            if (!_enableBoardDashboard)
                return;

#if UNITY_2023_1_OR_NEWER
            var existing = UnityEngine.Object.FindObjectsByType<
                BoardUiStackManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            var existing = UnityEngine.Object.FindObjectsOfType<
                BoardUiStackManager>(true);
#endif
            for (var index = 0; index < existing.Length; index++)
            {
                var manager = existing[index];
                if (manager != null && manager.gameObject != gameObject)
                    manager.enabled = false;
            }

            var local = GetComponent<BoardUiStackManager>();
            if (local == null)
                local = gameObject.AddComponent<BoardUiStackManager>();
            local.Configure(this);
        }

        private void OnEnable()
        {
            if (_pawnManager == null || _infoBar == null)
            {
                return;
            }

            _pawnManager.InteractiveSelectionChanged +=
                HandleInteractiveSelectionChanged;
            _pawnManager.MovementManager.MovementBudgetChanged +=
                HandleMovementBudgetChanged;
            _pawnManager.MovementManager.PathPreviewChanged +=
                HandlePathPreviewChanged;
            _pawnManager.MovementManager.MovementRangeChanged +=
                HandleMovementRangeChanged;

            _infoBar.CloseRequested += HandleCloseRequested;
            _infoBar.MoveRequested += StartWalking;
            _infoBar.CheckRollRequested += HandleCheckRollRequested;
            _infoBar.StatRerollRequested +=
                HandleStatRerollRequested;
            _infoBar.EffectRollRequested += HandleEffectRollRequested;
            _infoBar.ResourceRollRequested +=
                HandleResourceRollRequested;
            _infoBar.ResourceConditionRequested +=
                HandleResourceConditionRequested;
            _infoBar.RollPresentationCompleted +=
                HandleRollPresentationCompleted;
            _infoBar.StatValueEditRequested +=
                HandleStatValueEditRequested;
            _infoBar.PlayerSkillAddRequested +=
                HandleSkillAddRequested;
            _infoBar.PlayerSkillNameEditRequested +=
                HandleSkillNameEditRequested;
            _infoBar.PlayerSkillRegularEditRequested +=
                HandleSkillRegularEditRequested;
            _infoBar.PlayerSkillRemoveRequested +=
                HandleSkillRemoveRequested;
            _infoBar.InventoryRequested +=
                HandleInventoryRequested;
            _infoBar.ProfileRequested +=
                HandleProfileRequested;
            _infoBar.HiddenToggleRequested +=
                HandleHiddenToggleRequested;
            _infoBar.DeathToggleRequested +=
                HandleDeathToggleRequested;

            BindHandoutSystem();
            BindWalkButton();
            HandleInteractiveSelectionChanged(
                _pawnManager.SelectedInteractive);
        }

        private void LateUpdate()
        {
            if (_deferredStatRefresh &&
                (_infoBar == null || !_infoBar.HasActiveStatEdit))
            {
                _deferredStatRefresh = false;
                RefreshStatUi();
                RefreshInventoryUi();
            }

            if (_deferredProfileRefresh &&
                (_profileWidget == null ||
                 !_profileWidget.HasActiveEdit))
            {
                _deferredProfileRefresh = false;
                if (_profileWidget != null &&
                    _profileWidget.IsVisible)
                {
                    _profileWidget.RefreshFromState();
                }
            }
        }

        private void OnDisable()
        {
            FlushPendingEdits();
            if (_pawnManager != null)
            {
                _pawnManager.InteractiveSelectionChanged -=
                    HandleInteractiveSelectionChanged;
                _pawnManager.MovementManager.MovementBudgetChanged -=
                    HandleMovementBudgetChanged;
                _pawnManager.MovementManager.PathPreviewChanged -=
                    HandlePathPreviewChanged;
                _pawnManager.MovementManager.MovementRangeChanged -=
                    HandleMovementRangeChanged;
            }

            if (_infoBar != null)
            {
                _infoBar.CloseRequested -= HandleCloseRequested;
                _infoBar.MoveRequested -= StartWalking;
                _infoBar.CheckRollRequested -= HandleCheckRollRequested;
                _infoBar.StatRerollRequested -=
                    HandleStatRerollRequested;
                _infoBar.EffectRollRequested -= HandleEffectRollRequested;
                _infoBar.ResourceRollRequested -=
                    HandleResourceRollRequested;
                _infoBar.ResourceConditionRequested -=
                    HandleResourceConditionRequested;
                _infoBar.RollPresentationCompleted -=
                    HandleRollPresentationCompleted;
                _infoBar.StatValueEditRequested -=
                    HandleStatValueEditRequested;
                _infoBar.PlayerSkillAddRequested -=
                    HandleSkillAddRequested;
                _infoBar.PlayerSkillNameEditRequested -=
                    HandleSkillNameEditRequested;
                _infoBar.PlayerSkillRegularEditRequested -=
                    HandleSkillRegularEditRequested;
                _infoBar.PlayerSkillRemoveRequested -=
                    HandleSkillRemoveRequested;
                _infoBar.InventoryRequested -=
                    HandleInventoryRequested;
                _infoBar.ProfileRequested -=
                    HandleProfileRequested;
                _infoBar.HiddenToggleRequested -=
                    HandleHiddenToggleRequested;
                _infoBar.DeathToggleRequested -=
                    HandleDeathToggleRequested;
                _infoBar.Hide();
            }

            UnbindStatState();
            UnbindSkillState();
            UnbindInventoryState();
            if (_boardStackManager != null)
                HideInventoryFromBoardStack();
            else
                _inventoryWidget?.Hide();
            UnbindProfileState();
            if (_boardStackManager != null)
                HideProfileFromBoardStack();
            else
                _profileWidget?.Hide();
            UnbindHandoutSystem();
            _handoutWidget?.Hide();
            _conditionWidget?.Hide();
            if (_handoutButton != null)
                _handoutButton.gameObject.SetActive(false);
            UnbindWalkButton();
            _isRollInProgress = false;
            PlayerStatState.SetActive(null);
        }

        private void OnDestroy()
        {
            if (_inventoryWidget != null)
            {
                Destroy(_inventoryWidget.gameObject);
                _inventoryWidget = null;
            }

            if (_profileWidget != null)
            {
                _profileWidget.CloseRequested -=
                    HandleProfileCloseRequested;
                _profileWidget.Applied -=
                    HandleProfileApplied;
                _profileWidget.ValueEditRequested -=
                    HandleProfileValueEditRequested;
                Destroy(_profileWidget.gameObject);
                _profileWidget = null;
            }


            if (_conditionWidget != null)
            {
                _conditionWidget.AddRequested -=
                    HandleConditionAddRequested;
                _conditionWidget.RemoveRequested -=
                    HandleConditionRemoveRequested;
                _conditionWidget.CloseRequested -=
                    HandleConditionCloseRequested;
                Destroy(_conditionWidget.gameObject);
                _conditionWidget = null;
            }

            if (_handoutWidget != null)
            {
                _handoutWidget.AddRequested -=
                    HandleHandoutAddRequested;
                _handoutWidget.RemoveRequested -=
                    HandleHandoutRemoveRequested;
                _handoutWidget.MoveRequested -=
                    HandleHandoutMoveRequested;
                _handoutWidget.Opened -= HandleHandoutOpened;
                _handoutWidget.CloseRequested -=
                    HandleHandoutCloseRequested;
                Destroy(_handoutWidget.gameObject);
                _handoutWidget = null;
            }

            if (_handoutButton != null)
            {
                Destroy(_handoutButton.gameObject);
                _handoutButton = null;
                _handoutButtonRect = null;
            }
        }

        private void HandleHiddenToggleRequested()
        {
            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (pawn == null ||
                pawn.Definition == null ||
                !pawn.Definition.IsNpc ||
                !IsLocalGameMasterOrOffline())
            {
                return;
            }

            var authority = TRPGSessionAuthority.Instance;
            if (authority != null && authority.IsOnline)
            {
                authority.SetHostPawnHidden(
                    pawn,
                    !pawn.IsHidden);
                return;
            }

            var next = !pawn.IsHidden;
            pawn.SetRuntimeState(next, pawn.IsDead);
            PawnRollLogService.RecordAction(
                pawn,
                next ? "NPC 숨김" : "NPC 숨김 해제",
                next
                    ? "GM이 보드에서 NPC를 숨겼습니다."
                    : "GM이 보드에 NPC를 다시 표시했습니다.");
        }

        private void HandleDeathToggleRequested()
        {
            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (pawn == null ||
                pawn.Definition == null ||
                !pawn.Definition.IsNpc ||
                !IsLocalGameMasterOrOffline())
            {
                return;
            }

            var authority = TRPGSessionAuthority.Instance;
            if (authority != null && authority.IsOnline)
            {
                authority.SetHostPawnDead(
                    pawn,
                    !pawn.IsDead);
                return;
            }

            var next = !pawn.IsDead;
            pawn.SetRuntimeState(pawn.IsHidden, next);
            PawnRollLogService.RecordAction(
                pawn,
                next ? "NPC 사망처리" : "NPC 사망 해제",
                next
                    ? "GM이 NPC를 사망 상태로 처리했습니다."
                    : "GM이 NPC의 사망 상태를 해제했습니다.");
        }

        private void HandleCloseRequested()
        {
            FlushPendingEdits();
            _inventoryWidget?.Hide();
            _profileWidget?.Hide();
            _pawnManager.ClearSelection();
        }

        private void HandleInteractiveSelectionChanged(
            InteractivePawn pawn)
        {
            FlushPendingEdits();
            _infoBar?.CancelRollPresentation();
            _conditionWidget?.Hide();
            _deferredStatRefresh = false;
            _deferredProfileRefresh = false;
            _boundCharacterCanEdit = false;
            _boundCharacterCanRoll = false;
            _boundCharacterCanReroll = false;
            _boundHasFullCharacterSheet = false;
            _boundSupportsSkills = false;
            _boundSupportsInventory = false;
            _boundSupportsProfile = false;
            UnbindStatState();
            UnbindSkillState();
            UnbindInventoryState();
            if (_boardStackManager != null)
                HideInventoryFromBoardStack();
            else
                _inventoryWidget?.Hide();
            UnbindProfileState();
            if (_boardStackManager != null)
                HideProfileFromBoardStack();
            else
                _profileWidget?.Hide();

            if (pawn == null || pawn.Definition == null)
            {
                PlayerStatState.SetActive(null);
                _infoBar.SetResourceConditionEditingEnabled(false);
                _infoBar.Unbind();
                RefreshWalkButton(null);
                RefreshRollButtons(null);
                RefreshHandoutUi();
                return;
            }

            var definition = pawn.Definition;
            var isLocalGameMaster = IsLocalGameMasterOrOffline();
            var showMovement = CanViewMovementInformation(pawn);
            _boundHasFullCharacterSheet = CanViewStats(pawn);
            _boundSupportsSkills =
                _boundHasFullCharacterSheet &&
                definition.SupportsSkills;
            _boundSupportsInventory =
                _boundHasFullCharacterSheet &&
                definition.SupportsInventory;
            _boundSupportsProfile =
                _boundHasFullCharacterSheet &&
                definition.SupportsProfile;
            _boundCharacterCanEdit = CanEditCharacter(pawn);
            _boundCharacterCanRoll = CanRollCharacter(pawn);
            _boundCharacterCanReroll = CanRerollCharacter(pawn);
            var displayName =
                string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? pawn.name
                    : definition.DisplayName;
            var showGmInstructions =
                isLocalGameMaster && definition.IsNpc;
            var showGmStateControls =
                isLocalGameMaster && definition.IsNpc;
            var infoData = new PawnInfoBarData(
                displayName,
                definition.Description,
                showGmInstructions
                    ? definition.GmInstructions
                    : string.Empty,
                definition.Portrait,
                showMovement ? definition.MovementScore : 0,
                definition.HasIdentityDetail,
                showGmInstructions,
                showMovement,
                _boundHasFullCharacterSheet,
                _boundSupportsSkills,
                _boundSupportsInventory,
                _boundSupportsProfile,
                showGmStateControls,
                pawn.IsHidden,
                pawn.IsDead,
                _boundCharacterCanRoll,
                CanMoveCharacter(pawn));

            _infoBar.Bind(infoData);
            _infoBar.SetResourceConditionEditingEnabled(
                _boundCharacterCanEdit);

            if (!_boundHasFullCharacterSheet)
            {
                PlayerStatState.SetActive(null);
                _infoBar.ClearStats();
                RefreshMovementBudget(pawn);
                RefreshWalkButton(pawn);
                RefreshRollButtons(pawn);
                RefreshHandoutUi();
                return;
            }

            PlayerStatState.SetActiveFrom(pawn.gameObject);
            var statState = ResolveStatState(pawn);
            if (statState != null)
            {
                BindStatState(
                    statState,
                    displayName,
                    definition.Portrait);
                if (_boundSupportsSkills)
                {
                    BindSkillState(
                        PlayerSkillState.ResolveOrCreate(
                            pawn.gameObject,
                            definition));
                }
                RefreshStatUi();
            }
            else
            {
                _infoBar.ClearStats();
            }

            if (_boundSupportsInventory)
            {
                BindInventoryState(
                    PlayerInventoryState.ResolveOrCreate(
                        pawn.gameObject,
                        definition,
                        _itemCatalog));
            }

            if (_boundSupportsProfile)
            {
                BindProfileState(
                    PawnProfileState.ResolveOrCreate(
                        pawn.gameObject,
                        definition));
            }

            RefreshMovementBudget(pawn);
            RefreshWalkButton(pawn);
            RefreshRollButtons(pawn);
            RefreshHandoutUi();
        }

        public void StartWalking()
        {
            if (_pawnManager == null ||
                !CanMoveCharacter(
                    _pawnManager.SelectedInteractive))
            {
                return;
            }

            _pawnManager.SetMovementMode(true);
            RefreshWalkButton(_pawnManager.SelectedInteractive);
        }

        public void SetCheckStatId(string statId)
        {
            _checkStatId = string.IsNullOrWhiteSpace(statId)
                ? PawnRollStats.DefaultStatId
                : statId.Trim();
            RefreshRollButtons(_pawnManager.SelectedInteractive);
        }

        public void SetEffectDiceExpression(
            int diceCount,
            int sides,
            int modifier = 0)
        {
            _effectDiceCount = Mathf.Clamp(
                diceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _effectDiceSides = Mathf.Clamp(
                sides,
                2,
                PawnRollService.MaximumDiceSides);
            _effectDiceModifier = modifier;
            RefreshRollButtons(_pawnManager.SelectedInteractive);
        }

        public void RollEffectFromBoardStack(
            PawnEffectRollRequest request)
        {
            HandleEffectRollRequested(request);
        }

        private void BindStatState(
            PlayerStatState statState,
            string displayName,
            Sprite portrait)
        {
            if (statState == null)
            {
                _infoBar.ClearStats();
                return;
            }

            if (!statState.IsInitialized)
            {
                statState.Initialize();
            }

            if (!statState.IsInitialized)
            {
                _infoBar.ClearStats();
                return;
            }

            _boundStatState = statState;
            _boundDisplayName = displayName;
            _boundPortrait = portrait;
            _boundStatState.Changed -= HandleBoundStatStateChanged;
            _boundStatState.Changed += HandleBoundStatStateChanged;
        }

        private void UnbindStatState()
        {
            if (_boundStatState != null)
            {
                _boundStatState.Changed -= HandleBoundStatStateChanged;
            }

            _boundStatState = null;
            _boundDisplayName = string.Empty;
            _boundPortrait = null;
        }

        private void BindSkillState(PlayerSkillState skillState)
        {
            if (skillState == null)
                return;

            if (!skillState.IsInitialized)
                skillState.Initialize();
            if (!skillState.IsInitialized)
                return;

            _boundSkillState = skillState;
            _boundSkillState.Changed -= HandleBoundSkillStateChanged;
            _boundSkillState.Changed += HandleBoundSkillStateChanged;
        }

        private void UnbindSkillState()
        {
            if (_boundSkillState != null)
            {
                _boundSkillState.Changed -= HandleBoundSkillStateChanged;
            }

            _boundSkillState = null;
        }

        private void BindInventoryState(
            PlayerInventoryState inventoryState)
        {
            if (inventoryState == null)
                return;

            if (!inventoryState.IsInitialized)
                inventoryState.Initialize();
            if (!inventoryState.IsInitialized)
                return;

            _boundInventoryState = inventoryState;
            _boundInventoryState.Changed -=
                HandleBoundInventoryStateChanged;
            _boundInventoryState.Changed +=
                HandleBoundInventoryStateChanged;
        }

        private void UnbindInventoryState()
        {
            if (_boundInventoryState != null)
            {
                _boundInventoryState.Changed -=
                    HandleBoundInventoryStateChanged;
            }

            _boundInventoryState = null;
        }

        private void BindProfileState(PawnProfileState profileState)
        {
            if (profileState == null)
                return;

            if (!profileState.IsInitialized)
                profileState.Initialize();
            if (!profileState.IsInitialized)
                return;

            _boundProfileState = profileState;
            _boundProfileState.Changed -=
                HandleBoundProfileStateChanged;
            _boundProfileState.Changed +=
                HandleBoundProfileStateChanged;
        }

        private void UnbindProfileState()
        {
            if (_boundProfileState != null)
            {
                _boundProfileState.Changed -=
                    HandleBoundProfileStateChanged;
            }

            _boundProfileState = null;
        }

        private void HandleBoundProfileStateChanged()
        {
            RefreshConditionWidget();
            if (_profileWidget == null ||
                !_profileWidget.IsVisible)
            {
                return;
            }

            if (_profileWidget.HasActiveEdit)
            {
                _deferredProfileRefresh = true;
                return;
            }

            _deferredProfileRefresh = false;
            _profileWidget.RefreshFromState();
        }

        private void HandleBoundInventoryStateChanged()
        {
            RefreshInventoryUi();
        }

        private void HandleBoundStatStateChanged()
        {
            if (_infoBar != null && _infoBar.HasActiveStatEdit)
            {
                _deferredStatRefresh = true;
                return;
            }

            _deferredStatRefresh = false;
            RefreshStatUi();
            RefreshInventoryUi();
        }

        private void HandleBoundSkillStateChanged()
        {
            RefreshStatUi();
        }

        private void HandleStatValueEditRequested(
            string statId,
            double value)
        {
            if (!_boundCharacterCanEdit)
            {
                RefreshStatUi();
                return;
            }

            if (_boundStatState == null ||
                string.IsNullOrWhiteSpace(statId))
            {
                return;
            }

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;

            var authority = TRPGSessionAuthority.Instance;
            var shouldRouteClientStat =
                (_networkGameManager != null &&
                 _networkGameManager.ShouldRouteClientStatChange) ||
                (authority != null &&
                 authority.ShouldRouteClientStatChange);

            if (shouldRouteClientStat)
            {
                var requested =
                    _networkGameManager != null &&
                    _networkGameManager.RequestStatChange(
                        pawn,
                        statId,
                        value);

                if (!requested && authority != null)
                {
                    requested = authority.RequestStatChange(
                        pawn,
                        statId,
                        value);
                }

                if (!requested)
                    RefreshStatUi();

                return;
            }

            var previous = TryGetDisplayedStatValue(
                _boundStatState,
                statId,
                out var previousValue)
                    ? previousValue
                    : value;

            if (!_boundStatState.TrySetAuthoritativeDisplayedValue(
                    statId,
                    value))
            {
                Debug.LogWarning(
                    $"[{name}] 표시 스탯 값을 변경하지 못했습니다. " +
                    $"StatId={statId}, Value={value}",
                    _boundStatState);
                RefreshStatUi();
                return;
            }

            var current = TryGetDisplayedStatValue(
                _boundStatState,
                statId,
                out var currentValue)
                    ? currentValue
                    : value;

            var sessionAuthority =
                TRPGSessionAuthority.Instance;

            if (sessionAuthority != null)
            {
                sessionAuthority.PublishHostStatChange(
                    pawn,
                    statId,
                    previous,
                    current);
            }
            else
            {
                _networkGameManager?.PublishHostStatChange(
                    pawn,
                    statId,
                    previous,
                    current);
            }
        }

        private static bool TryGetDisplayedStatValue(
            PlayerStatState statState,
            string statId,
            out double value)
        {
            value = 0d;
            if (statState?.Runtime == null ||
                string.IsNullOrWhiteSpace(statId))
            {
                return false;
            }

            try
            {
                value = statState.Runtime.GetNumber(statId);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void HandleSkillAddRequested(
            PawnSkillAddRequest request)
        {
            if (!_boundCharacterCanEdit)
                return;

            if (_boundSkillState == null)
                return;

            string error;
            var succeeded = string.IsNullOrWhiteSpace(request.SkillId)
                ? _boundSkillState.TryAddCustom(
                    "새 스킬",
                    request.RegularValue,
                    out _,
                    out error)
                : _boundSkillState.TryAdd(
                    request.SkillId,
                    request.RegularValue,
                    out error);

            if (!succeeded)
            {
                Debug.LogWarning(
                    $"[{name}] 스킬을 추가하지 못했습니다. {error}",
                    _boundSkillState);
                RefreshStatUi();
                return;
            }

            PublishSkillStateChange();
        }

        private void HandleSkillNameEditRequested(
            PawnSkillNameEditRequest request)
        {
            if (!_boundCharacterCanEdit)
                return;

            if (_boundSkillState == null ||
                !_boundSkillState.TrySetDisplayName(
                    request.SkillId,
                    request.DisplayName))
            {
                RefreshStatUi();
                return;
            }

            PublishSkillStateChange();
        }

        private void HandleSkillRegularEditRequested(
            PawnSkillRegularEditRequest request)
        {
            if (!_boundCharacterCanEdit)
                return;

            if (_boundSkillState == null ||
                !_boundSkillState.TrySetRegularValue(
                    request.SkillId,
                    request.RegularValue))
            {
                RefreshStatUi();
                return;
            }

            PublishSkillStateChange();
        }

        private void HandleSkillRemoveRequested(
            PawnSkillRemoveRequest request)
        {
            if (!_boundCharacterCanEdit)
                return;

            if (_boundSkillState == null ||
                !_boundSkillState.TryRemove(request.SkillId))
            {
                RefreshStatUi();
                return;
            }

            PublishSkillStateChange();
        }

        private void PublishSkillStateChange()
        {
            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            var authority = TRPGSessionAuthority.Instance;
            if (pawn == null || authority == null || !authority.IsOnline)
                return;

            if (authority.ShouldRouteClientSkillChange)
            {
                authority.RequestSkillSnapshot(pawn);
                return;
            }

            if (authority.IsLocalGameMaster)
                authority.PublishHostSkillSnapshot(pawn);
        }

        private void BindHandoutSystem()
        {
            if (_publicHandoutState == null)
            {
                _publicHandoutState = PublicHandoutState.ResolveOrCreate(
                    gameObject,
                    _handoutCatalog);
            }
            else
            {
                _publicHandoutState.Configure(_handoutCatalog);
                _publicHandoutState.Initialize();
            }

            if (_publicHandoutState != null)
            {
                _publicHandoutState.Changed -=
                    HandlePublicHandoutStateChanged;
                _publicHandoutState.Changed +=
                    HandlePublicHandoutStateChanged;
            }

            EnsureHandoutButton();
            if (_handoutButton != null)
            {
                _handoutButton.onClick.RemoveListener(
                    HandleHandoutButtonClicked);
                _handoutButton.onClick.AddListener(
                    HandleHandoutButtonClicked);
                _handoutButton.gameObject.SetActive(true);
            }
            RefreshHandoutUi();
        }

        private void UnbindHandoutSystem()
        {
            if (_publicHandoutState != null)
            {
                _publicHandoutState.Changed -=
                    HandlePublicHandoutStateChanged;
            }

            if (_handoutButton != null)
            {
                _handoutButton.onClick.RemoveListener(
                    HandleHandoutButtonClicked);
            }
        }

        private void HandlePublicHandoutStateChanged()
        {
            RefreshHandoutUi();
        }

        private void EnsureHandoutButton()
        {
            if (_handoutButton != null || _infoBar == null)
                return;

            var canvas = _infoBar.GetComponentInParent<Canvas>();
            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            if (rootCanvas == null)
            {
                Debug.LogError(
                    $"[{name}] 핸드아웃 버튼을 생성할 Root Canvas를 " +
                    "찾지 못했습니다.",
                    this);
                return;
            }

            var buttonObject = new GameObject(
                "PublicHandoutButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            _handoutButtonRect =
                buttonObject.GetComponent<RectTransform>();
            _handoutButtonRect.SetParent(rootCanvas.transform, false);
            _handoutButtonRect.anchorMin = new Vector2(1f, 0f);
            _handoutButtonRect.anchorMax = new Vector2(1f, 0f);
            _handoutButtonRect.pivot = new Vector2(1f, 0f);
            _handoutButtonRect.anchoredPosition =
                new Vector2(-24f, 24f);
            _handoutButtonRect.sizeDelta = new Vector2(148f, 46f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.07f, 0.16f, 0.20f, 0.98f);
            _handoutButton = buttonObject.GetComponent<Button>();
            _handoutButton.targetGraphic = image;
            _handoutButton.transition = Selectable.Transition.ColorTint;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(_handoutButtonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(5f, 2f);
            labelRect.offsetMax = new Vector2(-5f, -2f);

            var label = labelObject.GetComponent<Text>();
            var sourceText = _infoBar.GetComponentInChildren<Text>(true);
            label.font = sourceText != null
                ? sourceText.font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            label.fontSize = 17;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "핸드아웃";
            label.raycastTarget = false;
            buttonObject.transform.SetAsLastSibling();
        }

        private void HandleHandoutButtonClicked()
        {
            if (_publicHandoutState == null)
                return;

            EnsureHandoutWidget();
            if (_handoutWidget == null)
                return;

            if (_handoutWidget.IsVisible)
            {
                _handoutWidget.Hide();
                return;
            }

            RefreshHandoutUi();
            _handoutWidget.Show(_handoutButtonRect);
        }

        private void EnsureHandoutWidget()
        {
            if (_handoutWidget != null || _infoBar == null)
                return;

            var canvas = _infoBar.GetComponentInParent<Canvas>();
            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            if (rootCanvas == null)
            {
                Debug.LogError(
                    $"[{name}] 핸드아웃 UI를 생성할 Root Canvas를 " +
                    "찾지 못했습니다.",
                    this);
                return;
            }

            var text = _infoBar.GetComponentInChildren<Text>(true);
            _handoutWidget = PublicHandoutWidget.CreateRuntime(
                rootCanvas,
                text != null ? text.font : null);
            _handoutWidget.AddRequested +=
                HandleHandoutAddRequested;
            _handoutWidget.RemoveRequested +=
                HandleHandoutRemoveRequested;
            _handoutWidget.MoveRequested +=
                HandleHandoutMoveRequested;
            _handoutWidget.Opened +=
                HandleHandoutOpened;
            _handoutWidget.CloseRequested +=
                HandleHandoutCloseRequested;
        }

        private void HandleHandoutAddRequested(
            HandoutDefinition definition)
        {
            if (_publicHandoutState == null)
                return;

            var authority = TRPGSessionAuthority.Instance;
            if (authority != null &&
                authority.IsOnline &&
                !authority.IsLocalGameMaster)
            {
                Debug.LogWarning(
                    $"[{name}] 핸드아웃 공개는 GM만 변경할 수 있습니다.",
                    this);
                RefreshHandoutUi();
                return;
            }

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (!_publicHandoutState.TryGrantToPawn(
                    pawn,
                    definition,
                    out var error))
            {
                Debug.LogWarning(
                    $"[{name}] 선택 Pawn에게 핸드아웃을 공개하지 " +
                    $"못했습니다. {error}",
                    this);
                RefreshHandoutUi();
                return;
            }

            if (authority != null && authority.IsOnline)
            {
                authority.PublishHostHandoutRecord(
                    pawn,
                    definition != null ? definition.Id : string.Empty);
            }

            RefreshHandoutUi();
        }

        private void HandleHandoutRemoveRequested(string definitionId)
        {
            var authority = TRPGSessionAuthority.Instance;
            if (authority != null &&
                authority.IsOnline &&
                !authority.IsLocalGameMaster)
            {
                Debug.LogWarning(
                    $"[{name}] 핸드아웃 공개는 GM만 변경할 수 있습니다.",
                    this);
                RefreshHandoutUi();
                return;
            }

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (_publicHandoutState == null ||
                !_publicHandoutState.TryRevokeFromPawn(
                    pawn,
                    definitionId))
            {
                RefreshHandoutUi();
                return;
            }

            if (authority != null && authority.IsOnline)
            {
                authority.PublishHostHandoutRecord(
                    pawn,
                    definitionId);
            }

            RefreshHandoutUi();
        }

        private void HandleHandoutMoveRequested(
            string definitionId,
            int targetIndex)
        {
            if (_publicHandoutState == null ||
                !_publicHandoutState.TryMove(
                    definitionId,
                    targetIndex))
            {
                RefreshHandoutUi();
            }
        }

        private void HandleHandoutOpened(string definitionId)
        {
            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (_publicHandoutState == null || pawn == null)
                return;

            var authority = TRPGSessionAuthority.Instance;
            if (authority == null || !authority.IsOnline)
            {
                _publicHandoutState.MarkOpened(pawn, definitionId);
                return;
            }

            // GM의 미리보기는 조사자의 열람 기록으로 취급하지 않습니다.
            if (authority.IsLocalGameMaster)
                return;

            _publicHandoutState.MarkOpened(pawn, definitionId);
            authority.RequestHandoutOpened(pawn, definitionId);
        }

        private void HandleHandoutCloseRequested()
        {
            _handoutWidget?.Hide();
        }

        private void RefreshHandoutUi()
        {
            if (_handoutWidget == null ||
                _publicHandoutState == null)
            {
                return;
            }

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            var contextId = pawn != null
                ? !string.IsNullOrWhiteSpace(pawn.InstanceId)
                    ? pawn.InstanceId
                    : pawn.Definition != null
                        ? pawn.Definition.Id
                        : string.Empty
                : string.Empty;
            _handoutWidget.Bind(
                _publicHandoutState.GetAvailableForPawn(pawn),
                _handoutCatalog,
                contextId);
        }

        private void HandleProfileRequested()
        {
            if (!_boundSupportsProfile)
                return;

            if (_boardStackManager != null)
            {
                _boardStackManager.RequestTab(SheetTab.Profile);
                return;
            }

            if (_boundProfileState == null)
                return;

            EnsureProfileWidget();
            if (_profileWidget == null)
                return;

            if (_profileWidget.IsVisible)
            {
                _profileWidget.Hide();
                return;
            }

            _profileWidget.Bind(
                _boundProfileState,
                _boundDisplayName);
            _profileWidget.SetReadOnly(
                !_boundCharacterCanEdit);
            _profileWidget.Show(_infoBar.PortraitAnchorRect);
        }

        private void EnsureProfileWidget()
        {
            if (_profileWidget != null || _infoBar == null)
                return;

            var canvas = _infoBar.GetComponentInParent<Canvas>();
            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            if (rootCanvas == null)
            {
                Debug.LogError(
                    $"[{name}] 플레이어 정보 UI를 생성할 Root Canvas를 " +
                    "찾지 못했습니다.",
                    this);
                return;
            }

            var text = _infoBar.GetComponentInChildren<Text>(true);
            _profileWidget = PawnProfileWidget.CreateRuntime(
                rootCanvas.transform as RectTransform,
                text != null ? text.font : null);
            _profileWidget.CloseRequested +=
                HandleProfileCloseRequested;
            _profileWidget.Applied +=
                HandleProfileApplied;
            _profileWidget.ValueEditRequested +=
                HandleProfileValueEditRequested;
        }

        private void HandleProfileCloseRequested()
        {
            _profileWidget?.FlushPendingEdit();
            _profileWidget?.Hide();
        }

        private void HandleProfileApplied()
        {
            _profileWidget?.FlushPendingEdit();
        }

        private void HandleProfileValueEditRequested(
            PawnProfileSection section,
            string value)
        {
            if (!_boundCharacterCanEdit)
            {
                _profileWidget?.RefreshFromState();
                return;
            }

            if (_boundProfileState == null)
                return;

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (pawn == null || pawn.Definition == null)
                return;

            var authority = TRPGSessionAuthority.Instance;
            var routeToHost = authority != null &&
                              authority.ShouldRouteClientProfileChange;
            if (routeToHost)
            {
                if (!authority.RequestProfileFieldChange(
                        pawn,
                        section,
                        value))
                {
                    _profileWidget?.RefreshFromState();
                }
                return;
            }

            if (!_boundProfileState.TrySetField(section, value))
            {
                _profileWidget?.RefreshFromState();
                return;
            }

            if (authority != null &&
                authority.IsLocalGameMaster &&
                authority.IsGameplayReady)
            {
                authority.PublishHostProfileFieldChange(
                    pawn,
                    section,
                    value);
            }
        }

        private void HandleInventoryRequested()
        {
            if (!_boundSupportsInventory)
                return;

            if (_boardStackManager != null)
            {
                _boardStackManager.RequestTab(SheetTab.Bag);
                return;
            }

            if (_boundInventoryState == null)
                return;

            EnsureInventoryWidget();
            if (_inventoryWidget == null)
                return;

            if (_inventoryWidget.IsVisible)
            {
                _inventoryWidget.Hide();
                return;
            }

            RefreshInventoryUi();
            _inventoryWidget.SetReadOnly(
                !_boundCharacterCanEdit);
            _inventoryWidget.Show(_infoBar.InventoryAnchorRect);
        }

        private void EnsureInventoryWidget()
        {
            if (_inventoryWidget != null || _infoBar == null)
                return;

            var canvas = _infoBar.GetComponentInParent<Canvas>();
            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            if (rootCanvas == null)
            {
                Debug.LogError(
                    $"[{name}] 인벤토리를 생성할 Root Canvas를 찾지 못했습니다.",
                    this);
                return;
            }

            var text = _infoBar.GetComponentInChildren<Text>(true);
            _inventoryWidget = PawnInventoryWidget.CreateRuntime(
                rootCanvas.transform as RectTransform,
                text != null ? text.font : null);
            _inventoryWidget.AddRequested +=
                HandleInventoryAddRequested;
            _inventoryWidget.RemoveRequested +=
                HandleInventoryRemoveRequested;
            _inventoryWidget.QuantityChangedRequested +=
                HandleInventoryQuantityChangedRequested;
            _inventoryWidget.MoveRequested +=
                HandleInventoryMoveRequested;
            _inventoryWidget.CloseRequested +=
                HandleInventoryCloseRequested;
        }

        private void HandleInventoryAddRequested(
            InventoryItemDraft draft)
        {
            if (!_boundCharacterCanEdit)
            {
                RefreshInventoryUi();
                return;
            }

            if (_boundInventoryState == null)
                return;

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            var authority = TRPGSessionAuthority.Instance;

            if (ShouldRouteClientInventory(authority))
            {
                var requested =
                    _networkGameManager != null &&
                    _networkGameManager.RequestInventoryAdd(
                        pawn,
                        draft);

                if (!requested && authority != null)
                {
                    requested = authority.RequestInventoryAdd(
                        pawn,
                        draft);
                }

                if (!requested)
                    RefreshInventoryUi();
                return;
            }

            string error;
            string runtimeId;
            var succeeded = draft.Definition != null
                ? _boundInventoryState.TryAdd(
                    draft.Definition,
                    draft.Quantity,
                    out runtimeId,
                    out error)
                : _boundInventoryState.TryAddCustom(
                    draft.Type,
                    draft.DisplayName,
                    draft.Quantity,
                    draft.UnitWeight,
                    out runtimeId,
                    out error);

            if (!succeeded)
            {
                Debug.LogWarning(
                    $"[{name}] 아이템을 추가하지 못했습니다. {error}",
                    _boundInventoryState);
                RefreshInventoryUi();
                return;
            }

            var itemName = draft.Definition != null
                ? draft.Definition.DisplayName
                : draft.DisplayName;
            PublishHostInventoryChange(
                authority,
                pawn,
                "아이템 추가",
                $"{itemName} ×{Mathf.Max(1, draft.Quantity)} 추가");
        }

        private void HandleInventoryRemoveRequested(string runtimeId)
        {
            if (!_boundCharacterCanEdit)
            {
                RefreshInventoryUi();
                return;
            }

            if (_boundInventoryState == null)
                return;

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            var authority = TRPGSessionAuthority.Instance;

            if (ShouldRouteClientInventory(authority))
            {
                var requested =
                    _networkGameManager != null &&
                    _networkGameManager.RequestInventoryRemove(
                        pawn,
                        runtimeId);

                if (!requested && authority != null)
                {
                    requested = authority.RequestInventoryRemove(
                        pawn,
                        runtimeId);
                }

                if (!requested)
                    RefreshInventoryUi();
                return;
            }

            var displayName = ResolveInventoryItemName(runtimeId);
            if (!_boundInventoryState.TryRemove(runtimeId))
            {
                RefreshInventoryUi();
                return;
            }

            PublishHostInventoryChange(
                authority,
                pawn,
                "아이템 제거",
                $"{displayName} 제거");
        }

        private void HandleInventoryQuantityChangedRequested(
            string runtimeId,
            int quantity)
        {
            if (!_boundCharacterCanEdit)
            {
                RefreshInventoryUi();
                return;
            }

            if (_boundInventoryState == null)
                return;

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            var authority = TRPGSessionAuthority.Instance;

            if (ShouldRouteClientInventory(authority))
            {
                var requested =
                    _networkGameManager != null &&
                    _networkGameManager.RequestInventoryQuantity(
                        pawn,
                        runtimeId,
                        quantity);

                if (!requested && authority != null)
                {
                    requested = authority.RequestInventoryQuantity(
                        pawn,
                        runtimeId,
                        quantity);
                }

                if (!requested)
                    RefreshInventoryUi();
                return;
            }

            var displayName = ResolveInventoryItemName(runtimeId);
            var previousQuantity =
                ResolveInventoryItemQuantity(runtimeId);
            if (!_boundInventoryState.TrySetQuantity(
                    runtimeId,
                    quantity))
            {
                RefreshInventoryUi();
                return;
            }

            PublishHostInventoryChange(
                authority,
                pawn,
                "아이템 수량",
                $"{displayName} {previousQuantity} → " +
                $"{Mathf.Max(1, quantity)}");
        }

        private void HandleInventoryMoveRequested(
            string runtimeId,
            int targetIndex)
        {
            if (!_boundCharacterCanEdit)
            {
                RefreshInventoryUi();
                return;
            }

            if (_boundInventoryState == null)
                return;

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            var authority = TRPGSessionAuthority.Instance;

            if (ShouldRouteClientInventory(authority))
            {
                var requested =
                    _networkGameManager != null &&
                    _networkGameManager.RequestInventoryMove(
                        pawn,
                        runtimeId,
                        targetIndex);

                if (!requested && authority != null)
                {
                    requested = authority.RequestInventoryMove(
                        pawn,
                        runtimeId,
                        targetIndex);
                }

                if (!requested)
                    RefreshInventoryUi();
                return;
            }

            var displayName = ResolveInventoryItemName(runtimeId);
            if (!_boundInventoryState.TryMove(
                    runtimeId,
                    targetIndex))
            {
                RefreshInventoryUi();
                return;
            }

            PublishHostInventoryChange(
                authority,
                pawn,
                "아이템 정렬",
                $"{displayName} → 슬롯 {targetIndex + 1}");
        }

        private bool ShouldRouteClientInventory(
            TRPGSessionAuthority authority)
        {
            return (_networkGameManager != null &&
                    _networkGameManager
                        .ShouldRouteClientInventoryChange) ||
                   (authority != null &&
                    authority.ShouldRouteClientInventoryChange);
        }

        private void PublishHostInventoryChange(
            TRPGSessionAuthority authority,
            InteractivePawn pawn,
            string title,
            string detail)
        {
            if (_networkGameManager != null)
            {
                _networkGameManager.PublishHostInventorySnapshot(
                    pawn,
                    title,
                    detail);
                return;
            }

            authority?.PublishHostInventorySnapshot(
                pawn,
                title,
                detail);
        }

        private string ResolveInventoryItemName(string runtimeId)
        {
            if (_boundInventoryState == null)
                return "아이템";

            var items = _boundInventoryState.Items;
            for (var index = 0; index < items.Count; index++)
            {
                if (string.Equals(
                        items[index].RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    return items[index].DisplayName;
                }
            }

            return "아이템";
        }

        private int ResolveInventoryItemQuantity(string runtimeId)
        {
            if (_boundInventoryState == null)
                return 0;

            var items = _boundInventoryState.Items;
            for (var index = 0; index < items.Count; index++)
            {
                if (string.Equals(
                        items[index].RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    return items[index].Quantity;
                }
            }

            return 0;
        }

        private void HandleInventoryCloseRequested()
        {
            _inventoryWidget?.Hide();
        }

        private void RefreshInventoryUi()
        {
            if (_inventoryWidget == null ||
                _boundInventoryState == null)
            {
                return;
            }

            var capacity = _boundInventoryState.CalculateCapacity(
                _boundStatState,
                _inventoryCapacityStatId,
                _inventoryCapacityMultiplier,
                _inventoryFallbackCapacity);
            _inventoryWidget.Bind(
                _boundInventoryState.Items,
                _boundInventoryState.CurrentWeight,
                capacity,
                _itemCatalog,
                _inventoryIconSet);
            _inventoryWidget.SetReadOnly(
                !_boundCharacterCanEdit);
        }

        private static bool IsPlayerPawn(InteractivePawn pawn)
        {
            var definition = pawn != null ? pawn.Definition : null;
            return definition != null && definition.IsPlayer;
        }

        private static bool IsCharacterPawn(InteractivePawn pawn)
        {
            return pawn != null && pawn.HasStats;
        }

        private static bool IsLocalGameMasterOrOffline()
        {
            var authority = TRPGSessionAuthority.Instance;
            return authority == null ||
                   !authority.IsOnline ||
                   authority.IsLocalGameMaster;
        }

        private static bool CanViewStats(InteractivePawn pawn)
        {
            if (!IsCharacterPawn(pawn) || pawn.Definition == null)
                return false;

            if (IsLocalGameMasterOrOffline())
                return true;

            var authority = TRPGSessionAuthority.Instance;
            return pawn.Definition.IsPlayer &&
                   authority != null &&
                   authority.CanLocalViewFullCharacter(pawn);
        }

        private static bool CanViewMovementInformation(
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
                return false;

            if (IsLocalGameMasterOrOffline())
                return pawn.Definition.CanMove;

            var authority = TRPGSessionAuthority.Instance;
            return pawn.Definition.IsPlayer &&
                   authority != null &&
                   authority.CanLocalViewFullCharacter(pawn);
        }

        private static bool CanEditCharacter(InteractivePawn pawn)
        {
            if (!IsCharacterPawn(pawn))
                return false;

            var authority = TRPGSessionAuthority.Instance;
            if (authority == null ||
                !authority.IsOnline ||
                authority.IsLocalGameMaster)
            {
                return true;
            }

            return IsPlayerPawn(pawn) &&
                   authority.CanLocalViewFullCharacter(pawn);
        }

        private static bool CanRollCharacter(InteractivePawn pawn)
        {
            if (!IsCharacterPawn(pawn) || pawn.IsDead)
                return false;

            var authority = TRPGSessionAuthority.Instance;
            return authority == null ||
                   !authority.IsOnline ||
                   authority.CanLocalRollPawn(pawn);
        }

        private static bool CanRerollCharacter(
            InteractivePawn pawn)
        {
            if (!IsCharacterPawn(pawn) || pawn.IsDead)
                return false;

            var authority = TRPGSessionAuthority.Instance;
            if (authority == null ||
                !authority.IsOnline ||
                authority.IsLocalGameMaster)
            {
                return true;
            }

            return IsPlayerPawn(pawn) &&
                   authority.CanLocalViewFullCharacter(pawn);
        }

        private static bool CanMoveCharacter(InteractivePawn pawn)
        {
            if (pawn == null ||
                pawn.Definition == null ||
                !pawn.IsMoveable)
            {
                return false;
            }

            var authority = TRPGSessionAuthority.Instance;
            return authority == null ||
                   !authority.IsOnline ||
                   authority.CanLocalMovePawn(pawn);
        }

        private void RefreshStatUi()
        {
            if (_boundStatState == null ||
                !_boundStatState.IsInitialized ||
                _boundStatState.Runtime == null)
            {
                _infoBar.ClearStats();
                return;
            }

            try
            {
                var data = BuildStatPanelData(
                    _boundStatState.Runtime,
                    _boundSkillState,
                    _boundDisplayName,
                    _boundPortrait,
                    _boundCharacterCanEdit,
                    _boundCharacterCanRoll,
                    _boundCharacterCanReroll);
                _infoBar.SetStats(data);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _infoBar.ClearStats();
            }
        }

        private static PawnStatPanelData BuildStatPanelData(
            StatRuntimeState runtime,
            PlayerSkillState skillState,
            string displayName,
            Sprite portrait,
            bool canEditCharacter,
            bool canRollCharacter,
            bool canRerollCharacter)
        {
            var definitions = new List<IStatDefinition>(
                runtime.Template.Stats);
            definitions.Sort(
                (left, right) =>
                    left.SortOrder.CompareTo(right.SortOrder));

            var resourceIds = new HashSet<string>(
                StringComparer.Ordinal);
            var resources = new List<PawnResourceValueData>(3);

            TryAddResource(
                runtime,
                StatRole.HealthCurrent,
                StatRole.HealthMax,
                "체력",
                resources,
                resourceIds,
                canEditCharacter);
            TryAddResource(
                runtime,
                StatRole.SanityCurrent,
                StatRole.SanityMax,
                "이성",
                resources,
                resourceIds,
                canEditCharacter);
            TryAddResource(
                runtime,
                StatRole.MagicCurrent,
                StatRole.MagicMax,
                "마력",
                resources,
                resourceIds,
                canEditCharacter);

            var axes = BuildAxes(
                runtime,
                definitions,
                resourceIds);
            var authority = TRPGSessionAuthority.Instance;
            var hasUnlimitedRerolls =
                authority == null ||
                !authority.IsOnline ||
                authority.IsLocalGameMaster;
            var rerollPointsRemaining =
                CoCStatGenerationRules.DefaultPlayerRerollPoints;
            if (runtime.TryGetDefinition(
                    CoCStatGenerationRules.RerollPointsStatId,
                    out _))
            {
                rerollPointsRemaining =
                    CoCStatGenerationRules.ClampPlayerRerollPoints(
                        runtime.GetNumber(
                            CoCStatGenerationRules.RerollPointsStatId));
            }

            var canUseStatRerolls =
                canRerollCharacter &&
                (hasUnlimitedRerolls || rerollPointsRemaining > 0);
            var entries = BuildEntries(
                runtime,
                definitions,
                resourceIds,
                canEditCharacter,
                canUseStatRerolls);

            var skills = BuildSkillValues(skillState);

            return new PawnStatPanelData(
                displayName,
                portrait,
                axes,
                entries,
                resources,
                skills,
                Array.Empty<PawnSkillOptionData>(),
                canEditCharacter &&
                skillState != null &&
                skillState.IsInitialized,
                canRollCharacter,
                hasUnlimitedRerolls,
                rerollPointsRemaining,
                CoCStatGenerationRules.MaximumPlayerRerollPoints);
        }

        private static List<PawnSkillValueData> BuildSkillValues(
            PlayerSkillState skillState)
        {
            var result = new List<PawnSkillValueData>();
            if (skillState == null || !skillState.IsInitialized)
                return result;

            var source = skillState.Skills;
            for (var index = 0; index < source.Count; index++)
            {
                var value = source[index];
                var regular = Mathf.Clamp(
                    value.RegularValue,
                    0,
                    999);
                result.Add(
                    new PawnSkillValueData(
                        value.SkillId,
                        value.DisplayName,
                        value.Category,
                        regular,
                        regular / 2,
                        regular / 5,
                        value.UsesBaseValue,
                        value.RequiresTraining,
                        value.SortOrder));
            }

            result.Sort((left, right) =>
            {
                var order = left.SortOrder.CompareTo(right.SortOrder);
                if (order != 0)
                    return order;

                return string.Compare(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.CurrentCulture);
            });
            return result;
        }

        private static List<PawnStatAxisData> BuildAxes(
            StatRuntimeState runtime,
            IReadOnlyList<IStatDefinition> definitions,
            ISet<string> excludedIds)
        {
            var axes = new List<PawnStatAxisData>(8);
            var usedIds = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0;
                 index < PreferredCocAxisIds.Length;
                 index++)
            {
                TryAddAxis(
                    runtime,
                    PreferredCocAxisIds[index],
                    axes,
                    usedIds,
                    excludedIds);
            }

            for (var index = 0;
                 index < definitions.Count && axes.Count < 8;
                 index++)
            {
                var definition = definitions[index];
                if (definition.Source != StatValueSource.Base)
                {
                    continue;
                }

                TryAddAxis(
                    runtime,
                    definition.Id,
                    axes,
                    usedIds,
                    excludedIds);
            }

            return axes;
        }

        private static void TryAddAxis(
            StatRuntimeState runtime,
            string statId,
            ICollection<PawnStatAxisData> destination,
            ISet<string> usedIds,
            ISet<string> excludedIds)
        {
            if (string.IsNullOrWhiteSpace(statId) ||
                usedIds.Contains(statId) ||
                excludedIds.Contains(statId) ||
                !runtime.TryGetDefinition(statId, out var definition))
            {
                return;
            }

            var value = runtime.GetNumber(statId);
            var minimum = definition.MinValue;
            var maximum = definition.MaxValue;
            if (maximum <= minimum)
            {
                maximum = Math.Max(minimum + 1d, value);
            }

            destination.Add(
                new PawnStatAxisData(
                    statId,
                    definition.DisplayName,
                    value,
                    minimum,
                    maximum));
            usedIds.Add(statId);
        }

        private static List<PawnStatEntryData> BuildEntries(
            StatRuntimeState runtime,
            IReadOnlyList<IStatDefinition> definitions,
            ISet<string> resourceIds,
            bool canEditCharacter,
            bool canUseStatRerolls)
        {
            var entries = new List<PawnStatEntryData>(
                definitions.Count);
            var presentation = new StatPresentationService(runtime);
            var isCocTemplate = IsCocTemplate(runtime.Template);

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (resourceIds.Contains(definition.Id) ||
                    string.Equals(
                        definition.Id,
                        CoCStatGenerationRules.RerollPointsStatId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var value = runtime.GetNumber(definition.Id);
                var showsDifficulty =
                    isCocTemplate &&
                    IsD100Checkable(definition, value);
                var regular = showsDifficulty
                    ? Mathf.Clamp(
                        (int)Math.Floor(value),
                        0,
                        100)
                    : 0;
                var hard = showsDifficulty ? regular / 2 : 0;
                var extreme = showsDifficulty ? regular / 5 : 0;
                var hasGenerationRule =
                    CoCStatGenerationRules.TryGetFormula(
                        definition.Id,
                        out var rerollFormula,
                        out _,
                        out _);

                entries.Add(
                    new PawnStatEntryData(
                        definition.Id,
                        definition.DisplayName,
                        presentation.FormatValue(definition.Id),
                        value,
                        IsEditable(
                            definition,
                            canEditCharacter),
                        showsDifficulty,
                        regular,
                        hard,
                        extreme,
                        value,
                        0d,
                        0d,
                        definition.MinValue,
                        definition.MaxValue,
                        hasGenerationRule && canUseStatRerolls,
                        hasGenerationRule
                            ? rerollFormula
                            : string.Empty));
            }

            return entries;
        }

        private static bool IsCocTemplate(IStatRuleTemplate template)
        {
            if (template == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(template.Id) &&
                template.Id.IndexOf(
                    "coc",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var stats = template.Stats;
            for (var index = 0; index < stats.Count; index++)
            {
                if (stats[index].Id.StartsWith(
                        "coc.",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsD100Checkable(
            IStatDefinition definition,
            double value)
        {
            if (definition == null ||
                value < 0d ||
                value > 100d)
            {
                return false;
            }

            if (definition.Source == StatValueSource.Base)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(definition.Category) &&
                   definition.Category.IndexOf(
                       "기술",
                       StringComparison.Ordinal) >= 0;
        }

        private static void TryAddResource(
            StatRuntimeState runtime,
            StatRole currentRole,
            StatRole maximumRole,
            string label,
            ICollection<PawnResourceValueData> destination,
            ISet<string> resourceIds,
            bool canEditCharacter)
        {
            var currentId = runtime.Template.GetStatId(currentRole);
            var maximumId = runtime.Template.GetStatId(maximumRole);

            if (string.IsNullOrWhiteSpace(currentId) ||
                !runtime.TryGetDefinition(
                    currentId,
                    out var currentDefinition))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(maximumId))
            {
                maximumId = currentDefinition.MaxStatId;
            }

            var current = runtime.GetNumber(currentId);
            var maximum = current;
            var canEditMaximum = false;

            if (!string.IsNullOrWhiteSpace(maximumId) &&
                runtime.TryGetDefinition(
                    maximumId,
                    out var maximumDefinition))
            {
                maximum = runtime.GetNumber(maximumId);
                canEditMaximum = IsEditable(
                    maximumDefinition,
                    canEditCharacter);
                resourceIds.Add(maximumId);
            }

            destination.Add(
                new PawnResourceValueData(
                    label,
                    currentId,
                    current,
                    maximumId,
                    maximum,
                    true,
                    IsEditable(
                        currentDefinition,
                        canEditCharacter),
                    canEditMaximum));
            resourceIds.Add(currentId);
        }

        private static bool IsEditable(
            IStatDefinition definition,
            bool canEditCharacter)
        {
            if (!canEditCharacter || definition == null)
                return false;

            var authority = TRPGSessionAuthority.Instance;
            var isNetworkPlayer =
                authority != null &&
                authority.IsOnline &&
                !authority.IsLocalGameMaster;

            if (!isNetworkPlayer)
            {
                return definition.Source == StatValueSource.Base ||
                       definition.Source == StatValueSource.Runtime;
            }

            if (definition.Source != StatValueSource.Runtime)
                return false;

            return definition.IsAdjustable ||
                   IsPlayerEditableCurrentStatId(definition.Id);
        }

        private static bool IsPlayerEditableCurrentStatId(
            string statId)
        {
            return string.Equals(
                       statId,
                       "coc.hp.current",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       statId,
                       "coc.mp.current",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       statId,
                       "coc.san.current",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       statId,
                       "coc.luck.current",
                       StringComparison.Ordinal);
        }

        private static PlayerStatState ResolveStatState(
            InteractivePawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            var state = pawn.GetComponentInParent<PlayerStatState>();
            if (state == null)
            {
                state = pawn.GetComponentInChildren<PlayerStatState>();
            }

            if (state == null)
            {
                state = PlayerStatState.ActiveState;
            }

            return state;
        }

        private void HandleMovementBudgetChanged(
            InteractivePawn pawn,
            float remainingMeters,
            float maximumMeters)
        {
            if (pawn == _pawnManager.SelectedInteractive &&
                CanViewMovementInformation(pawn))
            {
                _infoBar.SetMovementBudget(
                    remainingMeters,
                    maximumMeters);
            }
        }

        private void HandlePathPreviewChanged(PawnPathPreviewData data)
        {
            _infoBar.SetPathPreview(data, _pawnManager.BoardCamera);
        }

        private void HandleMovementRangeChanged(
            PawnMovementRangeData data)
        {
            _infoBar.SetMovementRange(data, _pawnManager.BoardCamera);
        }

        private void RefreshMovementBudget(InteractivePawn pawn)
        {
            if (!CanViewMovementInformation(pawn))
                return;

            if (_pawnManager.MovementManager.TryGetMovementBudget(
                    pawn,
                    out var remaining,
                    out var maximum))
            {
                _infoBar.SetMovementBudget(remaining, maximum);
            }
        }

        private void BindWalkButton()
        {
            if (_walkButton == null)
            {
                return;
            }

            _walkButton.onClick.RemoveListener(StartWalking);
            _walkButton.onClick.AddListener(StartWalking);
        }

        private void UnbindWalkButton()
        {
            if (_walkButton != null)
            {
                _walkButton.onClick.RemoveListener(StartWalking);
            }
        }

        private void RefreshWalkButton(InteractivePawn pawn)
        {
            var canMove = CanMoveCharacter(pawn);
            var isActive =
                canMove &&
                _pawnManager != null &&
                _pawnManager.IsMovementModeActive &&
                _pawnManager.SelectedInteractive == pawn;

            _infoBar?.SetMovementModeState(canMove, isActive);

            if (_walkButton != null)
            {
                _walkButton.interactable = canMove;
            }
        }

        private void HandleStatRerollRequested(
            string statId)
        {
            if (!_boundCharacterCanReroll ||
                string.IsNullOrWhiteSpace(statId))
            {
                return;
            }

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (pawn == null || pawn.Definition == null)
                return;

            var authority = TRPGSessionAuthority.Instance;
            if (authority != null)
            {
                if (!authority.RequestCocStatReroll(
                        pawn,
                        statId))
                {
                    RefreshStatUi();
                }
                return;
            }

            if (!CoCStatGenerationRules.TryRoll(
                    statId,
                    out var result,
                    out var expression,
                    out var minimum,
                    out var maximum) ||
                _boundStatState == null ||
                !TryGetDisplayedStatValue(
                    _boundStatState,
                    statId,
                    out var previousValue) ||
                !_boundStatState.TrySetAuthoritativeDisplayedValue(
                    statId,
                    result))
            {
                return;
            }

            var title =
                CoCStatGenerationRules.GetAbbreviation(statId) +
                " 재굴림";
            var presentation = new PawnRollPresentationData(
                title,
                expression,
                result,
                minimum,
                maximum,
                result.ToString(),
                $"{previousValue:0} → {result}",
                new Color(0.78f, 0.60f, 1f, 1f),
                1.35f);
            PawnRollLogService.RecordRoll(
                PawnRollLogKind.Effect,
                pawn,
                presentation.Title,
                presentation.Expression,
                presentation.FinalValue,
                presentation.ResultLabel,
                presentation.DetailLabel);
            _infoBar?.PlayRoll(presentation);
            RefreshStatUi();
        }

        private void HandleCheckRollRequested(
            PawnCheckRollRequest request)
        {
            if (!TryBeginRoll(out var pawn))
            {
                return;
            }

            var target = Mathf.Clamp(request.Target, 1, 100);
            var modifiedRoll = _rollService.RollD100Modified(
                target,
                request.BonusPenaltyLevel);
            var result = modifiedRoll.SelectedResult;
            var presentation = new PawnRollPresentationData(
                "판정 굴림",
                BuildD100Expression(modifiedRoll) + $" / 목표 {target}",
                result.Roll,
                1,
                100,
                GetCheckResultLabel(result.Grade),
                modifiedRoll.GetCandidateLabel() +
                $" / 목표 {target}",
                GetCheckResultColor(result.Grade),
                1.55f,
                target);
            PawnRollLogService.RecordRoll(
                PawnRollLogKind.Check,
                pawn,
                presentation.Title,
                presentation.Expression,
                presentation.FinalValue,
                presentation.ResultLabel,
                presentation.DetailLabel,
                request.Visibility);
            TRPGSessionAuthority.PublishRoll(
                pawn,
                PawnRollLogKind.Check,
                presentation);
            _infoBar.PlayModifiedD100(modifiedRoll, presentation);
        }

        private void HandleEffectRollRequested(
            PawnEffectRollRequest request)
        {
            if (!TryBeginRoll(out var pawn))
            {
                return;
            }

            _effectDiceCount = Mathf.Clamp(
                request.DiceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _effectDiceSides = Mathf.Clamp(
                request.DiceSides,
                2,
                PawnRollService.MaximumDiceSides);
            _effectDiceModifier = Mathf.Clamp(
                request.Modifier,
                -999,
                999);

            var result = _rollService.RollEffect(
                _effectDiceCount,
                _effectDiceSides,
                _effectDiceModifier);
            var presentation = new PawnRollPresentationData(
                "효과 굴림",
                result.Expression,
                result.Total,
                result.MinimumTotal,
                result.MaximumTotal,
                $"합계 {result.Total}",
                result.GetBreakdownLabel(),
                _effectColor,
                1.35f);
            PawnRollLogService.RecordRoll(
                PawnRollLogKind.Effect,
                pawn,
                presentation.Title,
                presentation.Expression,
                presentation.FinalValue,
                presentation.ResultLabel,
                presentation.DetailLabel,
                request.Visibility);
            TRPGSessionAuthority.PublishRoll(
                pawn,
                PawnRollLogKind.Effect,
                presentation);
            _infoBar.PlayRoll(presentation);
        }

        private void HandleResourceConditionRequested(
            PawnResourceValueData resource)
        {
            if (!_boundCharacterCanEdit)
                return;

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (pawn == null || pawn.Definition == null)
                return;

            if (_boundProfileState == null)
            {
                BindProfileState(
                    PawnProfileState.ResolveOrCreate(
                        pawn.gameObject,
                        pawn.Definition));
            }
            if (_boundProfileState == null)
            {
                Debug.LogWarning(
                    $"[{name}] 상태를 저장할 캐릭터 프로필을 " +
                    "생성하지 못했습니다.",
                    this);
                return;
            }

            var isSanity = string.Equals(
                resource.Label,
                "이성",
                StringComparison.Ordinal);
            _conditionSection = isSanity
                ? PawnProfileSection.PhobiasAndManias
                : PawnProfileSection.OtherNotes;
            _conditionTitle = isSanity
                ? "이성 상태 관리"
                : "체력 상태 관리";

            EnsureConditionWidget();
            RefreshConditionWidget();
            _conditionWidget?.Show();
        }

        private void EnsureConditionWidget()
        {
            if (_conditionWidget != null || _infoBar == null)
                return;

            var canvas = _infoBar.GetComponentInParent<Canvas>();
            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            if (rootCanvas == null)
            {
                Debug.LogError(
                    $"[{name}] 상태 관리 UI를 생성할 Root Canvas를 " +
                    "찾지 못했습니다.",
                    this);
                return;
            }

            var text = _infoBar.GetComponentInChildren<Text>(true);
            _conditionWidget = PawnConditionWidget.CreateRuntime(
                rootCanvas,
                text != null ? text.font : null);
            _conditionWidget.AddRequested +=
                HandleConditionAddRequested;
            _conditionWidget.RemoveRequested +=
                HandleConditionRemoveRequested;
            _conditionWidget.CloseRequested +=
                HandleConditionCloseRequested;
        }

        private void RefreshConditionWidget()
        {
            if (_conditionWidget == null ||
                !_conditionWidget.IsVisible &&
                _boundProfileState == null)
            {
                return;
            }

            IReadOnlyList<string> conditions =
                _boundProfileState != null
                    ? ExtractManagedConditions(
                        _boundProfileState.GetField(_conditionSection))
                    : Array.Empty<string>();
            _conditionWidget.Bind(
                _conditionTitle,
                conditions,
                _boundCharacterCanEdit);
        }

        private void HandleConditionAddRequested(string value)
        {
            if (!_boundCharacterCanEdit ||
                _boundProfileState == null ||
                string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var condition = "[상태] " + value.Trim();
            var current =
                _boundProfileState.GetField(_conditionSection) ??
                string.Empty;
            var existing = ExtractManagedConditions(current);
            for (var index = 0; index < existing.Count; index++)
            {
                if (string.Equals(
                        existing[index],
                        condition,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            var next = string.IsNullOrWhiteSpace(current)
                ? condition
                : current.TrimEnd() + "\n" + condition;
            HandleProfileValueEditRequested(_conditionSection, next);
            RefreshConditionWidget();
        }

        private void HandleConditionRemoveRequested(string condition)
        {
            if (!_boundCharacterCanEdit ||
                _boundProfileState == null ||
                string.IsNullOrWhiteSpace(condition))
            {
                return;
            }

            var current =
                _boundProfileState.GetField(_conditionSection) ??
                string.Empty;
            var lines = NormalizeProfileLines(current);
            var remaining = new List<string>(lines.Length);
            var removed = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!removed &&
                    string.Equals(
                        line.Trim(),
                        condition.Trim(),
                        StringComparison.Ordinal))
                {
                    removed = true;
                    continue;
                }

                remaining.Add(line);
            }

            if (!removed)
                return;

            while (remaining.Count > 0 &&
                   string.IsNullOrWhiteSpace(
                       remaining[remaining.Count - 1]))
            {
                remaining.RemoveAt(remaining.Count - 1);
            }

            HandleProfileValueEditRequested(
                _conditionSection,
                string.Join("\n", remaining));
            RefreshConditionWidget();
        }

        private void HandleConditionCloseRequested()
        {
            _conditionWidget?.Hide();
        }

        private static IReadOnlyList<string> ExtractManagedConditions(
            string value)
        {
            var result = new List<string>();
            var lines = NormalizeProfileLines(value);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (IsManagedConditionLine(line))
                    result.Add(line);
            }

            return result;
        }

        private static bool IsManagedConditionLine(string line)
        {
            return !string.IsNullOrWhiteSpace(line) &&
                   (line.StartsWith(
                        "[상태]",
                        StringComparison.Ordinal) ||
                    line.StartsWith(
                        "[자동 감지]",
                        StringComparison.Ordinal));
        }

        private static string[] NormalizeProfileLines(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private void HandleResourceRollRequested(
            PawnResourceRollRequest request)
        {
            if (!TryBeginRoll(out var pawn) ||
                _boundStatState == null ||
                string.IsNullOrWhiteSpace(
                    request.Resource.CurrentStatId) ||
                !TryGetDisplayedStatValue(
                    _boundStatState,
                    request.Resource.CurrentStatId,
                    out var previousValue))
            {
                _isRollInProgress = false;
                return;
            }

            var maximumValue = Math.Max(
                0d,
                request.Resource.Maximum);
            PawnRollPresentationData presentation;
            D100ModifiedRollResult sanityModifiedRoll = null;

            if (request.Mode == PawnResourceRollMode.Sanity &&
                UsesCallOfCthulhuRules)
            {
                var target = Mathf.Clamp(request.Target, 1, 100);
                sanityModifiedRoll = _rollService.RollD100Modified(
                    target,
                    request.BonusPenaltyLevel);
                var check = sanityModifiedRoll.SelectedResult;
                var lossExpression = check.IsSuccess
                    ? request.SuccessExpression
                    : request.FailureExpression;
                var lossRoll = _rollService.RollExpression(
                    lossExpression);
                var rolledLoss = Mathf.Max(0, lossRoll.Total);
                var nextValue = Math.Max(0d, previousValue - rolledLoss);
                var appliedLoss = Mathf.Max(
                    0,
                    Mathf.RoundToInt((float)(previousValue - nextValue)));
                HandleStatValueEditRequested(
                    request.Resource.CurrentStatId,
                    nextValue);

                var sanityState =
                    CoCSanityRuntimeState.ResolveOrCreate(pawn);
                var evaluation = sanityState.RecordLoss(
                    Mathf.RoundToInt((float)previousValue),
                    Mathf.RoundToInt((float)nextValue),
                    appliedLoss,
                    _settings.CocSingleSanityLossThreshold,
                    _settings.CocPeriodSanityLossRatio);
                TRPGSessionAuthority.Instance?.PublishSanityState(
                    pawn,
                    sanityState.CreateSnapshot());
                var conditionLabel = BuildSanityConditionLabel(
                    evaluation);
                if (!string.IsNullOrWhiteSpace(conditionLabel))
                {
                    AppendAutomaticProfileCondition(
                        pawn,
                        PawnProfileSection.PhobiasAndManias,
                        conditionLabel);
                }

                var detail =
                    $"{GetCheckResultLabel(check.Grade)} | " +
                    $"손실 {lossRoll.Expression} = {rolledLoss}" +
                    (rolledLoss != appliedLoss
                        ? $" (실제 적용 {appliedLoss})"
                        : string.Empty) +
                    $" | SAN {previousValue:0} → {nextValue:0}";
                if (!string.IsNullOrWhiteSpace(conditionLabel))
                {
                    detail += " | " + conditionLabel.Replace(
                        "\n",
                        " | ");
                }

                presentation = new PawnRollPresentationData(
                    "이성 판정",
                    BuildD100Expression(sanityModifiedRoll) + $" / SAN {target}",
                    check.Roll,
                    1,
                    100,
                    GetCheckResultLabel(check.Grade),
                    sanityModifiedRoll.GetCandidateLabel() + " | " + detail,
                    GetCheckResultColor(check.Grade),
                    1.55f,
                    target);
                PawnRollLogService.RecordRoll(
                    PawnRollLogKind.Check,
                    pawn,
                    presentation.Title,
                    presentation.Expression,
                    presentation.FinalValue,
                    presentation.ResultLabel,
                    presentation.DetailLabel,
                    request.Visibility);
                TRPGSessionAuthority.PublishRoll(
                    pawn,
                    PawnRollLogKind.Check,
                    presentation);
            }
            else
            {
                var result = _rollService.RollExpression(
                    request.Expression);
                var amount = Mathf.Max(0, result.Total);
                var isHealing =
                    request.Mode == PawnResourceRollMode.Healing;
                var nextValue = isHealing
                    ? Math.Min(maximumValue, previousValue + amount)
                    : Math.Max(0d, previousValue - amount);
                HandleStatValueEditRequested(
                    request.Resource.CurrentStatId,
                    nextValue);

                var conditionLabels = new List<string>();
                if (!isHealing &&
                    UsesCallOfCthulhuRules)
                {
                    if (maximumValue > 0d &&
                        amount >= Math.Ceiling(maximumValue * 0.5d))
                    {
                        conditionLabels.Add(
                            $"[자동 감지] 중상 조건: 한 번에 {amount} 피해");
                    }

                    if (previousValue > 0d && nextValue <= 0d)
                    {
                        conditionLabels.Add(
                            "[자동 감지] HP 0 도달: 빈사/의식 상태 확인 필요");
                    }

                    for (var conditionIndex = 0;
                         conditionIndex < conditionLabels.Count;
                         conditionIndex++)
                    {
                        AppendAutomaticProfileCondition(
                            pawn,
                            PawnProfileSection.OtherNotes,
                            conditionLabels[conditionIndex]);
                    }
                }

                var conditionLabel = string.Join(
                    " | ",
                    conditionLabels);

                var title = isHealing ? "회복 굴림" : "피해 굴림";
                var detail =
                    $"{request.Resource.Label} " +
                    $"{previousValue:0} → {nextValue:0}";
                if (!string.IsNullOrWhiteSpace(conditionLabel))
                {
                    detail += $" | {conditionLabel}";
                }

                presentation = new PawnRollPresentationData(
                    title,
                    result.Expression,
                    result.Total,
                    result.MinimumTotal,
                    result.MaximumTotal,
                    $"합계 {result.Total}",
                    detail,
                    _effectColor,
                    1.35f);
                PawnRollLogService.RecordRoll(
                    PawnRollLogKind.Effect,
                    pawn,
                    presentation.Title,
                    presentation.Expression,
                    presentation.FinalValue,
                    presentation.ResultLabel,
                    presentation.DetailLabel,
                    request.Visibility);
                TRPGSessionAuthority.PublishRoll(
                    pawn,
                    PawnRollLogKind.Effect,
                    presentation);
            }

            if (sanityModifiedRoll != null)
                _infoBar.PlayModifiedD100(sanityModifiedRoll, presentation);
            else
                _infoBar.PlayRoll(presentation);
            RefreshStatUi();
        }

        private static string BuildD100Expression(
            D100ModifiedRollResult result)
        {
            if (result == null || result.BonusPenaltyLevel == 0)
                return "d100";
            return result.BonusPenaltyLevel > 0
                ? $"d100 + 보너스 {result.BonusPenaltyLevel}"
                : $"d100 + 페널티 {-result.BonusPenaltyLevel}";
        }

        private static string BuildSanityConditionLabel(
            CoCSanityEvaluation evaluation)
        {
            var conditions = new List<string>();
            if (evaluation.TemporaryNew)
            {
                conditions.Add(
                    "[자동 감지] 일시적 광기 조건");
            }

            if (evaluation.IndefiniteNew)
            {
                conditions.Add(
                    $"[자동 감지] 장기적 광기 조건: 기간 누적 " +
                    $"{evaluation.PeriodLoss} SAN 손실");
            }

            if (evaluation.PermanentNew)
            {
                conditions.Add(
                    "[자동 감지] SAN 0: 영구적 광기 조건");
            }

            return string.Join("\n", conditions);
        }

        private void AppendAutomaticProfileCondition(
            InteractivePawn pawn,
            PawnProfileSection section,
            string condition)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(condition))
            {
                return;
            }

            var profile = PawnProfileState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (profile == null)
            {
                return;
            }

            var current = profile.GetField(section) ?? string.Empty;
            if (current.IndexOf(condition, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            var next = string.IsNullOrWhiteSpace(current)
                ? condition
                : current.TrimEnd() + "\n" + condition;
            var authority = TRPGSessionAuthority.Instance;
            if (authority != null &&
                authority.ShouldRouteClientProfileChange)
            {
                authority.RequestProfileFieldChange(
                    pawn,
                    section,
                    next);
                return;
            }

            if (!profile.TrySetField(section, next))
            {
                return;
            }

            if (authority != null &&
                authority.IsLocalGameMaster &&
                authority.IsGameplayReady)
            {
                authority.PublishHostProfileFieldChange(
                    pawn,
                    section,
                    next);
            }
        }

        private void HandleRollPresentationCompleted()
        {
            _isRollInProgress = false;
            var selected = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            _infoBar.SetRollButtonsEnabled(
                selected != null && CanRollCharacter(selected));
        }

        private bool TryBeginRoll(out InteractivePawn pawn)
        {
            pawn = _pawnManager.SelectedInteractive;
            if (_isRollInProgress ||
                !_boundCharacterCanRoll ||
                pawn == null ||
                pawn.Definition == null)
            {
                return false;
            }

            _isRollInProgress = true;
            _infoBar.SetRollButtonsEnabled(false);
            return true;
        }

        private int ResolveCheckTarget(InteractivePawn pawn)
        {
            if (pawn != null &&
                pawn.TryGetComponent<PawnRollStats>(out var stats))
            {
                return stats.GetCheckTarget(_checkStatId);
            }

            return DefaultCheckTarget;
        }

        private void RefreshRollButtons(InteractivePawn pawn)
        {
            if (_infoBar == null)
            {
                return;
            }

            _isRollInProgress = false;
            _infoBar.CancelRollPresentation();

            var hasPawn = pawn != null && pawn.Definition != null;
            var canRoll = hasPawn && CanRollCharacter(pawn);
            _boundCharacterCanRoll = canRoll;
            _boundCharacterCanReroll =
                hasPawn && CanRerollCharacter(pawn);
            _infoBar.SetRollButtonsEnabled(canRoll);
            if (!hasPawn)
            {
                return;
            }

            var target = Mathf.Clamp(
                ResolveCheckTarget(pawn),
                MinimumCheckTarget,
                MaximumCheckTarget);
            var effectExpression = PawnRollService.FormatExpression(
                _effectDiceCount,
                _effectDiceSides,
                _effectDiceModifier);

            // 판정 입력창의 목표값을 강제로 다시 밀어 넣지 않는다.
            // 잘못 추가된 별도 판정 Overlay도 이 Manager에서 생성하지 않는다.
            _infoBar.SetRollButtonLabels(
                $"판정 굴림\nD100 ≤ {target}",
                $"효과 굴림\n{effectExpression}");
        }

        private Color GetCheckResultColor(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return _criticalColor;
                case CheckRollGrade.Fumble:
                    return _fumbleColor;
                case CheckRollGrade.ExtremeSuccess:
                case CheckRollGrade.HardSuccess:
                case CheckRollGrade.Success:
                    return _successColor;
                default:
                    return _failureColor;
            }
        }

        private static string GetCheckResultLabel(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return "대성공";
                case CheckRollGrade.ExtremeSuccess:
                    return "극단적 성공";
                case CheckRollGrade.HardSuccess:
                    return "어려운 성공";
                case CheckRollGrade.Success:
                    return "성공";
                case CheckRollGrade.Fumble:
                    return "대실패";
                default:
                    return "실패";
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _effectDiceCount = Mathf.Clamp(
                _effectDiceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _effectDiceSides = Mathf.Clamp(
                _effectDiceSides,
                2,
                PawnRollService.MaximumDiceSides);
            if (string.IsNullOrWhiteSpace(_checkStatId))
            {
                _checkStatId = PawnRollStats.DefaultStatId;
            }
        }
#endif
    }
}
