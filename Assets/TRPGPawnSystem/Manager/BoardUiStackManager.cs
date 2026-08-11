using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trpg.Pawns
{
    /// <summary>
    /// PawnUIManager가 같은 GameObject에 자동 설치하는 좌우 캐릭터 대시보드입니다.
    /// 씬에 별도 오브젝트를 요구하지 않으며 기존 위젯의 데이터 소유권을 유지합니다.
    /// </summary>
    [DefaultExecutionOrder(-180)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PawnUIManager))]
    public sealed class BoardUiStackManager : MonoBehaviour
    {
        private const float SheetOpenDuration = 0.18f;
        private const float SheetCloseDuration = 0.14f;
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
        private PawnStatPanelWidget _statPanel;
        private BoardUiStackWidget _widget;
        private Sequence _transition;
        private Coroutine _connectRoutine;
        private BoardUiLayout _layout;
        private Vector2Int _lastScreenSize;
        private float _screenStableSince;
        private bool _connected;
        private bool _embeddedWidgetsReady;
        private bool _dropAccepted;
        private bool _effectRollActive;
        private bool _hadRollBeforeDrag;
        private bool _effectRollBeforeDrag;
        private BoardLeftPane _leftPane = BoardLeftPane.Identity;
        private BoardRightPane _rightPane = BoardRightPane.Stats;

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
            PawnRollSourceWidget.DragStarted += HandleDragStarted;
            PawnRollSourceWidget.DragEnded += HandleDragEnded;
            BeginConnect();
        }

        private void OnDisable()
        {
            _pawnUiManager?.FlushPendingEdits();
            _stateMachine.Changed -= HandleStateChanged;
            PawnRollSourceWidget.DragStarted -= HandleDragStarted;
            PawnRollSourceWidget.DragEnded -= HandleDragEnded;
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
            if (!_connected ||
                tab == SheetTab.None ||
                _infoBar == null ||
                !CanOpenTab(tab))
            {
                return;
            }

            _pawnUiManager?.FlushPendingEdits();
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
            if (!_connected ||
                _infoBar == null ||
                !_infoBar.HasStats ||
                !_pawnUiManager.CanCurrentCharacterRoll)
            {
                return;
            }

            if (_pawnManager != null &&
                _pawnManager.IsMovementModeActive)
            {
                _pawnManager.SetMovementMode(false);
            }

            _effectRollActive = false;
            EnsureSheetOpen(SheetTab.Stats);
            EnsureCheckSession();
            _widget.ShowEmptyRollPanel();
            _stateMachine.ClickCheckRoll();
        }

        public bool RollRandomSource(
            PawnCheckSourceData source)
        {
            if (!_connected ||
                !source.IsValid ||
                _infoBar == null ||
                !_infoBar.HasStats ||
                !_pawnUiManager.CanCurrentCharacterRoll)
            {
                return false;
            }

            if (_pawnManager != null &&
                _pawnManager.IsMovementModeActive)
            {
                _pawnManager.SetMovementMode(false);
            }

            OpenRollForSource(source);
            _checkRollManager?.SetDifficultyFromBoardStack(
                PawnCheckDifficulty.Regular);
            return _checkRollManager != null &&
                   _checkRollManager.RollFromBoardStack();
        }

        public void RequestEffectRoll()
        {
            if (!_connected ||
                _infoBar == null ||
                !_infoBar.HasStats ||
                !_pawnUiManager.CanCurrentCharacterRoll)
            {
                return;
            }

            if (_pawnManager != null &&
                _pawnManager.IsMovementModeActive)
            {
                _pawnManager.SetMovementMode(false);
            }

            _effectRollActive = true;
            EnsureSheetOpen(SheetTab.Stats);
            _widget.ShowEffectRollPanel(
                _pawnUiManager.EffectDiceCount,
                _pawnUiManager.EffectDiceSides,
                _pawnUiManager.EffectDiceModifier);
            _stateMachine.ClickCheckRoll();
        }

        /// <summary>
        /// 네트워크에서 수신한 굴림을 기존 Stats/RollRoulette 탭에
        /// 읽기 전용으로 표시할 준비를 합니다.
        /// </summary>
        public bool PresentRemoteRollPanel()
        {
            if (!_connected || _widget == null)
                return false;

            if (_pawnManager != null &&
                _pawnManager.IsMovementModeActive)
            {
                _pawnManager.SetMovementMode(false);
            }

            _effectRollActive = false;
            EnsureSheetOpen(SheetTab.Stats);
            _widget.ShowEmptyRollPanel();
            _stateMachine.ClickCheckRoll();
            return true;
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

            _widget.gameObject.SetActive(
                _infoBar.HasBoardStackInfo);
            _connected = true;
            ApplyStateImmediate(_stateMachine.State);
        }

        private void Disconnect()
        {
            if (_connectRoutine != null)
            {
                StopCoroutine(_connectRoutine);
                _connectRoutine = null;
            }

            if (_transition != null)
            {
                _transition.Kill(true);
                _transition = null;
            }

            HideEmbeddedWidgets();
            UnbindInfoBar();
            UnbindWidget();
            _checkRollManager?.UnregisterBoardStack(this);
            _pawnUiManager?.UnregisterBoardStack(this);
            _connected = false;

            if (_widget != null)
            {
                Destroy(_widget.gameObject);
                _widget = null;
            }
        }

        private void BindInfoBar()
        {
            _infoBar.BoardStackIdentityRequested +=
                HandleIdentityRequested;
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

            _infoBar.BoardStackIdentityRequested -=
                HandleIdentityRequested;
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
            _widget.CheckRollRequested += RequestCheckRoll;
            _widget.EffectRollRequested += RequestEffectRoll;
            _widget.EffectRollConfirmed += HandleEffectRollConfirmed;
            _widget.RollOverlayCloseRequested +=
                HandleRollOverlayCloseRequested;
            _widget.SourceDropped += HandleSourceDropped;
            _widget.DifficultyRequested += HandleDifficultyRequested;
            _widget.RollRequested += HandleRollRequested;
            _widget.BonusPenaltyChanged +=
                HandleBonusPenaltyChanged;
            _widget.VisibilityChanged +=
                HandleRollVisibilityChanged;
        }

        private void UnbindWidget()
        {
            if (_widget == null)
                return;

            _widget.LeftPaneRequested -= HandleLeftPaneRequested;
            _widget.RightPaneRequested -= HandleRightPaneRequested;
            _widget.CheckRollRequested -= RequestCheckRoll;
            _widget.EffectRollRequested -= RequestEffectRoll;
            _widget.EffectRollConfirmed -= HandleEffectRollConfirmed;
            _widget.RollOverlayCloseRequested -=
                HandleRollOverlayCloseRequested;
            _widget.SourceDropped -= HandleSourceDropped;
            _widget.DifficultyRequested -= HandleDifficultyRequested;
            _widget.RollRequested -= HandleRollRequested;
            _widget.BonusPenaltyChanged -=
                HandleBonusPenaltyChanged;
            _widget.VisibilityChanged -=
                HandleRollVisibilityChanged;
        }


        private bool CanOpenTab(SheetTab tab)
        {
            if (_infoBar == null)
                return false;

            switch (tab)
            {
                case SheetTab.Stats:
                    return _infoBar.HasIdentityDetail;
                case SheetTab.Bag:
                    return _infoBar.HasInventory;
                case SheetTab.Profile:
                    return _infoBar.HasProfile;
                default:
                    return false;
            }
        }

        private void HandleIdentityRequested()
        {
            if (_infoBar == null || !_infoBar.HasIdentityDetail)
                return;

            _pawnUiManager?.FlushPendingEdits();
            if (_pawnManager != null &&
                _pawnManager.IsMovementModeActive)
            {
                _pawnManager.SetMovementMode(false);
            }

            _leftPane = BoardLeftPane.Identity;
            _rightPane = BoardRightPane.Stats;
            EnsureSheetOpen(SheetTab.Stats);
        }

        private void HandleStatsRequested()
        {
            if (_infoBar == null || !_infoBar.HasStats)
                return;

            _leftPane = BoardLeftPane.Identity;
            _rightPane = BoardRightPane.Stats;
            RequestTab(SheetTab.Stats);
        }

        private void HandleBagRequested()
        {
            if (_infoBar == null || !_infoBar.HasInventory)
                return;

            _leftPane = BoardLeftPane.Inventory;
            RequestTab(SheetTab.Bag);
        }

        private void HandleProfileRequested()
        {
            if (_infoBar == null || !_infoBar.HasProfile)
                return;

            _leftPane = BoardLeftPane.Profile;
            RequestTab(SheetTab.Profile);
        }

        private void HandleLeftPaneRequested(BoardLeftPane pane)
        {
            if (pane == BoardLeftPane.Inventory &&
                (_infoBar == null || !_infoBar.HasInventory) ||
                pane == BoardLeftPane.Profile &&
                (_infoBar == null || !_infoBar.HasProfile))
            {
                pane = BoardLeftPane.Identity;
            }

            _leftPane = pane;
            var focus = pane == BoardLeftPane.Inventory
                ? SheetTab.Bag
                : pane == BoardLeftPane.Profile
                    ? SheetTab.Profile
                    : SheetTab.Stats;
            EnsureSheetOpen(focus);
            _widget.SetLeftPane(_leftPane);
        }

        private void HandleRightPaneRequested(BoardRightPane pane)
        {
            if (pane == BoardRightPane.Skills &&
                (_infoBar == null || !_infoBar.BoardStackInfo.HasSkills))
            {
                pane = BoardRightPane.Stats;
            }

            _rightPane = pane;
            _effectRollActive = false;
            _stateMachine.FocusSheet(
                SheetTab.Stats,
                SheetDetail.None);
            _widget.SetRightPane(_rightPane);
            _widget.HideRollOverlayImmediate();
        }

        private void EnsureSheetOpen(SheetTab focus)
        {
            var detail = _stateMachine.State.Mode == BoardMode.Sheet &&
                         focus == SheetTab.Stats
                ? _stateMachine.State.Detail
                : SheetDetail.None;
            _stateMachine.FocusSheet(focus, detail);
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
            if (!data.HasIdentityDetail)
            {
                _pawnUiManager?.FlushPendingEdits();
                HideEmbeddedWidgets();
                _stateMachine.ForceIdle();
                _widget.SetPanelsImmediate(false, false);
                _widget.HideRollOverlayImmediate();
                return;
            }

            if (!data.HasStats)
            {
                _pawnUiManager?.FlushPendingEdits();
                HideEmbeddedWidgets();
                _leftPane = BoardLeftPane.Identity;
                _rightPane = BoardRightPane.Stats;
                _widget.HideRollOverlayImmediate();
            }

            if (!data.HasInventory &&
                _leftPane == BoardLeftPane.Inventory ||
                !data.HasProfile &&
                _leftPane == BoardLeftPane.Profile)
            {
                _leftPane = BoardLeftPane.Identity;
            }

            if (!data.HasSkills)
                _rightPane = BoardRightPane.Stats;

            var state = _stateMachine.State;
            var invalidTab =
                state.Tab == SheetTab.Bag && !data.HasInventory ||
                state.Tab == SheetTab.Profile && !data.HasProfile;
            var invalidDetail =
                !data.HasStats &&
                state.Detail != SheetDetail.None;
            if (state.Mode == BoardMode.Sheet &&
                (invalidTab || invalidDetail))
            {
                _stateMachine.FocusSheet(
                    SheetTab.Stats,
                    SheetDetail.None);
            }
            else if (state.Mode == BoardMode.Sheet)
            {
                _widget.SetLeftPane(_leftPane);
                _widget.SetRightPane(_rightPane);
                _widget.SetPanelsImmediate(true, data.HasStats);
            }
        }

        private void HandleStatsChanged(PawnStatPanelData data)
        {
            if (_statPanel != null && _statPanel.IsEmbedded)
                _statPanel.RefreshResponsiveLayout();
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

        private void HandleDragStarted(PawnCheckSourceData source)
        {
            if (!_connected ||
                !source.IsValid ||
                _infoBar == null ||
                !_infoBar.HasStats ||
                !_pawnUiManager.CanCurrentCharacterRoll)
            {
                return;
            }

            _dropAccepted = false;
            _hadRollBeforeDrag =
                _stateMachine.State.Detail == SheetDetail.RollRoulette;
            _effectRollBeforeDrag = _effectRollActive;
            _effectRollActive = false;
            EnsureSheetOpen(SheetTab.Stats);
            _widget.ShowDragTarget(source);
        }

        private void HandleDragEnded()
        {
            if (!_dropAccepted)
            {
                if (_hadRollBeforeDrag)
                {
                    _effectRollActive = _effectRollBeforeDrag;
                    if (_effectRollActive)
                    {
                        _widget.ShowEffectRollPanel(
                            _pawnUiManager.EffectDiceCount,
                            _pawnUiManager.EffectDiceSides,
                            _pawnUiManager.EffectDiceModifier);
                    }
                    else if (_widget.SelectedSource.IsValid)
                    {
                        _widget.PrepareRollPanel(
                            _widget.SelectedSource);
                    }
                    else
                    {
                        _widget.ShowEmptyRollPanel();
                    }

                    _widget.RollOverlayMask.sizeDelta = new Vector2(
                        _widget.RollOverlayContent.sizeDelta.x,
                        _widget.RollOverlayContent.sizeDelta.y);
                }
                else
                {
                    _widget.HideDragTarget();
                }
            }

            _dropAccepted = false;
            _hadRollBeforeDrag = false;
            _effectRollBeforeDrag = false;
        }

        private void HandleSourceDropped(PawnCheckSourceData source)
        {
            _dropAccepted = true;
            OpenRollForSource(source);
        }

        private void OpenRollForSource(PawnCheckSourceData source)
        {
            if (!source.IsValid)
                return;

            var rollAlreadyOpen =
                _stateMachine.State.Detail == SheetDetail.RollRoulette;
            _effectRollActive = false;
            EnsureSheetOpen(SheetTab.Stats);
            _widget.PrepareRollPanel(source);
            if (rollAlreadyOpen)
            {
                _widget.RollOverlayMask.sizeDelta = new Vector2(
                    _widget.RollOverlayContent.sizeDelta.x,
                    _widget.RollOverlayContent.sizeDelta.y);
            }
            EnsureCheckSession();
            _checkRollManager?.SelectSourceFromBoardStack(source);
            _stateMachine.SelectRollSource();
        }

        private void HandleDifficultyRequested(
            PawnCheckDifficulty difficulty)
        {
            _checkRollManager?.SetDifficultyFromBoardStack(difficulty);
        }

        private void HandleBonusPenaltyChanged(int value)
        {
            EnsureCheckSession();
            _checkRollManager?.SetBonusPenaltyFromBoardStack(value);
        }

        private void HandleRollVisibilityChanged(
            RollVisibility visibility)
        {
            EnsureCheckSession();
            _checkRollManager?.SetVisibilityFromBoardStack(visibility);
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
            _checkRollManager?.SetBonusPenaltyFromBoardStack(
                _widget.BonusPenalty);
            _checkRollManager?.SetVisibilityFromBoardStack(
                _widget.Visibility);
            _checkRollManager?.RollFromBoardStack();
        }

        private void HandleEffectRollConfirmed(
            PawnEffectRollRequest request)
        {
            _pawnUiManager?.RollEffectFromBoardStack(request);
        }

        private void HandleRollOverlayCloseRequested()
        {
            _effectRollActive = false;
            _widget.HideRollOverlayImmediate();
            _stateMachine.FocusSheet(
                SheetTab.Stats,
                SheetDetail.None);
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

            var wasSheet = previous.Mode == BoardMode.Sheet;
            var isSheet = next.Mode == BoardMode.Sheet;
            var hadRoll = previous.Detail == SheetDetail.RollRoulette;
            var hasRoll = next.Detail == SheetDetail.RollRoulette;
            var showRightPanel =
                _infoBar != null && _infoBar.HasStats;

            if (isSheet)
            {
                EnsureEmbeddedWidgets();
                _widget.SetLeftPane(_leftPane);
                _widget.SetRightPane(_rightPane);
            }

            var sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            if (!wasSheet && isSheet)
            {
                _widget.LeftMask.gameObject.SetActive(true);
                _widget.RightMask.gameObject.SetActive(showRightPanel);
                _widget.SetPanelInput(false);
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
                                _widget.LeftPanelHeight),
                            SheetOpenDuration)
                        .SetEase(Ease.OutCubic));
                if (showRightPanel)
                {
                    sequence.Insert(
                        0f,
                        _widget.RightMask.DOSizeDelta(
                                new Vector2(
                                    _layout.RightWidth,
                                    _widget.RightPanelHeight),
                                SheetOpenDuration)
                            .SetEase(Ease.OutCubic));
                }
                sequence.Insert(
                    ContentDelay,
                    _widget.LeftContentGroup.DOFade(
                        1f,
                        ContentFadeDuration));
                if (showRightPanel)
                {
                    sequence.Insert(
                        ContentDelay,
                        _widget.RightContentGroup.DOFade(
                            1f,
                            ContentFadeDuration));
                }
                sequence.InsertCallback(
                    SheetOpenDuration,
                    () => _widget?.SetPanelInput(true));
            }
            else if (wasSheet && !isSheet)
            {
                _widget.SetPanelInput(false);
                sequence.Insert(
                    0f,
                    _widget.LeftContentGroup.DOFade(
                        0f,
                        SheetCloseDuration * 0.72f));
                if (_widget.RightMask.gameObject.activeSelf)
                {
                    sequence.Insert(
                        0f,
                        _widget.RightContentGroup.DOFade(
                            0f,
                            SheetCloseDuration * 0.72f));
                }
                sequence.Insert(
                    0f,
                    _widget.LeftMask.DOSizeDelta(
                            new Vector2(_layout.LeftWidth, 0f),
                            SheetCloseDuration)
                        .SetEase(Ease.InCubic));
                if (_widget.RightMask.gameObject.activeSelf)
                {
                    sequence.Insert(
                        0f,
                        _widget.RightMask.DOSizeDelta(
                                new Vector2(_layout.RightWidth, 0f),
                                SheetCloseDuration)
                            .SetEase(Ease.InCubic));
                }
                sequence.InsertCallback(
                    SheetCloseDuration,
                    () =>
                    {
                        if (_widget != null)
                        {
                            _widget.LeftMask.gameObject.SetActive(false);
                            _widget.RightMask.gameObject.SetActive(false);
                            _widget.HideRollOverlayImmediate();
                        }
                        HideEmbeddedWidgets();
                    });
            }
            else if (isSheet)
            {
                sequence.InsertCallback(
                    0f,
                    () =>
                    {
                        if (_widget == null)
                            return;
                        _widget.SetLeftPane(_leftPane);
                        _widget.SetRightPane(_rightPane);
                        _widget.SetPanelsImmediate(
                            true,
                            _infoBar != null && _infoBar.HasStats);
                        _widget.SetPanelInput(true);
                    });
            }

            if (!hadRoll && hasRoll)
            {
                var startHeight = Mathf.Max(
                    0f,
                    _widget.RollOverlayMask.sizeDelta.y);
                _widget.RollOverlayMask.gameObject.SetActive(true);
                _widget.RollOverlayMask.sizeDelta = new Vector2(
                    _widget.RollOverlayContent.sizeDelta.x,
                    startHeight);
                sequence.Insert(
                    0f,
                    _widget.RollOverlayMask.DOSizeDelta(
                            new Vector2(
                                _widget.RollOverlayContent.sizeDelta.x,
                                _widget.RollOverlayContent.sizeDelta.y),
                            RollOpenDuration)
                        .SetEase(Ease.OutCubic));
            }
            else if (hadRoll && !hasRoll)
            {
                _widget.RollOverlayGroup.interactable = false;
                _widget.RollOverlayGroup.blocksRaycasts = false;
                sequence.Insert(
                    0f,
                    _widget.RollOverlayMask.DOSizeDelta(
                            new Vector2(
                                _widget.RollOverlayContent.sizeDelta.x,
                                0f),
                            RollCloseDuration)
                        .SetEase(Ease.InCubic));
                sequence.InsertCallback(
                    RollCloseDuration,
                    () => _widget?.HideRollOverlayImmediate());
            }

            _transition = sequence;
        }

        private void EnsureEmbeddedWidgets()
        {
            if (_embeddedWidgetsReady ||
                _infoBar == null ||
                !_infoBar.HasStats)
            {
                return;
            }

            if (_infoBar.HasInventory)
            {
                _pawnUiManager.ShowInventoryInBoardStack(
                    _widget.BagHost);
            }

            if (_infoBar.HasProfile)
            {
                _pawnUiManager.ShowProfileInBoardStack(
                    _widget.ProfileHost);
            }

            _statPanel = _infoBar.BoardStackStatPanel;
            if (_statPanel != null)
            {
                _statPanel.SetEmbeddedMode(
                    _widget.StatsHost,
                    _widget.SkillsHost,
                    true);
                _statPanel.RefreshResponsiveLayout();
            }

            EnsureCheckSession();
            _embeddedWidgetsReady = true;
        }

        private void HideEmbeddedWidgets()
        {
            if (!_embeddedWidgetsReady)
                return;

            _pawnUiManager?.FlushPendingEdits();
            _pawnUiManager?.HideInventoryFromBoardStack();
            _pawnUiManager?.HideProfileFromBoardStack();
            if (_statPanel != null)
            {
                _statPanel.SetEmbeddedMode(null, null, false);
                _statPanel = null;
            }
            _embeddedWidgetsReady = false;
        }

        private void ApplyStateImmediate(BoardUiState state)
        {
            if (_widget == null)
                return;

            var visible = state.Mode == BoardMode.Sheet;
            if (visible)
            {
                EnsureEmbeddedWidgets();
                _widget.SetLeftPane(_leftPane);
                _widget.SetRightPane(_rightPane);
            }
            _widget.SetPanelsImmediate(
                visible,
                visible && _infoBar != null && _infoBar.HasStats);

            if (state.Detail == SheetDetail.RollRoulette)
            {
                if (_effectRollActive)
                {
                    _widget.ShowEffectRollPanel(
                        _pawnUiManager.EffectDiceCount,
                        _pawnUiManager.EffectDiceSides,
                        _pawnUiManager.EffectDiceModifier);
                }
                else if (_widget.SelectedSource.IsValid)
                {
                    _widget.PrepareRollPanel(_widget.SelectedSource);
                }
                else
                {
                    _widget.ShowEmptyRollPanel();
                }
                _widget.RollOverlayMask.sizeDelta = new Vector2(
                    _widget.RollOverlayContent.sizeDelta.x,
                    _widget.RollOverlayContent.sizeDelta.y);
                _widget.RollOverlayGroup.alpha = 1f;
            }
            else
            {
                _widget.HideRollOverlayImmediate();
            }

            if (!visible)
                HideEmbeddedWidgets();
        }

        private void HandleEscape()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null ||
                !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            _pawnUiManager?.FlushPendingEdits();
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
            var leftPanelHeight = Mathf.Max(
                BoardUiLayoutCalculator.MinimumPanelHeight,
                rootHeight -
                bottom -
                BoardUiLayoutCalculator.TopMargin);
            var rightTopInset = CalculateRightTopInset();
            var rightPanelHeight = Mathf.Max(
                320f,
                rootHeight -
                bottom -
                rightTopInset);

            _widget.ApplyLayout(
                _layout,
                bottom,
                leftPanelHeight,
                rightPanelHeight);
            _statPanel?.RefreshResponsiveLayout();
        }

        private float CalculateRightTopInset()
        {
            if (_widget == null || _widget.RootRect == null)
                return BoardUiLayoutCalculator.TopMargin;

            var playerBar = FindFirst<PlayerInteractionBarWidget>();
            if (playerBar == null ||
                !playerBar.gameObject.activeInHierarchy)
            {
                return BoardUiLayoutCalculator.TopMargin;
            }

            var panelRect = playerBar.transform as RectTransform;
            if (panelRect == null)
                return BoardUiLayoutCalculator.TopMargin;

            var canvas = playerBar.GetComponentInParent<Canvas>();
            var camera = canvas != null &&
                         canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            var worldBottom = panelRect.TransformPoint(
                new Vector3(
                    panelRect.rect.center.x,
                    panelRect.rect.yMin,
                    0f));
            var screenBottom = RectTransformUtility.WorldToScreenPoint(
                camera,
                worldBottom);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _widget.RootRect,
                    screenBottom,
                    camera,
                    out var localBottom))
            {
                return BoardUiLayoutCalculator.TopMargin;
            }

            var inset =
                _widget.RootRect.rect.yMax -
                localBottom.y +
                12f;
            return Mathf.Max(
                BoardUiLayoutCalculator.TopMargin,
                inset);
        }

        private float CalculatePanelBottomOffset()
        {
            var fallback = BoardUiLayoutCalculator.DefaultBottomOffset;
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
            var maximum = Mathf.Max(
                96f,
                _widget.RootRect.rect.height -
                BoardUiLayoutCalculator.MinimumPanelHeight);
            return Mathf.Clamp(fromBottom, 96f, maximum);
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

            var next = BoardUiLayoutCalculator.Calculate(size.x, size.y);
            var nextBottom = CalculatePanelBottomOffset();
            var rootHeight = Mathf.Max(
                1f,
                _widget.RootRect.rect.height);
            var nextRightHeight = Mathf.Max(
                320f,
                rootHeight -
                nextBottom -
                CalculateRightTopInset());
            var unchanged =
                next.Band == _layout.Band &&
                Mathf.Abs(next.LeftWidth - _layout.LeftWidth) < 0.5f &&
                Mathf.Abs(next.RightWidth - _layout.RightWidth) < 0.5f &&
                Mathf.Abs(
                    nextBottom - _widget.PanelBottomOffset) < 1f &&
                Mathf.Abs(
                    nextRightHeight -
                    _widget.RightPanelHeight) < 1f;
            if (unchanged)
                return;

            _layout = next;
            ApplyStackLayout();
            ApplyStateImmediate(_stateMachine.State);
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
}
