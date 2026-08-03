using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Trpg.Domain.Stats;
using Trpg.UI.Stats;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 기존 Pawn UI를 유지한 채 판정 버튼, 중앙 판정 패널,
    /// 스탯/스킬 선택과 CoC 판정 세션을 연결합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnCheckRollManager : MonoBehaviour
    {
        private const string LuckStatId = "coc.luck";
        private const int IntegrationFrameLimit = 90;
        private const string CheckButtonName = "CheckRollButton";
        private const string CheckButtonLabel =
            "판정 굴림\nD100 / 스탯·스킬";

        [SerializeField] private PawnInfoBarWidget _infoBar;

        private readonly List<PawnRollSourceWidget> _sourceWidgets =
            new List<PawnRollSourceWidget>();

        private PawnManager _pawnManager;
        private PawnRollWidget _rollWidget;
        private Button _checkButton;
        private Text _checkButtonText;
        private Canvas _rootCanvas;
        private PawnRollService _rollService;
        private PawnCheckRollOverlayWidget _overlay;
        private PawnRollResultWindow _resultWindow;
        private PawnUiDragHandle _overlayDragHandle;
        private PlayerStatState _statState;
        private InteractivePawn _currentPawn;
        private PawnCheckRollState _sessionState;
        private PawnCheckSourceData _source;
        private PawnCheckDifficulty _difficulty;
        private PawnCheckEvaluation _evaluation;
        private Button _effectButton;
        private InteractivePawn _legacyRollOwner;
        private GameObject _legacyRollBlocker;
        private Coroutine _legacyRollTimeoutRoutine;
        private Coroutine _integrationRoutine;
        private Coroutine _deferredRowBindRoutine;
        private bool _hasSource;
        private bool _challengeUsed;
        private bool _isOpen;
        private bool _overlayEventsBound;
        private bool _pawnEventsBound;
        private bool _legacyPresentationBound;
        private bool _resultWindowEventsBound;
        private PlayerStatState _resourceState;
        private Coroutine _resourceRefreshRoutine;
        private Vector2Int _lastConfiguredScreenSize;
        private BoardUiStackManager _boardStackManager;

        public event Action SessionClosed;
        public event Action<PawnCheckSourceData> SourceChanged;
        public event Action<PawnCheckDifficulty> DifficultyChanged;

        public bool IsOpen => _isOpen;
        public bool IsBoardStackMode => _boardStackManager != null;
        public PawnCheckSourceData CurrentSource => _source;
        public PawnCheckDifficulty CurrentDifficulty => _difficulty;

        public void RegisterBoardStack(BoardUiStackManager manager)
        {
            _boardStackManager = manager;
            if (_boardStackManager != null)
            {
                _overlay?.Hide();
                if (_overlayDragHandle != null)
                    _overlayDragHandle.enabled = false;
                if (_isOpen)
                {
                    ConfigureExistingStatPanelInteraction();
                    BindSourceRows();
                    StartDeferredRowBinding();
                }
            }
        }

        public void UnregisterBoardStack(BoardUiStackManager manager)
        {
            if (_boardStackManager != manager)
                return;
            _boardStackManager = null;
            if (_overlayDragHandle != null)
                _overlayDragHandle.enabled = true;
        }

        public bool OpenFromBoardStack(PlayerStatState statState)
        {
            return Open(statState);
        }

        public bool SelectSourceFromBoardStack(PawnCheckSourceData source)
        {
            if (!_isOpen && !Open(ResolveStatState(
                    _pawnManager != null
                        ? _pawnManager.SelectedInteractive
                        : null)))
            {
                return false;
            }

            HandleSourceSelected(source);
            return _hasSource;
        }

        public void SetDifficultyFromBoardStack(
            PawnCheckDifficulty difficulty)
        {
            if (!_isOpen || !_hasSource)
                return;
            _difficulty = difficulty;
            DifficultyChanged?.Invoke(difficulty);
        }

        public bool RollFromBoardStack()
        {
            if (!_isOpen || !_hasSource || _rollService == null ||
                _sessionState == null ||
                _sessionState.Phase == PawnCheckRollSessionPhase.Rolling)
            {
                return false;
            }

            ExecuteCheckRoll(isChallenge: false);
            return true;
        }

        /// <summary>
        /// 다른 Peer의 굴림을 기존 Stats/RollRoulette UI에
        /// 읽기 전용으로 표시합니다.
        /// </summary>
        public bool PresentRemoteRoll(
            InteractivePawn pawn,
            in PawnRollWindowData data,
            bool animate)
        {
            if (!TryIntegrate())
            {
                BeginIntegration();
                return false;
            }

            if (pawn != null &&
                _pawnManager != null &&
                _pawnManager.SelectedInteractive != pawn)
            {
                _pawnManager.SelectInteractive(pawn);
            }

            _boardStackManager?.PresentRemoteRollPanel();

            if (_resultWindow == null)
                return false;

            _resultWindow.HideFailureActions();
            if (animate)
                _resultWindow.Play(data, null);
            else
                _resultWindow.ShowInstant(data);

            return true;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            var existing = FindFirst<PawnCheckRollManager>();
            if (existing != null)
            {
                existing.BeginIntegration();
                return;
            }

            var uiManager = FindFirst<PawnUIManager>();
            if (uiManager == null)
                return;

            var manager =
                uiManager.gameObject.AddComponent<PawnCheckRollManager>();
            manager.BeginIntegration();
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

        private void Awake()
        {
            if (_rollService == null)
            {
                var seed = unchecked(
                    Environment.TickCount * 397 ^ GetInstanceID());
                _rollService = new PawnRollService(seed);
            }
        }

        private void OnEnable()
        {
            PawnRollSourceWidget.SourceSelected +=
                HandleSourceSelected;
            BeginIntegration();
        }

        private void LateUpdate()
        {
            var current = new Vector2Int(Screen.width, Screen.height);
            if (_rootCanvas == null || current == _lastConfiguredScreenSize)
                return;

            ConfigureExistingStatPanelInteraction();
        }

        private void OnDisable()
        {
            PawnRollSourceWidget.SourceSelected -=
                HandleSourceSelected;
            StopIntegrationRoutine();
            StopDeferredRowBinding();
            StopLegacyRollTimeout();
            SetLegacyRollBlocker(false);
            UnbindPawnEvents();
            UnbindLegacyPresentationEvents();
            UnbindResultWindowEvents();
            UnbindResourceState();
            StopResourceRefresh();
            HidePanelPreservingState();
        }

        /// <summary>
        /// 기존 PawnUIManager가 명시적으로 연결하는 구성과도 호환됩니다.
        /// </summary>
        public void Configure(
            PawnInfoBarWidget infoBar,
            PawnRollService rollService)
        {
            if (infoBar == null)
                throw new ArgumentNullException(nameof(infoBar));
            if (rollService == null)
                throw new ArgumentNullException(nameof(rollService));

            _infoBar = infoBar;
            _rollService = rollService;
            ResetIntegrationReferences(keepInfoBar: true);
            BeginIntegration();
        }

        /// <summary>
        /// 외부 Manager에서 판정 세션을 여는 기존 호출과 호환됩니다.
        /// 판정 세션은 선택된 캐릭터 Pawn에 저장됩니다.
        /// </summary>
        public bool Open(PlayerStatState statState)
        {
            if (!TryIntegrate())
            {
                BeginIntegration();
                return false;
            }

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (!IsCharacterPawn(pawn))
            {
                Debug.LogWarning(
                    $"[{name}] 판정 굴림은 캐릭터 Pawn에서만 사용할 수 있습니다.",
                    this);
                return false;
            }

            if (statState == null)
                statState = ResolveStatState(pawn);
            if (statState != null && !statState.IsInitialized)
                statState.Initialize();

            SaveCurrentWindowPositions();
            _currentPawn = pawn;
            _statState = statState;
            _sessionState = GetOrCreateState(pawn);
            _sessionState.EnsureSession();
            LoadSessionState();
            _isOpen = true;

            HideLegacyManualTargetInput();
            BindResourceState(pawn);
            RefreshResourceBarWithLuck(pawn);

            if (_boardStackManager != null)
            {
                _overlay?.Hide();
                ConfigureExistingStatPanelInteraction();
                BindSourceRows();
                StartDeferredRowBinding();
                if (_sessionState.HasSource)
                {
                    _source = _sessionState.Source;
                    _hasSource = _source.IsValid;
                    SourceChanged?.Invoke(_source);
                }
                return true;
            }

            ExpandExistingStatPanel();
            ConfigureExistingStatPanelInteraction();
            _overlay.OpenWaiting();
            ApplyStoredWindowPositions();
            RestoreSessionUi();
            BindSourceRows();
            StartDeferredRowBinding();
            return true;
        }

        public void Cancel()
        {
            HidePanelPreservingState();
        }

        private void BeginIntegration()
        {
            if (!isActiveAndEnabled)
                return;

            StopIntegrationRoutine();
            if (TryIntegrate())
                return;

            _integrationRoutine = StartCoroutine(
                IntegrateWhenReady());
        }

        private IEnumerator IntegrateWhenReady()
        {
            for (var frame = 0;
                 frame < IntegrationFrameLimit;
                 frame++)
            {
                if (TryIntegrate())
                {
                    _integrationRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _integrationRoutine = null;
            Debug.LogWarning(
                $"[{name}] 기존 판정 버튼 또는 Root Canvas를 " +
                "찾지 못해 판정 UI를 연결하지 못했습니다.",
                this);
        }

        private bool TryIntegrate()
        {
            if (_infoBar == null)
                _infoBar = FindFirst<PawnInfoBarWidget>();
            if (_infoBar == null)
                return false;

            if (_rootCanvas == null)
            {
                var canvas = _infoBar.GetComponentInParent<Canvas>(true);
                _rootCanvas = canvas != null ? canvas.rootCanvas : null;
            }
            if (_rootCanvas == null)
                return false;

            if (_rollWidget == null)
            {
                _rollWidget = _infoBar.GetComponentInChildren<
                    PawnRollWidget>(true);
            }
            if (_rollWidget == null)
                return false;

            var referenceText =
                _infoBar.GetComponentInChildren<Text>(true);
            var referenceFont = referenceText != null
                ? referenceText.font
                : null;

            if (_overlay == null)
            {
                _overlay = _rootCanvas.GetComponentInChildren<
                    PawnCheckRollOverlayWidget>(true);
                if (_overlay == null)
                {
                    _overlay =
                        PawnCheckRollOverlayWidget.CreateRuntime(
                            _rootCanvas,
                            referenceFont);
                }
            }

            if (_resultWindow == null)
            {
                _resultWindow = _rootCanvas.GetComponentInChildren<
                    PawnRollResultWindow>(true);
                if (_resultWindow == null)
                {
                    _resultWindow = PawnRollResultWindow.CreateRuntime(
                        _rootCanvas,
                        referenceFont);
                }
            }

            AttachOverlayDragHandle();
            BindOverlayEvents();
            BindResultWindowEvents();
            FindCheckButton();
            FindEffectButton();
            EnsureLegacyRollBlocker();
            if (_checkButton == null)
                return false;

            OwnCheckButton();
            BindLegacyPresentationEvents();
            HideLegacyManualTargetInput();
            BindPawnEvents();
            ConfigureExistingStatPanelInteraction();
            if (_pawnManager != null)
            {
                BindResourceState(_pawnManager.SelectedInteractive);
                ScheduleResourceRefresh(
                    _pawnManager.SelectedInteractive);
            }
            return true;
        }

        private void BindResultWindowEvents()
        {
            if (_resultWindow == null || _resultWindowEventsBound)
                return;

            _resultWindow.Closed += HandleResultWindowClosed;
            _resultWindow.AcceptRequested += HandleAcceptRequested;
            _resultWindow.ChallengeRequested +=
                HandleChallengeRequested;
            _resultWindow.LuckRequested += HandleLuckRequested;
            _resultWindow.ConfirmationAccepted +=
                HandleConfirmationAccepted;
            _resultWindowEventsBound = true;
        }

        private void UnbindResultWindowEvents()
        {
            if (_resultWindow == null || !_resultWindowEventsBound)
                return;

            _resultWindow.Closed -= HandleResultWindowClosed;
            _resultWindow.AcceptRequested -= HandleAcceptRequested;
            _resultWindow.ChallengeRequested -=
                HandleChallengeRequested;
            _resultWindow.LuckRequested -= HandleLuckRequested;
            _resultWindow.ConfirmationAccepted -=
                HandleConfirmationAccepted;
            _resultWindowEventsBound = false;
        }

        private void HandleResultWindowClosed()
        {
            SaveCurrentWindowPositions();
            if (!_isOpen || _sessionState == null ||
                _sessionState.Phase != PawnCheckRollSessionPhase.Rolling)
            {
                return;
            }

            ResolveInterruptedPresentation(_sessionState);
            RefreshOverlayForStoredState();
        }

        private void FindCheckButton()
        {
            if (_rollWidget == null)
                return;

            if (_checkButton != null &&
                _checkButton.transform.IsChildOf(_rollWidget.transform))
            {
                return;
            }

            _checkButton = null;
            _checkButtonText = null;
            var buttons =
                _rollWidget.GetComponentsInChildren<Button>(true);
            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                if (button == null ||
                    !string.Equals(
                        button.gameObject.name,
                        CheckButtonName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _checkButton = button;
                _checkButtonText =
                    button.GetComponentInChildren<Text>(true);
                return;
            }
        }

        private void FindEffectButton()
        {
            if (_rollWidget == null)
                return;

            if (_effectButton != null &&
                _effectButton.transform.IsChildOf(_rollWidget.transform))
            {
                return;
            }

            _effectButton = null;
            var buttons = _rollWidget.GetComponentsInChildren<Button>(true);
            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                if (button != null && string.Equals(
                        button.gameObject.name,
                        "EffectRollButton",
                        StringComparison.OrdinalIgnoreCase))
                {
                    _effectButton = button;
                    return;
                }
            }
        }

        private void AttachOverlayDragHandle()
        {
            if (_overlay == null)
                return;

            var panel = _overlay.transform.Find(
                "SafeArea/CenteredPanel") as RectTransform;
            if (panel == null)
                return;

            _overlayDragHandle = PawnUiDragHandle.Attach(
                panel,
                panel.parent as RectTransform,
                58f,
                8f);
        }

        private void OwnCheckButton()
        {
            if (_checkButton == null)
                return;

            // 판정 버튼의 기존 목표 숫자 입력 흐름만 제거합니다.
            // 효과 굴림, 이동, 스탯 UI의 Listener는 건드리지 않습니다.
            _checkButton.onClick.RemoveAllListeners();
            _checkButton.onClick.AddListener(HandleCheckButtonClicked);
            if (_checkButtonText != null)
                _checkButtonText.text = CheckButtonLabel;
        }

        private void HandleCheckButtonClicked()
        {
            TryIntegrate();
            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (_boardStackManager != null)
            {
                _boardStackManager.RequestCheckRoll();
                return;
            }

            Open(ResolveStatState(pawn));
        }

        private void BindPawnEvents()
        {
            if (_pawnEventsBound)
                return;

            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();
            if (_pawnManager == null)
                return;

            _pawnManager.InteractiveSelectionChanged +=
                HandleInteractiveSelectionChanged;
            _pawnManager.TurnGroupChanged += HandleTurnGroupChanged;
            _pawnEventsBound = true;
        }

        private void UnbindPawnEvents()
        {
            if (!_pawnEventsBound || _pawnManager == null)
                return;

            _pawnManager.InteractiveSelectionChanged -=
                HandleInteractiveSelectionChanged;
            _pawnManager.TurnGroupChanged -= HandleTurnGroupChanged;
            _pawnEventsBound = false;
        }

        private void HandleInteractiveSelectionChanged(
            InteractivePawn pawn)
        {
            if (_isOpen && pawn != _currentPawn)
                HidePanelPreservingState();

            BindResourceState(pawn);
            if (isActiveAndEnabled)
                StartCoroutine(RefreshButtonAfterSelection(pawn));
        }

        private void HandleTurnGroupChanged(
            PawnManager.TurnGroup group,
            IReadOnlyList<InteractivePawn> groupPawns)
        {
            // Player 턴이 시작될 때마다 캐릭터별 진행 중 판정 데이터를
            // 초기화합니다. Player만 존재하는 전투에서도 동일하게 동작합니다.
            if (group == PawnManager.TurnGroup.Player)
                ResetAllCharacterRollStates();
        }

        [ContextMenu("Reset Character Roll States")]
        public void ResetAllCharacterRollStates()
        {
            if (_pawnManager == null)
                return;

            // UI를 먼저 닫아 현재 위치를 저장한 뒤, 모든 캐릭터 세션을
            // 완전히 초기화합니다. 초기화 후 위치가 다시 저장되지 않게 합니다.
            if (_currentPawn != null || _isOpen)
                HidePanelPreservingState();

            var players = _pawnManager.PlayerPawns;
            for (var index = 0; index < players.Count; index++)
            {
                var pawn = players[index];
                if (pawn == null)
                    continue;

                var state = pawn.GetComponent<PawnCheckRollState>();
                if (state != null)
                    state.ResetForTurn();
            }
        }

        private IEnumerator RefreshButtonAfterSelection(
            InteractivePawn pawn)
        {
            yield return null;
            TryIntegrate();
            OwnCheckButton();
            ConfigureExistingStatPanelInteraction();
            RefreshResourceBarWithLuck(pawn);
        }

        private void HideLegacyManualTargetInput()
        {
            if (_rollWidget == null)
                return;

            var texts = _rollWidget.GetComponentsInChildren<Text>(true);
            for (var index = 0; index < texts.Length; index++)
            {
                var text = texts[index];
                if (text == null || string.IsNullOrWhiteSpace(text.text))
                    continue;

                var isTargetPrompt =
                    text.text.IndexOf(
                        "목표로 하는 넘버",
                        StringComparison.Ordinal) >= 0 ||
                    text.text.IndexOf(
                        "목표 넘버",
                        StringComparison.Ordinal) >= 0;
                if (!isTargetPrompt)
                    continue;

                var target = text.transform;
                while (target.parent != null &&
                       target.parent != _rollWidget.transform)
                {
                    target = target.parent;
                }

                if (target != _rollWidget.transform)
                    target.gameObject.SetActive(false);
            }
        }

        private void ExpandExistingStatPanel()
        {
            if (_rootCanvas == null)
                return;

            var panels = _rootCanvas.GetComponentsInChildren<
                PawnStatPanelWidget>(true);
            for (var index = 0; index < panels.Length; index++)
                panels[index].SetExpanded(true);
        }

        private void BindSourceRows()
        {
            PawnCheckSourceRuntimeBinder.BindExistingRows(
                _rootCanvas,
                _sourceWidgets);
            SetSourceInteraction(true);
        }

        private void StartDeferredRowBinding()
        {
            StopDeferredRowBinding();
            _deferredRowBindRoutine = StartCoroutine(
                BindRowsAfterLayout());
        }

        private IEnumerator BindRowsAfterLayout()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_isOpen)
                BindSourceRows();
            _deferredRowBindRoutine = null;
        }

        private void StopDeferredRowBinding()
        {
            if (_deferredRowBindRoutine == null)
                return;

            StopCoroutine(_deferredRowBindRoutine);
            _deferredRowBindRoutine = null;
        }

        private void SetSourceInteraction(bool value)
        {
            for (var index = 0; index < _sourceWidgets.Count; index++)
            {
                if (_sourceWidgets[index] != null)
                {
                    _sourceWidgets[index].SetInteractionEnabled(value);
                }
            }
        }

        private void BindOverlayEvents()
        {
            if (_overlay == null || _overlayEventsBound)
                return;

            _overlay.PureRollRequested += HandlePureRollRequested;
            _overlay.SourceDropped += HandleSourceSelected;
            _overlay.DifficultyRequested +=
                HandleDifficultyRequested;
            _overlay.ClearSourceRequested +=
                HandleClearSourceRequested;
            _overlay.CloseRequested += HidePanelPreservingState;
            _overlayEventsBound = true;
        }

        private void UnbindOverlayEvents()
        {
            if (_overlay == null || !_overlayEventsBound)
                return;

            _overlay.PureRollRequested -= HandlePureRollRequested;
            _overlay.SourceDropped -= HandleSourceSelected;
            _overlay.DifficultyRequested -=
                HandleDifficultyRequested;
            _overlay.ClearSourceRequested -=
                HandleClearSourceRequested;
            _overlay.CloseRequested -= HidePanelPreservingState;
            _overlayEventsBound = false;
        }

        private void HandlePureRollRequested()
        {
            if (!_isOpen || _rollService == null ||
                _currentPawn == null || _sessionState == null ||
                _sessionState.Phase == PawnCheckRollSessionPhase.Rolling)
            {
                return;
            }

            var roll = _rollService.RollD100(100).Roll;
            var data = new PawnRollWindowData(
                $"{ResolvePawnName(_currentPawn)} · 순수 D100",
                "d100",
                roll,
                1,
                100,
                $"결과 {roll}",
                "목표값 없이 숫자만 확인하는 굴림",
                new Color(1f, 0.78f, 0.22f),
                1.55f);
            var ownerState = _sessionState;
            var ownerPawn = _currentPawn;
            ownerState.RecordPureRoll(data);
            SaveCurrentWindowPositions();
            PawnRollLogService.RecordRoll(
                PawnRollLogKind.PureD100,
                ownerPawn,
                "순수 D100",
                "d100",
                roll,
                $"결과 {roll}",
                "목표값 없음");
            TRPGSessionAuthority.PublishRoll(
                ownerPawn,
                PawnRollLogKind.PureD100,
                data);

            if (_boardStackManager == null)
                _overlay.Hide();
            _resultWindow.Play(data, () =>
            {
                ownerState.MarkFinalized();
                if (_isOpen && _sessionState == ownerState)
                {
                    _resultWindow.HideFailureActions();
                    ShowSessionStatus(
                        "순수 D100 결과가 캐릭터에 저장되었습니다.");
                }
            });
        }

        private void HandleSourceSelected(PawnCheckSourceData source)
        {
            if (!_isOpen || !source.IsValid || _sessionState == null ||
                _sessionState.Phase == PawnCheckRollSessionPhase.Rolling)
                return;

            _source = source;
            _hasSource = true;
            _challengeUsed = false;
            _evaluation = default;
            _sessionState.SelectSource(source);
            if (_boardStackManager == null)
                _overlay.BindSource(_source);
            SourceChanged?.Invoke(_source);
            _resultWindow?.Hide();
        }

        private void HandleClearSourceRequested()
        {
            if (!_isOpen || _sessionState == null ||
                _sessionState.Phase == PawnCheckRollSessionPhase.Rolling)
                return;

            _source = default;
            _evaluation = default;
            _hasSource = false;
            _challengeUsed = false;
            _sessionState.ClearSource();
            if (_boardStackManager == null)
                _overlay.ClearSource();
            SourceChanged?.Invoke(default);
            _resultWindow?.Hide();
        }

        private void HandleDifficultyRequested(
            PawnCheckDifficulty difficulty)
        {
            if (!_isOpen || !_hasSource || _rollService == null ||
                _sessionState == null ||
                _sessionState.Phase == PawnCheckRollSessionPhase.Rolling)
            {
                return;
            }

            _difficulty = difficulty;
            DifficultyChanged?.Invoke(difficulty);
            ExecuteCheckRoll(isChallenge: false);
        }

        private void ExecuteCheckRoll(bool isChallenge)
        {
            var roll = _rollService.RollD100(_source.Regular).Roll;
            _evaluation = PawnCheckRollRules.Evaluate(
                _source,
                _difficulty,
                roll,
                _challengeUsed);
            var difficultyLabel =
                PawnCheckRollRules.GetDifficultyLabel(_difficulty);
            var gradeLabel =
                PawnCheckRollRules.GetGradeLabel(_evaluation.Grade);
            var successLabel = _evaluation.IsSuccessForDifficulty
                ? gradeLabel
                : "실패";
            var data = new PawnRollWindowData(
                $"{ResolvePawnName(_currentPawn)} · " +
                (isChallenge ? "대항 판정" : "판정 굴림"),
                $"{_source.DisplayName} · {difficultyLabel} · " +
                $"목표 {_evaluation.RequiredTarget}",
                roll,
                1,
                100,
                successLabel,
                $"굴림 {roll} / 목표 {_evaluation.RequiredTarget} / " +
                $"판정 단계 {gradeLabel}",
                GetResultColor(_evaluation),
                1.55f,
                GetResultTone(_evaluation));

            var ownerState = _sessionState;
            var ownerPawn = _currentPawn;
            var evaluation = _evaluation;
            ownerState.BeginCheckRoll(
                _difficulty,
                evaluation,
                isChallenge,
                data);
            SaveCurrentWindowPositions();
            var networkLogKind = isChallenge
                ? PawnRollLogKind.Challenge
                : PawnRollLogKind.Check;
            PawnRollLogService.RecordRoll(
                networkLogKind,
                ownerPawn,
                isChallenge ? "대항 판정" : "판정 굴림",
                $"{_source.DisplayName} {difficultyLabel} " +
                $"(목표 {evaluation.RequiredTarget})",
                roll,
                successLabel,
                $"판정 단계 {gradeLabel}");
            TRPGSessionAuthority.PublishRoll(
                ownerPawn,
                networkLogKind,
                data);

            if (_boardStackManager == null)
                _overlay.Hide();
            _resultWindow.Play(
                data,
                () => HandleCheckPresentationCompleted(
                    ownerState,
                    evaluation,
                    isChallenge));
        }

        private void HandleCheckPresentationCompleted(
            PawnCheckRollState ownerState,
            PawnCheckEvaluation evaluation,
            bool isChallenge)
        {
            if (ownerState == null)
                return;

            if (evaluation.IsSuccessForDifficulty)
            {
                ownerState.MarkFinalized();
                if (_isOpen && _sessionState == ownerState)
                {
                    _resultWindow.HideFailureActions();
                    ShowSessionStatus(
                        "판정 결과가 캐릭터에 저장되었습니다.");
                }
                return;
            }

            if (isChallenge ||
                evaluation.Grade == PawnCheckOutcomeGrade.Fumble)
            {
                ownerState.MarkFinalized();
                if (_isOpen && _sessionState == ownerState)
                {
                    _resultWindow.HideFailureActions();
                    ShowSessionStatus(
                        isChallenge
                            ? "대항 판정 결과가 캐릭터에 저장되었습니다."
                            : "대실패 결과가 캐릭터에 저장되었습니다.");
                }
                return;
            }

            ownerState.MarkFailureDecision();
            if (!_isOpen || _sessionState != ownerState)
                return;

            var currentLuck = GetCurrentLuck();
            _resultWindow.ShowFailureActions(
                evaluation.LuckCost,
                currentLuck,
                evaluation.CanChallenge,
                evaluation.CanSpendLuck &&
                currentLuck >= evaluation.LuckCost);
            ShowSessionStatus(
                "실패했습니다. 결과창 하단에서 후속 행동을 선택하세요.");
        }

        private void HandleAcceptRequested()
        {
            if (_sessionState != null)
                _sessionState.MarkFinalized();

            if (_isOpen)
            {
                _resultWindow?.HideFailureActions();
                ShowSessionStatus(
                    "현재 결과를 확정했습니다. 턴 리셋까지 캐릭터에 저장됩니다.");
            }
        }

        private void HandleChallengeRequested()
        {
            if (!_isOpen ||
                !_evaluation.CanChallenge ||
                _challengeUsed)
            {
                return;
            }

            var difficulty =
                PawnCheckRollRules.GetDifficultyLabel(_difficulty);
            _resultWindow.ShowConfirmation(
                PawnCheckConfirmationKind.Challenge,
                "정말 강행하시겠습니까?",
                $"{_source.DisplayName} {difficulty} 판정을 " +
                "1회 다시 굴립니다.\n" +
                "강행 이후에는 다시 강행하거나 운을 사용할 수 없습니다.",
                "강행한다");
        }

        private void HandleLuckRequested()
        {
            if (!_isOpen || !_evaluation.CanSpendLuck)
                return;

            var currentLuck = GetCurrentLuck();
            if (currentLuck < _evaluation.LuckCost)
                return;

            _resultWindow.ShowConfirmation(
                PawnCheckConfirmationKind.Luck,
                "정말 운을 사용하시겠습니까?",
                $"필요 운: {_evaluation.LuckCost}\n" +
                $"현재 운: {currentLuck}\n" +
                $"사용 후 운: " +
                $"{currentLuck - _evaluation.LuckCost}",
                "운을 사용한다");
        }

        private void HandleConfirmationAccepted(
            PawnCheckConfirmationKind kind)
        {
            switch (kind)
            {
                case PawnCheckConfirmationKind.Challenge:
                    ConfirmChallenge();
                    break;
                case PawnCheckConfirmationKind.Luck:
                    ConfirmLuck();
                    break;
            }
        }

        private void ConfirmChallenge()
        {
            if (!_isOpen ||
                _challengeUsed ||
                !_evaluation.CanChallenge)
            {
                return;
            }

            _challengeUsed = true;
            _resultWindow?.HideFailureActions();
            ExecuteCheckRoll(isChallenge: true);
        }

        private void ConfirmLuck()
        {
            if (!_isOpen ||
                _statState == null ||
                !_evaluation.CanSpendLuck)
            {
                return;
            }

            var currentLuck = GetCurrentLuck();
            var cost = _evaluation.LuckCost;
            if (cost <= 0 || currentLuck < cost)
                return;

            var remaining = currentLuck - cost;
            if (!_statState.TrySetDisplayedValue(
                    LuckStatId,
                    remaining))
            {
                Debug.LogWarning(
                    $"[{name}] 운 수치를 차감하지 못했습니다. " +
                    $"StatId={LuckStatId}",
                    this);
                return;
            }

            _challengeUsed = true;
            var difficultyLabel =
                PawnCheckRollRules.GetDifficultyLabel(_difficulty);
            var luckPresentation = new PawnRollWindowData(
                $"{ResolvePawnName(_currentPawn)} · 판정 굴림",
                $"{_source.DisplayName} · {difficultyLabel} · " +
                $"목표 {_evaluation.RequiredTarget}",
                _evaluation.Roll,
                1,
                100,
                "운 사용으로 성공",
                $"원래 굴림 {_evaluation.Roll} / 목표 " +
                $"{_evaluation.RequiredTarget} / 운 {cost} 사용 / " +
                $"남은 운 {remaining}",
                new Color(0.38f, 0.95f, 0.52f),
                1.55f,
                PawnRollResultTone.Standard);
            _sessionState?.MarkLuckApplied(
                cost,
                remaining,
                luckPresentation);
            PawnRollLogService.RecordRoll(
                PawnRollLogKind.Luck,
                _currentPawn,
                "운 사용",
                _source.DisplayName,
                _evaluation.Roll,
                "운 사용으로 성공",
                $"운 {cost} 사용 / 남은 운 {remaining}");
            TRPGSessionAuthority.PublishRoll(
                _currentPawn,
                PawnRollLogKind.Luck,
                luckPresentation,
                animate: false);
            _resultWindow.ShowLuckApplied(luckPresentation);
            ShowSessionStatus(
                "운을 사용해 판정을 성공으로 확정했습니다.");
            ScheduleResourceRefresh(_currentPawn);
        }

        private int GetCurrentLuck()
        {
            if (_statState?.Runtime == null)
                return 0;

            try
            {
                return Math.Max(
                    0,
                    (int)Math.Floor(
                        _statState.Runtime.GetNumber(LuckStatId)));
            }
            catch
            {
                return 0;
            }
        }

        private void HidePanelPreservingState()
        {
            StopDeferredRowBinding();
            SaveCurrentWindowPositions();
            SetSourceInteraction(false);
            _overlay?.Hide();
            _resultWindow?.Hide();

            var wasOpen = _isOpen;
            _isOpen = false;
            _statState = null;
            _currentPawn = null;
            _sessionState = null;
            _source = default;
            _evaluation = default;
            _hasSource = false;
            _challengeUsed = false;

            if (wasOpen)
                SessionClosed?.Invoke();
        }

        private void LoadSessionState()
        {
            _source = _sessionState.HasSource
                ? _sessionState.Source
                : default;
            _hasSource = _sessionState.HasSource;
            _difficulty = _sessionState.HasDifficulty
                ? _sessionState.Difficulty
                : PawnCheckDifficulty.Regular;
            _evaluation = _sessionState.HasEvaluation
                ? _sessionState.Evaluation
                : default;
            _challengeUsed = _sessionState.ChallengeUsed;
        }

        private void RestoreSessionUi()
        {
            if (_sessionState == null)
                return;

            if (_sessionState.HasSource)
            {
                if (_boardStackManager == null)
                    _overlay.BindSource(_sessionState.Source);
                SourceChanged?.Invoke(_sessionState.Source);
            }

            if (_sessionState.HasLastPresentation)
            {
                _resultWindow.ShowInstant(
                    _sessionState.GetLastPresentation());
                if (_sessionState.HasResultWindowPosition)
                {
                    _resultWindow.SetWindowPosition(
                        _sessionState.ResultWindowPosition);
                }
            }

            if (_sessionState.Phase ==
                PawnCheckRollSessionPhase.Rolling)
            {
                ResolveInterruptedPresentation(_sessionState);
            }

            if (_sessionState.Phase ==
                PawnCheckRollSessionPhase.FailureDecision &&
                _sessionState.HasEvaluation)
            {
                var currentLuck = GetCurrentLuck();
                var evaluation = _sessionState.Evaluation;
                _resultWindow.ShowFailureActions(
                    evaluation.LuckCost,
                    currentLuck,
                    evaluation.CanChallenge &&
                    !_sessionState.ChallengeUsed,
                    evaluation.CanSpendLuck &&
                    !_sessionState.ChallengeUsed &&
                    currentLuck >= evaluation.LuckCost);
                ShowSessionStatus(
                    "실패했습니다. 결과창 하단에서 후속 행동을 선택하세요.");
            }
            else if (_sessionState.Phase ==
                     PawnCheckRollSessionPhase.Finalized &&
                     _sessionState.HasLastPresentation)
            {
                _resultWindow.HideFailureActions();
                ShowSessionStatus(
                    "저장된 판정 결과입니다. 턴 리셋 시 초기화됩니다.");
            }
        }

        private static void ResolveInterruptedPresentation(
            PawnCheckRollState state)
        {
            if (state == null ||
                state.Phase != PawnCheckRollSessionPhase.Rolling)
            {
                return;
            }

            if (!state.HasEvaluation)
            {
                state.MarkFinalized();
                return;
            }

            var evaluation = state.Evaluation;
            var mustFinalize =
                evaluation.IsSuccessForDifficulty ||
                evaluation.Grade == PawnCheckOutcomeGrade.Fumble ||
                state.LastRollWasChallenge;
            if (mustFinalize)
                state.MarkFinalized();
            else
                state.MarkFailureDecision();
        }

        private void RefreshOverlayForStoredState()
        {
            if (_sessionState == null || !_isOpen)
                return;

            if (_sessionState.Phase ==
                PawnCheckRollSessionPhase.FailureDecision &&
                _sessionState.HasEvaluation)
            {
                var evaluation = _sessionState.Evaluation;
                var currentLuck = GetCurrentLuck();
                _resultWindow.ShowFailureActions(
                    evaluation.LuckCost,
                    currentLuck,
                    evaluation.CanChallenge &&
                    !_sessionState.ChallengeUsed,
                    evaluation.CanSpendLuck &&
                    !_sessionState.ChallengeUsed &&
                    currentLuck >= evaluation.LuckCost);
                ShowSessionStatus(
                    "실패했습니다. 결과창 하단에서 후속 행동을 선택하세요.");
                return;
            }

            if (_sessionState.Phase ==
                PawnCheckRollSessionPhase.Finalized)
            {
                _resultWindow.HideFailureActions();
                ShowSessionStatus(
                    "판정 결과가 캐릭터에 저장되었습니다. " +
                    "턴 리셋 시 초기화됩니다.");
            }
        }

        private void SaveCurrentWindowPositions()
        {
            if (_sessionState == null)
                return;

            if (_overlayDragHandle != null)
            {
                _sessionState.SetConfigWindowPosition(
                    _overlayDragHandle.AnchoredPosition);
            }

            if (_resultWindow != null)
            {
                _sessionState.SetResultWindowPosition(
                    _resultWindow.WindowPosition);
            }
        }

        private void ApplyStoredWindowPositions()
        {
            if (_sessionState == null)
                return;

            if (_sessionState.HasConfigWindowPosition)
            {
                _overlayDragHandle?.SetAnchoredPosition(
                    _sessionState.ConfigWindowPosition);
            }
            else
            {
                _overlayDragHandle?.SetAnchoredPosition(Vector2.zero);
            }
        }

        private PawnCheckRollState GetOrCreateState(
            InteractivePawn pawn)
        {
            var state = pawn.GetComponent<PawnCheckRollState>();
            if (state == null)
                state = pawn.gameObject.AddComponent<PawnCheckRollState>();
            return state;
        }

        private bool IsCharacterPawn(InteractivePawn pawn)
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

        private static PlayerStatState ResolveStatState(
            InteractivePawn pawn)
        {
            if (pawn == null)
                return null;

            var state = pawn.GetComponentInParent<PlayerStatState>();
            if (state == null)
                state = pawn.GetComponentInChildren<PlayerStatState>();
            return state;
        }

        private static string ResolvePawnName(InteractivePawn pawn)
        {
            if (pawn == null)
                return "캐릭터";

            var definition = pawn.Definition;
            return definition != null &&
                   !string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.DisplayName
                : pawn.name;
        }

        private static Color GetResultColor(
            in PawnCheckEvaluation evaluation)
        {
            if (evaluation.Grade == PawnCheckOutcomeGrade.Critical)
                return new Color(0.20f, 0.95f, 0.38f);
            if (evaluation.Grade == PawnCheckOutcomeGrade.Fumble)
                return Color.black;
            return evaluation.IsSuccessForDifficulty
                ? new Color(0.24f, 0.84f, 1f)
                : new Color(1f, 0.36f, 0.28f);
        }

        private static PawnRollResultTone GetResultTone(
            in PawnCheckEvaluation evaluation)
        {
            if (evaluation.Grade == PawnCheckOutcomeGrade.Critical)
                return PawnRollResultTone.Critical;
            if (evaluation.Grade == PawnCheckOutcomeGrade.Fumble)
                return PawnRollResultTone.Fumble;
            return PawnRollResultTone.Standard;
        }

        private void ConfigureExistingStatPanelInteraction()
        {
            if (_rootCanvas == null)
                return;

            var panels = _rootCanvas.GetComponentsInChildren<
                PawnStatPanelWidget>(true);
            for (var index = 0; index < panels.Length; index++)
            {
                var panel = panels[index];
                if (panel == null)
                    continue;

                var drawer = ReadPrivateField<RectTransform>(
                    panel,
                    "_drawerRect");
                if (drawer != null && _boardStackManager == null)
                {
                    var bounds = _rootCanvas.transform as RectTransform;
                    var availableHeight = bounds != null
                        ? Mathf.Max(420f, bounds.rect.height - 24f)
                        : Mathf.Max(420f, Screen.height - 24f);
                    var size = drawer.sizeDelta;
                    size.y = Mathf.Min(size.y, availableHeight);
                    drawer.sizeDelta = size;
                    PawnUiDragHandle.Attach(
                        drawer,
                        bounds,
                        52f,
                        8f,
                        true);
                }

                var scrollRects =
                    panel.GetComponentsInChildren<ScrollRect>(true);
                for (var scrollIndex = 0;
                     scrollIndex < scrollRects.Length;
                     scrollIndex++)
                {
                    var scroll = scrollRects[scrollIndex];
                    if (scroll == null)
                        continue;

                    scroll.vertical = true;
                    scroll.horizontal = false;
                    scroll.inertia = true;
                    scroll.decelerationRate = 0.135f;
                    scroll.scrollSensitivity = 42f;
                    scroll.movementType =
                        ScrollRect.MovementType.Clamped;
                    if (scroll.viewport != null &&
                        scroll.viewport.GetComponent<Graphic>() == null)
                    {
                        var hitArea =
                            scroll.viewport.gameObject.AddComponent<Image>();
                        hitArea.color = new Color(0f, 0f, 0f, 0.001f);
                        hitArea.raycastTarget = true;
                    }
                    if (scroll.content != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(
                            scroll.content);
                    }
                }
            }

            _lastConfiguredScreenSize =
                new Vector2Int(Screen.width, Screen.height);
        }

        private static T ReadPrivateField<T>(
            object owner,
            string fieldName)
            where T : class
        {
            if (owner == null || string.IsNullOrWhiteSpace(fieldName))
                return null;

            var field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(owner) as T : null;
        }

        private void BindResourceState(InteractivePawn pawn)
        {
            var nextState = ResolveStatState(pawn);
            if (ReferenceEquals(_resourceState, nextState))
                return;

            UnbindResourceState();
            _resourceState = nextState;
            if (_resourceState == null)
                return;

            if (!_resourceState.IsInitialized)
                _resourceState.Initialize();
            _resourceState.Changed += HandleResourceStateChanged;
        }

        private void UnbindResourceState()
        {
            if (_resourceState != null)
                _resourceState.Changed -= HandleResourceStateChanged;
            _resourceState = null;
        }

        private void HandleResourceStateChanged()
        {
            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            ScheduleResourceRefresh(pawn);
        }

        private void ScheduleResourceRefresh(InteractivePawn pawn)
        {
            StopResourceRefresh();
            if (!isActiveAndEnabled)
                return;

            _resourceRefreshRoutine = StartCoroutine(
                RefreshResourceBarNextFrame(pawn));
        }

        private IEnumerator RefreshResourceBarNextFrame(
            InteractivePawn pawn)
        {
            yield return null;
            RefreshResourceBarWithLuck(pawn);
            ConfigureExistingStatPanelInteraction();
            _resourceRefreshRoutine = null;
        }

        private void StopResourceRefresh()
        {
            if (_resourceRefreshRoutine == null)
                return;

            StopCoroutine(_resourceRefreshRoutine);
            _resourceRefreshRoutine = null;
        }

        private void RefreshResourceBarWithLuck(InteractivePawn pawn)
        {
            if (_infoBar == null || pawn == null)
                return;

            var state = ResolveStatState(pawn);
            if (state == null)
                return;
            if (!state.IsInitialized)
                state.Initialize();
            if (!state.IsInitialized || state.Runtime == null)
                return;

            var resourceBar = _infoBar.GetComponentInChildren<
                PawnResourceBarWidget>(true);
            if (resourceBar == null)
                return;

            var resources = new List<PawnResourceValueData>(4);
            TryAddRoleResource(
                state.Runtime,
                StatRole.HealthCurrent,
                StatRole.HealthMax,
                "체력",
                resources);
            TryAddRoleResource(
                state.Runtime,
                StatRole.SanityCurrent,
                StatRole.SanityMax,
                "이성",
                resources);
            TryAddRoleResource(
                state.Runtime,
                StatRole.MagicCurrent,
                StatRole.MagicMax,
                "마력",
                resources);
            TryAddLuckResource(state.Runtime, resources);
            resourceBar.Bind(resources);
        }

        private static void TryAddRoleResource(
            StatRuntimeState runtime,
            StatRole currentRole,
            StatRole maximumRole,
            string label,
            ICollection<PawnResourceValueData> destination)
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
                maximumId = currentDefinition.MaxStatId;

            var current = runtime.GetNumber(currentId);
            var maximum = current;
            var canEditMaximum = false;
            if (!string.IsNullOrWhiteSpace(maximumId) &&
                runtime.TryGetDefinition(
                    maximumId,
                    out var maximumDefinition))
            {
                maximum = runtime.GetNumber(maximumId);
                canEditMaximum = IsEditableResource(maximumDefinition);
            }

            destination.Add(new PawnResourceValueData(
                label,
                currentId,
                current,
                maximumId,
                maximum,
                true,
                IsEditableResource(currentDefinition),
                canEditMaximum));
        }

        private static void TryAddLuckResource(
            StatRuntimeState runtime,
            ICollection<PawnResourceValueData> destination)
        {
            if (!runtime.TryGetDefinition(
                    LuckStatId,
                    out var definition))
            {
                return;
            }

            destination.Add(new PawnResourceValueData(
                "운",
                LuckStatId,
                runtime.GetNumber(LuckStatId),
                string.Empty,
                99d,
                true,
                IsEditableResource(definition),
                false));
        }

        private static bool IsEditableResource(
            IStatDefinition definition)
        {
            if (definition == null)
                return false;

            return definition.Source == StatValueSource.Base ||
                   definition.Source == StatValueSource.Runtime &&
                   definition.IsAdjustable;
        }

        private void EnsureLegacyRollBlocker()
        {
            if (_legacyRollBlocker != null || _rootCanvas == null)
                return;

            var blocker = new GameObject(
                "PawnLegacyRollInputBlocker",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = blocker.GetComponent<RectTransform>();
            rect.SetParent(_rootCanvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = blocker.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.001f);
            image.raycastTarget = true;
            blocker.SetActive(false);
            _legacyRollBlocker = blocker;
        }

        private void SetLegacyRollBlocker(bool active)
        {
            if (_legacyRollBlocker == null)
                return;

            _legacyRollBlocker.SetActive(active);
            if (active)
                _legacyRollBlocker.transform.SetAsLastSibling();
        }

        private void StartLegacyRollTimeout()
        {
            StopLegacyRollTimeout();
            _legacyRollTimeoutRoutine = StartCoroutine(
                ReleaseLegacyBlockerAfterTimeout());
        }

        private IEnumerator ReleaseLegacyBlockerAfterTimeout()
        {
            yield return new WaitForSecondsRealtime(5f);
            _legacyRollTimeoutRoutine = null;
            SetLegacyRollBlocker(false);
            _legacyRollOwner = null;
        }

        private void StopLegacyRollTimeout()
        {
            if (_legacyRollTimeoutRoutine == null)
                return;

            StopCoroutine(_legacyRollTimeoutRoutine);
            _legacyRollTimeoutRoutine = null;
        }

        private void BindLegacyPresentationEvents()
        {
            if (_legacyPresentationBound || _infoBar == null)
                return;

            _infoBar.RollPresentationCompleted +=
                HandleLegacyPresentationCompleted;
            if (_effectButton != null)
            {
                _effectButton.onClick.RemoveListener(
                    CaptureLegacyRollOwner);
                _effectButton.onClick.AddListener(
                    CaptureLegacyRollOwner);
            }
            _legacyPresentationBound = true;
        }

        private void UnbindLegacyPresentationEvents()
        {
            if (!_legacyPresentationBound)
                return;

            if (_infoBar != null)
            {
                _infoBar.RollPresentationCompleted -=
                    HandleLegacyPresentationCompleted;
            }
            if (_effectButton != null)
            {
                _effectButton.onClick.RemoveListener(
                    CaptureLegacyRollOwner);
            }
            _legacyPresentationBound = false;
        }

        private void CaptureLegacyRollOwner()
        {
            _legacyRollOwner = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            SetLegacyRollBlocker(true);
            StartLegacyRollTimeout();
        }

        private void HandleLegacyPresentationCompleted()
        {
            StopLegacyRollTimeout();
            SetLegacyRollBlocker(false);
            _legacyRollOwner = null;
        }

        private string FindRollText(string objectName)
        {
            var texts = _rollWidget.GetComponentsInChildren<Text>(true);
            for (var index = 0; index < texts.Length; index++)
            {
                var text = texts[index];
                if (text != null && string.Equals(
                        text.gameObject.name,
                        objectName,
                        StringComparison.Ordinal))
                {
                    return text.text ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private void ResetIntegrationReferences(bool keepInfoBar)
        {
            HidePanelPreservingState();
            UnbindLegacyPresentationEvents();
            UnbindResultWindowEvents();
            UnbindOverlayEvents();
            UnbindResourceState();
            StopResourceRefresh();
            if (_overlay != null)
                Destroy(_overlay.gameObject);
            if (_resultWindow != null)
                Destroy(_resultWindow.gameObject);
            if (_legacyRollBlocker != null)
                Destroy(_legacyRollBlocker);

            _overlay = null;
            _resultWindow = null;
            _overlayDragHandle = null;
            _rootCanvas = null;
            _rollWidget = null;
            _checkButton = null;
            _checkButtonText = null;
            _effectButton = null;
            _legacyRollBlocker = null;
            if (!keepInfoBar)
                _infoBar = null;
        }

        private void StopIntegrationRoutine()
        {
            if (_integrationRoutine == null)
                return;

            StopCoroutine(_integrationRoutine);
            _integrationRoutine = null;
        }

        private void ShowSessionStatus(string message)
        {
            if (_boardStackManager == null)
                _overlay?.ShowStatusOnly(message);
        }

        private void OnDestroy()
        {
            StopIntegrationRoutine();
            StopDeferredRowBinding();
            StopLegacyRollTimeout();
            UnbindPawnEvents();
            UnbindLegacyPresentationEvents();
            UnbindResultWindowEvents();
            UnbindOverlayEvents();
            if (_overlay != null)
                Destroy(_overlay.gameObject);
            if (_resultWindow != null)
                Destroy(_resultWindow.gameObject);
            if (_legacyRollBlocker != null)
                Destroy(_legacyRollBlocker);
            SessionClosed = null;
        }
    }
}
