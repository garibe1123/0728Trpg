using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public readonly struct PawnSkillValueData
    {
        public PawnSkillValueData(
            string skillId,
            string displayName,
            string category,
            int regular,
            int hard,
            int extreme,
            bool usesBaseValue,
            bool requiresTraining,
            int sortOrder)
        {
            SkillId = skillId;
            DisplayName = displayName;
            Category = category;
            Regular = regular;
            Hard = hard;
            Extreme = extreme;
            UsesBaseValue = usesBaseValue;
            RequiresTraining = requiresTraining;
            SortOrder = sortOrder;
        }

        public string SkillId { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public int Regular { get; }
        public int Hard { get; }
        public int Extreme { get; }
        public bool UsesBaseValue { get; }
        public bool RequiresTraining { get; }
        public int SortOrder { get; }
    }

    public readonly struct PawnSkillOptionData
    {
        public PawnSkillOptionData(
            string skillId,
            string displayName,
            int baseValue)
        {
            SkillId = skillId;
            DisplayName = displayName;
            BaseValue = Mathf.Max(0, baseValue);
        }

        public string SkillId { get; }
        public string DisplayName { get; }
        public int BaseValue { get; }
    }

    public readonly struct PawnSkillAddRequest
    {
        public PawnSkillAddRequest(
            string skillId,
            int regularValue)
        {
            SkillId = skillId;
            RegularValue = Mathf.Max(0, regularValue);
        }

        public string SkillId { get; }
        public int RegularValue { get; }
    }

    public readonly struct PawnSkillNameEditRequest
    {
        public PawnSkillNameEditRequest(
            string skillId,
            string displayName)
        {
            SkillId = skillId;
            DisplayName = displayName;
        }

        public string SkillId { get; }
        public string DisplayName { get; }
    }

    public readonly struct PawnSkillRegularEditRequest
    {
        public PawnSkillRegularEditRequest(
            string skillId,
            int regularValue)
        {
            SkillId = skillId;
            RegularValue = Mathf.Clamp(regularValue, 0, 999);
        }

        public string SkillId { get; }
        public int RegularValue { get; }
    }

    public readonly struct PawnSkillRemoveRequest
    {
        public PawnSkillRemoveRequest(string skillId)
        {
            SkillId = skillId ?? string.Empty;
        }

        public string SkillId { get; }
    }

    public sealed class PawnSkillPanelWidget :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        private const float PanelWidth = 372f;
        private const float PanelGap = 12f;
        private const float HeaderHeight = 82f;
        private const float RowHeight = 48f;
        private const float RowSpacing = 6f;
        private const float TransitionDuration = 0.22f;
        private const float HiddenOffset = 32f;

        private sealed class SkillVisual
        {
            public GameObject Root;
            public InputField NameInput;
            public InputField RegularInput;
            public Text Hard;
            public Text Extreme;
            public PawnRollSourceWidget RollSource;
            public Button RemoveButton;
            public string SkillId;
            public string BoundName;
            public int BoundRegular;
        }

        private readonly List<SkillVisual> _visuals =
            new List<SkillVisual>();

        private RectTransform _rect;
        private RectTransform _content;
        private RectTransform _viewport;
        private CanvasGroup _canvasGroup;
        private Text _emptyText;
        private Button _addButton;
        private ScrollRect _scrollRect;
        private Scrollbar _verticalScrollbar;
        private Font _font;
        private Sequence _transition;
        private Vector2 _shownPosition;
        private bool _isExpanded;
        private bool _canEdit;
        private int _usedVisualCount;
        private bool _isEmbedded;
        private Transform _legacyParent;
        private Vector2 _legacyAnchorMin;
        private Vector2 _legacyAnchorMax;
        private Vector2 _legacyPivot;
        private Vector2 _legacyAnchoredPosition;
        private Vector2 _legacySizeDelta;
        private Color _legacyBackgroundColor;

        public bool IsExpanded => _isExpanded;
        public bool IsEmbedded => _isEmbedded;
        public RectTransform RootRect => _rect;
        public event Action<PawnSkillAddRequest> AddRequested;
        public event Action<PawnSkillNameEditRequest>
            NameEditRequested;
        public event Action<PawnSkillRegularEditRequest>
            RegularEditRequested;
        public event Action<PawnSkillRemoveRequest> RemoveRequested;
        public event Action PointerEntered;
        public event Action PointerExited;

        public static PawnSkillPanelWidget CreateRuntime(
            RectTransform statDrawer,
            Font font)
        {
            if (statDrawer == null)
                return null;

            var root = new GameObject(
                "SkillDrawer",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(statDrawer, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-PanelGap, 0f);
            rect.sizeDelta = new Vector2(
                PanelWidth,
                Mathf.Max(420f, statDrawer.rect.height));
            root.GetComponent<Image>().color =
                new Color(0.025f, 0.075f, 0.095f, 0.985f);

            var widget = root.AddComponent<PawnSkillPanelWidget>();
            widget.Build(rect, font);
            root.SetActive(false);
            return widget;
        }

        public void Bind(IReadOnlyList<PawnSkillValueData> skills)
        {
            Bind(skills, null, false);
        }

        public void Bind(
            IReadOnlyList<PawnSkillValueData> skills,
            IReadOnlyList<PawnSkillOptionData> options,
            bool canAdd)
        {
            _canEdit = canAdd;
            _usedVisualCount = 0;
            var count = skills != null ? skills.Count : 0;
            for (var index = 0; index < count; index++)
            {
                BindVisual(GetVisual(), skills[index]);
            }

            for (var index = _usedVisualCount;
                 index < _visuals.Count;
                 index++)
            {
                UnbindVisual(_visuals[index]);
                _visuals[index].Root.SetActive(false);
            }

            if (_emptyText != null)
                _emptyText.gameObject.SetActive(_usedVisualCount == 0);
            if (_addButton != null)
                _addButton.interactable = _canEdit;

            RefreshContentHeight();
        }

        public void SetExpanded(bool expanded, bool animate)
        {
            if (_isEmbedded)
            {
                _isExpanded = true;
                Show(false);
                return;
            }

            if (_isExpanded == expanded)
                return;

            _isExpanded = expanded;
            if (_isExpanded)
                Show(animate);
            else
                Hide(animate);
        }

        public void SetEmbeddedMode(
            RectTransform host,
            bool enabled)
        {
            if (_rect == null)
                return;

            if (enabled)
            {
                if (host == null)
                    throw new ArgumentNullException(nameof(host));

                if (!_isEmbedded)
                {
                    _legacyParent = _rect.parent;
                    _legacyAnchorMin = _rect.anchorMin;
                    _legacyAnchorMax = _rect.anchorMax;
                    _legacyPivot = _rect.pivot;
                    _legacyAnchoredPosition = _rect.anchoredPosition;
                    _legacySizeDelta = _rect.sizeDelta;
                    var capturedBackground =
                        _rect.GetComponent<Image>();
                    if (capturedBackground != null)
                    {
                        _legacyBackgroundColor =
                            capturedBackground.color;
                    }
                }

                _isEmbedded = true;
                KillTransition();
                _rect.SetParent(host, false);
                StretchToHost(_rect);
                var embeddedBackground = _rect.GetComponent<Image>();
                if (embeddedBackground != null)
                    embeddedBackground.color = Color.clear;
                _shownPosition = Vector2.zero;
                _isExpanded = true;
                SetRollSourceInteraction(true);
                Show(false);
                return;
            }

            if (!_isEmbedded)
                return;

            KillTransition();
            _isEmbedded = false;
            _isExpanded = false;
            SetRollSourceInteraction(false);
            if (_legacyParent != null)
                _rect.SetParent(_legacyParent, false);
            _rect.anchorMin = _legacyAnchorMin;
            _rect.anchorMax = _legacyAnchorMax;
            _rect.pivot = _legacyPivot;
            _rect.anchoredPosition = _legacyAnchoredPosition;
            _rect.sizeDelta = _legacySizeDelta;
            _shownPosition = _legacyAnchoredPosition;
            var restoredBackground =
                _rect.GetComponent<Image>();
            if (restoredBackground != null)
            {
                restoredBackground.color =
                    _legacyBackgroundColor;
            }
            Hide(false);
        }

        private void SetRollSourceInteraction(bool enabled)
        {
            for (var index = 0; index < _visuals.Count; index++)
            {
                var source = _visuals[index].RollSource;
                if (source != null)
                    source.SetInteractionEnabled(enabled);
            }
        }

        public void ToggleExpanded()
        {
            SetExpanded(!_isExpanded, true);
        }

        public void SetLayout(float width, float height)
        {
            if (_rect == null)
                return;

            if (_isEmbedded)
            {
                StretchToHost(_rect);
                return;
            }

            _rect.sizeDelta = new Vector2(
                Mathf.Clamp(width, 280f, PanelWidth),
                Mathf.Max(420f, height));
        }

        public void Clear()
        {
            SetExpanded(false, false);
            _canEdit = false;
            _usedVisualCount = 0;
            for (var index = 0; index < _visuals.Count; index++)
            {
                UnbindVisual(_visuals[index]);
                _visuals[index].Root.SetActive(false);
            }

            if (_addButton != null)
                _addButton.interactable = false;
            if (_emptyText != null)
                _emptyText.gameObject.SetActive(true);
        }

        private void Build(RectTransform rect, Font font)
        {
            _rect = rect;
            _font = font;
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _shownPosition = rect.anchoredPosition;

            var title = CreateText(
                "Title",
                rect,
                20,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            title.text = "보유 스킬";
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(16f, -40f);
            titleRect.offsetMax = new Vector2(-58f, -6f);

            BuildAddButton(rect);
            BuildHeader(rect);
            BuildScrollArea(rect);
        }

        private void BuildAddButton(RectTransform parent)
        {
            var root = new GameObject(
                "AddSkillButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-12f, -8f);
            rect.sizeDelta = new Vector2(36f, 30f);
            var image = root.GetComponent<Image>();
            image.color = new Color(0.07f, 0.32f, 0.39f, 0.98f);
            _addButton = root.GetComponent<Button>();
            _addButton.targetGraphic = image;
            _addButton.onClick.AddListener(HandleAddClicked);

            var label = CreateText(
                "Label",
                rect,
                20,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            label.text = "+";
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        private void BuildHeader(RectTransform parent)
        {
            var header = new GameObject(
                "DifficultyHeader",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = header.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(12f, -76f);
            rect.offsetMax = new Vector2(-12f, -44f);
            header.GetComponent<Image>().color =
                new Color(0.06f, 0.16f, 0.19f, 0.98f);

            CreateCellText("기술", rect, 0f, 0.43f);
            CreateCellText("보통", rect, 0.43f, 0.59f);
            CreateCellText("어려움", rect, 0.59f, 0.74f);
            CreateCellText("극단", rect, 0.74f, 0.90f);
            CreateCellText("삭제", rect, 0.90f, 1f);
        }

        private void BuildScrollArea(RectTransform parent)
        {
            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D));
            _viewport = viewportObject.GetComponent<RectTransform>();
            _viewport.SetParent(parent, false);
            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(12f, 12f);
            _viewport.offsetMax = new Vector2(-30f, -HeaderHeight);
            viewportObject.GetComponent<Image>().color =
                new Color(1f, 1f, 1f, 0.012f);

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform));
            _content = contentObject.GetComponent<RectTransform>();
            _content.SetParent(_viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            _scrollRect = gameObject.AddComponent<ScrollRect>();
            _scrollRect.viewport = _viewport;
            _scrollRect.content = _content;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 30f;
            BuildVerticalScrollbar();

            _emptyText = CreateText(
                "Empty",
                _viewport,
                15,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            _emptyText.text = "등록된 스킬이 없습니다.";
            _emptyText.color = new Color(0.68f, 0.76f, 0.80f, 1f);
            _emptyText.rectTransform.anchorMin = Vector2.zero;
            _emptyText.rectTransform.anchorMax = Vector2.one;
            _emptyText.rectTransform.offsetMin = Vector2.zero;
            _emptyText.rectTransform.offsetMax = Vector2.zero;
        }

        private void BuildVerticalScrollbar()
        {
            var root = new GameObject(
                "SkillVerticalScrollbar",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Scrollbar));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(_rect, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-18f, 12f);
            rect.offsetMax = new Vector2(-6f, -HeaderHeight);

            var background = root.GetComponent<Image>();
            background.color =
                new Color(0.015f, 0.04f, 0.05f, 0.90f);

            var handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var handleRect =
                handleObject.GetComponent<RectTransform>();
            handleRect.SetParent(rect, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(2f, 2f);
            handleRect.offsetMax = new Vector2(-2f, -2f);
            var handleImage =
                handleObject.GetComponent<Image>();
            handleImage.color =
                new Color(0.08f, 0.48f, 0.60f, 0.95f);

            _verticalScrollbar = root.GetComponent<Scrollbar>();
            _verticalScrollbar.direction =
                Scrollbar.Direction.BottomToTop;
            _verticalScrollbar.targetGraphic = handleImage;
            _verticalScrollbar.handleRect = handleRect;
            _scrollRect.verticalScrollbar = _verticalScrollbar;
            _scrollRect.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
        }

        private void CreateCellText(
            string label,
            RectTransform parent,
            float anchorMinX,
            float anchorMaxX)
        {
            var text = CreateText(
                label,
                parent,
                12,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            text.text = label;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.offsetMin = new Vector2(3f, 2f);
            rect.offsetMax = new Vector2(-3f, -2f);
        }

        private SkillVisual GetVisual()
        {
            SkillVisual visual;
            if (_usedVisualCount < _visuals.Count)
            {
                visual = _visuals[_usedVisualCount];
            }
            else
            {
                visual = CreateVisual();
                _visuals.Add(visual);
            }

            var rowIndex = _usedVisualCount;
            _usedVisualCount++;
            var rect = visual.Root.transform as RectTransform;
            rect.anchoredPosition = new Vector2(
                0f,
                -rowIndex * (RowHeight + RowSpacing));
            visual.Root.SetActive(true);
            return visual;
        }

        private SkillVisual CreateVisual()
        {
            var root = new GameObject(
                "SkillEntry",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(_content, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, RowHeight);
            root.GetComponent<Image>().color =
                new Color(0.055f, 0.14f, 0.17f, 0.98f);

            var visual = new SkillVisual
            {
                Root = root,
                NameInput = CreateInput(
                    "Name",
                    rect,
                    0f,
                    0.43f,
                    InputField.ContentType.Standard,
                    TextAnchor.MiddleLeft),
                RegularInput = CreateInput(
                    "Regular",
                    rect,
                    0.43f,
                    0.59f,
                    InputField.ContentType.IntegerNumber,
                    TextAnchor.MiddleCenter),
                Hard = CreateValueText(
                    "Hard",
                    rect,
                    0.59f,
                    0.74f),
                Extreme = CreateValueText(
                    "Extreme",
                    rect,
                    0.74f,
                    0.90f),
                RemoveButton = CreateRemoveButton(rect)
            };
            visual.RollSource = root.AddComponent<PawnRollSourceWidget>();
            PawnRollSourceWidget.ForwardInputDragEvents(
                visual.NameInput.gameObject,
                visual.RollSource);
            PawnRollSourceWidget.ForwardInputDragEvents(
                visual.RegularInput.gameObject,
                visual.RollSource);
            return visual;
        }

        private Button CreateRemoveButton(RectTransform parent)
        {
            var root = new GameObject(
                "Remove",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.90f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(4f, 7f);
            rect.offsetMax = new Vector2(-4f, -7f);

            var image = root.GetComponent<Image>();
            image.color = new Color(0.42f, 0.10f, 0.12f, 0.92f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;

            var label = CreateText(
                "Label",
                rect,
                17,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            label.text = "×";
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private InputField CreateInput(
            string objectName,
            RectTransform parent,
            float anchorMinX,
            float anchorMaxX,
            InputField.ContentType contentType,
            TextAnchor alignment)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(InputField));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.offsetMin = new Vector2(
                alignment == TextAnchor.MiddleLeft ? 5f : 3f,
                5f);
            rect.offsetMax = new Vector2(-3f, -5f);

            var image = root.GetComponent<Image>();
            image.color = new Color(0.02f, 0.07f, 0.085f, 0.72f);
            var text = CreateText(
                "Text",
                rect,
                13,
                FontStyle.Normal,
                alignment);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(
                alignment == TextAnchor.MiddleLeft ? 7f : 2f,
                1f);
            text.rectTransform.offsetMax = new Vector2(-2f, -1f);

            var input = root.GetComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.contentType = contentType;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit =
                contentType == InputField.ContentType.Standard
                    ? 32
                    : 3;
            return input;
        }

        private Text CreateValueText(
            string objectName,
            RectTransform parent,
            float anchorMinX,
            float anchorMaxX)
        {
            var text = CreateText(
                objectName,
                parent,
                13,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.offsetMin = new Vector2(3f, 3f);
            rect.offsetMax = new Vector2(-3f, -3f);
            return text;
        }

        private void BindVisual(
            SkillVisual visual,
            PawnSkillValueData data)
        {
            UnbindVisual(visual);
            visual.SkillId = data.SkillId ?? string.Empty;
            visual.BoundName = string.IsNullOrWhiteSpace(data.DisplayName)
                ? "새 스킬"
                : data.DisplayName.Trim();
            visual.BoundRegular = Mathf.Clamp(data.Regular, 0, 999);

            visual.NameInput.SetTextWithoutNotify(visual.BoundName);
            visual.RegularInput.SetTextWithoutNotify(
                visual.BoundRegular.ToString(
                    CultureInfo.InvariantCulture));
            visual.Hard.text = data.Hard.ToString(
                CultureInfo.InvariantCulture);
            visual.Extreme.text = data.Extreme.ToString(
                CultureInfo.InvariantCulture);

            visual.NameInput.interactable = _canEdit;
            visual.RegularInput.interactable = _canEdit;
            visual.RemoveButton.interactable = _canEdit;

            var sourceId = string.IsNullOrWhiteSpace(data.SkillId)
                ? $"skill:{visual.BoundName}"
                : data.SkillId;
            var regular = Mathf.Clamp(data.Regular, 1, 100);
            var hard = data.Hard >= 1
                ? Mathf.Clamp(data.Hard, 1, 100)
                : Mathf.Max(1, regular / 2);
            var extreme = data.Extreme >= 1
                ? Mathf.Clamp(data.Extreme, 1, 100)
                : Mathf.Max(1, regular / 5);
            var source = new PawnCheckSourceData(
                sourceId,
                visual.BoundName,
                PawnRollSourceKind.Skill,
                regular,
                hard,
                extreme);
            visual.RollSource.Bind(source);
            visual.RollSource.SetInteractionEnabled(_isEmbedded);

            var unavailable =
                data.UsesBaseValue && data.RequiresTraining;
            var color = unavailable
                ? new Color(0.62f, 0.64f, 0.66f, 1f)
                : Color.white;
            visual.NameInput.textComponent.color = color;
            visual.RegularInput.textComponent.color = color;
            visual.Hard.color = color;
            visual.Extreme.color = color;

            visual.NameInput.onEndEdit.AddListener(
                value => HandleNameEnded(visual, value));
            visual.RegularInput.onEndEdit.AddListener(
                value => HandleRegularEnded(visual, value));
            visual.RemoveButton.onClick.AddListener(
                () => HandleRemoveClicked(visual));
        }

        private static void UnbindVisual(SkillVisual visual)
        {
            if (visual == null)
                return;

            visual.NameInput.onEndEdit.RemoveAllListeners();
            visual.RegularInput.onEndEdit.RemoveAllListeners();
            visual.RemoveButton.onClick.RemoveAllListeners();
            visual.RollSource?.Unbind();
            visual.SkillId = string.Empty;
        }

        private void HandleAddClicked()
        {
            if (!_canEdit)
                return;

            AddRequested?.Invoke(
                new PawnSkillAddRequest(string.Empty, 0));
        }

        private void HandleRemoveClicked(SkillVisual visual)
        {
            if (!_canEdit ||
                visual == null ||
                string.IsNullOrWhiteSpace(visual.SkillId))
            {
                return;
            }

            RemoveRequested?.Invoke(
                new PawnSkillRemoveRequest(visual.SkillId));
        }

        private void HandleNameEnded(
            SkillVisual visual,
            string value)
        {
            if (!_canEdit ||
                visual == null ||
                string.IsNullOrWhiteSpace(visual.SkillId))
            {
                return;
            }

            if (visual.NameInput.wasCanceled)
            {
                visual.NameInput.SetTextWithoutNotify(
                    visual.BoundName);
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(value)
                ? "새 스킬"
                : value.Trim();
            visual.NameInput.SetTextWithoutNotify(displayName);
            NameEditRequested?.Invoke(
                new PawnSkillNameEditRequest(
                    visual.SkillId,
                    displayName));
        }

        private void HandleRegularEnded(
            SkillVisual visual,
            string value)
        {
            if (!_canEdit ||
                visual == null ||
                string.IsNullOrWhiteSpace(visual.SkillId))
            {
                return;
            }

            if (visual.RegularInput.wasCanceled ||
                !TryParseInteger(value, out var regularValue))
            {
                visual.RegularInput.SetTextWithoutNotify(
                    visual.BoundRegular.ToString(
                        CultureInfo.InvariantCulture));
                return;
            }

            var clamped = Mathf.Clamp(regularValue, 0, 999);
            visual.RegularInput.SetTextWithoutNotify(
                clamped.ToString(CultureInfo.InvariantCulture));
            RegularEditRequested?.Invoke(
                new PawnSkillRegularEditRequest(
                    visual.SkillId,
                    clamped));
        }

        private static bool TryParseInteger(
            string text,
            out int value)
        {
            return int.TryParse(
                       text,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value) ||
                   int.TryParse(
                       text,
                       NumberStyles.Integer,
                       CultureInfo.CurrentCulture,
                       out value);
        }

        private void RefreshContentHeight()
        {
            if (_content == null)
                return;

            var height = _usedVisualCount > 0
                ? _usedVisualCount * RowHeight +
                  Mathf.Max(0, _usedVisualCount - 1) * RowSpacing
                : RowHeight;
            _content.sizeDelta = new Vector2(0f, height);
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void Show(bool animate)
        {
            KillTransition();
            gameObject.SetActive(true);
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            if (!animate)
            {
                _rect.anchoredPosition = _shownPosition;
                _canvasGroup.alpha = 1f;
                return;
            }

            _rect.anchoredPosition =
                _shownPosition + Vector2.right * HiddenOffset;
            _canvasGroup.alpha = 0f;
            _transition = DOTween.Sequence()
                .Join(_rect.DOAnchorPos(
                        _shownPosition,
                        TransitionDuration)
                    .SetEase(Ease.OutCubic))
                .Join(_canvasGroup.DOFade(
                    1f,
                    TransitionDuration))
                .SetUpdate(true);
        }

        private void Hide(bool animate)
        {
            KillTransition();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            if (!gameObject.activeSelf)
                return;

            if (!animate)
            {
                _rect.anchoredPosition = _shownPosition;
                _canvasGroup.alpha = 0f;
                gameObject.SetActive(false);
                return;
            }

            _transition = DOTween.Sequence()
                .Join(_rect.DOAnchorPos(
                        _shownPosition + Vector2.right * HiddenOffset,
                        TransitionDuration * 0.8f)
                    .SetEase(Ease.InCubic))
                .Join(_canvasGroup.DOFade(
                    0f,
                    TransitionDuration * 0.8f))
                .OnComplete(() =>
                {
                    if (this != null)
                        gameObject.SetActive(false);
                })
                .SetUpdate(true);
        }

        private static void StretchToHost(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void KillTransition()
        {
            _transition?.Kill();
            _transition = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_isExpanded)
                PointerEntered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_isExpanded)
                PointerExited?.Invoke();
        }

        private void OnDestroy()
        {
            KillTransition();
            if (_addButton != null)
                _addButton.onClick.RemoveListener(HandleAddClicked);
            for (var index = 0; index < _visuals.Count; index++)
                UnbindVisual(_visuals[index]);

            AddRequested = null;
            NameEditRequested = null;
            RegularEditRequested = null;
            RemoveRequested = null;
            PointerEntered = null;
            PointerExited = null;
        }
    }
}
