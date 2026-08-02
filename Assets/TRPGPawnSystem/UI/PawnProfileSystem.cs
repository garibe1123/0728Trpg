using System;
using Trpg.Pawns;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.UI.Profile
{
    [Serializable]
    public sealed class PawnProfileRuntimeSnapshot
    {
        public string CharacterDefinitionId;
        public string Appearance;
        public string BackgroundAndPersonality;
        public string PlayerRelationships;
        public string PhobiasAndManias;
        public string OtherNotes;
    }

    [DisallowMultipleComponent]
    public sealed class PawnProfileState : MonoBehaviour
    {
        [Header("Player Profile Defaults")]
        [SerializeField, TextArea(3, 12)]
        private string _defaultAppearance;
        [SerializeField, TextArea(3, 12)]
        private string _defaultBackgroundAndPersonality;
        [SerializeField, TextArea(3, 12)]
        private string _defaultPlayerRelationships;
        [SerializeField, TextArea(3, 12)]
        private string _defaultPhobiasAndManias;
        [SerializeField, TextArea(3, 12)]
        private string _defaultOtherNotes;

        private InteractivePawnDefinition _definition;
        private bool _isInitialized;
        private string _appearance = string.Empty;
        private string _backgroundAndPersonality = string.Empty;
        private string _playerRelationships = string.Empty;
        private string _phobiasAndManias = string.Empty;
        private string _otherNotes = string.Empty;

        public event Action Changed;

        public InteractivePawnDefinition Definition => _definition;
        public bool IsInitialized => _isInitialized;
        public string Appearance => _appearance;
        public string BackgroundAndPersonality =>
            _backgroundAndPersonality;
        public string PlayerRelationships => _playerRelationships;
        public string PhobiasAndManias => _phobiasAndManias;
        public string OtherNotes => _otherNotes;

        public bool Configure(InteractivePawnDefinition definition)
        {
            if (definition == null)
                return false;

            if (_definition != null &&
                !ReferenceEquals(_definition, definition) &&
                _isInitialized)
            {
                return false;
            }

            _definition = definition;
            return true;
        }

        public void Initialize()
        {
            if (_isInitialized || _definition == null)
                return;

            _appearance = Normalize(_defaultAppearance);
            _backgroundAndPersonality = Normalize(
                _defaultBackgroundAndPersonality);
            _playerRelationships = Normalize(
                _defaultPlayerRelationships);
            _phobiasAndManias = Normalize(_defaultPhobiasAndManias);
            _otherNotes = Normalize(_defaultOtherNotes);
            _isInitialized = true;
            Changed?.Invoke();
        }

        public bool TrySet(
            string appearance,
            string backgroundAndPersonality,
            string playerRelationships,
            string phobiasAndManias,
            string otherNotes)
        {
            if (!EnsureInitialized())
                return false;

            var nextAppearance = Normalize(appearance);
            var nextBackground = Normalize(backgroundAndPersonality);
            var nextRelationships = Normalize(playerRelationships);
            var nextPhobias = Normalize(phobiasAndManias);
            var nextOther = Normalize(otherNotes);

            if (string.Equals(
                    _appearance,
                    nextAppearance,
                    StringComparison.Ordinal) &&
                string.Equals(
                    _backgroundAndPersonality,
                    nextBackground,
                    StringComparison.Ordinal) &&
                string.Equals(
                    _playerRelationships,
                    nextRelationships,
                    StringComparison.Ordinal) &&
                string.Equals(
                    _phobiasAndManias,
                    nextPhobias,
                    StringComparison.Ordinal) &&
                string.Equals(
                    _otherNotes,
                    nextOther,
                    StringComparison.Ordinal))
            {
                return true;
            }

            _appearance = nextAppearance;
            _backgroundAndPersonality = nextBackground;
            _playerRelationships = nextRelationships;
            _phobiasAndManias = nextPhobias;
            _otherNotes = nextOther;
            Changed?.Invoke();
            return true;
        }

        public PawnProfileRuntimeSnapshot CreateSnapshot()
        {
            EnsureInitialized();
            return new PawnProfileRuntimeSnapshot
            {
                CharacterDefinitionId = _definition != null
                    ? _definition.Id
                    : string.Empty,
                Appearance = _appearance,
                BackgroundAndPersonality =
                    _backgroundAndPersonality,
                PlayerRelationships = _playerRelationships,
                PhobiasAndManias = _phobiasAndManias,
                OtherNotes = _otherNotes
            };
        }

        public bool TryApplySnapshot(
            PawnProfileRuntimeSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (_definition == null)
            {
                error = "캐릭터 정의가 연결되지 않았습니다.";
                return false;
            }

            if (snapshot == null)
            {
                error = "플레이어 정보 Snapshot이 비어 있습니다.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    snapshot.CharacterDefinitionId) &&
                !string.Equals(
                    snapshot.CharacterDefinitionId.Trim(),
                    _definition.Id != null
                        ? _definition.Id.Trim()
                        : string.Empty,
                    StringComparison.Ordinal))
            {
                error = "다른 Pawn Definition의 플레이어 정보입니다.";
                return false;
            }

            if (!EnsureInitialized())
            {
                error = "플레이어 정보 상태를 초기화하지 못했습니다.";
                return false;
            }

            return TrySet(
                snapshot.Appearance,
                snapshot.BackgroundAndPersonality,
                snapshot.PlayerRelationships,
                snapshot.PhobiasAndManias,
                snapshot.OtherNotes);
        }

        public static PawnProfileState ResolveOrCreate(
            GameObject selectedObject,
            InteractivePawnDefinition definition)
        {
            if (selectedObject == null || definition == null)
                return null;

            var pawn = ResolveInteractivePawn(selectedObject);
            var root = pawn != null
                ? pawn.gameObject
                : selectedObject;

            var state = root.GetComponent<PawnProfileState>();
            if (state == null)
            {
                state = root.GetComponentInChildren<
                    PawnProfileState>(true);
            }
            if (state == null)
                state = root.AddComponent<PawnProfileState>();

            if (!state.Configure(definition))
                return null;

            state.Initialize();
            return state.IsInitialized ? state : null;
        }

        private bool EnsureInitialized()
        {
            if (!_isInitialized)
                Initialize();
            return _isInitialized;
        }

        private static InteractivePawn ResolveInteractivePawn(
            GameObject selectedObject)
        {
            var pawn = selectedObject.GetComponent<InteractivePawn>();
            if (pawn == null)
            {
                pawn = selectedObject.GetComponentInParent<
                    InteractivePawn>(true);
            }
            if (pawn == null)
            {
                pawn = selectedObject.GetComponentInChildren<
                    InteractivePawn>(true);
            }

            return pawn;
        }

        private static string Normalize(string value)
        {
            return value ?? string.Empty;
        }

        private void OnDestroy()
        {
            Changed = null;
        }
    }

    public enum PawnProfileSection
    {
        Appearance,
        BackgroundAndPersonality,
        PlayerRelationships,
        PhobiasAndManias,
        OtherNotes
    }

    public sealed class PawnProfileWidget : MonoBehaviour
    {
        private const float PanelWidth = 720f;
        private const float PanelHeight = 470f;
        private const float TitleHeight = 48f;
        private const float TabWidth = 190f;
        private const float FooterHeight = 58f;
        private const float OuterPadding = 14f;
        private const int MaximumTextLength = 16000;

        private RectTransform _canvasRect;
        private Canvas _rootCanvas;
        private RectTransform _panel;
        private CanvasGroup _canvasGroup;
        private Text _titleText;
        private InputField _input;
        private Text _inputPlaceholder;
        private Button _applyButton;
        private Button _closeButton;
        private readonly Button[] _tabs = new Button[5];
        private readonly Text[] _tabLabels = new Text[5];
        private PawnProfileState _state;
        private PawnProfileSection _section;
        private string _displayName = string.Empty;
        private string _appearance = string.Empty;
        private string _background = string.Empty;
        private string _relationships = string.Empty;
        private string _phobias = string.Empty;
        private string _other = string.Empty;
        private bool _isVisible;
        private bool _hasUserPosition;
        private bool _isEmbedded;
        private RectTransform _legacyParent;
        private Vector2 _legacyAnchorMin;
        private Vector2 _legacyAnchorMax;
        private Vector2 _legacyPivot;
        private Vector2 _legacyAnchoredPosition;
        private Vector2 _legacySizeDelta;
        private RectTransform _titleBar;
        private RectTransform _tabsRoot;
        private RectTransform _inputRoot;
        private RectTransform _footer;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartPanel;

        public event Action CloseRequested;
        public event Action Applied;

        public bool IsVisible => _isVisible;

        public static PawnProfileWidget CreateRuntime(
            RectTransform parentRect,
            Font font)
        {
            if (parentRect == null)
                throw new ArgumentNullException(nameof(parentRect));

            var root = new GameObject(
                "PawnProfileWidget",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(PawnProfileWidget));
            var widget = root.GetComponent<PawnProfileWidget>();
            widget.Build(parentRect, font);
            return widget;
        }

        public RectTransform RootRect => _panel;
        public bool IsEmbedded => _isEmbedded;

        public void SetEmbeddedMode(
            RectTransform host,
            bool enabled)
        {
            if (_panel == null)
                return;

            if (enabled)
            {
                if (host == null)
                    throw new ArgumentNullException(nameof(host));

                if (!_isEmbedded)
                {
                    _legacyParent = _panel.parent as RectTransform;
                    _legacyAnchorMin = _panel.anchorMin;
                    _legacyAnchorMax = _panel.anchorMax;
                    _legacyPivot = _panel.pivot;
                    _legacyAnchoredPosition = _panel.anchoredPosition;
                    _legacySizeDelta = _panel.sizeDelta;
                }

                _isEmbedded = true;
                _panel.SetParent(host, false);
                _panel.anchorMin = Vector2.zero;
                _panel.anchorMax = Vector2.one;
                _panel.pivot = new Vector2(0.5f, 0.5f);
                _panel.offsetMin = Vector2.zero;
                _panel.offsetMax = Vector2.zero;
                _panel.localScale = Vector3.one;
                _hasUserPosition = true;
                ApplyEmbeddedLayout();
                return;
            }

            if (!_isEmbedded)
                return;

            _isEmbedded = false;
            if (_legacyParent != null)
                _panel.SetParent(_legacyParent, false);
            _panel.anchorMin = _legacyAnchorMin;
            _panel.anchorMax = _legacyAnchorMax;
            _panel.pivot = _legacyPivot;
            _panel.anchoredPosition = _legacyAnchoredPosition;
            _panel.sizeDelta = _legacySizeDelta;
            _panel.localScale = Vector3.one;
            ApplyFloatingLayout();
        }

        public void Bind(
            PawnProfileState state,
            string displayName)
        {
            _state = state;
            _displayName = string.IsNullOrWhiteSpace(displayName)
                ? "플레이어"
                : displayName.Trim();
            ReadState();
            _section = PawnProfileSection.Appearance;
            RefreshSection();
            RefreshTitle();
        }

        public void Show(RectTransform portraitAnchor)
        {
            if (_panel == null || _canvasGroup == null || _state == null)
                return;

            if (!_isEmbedded)
            {
                if (!_hasUserPosition)
                    PositionAboveAnchor(portraitAnchor);
                ClampInsideCanvas();
            }
            _panel.SetAsLastSibling();
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            _isVisible = true;
            gameObject.SetActive(true);
            RefreshSection();
        }

        public void Hide()
        {
            _isVisible = false;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        public void RefreshFromState()
        {
            if (_state == null)
                return;

            ReadState();
            RefreshSection();
        }

        internal void BeginDrag(PointerEventData eventData)
        {
            if (_isEmbedded || _panel == null || eventData == null)
                return;

            if (!TryGetCanvasLocalPoint(
                    eventData.position,
                    eventData.pressEventCamera,
                    out _dragStartPointer))
            {
                return;
            }

            _dragStartPanel = _panel.anchoredPosition;
        }

        internal void Drag(PointerEventData eventData)
        {
            if (_isEmbedded || _panel == null || eventData == null)
                return;

            if (!TryGetCanvasLocalPoint(
                    eventData.position,
                    eventData.pressEventCamera,
                    out var current))
            {
                return;
            }

            _panel.anchoredPosition =
                _dragStartPanel + current - _dragStartPointer;
            _hasUserPosition = true;
            ClampInsideCanvas();
        }

        private void Build(RectTransform parentRect, Font font)
        {
            _canvasRect = parentRect;
            _rootCanvas = parentRect.GetComponentInParent<Canvas>();
            _panel = transform as RectTransform;
            _panel.SetParent(parentRect, false);
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panel.anchoredPosition = Vector2.zero;

            var background = GetComponent<Image>();
            background.color = new Color(0.035f, 0.055f, 0.065f, 0.985f);

            _canvasGroup = GetComponent<CanvasGroup>();
            var resolvedFont = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");

            _titleBar = CreatePanel(
                "TitleBar",
                _panel,
                new Color(0.065f, 0.17f, 0.21f, 1f));
            var titleBar = _titleBar;
            SetAnchors(
                titleBar,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.up,
                new Vector2(0f, -TitleHeight),
                Vector2.zero);
            var dragHandle = titleBar.gameObject.AddComponent<
                PawnProfileDragHandle>();
            dragHandle.Configure(this);

            _titleText = CreateText(
                "Title",
                titleBar,
                resolvedFont,
                20,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);
            Stretch(_titleText.rectTransform, 18f, 64f, 0f, 0f);

            _closeButton = CreateButton(
                "CloseButton",
                titleBar,
                resolvedFont,
                "×",
                24,
                new Color(0.38f, 0.10f, 0.10f, 0.98f));
            SetAnchors(
                _closeButton.transform as RectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f),
                new Vector2(44f, 34f));
            _closeButton.onClick.AddListener(HandleCloseClicked);

            _tabsRoot = CreatePanel(
                "Tabs",
                _panel,
                new Color(0.045f, 0.08f, 0.095f, 0.98f));
            var tabsRoot = _tabsRoot;
            SetAnchors(
                tabsRoot,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                new Vector2(OuterPadding, -TitleHeight * 0.5f),
                new Vector2(
                    TabWidth,
                    -(TitleHeight + FooterHeight + OuterPadding * 2f)));

            var labels = new[]
            {
                "외관",
                "배경과 성격",
                "플레이어 간의 관계",
                "공포증과 집착증",
                "기타"
            };
            for (var index = 0; index < _tabs.Length; index++)
            {
                var button = CreateButton(
                    $"Tab_{index}",
                    tabsRoot,
                    resolvedFont,
                    labels[index],
                    16,
                    new Color(0.075f, 0.13f, 0.15f, 1f));
                var rect = button.transform as RectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(
                    0f,
                    -8f - index * 62f);
                rect.sizeDelta = new Vector2(-12f, 52f);
                var captured = index;
                button.onClick.AddListener(
                    () => SelectSection(
                        (PawnProfileSection)captured));
                _tabs[index] = button;
                _tabLabels[index] =
                    button.GetComponentInChildren<Text>(true);
            }

            _inputRoot = CreatePanel(
                "ProfileInput",
                _panel,
                new Color(0.025f, 0.035f, 0.04f, 1f));
            var inputRoot = _inputRoot;
            inputRoot.anchorMin = Vector2.zero;
            inputRoot.anchorMax = Vector2.one;
            inputRoot.offsetMin = new Vector2(
                OuterPadding + TabWidth + OuterPadding,
                FooterHeight + OuterPadding);
            inputRoot.offsetMax = new Vector2(
                -OuterPadding,
                -TitleHeight - OuterPadding);

            inputRoot.gameObject.AddComponent<RectMask2D>();
            _input = inputRoot.gameObject.AddComponent<InputField>();
            _input.lineType = InputField.LineType.MultiLineNewline;
            _input.contentType = InputField.ContentType.Standard;
            _input.characterLimit = MaximumTextLength;
            _input.caretColor = Color.white;
            _input.selectionColor = new Color(0.18f, 0.55f, 0.70f, 0.55f);

            var inputText = CreateText(
                "Text",
                inputRoot,
                resolvedFont,
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Color(0.91f, 0.95f, 0.97f, 1f));
            inputText.supportRichText = false;
            inputText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            inputText.verticalOverflow =
                VerticalWrapMode.Overflow;
            Stretch(inputText.rectTransform, 14f, 14f, 12f, 12f);

            _inputPlaceholder = CreateText(
                "Placeholder",
                inputRoot,
                resolvedFont,
                17,
                FontStyle.Italic,
                TextAnchor.UpperLeft,
                new Color(0.46f, 0.55f, 0.58f, 0.85f));
            _inputPlaceholder.supportRichText = false;
            Stretch(
                _inputPlaceholder.rectTransform,
                14f,
                14f,
                12f,
                12f);

            _input.textComponent = inputText;
            _input.placeholder = _inputPlaceholder;

            _footer = CreatePanel(
                "Footer",
                _panel,
                new Color(0.045f, 0.075f, 0.085f, 0.98f));
            var footer = _footer;
            SetAnchors(
                footer,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, FooterHeight));

            _applyButton = CreateButton(
                "ApplyButton",
                footer,
                resolvedFont,
                "적용",
                17,
                new Color(0.08f, 0.38f, 0.46f, 1f));
            SetAnchors(
                _applyButton.transform as RectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f),
                new Vector2(118f, 38f));
            _applyButton.onClick.AddListener(HandleApplyClicked);

            Hide();
        }

        private void SelectSection(PawnProfileSection section)
        {
            StoreCurrentSection();
            _section = section;
            RefreshSection();
        }

        private void StoreCurrentSection()
        {
            if (_input == null)
                return;

            switch (_section)
            {
                case PawnProfileSection.Appearance:
                    _appearance = _input.text;
                    break;
                case PawnProfileSection.BackgroundAndPersonality:
                    _background = _input.text;
                    break;
                case PawnProfileSection.PlayerRelationships:
                    _relationships = _input.text;
                    break;
                case PawnProfileSection.PhobiasAndManias:
                    _phobias = _input.text;
                    break;
                case PawnProfileSection.OtherNotes:
                    _other = _input.text;
                    break;
            }
        }

        private void RefreshSection()
        {
            if (_input == null)
                return;

            _input.SetTextWithoutNotify(GetSectionText(_section));
            if (_inputPlaceholder != null)
            {
                _inputPlaceholder.text = GetPlaceholder(_section);
            }

            for (var index = 0; index < _tabs.Length; index++)
            {
                var button = _tabs[index];
                if (button == null)
                    continue;

                var selected = index == (int)_section;
                var image = button.targetGraphic as Image;
                if (image != null)
                {
                    image.color = selected
                        ? new Color(0.10f, 0.40f, 0.49f, 1f)
                        : new Color(0.075f, 0.13f, 0.15f, 1f);
                }

                if (_tabLabels[index] != null)
                {
                    _tabLabels[index].color = selected
                        ? Color.white
                        : new Color(0.76f, 0.84f, 0.87f, 1f);
                }
            }
        }

        private void HandleApplyClicked()
        {
            StoreCurrentSection();
            if (_state == null ||
                !_state.TrySet(
                    _appearance,
                    _background,
                    _relationships,
                    _phobias,
                    _other))
            {
                return;
            }

            Applied?.Invoke();
        }

        private void HandleCloseClicked()
        {
            CloseRequested?.Invoke();
        }

        private void ReadState()
        {
            if (_state == null)
            {
                _appearance = string.Empty;
                _background = string.Empty;
                _relationships = string.Empty;
                _phobias = string.Empty;
                _other = string.Empty;
                return;
            }

            if (!_state.IsInitialized)
                _state.Initialize();
            _appearance = _state.Appearance;
            _background = _state.BackgroundAndPersonality;
            _relationships = _state.PlayerRelationships;
            _phobias = _state.PhobiasAndManias;
            _other = _state.OtherNotes;
        }

        private void RefreshTitle()
        {
            if (_titleText != null)
                _titleText.text = $"PLAYER INFORMATION · {_displayName}";
        }

        private string GetSectionText(PawnProfileSection section)
        {
            switch (section)
            {
                case PawnProfileSection.Appearance:
                    return _appearance;
                case PawnProfileSection.BackgroundAndPersonality:
                    return _background;
                case PawnProfileSection.PlayerRelationships:
                    return _relationships;
                case PawnProfileSection.PhobiasAndManias:
                    return _phobias;
                case PawnProfileSection.OtherNotes:
                    return _other;
                default:
                    return string.Empty;
            }
        }

        private static string GetPlaceholder(PawnProfileSection section)
        {
            switch (section)
            {
                case PawnProfileSection.Appearance:
                    return "체형, 복장, 인상, 특징적인 상처나 습관 등을 입력";
                case PawnProfileSection.BackgroundAndPersonality:
                    return "과거, 직업, 성장 배경과 성격을 입력";
                case PawnProfileSection.PlayerRelationships:
                    return "다른 플레이어 캐릭터와의 관계를 입력";
                case PawnProfileSection.PhobiasAndManias:
                    return "공포증, 집착증과 발현 조건을 입력";
                case PawnProfileSection.OtherNotes:
                    return "그 밖의 설정과 메모를 입력";
                default:
                    return string.Empty;
            }
        }

        private void PositionAboveAnchor(RectTransform anchor)
        {
            if (_panel == null || _canvasRect == null)
                return;

            if (anchor == null)
            {
                _panel.anchoredPosition = Vector2.zero;
                return;
            }

            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            var worldTopCenter = (corners[1] + corners[2]) * 0.5f;
            var camera = ResolveCanvasCamera();
            var screenPoint = RectTransformUtility.WorldToScreenPoint(
                camera,
                worldTopCenter);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect,
                    screenPoint,
                    camera,
                    out var localPoint))
            {
                return;
            }

            _panel.anchoredPosition = localPoint + new Vector2(
                PanelWidth * 0.5f - anchor.rect.width * 0.5f,
                PanelHeight * 0.5f + 16f);
        }

        private bool TryGetCanvasLocalPoint(
            Vector2 screenPoint,
            Camera eventCamera,
            out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (_canvasRect == null)
                return false;

            var camera = eventCamera != null
                ? eventCamera
                : ResolveCanvasCamera();
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                screenPoint,
                camera,
                out localPoint);
        }

        private Camera ResolveCanvasCamera()
        {
            if (_rootCanvas == null ||
                _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return _rootCanvas.worldCamera;
        }

        private void ClampInsideCanvas()
        {
            if (_panel == null || _canvasRect == null)
                return;

            var canvas = _canvasRect.rect;
            var half = _panel.rect.size * 0.5f;
            var margin = 10f;
            var position = _panel.anchoredPosition;
            position.x = Mathf.Clamp(
                position.x,
                canvas.xMin + half.x + margin,
                canvas.xMax - half.x - margin);
            position.y = Mathf.Clamp(
                position.y,
                canvas.yMin + half.y + margin,
                canvas.yMax - half.y - margin);
            _panel.anchoredPosition = position;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_isVisible && !_isEmbedded)
                ClampInsideCanvas();
        }

        private void ApplyEmbeddedLayout()
        {
            if (_titleBar != null)
                _titleBar.gameObject.SetActive(false);
            if (_closeButton != null)
                _closeButton.gameObject.SetActive(false);

            const float embeddedTabsHeight = 94f;
            if (_tabsRoot != null)
            {
                _tabsRoot.anchorMin = new Vector2(0f, 1f);
                _tabsRoot.anchorMax = new Vector2(1f, 1f);
                _tabsRoot.pivot = new Vector2(0.5f, 1f);
                _tabsRoot.anchoredPosition = Vector2.zero;
                _tabsRoot.sizeDelta = new Vector2(0f, embeddedTabsHeight);
                LayoutEmbeddedTabs();
            }

            if (_inputRoot != null)
            {
                _inputRoot.anchorMin = Vector2.zero;
                _inputRoot.anchorMax = Vector2.one;
                _inputRoot.offsetMin = new Vector2(
                    OuterPadding,
                    FooterHeight + OuterPadding);
                _inputRoot.offsetMax = new Vector2(
                    -OuterPadding,
                    -(embeddedTabsHeight + OuterPadding));
            }
        }

        private void ApplyFloatingLayout()
        {
            if (_titleBar != null)
                _titleBar.gameObject.SetActive(true);
            if (_closeButton != null)
                _closeButton.gameObject.SetActive(true);

            if (_tabsRoot != null)
            {
                SetAnchors(
                    _tabsRoot,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 0.5f),
                    new Vector2(OuterPadding, -TitleHeight * 0.5f),
                    new Vector2(
                        TabWidth,
                        -(TitleHeight + FooterHeight +
                          OuterPadding * 2f)));
                LayoutFloatingTabs();
            }

            if (_inputRoot != null)
            {
                _inputRoot.anchorMin = Vector2.zero;
                _inputRoot.anchorMax = Vector2.one;
                _inputRoot.offsetMin = new Vector2(
                    OuterPadding + TabWidth + OuterPadding,
                    FooterHeight + OuterPadding);
                _inputRoot.offsetMax = new Vector2(
                    -OuterPadding,
                    -TitleHeight - OuterPadding);
            }
        }

        private void LayoutEmbeddedTabs()
        {
            for (var index = 0; index < _tabs.Length; index++)
            {
                var button = _tabs[index];
                if (button == null)
                    continue;

                var row = index < 3 ? 0 : 1;
                var column = index < 3 ? index : index - 3;
                var columns = row == 0 ? 3f : 2f;
                var rect = button.transform as RectTransform;
                rect.anchorMin = new Vector2(column / columns, 1f);
                rect.anchorMax = new Vector2((column + 1f) / columns, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -4f - row * 44f);
                rect.sizeDelta = new Vector2(-6f, 38f);

                if (_tabLabels[index] != null)
                {
                    _tabLabels[index].fontSize = 13;
                    _tabLabels[index].resizeTextForBestFit = true;
                    _tabLabels[index].resizeTextMinSize = 10;
                    _tabLabels[index].resizeTextMaxSize = 13;
                }
            }
        }

        private void LayoutFloatingTabs()
        {
            for (var index = 0; index < _tabs.Length; index++)
            {
                var button = _tabs[index];
                if (button == null)
                    continue;

                var rect = button.transform as RectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(
                    0f,
                    -8f - index * 62f);
                rect.sizeDelta = new Vector2(-12f, 52f);

                if (_tabLabels[index] != null)
                {
                    _tabLabels[index].fontSize = 16;
                    _tabLabels[index].resizeTextForBestFit = false;
                }
            }
        }

        private void OnDestroy()
        {
            if (_applyButton != null)
            {
                _applyButton.onClick.RemoveListener(
                    HandleApplyClicked);
            }
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(
                    HandleCloseClicked);
            }

            CloseRequested = null;
            Applied = null;
        }

        private static RectTransform CreatePanel(
            string objectName,
            Transform parent,
            Color color)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            root.GetComponent<Image>().color = color;
            return rect;
        }

        private static Text CreateText(
            string objectName,
            Transform parent,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateButton(
            string objectName,
            Transform parent,
            Font font,
            string label,
            int fontSize,
            Color color)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = color;
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var text = CreateText(
                "Label",
                rect,
                font,
                fontSize,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(text.rectTransform, 5f, 5f, 2f, 2f);
            text.text = label;
            text.raycastTarget = false;
            return button;
        }

        private static void SetAnchors(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void Stretch(
            RectTransform rect,
            float left,
            float right,
            float bottom,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }

    internal sealed class PawnProfileDragHandle : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler
    {
        private PawnProfileWidget _owner;

        public void Configure(PawnProfileWidget owner)
        {
            _owner = owner;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _owner?.BeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            _owner?.Drag(eventData);
        }
    }
}
