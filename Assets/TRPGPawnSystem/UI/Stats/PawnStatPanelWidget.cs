using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public readonly struct PawnResourceValueData
    {
        public PawnResourceValueData(
            string label,
            string currentStatId,
            double current,
            string maximumStatId,
            double maximum,
            bool isVisible,
            bool canEditCurrent,
            bool canEditMaximum)
        {
            Label = label;
            CurrentStatId = currentStatId;
            Current = current;
            MaximumStatId = maximumStatId;
            Maximum = maximum;
            IsVisible = isVisible;
            CanEditCurrent = canEditCurrent;
            CanEditMaximum = canEditMaximum;
        }

        public string Label { get; }
        public string CurrentStatId { get; }
        public double Current { get; }
        public string MaximumStatId { get; }
        public double Maximum { get; }
        public bool IsVisible { get; }
        public bool CanEditCurrent { get; }
        public bool CanEditMaximum { get; }

        public string FormatValue()
        {
            return $"{FormatNumber(Current)} / {FormatNumber(Maximum)}";
        }

        internal static string FormatNumber(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 0.0001d
                ? Math.Round(value).ToString(
                    "0",
                    CultureInfo.InvariantCulture)
                : value.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture);
        }
    }

    public readonly struct PawnStatAxisData
    {
        public PawnStatAxisData(
            string statId,
            string displayName,
            double value,
            double minimum,
            double maximum)
        {
            StatId = statId;
            DisplayName = displayName;
            Value = value;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string StatId { get; }
        public string DisplayName { get; }
        public double Value { get; }
        public double Minimum { get; }
        public double Maximum { get; }

        public float NormalizedValue
        {
            get
            {
                var range = Maximum - Minimum;
                if (range <= 0.0001d)
                    return 0f;

                return Mathf.Clamp01(
                    (float)((Value - Minimum) / range));
            }
        }

        public string FormatLabel()
        {
            var value = Math.Abs(Value - Math.Round(Value)) < 0.0001d
                ? Math.Round(Value).ToString(
                    "0",
                    CultureInfo.InvariantCulture)
                : Value.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture);
            return $"{DisplayName}\n{value}";
        }
    }

    public readonly struct PawnStatEntryData
    {
        public PawnStatEntryData(
            string statId,
            string displayName,
            string valueLabel,
            double editableValue,
            bool canEdit)
            : this(
                statId,
                displayName,
                valueLabel,
                editableValue,
                canEdit,
                false,
                0,
                0,
                0,
                editableValue,
                0,
                0,
                double.MinValue,
                double.MaxValue)
        {
        }

        public PawnStatEntryData(
            string statId,
            string displayName,
            string valueLabel,
            double editableValue,
            bool canEdit,
            bool showsDifficulty,
            int regular,
            int hard,
            int extreme,
            double baseValue,
            double otherModifier,
            double manualModifier,
            double minimumValue,
            double maximumValue)
        {
            StatId = statId;
            DisplayName = displayName;
            ValueLabel = valueLabel;
            EditableValue = editableValue;
            CanEdit = canEdit;
            ShowsDifficulty = showsDifficulty;
            Regular = regular;
            Hard = hard;
            Extreme = extreme;
            BaseValue = baseValue;
            OtherModifier = otherModifier;
            ManualModifier = manualModifier;
            MinimumValue = minimumValue;
            MaximumValue = maximumValue;
        }

        public string StatId { get; }
        public string DisplayName { get; }
        public string ValueLabel { get; }
        public double EditableValue { get; }
        public bool CanEdit { get; }
        public bool ShowsDifficulty { get; }
        public int Regular { get; }
        public int Hard { get; }
        public int Extreme { get; }
        public double BaseValue { get; }
        public double OtherModifier { get; }
        public double ManualModifier { get; }
        public double MinimumValue { get; }
        public double MaximumValue { get; }

        public string FormatRegular()
        {
            return ShowsDifficulty
                ? Regular.ToString(CultureInfo.InvariantCulture)
                : ValueLabel ?? string.Empty;
        }

        public string FormatHard()
        {
            return ShowsDifficulty
                ? Hard.ToString(CultureInfo.InvariantCulture)
                : "—";
        }

        public string FormatExtreme()
        {
            return ShowsDifficulty
                ? Extreme.ToString(CultureInfo.InvariantCulture)
                : "—";
        }
    }

    public readonly struct PawnStatPanelData
    {
        public PawnStatPanelData(
            IReadOnlyList<PawnStatAxisData> axes,
            IReadOnlyList<PawnStatEntryData> entries,
            IReadOnlyList<PawnResourceValueData> resources)
            : this(
                string.Empty,
                null,
                axes,
                entries,
                resources,
                null)
        {
        }

        public PawnStatPanelData(
            string displayName,
            Sprite portrait,
            IReadOnlyList<PawnStatAxisData> axes,
            IReadOnlyList<PawnStatEntryData> entries,
            IReadOnlyList<PawnResourceValueData> resources)
            : this(
                displayName,
                portrait,
                axes,
                entries,
                resources,
                null)
        {
        }

        public PawnStatPanelData(
            string displayName,
            Sprite portrait,
            IReadOnlyList<PawnStatAxisData> axes,
            IReadOnlyList<PawnStatEntryData> entries,
            IReadOnlyList<PawnResourceValueData> resources,
            IReadOnlyList<PawnSkillValueData> skills)
            : this(
                displayName,
                portrait,
                axes,
                entries,
                resources,
                skills,
                null,
                false)
        {
        }

        public PawnStatPanelData(
            string displayName,
            Sprite portrait,
            IReadOnlyList<PawnStatAxisData> axes,
            IReadOnlyList<PawnStatEntryData> entries,
            IReadOnlyList<PawnResourceValueData> resources,
            IReadOnlyList<PawnSkillValueData> skills,
            IReadOnlyList<PawnSkillOptionData> availableSkillOptions,
            bool canAddSkills)
        {
            DisplayName = displayName;
            Portrait = portrait;
            Axes = axes;
            Entries = entries;
            Resources = resources;
            Skills = skills;
            AvailableSkillOptions = availableSkillOptions;
            CanAddSkills = canAddSkills;
        }

        public string DisplayName { get; }
        public Sprite Portrait { get; }
        public IReadOnlyList<PawnStatAxisData> Axes { get; }
        public IReadOnlyList<PawnStatEntryData> Entries { get; }
        public IReadOnlyList<PawnResourceValueData> Resources { get; }
        public IReadOnlyList<PawnSkillValueData> Skills { get; }
        public IReadOnlyList<PawnSkillOptionData>
            AvailableSkillOptions { get; }
        public bool CanAddSkills { get; }
    }

    public sealed class PawnResourceBarWidget : MonoBehaviour
    {
        private sealed class ResourceVisual
        {
            public GameObject Root;
            public Text Label;
            public InputField CurrentInput;
            public InputField MaximumInput;
        }

        private readonly List<ResourceVisual> _visuals =
            new List<ResourceVisual>();

        private RectTransform _rect;
        private GridLayoutGroup _layout;
        private Font _font;

        public event Action<string, double> ValueEditRequested;

        public static PawnResourceBarWidget CreateRuntime(
            RectTransform infoBarPanel,
            Font font)
        {
            if (infoBarPanel == null)
                return null;

            var root = new GameObject(
                "PawnResourceBar",
                typeof(RectTransform),
                typeof(GridLayoutGroup));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(infoBarPanel, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var widget = root.AddComponent<PawnResourceBarWidget>();
            widget.Build(rect, font);
            root.SetActive(false);
            return widget;
        }

        public void Bind(
            IReadOnlyList<PawnResourceValueData> resources)
        {
            var used = 0;
            var count = resources != null ? resources.Count : 0;
            for (var index = 0; index < count; index++)
            {
                var data = resources[index];
                if (!data.IsVisible)
                    continue;

                var visual = GetVisual(used);
                visual.Label.text = data.Label ?? string.Empty;
                BindValueInput(
                    visual.CurrentInput,
                    data.CurrentStatId,
                    data.Current,
                    data.CanEditCurrent);
                BindValueInput(
                    visual.MaximumInput,
                    data.MaximumStatId,
                    data.Maximum,
                    data.CanEditMaximum);
                visual.Root.SetActive(true);
                used++;
            }

            for (var index = used; index < _visuals.Count; index++)
            {
                _visuals[index].Root.SetActive(false);
            }

            gameObject.SetActive(used > 0);
            if (used > 0)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
        }

        public void Clear()
        {
            for (var index = 0; index < _visuals.Count; index++)
            {
                var visual = _visuals[index];
                visual.CurrentInput.onEndEdit.RemoveAllListeners();
                visual.MaximumInput.onEndEdit.RemoveAllListeners();
                visual.Root.SetActive(false);
            }

            gameObject.SetActive(false);
        }

        public void SetLayoutArea(
            float left,
            float bottom,
            float width,
            float height)
        {
            if (_rect == null)
                return;

            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.zero;
            _rect.pivot = Vector2.zero;
            _rect.anchoredPosition = new Vector2(
                Mathf.Max(0f, left),
                Mathf.Max(0f, bottom));
            _rect.sizeDelta = new Vector2(
                Mathf.Max(240f, width),
                Mathf.Max(100f, height));

            if (_layout == null)
                return;

            var spacing = _layout.spacing;
            var cellWidth = Mathf.Max(
                112f,
                (_rect.sizeDelta.x - spacing.x) * 0.5f);
            var cellHeight = Mathf.Max(
                54f,
                (_rect.sizeDelta.y - spacing.y) * 0.5f);
            _layout.cellSize = new Vector2(cellWidth, cellHeight);
        }

        private void Build(RectTransform rect, Font font)
        {
            _rect = rect;
            _font = font;
            _layout = GetComponent<GridLayoutGroup>();
            _layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _layout.constraint =
                GridLayoutGroup.Constraint.FixedColumnCount;
            _layout.constraintCount = 2;
            _layout.spacing = new Vector2(10f, 10f);
            _layout.padding = new RectOffset(0, 0, 2, 2);
            _layout.childAlignment = TextAnchor.UpperLeft;
            _layout.cellSize = new Vector2(176f, 62f);
        }

        private ResourceVisual GetVisual(int index)
        {
            while (_visuals.Count <= index)
                _visuals.Add(CreateVisual());

            return _visuals[index];
        }

        private ResourceVisual CreateVisual()
        {
            var root = new GameObject(
                "Resource",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            root.transform.SetParent(_rect, false);
            root.GetComponent<Image>().color =
                new Color(0.045f, 0.13f, 0.16f, 0.96f);
            root.GetComponent<LayoutElement>().ignoreLayout = false;

            var label = CreateText(
                "Label",
                root.transform,
                13,
                TextAnchor.UpperLeft);
            label.color = new Color(0.58f, 0.78f, 0.82f);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(9f, 3f);
            labelRect.offsetMax = new Vector2(-9f, -3f);

            var currentInput = CreateValueInput(
                "Current",
                root.transform,
                new Vector2(0f, 0f),
                new Vector2(0.45f, 0.62f),
                new Vector2(7f, 4f),
                new Vector2(-2f, -2f));

            var separator = CreateText(
                "Separator",
                root.transform,
                16,
                TextAnchor.LowerCenter);
            separator.text = "/";
            var separatorRect = separator.rectTransform;
            separatorRect.anchorMin = new Vector2(0.43f, 0f);
            separatorRect.anchorMax = new Vector2(0.57f, 0.62f);
            separatorRect.offsetMin = new Vector2(-2f, 4f);
            separatorRect.offsetMax = new Vector2(2f, -2f);

            var maximumInput = CreateValueInput(
                "Maximum",
                root.transform,
                new Vector2(0.55f, 0f),
                new Vector2(1f, 0.62f),
                new Vector2(2f, 4f),
                new Vector2(-7f, -2f));

            return new ResourceVisual
            {
                Root = root,
                Label = label,
                CurrentInput = currentInput,
                MaximumInput = maximumInput
            };
        }

        private InputField CreateValueInput(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var value = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(InputField));
            value.transform.SetParent(parent, false);
            var rect = value.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var image = value.GetComponent<Image>();
            var text = CreateText(
                "Text",
                value.transform,
                18,
                TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2f, 1f);
            textRect.offsetMax = new Vector2(-2f, -1f);

            var input = value.GetComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.contentType = InputField.ContentType.DecimalNumber;
            return input;
        }

        private void BindValueInput(
            InputField input,
            string statId,
            double value,
            bool canEdit)
        {
            input.onEndEdit.RemoveAllListeners();
            input.SetTextWithoutNotify(
                PawnResourceValueData.FormatNumber(value));
            input.interactable = canEdit;

            var image = input.targetGraphic as Image;
            if (image != null)
            {
                image.color = canEdit
                    ? new Color(0.03f, 0.20f, 0.25f, 0.98f)
                    : new Color(1f, 1f, 1f, 0f);
            }

            if (canEdit && !string.IsNullOrWhiteSpace(statId))
            {
                input.onEndEdit.AddListener(
                    text => HandleValueEdit(statId, text));
            }
        }

        private void HandleValueEdit(string statId, string text)
        {
            const NumberStyles styles = NumberStyles.Float;
            if (double.TryParse(
                    text,
                    styles,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                double.TryParse(
                    text,
                    styles,
                    CultureInfo.CurrentCulture,
                    out value))
            {
                ValueEditRequested?.Invoke(statId, value);
            }
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            TextAnchor alignment)
        {
            var value = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            value.transform.SetParent(parent, false);
            var text = value.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void OnDestroy()
        {
            ValueEditRequested = null;
        }
    }

    public sealed class PawnStatPanelWidget : MonoBehaviour
    {
        private const int AxisCount = 8;
        private const float DrawerWidth = 480f;
        private const float DrawerHeight = 850f;
        private const float SummaryWidth = 356f;
        private const float SummaryHeight = 160f;
        private const float EntryHeight = 40f;
        private const float EntrySpacing = 4f;
        private const float EntryHeaderHeight = 28f;
        private const float DrawerTopOffset = 188f;
        private const float DrawerTransitionDuration = 0.24f;
        private const float DrawerHiddenOffset = 48f;
        private const float RadarRevealDuration = 0.55f;
        private const float RadarTrackingDuration = 0.28f;
        private const float SummaryRestScale = 0.84f;
        private const float SummaryHoverScale = 1f;
        private const float SummaryHoverDuration = 0.18f;
        private const float ChartTop = 48f;
        private const float ChartGap = 8f;
        private const float ViewportGap = 6f;
        private const float ViewportBottom = 14f;
        private const float DefaultChartMinHeight = 112f;
        private const float DefaultChartMaxHeight = 220f;
        private const float GraphFocusChartHeight = 330f;
        private const float DetailFocusChartHeight = 108f;
        private const float MinimumEntryViewportHeight = 140f;
        private const float LayoutFocusDuration = 0.20f;
        private const float RadarVerticalPadding = 52f;
        private const float RadarMinSize = 72f;
        private const float RadarMaxSize = 260f;
        private const float AxisLabelMinRadius = 34f;
        private const float AxisLabelMaxRadius = 126f;

        private enum LayoutFocus
        {
            Default,
            Graph,
            Details
        }

        private sealed class EntryVisual
        {
            public GameObject Root;
            public Button Button;
            public Text NameText;
            public Text RegularText;
            public Text HardText;
            public Text ExtremeText;
            public InputField RegularInput;
            public PawnStatEntryData Data;
        }

        private sealed class SummaryVisual
        {
            public GameObject Root;
            public Text Label;
            public Text Value;
            public Image Fill;
        }

        private readonly List<EntryVisual> _entries =
            new List<EntryVisual>();
        private readonly List<Text> _axisLabels =
            new List<Text>(AxisCount);
        private readonly List<SummaryVisual> _summaryVisuals =
            new List<SummaryVisual>(2);
        private readonly float[] _radarValues = new float[AxisCount];

        private RectTransform _rootRect;
        private RectTransform _drawerRect;
        private RectTransform _summaryRect;
        private RectTransform _chartRect;
        private RectTransform _radarRect;
        private RectTransform _content;
        private RectTransform _entryViewportRect;
        private RectTransform _entryHeaderRect;
        private PawnSkillPanelWidget _skillPanel;
        private Button _skillToggleButton;
        private Image _skillToggleImage;
        private Button _quickCheckButton;
        private Button _closeButton;
        private Image _summaryPortraitImage;
        private Button _summaryPortraitButton;
        private Text _summaryNameText;
        private EventTrigger.Entry _summaryPointerEnterTrigger;
        private EventTrigger.Entry _summaryPointerExitTrigger;
        private EventTrigger.Entry _chartPointerEnterTrigger;
        private EventTrigger.Entry _chartPointerExitTrigger;
        private EventTrigger.Entry _entryPointerEnterTrigger;
        private EventTrigger.Entry _entryPointerExitTrigger;
        private GridLayoutGroup _grid;
        private ScrollRect _scrollRect;
        private PawnRadarGraphic _radar;
        private CanvasGroup _drawerCanvasGroup;
        private Sequence _drawerTransition;
        private Tween _summaryHoverTween;
        private Tween _layoutTween;
        private Vector2 _drawerShownPosition;
        private Font _font;
        private int _usedEntryCount;
        private float _responsiveDrawerHeight;
        private float _defaultChartHeight;
        private float _currentChartHeight;
        private bool _isBound;
        private bool _isExpanded;
        private LayoutFocus _layoutFocus;
        private EntryVisual _editingEntry;

        public event Action<string, double> ValueEditRequested;
        public event Action<PawnSkillAddRequest> SkillAddRequested;
        public event Action<PawnSkillNameEditRequest>
            SkillNameEditRequested;
        public event Action<PawnSkillRegularEditRequest>
            SkillRegularEditRequested;
        public event Action<PawnSkillRemoveRequest>
            SkillRemoveRequested;
        public event Action QuickCheckRequested;
        public event Action<bool> ExpandedChanged;
        public event Action SummaryClicked;

        public bool IsExpanded => _isExpanded;

        public static PawnStatPanelWidget CreateRuntime(
            RectTransform canvasRect,
            Font font)
        {
            if (canvasRect == null)
                return null;

            var root = new GameObject(
                "PawnRightStatUi",
                typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(canvasRect, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            var widget = root.AddComponent<PawnStatPanelWidget>();
            widget.Build(rect, font);
            root.SetActive(false);
            return widget;
        }

        public void Bind(in PawnStatPanelData data)
        {
            _isBound = true;
            BindIdentity(data.DisplayName, data.Portrait);
            BindSummary(data.Resources);
            BindAxes(data.Axes);
            BindEntries(data.Entries);
            _skillPanel?.Bind(
                data.Skills,
                data.AvailableSkillOptions,
                data.CanAddSkills);
            if (_summaryPortraitButton != null)
                _summaryPortraitButton.interactable = true;
            gameObject.SetActive(true);
            ApplyExpandedState(false);
            RefreshLayout();
        }

        public void SetExpanded(bool expanded)
        {
            var value = _isBound && expanded;
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            ApplyExpandedState(true);
            ExpandedChanged?.Invoke(_isExpanded);
        }

        public void ToggleExpanded()
        {
            SetExpanded(!_isExpanded);
        }

        public void Clear()
        {
            _isBound = false;
            _isExpanded = false;
            _usedEntryCount = 0;
            KillDrawerTransition();
            KillSummaryHoverTween();
            KillLayoutTween();
            _radar?.KillAnimation();
            _skillPanel?.Clear();
            _layoutFocus = LayoutFocus.Default;
            RefreshSkillToggleVisual();
            CancelInlineEdit();

            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                entry.Button.onClick.RemoveAllListeners();
                entry.Root.SetActive(false);
            }

            for (var index = 0; index < _summaryVisuals.Count; index++)
                _summaryVisuals[index].Root.SetActive(false);

            if (_drawerRect != null)
                _drawerRect.gameObject.SetActive(false);
            if (_summaryRect != null)
            {
                _summaryRect.localScale =
                    Vector3.one * SummaryRestScale;
                _summaryRect.gameObject.SetActive(false);
            }
            if (_summaryPortraitButton != null)
                _summaryPortraitButton.interactable = false;

            gameObject.SetActive(false);
        }

        public void RefreshResponsiveLayout()
        {
            if (_isBound)
                RefreshLayout();
        }

        private void Build(RectTransform rect, Font font)
        {
            _rootRect = rect;
            _font = font;
            BuildSummary();
            BuildDrawer();
        }

        private void BuildSummary()
        {
            var summary = new GameObject(
                "CharacterSummary",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _summaryRect = summary.GetComponent<RectTransform>();
            _summaryRect.SetParent(_rootRect, false);
            _summaryRect.anchorMax = Vector2.one;
            _summaryRect.anchorMin = Vector2.one;
            _summaryRect.pivot = Vector2.one;
            _summaryRect.anchoredPosition = new Vector2(-18f, -18f);
            _summaryRect.sizeDelta =
                new Vector2(SummaryWidth, SummaryHeight);
            _summaryRect.localScale =
                Vector3.one * SummaryRestScale;
            summary.GetComponent<Image>().color =
                new Color(0.025f, 0.075f, 0.095f, 0.97f);

            var portraitFrame = new GameObject(
                "PortraitFrame",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var portraitFrameRect =
                portraitFrame.GetComponent<RectTransform>();
            portraitFrameRect.SetParent(_summaryRect, false);
            portraitFrameRect.anchorMin = Vector2.up;
            portraitFrameRect.anchorMax = Vector2.up;
            portraitFrameRect.pivot = Vector2.up;
            portraitFrameRect.anchoredPosition =
                new Vector2(12f, -14f);
            portraitFrameRect.sizeDelta = new Vector2(132f, 132f);
            var portraitFrameImage =
                portraitFrame.GetComponent<Image>();
            portraitFrameImage.color =
                new Color(0.08f, 0.21f, 0.25f, 1f);
            _summaryPortraitButton =
                portraitFrame.GetComponent<Button>();
            _summaryPortraitButton.targetGraphic =
                portraitFrameImage;
            _summaryPortraitButton.onClick.AddListener(
                HandleSummaryClicked);

            var portraitObject = new GameObject(
                "Portrait",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var portraitRect =
                portraitObject.GetComponent<RectTransform>();
            portraitRect.SetParent(portraitFrameRect, false);
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(5f, 5f);
            portraitRect.offsetMax = new Vector2(-5f, -5f);
            _summaryPortraitImage =
                portraitObject.GetComponent<Image>();
            _summaryPortraitImage.preserveAspect = true;
            _summaryPortraitImage.color = Color.white;
            _summaryPortraitImage.raycastTarget = false;

            _summaryNameText = CreateText(
                "CharacterName",
                _summaryRect,
                18,
                TextAnchor.MiddleLeft);
            _summaryNameText.fontStyle = FontStyle.Bold;
            _summaryNameText.resizeTextForBestFit = true;
            _summaryNameText.resizeTextMinSize = 12;
            _summaryNameText.resizeTextMaxSize = 18;
            var nameRect = _summaryNameText.rectTransform;
            nameRect.anchorMin = Vector2.up;
            nameRect.anchorMax = Vector2.up;
            nameRect.pivot = Vector2.up;
            nameRect.anchoredPosition = new Vector2(158f, -12f);
            nameRect.sizeDelta = new Vector2(182f, 34f);

            _summaryVisuals.Add(
                CreateSummaryVisual(
                    "현재 체력",
                    new Color(0.92f, 0.25f, 0.24f, 0.95f),
                    -54f));
            _summaryVisuals.Add(
                CreateSummaryVisual(
                    "현재 이성",
                    new Color(0.18f, 0.68f, 0.90f, 0.95f),
                    -102f));

            var eventTrigger = summary.AddComponent<EventTrigger>();
            eventTrigger.triggers =
                new List<EventTrigger.Entry>(2);
            _summaryPointerEnterTrigger =
                new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerEnter
                };
            _summaryPointerEnterTrigger.callback.AddListener(
                HandleSummaryPointerEntered);
            eventTrigger.triggers.Add(
                _summaryPointerEnterTrigger);
            _summaryPointerExitTrigger =
                new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerExit
                };
            _summaryPointerExitTrigger.callback.AddListener(
                HandleSummaryPointerExited);
            eventTrigger.triggers.Add(
                _summaryPointerExitTrigger);
        }

        private SummaryVisual CreateSummaryVisual(
            string label,
            Color fillColor,
            float anchoredY)
        {
            var root = new GameObject(
                label,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            root.transform.SetParent(_summaryRect, false);
            root.GetComponent<Image>().color =
                new Color(0.04f, 0.11f, 0.14f, 0.98f);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.up;
            rootRect.anchorMax = Vector2.up;
            rootRect.pivot = Vector2.up;
            rootRect.anchoredPosition =
                new Vector2(158f, anchoredY);
            rootRect.sizeDelta = new Vector2(182f, 38f);

            var fillObject = new GameObject(
                "Fill",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var fillRect =
                fillObject.GetComponent<RectTransform>();
            fillRect.SetParent(rootRect, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(-4f, -4f);
            var fill = fillObject.GetComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;

            var labelText = CreateText(
                "Label",
                root.transform,
                12,
                TextAnchor.MiddleLeft);
            labelText.text = label;
            labelText.color = Color.white;
            var labelRect = labelText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 3f);
            labelRect.offsetMax = new Vector2(-72f, -3f);

            var valueText = CreateText(
                "Value",
                root.transform,
                13,
                TextAnchor.MiddleRight);
            valueText.fontStyle = FontStyle.Bold;
            var valueRect = valueText.rectTransform;
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = new Vector2(90f, 3f);
            valueRect.offsetMax = new Vector2(-10f, -3f);

            return new SummaryVisual
            {
                Root = root,
                Label = labelText,
                Value = valueText,
                Fill = fill
            };
        }

        private void BindIdentity(
            string displayName,
            Sprite portrait)
        {
            if (_summaryNameText != null)
            {
                _summaryNameText.text =
                    string.IsNullOrWhiteSpace(displayName)
                        ? "플레이어"
                        : displayName;
            }

            if (_summaryPortraitImage != null)
            {
                _summaryPortraitImage.sprite = portrait;
                _summaryPortraitImage.enabled = portrait != null;
            }
        }

        private void BuildDrawer()
        {
            var drawer = new GameObject(
                "StatDrawer",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            _drawerRect = drawer.GetComponent<RectTransform>();
            _drawerRect.SetParent(_rootRect, false);
            _drawerRect.anchorMin = Vector2.one;
            _drawerRect.anchorMax = Vector2.one;
            _drawerRect.pivot = Vector2.one;
            _drawerShownPosition =
                new Vector2(-18f, -DrawerTopOffset);
            _drawerRect.anchoredPosition = _drawerShownPosition;
            _drawerRect.sizeDelta =
                new Vector2(DrawerWidth, DrawerHeight);
            drawer.GetComponent<Image>().color =
                new Color(0.025f, 0.075f, 0.095f, 0.985f);
            _drawerCanvasGroup = drawer.GetComponent<CanvasGroup>();
            _drawerCanvasGroup.alpha = 0f;
            _drawerCanvasGroup.interactable = false;
            _drawerCanvasGroup.blocksRaycasts = false;

            var title = CreateText(
                "Title",
                _drawerRect,
                21,
                TextAnchor.MiddleLeft);
            title.text = "캐릭터 스탯";
            title.fontStyle = FontStyle.Bold;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(112f, -42f);
            titleRect.offsetMax = new Vector2(-180f, -6f);

            BuildCloseButton();
            BuildQuickCheckButton();
            BuildSkillToggle();
            BuildChart();
            BuildEntryList();
            _skillPanel = PawnSkillPanelWidget.CreateRuntime(
                _drawerRect,
                _font);
            if (_skillPanel != null)
            {
                _skillPanel.AddRequested +=
                    HandleSkillAddRequested;
                _skillPanel.NameEditRequested +=
                    HandleSkillNameEditRequested;
                _skillPanel.RegularEditRequested +=
                    HandleSkillRegularEditRequested;
                _skillPanel.RemoveRequested +=
                    HandleSkillRemoveRequested;
                _skillPanel.PointerEntered +=
                    HandleSkillPointerEntered;
                _skillPanel.PointerExited +=
                    HandleSkillPointerExited;
            }
            drawer.SetActive(false);
        }

        private void BuildCloseButton()
        {
            var root = new GameObject(
                "CloseButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(_drawerRect, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-10f, -8f);
            rect.sizeDelta = new Vector2(34f, 32f);

            var image = root.GetComponent<Image>();
            image.color = new Color(0.20f, 0.08f, 0.09f, 0.98f);
            _closeButton = root.GetComponent<Button>();
            _closeButton.targetGraphic = image;
            _closeButton.onClick.AddListener(HandleCloseClicked);

            var colors = _closeButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.72f, 0.72f, 1f);
            colors.pressedColor = new Color(0.84f, 0.45f, 0.45f, 1f);
            colors.selectedColor = colors.highlightedColor;
            _closeButton.colors = colors;

            var label = CreateText(
                "Label",
                rect,
                22,
                TextAnchor.MiddleCenter);
            label.text = "×";
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        private void BuildQuickCheckButton()
        {
            var root = new GameObject(
                "QuickCheckButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(_drawerRect, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-52f, -8f);
            rect.sizeDelta = new Vector2(116f, 32f);

            var image = root.GetComponent<Image>();
            image.color = new Color(0.08f, 0.28f, 0.36f, 0.98f);
            _quickCheckButton = root.GetComponent<Button>();
            _quickCheckButton.targetGraphic = image;
            _quickCheckButton.onClick.AddListener(
                HandleQuickCheckClicked);

            var colors = _quickCheckButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color(0.72f, 0.95f, 1f, 1f);
            colors.pressedColor =
                new Color(0.44f, 0.76f, 0.84f, 1f);
            colors.selectedColor = colors.highlightedColor;
            _quickCheckButton.colors = colors;

            var label = CreateText(
                "Label",
                rect,
                14,
                TextAnchor.MiddleCenter);
            label.text = "행위 굴림";
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        private void BuildSkillToggle()
        {
            var root = new GameObject(
                "SkillToggleButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(_drawerRect, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(14f, -8f);
            rect.sizeDelta = new Vector2(88f, 32f);

            _skillToggleImage = root.GetComponent<Image>();
            _skillToggleButton = root.GetComponent<Button>();
            _skillToggleButton.targetGraphic = _skillToggleImage;
            _skillToggleButton.onClick.AddListener(
                HandleSkillToggleClicked);

            var label = CreateText(
                "Label",
                rect,
                13,
                TextAnchor.MiddleCenter);
            label.text = "스킬";
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            RefreshSkillToggleVisual();
        }

        private void BuildChart()
        {
            var chart = new GameObject(
                "OctagonalStatChart",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _chartRect = chart.GetComponent<RectTransform>();
            _chartRect.SetParent(_drawerRect, false);
            _chartRect.anchorMin = new Vector2(0f, 1f);
            _chartRect.anchorMax = new Vector2(1f, 1f);
            _chartRect.pivot = new Vector2(0.5f, 1f);
            _chartRect.anchoredPosition = new Vector2(0f, -48f);
            _chartRect.sizeDelta = new Vector2(-24f, 310f);
            chart.GetComponent<Image>().color =
                new Color(1f, 1f, 1f, 0.018f);
            var chartTrigger = chart.AddComponent<EventTrigger>();
            chartTrigger.triggers =
                new List<EventTrigger.Entry>(2);
            _chartPointerEnterTrigger =
                new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerEnter
                };
            _chartPointerEnterTrigger.callback.AddListener(
                HandleChartPointerEntered);
            chartTrigger.triggers.Add(_chartPointerEnterTrigger);
            _chartPointerExitTrigger =
                new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerExit
                };
            _chartPointerExitTrigger.callback.AddListener(
                HandleChartPointerExited);
            chartTrigger.triggers.Add(_chartPointerExitTrigger);

            var radarObject = new GameObject(
                "Radar",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(PawnRadarGraphic));
            _radarRect = radarObject.GetComponent<RectTransform>();
            _radarRect.SetParent(_chartRect, false);
            _radarRect.anchorMin = new Vector2(0.5f, 0.5f);
            _radarRect.anchorMax = new Vector2(0.5f, 0.5f);
            _radarRect.pivot = new Vector2(0.5f, 0.5f);
            _radarRect.anchoredPosition = new Vector2(0f, -2f);
            _radarRect.sizeDelta = new Vector2(224f, 224f);
            _radar = radarObject.GetComponent<PawnRadarGraphic>();
            _radar.raycastTarget = false;

            for (var index = 0; index < AxisCount; index++)
            {
                var label = CreateText(
                    $"Axis{index + 1}",
                    _chartRect,
                    13,
                    TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = 13;
                label.rectTransform.sizeDelta =
                    new Vector2(88f, 44f);
                _axisLabels.Add(label);
            }
        }

        private void BuildEntryList()
        {
            BuildEntryHeader();

            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D));
            _entryViewportRect =
                viewportObject.GetComponent<RectTransform>();
            _entryViewportRect.SetParent(_drawerRect, false);
            _entryViewportRect.anchorMin = Vector2.zero;
            _entryViewportRect.anchorMax = Vector2.one;
            _entryViewportRect.offsetMin = new Vector2(14f, 14f);
            _entryViewportRect.offsetMax = new Vector2(
                -14f,
                -(366f + EntryHeaderHeight + 6f));
            viewportObject.GetComponent<Image>().color =
                new Color(1f, 1f, 1f, 0.018f);
            var entryTrigger =
                viewportObject.AddComponent<EventTrigger>();
            entryTrigger.triggers =
                new List<EventTrigger.Entry>(2);
            _entryPointerEnterTrigger =
                new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerEnter
                };
            _entryPointerEnterTrigger.callback.AddListener(
                HandleEntryPointerEntered);
            entryTrigger.triggers.Add(_entryPointerEnterTrigger);
            _entryPointerExitTrigger =
                new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerExit
                };
            _entryPointerExitTrigger.callback.AddListener(
                HandleEntryPointerExited);
            entryTrigger.triggers.Add(_entryPointerExitTrigger);

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(GridLayoutGroup));
            _content = contentObject.GetComponent<RectTransform>();
            _content.SetParent(_entryViewportRect, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            _grid = contentObject.GetComponent<GridLayoutGroup>();
            _grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            _grid.childAlignment = TextAnchor.UpperLeft;
            _grid.spacing = new Vector2(EntrySpacing, EntrySpacing);
            _grid.constraint =
                GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 1;

            _scrollRect = _drawerRect.gameObject.AddComponent<ScrollRect>();
            _scrollRect.viewport = _entryViewportRect;
            _scrollRect.content = _content;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 22f;
        }

        private void BuildEntryHeader()
        {
            var root = new GameObject(
                "StatEntryHeader",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _entryHeaderRect = root.GetComponent<RectTransform>();
            _entryHeaderRect.SetParent(_drawerRect, false);
            _entryHeaderRect.anchorMin = new Vector2(0f, 1f);
            _entryHeaderRect.anchorMax = new Vector2(1f, 1f);
            _entryHeaderRect.pivot = new Vector2(0.5f, 1f);
            _entryHeaderRect.anchoredPosition =
                new Vector2(0f, -366f);
            _entryHeaderRect.sizeDelta =
                new Vector2(-28f, EntryHeaderHeight);
            root.GetComponent<Image>().color =
                new Color(0.04f, 0.11f, 0.13f, 0.99f);

            CreateEntryColumnText(
                "Stat",
                _entryHeaderRect,
                "스탯",
                0f,
                0.34f,
                TextAnchor.MiddleLeft,
                true);
            CreateEntryColumnText(
                "Regular",
                _entryHeaderRect,
                "보통",
                0.34f,
                0.56f,
                TextAnchor.MiddleCenter,
                true);
            CreateEntryColumnText(
                "Hard",
                _entryHeaderRect,
                "어려움",
                0.56f,
                0.78f,
                TextAnchor.MiddleCenter,
                true);
            CreateEntryColumnText(
                "Extreme",
                _entryHeaderRect,
                "극단",
                0.78f,
                1f,
                TextAnchor.MiddleCenter,
                true);
        }

        private void BindSummary(
            IReadOnlyList<PawnResourceValueData> resources)
        {
            PawnResourceValueData health = default;
            PawnResourceValueData sanity = default;
            var hasHealth = false;
            var hasSanity = false;

            var count = resources != null ? resources.Count : 0;
            for (var index = 0; index < count; index++)
            {
                var value = resources[index];
                if (!value.IsVisible)
                    continue;

                if (string.Equals(
                        value.Label,
                        "현재 체력",
                        StringComparison.Ordinal))
                {
                    health = value;
                    hasHealth = true;
                }
                else if (string.Equals(
                             value.Label,
                             "현재 이성",
                             StringComparison.Ordinal))
                {
                    sanity = value;
                    hasSanity = true;
                }
            }

            BindSummaryVisual(
                _summaryVisuals[0],
                health,
                hasHealth);
            BindSummaryVisual(
                _summaryVisuals[1],
                sanity,
                hasSanity);
        }

        private static void BindSummaryVisual(
            SummaryVisual visual,
            PawnResourceValueData data,
            bool visible)
        {
            visual.Root.SetActive(visible);
            if (!visible)
                return;

            visual.Label.text = data.Label;
            visual.Value.text = data.FormatValue();
            if (visual.Fill != null)
            {
                var ratio = data.Maximum > 0.0001d
                    ? Mathf.Clamp01(
                        (float)(data.Current / data.Maximum))
                    : 0f;
                var fillRect = visual.Fill.rectTransform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(ratio, 1f);
                fillRect.offsetMin = new Vector2(4f, 4f);
                fillRect.offsetMax = new Vector2(
                    Mathf.Lerp(4f, -4f, ratio),
                    -4f);
            }
        }

        private void BindAxes(
            IReadOnlyList<PawnStatAxisData> axes)
        {
            var count = axes != null
                ? Mathf.Min(AxisCount, axes.Count)
                : 0;

            for (var index = 0; index < AxisCount; index++)
            {
                if (index < count)
                {
                    var axis = axes[index];
                    _radarValues[index] = axis.NormalizedValue;
                    _axisLabels[index].text = axis.FormatLabel();
                    _axisLabels[index].gameObject.SetActive(true);
                }
                else
                {
                    _radarValues[index] = 0f;
                    _axisLabels[index].text = "—";
                    _axisLabels[index].gameObject.SetActive(true);
                }
            }

            if (_isExpanded && _drawerRect.gameObject.activeSelf)
            {
                _radar.TweenTo(
                    _radarValues,
                    RadarTrackingDuration);
            }
            else
            {
                _radar.SetValuesImmediate(_radarValues);
            }
        }

        private void BindEntries(
            IReadOnlyList<PawnStatEntryData> entries)
        {
            CancelInlineEdit();
            _usedEntryCount = 0;
            var count = entries != null ? entries.Count : 0;
            for (var index = 0; index < count; index++)
                BindEntry(GetEntry(), entries[index]);

            for (var index = _usedEntryCount;
                 index < _entries.Count;
                 index++)
            {
                _entries[index].Root.SetActive(false);
            }

            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 1f;
        }

        private EntryVisual GetEntry()
        {
            EntryVisual entry;
            if (_usedEntryCount < _entries.Count)
            {
                entry = _entries[_usedEntryCount];
            }
            else
            {
                entry = CreateEntry();
                _entries.Add(entry);
            }

            _usedEntryCount++;
            entry.Root.SetActive(true);
            return entry;
        }

        private EntryVisual CreateEntry()
        {
            var root = new GameObject(
                "StatEntry",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            root.transform.SetParent(_content, false);
            var background = root.GetComponent<Image>();
            background.color =
                new Color(0.07f, 0.16f, 0.19f, 0.98f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = background;

            var nameText = CreateEntryColumnText(
                "Name",
                root.transform,
                string.Empty,
                0f,
                0.34f,
                TextAnchor.MiddleLeft,
                false);
            var regularText = CreateEntryColumnText(
                "Regular",
                root.transform,
                string.Empty,
                0.34f,
                0.56f,
                TextAnchor.MiddleCenter,
                false);
            var hardText = CreateEntryColumnText(
                "Hard",
                root.transform,
                string.Empty,
                0.56f,
                0.78f,
                TextAnchor.MiddleCenter,
                false);
            var extremeText = CreateEntryColumnText(
                "Extreme",
                root.transform,
                string.Empty,
                0.78f,
                1f,
                TextAnchor.MiddleCenter,
                false);
            var regularInput = CreateInlineStatInput(root.transform);

            return new EntryVisual
            {
                Root = root,
                Button = button,
                NameText = nameText,
                RegularText = regularText,
                HardText = hardText,
                ExtremeText = extremeText,
                RegularInput = regularInput
            };
        }

        private InputField CreateInlineStatInput(Transform parent)
        {
            var root = new GameObject(
                "RegularInput",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(InputField));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.34f, 0f);
            rect.anchorMax = new Vector2(0.56f, 1f);
            rect.offsetMin = new Vector2(3f, 4f);
            rect.offsetMax = new Vector2(-3f, -4f);

            var image = root.GetComponent<Image>();
            image.color = new Color(0.03f, 0.29f, 0.35f, 1f);

            var valueText = CreateText(
                "Text",
                rect,
                15,
                TextAnchor.MiddleCenter);
            valueText.resizeTextForBestFit = true;
            valueText.resizeTextMinSize = 11;
            valueText.resizeTextMaxSize = 15;
            valueText.rectTransform.anchorMin = Vector2.zero;
            valueText.rectTransform.anchorMax = Vector2.one;
            valueText.rectTransform.offsetMin = new Vector2(3f, 1f);
            valueText.rectTransform.offsetMax = new Vector2(-3f, -1f);

            var input = root.GetComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = valueText;
            input.contentType = InputField.ContentType.DecimalNumber;
            input.lineType = InputField.LineType.SingleLine;
            root.SetActive(false);
            return input;
        }

        private Text CreateEntryColumnText(
            string objectName,
            Transform parent,
            string value,
            float anchorMinX,
            float anchorMaxX,
            TextAnchor alignment,
            bool isHeader)
        {
            var text = CreateText(
                objectName,
                parent,
                isHeader ? 12 : 15,
                alignment);
            text.text = value;
            text.fontStyle = isHeader
                ? FontStyle.Bold
                : FontStyle.Normal;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = isHeader ? 10 : 12;
            text.resizeTextMaxSize = isHeader ? 12 : 15;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.offsetMin = new Vector2(
                anchorMinX <= 0.001f ? 10f : 4f,
                3f);
            rect.offsetMax = new Vector2(-4f, -3f);
            return text;
        }

        private void BindEntry(
            EntryVisual visual,
            PawnStatEntryData data)
        {
            visual.Data = data;
            visual.NameText.text = data.DisplayName ?? string.Empty;
            visual.RegularText.text = data.FormatRegular();
            visual.HardText.text = data.FormatHard();
            visual.ExtremeText.text = data.FormatExtreme();

            var difficultyColor = data.ShowsDifficulty
                ? new Color(0.72f, 0.92f, 1f, 1f)
                : new Color(0.58f, 0.66f, 0.69f, 1f);
            visual.RegularText.color = difficultyColor;
            visual.HardText.color = difficultyColor;
            visual.ExtremeText.color = difficultyColor;

            visual.Button.onClick.RemoveAllListeners();
            visual.RegularInput.onEndEdit.RemoveAllListeners();
            visual.RegularInput.gameObject.SetActive(false);
            visual.RegularText.gameObject.SetActive(true);
            visual.Button.interactable = data.CanEdit;
            var image = visual.Button.targetGraphic as Image;
            if (image != null)
            {
                image.color = data.CanEdit
                    ? new Color(0.06f, 0.20f, 0.24f, 0.99f)
                    : new Color(0.07f, 0.16f, 0.19f, 0.98f);
            }

            if (data.CanEdit)
            {
                visual.Button.onClick.AddListener(
                    () => BeginInlineEdit(visual));
            }
        }

        private void BeginInlineEdit(EntryVisual visual)
        {
            if (visual == null || !visual.Data.CanEdit)
                return;

            if (_editingEntry == visual)
            {
                FocusAndSelectAll(visual.RegularInput);
                return;
            }

            if (_editingEntry != null && _editingEntry != visual)
                CompleteInlineEdit(
                    _editingEntry,
                    _editingEntry.RegularInput.text);

            _editingEntry = visual;
            var input = visual.RegularInput;
            input.onEndEdit.RemoveAllListeners();
            input.SetTextWithoutNotify(
                PawnResourceValueData.FormatNumber(
                    visual.Data.EditableValue));
            visual.RegularText.gameObject.SetActive(false);
            input.gameObject.SetActive(true);
            input.onEndEdit.AddListener(
                text => CompleteInlineEdit(visual, text));
            FocusAndSelectAll(input);
        }

        private static void FocusAndSelectAll(InputField input)
        {
            if (input == null)
                return;

            input.Select();
            input.ActivateInputField();
            input.MoveTextStart(false);
            input.MoveTextEnd(true);
        }

        private void CompleteInlineEdit(
            EntryVisual visual,
            string text)
        {
            if (visual == null || _editingEntry != visual)
                return;

            var input = visual.RegularInput;
            var wasCanceled = input.wasCanceled;
            input.onEndEdit.RemoveAllListeners();
            input.gameObject.SetActive(false);
            visual.RegularText.gameObject.SetActive(true);
            _editingEntry = null;

            if (wasCanceled ||
                !TryParseModifier(text, out var desiredFinalValue))
            {
                return;
            }

            var data = visual.Data;
            desiredFinalValue = Math.Max(
                data.MinimumValue,
                Math.Min(data.MaximumValue, desiredFinalValue));
            var manualModifier =
                desiredFinalValue -
                data.BaseValue -
                data.OtherModifier;
            ValueEditRequested?.Invoke(
                data.StatId,
                manualModifier);
        }

        private void CancelInlineEdit()
        {
            if (_editingEntry == null)
                return;

            var input = _editingEntry.RegularInput;
            input.onEndEdit.RemoveAllListeners();
            input.gameObject.SetActive(false);
            _editingEntry.RegularText.gameObject.SetActive(true);
            _editingEntry = null;
        }

        private static bool TryParseModifier(
            string text,
            out double value)
        {
            const NumberStyles styles = NumberStyles.Float;
            return double.TryParse(
                       text,
                       styles,
                       CultureInfo.InvariantCulture,
                       out value) ||
                   double.TryParse(
                       text,
                       styles,
                       CultureInfo.CurrentCulture,
                       out value);
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            TextAnchor alignment)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void ApplyExpandedState(bool animate)
        {
            if (_summaryRect != null)
                _summaryRect.gameObject.SetActive(_isBound);

            if (_drawerRect == null || _drawerCanvasGroup == null)
                return;

            var showDrawer = _isBound && _isExpanded;
            if (showDrawer)
            {
                ShowDrawer(animate);
                return;
            }

            _skillPanel?.SetExpanded(false, animate);
            _layoutFocus = LayoutFocus.Default;
            KillLayoutTween();
            ApplyFocusedLayout(false);
            CancelInlineEdit();
            RefreshSkillToggleVisual();
            HideDrawer(animate);
        }

        private void HandleSkillToggleClicked()
        {
            if (!_isBound || !_isExpanded || _skillPanel == null)
                return;

            CancelInlineEdit();
            _skillPanel.ToggleExpanded();
            if (!_skillPanel.IsExpanded)
                SetLayoutFocus(LayoutFocus.Default);
            RefreshSkillToggleVisual();
        }

        private void HandleCloseClicked()
        {
            SetExpanded(false);
        }

        private void HandleQuickCheckClicked()
        {
            if (!_isBound || !_isExpanded)
                return;

            SetExpanded(false);
            QuickCheckRequested?.Invoke();
        }

        private void HandleSkillAddRequested(
            PawnSkillAddRequest request)
        {
            SkillAddRequested?.Invoke(request);
        }

        private void HandleSkillNameEditRequested(
            PawnSkillNameEditRequest request)
        {
            SkillNameEditRequested?.Invoke(request);
        }

        private void HandleSkillRegularEditRequested(
            PawnSkillRegularEditRequest request)
        {
            SkillRegularEditRequested?.Invoke(request);
        }

        private void HandleSkillRemoveRequested(
            PawnSkillRemoveRequest request)
        {
            SkillRemoveRequested?.Invoke(request);
        }

        private void HandleSkillPointerEntered()
        {
            SetLayoutFocus(LayoutFocus.Details);
        }

        private void HandleSkillPointerExited()
        {
            SetLayoutFocus(LayoutFocus.Default);
        }

        private void RefreshSkillToggleVisual()
        {
            if (_skillToggleImage == null)
                return;

            _skillToggleImage.color =
                _skillPanel != null && _skillPanel.IsExpanded
                    ? new Color(0.08f, 0.48f, 0.60f, 0.99f)
                    : new Color(0.07f, 0.20f, 0.24f, 0.98f);
        }

        private void ShowDrawer(bool animate)
        {
            KillDrawerTransition();
            _drawerRect.gameObject.SetActive(true);
            _drawerCanvasGroup.interactable = true;
            _drawerCanvasGroup.blocksRaycasts = true;

            if (!animate)
            {
                _drawerRect.anchoredPosition = _drawerShownPosition;
                _drawerRect.localScale = Vector3.one;
                _drawerCanvasGroup.alpha = 1f;
                return;
            }

            _drawerRect.anchoredPosition =
                _drawerShownPosition +
                Vector2.right * DrawerHiddenOffset;
            _drawerRect.localScale =
                new Vector3(0.97f, 0.97f, 1f);
            _drawerCanvasGroup.alpha = 0f;
            _radar?.AnimateFromZero(
                _radarValues,
                RadarRevealDuration);
            _drawerTransition = DOTween.Sequence()
                .Join(DOTween.To(
                        () => _drawerRect.anchoredPosition,
                        value =>
                            _drawerRect.anchoredPosition = value,
                        _drawerShownPosition,
                        DrawerTransitionDuration)
                    .SetEase(Ease.OutCubic))
                .Join(DOTween.To(
                        () => _drawerRect.localScale,
                        value => _drawerRect.localScale = value,
                        Vector3.one,
                        DrawerTransitionDuration)
                    .SetEase(Ease.OutCubic))
                .Join(DOTween.To(
                    () => _drawerCanvasGroup.alpha,
                    value => _drawerCanvasGroup.alpha = value,
                    1f,
                    DrawerTransitionDuration))
                .SetUpdate(true);
        }

        private void HideDrawer(bool animate)
        {
            KillDrawerTransition();
            _drawerCanvasGroup.interactable = false;
            _drawerCanvasGroup.blocksRaycasts = false;

            if (!_drawerRect.gameObject.activeSelf)
                return;

            if (!animate)
            {
                _drawerCanvasGroup.alpha = 0f;
                _drawerRect.anchoredPosition =
                    _drawerShownPosition +
                    Vector2.right * DrawerHiddenOffset;
                _drawerRect.localScale = Vector3.one;
                _drawerRect.gameObject.SetActive(false);
                return;
            }

            _drawerTransition = DOTween.Sequence()
                .Join(DOTween.To(
                        () => _drawerRect.anchoredPosition,
                        value =>
                            _drawerRect.anchoredPosition = value,
                        _drawerShownPosition +
                        Vector2.right * DrawerHiddenOffset,
                        DrawerTransitionDuration * 0.75f)
                    .SetEase(Ease.InCubic))
                .Join(DOTween.To(
                    () => _drawerCanvasGroup.alpha,
                    value => _drawerCanvasGroup.alpha = value,
                    0f,
                    DrawerTransitionDuration * 0.75f))
                .OnComplete(() =>
                {
                    if (_drawerRect != null)
                        _drawerRect.gameObject.SetActive(false);
                })
                .SetUpdate(true);
        }

        private void KillDrawerTransition()
        {
            _drawerTransition?.Kill();
            _drawerTransition = null;
        }

        private void HandleSummaryPointerEntered(
            BaseEventData eventData)
        {
            TweenSummaryScale(SummaryHoverScale);
        }

        private void HandleSummaryPointerExited(
            BaseEventData eventData)
        {
            TweenSummaryScale(SummaryRestScale);
        }

        private void HandleSummaryClicked()
        {
            if (_isBound)
                SummaryClicked?.Invoke();
        }

        private void TweenSummaryScale(float targetScale)
        {
            if (_summaryRect == null || !_isBound)
                return;

            KillSummaryHoverTween();
            _summaryHoverTween = _summaryRect
                .DOScale(
                    Vector3.one * targetScale,
                    SummaryHoverDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        private void KillSummaryHoverTween()
        {
            _summaryHoverTween?.Kill();
            _summaryHoverTween = null;
        }

        private void HandleChartPointerEntered(BaseEventData eventData)
        {
            SetLayoutFocus(LayoutFocus.Graph);
        }

        private void HandleChartPointerExited(BaseEventData eventData)
        {
            SetLayoutFocus(LayoutFocus.Default);
        }

        private void HandleEntryPointerEntered(BaseEventData eventData)
        {
            SetLayoutFocus(LayoutFocus.Details);
        }

        private void HandleEntryPointerExited(BaseEventData eventData)
        {
            SetLayoutFocus(LayoutFocus.Default);
        }

        private void SetLayoutFocus(LayoutFocus focus)
        {
            if (!_isBound || !_isExpanded || _layoutFocus == focus)
                return;

            _layoutFocus = focus;
            ApplyFocusedLayout(true);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_isBound)
                RefreshLayout();
        }

        private void RefreshLayout()
        {
            if (_rootRect == null ||
                _drawerRect == null ||
                _content == null)
            {
                return;
            }

            var availableWidth = Mathf.Max(320f, _rootRect.rect.width);
            var availableHeight = Mathf.Max(420f, _rootRect.rect.height);
            var drawerWidth = Mathf.Min(
                DrawerWidth,
                Mathf.Max(380f, availableWidth * 0.42f));
            var drawerHeight = Mathf.Min(
                DrawerHeight,
                Mathf.Max(420f, availableHeight - DrawerTopOffset - 18f));
            _drawerRect.sizeDelta =
                new Vector2(drawerWidth, drawerHeight);
            var skillPanelWidth = Mathf.Min(
                372f,
                Mathf.Max(
                    280f,
                    availableWidth - drawerWidth - 60f));
            _skillPanel?.SetLayout(
                skillPanelWidth,
                drawerHeight);

            var listWidth = drawerWidth - 28f;
            _grid.constraintCount = 1;
            var rows = Mathf.Max(
                1,
                _usedEntryCount);
            _grid.spacing =
                new Vector2(EntrySpacing, EntrySpacing);
            _grid.cellSize =
                new Vector2(listWidth, EntryHeight);
            var requiredListHeight =
                rows * EntryHeight +
                Mathf.Max(0, rows - 1) * EntrySpacing;
            _content.sizeDelta =
                new Vector2(0f, requiredListHeight);

            var chartHeightForFullList =
                drawerHeight -
                requiredListHeight -
                ChartTop -
                ChartGap -
                EntryHeaderHeight -
                ViewportGap -
                ViewportBottom;
            _responsiveDrawerHeight = drawerHeight;
            _defaultChartHeight = Mathf.Clamp(
                chartHeightForFullList,
                DefaultChartMinHeight,
                DefaultChartMaxHeight);
            ApplyFocusedLayout(false);
        }

        private void ApplyFocusedLayout(bool animate)
        {
            if (_chartRect == null ||
                _entryHeaderRect == null ||
                _entryViewportRect == null)
            {
                return;
            }

            var maximumChartHeight =
                _responsiveDrawerHeight -
                ChartTop -
                ChartGap -
                EntryHeaderHeight -
                ViewportGap -
                ViewportBottom -
                MinimumEntryViewportHeight;
            maximumChartHeight = Mathf.Max(
                DetailFocusChartHeight,
                maximumChartHeight);

            float preferredHeight;
            switch (_layoutFocus)
            {
                case LayoutFocus.Graph:
                    preferredHeight = GraphFocusChartHeight;
                    break;
                case LayoutFocus.Details:
                    preferredHeight = DetailFocusChartHeight;
                    break;
                default:
                    preferredHeight = _defaultChartHeight;
                    break;
            }

            var targetHeight = Mathf.Clamp(
                preferredHeight,
                DetailFocusChartHeight,
                maximumChartHeight);
            KillLayoutTween();
            if (!animate ||
                !gameObject.activeInHierarchy ||
                Mathf.Abs(_currentChartHeight - targetHeight) < 0.5f)
            {
                ApplyChartHeight(targetHeight);
                return;
            }

            var startHeight = _currentChartHeight > 0f
                ? _currentChartHeight
                : targetHeight;
            _layoutTween = DOTween.To(
                    () => startHeight,
                    ApplyChartHeight,
                    targetHeight,
                    LayoutFocusDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => _layoutTween = null);
        }

        private void ApplyChartHeight(float chartHeight)
        {
            _currentChartHeight = chartHeight;
            _chartRect.gameObject.SetActive(true);
            _chartRect.sizeDelta =
                new Vector2(-24f, chartHeight);

            if (_radarRect != null)
            {
                var radarSize = Mathf.Clamp(
                    chartHeight - RadarVerticalPadding,
                    RadarMinSize,
                    RadarMaxSize);
                _radarRect.sizeDelta =
                    new Vector2(radarSize, radarSize);
            }

            var entryHeaderTop =
                ChartTop + chartHeight + ChartGap;
            _entryHeaderRect.anchoredPosition =
                new Vector2(0f, -entryHeaderTop);
            _entryViewportRect.offsetMin =
                new Vector2(14f, ViewportBottom);
            _entryViewportRect.offsetMax =
                new Vector2(
                    -14f,
                    -(entryHeaderTop +
                      EntryHeaderHeight +
                      ViewportGap));
            PositionAxisLabels();
        }

        private void KillLayoutTween()
        {
            _layoutTween?.Kill();
            _layoutTween = null;
        }

        private void PositionAxisLabels()
        {
            if (_chartRect == null || _axisLabels.Count < AxisCount)
                return;

            var labelRadius = Mathf.Clamp(
                (_chartRect.rect.height - RadarVerticalPadding) * 0.5f,
                AxisLabelMinRadius,
                AxisLabelMaxRadius);
            for (var index = 0; index < AxisCount; index++)
            {
                var angle =
                    (90f - index * 360f / AxisCount) *
                    Mathf.Deg2Rad;
                _axisLabels[index].rectTransform.anchoredPosition =
                    new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)) *
                    labelRadius;
            }
        }

        private void OnDestroy()
        {
            KillDrawerTransition();
            KillSummaryHoverTween();
            KillLayoutTween();
            _radar?.KillAnimation();
            if (_summaryPortraitButton != null)
            {
                _summaryPortraitButton.onClick.RemoveListener(
                    HandleSummaryClicked);
            }
            if (_skillToggleButton != null)
            {
                _skillToggleButton.onClick.RemoveListener(
                    HandleSkillToggleClicked);
            }
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(
                    HandleCloseClicked);
            }
            if (_quickCheckButton != null)
            {
                _quickCheckButton.onClick.RemoveListener(
                    HandleQuickCheckClicked);
            }
            if (_skillPanel != null)
            {
                _skillPanel.AddRequested -=
                    HandleSkillAddRequested;
                _skillPanel.NameEditRequested -=
                    HandleSkillNameEditRequested;
                _skillPanel.RegularEditRequested -=
                    HandleSkillRegularEditRequested;
                _skillPanel.RemoveRequested -=
                    HandleSkillRemoveRequested;
                _skillPanel.PointerEntered -=
                    HandleSkillPointerEntered;
                _skillPanel.PointerExited -=
                    HandleSkillPointerExited;
            }
            if (_summaryPointerEnterTrigger != null)
            {
                _summaryPointerEnterTrigger.callback.RemoveListener(
                    HandleSummaryPointerEntered);
            }
            if (_summaryPointerExitTrigger != null)
            {
                _summaryPointerExitTrigger.callback.RemoveListener(
                    HandleSummaryPointerExited);
            }
            if (_chartPointerEnterTrigger != null)
            {
                _chartPointerEnterTrigger.callback.RemoveListener(
                    HandleChartPointerEntered);
            }
            if (_chartPointerExitTrigger != null)
            {
                _chartPointerExitTrigger.callback.RemoveListener(
                    HandleChartPointerExited);
            }
            if (_entryPointerEnterTrigger != null)
            {
                _entryPointerEnterTrigger.callback.RemoveListener(
                    HandleEntryPointerEntered);
            }
            if (_entryPointerExitTrigger != null)
            {
                _entryPointerExitTrigger.callback.RemoveListener(
                    HandleEntryPointerExited);
            }
            _summaryPointerEnterTrigger = null;
            _summaryPointerExitTrigger = null;
            _chartPointerEnterTrigger = null;
            _chartPointerExitTrigger = null;
            _entryPointerEnterTrigger = null;
            _entryPointerExitTrigger = null;
            ValueEditRequested = null;
            SkillAddRequested = null;
            SkillNameEditRequested = null;
            SkillRegularEditRequested = null;
            SkillRemoveRequested = null;
            QuickCheckRequested = null;
            ExpandedChanged = null;
            SummaryClicked = null;
        }
    }

    internal sealed class PawnRadarGraphic : Graphic
    {
        private const int AxisCount = 8;
        private readonly float[] _values = new float[AxisCount];
        private readonly float[] _startValues = new float[AxisCount];
        private readonly float[] _targetValues = new float[AxisCount];
        private Tween _valueTween;

        [SerializeField] private Color _gridColor =
            new Color(0.28f, 0.58f, 0.64f, 0.42f);
        [SerializeField] private Color _fillColor =
            new Color(0.10f, 0.74f, 0.90f, 0.30f);
        [SerializeField] private Color _outlineColor =
            new Color(0.26f, 0.92f, 1f, 0.95f);
        [SerializeField, Min(0.5f)] private float _lineWidth = 1.5f;

        public void SetValuesImmediate(IReadOnlyList<float> values)
        {
            KillAnimation();
            for (var index = 0; index < AxisCount; index++)
            {
                _values[index] =
                    values != null && index < values.Count
                        ? Mathf.Clamp01(values[index])
                        : 0f;
            }

            SetVerticesDirty();
        }

        public void AnimateFromZero(
            IReadOnlyList<float> values,
            float duration)
        {
            KillAnimation();
            CopyValues(values, _targetValues);
            for (var index = 0; index < AxisCount; index++)
            {
                _startValues[index] = 0f;
                _values[index] = 0f;
            }

            StartTween(duration, Ease.OutCubic);
        }

        public void TweenTo(
            IReadOnlyList<float> values,
            float duration)
        {
            KillAnimation();
            CopyValues(_values, _startValues);
            CopyValues(values, _targetValues);
            StartTween(duration, Ease.OutCubic);
        }

        public void KillAnimation()
        {
            _valueTween?.Kill();
            _valueTween = null;
        }

        private void StartTween(float duration, Ease ease)
        {
            if (duration <= 0f)
            {
                CopyValues(_targetValues, _values);
                SetVerticesDirty();
                return;
            }

            var progress = 0f;
            _valueTween = DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        for (var index = 0;
                             index < AxisCount;
                             index++)
                        {
                            _values[index] = Mathf.Lerp(
                                _startValues[index],
                                _targetValues[index],
                                progress);
                        }
                        SetVerticesDirty();
                    },
                    1f,
                    duration)
                .SetEase(ease)
                .SetUpdate(true);
        }

        private static void CopyValues(
            IReadOnlyList<float> source,
            float[] destination)
        {
            for (var index = 0; index < AxisCount; index++)
            {
                destination[index] =
                    source != null && index < source.Count
                        ? Mathf.Clamp01(source[index])
                        : 0f;
            }
        }

        protected override void OnDisable()
        {
            KillAnimation();
            base.OnDisable();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            var center = rect.center;
            var radius =
                Mathf.Min(rect.width, rect.height) * 0.46f;
            AddFilledPolygon(
                vertexHelper,
                center,
                radius,
                _values,
                _fillColor);

            for (var ring = 1; ring <= 4; ring++)
            {
                AddRing(
                    vertexHelper,
                    center,
                    radius * ring / 4f,
                    _gridColor,
                    _lineWidth);
            }

            for (var index = 0; index < AxisCount; index++)
            {
                AddLine(
                    vertexHelper,
                    center,
                    center + Direction(index) * radius,
                    _gridColor,
                    _lineWidth);
            }

            var polygon = new Vector2[AxisCount];
            for (var index = 0; index < AxisCount; index++)
            {
                polygon[index] =
                    center +
                    Direction(index) *
                    radius *
                    Mathf.Clamp01(_values[index]);
            }

            AddPolyline(
                vertexHelper,
                polygon,
                _outlineColor,
                Mathf.Max(2f, _lineWidth),
                true);
        }

        private static Vector2 Direction(int index)
        {
            var angle =
                (90f - index * 360f / AxisCount) *
                Mathf.Deg2Rad;
            return new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle));
        }

        private static void AddFilledPolygon(
            VertexHelper helper,
            Vector2 center,
            float radius,
            IReadOnlyList<float> values,
            Color color)
        {
            var start = helper.currentVertCount;
            helper.AddVert(center, color, Vector2.zero);
            for (var index = 0; index < AxisCount; index++)
            {
                helper.AddVert(
                    center +
                    Direction(index) *
                    radius *
                    Mathf.Clamp01(values[index]),
                    color,
                    Vector2.zero);
            }

            for (var index = 0; index < AxisCount; index++)
            {
                var next = (index + 1) % AxisCount;
                helper.AddTriangle(
                    start,
                    start + 1 + index,
                    start + 1 + next);
            }
        }

        private static void AddRing(
            VertexHelper helper,
            Vector2 center,
            float radius,
            Color color,
            float width)
        {
            var points = new Vector2[AxisCount];
            for (var index = 0; index < AxisCount; index++)
                points[index] = center + Direction(index) * radius;

            AddPolyline(helper, points, color, width, true);
        }

        private static void AddPolyline(
            VertexHelper helper,
            IReadOnlyList<Vector2> points,
            Color color,
            float width,
            bool closed)
        {
            if (points == null || points.Count < 2)
                return;

            var limit = closed ? points.Count : points.Count - 1;
            for (var index = 0; index < limit; index++)
            {
                var next = (index + 1) % points.Count;
                AddLine(
                    helper,
                    points[index],
                    points[next],
                    color,
                    width);
            }
        }

        private static void AddLine(
            VertexHelper helper,
            Vector2 start,
            Vector2 end,
            Color color,
            float width)
        {
            var direction = end - start;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            direction.Normalize();
            var normal =
                new Vector2(-direction.y, direction.x) *
                width *
                0.5f;
            var vertexStart = helper.currentVertCount;
            helper.AddVert(start - normal, color, Vector2.zero);
            helper.AddVert(start + normal, color, Vector2.zero);
            helper.AddVert(end + normal, color, Vector2.zero);
            helper.AddVert(end - normal, color, Vector2.zero);
            helper.AddTriangle(
                vertexStart,
                vertexStart + 1,
                vertexStart + 2);
            helper.AddTriangle(
                vertexStart,
                vertexStart + 2,
                vertexStart + 3);
        }
    }

    internal sealed class PawnCircleGraphic : Graphic
    {
        private const int SegmentCount = 40;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            var center = rect.center;
            var radius =
                Mathf.Min(rect.width, rect.height) * 0.5f;
            vertexHelper.AddVert(center, color, Vector2.zero);

            for (var index = 0; index <= SegmentCount; index++)
            {
                var angle =
                    index * Mathf.PI * 2f / SegmentCount;
                var point = center +
                    new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)) *
                    radius;
                vertexHelper.AddVert(point, color, Vector2.zero);
            }

            for (var index = 0; index < SegmentCount; index++)
            {
                vertexHelper.AddTriangle(
                    0,
                    index + 1,
                    index + 2);
            }
        }
    }
}
