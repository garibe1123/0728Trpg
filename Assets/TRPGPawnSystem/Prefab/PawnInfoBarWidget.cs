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
        [SerializeField] private Button _closeButton;
        [SerializeField] private RectTransform _cursorBadge;
        [SerializeField] private Image _cursorBadgeImage;
        [SerializeField] private Text _cursorDistanceText;
        [SerializeField] private RectTransform _canvasRect;
        [SerializeField] private PawnBoardOverlayGraphic _boardOverlay;

        private GameObject _ownedBoardOverlayCanvas;
        private Vector2 _shownPosition;
        private Vector2 _hiddenPosition;
        private float _showDuration;
        private float _hideDuration;
        private Sequence _transition;
        private bool _isVisible;
        private int _movementScore;

        public event Action CloseRequested;

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
            Show();
        }
        public void Unbind()
        {
            Hide();
            _nameText.text = string.Empty;
            _descriptionText.text = string.Empty;
            SetPathPreview(PawnPathPreviewData.Hidden);
            SetMovementRange(PawnMovementRangeData.Hidden);
        }
        public void SetMovementBudget(
            float remainingMeters,
            float maximumMeters)
        {
            if (_movementText == null)
            {
                return;
            }

            _movementText.text =
                $"이동 {_movementScore}  " +
                $"{remainingMeters:0.0}/{maximumMeters:0.0}m";
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
            BindCloseButton();
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
            BindCloseButton();
        }

        private void OnDisable()
        {
            UnbindCloseButton();
            KillTransition();
            _boardOverlay?.ClearAll();
        }

        private void OnDestroy()
        {
            UnbindCloseButton();
            if (_ownedBoardOverlayCanvas != null)
            {
                Destroy(_ownedBoardOverlayCanvas);
                _ownedBoardOverlayCanvas = null;
                _boardOverlay = null;
            }

            CloseRequested = null;
        }

        private void HandleCloseClicked()
        {
            CloseRequested?.Invoke();
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
