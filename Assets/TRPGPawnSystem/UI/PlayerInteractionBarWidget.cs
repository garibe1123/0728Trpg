using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    [Serializable]
    public struct PlayerInteractionBarStyle
    {
        [SerializeField] private Vector2 _referenceResolution;
        [SerializeField] private Vector2 _rightTopOffset;
        [SerializeField] private float _barWidth;
        [SerializeField] private float _slotHeight;
        [SerializeField] private float _slotSpacing;
        [SerializeField] private float _panelPadding;
        [SerializeField] private float _titleHeight;
        [SerializeField] private float _innerPadding;
        [SerializeField] private float _portraitSize;
        [SerializeField] private int _titleFontSize;
        [SerializeField] private int _nameFontSize;
        [SerializeField] private int _orderFontSize;
        [SerializeField] private float _hoverOffset;
        [SerializeField] private float _selectedOffset;
        [SerializeField] private float _motionDuration;
        [SerializeField] private float _fillDuration;
        [SerializeField] private int _sortingOrder;
        [SerializeField] private Color _panelColor;
        [SerializeField] private Color _slotColor;
        [SerializeField] private Color _hoverColor;
        [SerializeField] private Color _interactionColor;
        [SerializeField] private Color _interactionAccentColor;
        [SerializeField] private Color _textColor;
        [SerializeField] private Color _mutedTextColor;

        public Vector2 ReferenceResolution =>
            _referenceResolution.x > 0f && _referenceResolution.y > 0f
                ? _referenceResolution
                : new Vector2(1920f, 1080f);
        public Vector2 RightTopOffset => _rightTopOffset;
        public float BarWidth => Mathf.Max(220f, _barWidth);
        public float SlotHeight => Mathf.Max(56f, _slotHeight);
        public float SlotSpacing => Mathf.Max(0f, _slotSpacing);
        public float PanelPadding => Mathf.Max(0f, _panelPadding);
        public float TitleHeight => Mathf.Max(24f, _titleHeight);
        public float InnerPadding => Mathf.Max(8f, _innerPadding);
        public float PortraitSize => Mathf.Clamp(
            _portraitSize,
            32f,
            SlotHeight - 12f);
        public int TitleFontSize => Mathf.Clamp(_titleFontSize, 12, 36);
        public int NameFontSize => Mathf.Clamp(_nameFontSize, 12, 40);
        public int OrderFontSize => Mathf.Clamp(_orderFontSize, 10, 30);
        public float HoverOffset => Mathf.Max(0f, _hoverOffset);
        public float SelectedOffset => Mathf.Max(0f, _selectedOffset);
        public float MotionDuration => Mathf.Max(0.01f, _motionDuration);
        public float FillDuration => Mathf.Max(0.01f, _fillDuration);
        public int SortingOrder => _sortingOrder;
        public Color PanelColor => _panelColor;
        public Color SlotColor => _slotColor;
        public Color HoverColor => _hoverColor;
        public Color InteractionColor => _interactionColor;
        public Color InteractionAccentColor =>
            _interactionAccentColor;
        public Color TextColor => _textColor;
        public Color MutedTextColor => _mutedTextColor;

        public static PlayerInteractionBarStyle Default
        {
            get
            {
                return new PlayerInteractionBarStyle
                {
                    _referenceResolution = new Vector2(1920f, 1080f),
                    _rightTopOffset = new Vector2(-24f, -140f),
                    _barWidth = 320f,
                    _slotHeight = 72f,
                    _slotSpacing = 8f,
                    _panelPadding = 12f,
                    _titleHeight = 34f,
                    _innerPadding = 12f,
                    _portraitSize = 52f,
                    _titleFontSize = 18,
                    _nameFontSize = 22,
                    _orderFontSize = 14,
                    _hoverOffset = 22f,
                    _selectedOffset = 8f,
                    _motionDuration = 0.16f,
                    _fillDuration = 0.24f,
                    _sortingOrder = 5050,
                    _panelColor =
                        new Color(0.035f, 0.045f, 0.065f, 0.90f),
                    _slotColor =
                        new Color(0.075f, 0.095f, 0.13f, 0.96f),
                    _hoverColor =
                        new Color(0.11f, 0.15f, 0.20f, 1f),
                    _interactionColor =
                        new Color(0.10f, 0.54f, 0.75f, 0.72f),
                    _interactionAccentColor =
                        new Color(0.32f, 0.84f, 1f, 1f),
                    _textColor = Color.white,
                    _mutedTextColor =
                        new Color(0.68f, 0.76f, 0.84f, 1f)
                };
            }
        }
    }

    public readonly struct PlayerInteractionSlotData
    {
        public PlayerInteractionSlotData(
            InteractivePawn pawn,
            int displayOrder,
            string displayName,
            Sprite portrait,
            bool isInteractionTarget)
        {
            Pawn = pawn;
            DisplayOrder = displayOrder;
            DisplayName = displayName;
            Portrait = portrait;
            IsInteractionTarget = isInteractionTarget;
        }

        public InteractivePawn Pawn { get; }
        public int DisplayOrder { get; }
        public string DisplayName { get; }
        public Sprite Portrait { get; }
        public bool IsInteractionTarget { get; }
    }

    public sealed class PlayerInteractionBarWidget : MonoBehaviour
    {
        [SerializeField] private RectTransform _panelRect;
        [SerializeField] private Image _panelImage;
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private VerticalLayoutGroup _contentLayout;
        [SerializeField] private Text _titleText;

        private readonly List<PlayerInteractionSlotWidget> _slots =
            new List<PlayerInteractionSlotWidget>();

        private PlayerInteractionBarStyle _style;
        private Font _runtimeFont;

        public event Action<InteractivePawn> PlayerClicked;

        public static PlayerInteractionBarWidget CreateRuntime(
            in PlayerInteractionBarStyle style,
            out GameObject canvasRoot)
        {
            EnsureEventSystem();

            canvasRoot = new GameObject(
                "PlayerInteractionBarCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = style.SortingOrder;

            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = style.ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            var panelObject = new GameObject(
                "PlayerInteractionBar",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(PlayerInteractionBarWidget));
            panelObject.transform.SetParent(canvasRoot.transform, false);

            var widget =
                panelObject.GetComponent<PlayerInteractionBarWidget>();
            widget._panelRect =
                panelObject.GetComponent<RectTransform>();
            widget._panelRect.anchorMin = new Vector2(1f, 1f);
            widget._panelRect.anchorMax = new Vector2(1f, 1f);
            widget._panelRect.pivot = new Vector2(1f, 1f);
            widget._panelRect.anchoredPosition = style.RightTopOffset;

            widget._panelImage = panelObject.GetComponent<Image>();
            widget._panelImage.color = style.PanelColor;
            widget._panelImage.raycastTarget = false;

            widget._runtimeFont = GetRuntimeFont();

            var titleObject = new GameObject(
                "Title",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            titleObject.transform.SetParent(widget._panelRect, false);
            widget._titleText = titleObject.GetComponent<Text>();
            widget._titleText.font = widget._runtimeFont;
            widget._titleText.fontSize = style.TitleFontSize;
            widget._titleText.fontStyle = FontStyle.Bold;
            widget._titleText.alignment = TextAnchor.MiddleLeft;
            widget._titleText.color = style.MutedTextColor;
            widget._titleText.raycastTarget = false;

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup));
            contentObject.transform.SetParent(widget._panelRect, false);
            widget._contentRoot =
                contentObject.GetComponent<RectTransform>();
            widget._contentLayout =
                contentObject.GetComponent<VerticalLayoutGroup>();
            widget._contentLayout.childAlignment =
                TextAnchor.UpperRight;
            widget._contentLayout.childControlWidth = true;
            widget._contentLayout.childControlHeight = false;
            widget._contentLayout.childForceExpandWidth = true;
            widget._contentLayout.childForceExpandHeight = false;

            widget.ApplyStyle(style);
            return widget;
        }

        public void Bind(
            IReadOnlyList<PlayerInteractionSlotData> players,
            in PlayerInteractionBarStyle style)
        {
            ReleaseSlotBindings();
            ApplyStyle(style);

            var count = players != null ? players.Count : 0;
            EnsureSlotCount(count);

            for (var index = 0; index < _slots.Count; index++)
            {
                var active = index < count;
                _slots[index].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                _slots[index].Bind(players[index], style);
                _slots[index].Clicked += HandleSlotClicked;
            }

            _titleText.text = $"PLAYERS  {count}";
            ApplyLayout(count);
        }

        public void Unbind()
        {
            ReleaseSlotBindings();
            PlayerClicked = null;
        }

        public void SetInteractionTarget(
            InteractivePawn selectedPlayer,
            bool animate)
        {
            for (var index = 0; index < _slots.Count; index++)
            {
                if (!_slots[index].gameObject.activeSelf)
                {
                    continue;
                }

                _slots[index].SetInteractionState(
                    _slots[index].BoundPawn == selectedPlayer,
                    animate);
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void ApplyStyle(in PlayerInteractionBarStyle style)
        {
            _style = style;
            if (_runtimeFont == null)
            {
                _runtimeFont = GetRuntimeFont();
            }

            if (_panelImage != null)
            {
                _panelImage.color = style.PanelColor;
            }

            if (_panelRect != null)
            {
                _panelRect.anchoredPosition = style.RightTopOffset;
            }

            if (_titleText != null)
            {
                _titleText.font = _runtimeFont;
                _titleText.fontSize = style.TitleFontSize;
                _titleText.color = style.MutedTextColor;
            }

            if (_contentLayout != null)
            {
                _contentLayout.spacing = style.SlotSpacing;
            }
        }

        private void ApplyLayout(int playerCount)
        {
            var visibleSlotHeight = playerCount > 0
                ? playerCount * _style.SlotHeight +
                  Mathf.Max(0, playerCount - 1) * _style.SlotSpacing
                : 0f;
            var panelHeight =
                _style.PanelPadding * 2f +
                _style.TitleHeight +
                visibleSlotHeight;

            _panelRect.sizeDelta =
                new Vector2(_style.BarWidth, panelHeight);

            var titleRect =
                _titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition =
                new Vector2(0f, -_style.PanelPadding);
            titleRect.sizeDelta =
                new Vector2(
                    -_style.PanelPadding * 2f,
                    _style.TitleHeight);

            _contentRoot.anchorMin = new Vector2(0f, 1f);
            _contentRoot.anchorMax = new Vector2(1f, 1f);
            _contentRoot.pivot = new Vector2(0.5f, 1f);
            _contentRoot.anchoredPosition = new Vector2(
                0f,
                -_style.PanelPadding - _style.TitleHeight);
            _contentRoot.sizeDelta = new Vector2(
                -_style.PanelPadding * 2f,
                visibleSlotHeight);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);
        }

        private void EnsureSlotCount(int count)
        {
            while (_slots.Count < count)
            {
                _slots.Add(
                    PlayerInteractionSlotWidget.CreateRuntime(
                        _contentRoot,
                        _runtimeFont,
                        _style));
            }
        }

        private void ReleaseSlotBindings()
        {
            for (var index = 0; index < _slots.Count; index++)
            {
                _slots[index].Clicked -= HandleSlotClicked;
                _slots[index].Unbind();
                _slots[index].gameObject.SetActive(false);
            }
        }

        private void HandleSlotClicked(InteractivePawn pawn)
        {
            PlayerClicked?.Invoke(pawn);
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
    }

    public sealed class PlayerInteractionSlotWidget :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        [SerializeField] private RectTransform _motionRoot;
        [SerializeField] private LayoutElement _layoutElement;
        [SerializeField] private Image _hitAreaImage;
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Image _interactionFillImage;
        [SerializeField] private Image _portraitImage;
        [SerializeField] private Text _orderText;
        [SerializeField] private Text _nameText;
        [SerializeField] private Button _button;

        private PlayerInteractionBarStyle _style;
        private InteractivePawn _boundPawn;
        private Tween _motionTween;
        private Tween _backgroundTween;
        private Tween _fillTween;
        private bool _isHovered;
        private bool _isInteractionTarget;

        public event Action<InteractivePawn> Clicked;

        public InteractivePawn BoundPawn => _boundPawn;

        public static PlayerInteractionSlotWidget CreateRuntime(
            Transform parent,
            Font font,
            in PlayerInteractionBarStyle style)
        {
            var slotObject = new GameObject(
                "PlayerInteractionSlot",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement),
                typeof(PlayerInteractionSlotWidget));
            slotObject.transform.SetParent(parent, false);

            var widget =
                slotObject.GetComponent<PlayerInteractionSlotWidget>();
            widget._layoutElement =
                slotObject.GetComponent<LayoutElement>();
            widget._hitAreaImage = slotObject.GetComponent<Image>();
            widget._hitAreaImage.color = Color.clear;
            widget._hitAreaImage.raycastTarget = true;
            widget._button = slotObject.GetComponent<Button>();
            widget._button.targetGraphic = widget._hitAreaImage;

            var motionObject = new GameObject(
                "MotionRoot",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            motionObject.transform.SetParent(slotObject.transform, false);
            widget._motionRoot =
                motionObject.GetComponent<RectTransform>();
            widget._motionRoot.anchorMin = Vector2.zero;
            widget._motionRoot.anchorMax = Vector2.one;
            widget._motionRoot.offsetMin = Vector2.zero;
            widget._motionRoot.offsetMax = Vector2.zero;

            widget._backgroundImage = motionObject.GetComponent<Image>();
            widget._backgroundImage.color = style.SlotColor;
            widget._backgroundImage.raycastTarget = false;

            var fillObject = CreateImageObject(
                "InteractionFill",
                widget._motionRoot);
            widget._interactionFillImage =
                fillObject.GetComponent<Image>();
            widget._interactionFillImage.color =
                style.InteractionColor;
            widget._interactionFillImage.type = Image.Type.Filled;
            widget._interactionFillImage.fillMethod =
                Image.FillMethod.Horizontal;
            widget._interactionFillImage.fillOrigin = 0;
            widget._interactionFillImage.fillAmount = 0f;
            widget._interactionFillImage.raycastTarget = false;
            Stretch(fillObject.GetComponent<RectTransform>());

            var accentObject = CreateImageObject(
                "InteractionAccent",
                widget._motionRoot);
            var accentImage = accentObject.GetComponent<Image>();
            accentImage.color = style.InteractionAccentColor;
            accentImage.raycastTarget = false;
            var accentRect =
                accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(5f, 0f);

            var portraitObject = CreateImageObject(
                "Portrait",
                widget._motionRoot);
            widget._portraitImage =
                portraitObject.GetComponent<Image>();
            widget._portraitImage.preserveAspect = true;
            widget._portraitImage.raycastTarget = false;
            var portraitRect =
                portraitObject.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 0.5f);
            portraitRect.anchorMax = new Vector2(0f, 0.5f);
            portraitRect.pivot = new Vector2(0f, 0.5f);
            portraitRect.anchoredPosition =
                new Vector2(style.InnerPadding, 0f);
            portraitRect.sizeDelta =
                Vector2.one * style.PortraitSize;

            var orderObject = CreateTextObject(
                "Order",
                widget._motionRoot,
                font);
            widget._orderText = orderObject.GetComponent<Text>();
            widget._orderText.fontSize = style.OrderFontSize;
            widget._orderText.color = style.MutedTextColor;
            widget._orderText.alignment =
                TextAnchor.MiddleCenter;
            widget._orderText.raycastTarget = false;
            var orderRect =
                orderObject.GetComponent<RectTransform>();
            orderRect.anchorMin = new Vector2(0f, 0f);
            orderRect.anchorMax = new Vector2(0f, 1f);
            orderRect.pivot = new Vector2(0f, 0.5f);
            orderRect.anchoredPosition = new Vector2(
                style.InnerPadding + style.PortraitSize + 8f,
                0f);
            orderRect.sizeDelta = new Vector2(34f, 0f);

            var nameObject = CreateTextObject(
                "Name",
                widget._motionRoot,
                font);
            widget._nameText = nameObject.GetComponent<Text>();
            widget._nameText.fontSize = style.NameFontSize;
            widget._nameText.color = style.TextColor;
            widget._nameText.alignment = TextAnchor.MiddleLeft;
            widget._nameText.raycastTarget = false;
            widget._nameText.resizeTextForBestFit = true;
            widget._nameText.resizeTextMinSize = 14;
            widget._nameText.resizeTextMaxSize =
                style.NameFontSize;
            var nameRect =
                nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(
                style.InnerPadding +
                style.PortraitSize +
                48f,
                8f);
            nameRect.offsetMax = new Vector2(
                -style.InnerPadding,
                -8f);

            return widget;
        }

        public void Bind(
            in PlayerInteractionSlotData data,
            in PlayerInteractionBarStyle style)
        {
            KillTweens();
            _style = style;
            _boundPawn = data.Pawn;
            _isHovered = false;
            _isInteractionTarget = data.IsInteractionTarget;

            gameObject.name =
                $"PlayerInteractionSlot_{data.DisplayOrder:00}";
            gameObject.SetActive(true);

            _layoutElement.preferredHeight = style.SlotHeight;
            _layoutElement.minHeight = style.SlotHeight;
            _layoutElement.flexibleHeight = 0f;

            _orderText.text = data.DisplayOrder.ToString("00");
            _nameText.text =
                string.IsNullOrWhiteSpace(data.DisplayName)
                    ? "Player"
                    : data.DisplayName;
            _portraitImage.sprite = data.Portrait;
            _portraitImage.enabled = data.Portrait != null;
            _button.interactable = data.Pawn != null;

            _backgroundImage.color = style.SlotColor;
            _interactionFillImage.color =
                style.InteractionColor;
            _interactionFillImage.fillAmount =
                data.IsInteractionTarget ? 1f : 0f;
            _motionRoot.anchoredPosition = new Vector2(
                data.IsInteractionTarget
                    ? -style.SelectedOffset
                    : 0f,
                0f);

            _button.onClick.RemoveListener(HandleClicked);
            _button.onClick.AddListener(HandleClicked);
        }

        public void Unbind()
        {
            KillTweens();
            if (_button != null)
            {
                _button.onClick.RemoveListener(HandleClicked);
            }

            _boundPawn = null;
            _isHovered = false;
            _isInteractionTarget = false;
            Clicked = null;

            if (_motionRoot != null)
            {
                _motionRoot.anchoredPosition = Vector2.zero;
            }

            if (_interactionFillImage != null)
            {
                _interactionFillImage.fillAmount = 0f;
            }
        }

        public void SetInteractionState(bool active, bool animate)
        {
            _isInteractionTarget = active;

            if (!animate)
            {
                KillTweens();
                _interactionFillImage.fillAmount =
                    active ? 1f : 0f;
                _backgroundImage.color =
                    _isHovered
                        ? _style.HoverColor
                        : _style.SlotColor;
                ApplyMotionImmediate();
                return;
            }

            _fillTween?.Kill();
            _fillTween = _interactionFillImage
                .DOFillAmount(
                    active ? 1f : 0f,
                    _style.FillDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
            AnimateMotion();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            AnimateMotion();
            AnimateBackground(_style.HoverColor);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            AnimateMotion();
            AnimateBackground(_style.SlotColor);
        }

        private void OnDisable()
        {
            KillTweens();
        }

        private void HandleClicked()
        {
            if (_boundPawn != null)
            {
                Clicked?.Invoke(_boundPawn);
            }
        }

        private void AnimateMotion()
        {
            _motionTween?.Kill();
            _motionTween = _motionRoot
                .DOAnchorPosX(
                    GetTargetOffsetX(),
                    _style.MotionDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        private void AnimateBackground(Color targetColor)
        {
            _backgroundTween?.Kill();
            _backgroundTween = _backgroundImage
                .DOColor(
                    targetColor,
                    _style.MotionDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        private void ApplyMotionImmediate()
        {
            _motionRoot.anchoredPosition = new Vector2(
                GetTargetOffsetX(),
                0f);
        }

        private float GetTargetOffsetX()
        {
            if (_isHovered)
            {
                return -_style.HoverOffset;
            }

            return _isInteractionTarget
                ? -_style.SelectedOffset
                : 0f;
        }

        private void KillTweens()
        {
            _motionTween?.Kill();
            _backgroundTween?.Kill();
            _fillTween?.Kill();
            _motionTween = null;
            _backgroundTween = null;
            _fillTween = null;
        }

        private static GameObject CreateImageObject(
            string objectName,
            Transform parent)
        {
            var result = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static GameObject CreateTextObject(
            string objectName,
            Transform parent,
            Font font)
        {
            var result = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            result.transform.SetParent(parent, false);
            result.GetComponent<Text>().font = font;
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
