using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 좌측 하단에서 슬라이드되는 굴림 로그 및 채팅 UI입니다.
    /// 데이터 보관과 발신자 결정은 Manager와 Service가 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnRollLogChatWidget : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        private const float DefaultPanelWidth = 548f;
        private const float MinimumWidthScale = 0.4f;
        private const float MaximumWidthScale = 1f;
        private const float ResizeHandleHalfWidth = 7f;
        private const float DefaultPanelHeight = 470f;
        private const float ToggleWidth = 52f;
        private const float LeftMargin = 18f;
        private const float BottomMargin = 24f;
        private const float SlideDuration = 0.28f;
        private const int MaximumVisibleEntries = 500;

        private readonly Queue<GameObject> _entryRows =
            new Queue<GameObject>();

        private RectTransform _rootRect;
        private RectTransform _panelViewportRect;
        private RectTransform _panelRect;
        private RectTransform _headerRect;
        private RectTransform _resizeHandleRect;
        private Text _widthPercentText;
        private RectTransform _viewportRect;
        private RectTransform _contentRect;
        private CanvasGroup _panelCanvasGroup;
        private ScrollRect _scrollRect;
        private Button _toggleButton;
        private Text _toggleLabel;
        private Button _closeButton;
        private Button _recentButton;
        private InputField _chatInput;
        private Button _sendButton;
        private Text _chatHint;
        private Font _font;
        private Coroutine _scrollRoutine;
        private bool _isOpen;
        private bool _chatAvailable;
        private string _speakerName = string.Empty;
        private bool _isRefreshingLayout;
        private bool _isDragging;
        private bool _isResizing;
        private Vector2 _dragPointerStart;
        private Vector2 _dragWindowStart;
        private Vector2 _resizePointerStart;
        private float _resizeWidthStart;
        private Vector2 _openPosition =
            new Vector2(LeftMargin, BottomMargin);
        private float _currentPanelWidth =
            DefaultPanelWidth * MinimumWidthScale;
        private float _maximumPanelWidth = DefaultPanelWidth;
        private float _widthScale = MinimumWidthScale;
        private float _currentPanelHeight = DefaultPanelHeight;

        public event Action<string> ChatSubmitted;

        public bool IsOpen => _isOpen;

        public static PawnRollLogChatWidget CreateRuntime(
            Font font,
            out GameObject ownedCanvas)
        {
            ownedCanvas = new GameObject(
                "PawnRollLogChatCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas = ownedCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2300;

            var scaler = ownedCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = new GameObject(
                "PawnRollLogChat",
                typeof(RectTransform));
            root.transform.SetParent(ownedCanvas.transform, false);

            var widget = root.AddComponent<PawnRollLogChatWidget>();
            widget.Build(font);
            widget.SetOpen(false, false);
            return widget;
        }

        private void OnEnable()
        {
            RefreshPanelInputState();
            RefreshResponsiveLayout(true);
        }

        private void OnDisable()
        {
            _isDragging = false;
            _isResizing = false;
            StopScrollRoutine();
            if (_rootRect != null)
                _rootRect.DOKill();
            if (_panelRect != null)
                _panelRect.DOKill();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled ||
                _rootRect == null ||
                _panelRect == null ||
                _isRefreshingLayout)
            {
                return;
            }

            RefreshResponsiveLayout(false);
        }

        public void SetEntries(IReadOnlyList<PawnRollLogEntry> entries)
        {
            ClearRows();
            if (entries != null)
            {
                var start = Mathf.Max(
                    0,
                    entries.Count - MaximumVisibleEntries);
                for (var index = start; index < entries.Count; index++)
                    CreateEntryRow(entries[index]);
            }

            ScrollToBottomDeferred();
        }

        public void Append(in PawnRollLogEntry entry)
        {
            var shouldFollow = IsAtBottom();
            CreateEntryRow(entry);
            TrimVisibleRows();

            if (shouldFollow)
                ScrollToBottomDeferred();
            else
                RefreshRecentButton();
        }

        public void ClearEntries()
        {
            ClearRows();
            RefreshRecentButton();
        }

        public void SetChatAvailability(
            bool available,
            string speakerName)
        {
            _chatAvailable = available;
            _speakerName = speakerName?.Trim() ?? string.Empty;
            RefreshChatControls();
        }

        public void SetOpen(bool open, bool animate = true)
        {
            _isOpen = open;
            RefreshPanelInputState();
            RefreshResponsiveLayout(!animate);
            RefreshChatControls();
            SetText(_toggleLabel, open ? "로그\n열림" : "로그\n채팅");

            if (!open && _chatInput != null)
                _chatInput.DeactivateInputField();

            RefreshRecentButton();
        }

        private void Build(Font font)
        {
            _font = ResolveFont(font);
            _rootRect = GetComponent<RectTransform>();
            _rootRect.anchorMin = Vector2.zero;
            _rootRect.anchorMax = Vector2.zero;
            _rootRect.pivot = Vector2.zero;

            _panelViewportRect = CreateRect(
                "PanelViewport",
                _rootRect,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero);
            _panelViewportRect.gameObject.AddComponent<RectMask2D>();

            _panelRect = CreatePanel(
                "Panel",
                _panelViewportRect,
                new Color(0.025f, 0.075f, 0.09f, 0.98f));
            _panelCanvasGroup =
                _panelRect.gameObject.AddComponent<CanvasGroup>();
            RefreshPanelInputState();

            BuildHeader();
            BuildLogScroll();
            BuildRecentButton();
            BuildChatInput();
            BuildResizeHandle();
            BuildToggle();
            RefreshResponsiveLayout(true);
        }

        private void BuildHeader()
        {
            _headerRect = CreatePanel(
                "Header",
                _panelRect,
                new Color(0.015f, 0.16f, 0.18f, 1f));
            SetRect(
                _headerRect,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0f, -48f),
                Vector2.zero);

            var title = CreateText(
                "Title",
                _headerRect,
                18,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetRect(
                title.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(14f, 8f),
                new Vector2(-52f, -8f));
            title.text = "굴림 로그 / 채팅";

            BuildCloseButton();
        }

        private void BuildCloseButton()
        {
            _closeButton = CreateButton(
                "CloseButton",
                _headerRect,
                "×",
                new Color(0.16f, 0.28f, 0.31f, 1f),
                out var closeLabel);

            var rect = _closeButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(34f, 32f);
            rect.anchoredPosition = new Vector2(-8f, 0f);

            if (closeLabel != null)
            {
                closeLabel.fontSize = 22;
                closeLabel.fontStyle = FontStyle.Bold;
            }

            _closeButton.onClick.AddListener(
                () => SetOpen(false));
        }

        private void BuildWidthSlider()
        {
            var guide = CreateText(
                "WidthGuide",
                _headerRect,
                12,
                FontStyle.Normal,
                TextAnchor.MiddleRight);
            var guideRect = guide.rectTransform;
            guideRect.anchorMin = new Vector2(1f, 0.5f);
            guideRect.anchorMax = new Vector2(1f, 0.5f);
            guideRect.pivot = new Vector2(1f, 0.5f);
            guideRect.sizeDelta = new Vector2(205f, 26f);
            guideRect.anchoredPosition = new Vector2(-60f, 0f);
            guide.text = "오른쪽 테두리 드래그";
            guide.color = new Color(0.68f, 0.80f, 0.82f, 0.9f);

            _widthPercentText = CreateText(
                "WidthPercent",
                _headerRect,
                13,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            var percentRect = _widthPercentText.rectTransform;
            percentRect.anchorMin = new Vector2(1f, 0.5f);
            percentRect.anchorMax = new Vector2(1f, 0.5f);
            percentRect.pivot = new Vector2(1f, 0.5f);
            percentRect.sizeDelta = new Vector2(54f, 26f);
            percentRect.anchoredPosition = new Vector2(-8f, 0f);

            SyncWidthSlider();
        }

        private void SyncWidthSlider()
        {
            if (_widthPercentText != null)
            {
                _widthPercentText.text =
                    $"{Mathf.RoundToInt(_widthScale * 100f)}%";
            }
        }

        private void BuildLogScroll()
        {
            var scrollRoot = CreatePanel(
                "LogScroll",
                _panelRect,
                new Color(0.02f, 0.055f, 0.065f, 0.94f));
            SetRect(
                scrollRoot,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 64f),
                new Vector2(-10f, -56f));

            _viewportRect = CreatePanel(
                "Viewport",
                scrollRoot,
                new Color(0f, 0f, 0f, 0.01f));
            Stretch(_viewportRect, 3f);
            var mask = _viewportRect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            _contentRect = CreateRect(
                "Content",
                _viewportRect,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0.5f, 1f),
                Vector2.zero);
            _contentRect.anchoredPosition = Vector2.zero;

            var layout =
                _contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter =
                _contentRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            _scrollRect.viewport = _viewportRect;
            _scrollRect.content = _contentRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 34f;
            _scrollRect.onValueChanged.AddListener(HandleScrolled);
        }

        private void BuildRecentButton()
        {
            _recentButton = CreateButton(
                "RecentLogButton",
                _panelRect,
                "최근 로그로 이동",
                new Color(0.06f, 0.34f, 0.42f, 0.98f),
                out _);
            var rect = _recentButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(166f, 34f);
            rect.anchoredPosition = new Vector2(0f, 66f);
            _recentButton.onClick.AddListener(ScrollToBottomDeferred);
            _recentButton.gameObject.SetActive(false);
        }

        private void BuildChatInput()
        {
            var row = CreatePanel(
                "ChatInputRow",
                _panelRect,
                new Color(0.015f, 0.11f, 0.13f, 1f));
            SetRect(
                row,
                Vector2.zero,
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(0f, 56f));

            var inputRoot = CreatePanel(
                "ChatInput",
                row,
                new Color(0.04f, 0.09f, 0.105f, 1f));
            SetRect(
                inputRoot,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 9f),
                new Vector2(-86f, -9f));

            _chatInput = inputRoot.gameObject.AddComponent<InputField>();
            _chatInput.lineType = InputField.LineType.SingleLine;
            _chatInput.characterLimit = 500;

            var inputText = CreateText(
                "Text",
                inputRoot,
                15,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            Stretch(inputText.rectTransform, 10f, 5f);
            inputText.color = Color.white;
            inputText.supportRichText = false;
            _chatInput.textComponent = inputText;

            _chatHint = CreateText(
                "Placeholder",
                inputRoot,
                14,
                FontStyle.Italic,
                TextAnchor.MiddleLeft);
            Stretch(_chatHint.rectTransform, 10f, 5f);
            _chatHint.color = new Color(0.62f, 0.72f, 0.75f, 0.8f);
            _chatHint.text = "활성 캐릭터를 선택하세요.";
            _chatInput.placeholder = _chatHint;
            _chatInput.onEndEdit.AddListener(HandleChatEndEdit);

            _sendButton = CreateButton(
                "SendButton",
                row,
                "전송",
                new Color(0.05f, 0.31f, 0.38f, 1f),
                out _);
            SetRect(
                _sendButton.GetComponent<RectTransform>(),
                new Vector2(1f, 0f),
                Vector2.one,
                new Vector2(-78f, 9f),
                new Vector2(-10f, -9f));
            _sendButton.onClick.AddListener(SubmitChat);
        }


        private void BuildResizeHandle()
        {
            _resizeHandleRect = CreatePanel(
                "WidthResizeHandle",
                _panelRect,
                new Color(0.12f, 0.55f, 0.63f, 0.34f));
            _resizeHandleRect.anchorMin = new Vector2(1f, 0f);
            _resizeHandleRect.anchorMax = new Vector2(1f, 1f);
            _resizeHandleRect.pivot = new Vector2(0.5f, 0.5f);
            _resizeHandleRect.offsetMin = new Vector2(
                -ResizeHandleHalfWidth,
                58f);
            _resizeHandleRect.offsetMax = new Vector2(
                ResizeHandleHalfWidth,
                -50f);
            _resizeHandleRect.GetComponent<Image>().raycastTarget = true;
            BindResizeHandleEvents();
            _resizeHandleRect.SetAsLastSibling();
        }

        private void BuildToggle()
        {
            _toggleButton = CreateButton(
                "LogChatToggle",
                _rootRect,
                "로그\n채팅",
                new Color(0.035f, 0.25f, 0.30f, 1f),
                out _toggleLabel);
            var rect = _toggleButton.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(ToggleWidth, 112f);
            rect.anchoredPosition = new Vector2(0f, 20f);
            _toggleButton.onClick.AddListener(
                () => SetOpen(!_isOpen));
        }

        private void CreateEntryRow(in PawnRollLogEntry entry)
        {
            if (_contentRect == null)
                return;

            var text = CreateText(
                $"Entry_{entry.Sequence}",
                _contentRect,
                14,
                entry.Kind == PawnRollLogKind.Chat
                    ? FontStyle.Bold
                    : FontStyle.Normal,
                TextAnchor.UpperLeft);
            text.color = GetEntryColor(entry);
            text.text = FormatEntry(entry);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            text.gameObject.AddComponent<LayoutElement>();
            ApplyEntryHeight(text);

            _entryRows.Enqueue(text.gameObject);
        }

        private void TrimVisibleRows()
        {
            while (_entryRows.Count > MaximumVisibleEntries)
            {
                var row = _entryRows.Dequeue();
                if (row != null)
                    Destroy(row);
            }
        }

        private void ClearRows()
        {
            while (_entryRows.Count > 0)
            {
                var row = _entryRows.Dequeue();
                if (row != null)
                    Destroy(row);
            }
        }

        private void HandleScrolled(Vector2 _)
        {
            RefreshRecentButton();
        }

        private void HandleChatEndEdit(string _)
        {
            if (!_isOpen || !_chatAvailable)
                return;

            if (Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                SubmitChat();
            }
        }

        private void SubmitChat()
        {
            if (!_isOpen || !_chatAvailable || _chatInput == null)
                return;

            var message = _chatInput.text?.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;

            _chatInput.text = string.Empty;
            ChatSubmitted?.Invoke(message);
            _chatInput.ActivateInputField();
        }

        private void RefreshChatControls()
        {
            var enabled = _isOpen && _chatAvailable;
            if (_chatInput != null)
                _chatInput.interactable = enabled;
            if (_sendButton != null)
                _sendButton.interactable = enabled;

            if (_chatHint != null)
            {
                _chatHint.text = _chatAvailable
                    ? $"{_speakerName}(으)로 메시지 입력"
                    : "오른쪽 목록에서 캐릭터를 활성화하세요.";
            }
        }

        private void RefreshPanelInputState()
        {
            if (_panelCanvasGroup == null)
                return;

            _panelCanvasGroup.interactable = _isOpen;
            _panelCanvasGroup.blocksRaycasts = _isOpen;
        }

        private void BindResizeHandleEvents()
        {
            if (_resizeHandleRect == null)
                return;

            var trigger =
                _resizeHandleRect.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger =
                    _resizeHandleRect.gameObject.AddComponent<EventTrigger>();
            }

            trigger.triggers = new List<EventTrigger.Entry>();
            AddTrigger(
                trigger,
                EventTriggerType.BeginDrag,
                BeginResizeFromEvent);
            AddTrigger(
                trigger,
                EventTriggerType.Drag,
                ContinueResizeFromEvent);
            AddTrigger(
                trigger,
                EventTriggerType.EndDrag,
                EndResizeFromEvent);
        }

        private static void AddTrigger(
            EventTrigger trigger,
            EventTriggerType eventType,
            Action<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry
            {
                eventID = eventType
            };
            entry.callback.AddListener(data => callback(data));
            trigger.triggers.Add(entry);
        }

        private void BeginResizeFromEvent(BaseEventData eventData)
        {
            var pointer = eventData as PointerEventData;
            if (!_isOpen || pointer == null || _rootRect == null)
                return;

            var parent = _rootRect.parent as RectTransform;
            if (parent == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    pointer.position,
                    pointer.pressEventCamera,
                    out var localPoint))
            {
                return;
            }

            _panelRect?.DOKill();
            _isDragging = false;
            _isResizing = true;
            _resizePointerStart = localPoint;
            _resizeWidthStart = _currentPanelWidth;
            _rootRect.SetAsLastSibling();
        }

        private void ContinueResizeFromEvent(BaseEventData eventData)
        {
            if (!_isResizing)
                return;

            var pointer = eventData as PointerEventData;
            var parent = _rootRect != null
                ? _rootRect.parent as RectTransform
                : null;
            if (pointer == null || parent == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    pointer.position,
                    pointer.pressEventCamera,
                    out var localPoint))
            {
                return;
            }

            ApplyResize(localPoint.x - _resizePointerStart.x);
        }

        private void EndResizeFromEvent(BaseEventData eventData)
        {
            if (!_isResizing)
                return;

            _isResizing = false;
            var parent = _rootRect != null
                ? _rootRect.parent as RectTransform
                : null;
            if (parent != null)
            {
                ClampOpenPosition(parent.rect.size);
            }

            RefreshResponsiveLayout(true);
        }

        private void ApplyResize(float pointerDeltaX)
        {
            var desiredWidth = _resizeWidthStart + pointerDeltaX;
            var minimumWidth =
                _maximumPanelWidth * MinimumWidthScale;
            desiredWidth = Mathf.Clamp(
                desiredWidth,
                minimumWidth,
                _maximumPanelWidth);

            _widthScale = _maximumPanelWidth > 0.01f
                ? desiredWidth / _maximumPanelWidth
                : MaximumWidthScale;
            _widthScale = Mathf.Clamp(
                _widthScale,
                MinimumWidthScale,
                MaximumWidthScale);
            SyncWidthSlider();
            RefreshResponsiveLayout(true);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _isDragging = false;
            _isResizing = false;
            if (!_isOpen || eventData == null || _rootRect == null)
                return;

            var parent = _rootRect.parent as RectTransform;
            if (parent == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var pointerPosition))
            {
                return;
            }

            if (_closeButton != null &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    _closeButton.GetComponent<RectTransform>(),
                    eventData.position,
                    eventData.pressEventCamera))
            {
                return;
            }

            if (_resizeHandleRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    _resizeHandleRect,
                    eventData.position,
                    eventData.pressEventCamera))
            {
                _rootRect.DOKill();
                _resizePointerStart = pointerPosition;
                _resizeWidthStart = _currentPanelWidth;
                _isResizing = true;
                _rootRect.SetAsLastSibling();
                return;
            }

            if (_headerRect == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(
                    _headerRect,
                    eventData.position,
                    eventData.pressEventCamera))
            {
                return;
            }

            _rootRect.DOKill();
            _dragPointerStart = pointerPosition;
            _dragWindowStart = _openPosition;
            _isDragging = true;
            _rootRect.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || _rootRect == null)
                return;

            var parent = _rootRect.parent as RectTransform;
            if (parent == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var pointerPosition))
            {
                return;
            }

            if (_isResizing)
            {
                ApplyResize(
                    pointerPosition.x - _resizePointerStart.x);
                return;
            }

            if (!_isDragging)
                return;

            _openPosition = _dragWindowStart +
                            pointerPosition -
                            _dragPointerStart;
            ClampOpenPosition(parent.rect.size);
            _rootRect.anchoredPosition = _openPosition;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            var wasResizing = _isResizing;
            _isDragging = false;
            _isResizing = false;

            var parent = _rootRect != null
                ? _rootRect.parent as RectTransform
                : null;
            if (parent == null)
                return;

            ClampOpenPosition(parent.rect.size);
            if (wasResizing)
                RefreshResponsiveLayout(true);
            else if (_isOpen)
                _rootRect.anchoredPosition = _openPosition;
        }

        private void ClampOpenPosition(Vector2 parentSize)
        {
            var minimumX = LeftMargin;
            var minimumY = BottomMargin;
            var occupiedWidth = ToggleWidth + 8f + _currentPanelWidth;
            var maximumX = Mathf.Max(
                minimumX,
                parentSize.x - occupiedWidth - LeftMargin);
            var maximumY = Mathf.Max(
                minimumY,
                parentSize.y - _currentPanelHeight - BottomMargin);

            _openPosition.x = Mathf.Clamp(
                _openPosition.x,
                minimumX,
                maximumX);
            _openPosition.y = Mathf.Clamp(
                _openPosition.y,
                minimumY,
                maximumY);
        }

        private void RefreshResponsiveLayout(bool immediate)
        {
            if (_rootRect == null ||
                _panelViewportRect == null ||
                _panelRect == null ||
                _rootRect.parent == null ||
                _isRefreshingLayout)
            {
                return;
            }

            var parent = _rootRect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            var parentSize = parent.rect.size;
            if (parentSize.x <= 1f || parentSize.y <= 1f)
            {
                return;
            }

            _isRefreshingLayout = true;
            try
            {
                _maximumPanelWidth = Mathf.Min(
                    DefaultPanelWidth,
                    Mathf.Max(220f, parentSize.x * 0.46f));
                _widthScale = Mathf.Clamp(
                    _widthScale,
                    MinimumWidthScale,
                    MaximumWidthScale);

                var panelWidth = _maximumPanelWidth * _widthScale;
                var panelHeight = Mathf.Min(
                    DefaultPanelHeight,
                    Mathf.Max(300f, parentSize.y - 80f));
                _currentPanelWidth = panelWidth;
                _currentPanelHeight = panelHeight;
                SyncWidthSlider();
                ClampOpenPosition(parentSize);

                _rootRect.sizeDelta = new Vector2(
                    ToggleWidth + 8f + panelWidth,
                    panelHeight);
                _rootRect.anchoredPosition = _openPosition;

                _panelViewportRect.anchorMin = Vector2.zero;
                _panelViewportRect.anchorMax = Vector2.zero;
                _panelViewportRect.pivot = Vector2.zero;
                _panelViewportRect.anchoredPosition =
                    new Vector2(ToggleWidth + 8f, 0f);
                _panelViewportRect.sizeDelta =
                    new Vector2(panelWidth, panelHeight);

                _panelRect.anchorMin = Vector2.zero;
                _panelRect.anchorMax = Vector2.zero;
                _panelRect.pivot = Vector2.zero;
                _panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
                RefreshEntryRowHeights();

                var target = _isOpen
                    ? Vector2.zero
                    : new Vector2(-panelWidth, 0f);
                _panelRect.DOKill();
                if (immediate)
                {
                    _panelRect.anchoredPosition = target;
                }
                else
                {
                    _panelRect.DOAnchorPos(target, SlideDuration)
                        .SetEase(Ease.OutCubic)
                        .SetUpdate(true);
                }
            }
            finally
            {
                _isRefreshingLayout = false;
            }
        }


        private void RefreshEntryRowHeights()
        {
            foreach (var row in _entryRows)
            {
                if (row == null)
                    continue;

                var text = row.GetComponent<Text>();
                if (text != null)
                    ApplyEntryHeight(text);
            }
        }

        private void ApplyEntryHeight(Text text)
        {
            if (text == null)
                return;

            var layout = text.GetComponent<LayoutElement>();
            if (layout == null)
                return;

            var availableWidth = Mathf.Max(80f, _currentPanelWidth - 46f);
            var charactersPerLine = Mathf.Max(
                10,
                Mathf.FloorToInt(availableWidth / 7.3f));
            var estimatedLines = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (text.text?.Length ?? 0) /
                    (float)charactersPerLine));
            var baseLines = text.fontStyle == FontStyle.Bold ? 1 : 2;
            layout.preferredHeight =
                Mathf.Max(baseLines, estimatedLines) * 19f + 12f;
        }

        private bool IsAtBottom()
        {
            if (_scrollRect == null ||
                _contentRect == null ||
                _viewportRect == null)
            {
                return true;
            }

            if (_contentRect.rect.height <= _viewportRect.rect.height + 1f)
                return true;

            return _scrollRect.verticalNormalizedPosition <= 0.025f;
        }

        private void RefreshRecentButton()
        {
            if (_recentButton == null)
                return;

            var hasOverflow = _contentRect != null &&
                              _viewportRect != null &&
                              _contentRect.rect.height >
                              _viewportRect.rect.height + 1f;
            _recentButton.gameObject.SetActive(
                _isOpen && hasOverflow && !IsAtBottom());
        }

        private void ScrollToBottomDeferred()
        {
            StopScrollRoutine();
            if (isActiveAndEnabled)
                _scrollRoutine = StartCoroutine(ScrollToBottomRoutine());
        }

        private IEnumerator ScrollToBottomRoutine()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null)
            {
                _scrollRect.StopMovement();
                _scrollRect.verticalNormalizedPosition = 0f;
            }

            _scrollRoutine = null;
            RefreshRecentButton();
        }

        private void StopScrollRoutine()
        {
            if (_scrollRoutine == null)
                return;

            StopCoroutine(_scrollRoutine);
            _scrollRoutine = null;
        }

        private string FormatEntry(in PawnRollLogEntry entry)
        {
            var time = entry.TimestampUtc.ToLocalTime().ToString("HH:mm");
            var owner = string.IsNullOrWhiteSpace(entry.PawnName)
                ? "시스템"
                : entry.PawnName;

            if (entry.Kind == PawnRollLogKind.Chat)
                return $"[{time}] {owner}: {entry.Detail}";

            if (entry.Kind == PawnRollLogKind.System)
                return $"[{time}] {entry.Title} · {entry.Detail}";

            var expression = string.IsNullOrWhiteSpace(entry.Expression)
                ? string.Empty
                : $"{entry.Expression} → ";
            var detail = string.IsNullOrWhiteSpace(entry.Detail)
                ? string.Empty
                : $"\n{entry.Detail}";
            return $"[{time}] {owner} · {entry.Title}\n" +
                   $"{expression}{entry.Value} · {entry.Result}{detail}";
        }

        private static Color GetEntryColor(in PawnRollLogEntry entry)
        {
            if (entry.Kind == PawnRollLogKind.Chat)
                return new Color(0.62f, 0.91f, 1f, 1f);
            if (entry.Kind == PawnRollLogKind.System)
                return new Color(0.66f, 0.72f, 0.74f, 1f);
            if (entry.Result.IndexOf(
                    "대실패",
                    StringComparison.Ordinal) >= 0)
            {
                return new Color(1f, 0.42f, 0.42f, 1f);
            }
            if (entry.Result.IndexOf(
                    "대성공",
                    StringComparison.Ordinal) >= 0)
            {
                return new Color(0.42f, 1f, 0.62f, 1f);
            }

            return new Color(0.9f, 0.94f, 0.95f, 1f);
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            string label,
            Color color,
            out Text labelText)
        {
            var rect = CreatePanel(objectName, parent, color);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = color * 1.14f;
            colors.pressedColor = color * 0.78f;
            colors.disabledColor = new Color(
                color.r,
                color.g,
                color.b,
                0.38f);
            button.colors = colors;

            labelText = CreateText(
                "Label",
                rect,
                14,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            Stretch(labelText.rectTransform, 5f);
            labelText.text = label;
            return button;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            FontStyle style,
            TextAnchor alignment)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var text = go.GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreatePanel(
            string objectName,
            Transform parent,
            Color color)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return rect;
        }

        private static RectTransform CreateRect(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 sizeDelta)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            Stretch(rect, inset, inset);
        }

        private static void Stretch(
            RectTransform rect,
            float horizontal,
            float vertical)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
                text.text = value ?? string.Empty;
        }

        private static Font ResolveFont(Font requested)
        {
            if (requested != null)
                return requested;

            try
            {
                return Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }
            catch
            {
                return Font.CreateDynamicFontFromOSFont("Arial", 16);
            }
        }

        private void OnDestroy()
        {
            StopScrollRoutine();
            if (_rootRect != null)
                _rootRect.DOKill();
            if (_panelRect != null)
                _panelRect.DOKill();
            ChatSubmitted = null;
        }
    }
}
