using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// GM용 턴 번호, 전체 행동값 리셋, 선택 Pawn 행동값 리셋을 담당합니다.
    ///
    /// 현재 코드베이스에서 실제 소모 자원으로 확인되는 것은
    /// PawnMovementManager의 이동 예산이므로 우선 이동값을 리셋합니다.
    /// Major / Minor / Reaction 등이 추가되면
    /// ResetPawnActionValues / ResetAllActionValues에 연결하면 됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnTurnResetManager : MonoBehaviour
    {
        private const int InitialTurnNumber = 1;
        private const int IntegrationFrameLimit = 120;

        private const string OwnedCanvasName =
            "PawnGmTurnResetCanvas";
        private const string TurnResetButtonName =
            "TurnResetButton";
        private const string ActionResetButtonName =
            "PawnActionResetButton";
        private const string ConfirmationRootName =
            "TurnResetConfirmation";
        private const string ActionGroupName =
            "PawnActionButtonGroup";

        [Header("Turn State")]
        [SerializeField, Min(1)]
        private int _currentTurnNumber = InitialTurnNumber;

        [Header("Top Left UI")]
        [SerializeField]
        private Vector2 _referenceResolution =
            new Vector2(1920f, 1080f);

        [SerializeField, Tooltip(
            "좌측 상단 턴 리셋 버튼 위치. " +
            "기존 턴 넘기기 버튼 오른쪽을 기본값으로 사용합니다.")]
        private Vector2 _resetButtonOffset =
            new Vector2(148f, -24f);

        [SerializeField, Min(72f)]
        private float _resetButtonSize = 112f;

        [SerializeField]
        private Color _buttonColor =
            new Color(0.075f, 0.16f, 0.22f, 0.96f);

        [Header("Selected Pawn Reset")]
        [SerializeField, Tooltip(
            "하단 InfoBar 우측 하단 기준 행동값 리셋 버튼 위치")]
        private Vector2 _actionResetOffset =
            new Vector2(-24f, 112f);

        [SerializeField]
        private Vector2 _actionResetSize =
            new Vector2(156f, 48f);

        private PawnManager _pawnManager;
        private PawnInfoBarWidget _infoBar;

        private GameObject _ownedCanvasObject;
        private Canvas _ownedCanvas;
        private Button _turnResetButton;
        private Text _turnResetButtonLabel;
        private Text _turnNumberLabel;

        private Button _actionResetButton;
        private Text _actionResetButtonLabel;

        private GameObject _confirmationRoot;
        private Text _confirmationBody;
        private Button _confirmationAcceptButton;
        private Button _confirmationCancelButton;

        private Coroutine _integrationRoutine;
        private bool _selectionEventsBound;

        public event Action<int> TurnNumberChanged;

        public int CurrentTurnNumber =>
            Mathf.Max(InitialTurnNumber, _currentTurnNumber);

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            var pawnManager = FindFirst<PawnManager>();
            if (pawnManager == null)
            {
                Debug.LogWarning(
                    "PawnTurnResetManager: PawnManager를 찾지 못했습니다.");
                return;
            }

            var managers = FindAll<PawnTurnResetManager>();
            var keeper =
                pawnManager.GetComponent<PawnTurnResetManager>();

            if (keeper == null)
            {
                for (var index = 0; index < managers.Length; index++)
                {
                    var candidate = managers[index];
                    if (candidate == null ||
                        !candidate.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    keeper = candidate;
                    break;
                }
            }

            if (keeper == null)
            {
                keeper = pawnManager.gameObject.AddComponent<
                    PawnTurnResetManager>();
            }

            keeper._pawnManager = pawnManager;
            keeper.enabled = true;

            for (var index = 0; index < managers.Length; index++)
            {
                var duplicate = managers[index];
                if (duplicate == null || duplicate == keeper)
                    continue;

                duplicate.enabled = false;
                UnityEngine.Object.Destroy(duplicate);
            }

            keeper.BeginIntegration();
        }

        public void Configure(PawnManager pawnManager)
        {
            if (pawnManager != null)
                _pawnManager = pawnManager;

            BeginIntegration();
        }

        /// <summary>
        /// Save/Load 또는 네트워크 상태에서 턴 번호를 복구할 때 사용합니다.
        /// </summary>
        public void SetCurrentTurnNumber(int turnNumber)
        {
            var next = Mathf.Max(InitialTurnNumber, turnNumber);
            if (_currentTurnNumber == next)
            {
                RefreshTurnNumberLabel();
                return;
            }

            _currentTurnNumber = next;
            RefreshTurnNumberLabel();
            TurnNumberChanged?.Invoke(_currentTurnNumber);
        }

        /// <summary>
        /// 선택 Pawn 하나의 행동값만 복구합니다.
        /// 턴 번호와 현재 TurnGroup에는 영향을 주지 않습니다.
        /// </summary>
        public bool ResetSelectedPawnActionValues()
        {
            if (!CanUseGmControls() ||
                _pawnManager == null ||
                _pawnManager.MovementManager == null)
            {
                return false;
            }

            var pawn = _pawnManager.SelectedInteractive;
            if (pawn == null || !pawn.IsMoveable)
                return false;

            ResetPawnActionValues(pawn);
            RefreshActionResetButton();
            return true;
        }

        /// <summary>
        /// 지정 Pawn의 행동 자원을 복구하는 확장 지점입니다.
        /// 현재는 이동 예산만 복구합니다.
        /// </summary>
        public void ResetPawnActionValues(InteractivePawn pawn)
        {
            if (pawn == null ||
                _pawnManager == null ||
                _pawnManager.MovementManager == null)
            {
                return;
            }

            _pawnManager.MovementManager.ResetMovementBudget(pawn);

            // 향후 Major / Minor / Reaction 등의 Runtime State가 생기면
            // 이 위치에서 해당 Pawn의 행동 자원을 함께 복구합니다.
        }

        /// <summary>
        /// 모든 Pawn의 행동 자원을 복구합니다.
        /// 이 메서드 자체는 턴 번호를 변경하지 않습니다.
        /// </summary>
        public void ResetAllActionValues()
        {
            if (_pawnManager == null ||
                _pawnManager.MovementManager == null)
            {
                return;
            }

            _pawnManager.MovementManager.ResetAllMovementBudgets();

            // 향후 Major / Minor / Reaction 등의 Runtime State가 생기면
            // 이 위치에서 모든 Pawn의 행동 자원을 함께 복구합니다.
        }

        private void Awake()
        {
            ResolvePawnManager();
            _currentTurnNumber =
                Mathf.Max(InitialTurnNumber, _currentTurnNumber);
        }

        private void OnEnable()
        {
            BeginIntegration();
        }

        private void OnDisable()
        {
            StopIntegration();
            UnbindSelectionEvents();
            UnbindTurnResetButton();
            UnbindActionResetButton();
            HideConfirmation();
        }

        private void OnDestroy()
        {
            StopIntegration();
            UnbindSelectionEvents();
            UnbindTurnResetButton();
            UnbindActionResetButton();

            if (_ownedCanvasObject != null)
            {
                Destroy(_ownedCanvasObject);
                _ownedCanvasObject = null;
                _ownedCanvas = null;
            }

            TurnNumberChanged = null;
        }

        private void BeginIntegration()
        {
            if (!isActiveAndEnabled)
                return;

            StopIntegration();
            _integrationRoutine =
                StartCoroutine(IntegrateWhenReady());
        }

        private void StopIntegration()
        {
            if (_integrationRoutine == null)
                return;

            StopCoroutine(_integrationRoutine);
            _integrationRoutine = null;
        }

        private IEnumerator IntegrateWhenReady()
        {
            for (var frame = 0;
                 frame < IntegrationFrameLimit;
                 frame++)
            {
                ResolvePawnManager();

                if (_pawnManager != null)
                {
                    EnsureGmUi();
                    BindSelectionEvents();

                    if (_infoBar == null)
                        _infoBar = FindFirst<PawnInfoBarWidget>();

                    if (_infoBar != null)
                    {
                        EnsureActionResetButton();
                        RefreshActionResetButton();
                    }

                    if (_turnResetButton != null &&
                        _infoBar != null)
                    {
                        _integrationRoutine = null;
                        yield break;
                    }
                }

                yield return null;
            }

            _integrationRoutine = null;
        }

        private bool ResolvePawnManager()
        {
            if (_pawnManager == null)
                _pawnManager = GetComponent<PawnManager>();
            if (_pawnManager == null)
                _pawnManager = GetComponentInParent<PawnManager>();
            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();

            return _pawnManager != null;
        }

        private void EnsureGmUi()
        {
            var visible = CanUseGmControls();

            EnsureTurnCanvas();
            EnsureTurnNumberLabel();

            var existingReset = FindExistingTurnResetButton();
            if (existingReset != null &&
                existingReset != _turnResetButton)
            {
                UnbindTurnResetButton();
                _turnResetButton = existingReset;
                _turnResetButtonLabel =
                    _turnResetButton.GetComponentInChildren<Text>(true);

                // 요구사항상 기존 "턴 리셋"의 즉시 실행을 제거하고
                // 확인 팝업을 거친 뒤 실행하도록 교체합니다.
                _turnResetButton.onClick.RemoveAllListeners();
                BindTurnResetButton();
            }

            if (_turnResetButton == null)
                CreateTurnResetButton();

            if (_turnResetButton != null)
                _turnResetButton.gameObject.SetActive(visible);

            if (_turnNumberLabel != null)
                _turnNumberLabel.gameObject.SetActive(visible);

            RefreshTurnNumberLabel();
        }

        private void EnsureTurnCanvas()
        {
            if (_ownedCanvas != null)
                return;

            var existing = GameObject.Find(OwnedCanvasName);
            if (existing != null)
            {
                _ownedCanvasObject = existing;
                _ownedCanvas = existing.GetComponent<Canvas>();
                if (_ownedCanvas != null)
                    return;
            }

            EnsureEventSystem();

            _ownedCanvasObject = new GameObject(
                OwnedCanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            _ownedCanvas = _ownedCanvasObject.GetComponent<Canvas>();
            _ownedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _ownedCanvas.sortingOrder = 5102;

            var scaler =
                _ownedCanvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void EnsureTurnNumberLabel()
        {
            if (_turnNumberLabel != null || _ownedCanvas == null)
                return;

            var root = CreateRect(
                "TurnNumberLabel",
                _ownedCanvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -146f),
                new Vector2(236f, 38f));

            var background = root.gameObject.AddComponent<Image>();
            background.color =
                new Color(0.035f, 0.065f, 0.085f, 0.86f);
            background.raycastTarget = false;

            _turnNumberLabel = CreateText(
                root,
                "Label",
                Vector2.zero,
                Vector2.zero,
                GetRuntimeFont(),
                22,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(_turnNumberLabel.rectTransform, 8f);
        }

        private void CreateTurnResetButton()
        {
            if (_ownedCanvas == null || _turnResetButton != null)
                return;

            var rect = CreateRect(
                TurnResetButtonName,
                _ownedCanvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                _resetButtonOffset,
                Vector2.one * _resetButtonSize);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = _buttonColor;

            _turnResetButton =
                rect.gameObject.AddComponent<Button>();
            _turnResetButton.targetGraphic = image;

            _turnResetButtonLabel = CreateText(
                rect,
                "Label",
                Vector2.zero,
                Vector2.zero,
                GetRuntimeFont(),
                22,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(_turnResetButtonLabel.rectTransform, 8f);
            _turnResetButtonLabel.text = "턴\n리셋";

            BindTurnResetButton();
        }

        private Button FindExistingTurnResetButton()
        {
            var buttons = FindAll<Button>();
            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                if (button == null ||
                    button == _turnResetButton ||
                    button.gameObject.name == ActionResetButtonName)
                {
                    continue;
                }

                if (string.Equals(
                        button.gameObject.name,
                        TurnResetButtonName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }

                var label =
                    button.GetComponentInChildren<Text>(true);
                if (label == null ||
                    string.IsNullOrWhiteSpace(label.text))
                {
                    continue;
                }

                var normalized =
                    label.text.Replace("\n", string.Empty)
                        .Replace(" ", string.Empty);

                if (normalized.Contains("턴리셋"))
                    return button;
            }

            return null;
        }

        private void BindTurnResetButton()
        {
            if (_turnResetButton == null)
                return;

            _turnResetButton.onClick.RemoveListener(
                HandleTurnResetClicked);
            _turnResetButton.onClick.AddListener(
                HandleTurnResetClicked);
        }

        private void UnbindTurnResetButton()
        {
            if (_turnResetButton != null)
            {
                _turnResetButton.onClick.RemoveListener(
                    HandleTurnResetClicked);
            }
        }

        private void HandleTurnResetClicked()
        {
            if (!CanUseGmControls())
                return;

            ShowConfirmation();
        }

        private void ShowConfirmation()
        {
            EnsureConfirmation();
            if (_confirmationRoot == null)
                return;

            RefreshConfirmationText();
            _confirmationRoot.SetActive(true);
            _confirmationRoot.transform.SetAsLastSibling();
        }

        private void HideConfirmation()
        {
            if (_confirmationRoot != null)
                _confirmationRoot.SetActive(false);
        }

        private void EnsureConfirmation()
        {
            if (_confirmationRoot != null || _ownedCanvas == null)
                return;

            var fullRect = CreateRect(
                ConfirmationRootName,
                _ownedCanvas.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            fullRect.offsetMin = Vector2.zero;
            fullRect.offsetMax = Vector2.zero;

            var dim = fullRect.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.64f);

            _confirmationRoot = fullRect.gameObject;

            var panel = CreateRect(
                "Panel",
                fullRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(520f, 300f));

            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color =
                new Color(0.045f, 0.075f, 0.095f, 0.985f);

            var title = CreateText(
                panel,
                "Title",
                new Vector2(0f, 104f),
                new Vector2(460f, 42f),
                GetRuntimeFont(),
                28,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            title.text = "턴 리셋";

            _confirmationBody = CreateText(
                panel,
                "Body",
                new Vector2(0f, 28f),
                new Vector2(450f, 112f),
                GetRuntimeFont(),
                20,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                new Color(0.90f, 0.94f, 0.97f, 1f));

            _confirmationCancelButton = CreateButton(
                panel,
                "CancelButton",
                new Vector2(-112f, -104f),
                new Vector2(180f, 58f),
                "취소",
                new Color(0.13f, 0.16f, 0.18f, 1f));

            _confirmationAcceptButton = CreateButton(
                panel,
                "AcceptButton",
                new Vector2(112f, -104f),
                new Vector2(180f, 58f),
                "리셋",
                new Color(0.46f, 0.12f, 0.10f, 1f));

            _confirmationCancelButton.onClick.AddListener(
                HideConfirmation);
            _confirmationAcceptButton.onClick.AddListener(
                HandleTurnResetConfirmed);

            _confirmationRoot.SetActive(false);
        }

        private void RefreshConfirmationText()
        {
            if (_confirmationBody == null)
                return;

            _confirmationBody.text =
                "모든 Pawn의 행동값을 리셋하시겠습니까?\n\n" +
                $"현재 턴 넘버는 {CurrentTurnNumber} 입니다.\n" +
                $"리셋하면 {CurrentTurnNumber + 1}턴으로 진행됩니다.";
        }

        private void HandleTurnResetConfirmed()
        {
            if (!CanUseGmControls())
            {
                HideConfirmation();
                return;
            }

            ResetAllActionValues();

            _currentTurnNumber =
                Mathf.Max(
                    InitialTurnNumber,
                    _currentTurnNumber + 1);

            RefreshTurnNumberLabel();
            TurnNumberChanged?.Invoke(_currentTurnNumber);
            HideConfirmation();
        }

        private void RefreshTurnNumberLabel()
        {
            if (_turnNumberLabel != null)
            {
                _turnNumberLabel.text =
                    $"TURN {CurrentTurnNumber:00}";
            }

            if (_turnResetButtonLabel != null &&
                string.IsNullOrWhiteSpace(
                    _turnResetButtonLabel.text))
            {
                _turnResetButtonLabel.text = "턴\n리셋";
            }
        }

        private void BindSelectionEvents()
        {
            if (_selectionEventsBound || _pawnManager == null)
                return;

            _pawnManager.InteractiveSelectionChanged +=
                HandleSelectionChanged;
            _selectionEventsBound = true;
        }

        private void UnbindSelectionEvents()
        {
            if (!_selectionEventsBound || _pawnManager == null)
                return;

            _pawnManager.InteractiveSelectionChanged -=
                HandleSelectionChanged;
            _selectionEventsBound = false;
        }

        private void HandleSelectionChanged(InteractivePawn pawn)
        {
            RefreshActionResetButton();
        }

        private void EnsureActionResetButton()
        {
            if (_actionResetButton != null || _infoBar == null)
                return;

            var panel = ResolveInfoBarPanel();
            if (panel == null)
                return;

            var existing =
                panel.Find(ActionResetButtonName);
            if (existing != null)
            {
                _actionResetButton =
                    existing.GetComponent<Button>();
                _actionResetButtonLabel =
                    existing.GetComponentInChildren<Text>(true);
                BindActionResetButton();
                return;
            }

            var rect = CreateRect(
                ActionResetButtonName,
                panel,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                _actionResetOffset,
                _actionResetSize);

            var image = rect.gameObject.AddComponent<Image>();
            image.color =
                new Color(0.12f, 0.24f, 0.29f, 0.98f);

            _actionResetButton =
                rect.gameObject.AddComponent<Button>();
            _actionResetButton.targetGraphic = image;

            _actionResetButtonLabel = CreateText(
                rect,
                "Label",
                Vector2.zero,
                Vector2.zero,
                ResolveInfoBarFont(),
                18,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(_actionResetButtonLabel.rectTransform, 6f);
            _actionResetButtonLabel.text = "행동값 리셋";

            BindActionResetButton();
        }

        private RectTransform ResolveInfoBarPanel()
        {
            if (_infoBar == null)
                return null;

            var rects =
                _infoBar.GetComponentsInChildren<RectTransform>(true);

            for (var index = 0; index < rects.Length; index++)
            {
                var rect = rects[index];
                if (rect == null ||
                    rect.gameObject.name != ActionGroupName)
                {
                    continue;
                }

                return rect.parent as RectTransform;
            }

            return _infoBar.transform as RectTransform;
        }

        private void BindActionResetButton()
        {
            if (_actionResetButton == null)
                return;

            _actionResetButton.onClick.RemoveListener(
                HandleActionResetClicked);
            _actionResetButton.onClick.AddListener(
                HandleActionResetClicked);
        }

        private void UnbindActionResetButton()
        {
            if (_actionResetButton != null)
            {
                _actionResetButton.onClick.RemoveListener(
                    HandleActionResetClicked);
            }
        }

        private void HandleActionResetClicked()
        {
            ResetSelectedPawnActionValues();
        }

        private void RefreshActionResetButton()
        {
            if (_actionResetButton == null)
                return;

            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;

            var valid =
                CanUseGmControls() &&
                pawn != null &&
                pawn.IsMoveable;

            _actionResetButton.gameObject.SetActive(valid);
            _actionResetButton.interactable = valid;
        }

        private bool CanUseGmControls()
        {
            var authority = TRPGSessionAuthority.Instance;

            // 네트워크 세션이 없는 오프라인/에디터 단독 플레이는
            // GM 조작 환경으로 취급합니다.
            if (authority == null || !authority.IsOnline)
                return true;

            return authority.IsLocalGameMaster;
        }

        private Font ResolveInfoBarFont()
        {
            if (_infoBar != null)
            {
                var text =
                    _infoBar.GetComponentInChildren<Text>(true);
                if (text != null && text.font != null)
                    return text.font;
            }

            return GetRuntimeFont();
        }

        private static Button CreateButton(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            string label,
            Color color)
        {
            var rect = CreateRect(
                objectName,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                anchoredPosition,
                size);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var text = CreateText(
                rect,
                "Label",
                Vector2.zero,
                Vector2.zero,
                GetRuntimeFont(),
                20,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(text.rectTransform, 6f);
            text.text = label;

            return button;
        }

        private static RectTransform CreateRect(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            var child = new GameObject(
                objectName,
                typeof(RectTransform));

            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Text CreateText(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color)
        {
            var rect = CreateRect(
                objectName,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                anchoredPosition,
                size);

            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            text.verticalOverflow =
                VerticalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(
            RectTransform rect,
            float inset)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
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
                // Unity 배포판에 따라 내장 폰트 이름이 다를 수 있습니다.
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
                return;

            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
        }

        private static T FindFirst<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }

        private static T[] FindAll<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<T>(true);
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _currentTurnNumber =
                Mathf.Max(
                    InitialTurnNumber,
                    _currentTurnNumber);
            _resetButtonSize =
                Mathf.Max(72f, _resetButtonSize);

            _actionResetSize.x =
                Mathf.Max(96f, _actionResetSize.x);
            _actionResetSize.y =
                Mathf.Max(36f, _actionResetSize.y);
        }
#endif
    }
}
