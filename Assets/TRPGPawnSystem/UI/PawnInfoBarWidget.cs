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
        private bool _contentOffsetsCaptured;
        private Vector2 _baseNameOffsetMax;
        private Vector2 _baseDescriptionOffsetMax;

        public event Action CloseRequested;
        public event Action MoveRequested;
        public event Action RollInputOpened;
        public event Action<PawnCheckRollRequest> CheckRollRequested;
        public event Action<PawnEffectRollRequest> EffectRollRequested;
        public event Action RollPresentationCompleted;

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
            _movementScore = data.MovementScore;
            _hasMovementBudget = false;
            EnsureMoveButton();
            SetMovementModeState(true, false);
            EnsureRollWidget();
            _rollWidget?.SetButtonsEnabled(true);
            Show();
        }
        public void Unbind()
        {
            Hide();
            _nameText.text = string.Empty;
            _descriptionText.text = string.Empty;
            _hasMovementBudget = false;
            SetMovementModeState(false, false);
            _rollWidget?.CancelPresentation();
            _rollWidget?.SetButtonsEnabled(false);
            SetPathPreview(PawnPathPreviewData.Hidden);
            SetMovementRange(PawnMovementRangeData.Hidden);
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
            _shownPosition = panel.anchoredPosition;
            _hiddenPosition = _shownPosition +
                Vector2.down * (panel.rect.height + hiddenPadding);
            _panel.anchoredPosition = _hiddenPosition;
            _canvasGroup.alpha = 0f;
            SetInteraction(false);
            _boardOverlay?.ClearAll();
            EnsureMoveButton();
            BindMoveButton();
            BindCloseButton();
            EnsureRollWidget();
            BindRollWidget();
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
            BindCloseButton();
            EnsureRollWidget();
            BindRollWidget();
            _rollWidget?.RefreshResponsiveLayout();
            ApplyActionRowContentPadding();
        }

        private void OnRectTransformDimensionsChange()
        {
            _rollWidget?.RefreshResponsiveLayout();
            ApplyActionRowContentPadding();
        }

        private void OnDisable()
        {
            UnbindMoveButton();
            UnbindCloseButton();
            UnbindRollWidget();
            KillTransition();
            _rollWidget?.CancelPresentation();
            _boardOverlay?.ClearAll();
        }

        private void OnDestroy()
        {
            UnbindMoveButton();
            UnbindCloseButton();
            UnbindRollWidget();
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

            var modeLabel = _isMovementModeActive
                ? "걷기 중"
                : "걷기";
            _movementText.text = _hasMovementBudget
                ? $"{modeLabel} {_movementScore}\n" +
                  $"{_remainingMovementMeters:0.0}/" +
                  $"{_maximumMovementMeters:0.0}m"
                : $"{modeLabel}\n{_movementScore}";
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
                _rollWidget?.ConfigureResponsiveLayout(
                    _moveButton,
                    _canvasRect,
                    _panel);
                ApplyActionRowContentPadding();
                return;
            }

            _rollWidget = PawnRollWidget.CreateRuntime(
                _panel,
                _movementText != null ? _movementText.font : null);
            _rollWidget.ConfigureResponsiveLayout(
                _moveButton,
                _canvasRect,
                _panel);
            ApplyActionRowContentPadding();
        }

        private void ApplyActionRowContentPadding()
        {
            if (_rollWidget == null ||
                _nameText == null ||
                _descriptionText == null)
            {
                return;
            }

            if (!_contentOffsetsCaptured)
            {
                _baseNameOffsetMax =
                    _nameText.rectTransform.offsetMax;
                _baseDescriptionOffsetMax =
                    _descriptionText.rectTransform.offsetMax;
                _contentOffsetsCaptured = true;
            }

            var reservedRight = Mathf.Max(
                _rollWidget.ReservedRightWidth,
                Mathf.Abs(_baseNameOffsetMax.x));
            var nameOffset =
                _baseNameOffsetMax;
            nameOffset.x = -reservedRight;
            _nameText.rectTransform.offsetMax = nameOffset;

            var descriptionOffset =
                _baseDescriptionOffsetMax;
            descriptionOffset.x = -reservedRight;
            _descriptionText.rectTransform.offsetMax =
                descriptionOffset;
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
