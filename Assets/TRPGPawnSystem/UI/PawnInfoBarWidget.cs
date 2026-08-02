using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public readonly struct PawnInfoBarData
    {
        public PawnInfoBarData(
            string displayName,
            string description,
            Sprite portrait,
            int movementScore)
        {
            DisplayName = displayName;
            Description = description;
            Portrait = portrait;
            MovementScore = movementScore;
        }

        public string DisplayName { get; }
        public string Description { get; }
        public Sprite Portrait { get; }
        public int MovementScore { get; }
    }

    public sealed class PawnInfoBarWidget : MonoBehaviour
    {
        private const float CompactPanelMinimumWidth = 900f;
        private const float CompactPanelMaximumWidth = 1320f;
        private const float CompactPanelSideMargin = 24f;
        private const float CompactPanelMinimumHeight = 196f;
        private const float PortraitLayoutSize = 150f;
        private const float PortraitLayoutLeft = 20f;
        private const float InventoryButtonSize = 48f;
        private const float InventoryButtonGap = 10f;
        private const float ResourceLayoutLeft = 240f;
        private const float ResourceLayoutBottom = 18f;
        private const float ResourceLayoutWidth = 378f;
        private const float ResourceLayoutHeight = 138f;
        private const float ActionButtonWidth = 164f;
        private const float ActionButtonHeight = 72f;
        private const float ActionButtonSpacing = 10f;
        private const float ActionRightInset = 24f;
        private const float ActionBottomInset = 30f;

        [SerializeField] private RectTransform _panel;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _portraitImage;
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _movementText;
        [SerializeField, Tooltip(
            "비어 있으면 이동 수치 텍스트를 버튼으로 런타임 구성")]
        private Button _moveButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private RectTransform _cursorBadge;
        [SerializeField] private Image _cursorBadgeImage;
        [SerializeField] private Text _cursorDistanceText;
        [SerializeField] private RectTransform _canvasRect;
        [SerializeField] private PawnBoardOverlayGraphic _boardOverlay;
        [SerializeField, Tooltip(
            "비어 있으면 정보 바 안에 굴림 UI를 런타임 생성")]
        private PawnRollWidget _rollWidget;
        private PawnStatPanelWidget _statPanel;
        private PawnResourceBarWidget _resourceBar;
        private Button _statToggleButton;
        private Graphic _statToggleGraphic;
        private RectTransform _actionGroup;
        private RectTransform _checkRollButtonRect;
        private RectTransform _effectRollButtonRect;

        private GameObject _ownedBoardOverlayCanvas;
        private Vector2 _shownPosition;
        private Vector2 _hiddenPosition;
        private float _showDuration;
        private float _hideDuration;
        private Sequence _transition;
        private bool _isVisible;
        private bool _isMovementModeActive;
        private bool _hasMovementBudget;
        private int _movementScore;
        private float _remainingMovementMeters;
        private float _maximumMovementMeters;
        private Image _moveButtonImage;
        private Button _inventoryButton;
        private RectTransform _inventoryButtonRect;
        private Button _portraitButton;
        private bool _isApplyingCompactLayout;
        private bool _hasCompactLayout;
        private float _hiddenPadding;
        private bool _boardStackMode;
        private bool _hasBoardStackInfo;
        private bool _hasBoardStackStats;
        private PawnInfoBarData _boardStackInfo;
        private PawnStatPanelData _boardStackStats;

        public event Action CloseRequested;
        public event Action MoveRequested;
        public event Action RollInputOpened;
        public event Action<PawnCheckRollRequest> CheckRollRequested;
        public event Action<PawnEffectRollRequest> EffectRollRequested;
        public event Action RollPresentationCompleted;
        public event Action<string, double> StatValueEditRequested;
        public event Action<string, double>
            ResourceStatValueEditRequested;
        public event Action<string, double>
            PlayerStatValueEditRequested;
        public event Action<PawnSkillAddRequest>
            PlayerSkillAddRequested;
        public event Action<PawnSkillNameEditRequest>
            PlayerSkillNameEditRequested;
        public event Action<PawnSkillRegularEditRequest>
            PlayerSkillRegularEditRequested;
        public event Action<PawnSkillRemoveRequest>
            PlayerSkillRemoveRequested;
        public event Action PlayerHudRequested;
        public event Action InventoryRequested;
        public event Action ProfileRequested;
        public event Action BoardStackStatsRequested;
        public event Action BoardStackBagRequested;
        public event Action BoardStackProfileRequested;
        public event Action<PawnInfoBarData> BoardStackInfoChanged;
        public event Action<PawnStatPanelData> BoardStackStatsChanged;
        public event Action<float, float> BoardStackMovementChanged;
        public event Action BoardStackUnbound;

        public RectTransform PortraitAnchorRect
        {
            get
            {
                EnsurePortraitButton();
                return _portraitImage != null
                    ? _portraitImage.rectTransform
                    : null;
            }
        }

        public RectTransform InventoryAnchorRect
        {
            get
            {
                EnsureInventoryButton();
                return _inventoryButtonRect;
            }
        }

        public RectTransform FullCanvasRect => _canvasRect;
        public RectTransform PanelRect => _panel;
        public RectTransform PortraitRect =>
            _portraitImage != null ? _portraitImage.rectTransform : null;
        public Font UiFont => _nameText != null ? _nameText.font : null;
        public bool IsBoardStackMode => _boardStackMode;
        public bool HasBoardStackInfo => _hasBoardStackInfo;
        public bool HasBoardStackStats => _hasBoardStackStats;
        public PawnInfoBarData BoardStackInfo => _boardStackInfo;
        public PawnStatPanelData BoardStackStats => _boardStackStats;
        public PawnStatPanelWidget BoardStackStatPanel
        {
            get
            {
                EnsureStatPanel();
                return _statPanel;
            }
        }

        public void SetBoardStackMode(bool enabled)
        {
            _boardStackMode = enabled;
            if (enabled)
                _statPanel?.SetExpanded(false);
            RefreshStatToggleVisual();
        }

        public void RequestBoardStackMove()
        {
            MoveRequested?.Invoke();
        }

        public static PawnInfoBarWidget CreateRuntime(
            PawnSystemSettings settings)
        {
            return PawnInfoBarFactory.Create(settings);
        }
        public void Bind(in PawnInfoBarData data)
        {
            _nameText.text = data.DisplayName;
            _descriptionText.text = data.Description;
            _portraitImage.sprite = data.Portrait;
            _portraitImage.enabled = data.Portrait != null;
            _boardStackInfo = data;
            _hasBoardStackInfo = true;
            BoardStackInfoChanged?.Invoke(data);
            EnsurePortraitButton();
            BindPortraitButton();
            EnsureInventoryButton();
            BindInventoryButton();
            SetInventoryButtonEnabled(true);
            _movementScore = data.MovementScore;
            _hasMovementBudget = false;
            EnsureMoveButton();
            SetMovementModeState(false, false);
            EnsureRollWidget();
            _rollWidget?.SetButtonsEnabled(true);
            EnsureResourceBar();
            EnsureStatPanel();
            EnsureStatToggleButton();
            Show();
        }
        public void Unbind()
        {
            _hasBoardStackInfo = false;
            _hasBoardStackStats = false;
            BoardStackUnbound?.Invoke();
            SetProfileButtonEnabled(false);
            SetInventoryButtonEnabled(false);
            Hide();
            _nameText.text = string.Empty;
            _descriptionText.text = string.Empty;
            _hasMovementBudget = false;
            SetMovementModeState(false, false);
            _rollWidget?.CancelPresentation();
            _rollWidget?.SetButtonsEnabled(false);
            ClearResourceStats();
            _statPanel?.SetExpanded(false);
            SetPathPreview(PawnPathPreviewData.Hidden);
            SetMovementRange(PawnMovementRangeData.Hidden);
        }
        public void SetStats(in PawnStatPanelData data)
        {
            _boardStackStats = data;
            _hasBoardStackStats = true;
            BoardStackStatsChanged?.Invoke(data);
            SetResourceStats(data.Resources);
            SetPlayerStats(data);
        }
        public void SetResourceStats(
            System.Collections.Generic.IReadOnlyList<
                PawnResourceValueData> resources)
        {
            EnsureResourceBar();
            _resourceBar?.Bind(resources);
            if (_descriptionText != null)
                _descriptionText.gameObject.SetActive(false);
            RefreshResourceBarLayout();
        }
        public void ClearResourceStats()
        {
            _resourceBar?.Clear();
            if (_descriptionText != null)
                _descriptionText.gameObject.SetActive(true);
        }
        public void SetPlayerStats(in PawnStatPanelData data)
        {
            EnsureStatPanel();
            EnsureStatToggleButton();
            _statPanel?.Bind(data);
            if (_statToggleButton != null)
            {
                _statToggleButton.gameObject.SetActive(true);
                _statToggleButton.interactable = true;
            }
            RefreshStatToggleVisual();
        }
        public void ClearPlayerStats()
        {
            _hasBoardStackStats = false;
            _statPanel?.Clear();
            if (_statToggleButton != null)
            {
                _statToggleButton.interactable = false;
                _statToggleButton.gameObject.SetActive(false);
            }
            RefreshStatToggleVisual();
        }
        public void ClearStats()
        {
            ClearResourceStats();
            ClearPlayerStats();
        }
        public void SetRollButtonsEnabled(bool enabled)
        {
            EnsureRollWidget();
            _rollWidget?.SetButtonsEnabled(enabled);
        }
        public void SetMovementModeState(bool enabled, bool active)
        {
            EnsureMoveButton();
            _isMovementModeActive = enabled && active;
            if (_moveButton != null)
            {
                _moveButton.interactable = enabled;
            }

            if (_moveButtonImage != null)
            {
                _moveButtonImage.color = _isMovementModeActive
                    ? new Color(0.08f, 0.42f, 0.58f, 0.98f)
                    : enabled
                        ? new Color(0.08f, 0.20f, 0.25f, 0.96f)
                        : new Color(0.07f, 0.08f, 0.09f, 0.72f);
            }

            RefreshMovementText();
        }
        public void SetRollButtonLabels(
            string checkLabel,
            string effectLabel)
        {
            EnsureRollWidget();
            _rollWidget?.SetButtonLabels(checkLabel, effectLabel);
        }
        public void SetRollInputDefaults(
            int checkTarget,
            int diceCount,
            int diceSides,
            int modifier)
        {
            EnsureRollWidget();
            _rollWidget?.SetInputDefaults(
                checkTarget,
                diceCount,
                diceSides,
                modifier);
        }
        public void PlayRoll(in PawnRollPresentationData data)
        {
            EnsureRollWidget();
            _rollWidget?.Play(data);
        }
        public void CancelRollPresentation()
        {
            _rollWidget?.CancelPresentation();
        }
        public void SetMovementBudget(
            float remainingMeters,
            float maximumMeters)
        {
            if (_movementText == null)
            {
                return;
            }

            _remainingMovementMeters = remainingMeters;
            _maximumMovementMeters = maximumMeters;
            _hasMovementBudget = true;
            BoardStackMovementChanged?.Invoke(
                remainingMeters,
                maximumMeters);
            RefreshMovementText();
        }
        public void SetMovementRange(
            PawnMovementRangeData data,
            Camera boardCamera = null)
        {
            EnsureBoardOverlay();
            if (_boardOverlay != null)
            {
                _boardOverlay.SetRange(data, boardCamera);
            }
        }
        public void SetPathPreview(
            PawnPathPreviewData data,
            Camera boardCamera = null)
        {
            EnsureBoardOverlay();
            if (_boardOverlay != null)
            {
                _boardOverlay.SetPath(data, boardCamera);
            }

            if (_cursorBadge == null || _cursorDistanceText == null)
            {
                return;
            }

            _cursorBadge.gameObject.SetActive(data.IsVisible);
            if (!data.IsVisible)
            {
                return;
            }

            _cursorDistanceText.text = !data.HasPath
                ? "이동 불가"
                : data.CanMove
                    ? $"{data.DistanceMeters:0.0}m · 이동 가능"
                    : $"{data.DistanceMeters:0.0}m · 범위 밖";
            _cursorDistanceText.color =
                data.HasPath && data.CanMove
                    ? new Color(0.35f, 0.95f, 1f)
                    : new Color(1f, 0.42f, 0.32f);
            if (_cursorBadgeImage != null)
            {
                _cursorBadgeImage.color = data.HasPath && data.CanMove
                    ? new Color(0.04f, 0.10f, 0.12f, 0.94f)
                    : new Color(0.16f, 0.045f, 0.035f, 0.94f);
            }

            PositionCursorBadge(data.ScreenPosition);
        }
        public void Show()
        {
            KillTransition();
            SetInteraction(true);

            if (_isVisible)
            {
                _panel.anchoredPosition = _shownPosition;
                _canvasGroup.alpha = 1f;
                return;
            }

            _isVisible = true;
            _transition = DOTween.Sequence()
                .Join(DOTween.To(
                        () => _panel.anchoredPosition,
                        value => _panel.anchoredPosition = value,
                        _shownPosition,
                        _showDuration)
                    .SetEase(Ease.OutCubic))
                .Join(DOTween.To(
                    () => _canvasGroup.alpha,
                    value => _canvasGroup.alpha = value,
                    1f,
                    _showDuration))
                .SetUpdate(true);
        }
        public void Hide()
        {
            if (_panel == null || _canvasGroup == null)
            {
                return;
            }

            KillTransition();
            _isVisible = false;
            SetInteraction(false);
            _rollWidget?.CancelPresentation();
            SetPathPreview(PawnPathPreviewData.Hidden);
            SetMovementRange(PawnMovementRangeData.Hidden);
            _transition = DOTween.Sequence()
                .Join(DOTween.To(
                        () => _panel.anchoredPosition,
                        value => _panel.anchoredPosition = value,
                        _hiddenPosition,
                        _hideDuration)
                    .SetEase(Ease.InCubic))
                .Join(DOTween.To(
                    () => _canvasGroup.alpha,
                    value => _canvasGroup.alpha = value,
                    0f,
                    _hideDuration))
                .SetUpdate(true);
        }
        internal void Configure(
            RectTransform panel,
            CanvasGroup canvasGroup,
            Image portraitImage,
            Text nameText,
            Text descriptionText,
            Text movementText,
            Button closeButton,
            RectTransform cursorBadge,
            Image cursorBadgeImage,
            Text cursorDistanceText,
            RectTransform canvasRect,
            float showDuration,
            float hideDuration,
            float hiddenPadding)
        {
            _panel = panel;
            _canvasGroup = canvasGroup;
            _portraitImage = portraitImage;
            _nameText = nameText;
            _descriptionText = descriptionText;
            _movementText = movementText;
            _closeButton = closeButton;
            _cursorBadge = cursorBadge;
            _cursorBadgeImage = cursorBadgeImage;
            _cursorDistanceText = cursorDistanceText;
            _canvasRect = canvasRect;
            _showDuration = showDuration;
            _hideDuration = hideDuration;
            _hiddenPadding = hiddenPadding;
            ApplyCompactPanelLayout();
            _shownPosition = panel.anchoredPosition;
            _hiddenPosition = _shownPosition +
                Vector2.down * (panel.rect.height + hiddenPadding);
            _panel.anchoredPosition = _hiddenPosition;
            _canvasGroup.alpha = 0f;
            SetInteraction(false);
            ClearBoardOverlay();
            EnsureMoveButton();
            BindMoveButton();
            EnsurePortraitButton();
            BindPortraitButton();
            SetProfileButtonEnabled(false);
            EnsureInventoryButton();
            BindInventoryButton();
            SetInventoryButtonEnabled(false);
            BindCloseButton();
            EnsureActionGroup();
            EnsureRollWidget();
            BindRollWidget();
            EnsureResourceBar();
            BindResourceBar();
            EnsureStatPanel();
            BindStatPanel();
            EnsureStatToggleButton();
            BindStatToggleButton();
        }

        private void PositionCursorBadge(Vector2 screenPosition)
        {
            if (_canvasRect == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect,
                    screenPosition,
                    null,
                    out var localPoint))
            {
                return;
            }

            var desired = localPoint + new Vector2(18f, 22f);
            var canvasRect = _canvasRect.rect;
            var size = _cursorBadge.rect.size;
            desired.x = Mathf.Clamp(
                desired.x,
                canvasRect.xMin,
                canvasRect.xMax - size.x);
            desired.y = Mathf.Clamp(
                desired.y,
                canvasRect.yMin,
                canvasRect.yMax - size.y);
            _cursorBadge.anchoredPosition = desired;
        }

        private void OnEnable()
        {
            EnsureBoardOverlay();
            EnsureMoveButton();
            BindMoveButton();
            EnsurePortraitButton();
            BindPortraitButton();
            EnsureInventoryButton();
            BindInventoryButton();
            BindCloseButton();
            EnsureActionGroup();
            EnsureRollWidget();
            BindRollWidget();
            EnsureResourceBar();
            BindResourceBar();
            EnsureStatPanel();
            BindStatPanel();
            EnsureStatToggleButton();
            BindStatToggleButton();
            _statPanel?.RefreshResponsiveLayout();
            ApplyActionRowContentPadding();
        }

        private void OnRectTransformDimensionsChange()
        {
            ApplyCompactPanelLayout();
            _statPanel?.RefreshResponsiveLayout();
            ApplyActionRowContentPadding();
        }

        private void OnDisable()
        {
            UnbindMoveButton();
            UnbindPortraitButton();
            UnbindInventoryButton();
            UnbindCloseButton();
            UnbindRollWidget();
            UnbindResourceBar();
            UnbindStatPanel();
            UnbindStatToggleButton();
            KillTransition();
            _rollWidget?.CancelPresentation();
            ClearStats();
            ClearBoardOverlay();
        }

        private void OnDestroy()
        {
            UnbindMoveButton();
            UnbindPortraitButton();
            UnbindInventoryButton();
            UnbindCloseButton();
            UnbindRollWidget();
            UnbindResourceBar();
            UnbindStatPanel();
            UnbindStatToggleButton();
            if (_ownedBoardOverlayCanvas != null)
            {
                Destroy(_ownedBoardOverlayCanvas);
                _ownedBoardOverlayCanvas = null;
                _boardOverlay = null;
            }

            CloseRequested = null;
            MoveRequested = null;
            RollInputOpened = null;
            CheckRollRequested = null;
            EffectRollRequested = null;
            RollPresentationCompleted = null;
            StatValueEditRequested = null;
            ResourceStatValueEditRequested = null;
            PlayerStatValueEditRequested = null;
            PlayerSkillAddRequested = null;
            PlayerSkillNameEditRequested = null;
            PlayerSkillRegularEditRequested = null;
            PlayerSkillRemoveRequested = null;
            PlayerHudRequested = null;
            InventoryRequested = null;
            ProfileRequested = null;
            BoardStackStatsRequested = null;
            BoardStackBagRequested = null;
            BoardStackProfileRequested = null;
            BoardStackInfoChanged = null;
            BoardStackStatsChanged = null;
            BoardStackMovementChanged = null;
            BoardStackUnbound = null;
        }

        private void EnsurePortraitButton()
        {
            if (_portraitImage == null)
                return;

            if (_portraitButton == null)
            {
                _portraitButton =
                    _portraitImage.GetComponent<Button>();
                if (_portraitButton == null)
                {
                    _portraitButton =
                        _portraitImage.gameObject.AddComponent<Button>();
                }
            }

            _portraitImage.raycastTarget = true;
            _portraitButton.targetGraphic = _portraitImage;
            _portraitButton.transition =
                Selectable.Transition.ColorTint;
        }

        private void BindPortraitButton()
        {
            if (_portraitButton == null)
                return;

            _portraitButton.onClick.RemoveListener(
                HandlePortraitClicked);
            _portraitButton.onClick.AddListener(
                HandlePortraitClicked);
        }

        private void UnbindPortraitButton()
        {
            if (_portraitButton != null)
            {
                _portraitButton.onClick.RemoveListener(
                    HandlePortraitClicked);
            }
        }

        public void SetProfileButtonEnabled(bool enabled)
        {
            EnsurePortraitButton();
            if (_portraitButton != null)
                _portraitButton.interactable = enabled;
        }

        private void HandlePortraitClicked()
        {
            if (_boardStackMode)
            {
                BoardStackProfileRequested?.Invoke();
                return;
            }

            ProfileRequested?.Invoke();
        }

        private void EnsureInventoryButton()
        {
            if (_inventoryButton != null || _panel == null)
                return;

            var buttonObject = new GameObject(
                "InventoryButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            _inventoryButtonRect =
                buttonObject.GetComponent<RectTransform>();
            _inventoryButtonRect.SetParent(_panel, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.08f, 0.22f, 0.28f, 0.98f);

            _inventoryButton = buttonObject.GetComponent<Button>();
            _inventoryButton.targetGraphic = image;
            _inventoryButton.transition = Selectable.Transition.ColorTint;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(_inventoryButtonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(3f, 2f);
            labelRect.offsetMax = new Vector2(-3f, -2f);

            var label = labelObject.GetComponent<Text>();
            label.font = _nameText != null && _nameText.font != null
                ? _nameText.font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            label.fontSize = 14;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.90f, 0.95f, 0.97f, 1f);
            label.text = "가방";
            label.raycastTarget = false;

            ApplyInventoryButtonLayout();
        }

        private void BindInventoryButton()
        {
            if (_inventoryButton == null)
                return;

            _inventoryButton.onClick.RemoveListener(
                HandleInventoryClicked);
            _inventoryButton.onClick.AddListener(
                HandleInventoryClicked);
        }

        private void UnbindInventoryButton()
        {
            if (_inventoryButton != null)
            {
                _inventoryButton.onClick.RemoveListener(
                    HandleInventoryClicked);
            }
        }

        private void SetInventoryButtonEnabled(bool enabled)
        {
            EnsureInventoryButton();
            if (_inventoryButton == null)
                return;

            _inventoryButton.interactable = enabled;
            _inventoryButton.gameObject.SetActive(enabled);
        }

        private void HandleInventoryClicked()
        {
            if (_boardStackMode)
            {
                BoardStackBagRequested?.Invoke();
                return;
            }

            InventoryRequested?.Invoke();
        }

        private void ApplyInventoryButtonLayout()
        {
            if (_inventoryButtonRect == null)
                return;

            _inventoryButtonRect.anchorMin = new Vector2(0f, 0.5f);
            _inventoryButtonRect.anchorMax = new Vector2(0f, 0.5f);
            _inventoryButtonRect.pivot = new Vector2(0f, 0.5f);
            _inventoryButtonRect.anchoredPosition = new Vector2(
                PortraitLayoutLeft +
                PortraitLayoutSize +
                InventoryButtonGap,
                0f);
            _inventoryButtonRect.sizeDelta = new Vector2(
                InventoryButtonSize,
                58f);
        }

        private void HandleCloseClicked()
        {
            CloseRequested?.Invoke();
        }

        private void HandleMoveClicked()
        {
            MoveRequested?.Invoke();
        }

        private void EnsureMoveButton()
        {
            if (_moveButton != null)
            {
                _moveButtonImage = _moveButton.targetGraphic as Image;
                return;
            }

            if (_movementText == null)
            {
                return;
            }

            var textRect = _movementText.rectTransform;
            var originalParent = textRect.parent;
            var siblingIndex = textRect.GetSiblingIndex();
            var anchorMin = textRect.anchorMin;
            var anchorMax = textRect.anchorMax;
            var pivot = textRect.pivot;
            var anchoredPosition = textRect.anchoredPosition;
            var sizeDelta = textRect.sizeDelta;

            var buttonObject = new GameObject(
                "MoveModeButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var buttonRect =
                buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(originalParent, false);
            buttonRect.SetSiblingIndex(siblingIndex);
            buttonRect.anchorMin = anchorMin;
            buttonRect.anchorMax = anchorMax;
            buttonRect.pivot = pivot;
            buttonRect.anchoredPosition = anchoredPosition;
            buttonRect.sizeDelta = sizeDelta;

            _moveButtonImage = buttonObject.GetComponent<Image>();
            _moveButtonImage.color =
                new Color(0.08f, 0.20f, 0.25f, 0.96f);
            _moveButton = buttonObject.GetComponent<Button>();
            _moveButton.targetGraphic = _moveButtonImage;

            textRect.SetParent(buttonRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(-12f, -4f);
            _movementText.alignment = TextAnchor.MiddleCenter;
        }

        private void BindMoveButton()
        {
            if (_moveButton == null)
            {
                return;
            }

            _moveButton.onClick.RemoveListener(HandleMoveClicked);
            _moveButton.onClick.AddListener(HandleMoveClicked);
        }

        private void UnbindMoveButton()
        {
            if (_moveButton != null)
            {
                _moveButton.onClick.RemoveListener(HandleMoveClicked);
            }
        }

        private void RefreshMovementText()
        {
            if (_movementText == null)
            {
                return;
            }

            _movementText.text = _hasMovementBudget
                ? $"이동 {_remainingMovementMeters:0.0}m 남음"
                : "이동";
        }

        private void BindCloseButton()
        {
            if (_closeButton == null)
            {
                return;
            }

            _closeButton.onClick.RemoveListener(HandleCloseClicked);
            _closeButton.onClick.AddListener(HandleCloseClicked);
        }

        private void UnbindCloseButton()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(HandleCloseClicked);
            }
        }

        private void EnsureRollWidget()
        {
            if (_rollWidget != null || _panel == null)
            {
                ApplyActionRowContentPadding();
                return;
            }

            _rollWidget = PawnRollWidget.CreateRuntime(
                _panel,
                _movementText != null ? _movementText.font : null);
            ApplyActionRowContentPadding();
        }

        private void EnsureActionGroup()
        {
            if (_actionGroup != null || _panel == null)
                return;

            var root = new GameObject(
                "PawnActionButtonGroup",
                typeof(RectTransform));
            _actionGroup = root.GetComponent<RectTransform>();
            _actionGroup.SetParent(_panel, false);
            _actionGroup.anchorMin = new Vector2(1f, 0f);
            _actionGroup.anchorMax = new Vector2(1f, 0f);
            _actionGroup.pivot = new Vector2(1f, 0f);
            _actionGroup.anchoredPosition =
                new Vector2(-ActionRightInset, ActionBottomInset);
            _actionGroup.sizeDelta =
                new Vector2(
                    ActionButtonWidth * 3f +
                    ActionButtonSpacing * 2f,
                    ActionButtonHeight);
        }

        private void ApplyActionRowContentPadding()
        {
            ApplyStructuredPanelLayout();
        }

        private void BindRollWidget()
        {
            if (_rollWidget == null)
            {
                return;
            }

            UnbindRollWidget();
            _rollWidget.RollInputOpened += HandleRollInputOpened;
            _rollWidget.CheckRollRequested += HandleCheckRollRequested;
            _rollWidget.EffectRollRequested += HandleEffectRollRequested;
            _rollWidget.PresentationCompleted +=
                HandleRollPresentationCompleted;
        }

        private void UnbindRollWidget()
        {
            if (_rollWidget == null)
            {
                return;
            }

            _rollWidget.RollInputOpened -= HandleRollInputOpened;
            _rollWidget.CheckRollRequested -= HandleCheckRollRequested;
            _rollWidget.EffectRollRequested -= HandleEffectRollRequested;
            _rollWidget.PresentationCompleted -=
                HandleRollPresentationCompleted;
        }

        private void HandleRollInputOpened()
        {
            RollInputOpened?.Invoke();
        }

        private void HandleCheckRollRequested(
            PawnCheckRollRequest request)
        {
            CheckRollRequested?.Invoke(request);
        }

        private void HandleEffectRollRequested(
            PawnEffectRollRequest request)
        {
            EffectRollRequested?.Invoke(request);
        }

        private void HandleRollPresentationCompleted()
        {
            RollPresentationCompleted?.Invoke();
        }

        private void EnsureStatPanel()
        {
            if (_statPanel != null || _canvasRect == null)
                return;

            _statPanel = PawnStatPanelWidget.CreateRuntime(
                _canvasRect,
                _movementText != null ? _movementText.font : null);
        }

        private void BindStatPanel()
        {
            if (_statPanel == null)
                return;

            _statPanel.ValueEditRequested -=
                HandlePlayerStatValueEditRequested;
            _statPanel.ValueEditRequested +=
                HandlePlayerStatValueEditRequested;
            _statPanel.SkillAddRequested -=
                HandlePlayerSkillAddRequested;
            _statPanel.SkillAddRequested +=
                HandlePlayerSkillAddRequested;
            _statPanel.SkillNameEditRequested -=
                HandlePlayerSkillNameEditRequested;
            _statPanel.SkillNameEditRequested +=
                HandlePlayerSkillNameEditRequested;
            _statPanel.SkillRegularEditRequested -=
                HandlePlayerSkillRegularEditRequested;
            _statPanel.SkillRegularEditRequested +=
                HandlePlayerSkillRegularEditRequested;
            _statPanel.SkillRemoveRequested -=
                HandlePlayerSkillRemoveRequested;
            _statPanel.SkillRemoveRequested +=
                HandlePlayerSkillRemoveRequested;
            _statPanel.SummaryClicked -=
                HandlePlayerHudRequested;
            _statPanel.SummaryClicked +=
                HandlePlayerHudRequested;
            _statPanel.ExpandedChanged -=
                HandleStatPanelExpandedChanged;
            _statPanel.ExpandedChanged +=
                HandleStatPanelExpandedChanged;
            _statPanel.QuickCheckRequested -=
                HandleQuickCheckRequested;
            _statPanel.QuickCheckRequested +=
                HandleQuickCheckRequested;
        }

        private void UnbindStatPanel()
        {
            if (_statPanel != null)
            {
                _statPanel.ValueEditRequested -=
                    HandlePlayerStatValueEditRequested;
                _statPanel.SkillAddRequested -=
                    HandlePlayerSkillAddRequested;
                _statPanel.SkillNameEditRequested -=
                    HandlePlayerSkillNameEditRequested;
                _statPanel.SkillRegularEditRequested -=
                    HandlePlayerSkillRegularEditRequested;
                _statPanel.SkillRemoveRequested -=
                    HandlePlayerSkillRemoveRequested;
                _statPanel.SummaryClicked -=
                    HandlePlayerHudRequested;
                _statPanel.ExpandedChanged -=
                    HandleStatPanelExpandedChanged;
                _statPanel.QuickCheckRequested -=
                    HandleQuickCheckRequested;
            }
        }

        private void HandlePlayerStatValueEditRequested(
            string statId,
            double value)
        {
            PlayerStatValueEditRequested?.Invoke(statId, value);
            StatValueEditRequested?.Invoke(statId, value);
        }

        private void HandlePlayerSkillAddRequested(
            PawnSkillAddRequest request)
        {
            PlayerSkillAddRequested?.Invoke(request);
        }

        private void HandlePlayerSkillNameEditRequested(
            PawnSkillNameEditRequest request)
        {
            PlayerSkillNameEditRequested?.Invoke(request);
        }

        private void HandlePlayerSkillRegularEditRequested(
            PawnSkillRegularEditRequest request)
        {
            PlayerSkillRegularEditRequested?.Invoke(request);
        }

        private void HandlePlayerSkillRemoveRequested(
            PawnSkillRemoveRequest request)
        {
            PlayerSkillRemoveRequested?.Invoke(request);
        }

        private void HandlePlayerHudRequested()
        {
            PlayerHudRequested?.Invoke();
        }

        private void HandleQuickCheckRequested()
        {
            _statPanel?.SetExpanded(false);
            OpenCheckRollInput();
        }

        private void OpenCheckRollInput()
        {
            EnsureRollWidget();
            if (_rollWidget == null)
            {
                Debug.LogWarning(
                    $"[{name}] 판정 굴림 UI를 찾지 못했습니다.",
                    this);
                return;
            }

            CacheRollActionButtons();
            var checkButton = _checkRollButtonRect != null
                ? _checkRollButtonRect.GetComponent<Button>()
                : null;
            if (checkButton == null)
            {
                Debug.LogWarning(
                    $"[{name}] 판정 굴림 버튼을 찾지 못했습니다.",
                    this);
                return;
            }

            if (!checkButton.IsActive() || !checkButton.interactable)
            {
                return;
            }

            checkButton.onClick.Invoke();
        }

        private void EnsureResourceBar()
        {
            if (_resourceBar != null || _panel == null)
                return;

            _resourceBar = PawnResourceBarWidget.CreateRuntime(
                _panel,
                _movementText != null ? _movementText.font : null);
            RefreshResourceBarLayout();
        }

        private void RefreshResourceBarLayout()
        {
            if (_resourceBar == null || _panel == null)
                return;

            _resourceBar.SetLayoutArea(
                ResourceLayoutLeft,
                ResourceLayoutBottom,
                ResourceLayoutWidth,
                ResourceLayoutHeight);
        }

        private void ApplyCompactPanelLayout()
        {
            if (_isApplyingCompactLayout ||
                _panel == null ||
                _canvasRect == null)
            {
                return;
            }

            _isApplyingCompactLayout = true;
            var availableWidth = Mathf.Max(
                360f,
                _canvasRect.rect.width -
                CompactPanelSideMargin * 2f);
            var targetWidth = Mathf.Clamp(
                availableWidth,
                CompactPanelMinimumWidth,
                CompactPanelMaximumWidth);
            var verticalPosition = _hasCompactLayout
                ? _shownPosition.y
                : _panel.anchoredPosition.y;
            _panel.anchorMin = new Vector2(0.5f, 0f);
            _panel.anchorMax = new Vector2(0.5f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.sizeDelta = new Vector2(
                targetWidth,
                Mathf.Max(
                    CompactPanelMinimumHeight,
                    _panel.sizeDelta.y));
            _panel.anchoredPosition = new Vector2(
                0f,
                verticalPosition);
            _shownPosition = _panel.anchoredPosition;
            _hiddenPosition = _shownPosition +
                Vector2.down *
                (_panel.rect.height +
                 Mathf.Max(
                     CompactPanelSideMargin,
                     _hiddenPadding));
            if (_hasCompactLayout)
            {
                _panel.anchoredPosition =
                    _isVisible
                        ? _shownPosition
                        : _hiddenPosition;
            }
            else
            {
                _hasCompactLayout = true;
            }
            ApplyStructuredPanelLayout();
            _isApplyingCompactLayout = false;
        }

        private void ApplyStructuredPanelLayout()
        {
            if (_panel == null)
                return;

            if (_portraitImage != null)
            {
                var portraitRect = _portraitImage.rectTransform;
                portraitRect.anchorMin = new Vector2(0f, 0.5f);
                portraitRect.anchorMax = new Vector2(0f, 0.5f);
                portraitRect.pivot = new Vector2(0f, 0.5f);
                portraitRect.anchoredPosition =
                    new Vector2(PortraitLayoutLeft, 0f);
                portraitRect.sizeDelta =
                    Vector2.one * PortraitLayoutSize;
            }

            EnsurePortraitButton();
            EnsureInventoryButton();
            ApplyInventoryButtonLayout();

            if (_nameText != null)
            {
                var nameRect = _nameText.rectTransform;
                nameRect.anchorMin = Vector2.zero;
                nameRect.anchorMax = Vector2.zero;
                nameRect.pivot = Vector2.zero;
                nameRect.anchoredPosition =
                    new Vector2(ResourceLayoutLeft, 162f);
                nameRect.sizeDelta =
                    new Vector2(ResourceLayoutWidth, 26f);
                _nameText.alignment = TextAnchor.MiddleLeft;
            }

            if (_descriptionText != null)
            {
                var descriptionRect = _descriptionText.rectTransform;
                descriptionRect.anchorMin = Vector2.zero;
                descriptionRect.anchorMax = Vector2.zero;
                descriptionRect.pivot = Vector2.zero;
                descriptionRect.anchoredPosition =
                    new Vector2(ResourceLayoutLeft, 18f);
                descriptionRect.sizeDelta =
                    new Vector2(ResourceLayoutWidth, 138f);
            }

            RefreshResourceBarLayout();
            ApplyActionButtonLayout();
        }

        private void ApplyActionButtonLayout()
        {
            EnsureMoveButton();
            EnsureActionGroup();
            CacheRollActionButtons();
            if (_actionGroup == null)
                return;

            var minimumStartX =
                ResourceLayoutLeft +
                ResourceLayoutWidth +
                24f;
            var availableActionWidth = Mathf.Max(
                236f,
                _panel.rect.width -
                ActionRightInset -
                minimumStartX);
            var buttonWidth = Mathf.Min(
                ActionButtonWidth,
                (availableActionWidth -
                 ActionButtonSpacing * 2f) / 3f);
            var totalWidth =
                buttonWidth * 3f +
                ActionButtonSpacing * 2f;
            _actionGroup.anchorMin = new Vector2(1f, 0f);
            _actionGroup.anchorMax = new Vector2(1f, 0f);
            _actionGroup.pivot = new Vector2(1f, 0f);
            _actionGroup.anchoredPosition =
                new Vector2(-ActionRightInset, ActionBottomInset);
            _actionGroup.sizeDelta =
                new Vector2(totalWidth, ActionButtonHeight);

            SetActionButtonRect(
                _moveButton != null
                    ? _moveButton.GetComponent<RectTransform>()
                    : null,
                _actionGroup,
                Vector2.zero,
                buttonWidth);

            var rollRect = _rollWidget != null
                ? _rollWidget.transform as RectTransform
                : null;
            if (rollRect == null)
                return;

            rollRect.SetParent(_actionGroup, false);
            rollRect.anchorMin = Vector2.zero;
            rollRect.anchorMax = Vector2.zero;
            rollRect.pivot = Vector2.zero;
            rollRect.anchoredPosition = new Vector2(
                buttonWidth +
                ActionButtonSpacing,
                0f);
            rollRect.sizeDelta = new Vector2(
                buttonWidth * 2f +
                ActionButtonSpacing,
                ActionButtonHeight);

            SetActionButtonRect(
                _checkRollButtonRect,
                rollRect,
                Vector2.zero,
                buttonWidth);
            SetActionButtonRect(
                _effectRollButtonRect,
                rollRect,
                new Vector2(
                    buttonWidth +
                    ActionButtonSpacing,
                    0f),
                buttonWidth);
        }

        private void CacheRollActionButtons()
        {
            if (_rollWidget == null ||
                (_checkRollButtonRect != null &&
                 _effectRollButtonRect != null))
            {
                return;
            }

            var buttons =
                _rollWidget.GetComponentsInChildren<Button>(true);
            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                if (button == null)
                    continue;

                if (button.name == "CheckRollButton")
                {
                    _checkRollButtonRect =
                        button.transform as RectTransform;
                }
                else if (button.name == "EffectRollButton")
                {
                    _effectRollButtonRect =
                        button.transform as RectTransform;
                }
            }
        }

        private static void SetActionButtonRect(
            RectTransform rect,
            RectTransform parent,
            Vector2 position,
            float width)
        {
            if (rect == null || parent == null)
                return;

            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(
                Mathf.Max(72f, width),
                ActionButtonHeight);
        }

        private void BindResourceBar()
        {
            if (_resourceBar == null)
                return;

            _resourceBar.ValueEditRequested -=
                HandleResourceStatValueEditRequested;
            _resourceBar.ValueEditRequested +=
                HandleResourceStatValueEditRequested;
        }

        private void UnbindResourceBar()
        {
            if (_resourceBar != null)
            {
                _resourceBar.ValueEditRequested -=
                    HandleResourceStatValueEditRequested;
            }
        }

        private void HandleResourceStatValueEditRequested(
            string statId,
            double value)
        {
            ResourceStatValueEditRequested?.Invoke(statId, value);
            StatValueEditRequested?.Invoke(statId, value);
        }

        private void EnsureStatToggleButton()
        {
            if (_statToggleButton != null || _panel == null)
                return;

            var buttonObject = new GameObject(
                "StatToggleButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(PawnCircleGraphic),
                typeof(Button));
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(_panel, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-52f, -10f);
            rect.sizeDelta = new Vector2(38f, 38f);

            _statToggleGraphic =
                buttonObject.GetComponent<PawnCircleGraphic>();
            _statToggleButton = buttonObject.GetComponent<Button>();
            _statToggleButton.targetGraphic = _statToggleGraphic;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var labelRect =
                labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.GetComponent<Text>();
            label.font =
                _movementText != null ? _movementText.font : null;
            label.fontSize = 13;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = "스탯";
            label.raycastTarget = false;

            buttonObject.SetActive(false);
            RefreshStatToggleVisual();
        }

        private void BindStatToggleButton()
        {
            if (_statToggleButton == null)
                return;

            _statToggleButton.onClick.RemoveListener(
                HandleStatToggleClicked);
            _statToggleButton.onClick.AddListener(
                HandleStatToggleClicked);
        }

        private void UnbindStatToggleButton()
        {
            if (_statToggleButton != null)
            {
                _statToggleButton.onClick.RemoveListener(
                    HandleStatToggleClicked);
            }
        }

        private void HandleStatToggleClicked()
        {
            if (_boardStackMode)
            {
                BoardStackStatsRequested?.Invoke();
                return;
            }

            _statPanel?.ToggleExpanded();
        }

        private void HandleStatPanelExpandedChanged(bool expanded)
        {
            RefreshStatToggleVisual();
        }

        private void RefreshStatToggleVisual()
        {
            if (_statToggleGraphic == null)
                return;

            _statToggleGraphic.color =
                !_boardStackMode &&
                _statPanel != null &&
                _statPanel.IsExpanded
                    ? new Color(0.08f, 0.48f, 0.60f, 0.99f)
                    : new Color(0.07f, 0.20f, 0.24f, 0.98f);
        }

        private void SetInteraction(bool enabled)
        {
            _canvasGroup.interactable = enabled;
            _canvasGroup.blocksRaycasts = enabled;
        }

        private void KillTransition()
        {
            _transition?.Kill();
            _transition = null;
        }

        private void ClearBoardOverlay()
        {
            if (_boardOverlay != null)
            {
                _boardOverlay.ClearAll();
                return;
            }

            // Unity에서 파괴된 Object 참조가 C# 필드에 남아 있을 수 있다.
            _boardOverlay = null;
        }

        private void EnsureBoardOverlay()
        {
            if (_boardOverlay != null &&
                _boardOverlay.GetComponent<Canvas>() == null)
            {
                return;
            }

            _boardOverlay = null;
            var parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                return;
            }

            _ownedBoardOverlayCanvas = new GameObject(
                "PawnBoardOverlayCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            var overlayCanvas =
                _ownedBoardOverlayCanvas.GetComponent<Canvas>();
            var rootCanvas = parentCanvas.rootCanvas;
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingLayerID = rootCanvas.sortingLayerID;
            overlayCanvas.sortingOrder = rootCanvas.sortingOrder - 1;

            CopyCanvasScaler(
                rootCanvas.GetComponent<CanvasScaler>(),
                _ownedBoardOverlayCanvas.GetComponent<CanvasScaler>());

            var overlayRect =
                _ownedBoardOverlayCanvas.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var overlayGraphicObject = new GameObject(
                "PawnBoardOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(PawnBoardOverlayGraphic));
            var graphicRect =
                overlayGraphicObject.GetComponent<RectTransform>();
            graphicRect.SetParent(overlayRect, false);
            graphicRect.anchorMin = Vector2.zero;
            graphicRect.anchorMax = Vector2.one;
            graphicRect.pivot = new Vector2(0.5f, 0.5f);
            graphicRect.anchoredPosition = Vector2.zero;
            graphicRect.sizeDelta = Vector2.zero;
            graphicRect.localScale = Vector3.one;

            _boardOverlay =
                overlayGraphicObject
                    .GetComponent<PawnBoardOverlayGraphic>();
            _boardOverlay.Configure(
                new Color(0.2f, 0.7f, 1f, 0.28f),
                new Color(0.2f, 0.9f, 1f, 1f),
                new Color(1f, 0.2f, 0.15f, 1f),
                8f);
        }

        private static void CopyCanvasScaler(
            CanvasScaler source,
            CanvasScaler destination)
        {
            if (destination == null)
            {
                return;
            }

            if (source == null)
            {
                destination.uiScaleMode =
                    CanvasScaler.ScaleMode.ConstantPixelSize;
                return;
            }

            destination.uiScaleMode = source.uiScaleMode;
            destination.referencePixelsPerUnit =
                source.referencePixelsPerUnit;
            destination.scaleFactor = source.scaleFactor;
            destination.referenceResolution =
                source.referenceResolution;
            destination.screenMatchMode = source.screenMatchMode;
            destination.matchWidthOrHeight =
                source.matchWidthOrHeight;
            destination.physicalUnit = source.physicalUnit;
            destination.fallbackScreenDPI =
                source.fallbackScreenDPI;
            destination.defaultSpriteDPI =
                source.defaultSpriteDPI;
            destination.dynamicPixelsPerUnit =
                source.dynamicPixelsPerUnit;
        }

    }
}
