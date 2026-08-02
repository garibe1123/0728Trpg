using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// PawnUIManager가 자동으로 설치하는 캐릭터 대시보드 조정자입니다.
    /// 별도 씬 오브젝트를 요구하지 않습니다.
    /// </summary>
    [DefaultExecutionOrder(-180)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PawnUIManager))]
    public sealed class BoardUiStackManager : MonoBehaviour
    {
        private const float L2OpenDuration = 0.16f;
        private const float L2CloseDuration = 0.14f;
        private const float ContentDelay = 0.04f;
        private const float ContentFadeDuration = 0.12f;
        private const float RollOpenDuration = 0.14f;
        private const float RollCloseDuration = 0.10f;

        [SerializeField] private PawnUIManager _pawnUiManager;
        [SerializeField] private PawnCheckRollManager _checkRollManager;

        private readonly BoardUiStateMachine _stateMachine =
            new BoardUiStateMachine();
        private PawnInfoBarWidget _infoBar;
        private PawnManager _pawnManager;
        private BoardUiStackWidget _widget;
        private Sequence _transition;
        private Coroutine _connectRoutine;
        private Coroutine _embedRoutine;
        private Coroutine _relayRoutine;
        private BoardUiLayout _layout;
        private Vector2Int _lastScreenSize;
        private float _screenStableSince;
        private bool _connected;
        private bool _embeddedWidgetsReady;
        private bool _dropAccepted;
        private BoardLeftPane _leftPane = BoardLeftPane.Identity;
        private BoardRightPane _rightPane = BoardRightPane.Stats;
        private PawnCheckSourceData _dragSource;

        private PawnStatPanelWidget _statPanel;
        private PawnSkillPanelWidget _skillPanel;
        private RectTransform _statDrawer;
        private RectTransform _skillRect;
        private RectTransform _statSummary;
        private Button _legacySkillToggle;
        private Transform _statTitle;
        private bool _statTitleWasActive;
        private Color _statDrawerColor;
        private Color _skillBackgroundColor;
        private RectTransformSnapshot _statDrawerSnapshot;
        private RectTransformSnapshot _skillSnapshot;
        private bool _statSummaryWasActive;
        private bool _legacySkillToggleWasActive;

        public BoardUiState CurrentState => _stateMachine.State;
        public bool IsConnected => _connected;

        public void Configure(PawnUIManager uiManager)
        {
            if (uiManager != null)
                _pawnUiManager = uiManager;
        }

        private void OnEnable()
        {
            _stateMachine.Changed += HandleStateChanged;
            PawnRollSourceWidget.SourceSelected +=
                HandleGlobalSourceSelected;
            BeginConnect();
        }

        private void OnDisable()
        {
            _stateMachine.Changed -= HandleStateChanged;
            PawnRollSourceWidget.SourceSelected -=
                HandleGlobalSourceSelected;
            Disconnect();
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private void Update()
        {
            if (!_connected)
                return;

            HandleEscape();
            SyncWalkState();
            RefreshLayoutWhenStable();
        }

        public void RequestTab(SheetTab tab)
        {
            if (!_connected || tab == SheetTab.None)
                return;

            if (_pawnManager != null &&
                _pawnManager.IsMovementModeActive)
            {
                _pawnManager.SetMovementMode(false);
            }

            ApplyFocusForTab(tab);
            _stateMachine.ClickTab(tab);
        }

        public void RequestCheckRoll()
        {
            if (!_connected)
                return;

            if (_pawnManager != null &&
                _pawnManager.IsMovementModeActive)
            {
                _pawnManager.SetMovementMode(false);
            }

            _rightPane = BoardRightPane.Stats;
            _stateMachine.ClickCheckRoll();
            EnsureCheckSession();
            _widget.ShowEmptyRollPanel();
        }

        private void BeginConnect()
        {
            if (_connectRoutine != null)
                StopCoroutine(_connectRoutine);
            _connectRoutine = StartCoroutine(ConnectWhenReady());
        }

        private IEnumerator ConnectWhenReady()
        {
            for (var frame = 0; frame < 240; frame++)
            {
                if (_pawnUiManager == null)
                    _pawnUiManager = GetComponent<PawnUIManager>();
                if (_pawnUiManager != null)
                {
                    _infoBar = _pawnUiManager.InfoBar;
                    _pawnManager = _pawnUiManager.PawnManager;
                }
                if (_checkRollManager == null)
                    _checkRollManager = FindFirst<PawnCheckRollManager>();

                if (_pawnUiManager != null &&
                    _infoBar != null &&
                    _infoBar.FullCanvasRect != null)
                {
                    Connect();
                    _connectRoutine = null;
                    yield break;
                }

                yield return null;
            }

            Debug.LogError(
                "[BoardUiStackManager] Pawn UI 연결에 실패했습니다. " +
                "기존 UI는 유지됩니다.",
                this);
            _connectRoutine = null;
        }

        private void Connect()
        {
            if (_connected)
                return;

            _widget = BoardUiStackWidget.CreateRuntime(
                _infoBar.FullCanvasRect,
                _infoBar.UiFont);
            _widget.RootRect.SetAsLastSibling();
            _layout = BoardUiLayoutCalculator.Calculate(
                Screen.width,
                Screen.height);
            ApplyStackLayout();

            BindInfoBar();
            BindWidget();
            _pawnUiManager.RegisterBoardStack(this);
            _checkRollManager?.RegisterBoardStack(this);

            if (_infoBar.HasBoardStackInfo)
                _widget.BindInfo(_infoBar.BoardStackInfo);

            _connected = true;
            ApplyStateImmediate(_stateMachine.State);
        }

        private void Disconnect()
        {
            StopRoutine(ref _connectRoutine);
            StopRoutine(ref _embedRoutine);
            StopRoutine(ref _relayRoutine);

            if (_transition != null)
            {
                _transition.Kill(true);
                _transition = null;
            }

            RestoreStatPanel();
            UnbindInfoBar();
            UnbindWidget();
            _checkRollManager?.UnregisterBoardStack(this);
            _pawnUiManager?.UnregisterBoardStack(this);
            _connected = false;
            _embeddedWidgetsReady = false;

            if (_widget != null)
            {
                Destroy(_widget.gameObject);
                _widget = null;
            }
        }

        private void BindInfoBar()
        {
            _infoBar.BoardStackStatsRequested += HandleStatsRequested;
            _infoBar.BoardStackBagRequested += HandleBagRequested;
            _infoBar.BoardStackProfileRequested += HandleProfileRequested;
            _infoBar.BoardStackInfoChanged += HandleInfoChanged;
            _infoBar.BoardStackStatsChanged += HandleStatsChanged;
            _infoBar.BoardStackMovementChanged += HandleMovementChanged;
            _infoBar.BoardStackUnbound += HandleUnbound;
            _infoBar.MoveRequested += HandleMoveRequested;
        }

        private void UnbindInfoBar()
        {
            if (_infoBar == null)
                return;

            _infoBar.BoardStackStatsRequested -= HandleStatsRequested;
            _infoBar.BoardStackBagRequested -= HandleBagRequested;
            _infoBar.BoardStackProfileRequested -= HandleProfileRequested;
            _infoBar.BoardStackInfoChanged -= HandleInfoChanged;
            _infoBar.BoardStackStatsChanged -= HandleStatsChanged;
            _infoBar.BoardStackMovementChanged -= HandleMovementChanged;
            _infoBar.BoardStackUnbound -= HandleUnbound;
            _infoBar.MoveRequested -= HandleMoveRequested;
        }

        private void BindWidget()
        {
            _widget.LeftPaneRequested += HandleLeftPaneRequested;
            _widget.RightPaneRequested += HandleRightPaneRequested;
            _widget.SourceDropped += HandleSourceDropped;
            _widget.DifficultyRequested += HandleDifficultyRequested;
            _widget.RollRequested += HandleRollRequested;
        }

        private void UnbindWidget()
        {
            if (_widget == null)
                return;

            _widget.LeftPaneRequested -= HandleLeftPaneRequested;
            _widget.RightPaneRequested -= HandleRightPaneRequested;
            _widget.SourceDropped -= HandleSourceDropped;
            _widget.DifficultyRequested -= HandleDifficultyRequested;
            _widget.RollRequested -= HandleRollRequested;
        }

        private void HandleStatsRequested()
        {
            _leftPane = BoardLeftPane.Identity;
            _rightPane = BoardRightPane.Stats;
            RequestTab(SheetTab.Stats);
        }

        private void HandleBagRequested()
        {
            _leftPane = BoardLeftPane.Inventory;
            RequestTab(SheetTab.Bag);
        }

        private void HandleProfileRequested()
        {
            _leftPane = BoardLeftPane.Profile;
            RequestTab(SheetTab.Profile);
        }

        private void HandleLeftPaneRequested(BoardLeftPane pane)
        {
            _leftPane = pane;
            EnsureSheetOpen(pane == BoardLeftPane.Inventory
                ? SheetTab.Bag
                : pane == BoardLeftPane.Profile
                    ? SheetTab.Profile
                    : SheetTab.Stats);
            _widget.SetLeftPane(_leftPane);
        }

        private void HandleRightPaneRequested(BoardRightPane pane)
        {
            _rightPane = pane;
            EnsureSheetOpen(SheetTab.Stats);
            _widget.SetRightPane(_rightPane);
            StartRelayBinding();
        }

        private void EnsureSheetOpen(SheetTab focus)
        {
            if (_stateMachine.State.Mode == BoardMode.Sheet)
            {
                var detail = _stateMachine.State.Detail;
                _stateMachine.Force(new BoardUiState(
                    BoardMode.Sheet,
                    focus,
                    focus == SheetTab.Stats
                        ? detail
                        : SheetDetail.None,
                    _stateMachine.State.Popover,
                    _stateMachine.State.Modal));
                return;
            }

            _stateMachine.Force(new BoardUiState(
                BoardMode.Sheet,
                focus));
        }

        private void ApplyFocusForTab(SheetTab tab)
        {
            switch (tab)
            {
                case SheetTab.Stats:
                    _leftPane = BoardLeftPane.Identity;
                    _rightPane = BoardRightPane.Stats;
                    break;
                case SheetTab.Bag:
                    _leftPane = BoardLeftPane.Inventory;
                    break;
                case SheetTab.Profile:
                    _leftPane = BoardLeftPane.Profile;
                    break;
            }
        }

        private void HandleInfoChanged(PawnInfoBarData data)
        {
            _widget.gameObject.SetActive(true);
            _widget.BindInfo(data);
        }

        private void HandleStatsChanged(PawnStatPanelData data)
        {
            if (_stateMachine.State.Mode == BoardMode.Sheet ||
                _embeddedWidgetsReady)
            {
                StartEmbedding();
            }
        }

        private void HandleMovementChanged(float remaining, float maximum)
        {
            _widget.SetMovement(remaining, maximum);
        }

        private void HandleUnbound()
        {
            _stateMachine.ForceIdle();
            _widget.ClearInfo();
            _widget.gameObject.SetActive(false);
        }

        private void HandleMoveRequested()
        {
            var state = _stateMachine.State;
            if (_pawnManager != null && _pawnManager.IsMovementModeActive)
            {
                if (state.Mode != BoardMode.Walk)
                    _stateMachine.Force(new BoardUiState(BoardMode.Walk));
            }
            else if (state.Mode == BoardMode.Walk)
            {
                _stateMachine.ForceIdle();
            }
            else
            {
                _stateMachine.ClickWalk();
            }
        }

        private void HandleSourceDropped(PawnCheckSourceData source)
        {
            _dropAccepted = true;
            OpenRollForSource(source);
        }

        private void HandleGlobalSourceSelected(PawnCheckSourceData source)
        {
            if (!_connected ||
                _stateMachine.State.Mode != BoardMode.Sheet ||
                !source.IsValid)
            {
                return;
            }

            OpenRollForSource(source);
        }

        private void OpenRollForSource(PawnCheckSourceData source)
        {
            if (!source.IsValid)
                return;

            _rightPane = source.SourceKind == PawnRollSourceKind.Skill
                ? BoardRightPane.Skills
                : BoardRightPane.Stats;
            _widget.SetRightPane(_rightPane);
            _widget.SelectSource(source);
            _stateMachine.SelectRollSource();
            EnsureCheckSession();
            _checkRollManager?.SelectSourceFromBoardStack(source);
        }

        private void HandleDifficultyRequested(
            PawnCheckDifficulty difficulty)
        {
            _checkRollManager?.SetDifficultyFromBoardStack(difficulty);
        }

        private void HandleRollRequested()
        {
            EnsureCheckSession();
            if (_widget.SelectedSource.IsValid)
            {
                _checkRollManager?.SelectSourceFromBoardStack(
                    _widget.SelectedSource);
                _checkRollManager?.SetDifficultyFromBoardStack(
                    _widget.SelectedDifficulty);
            }
            _checkRollManager?.RollFromBoardStack();
        }

        private void HandleDragBegin(PawnCheckSourceData source)
        {
            if (!source.IsValid)
                return;

            _dragSource = source;
            _dropAccepted = false;
            EnsureSheetOpen(SheetTab.Stats);
            _widget.ShowDragTarget(source);
        }

        private void HandleDragEnd()
        {
            if (!_dropAccepted &&
                _stateMachine.State.Detail !=
                SheetDetail.RollRoulette)
            {
                _widget.HideDragTarget();
            }

            _dragSource = default;
            _dropAccepted = false;
        }

        private void EnsureCheckSession()
        {
            if (_checkRollManager == null)
                _checkRollManager = FindFirst<PawnCheckRollManager>();
            if (_checkRollManager == null || _pawnUiManager == null)
                return;

            _checkRollManager.RegisterBoardStack(this);
            _checkRollManager.OpenFromBoardStack(
                _pawnUiManager.BoundStatState);
            StartRelayBinding();
        }

        private void HandleStateChanged(
            BoardUiState previous,
            BoardUiState next)
        {
            if (!_connected || _widget == null)
                return;

            BuildTransition(previous, next);
        }

        private void BuildTransition(
            BoardUiState previous,
            BoardUiState next)
        {
            if (_transition != null)
            {
                _transition.Kill(true);
                _transition = null;
            }

            PrepareContent(next);
            _widget.RootCanvasGroup.blocksRaycasts = false;
            var sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            var wasSheet = previous.Mode == BoardMode.Sheet;
            var isSheet = next.Mode == BoardMode.Sheet;
            if (!wasSheet && isSheet)
            {
                _widget.gameObject.SetActive(true);
                _widget.LeftMask.sizeDelta = new Vector2(
                    _layout.LeftWidth,
                    0f);
                _widget.RightMask.sizeDelta = new Vector2(
                    _layout.RightWidth,
                    0f);
                _widget.LeftContentGroup.alpha = 0f;
                _widget.RightContentGroup.alpha = 0f;

                sequence.Insert(
                    0f,
                    _widget.LeftMask.DOSizeDelta(
                        new Vector2(
                            _layout.LeftWidth,
                            _widget.PanelHeight),
                        L2OpenDuration).SetEase(Ease.OutCubic));
                sequence.Insert(
                    0f,
                    _widget.RightMask.DOSizeDelta(
                        new Vector2(
                            _layout.RightWidth,
                            _widget.PanelHeight),
                        L2OpenDuration).SetEase(Ease.OutCubic));
                sequence.Insert(
                    ContentDelay,
                    _widget.LeftContentGroup.DOFade(
                        1f,
                        ContentFadeDuration));
                sequence.Insert(
                    ContentDelay,
                    _widget.RightContentGroup.DOFade(
                        1f,
                        ContentFadeDuration));
            }
            else if (wasSheet && !isSheet)
            {
                sequence.Insert(
                    0f,
                    _widget.LeftContentGroup.DOFade(0f, 0.09f));
                sequence.Insert(
                    0f,
                    _widget.RightContentGroup.DOFade(0f, 0.09f));
                sequence.Insert(
                    0f,
                    _widget.LeftMask.DOSizeDelta(
                        new Vector2(_layout.LeftWidth, 0f),
                        L2CloseDuration).SetEase(Ease.InCubic));
                sequence.Insert(
                    0f,
                    _widget.RightMask.DOSizeDelta(
                        new Vector2(_layout.RightWidth, 0f),
                        L2CloseDuration).SetEase(Ease.InCubic));
            }

            var wasRoll = previous.Detail == SheetDetail.RollRoulette;
            var isRoll = next.Detail == SheetDetail.RollRoulette;
            if (!wasRoll && isRoll)
            {
                if (_widget.SelectedSource.IsValid)
                    _widget.PrepareRollPanel(_widget.SelectedSource);
                else
                    _widget.ShowEmptyRollPanel();

                _widget.RollOverlayMask.sizeDelta = new Vector2(
                    _widget.RollOverlayContent.sizeDelta.x,
                    0f);
                _widget.RollOverlayGroup.alpha = 0f;
                sequence.Insert(
                    0f,
                    _widget.RollOverlayMask.DOSizeDelta(
                        new Vector2(
                            _widget.RollOverlayContent.sizeDelta.x,
                            350f),
                        RollOpenDuration).SetEase(Ease.OutCubic));
                sequence.Insert(
                    ContentDelay,
                    _widget.RollOverlayGroup.DOFade(
                        1f,
                        0.10f));
            }
            else if (wasRoll && !isRoll)
            {
                sequence.Insert(
                    0f,
                    _widget.RollOverlayGroup.DOFade(0f, 0.07f));
                sequence.Insert(
                    0f,
                    _widget.RollOverlayMask.DOSizeDelta(
                        new Vector2(
                            _widget.RollOverlayContent.sizeDelta.x,
                            0f),
                        RollCloseDuration).SetEase(Ease.InCubic));
            }

            var finish = Mathf.Max(
                L2OpenDuration,
                Mathf.Max(L2CloseDuration, RollOpenDuration));
            sequence.InsertCallback(
                finish,
                () =>
                {
                    FinalizeContent(next);
                    if (_widget != null)
                        _widget.RootCanvasGroup.blocksRaycasts = true;
                });
            _transition = sequence;
        }

        private void PrepareContent(BoardUiState state)
        {
            if (state.Mode != BoardMode.Sheet)
                return;

            ActivateDashboard();
            _widget.SetLeftPane(_leftPane);
            _widget.SetRightPane(_rightPane);
        }

        private void FinalizeContent(BoardUiState state)
        {
            if (state.Mode == BoardMode.Sheet)
            {
                ActivateDashboard();
                _widget.SetLeftPane(_leftPane);
                _widget.SetRightPane(_rightPane);
            }
            else
            {
                HideEmbeddedWidgets();
            }

            if (state.Detail != SheetDetail.RollRoulette)
                _widget.HideRollOverlayImmediate();
        }

        private void ApplyStateImmediate(BoardUiState state)
        {
            if (_widget == null)
                return;

            var visible = state.Mode == BoardMode.Sheet;
            _widget.SetPanelsImmediate(visible);
            if (visible)
            {
                ActivateDashboard();
                _widget.SetLeftPane(_leftPane);
                _widget.SetRightPane(_rightPane);
            }
            else
            {
                HideEmbeddedWidgets();
            }

            if (state.Detail == SheetDetail.RollRoulette)
            {
                if (_widget.SelectedSource.IsValid)
                    _widget.PrepareRollPanel(_widget.SelectedSource);
                else
                    _widget.ShowEmptyRollPanel();
                _widget.RollOverlayMask.sizeDelta = new Vector2(
                    _widget.RollOverlayContent.sizeDelta.x,
                    350f);
                _widget.RollOverlayGroup.alpha = 1f;
            }
            else
            {
                _widget.HideRollOverlayImmediate();
            }
        }

        private void ActivateDashboard()
        {
            if (!_embeddedWidgetsReady)
            {
                _pawnUiManager.ShowInventoryInBoardStack(
                    _widget.BagHost);
                _pawnUiManager.ShowProfileInBoardStack(
                    _widget.ProfileHost);
                StartEmbedding();
                _embeddedWidgetsReady = true;
            }

            EnsureCheckSession();
            _widget.SetLeftPane(_leftPane);
            _widget.SetRightPane(_rightPane);
        }

        private void HideEmbeddedWidgets()
        {
            StopRoutine(ref _embedRoutine);
            StopRoutine(ref _relayRoutine);
            _pawnUiManager?.HideInventoryFromBoardStack();
            _pawnUiManager?.HideProfileFromBoardStack();
            RestoreStatPanel();
            _embeddedWidgetsReady = false;
        }

        private void StartEmbedding()
        {
            StopRoutine(ref _embedRoutine);
            _embedRoutine = StartCoroutine(EmbedAfterLayout());
        }

        private IEnumerator EmbedAfterLayout()
        {
            yield return null;
            yield return null;
            EmbedStatAndSkillPanels();
            StartRelayBinding();
            _embedRoutine = null;
        }

        private void EmbedStatAndSkillPanels()
        {
            _statPanel = _infoBar != null
                ? _infoBar.BoardStackStatPanel
                : null;
            if (_statPanel == null || _widget == null)
                return;

            _statPanel.SetExpanded(true);
            _statDrawer = ReadField<RectTransform>(
                _statPanel,
                "_drawerRect");
            _statSummary = ReadField<RectTransform>(
                _statPanel,
                "_summaryRect");
            _skillPanel = ReadField<PawnSkillPanelWidget>(
                _statPanel,
                "_skillPanel");
            _legacySkillToggle = ReadField<Button>(
                _statPanel,
                "_skillToggleButton");

            var firstEmbed = _statDrawerSnapshot == null;
            if (_statDrawer != null)
            {
                if (_statDrawerSnapshot == null)
                    _statDrawerSnapshot = new RectTransformSnapshot(_statDrawer);
                if (firstEmbed)
                {
                    var originalBackground =
                        _statDrawer.GetComponent<Image>();
                    if (originalBackground != null)
                        _statDrawerColor = originalBackground.color;
                    _statTitle = FindDescendant(_statDrawer, "Title");
                    if (_statTitle != null)
                    {
                        _statTitleWasActive =
                            _statTitle.gameObject.activeSelf;
                    }
                }
                _statDrawer.DOKill(true);
                _statDrawer.SetParent(_widget.StatsHost, false);
                Stretch(_statDrawer);
                _statDrawer.gameObject.SetActive(true);
                var group = ReadField<CanvasGroup>(
                    _statPanel,
                    "_drawerCanvasGroup");
                if (group != null)
                {
                    group.alpha = 1f;
                    group.interactable = true;
                    group.blocksRaycasts = true;
                }
                DisableDragHandles(_statDrawer);
                ApplyEmbeddedStatLayout();
            }

            if (_statSummary != null)
            {
                if (firstEmbed)
                {
                    _statSummaryWasActive =
                        _statSummary.gameObject.activeSelf;
                }
                _statSummary.gameObject.SetActive(false);
            }

            if (_legacySkillToggle != null)
            {
                if (firstEmbed)
                {
                    _legacySkillToggleWasActive =
                        _legacySkillToggle.gameObject.activeSelf;
                }
                _legacySkillToggle.gameObject.SetActive(false);
            }

            if (_skillPanel != null)
            {
                _skillPanel.SetExpanded(true, false);
                _skillRect = _skillPanel.transform as RectTransform;
                if (_skillRect != null)
                {
                    if (_skillSnapshot == null)
                        _skillSnapshot = new RectTransformSnapshot(_skillRect);
                    _skillRect.DOKill(true);
                    _skillRect.SetParent(_widget.SkillsHost, false);
                    Stretch(_skillRect);
                    _skillRect.gameObject.SetActive(true);
                    var group = _skillRect.GetComponent<CanvasGroup>();
                    if (group != null)
                    {
                        group.alpha = 1f;
                        group.interactable = true;
                        group.blocksRaycasts = true;
                    }
                    var background = _skillRect.GetComponent<Image>();
                    if (background != null)
                    {
                        if (firstEmbed)
                            _skillBackgroundColor = background.color;
                        background.color = Color.clear;
                    }
                    DisableDragHandles(_skillRect);
                }
            }

            _widget.SetRightPane(_rightPane);
            _widget.BringTabsToFront();
        }

        private void ApplyEmbeddedStatLayout()
        {
            if (_statPanel == null || _statDrawer == null)
                return;

            var background = _statDrawer.GetComponent<Image>();
            if (background != null)
                background.color = Color.clear;

            _statTitle = FindDescendant(_statDrawer, "Title");
            if (_statTitle != null)
                _statTitle.gameObject.SetActive(false);

            var hostHeight = _widget != null &&
                             _widget.StatsHost != null
                ? Mathf.Max(420f, _widget.StatsHost.rect.height)
                : 760f;
            var chartHeight = Mathf.Clamp(
                hostHeight * 0.36f,
                250f,
                340f);
            var headerTop = chartHeight + 12f;

            var chart = ReadField<RectTransform>(
                _statPanel,
                "_chartRect");
            if (chart != null)
            {
                chart.anchorMin = new Vector2(0f, 1f);
                chart.anchorMax = new Vector2(1f, 1f);
                chart.pivot = new Vector2(0.5f, 1f);
                chart.anchoredPosition = new Vector2(0f, -8f);
                chart.sizeDelta = new Vector2(-20f, chartHeight);
            }

            var radar = ReadField<RectTransform>(
                _statPanel,
                "_radarRect");
            if (radar != null)
            {
                var radarSize = Mathf.Clamp(
                    chartHeight - 42f,
                    190f,
                    290f);
                radar.sizeDelta = new Vector2(radarSize, radarSize);
            }

            var header = ReadField<RectTransform>(
                _statPanel,
                "_entryHeaderRect");
            if (header != null)
            {
                header.anchorMin = new Vector2(0f, 1f);
                header.anchorMax = new Vector2(1f, 1f);
                header.pivot = new Vector2(0.5f, 1f);
                header.anchoredPosition =
                    new Vector2(0f, -headerTop);
                header.sizeDelta = new Vector2(-20f, 32f);
            }

            var viewport = ReadField<RectTransform>(
                _statPanel,
                "_entryViewportRect");
            if (viewport != null)
            {
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = new Vector2(10f, 10f);
                viewport.offsetMax = new Vector2(
                    -10f,
                    -(headerTop + 42f));
            }

            var scroll = ReadField<ScrollRect>(
                _statPanel,
                "_scrollRect");
            if (scroll != null)
            {
                scroll.vertical = true;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 36f;
            }
        }

        private void RestoreStatPanel()
        {
            if (_skillSnapshot != null)
            {
                _skillSnapshot.Restore();
                _skillSnapshot = null;
            }
            if (_statDrawerSnapshot != null)
            {
                _statDrawerSnapshot.Restore();
                _statDrawerSnapshot = null;
            }
            if (_statSummary != null)
                _statSummary.gameObject.SetActive(_statSummaryWasActive);
            if (_legacySkillToggle != null)
            {
                _legacySkillToggle.gameObject.SetActive(
                    _legacySkillToggleWasActive);
            }
            if (_statTitle != null)
                _statTitle.gameObject.SetActive(_statTitleWasActive);
            if (_statDrawer != null)
            {
                var image = _statDrawer.GetComponent<Image>();
                if (image != null)
                    image.color = _statDrawerColor;
                SetDragHandlesEnabled(_statDrawer, true);
            }
            if (_skillRect != null)
            {
                var image = _skillRect.GetComponent<Image>();
                if (image != null)
                    image.color = _skillBackgroundColor;
                SetDragHandlesEnabled(_skillRect, true);
            }
            _skillPanel?.SetExpanded(false, false);
            _statPanel?.SetExpanded(false);
            _statPanel?.RefreshResponsiveLayout();
            _statPanel = null;
            _skillPanel = null;
            _statDrawer = null;
            _skillRect = null;
            _statSummary = null;
            _legacySkillToggle = null;
            _statTitle = null;
        }

        private void StartRelayBinding()
        {
            StopRoutine(ref _relayRoutine);
            _relayRoutine = StartCoroutine(BindRelaysAfterLayout());
        }

        private IEnumerator BindRelaysAfterLayout()
        {
            yield return null;
            yield return null;
            BindDragRelays(_widget.StatsHost);
            BindDragRelays(_widget.SkillsHost);
            _relayRoutine = null;
        }

        private void BindDragRelays(RectTransform host)
        {
            if (host == null)
                return;

            var sources = host.GetComponentsInChildren<
                PawnRollSourceWidget>(true);
            for (var index = 0; index < sources.Length; index++)
            {
                var source = sources[index];
                if (source == null)
                    continue;
                var relay = source.GetComponent<
                    BoardUiRollSourceDragRelay>();
                if (relay == null)
                {
                    relay = source.gameObject.AddComponent<
                        BoardUiRollSourceDragRelay>();
                }
                relay.Configure(
                    source,
                    HandleDragBegin,
                    HandleDragEnd);
            }
        }

        private void HandleEscape()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null ||
                !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            var before = _stateMachine.State;
            var after = _stateMachine.Escape();
            if (before == after)
                return;

            if (before.Mode == BoardMode.Walk &&
                after.Mode != BoardMode.Walk)
            {
                _pawnManager?.SetMovementMode(false);
            }
        }

        private void SyncWalkState()
        {
            if (_pawnManager == null)
                return;

            var active = _pawnManager.IsMovementModeActive;
            var state = _stateMachine.State;
            if (active && state.Mode != BoardMode.Walk)
            {
                _stateMachine.Force(new BoardUiState(BoardMode.Walk));
            }
            else if (!active && state.Mode == BoardMode.Walk)
            {
                _stateMachine.ForceIdle();
            }
        }

        private void ApplyStackLayout()
        {
            if (_widget == null || _infoBar == null)
                return;

            var bottom = CalculatePanelBottomOffset();
            var rootHeight = Mathf.Max(1f, _widget.RootRect.rect.height);
            var panelHeight = Mathf.Max(420f,
                rootHeight -
                bottom -
                BoardUiLayoutCalculator.TopMargin);
            _widget.ApplyLayout(_layout, bottom, panelHeight);
        }

        private float CalculatePanelBottomOffset()
        {
            var fallback = BoardUiLayoutCalculator.BottomOffset;
            if (_infoBar == null ||
                _infoBar.PanelRect == null ||
                _widget == null ||
                _widget.RootRect == null)
            {
                return fallback;
            }

            var canvas = _infoBar.FullCanvasRect != null
                ? _infoBar.FullCanvasRect.GetComponentInParent<Canvas>()
                : null;
            var camera = canvas != null &&
                         canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            var barRect = _infoBar.PanelRect;
            var worldTop = barRect.TransformPoint(new Vector3(
                barRect.rect.center.x,
                barRect.rect.yMax,
                0f));
            var screenTop = RectTransformUtility.WorldToScreenPoint(
                camera,
                worldTop);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _widget.RootRect,
                    screenTop,
                    camera,
                    out var localTop))
            {
                return fallback;
            }

            var fromBottom =
                localTop.y - _widget.RootRect.rect.yMin + 3f;
            return Mathf.Clamp(
                fromBottom,
                96f,
                Mathf.Max(96f,
                    _widget.RootRect.rect.height - 420f));
        }

        private void RefreshLayoutWhenStable()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size != _lastScreenSize)
            {
                _lastScreenSize = size;
                _screenStableSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _screenStableSince < 0.10f)
                return;

            var next = BoardUiLayoutCalculator.Calculate(
                size.x,
                size.y);
            var nextBottom = CalculatePanelBottomOffset();
            var widthUnchanged =
                next.Band == _layout.Band &&
                Mathf.Abs(next.LeftWidth - _layout.LeftWidth) < 0.5f &&
                Mathf.Abs(next.RightWidth - _layout.RightWidth) < 0.5f;
            var bottomUnchanged = _widget == null ||
                Mathf.Abs(
                    nextBottom - _widget.PanelBottomOffset) < 1f;
            if (widthUnchanged && bottomUnchanged)
                return;

            _layout = next;
            ApplyStackLayout();
            if (_statDrawer != null)
                ApplyEmbeddedStatLayout();
            ApplyStateImmediate(_stateMachine.State);
        }

        private static void DisableDragHandles(RectTransform root)
        {
            SetDragHandlesEnabled(root, false);
        }

        private static void SetDragHandlesEnabled(
            RectTransform root,
            bool enabled)
        {
            if (root == null)
                return;
            var handles = root.GetComponentsInChildren<
                PawnUiDragHandle>(true);
            for (var index = 0; index < handles.Length; index++)
            {
                if (handles[index] != null)
                    handles[index].enabled = enabled;
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName)
        {
            if (root == null)
                return null;
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (string.Equals(
                        child.name,
                        objectName,
                        StringComparison.Ordinal))
                {
                    return child;
                }
                var nested = FindDescendant(child, objectName);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static T ReadField<T>(object owner, string fieldName)
            where T : class
        {
            if (owner == null)
                return null;
            var type = owner.GetType();
            while (type != null)
            {
                var field = type.GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                if (field != null)
                    return field.GetValue(owner) as T;
                type = type.BaseType;
            }
            return null;
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
                return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine == null)
                return;
            StopCoroutine(routine);
            routine = null;
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

        private sealed class RectTransformSnapshot
        {
            private readonly RectTransform _rect;
            private readonly Transform _parent;
            private readonly int _sibling;
            private readonly Vector2 _anchorMin;
            private readonly Vector2 _anchorMax;
            private readonly Vector2 _pivot;
            private readonly Vector2 _anchoredPosition;
            private readonly Vector2 _sizeDelta;
            private readonly Vector2 _offsetMin;
            private readonly Vector2 _offsetMax;
            private readonly Vector3 _localScale;
            private readonly bool _active;

            public RectTransformSnapshot(RectTransform rect)
            {
                _rect = rect;
                _parent = rect.parent;
                _sibling = rect.GetSiblingIndex();
                _anchorMin = rect.anchorMin;
                _anchorMax = rect.anchorMax;
                _pivot = rect.pivot;
                _anchoredPosition = rect.anchoredPosition;
                _sizeDelta = rect.sizeDelta;
                _offsetMin = rect.offsetMin;
                _offsetMax = rect.offsetMax;
                _localScale = rect.localScale;
                _active = rect.gameObject.activeSelf;
            }

            public void Restore()
            {
                if (_rect == null)
                    return;
                _rect.SetParent(_parent, false);
                if (_rect.parent != null)
                {
                    _rect.SetSiblingIndex(Mathf.Clamp(
                        _sibling,
                        0,
                        _rect.parent.childCount - 1));
                }
                _rect.anchorMin = _anchorMin;
                _rect.anchorMax = _anchorMax;
                _rect.pivot = _pivot;
                _rect.anchoredPosition = _anchoredPosition;
                _rect.sizeDelta = _sizeDelta;
                _rect.offsetMin = _offsetMin;
                _rect.offsetMax = _offsetMax;
                _rect.localScale = _localScale;
                _rect.gameObject.SetActive(_active);
            }
        }
    }
}
