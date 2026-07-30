using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Trpg.Domain.Dice;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public sealed class SessionLogWidget : MonoBehaviour
    {
        private const int CanvasSortingOrder = 5200;
        private const float PanelWidth = 560f;
        private const float PanelHeight = 640f;

        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _toggleButton;
        [SerializeField] private Text _toggleText;
        [SerializeField] private Text _logText;
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private Text _selectionText;
        [SerializeField] private Button _previousButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _pushButton;
        [SerializeField] private Button _opposedButton;
        [SerializeField] private Button _luckButton;
        [SerializeField] private InputField _luckInput;
        [SerializeField] private Text _statusText;

        private readonly List<CoCCheckRecord> _records =
            new List<CoCCheckRecord>();
        private int _selectedIndex = -1;
        private bool _listenersBound;

        public event Action<string> PushRequested;
        public event Action<string> OpposedRequested;
        public event Action<string, int> LuckSpendRequested;
        public event Action<string> SelectionChanged;

        public string SelectedRecordId =>
            _selectedIndex >= 0 &&
            _selectedIndex < _records.Count
                ? _records[_selectedIndex].Id
                : string.Empty;

        public static SessionLogWidget CreateRuntime(
            Vector2 referenceResolution)
        {
            var canvasObject = new GameObject(
                "SessionLogCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            EnsureEventSystem();

            var widget = canvasObject.AddComponent<SessionLogWidget>();
            widget.BuildRuntimeUi(canvasObject.transform);
            widget.BindListeners();
            widget.SetVisible(false);
            widget.Bind(Array.Empty<CoCCheckRecord>());
            return widget;
        }

        public void Bind(IReadOnlyList<CoCCheckRecord> records)
        {
            var selectedId = SelectedRecordId;
            var previousCount = _records.Count;
            var incomingCount = records != null ? records.Count : 0;
            if (incomingCount > previousCount)
            {
                selectedId = string.Empty;
            }

            _records.Clear();
            if (records != null)
            {
                for (var index = 0; index < records.Count; index++)
                {
                    var record = records[index];
                    if (record != null)
                    {
                        _records.Add(record);
                    }
                }
            }

            _selectedIndex = FindIndex(selectedId);
            if (_selectedIndex < 0 && _records.Count > 0)
            {
                _selectedIndex = _records.Count - 1;
            }

            Refresh();
        }

        public void SetActionAvailability(
            bool canPush,
            bool canOppose,
            bool canSpendLuck,
            int suggestedLuck)
        {
            if (_pushButton != null)
            {
                _pushButton.interactable = canPush;
            }

            if (_opposedButton != null)
            {
                _opposedButton.interactable = canOppose;
            }

            if (_luckButton != null)
            {
                _luckButton.interactable = canSpendLuck;
            }

            if (_luckInput != null)
            {
                _luckInput.interactable = canSpendLuck;
                if (canSpendLuck && suggestedLuck > 0)
                {
                    _luckInput.text = suggestedLuck.ToString(
                        CultureInfo.InvariantCulture);
                }
                else if (!canSpendLuck)
                {
                    _luckInput.text = string.Empty;
                }
            }
        }

        public void SetStatus(string message, bool isError)
        {
            if (_statusText == null)
            {
                return;
            }

            _statusText.text = message ?? string.Empty;
            _statusText.color = isError
                ? new Color(1f, 0.36f, 0.30f)
                : new Color(0.42f, 0.92f, 0.72f);
        }

        public void SetVisible(bool visible)
        {
            if (_panel != null)
            {
                _panel.SetActive(visible);
            }

            if (_toggleText != null)
            {
                _toggleText.text = visible ? "LOG ×" : "LOG";
            }
        }

        private void BuildRuntimeUi(Transform canvasTransform)
        {
            var font = ResolveFont();
            _toggleButton = CreateButton(
                "SessionLogToggle",
                canvasTransform,
                font,
                "LOG",
                18,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-24f, -24f),
                new Vector2(96f, 44f),
                new Color(0.08f, 0.13f, 0.18f, 0.98f),
                out _toggleText);

            _panel = CreateUiObject("SessionLogPanel", canvasTransform);
            var panelRect = _panel.GetComponent<RectTransform>();
            SetRect(
                panelRect,
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-24f, -78f),
                new Vector2(PanelWidth, PanelHeight));
            var panelImage = _panel.AddComponent<Image>();
            panelImage.color =
                new Color(0.035f, 0.055f, 0.075f, 0.98f);

            var header = CreateText(
                "Header",
                _panel.transform,
                font,
                24,
                TextAnchor.MiddleLeft);
            SetOffsets(
                header.rectTransform,
                new Vector2(20f, PanelHeight - 60f),
                new Vector2(PanelWidth - 20f, PanelHeight - 12f));
            header.text = "세션 판정 로그";

            var viewportObject =
                CreateUiObject("Viewport", _panel.transform);
            var viewportRect =
                viewportObject.GetComponent<RectTransform>();
            SetOffsets(
                viewportRect,
                new Vector2(18f, 190f),
                new Vector2(PanelWidth - 18f, PanelHeight - 66f));
            var viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.color =
                new Color(0.015f, 0.025f, 0.035f, 0.86f);
            viewportObject.AddComponent<RectMask2D>();

            var contentObject =
                CreateUiObject("Content", viewportObject.transform);
            var contentRect =
                contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            _logText = contentObject.AddComponent<Text>();
            _logText.font = font;
            _logText.fontSize = 17;
            _logText.color = new Color(0.90f, 0.94f, 0.98f);
            _logText.alignment = TextAnchor.UpperLeft;
            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Overflow;
            _logText.raycastTarget = false;

            var fitter =
                contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit =
                ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _scrollRect = viewportObject.AddComponent<ScrollRect>();
            _scrollRect.viewport = viewportRect;
            _scrollRect.content = contentRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 24f;

            _previousButton = CreateButton(
                "PreviousRecord",
                _panel.transform,
                font,
                "‹",
                24,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                new Vector2(18f, 142f),
                new Vector2(46f, 40f),
                new Color(0.11f, 0.17f, 0.22f, 1f),
                out _);
            _nextButton = CreateButton(
                "NextRecord",
                _panel.transform,
                font,
                "›",
                24,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                new Vector2(PanelWidth - 64f, 142f),
                new Vector2(46f, 40f),
                new Color(0.11f, 0.17f, 0.22f, 1f),
                out _);

            _selectionText = CreateText(
                "SelectedRecord",
                _panel.transform,
                font,
                16,
                TextAnchor.MiddleCenter);
            SetOffsets(
                _selectionText.rectTransform,
                new Vector2(70f, 142f),
                new Vector2(PanelWidth - 70f, 182f));

            _pushButton = CreateButton(
                "PushCheck",
                _panel.transform,
                font,
                "강행",
                17,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                new Vector2(18f, 88f),
                new Vector2(118f, 42f),
                new Color(0.28f, 0.17f, 0.12f, 1f),
                out _);
            _opposedButton = CreateButton(
                "OpposedCheck",
                _panel.transform,
                font,
                "대항",
                17,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                new Vector2(144f, 88f),
                new Vector2(118f, 42f),
                new Color(0.13f, 0.21f, 0.31f, 1f),
                out _);

            _luckInput = CreateInput(
                "LuckAmount",
                _panel.transform,
                font,
                "LUCK",
                new Vector2(270f, 88f),
                new Vector2(112f, 42f));
            _luckButton = CreateButton(
                "SpendLuck",
                _panel.transform,
                font,
                "소비",
                17,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                new Vector2(390f, 88f),
                new Vector2(152f, 42f),
                new Color(0.27f, 0.23f, 0.10f, 1f),
                out _);

            _statusText = CreateText(
                "Status",
                _panel.transform,
                font,
                15,
                TextAnchor.MiddleLeft);
            SetOffsets(
                _statusText.rectTransform,
                new Vector2(18f, 18f),
                new Vector2(PanelWidth - 18f, 74f));
            _statusText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _statusText.verticalOverflow =
                VerticalWrapMode.Truncate;
        }

        private void BindListeners()
        {
            if (_listenersBound)
            {
                return;
            }

            _toggleButton.onClick.AddListener(HandleToggle);
            _previousButton.onClick.AddListener(
                () => ChangeSelection(-1));
            _nextButton.onClick.AddListener(
                () => ChangeSelection(1));
            _pushButton.onClick.AddListener(HandlePush);
            _opposedButton.onClick.AddListener(HandleOpposed);
            _luckButton.onClick.AddListener(HandleLuck);
            _listenersBound = true;
        }

        private void OnDestroy()
        {
            PushRequested = null;
            OpposedRequested = null;
            LuckSpendRequested = null;
            SelectionChanged = null;
        }

        private void HandleToggle()
        {
            SetVisible(_panel == null || !_panel.activeSelf);
        }

        private void ChangeSelection(int direction)
        {
            if (_records.Count == 0)
            {
                return;
            }

            _selectedIndex = Mathf.Clamp(
                _selectedIndex + direction,
                0,
                _records.Count - 1);
            Refresh();
            SelectionChanged?.Invoke(SelectedRecordId);
        }

        private void HandlePush()
        {
            if (!string.IsNullOrWhiteSpace(SelectedRecordId))
            {
                PushRequested?.Invoke(SelectedRecordId);
            }
        }

        private void HandleOpposed()
        {
            if (!string.IsNullOrWhiteSpace(SelectedRecordId))
            {
                OpposedRequested?.Invoke(SelectedRecordId);
            }
        }

        private void HandleLuck()
        {
            if (string.IsNullOrWhiteSpace(SelectedRecordId) ||
                _luckInput == null ||
                !int.TryParse(
                    _luckInput.text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                SetStatus("소비할 Luck을 정수로 입력해 주세요.", true);
                return;
            }

            LuckSpendRequested?.Invoke(SelectedRecordId, amount);
        }

        private void Refresh()
        {
            if (_logText != null)
            {
                var builder = new StringBuilder();
                if (_records.Count == 0)
                {
                    builder.Append("아직 판정 기록이 없습니다.");
                }
                else
                {
                    for (var index = 0;
                         index < _records.Count;
                         index++)
                    {
                        builder.Append(index == _selectedIndex
                                ? "▶ "
                                : "   ")
                            .Append(FormatRecord(_records[index]));
                        if (index + 1 < _records.Count)
                        {
                            builder.Append('\n');
                        }
                    }
                }

                _logText.text = builder.ToString();
            }

            if (_selectionText != null)
            {
                _selectionText.text =
                    _selectedIndex >= 0
                        ? $"{_selectedIndex + 1} / {_records.Count}  " +
                          FormatSelection(_records[_selectedIndex])
                        : "선택된 판정 없음";
            }

            if (_previousButton != null)
            {
                _previousButton.interactable = _selectedIndex > 0;
            }

            if (_nextButton != null)
            {
                _nextButton.interactable =
                    _selectedIndex >= 0 &&
                    _selectedIndex < _records.Count - 1;
            }

            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null)
            {
                _scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private int FindIndex(string recordId)
        {
            if (string.IsNullOrWhiteSpace(recordId))
            {
                return -1;
            }

            for (var index = 0; index < _records.Count; index++)
            {
                if (string.Equals(
                        _records[index].Id,
                        recordId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string FormatRecord(CoCCheckRecord record)
        {
            var builder = new StringBuilder();
            if (DateTime.TryParse(
                    record.OccurredAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var occurredAt))
            {
                builder.Append('[')
                    .Append(
                        occurredAt.ToLocalTime().ToString(
                            "HH:mm:ss",
                            CultureInfo.InvariantCulture))
                    .Append("] ");
            }

            builder.Append('#')
                .Append(record.Sequence)
                .Append(' ')
                .Append(GetKindLabel(record.Kind))
                .Append(' ')
                .Append(record.PawnName)
                .Append(" / ")
                .Append(record.StatName)
                .Append("  ")
                .Append(record.OriginalRoll);

            if (record.FinalRoll != record.OriginalRoll)
            {
                builder.Append('→').Append(record.FinalRoll);
            }

            builder.Append(" ≤ ")
                .Append(record.Target)
                .Append("  ")
                .Append(GetOutcomeLabel(record.Outcome));

            if (record.LuckSpent > 0)
            {
                builder.Append("  [LUCK -")
                    .Append(record.LuckSpent)
                    .Append(']');
            }

            if (record.OpposedResult != CoCOpposedResult.None)
            {
                builder.Append("  [대항 ")
                    .Append(GetOpposedLabel(record.OpposedResult))
                    .Append(']');
            }

            return builder.ToString();
        }

        private static string FormatSelection(CoCCheckRecord record)
        {
            return $"{record.PawnName} · {record.StatName}";
        }

        private static string GetKindLabel(CoCCheckKind kind)
        {
            switch (kind)
            {
                case CoCCheckKind.Pushed:
                    return "[강행]";
                case CoCCheckKind.Opposed:
                    return "[대항]";
                default:
                    return "[판정]";
            }
        }

        private static string GetOutcomeLabel(CoCCheckOutcome outcome)
        {
            switch (outcome)
            {
                case CoCCheckOutcome.CriticalSuccess:
                    return "대성공";
                case CoCCheckOutcome.ExtremeSuccess:
                    return "극단적 성공";
                case CoCCheckOutcome.HardSuccess:
                    return "어려운 성공";
                case CoCCheckOutcome.Success:
                    return "성공";
                case CoCCheckOutcome.Fumble:
                    return "대실패";
                case CoCCheckOutcome.Failure:
                    return "실패";
                default:
                    return "무효";
            }
        }

        private static string GetOpposedLabel(
            CoCOpposedResult result)
        {
            switch (result)
            {
                case CoCOpposedResult.Win:
                    return "승";
                case CoCOpposedResult.Lose:
                    return "패";
                case CoCOpposedResult.Draw:
                    return "동률";
                case CoCOpposedResult.NoWinner:
                    return "승자 없음";
                default:
                    return "-";
            }
        }

        private static Button CreateButton(
            string objectName,
            Transform parent,
            Font font,
            string label,
            int fontSize,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            Color color,
            out Text labelText)
        {
            var buttonObject = CreateUiObject(objectName, parent);
            SetRect(
                buttonObject.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                pivot,
                position,
                size);
            var image = buttonObject.AddComponent<Image>();
            image.color = color;
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            labelText = CreateText(
                "Label",
                buttonObject.transform,
                font,
                fontSize,
                TextAnchor.MiddleCenter);
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = Vector2.zero;
            labelText.rectTransform.offsetMax = Vector2.zero;
            labelText.text = label;
            return button;
        }

        private static InputField CreateInput(
            string objectName,
            Transform parent,
            Font font,
            string placeholder,
            Vector2 position,
            Vector2 size)
        {
            var inputObject = CreateUiObject(objectName, parent);
            SetRect(
                inputObject.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                position,
                size);
            var image = inputObject.AddComponent<Image>();
            image.color = new Color(0.07f, 0.09f, 0.11f, 1f);

            var text = CreateText(
                "Text",
                inputObject.transform,
                font,
                17,
                TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8f, 4f);
            text.rectTransform.offsetMax = new Vector2(-8f, -4f);

            var placeholderText = CreateText(
                "Placeholder",
                inputObject.transform,
                font,
                16,
                TextAnchor.MiddleCenter);
            placeholderText.rectTransform.anchorMin = Vector2.zero;
            placeholderText.rectTransform.anchorMax = Vector2.one;
            placeholderText.rectTransform.offsetMin =
                new Vector2(8f, 4f);
            placeholderText.rectTransform.offsetMax =
                new Vector2(-8f, -4f);
            placeholderText.text = placeholder;
            placeholderText.color = new Color(1f, 1f, 1f, 0.38f);

            var input = inputObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.contentType = InputField.ContentType.IntegerNumber;
            return input;
        }

        private static Text CreateText(
            string objectName,
            Transform parent,
            Font font,
            int fontSize,
            TextAnchor alignment)
        {
            var textObject = CreateUiObject(objectName, parent);
            var text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent)
        {
            var value = new GameObject(
                objectName,
                typeof(RectTransform));
            value.transform.SetParent(parent, false);
            return value;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetOffsets(
            RectTransform rect,
            Vector2 minimum,
            Vector2 maximum)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = minimum;
            rect.sizeDelta = maximum - minimum;
        }

        private static Font ResolveFont()
        {
            Font font = null;
            try
            {
                font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                // Unity 배포판별 내장 폰트 이름 차이는 OS 폰트로 보완한다.
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
}
