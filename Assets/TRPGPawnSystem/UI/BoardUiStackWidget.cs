using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public enum BoardUiWidthBand
    {
        Base,
        Compact,
        Minimal
    }

    public enum BoardLeftPane
    {
        Identity,
        Inventory,
        Profile
    }

    public enum BoardRightPane
    {
        Stats,
        Skills
    }

    public readonly struct BoardUiLayout
    {
        public BoardUiLayout(
            BoardUiWidthBand band,
            float effectiveWidth,
            float leftWidth,
            float rightWidth,
            float centerWidth)
        {
            Band = band;
            EffectiveWidth = effectiveWidth;
            LeftWidth = leftWidth;
            RightWidth = rightWidth;
            CenterWidth = centerWidth;
        }

        public BoardUiWidthBand Band { get; }
        public float EffectiveWidth { get; }
        public float LeftWidth { get; }
        public float RightWidth { get; }
        public float CenterWidth { get; }
    }

    public static class BoardUiLayoutCalculator
    {
        public const float ReferenceHeight = 1080f;
        public const float HorizontalMargin = 24f;
        public const float SheetHeight = 420f;
        public const float BottomOffset = 147f;
        public const float TopMargin = 24f;

        public static BoardUiLayout Calculate(
            int actualWidth,
            int actualHeight)
        {
            var width = Math.Max(1, actualWidth);
            var height = Math.Max(1, actualHeight);
            var effective = width * ReferenceHeight / height;
            var band = effective >= 1500f
                ? BoardUiWidthBand.Base
                : effective >= 1300f
                    ? BoardUiWidthBand.Compact
                    : BoardUiWidthBand.Minimal;

            var usable = Mathf.Max(
                1f,
                effective - HorizontalMargin * 2f);

            float left;
            float right;
            float minimumCenter;
            switch (band)
            {
                case BoardUiWidthBand.Base:
                    left = 440f;
                    right = 620f;
                    minimumCenter = 640f;
                    break;
                case BoardUiWidthBand.Compact:
                    left = 380f;
                    right = 560f;
                    minimumCenter = 380f;
                    break;
                default:
                    left = Mathf.Clamp(usable * 0.29f, 280f, 340f);
                    right = Mathf.Clamp(usable * 0.43f, 390f, 500f);
                    minimumCenter = 220f;
                    break;
            }

            var total = left + right + minimumCenter;
            if (total > usable)
            {
                var overflow = total - usable;
                var rightReduce = Mathf.Min(
                    overflow * 0.62f,
                    Mathf.Max(0f, right - 350f));
                right -= rightReduce;
                overflow -= rightReduce;
                left -= Mathf.Min(
                    overflow,
                    Mathf.Max(0f, left - 250f));
            }

            var center = Mathf.Max(0f, usable - left - right);
            return new BoardUiLayout(
                band,
                effective,
                left,
                right,
                center);
        }
    }

    /// <summary>
    /// 좌측은 인물/가방/캐릭터 정보, 우측은 능력치/기술을
    /// 각각 같은 자리에서 교체해 보여 줍니다. 중앙은 평소 비워 두고,
    /// 판정 원본 드래그 중에만 드롭 타깃과 판정 패널을 표시합니다.
    /// </summary>
    public sealed class BoardUiStackWidget : MonoBehaviour
    {
        private static readonly Color Surface =
            new Color(0.035f, 0.055f, 0.065f, 0.985f);
        private static readonly Color SurfaceElement =
            new Color(0.055f, 0.095f, 0.11f, 0.985f);
        private static readonly Color Border =
            new Color(0.23f, 0.38f, 0.42f, 0.42f);
        private static readonly Color Accent =
            new Color(0.08f, 0.48f, 0.60f, 1f);
        private static readonly Color TextMain =
            new Color(0.91f, 0.95f, 0.97f, 1f);
        private static readonly Color TextMuted =
            new Color(0.58f, 0.70f, 0.74f, 1f);

        private const float TabBarHeight = 42f;
        private const float InnerPadding = 12f;
        private const float RollWidth = 470f;
        private const float RollHeight = 350f;

        private Font _font;
        private Image _portraitImage;
        private Text _nameText;
        private Text _jobText;
        private Text _movementText;
        private Button _identityTab;
        private Button _inventoryTab;
        private Button _profileTab;
        private Button _statsTab;
        private Button _skillsTab;
        private RectTransform _leftTabs;
        private RectTransform _rightTabs;
        private Text _identityDetailText;
        private Text _dragSourceText;
        private Text _sourceText;
        private Text _targetText;
        private Text _bonusPenaltyText;
        private Button _regularButton;
        private Button _hardButton;
        private Button _extremeButton;
        private Button _rollButton;
        private Button _pushButton;
        private Button _luckButton;
        private PawnCheckSourceData _selectedSource;
        private PawnCheckDifficulty _selectedDifficulty;
        private int _bonusPenalty;
        private BoardLeftPane _leftPane = BoardLeftPane.Identity;
        private BoardRightPane _rightPane = BoardRightPane.Stats;

        public RectTransform RootRect { get; private set; }
        public CanvasGroup RootCanvasGroup { get; private set; }
        public RectTransform LeftMask { get; private set; }
        public RectTransform LeftContent { get; private set; }
        public CanvasGroup LeftContentGroup { get; private set; }
        public RectTransform RightMask { get; private set; }
        public RectTransform RightContent { get; private set; }
        public CanvasGroup RightContentGroup { get; private set; }
        public RectTransform IdentityHost { get; private set; }
        public RectTransform BagHost { get; private set; }
        public RectTransform ProfileHost { get; private set; }
        public RectTransform StatsHost { get; private set; }
        public RectTransform SkillsHost { get; private set; }
        public RectTransform RollOverlayMask { get; private set; }
        public RectTransform RollOverlayContent { get; private set; }
        public CanvasGroup RollOverlayGroup { get; private set; }
        public RectTransform DropPromptHost { get; private set; }
        public RectTransform RollHost { get; private set; }
        public BoardUiLayout Layout { get; private set; }
        public float PanelHeight { get; private set; } = 900f;
        public float PanelBottomOffset { get; private set; } =
            BoardUiLayoutCalculator.BottomOffset;
        public BoardLeftPane LeftPane => _leftPane;
        public BoardRightPane RightPane => _rightPane;
        public PawnCheckSourceData SelectedSource => _selectedSource;
        public PawnCheckDifficulty SelectedDifficulty => _selectedDifficulty;
        public int BonusPenalty => _bonusPenalty;

        public event Action<BoardLeftPane> LeftPaneRequested;
        public event Action<BoardRightPane> RightPaneRequested;
        public event Action<PawnCheckSourceData> SourceDropped;
        public event Action<PawnCheckDifficulty> DifficultyRequested;
        public event Action RollRequested;
        public event Action<int> BonusPenaltyChanged;

        public static BoardUiStackWidget CreateRuntime(
            RectTransform parentRect,
            Font font)
        {
            if (parentRect == null)
                throw new ArgumentNullException(nameof(parentRect));

            var root = new GameObject(
                "BoardUiStackWidget",
                typeof(RectTransform),
                typeof(CanvasGroup));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parentRect, false);
            Stretch(rect, 0f);

            var widget = root.AddComponent<BoardUiStackWidget>();
            widget.Build(rect, font);
            return widget;
        }

        public void ApplyLayout(BoardUiLayout layout)
        {
            var rootHeight = RootRect != null
                ? Mathf.Max(1f, RootRect.rect.height)
                : 1080f;
            var panelHeight = Mathf.Max(420f,
                rootHeight -
                BoardUiLayoutCalculator.BottomOffset -
                BoardUiLayoutCalculator.TopMargin);
            ApplyLayout(
                layout,
                BoardUiLayoutCalculator.BottomOffset,
                panelHeight);
        }

        public void ApplyLayout(
            BoardUiLayout layout,
            float bottomOffset,
            float panelHeight)
        {
            Layout = layout;
            PanelBottomOffset = Mathf.Max(0f, bottomOffset);
            PanelHeight = Mathf.Max(420f, panelHeight);

            var leftOpen = LeftMask.sizeDelta.y > 0.5f;
            var rightOpen = RightMask.sizeDelta.y > 0.5f;

            SetBottomLeft(
                LeftMask,
                BoardUiLayoutCalculator.HorizontalMargin,
                PanelBottomOffset,
                layout.LeftWidth,
                leftOpen ? PanelHeight : 0f);
            LeftContent.sizeDelta = new Vector2(
                layout.LeftWidth,
                PanelHeight);

            SetBottomRight(
                RightMask,
                BoardUiLayoutCalculator.HorizontalMargin,
                PanelBottomOffset,
                layout.RightWidth,
                rightOpen ? PanelHeight : 0f);
            RightContent.sizeDelta = new Vector2(
                layout.RightWidth,
                PanelHeight);

            var rollWidth = Mathf.Min(
                RollWidth,
                Mathf.Max(360f, layout.CenterWidth - 24f));
            RollOverlayContent.sizeDelta = new Vector2(
                rollWidth,
                RollHeight);
            RollOverlayMask.sizeDelta = new Vector2(
                rollWidth,
                RollOverlayMask.sizeDelta.y);
            ApplyBand(layout.Band);
            BringTabsToFront();
        }

        public void BindInfo(PawnInfoBarData data)
        {
            _portraitImage.sprite = data.Portrait;
            _portraitImage.enabled = data.Portrait != null;
            _nameText.text = string.IsNullOrWhiteSpace(data.DisplayName)
                ? "캐릭터"
                : data.DisplayName;
            _jobText.text = string.IsNullOrWhiteSpace(data.Description)
                ? "직업 정보 없음"
                : data.Description;
            if (_identityDetailText != null)
            {
                _identityDetailText.text =
                    $"{_nameText.text}\n\n{_jobText.text}";
            }
        }

        public void ClearInfo()
        {
            _portraitImage.sprite = null;
            _portraitImage.enabled = false;
            _nameText.text = string.Empty;
            _jobText.text = string.Empty;
            _movementText.text = string.Empty;
            if (_identityDetailText != null)
                _identityDetailText.text = string.Empty;
            ClearSource();
        }

        public void SetMovement(float remaining, float maximum)
        {
            _movementText.text = maximum > 0.0001f
                ? $"이동 {remaining:0.0}m 남음"
                : "이동 정보 없음";
        }

        public void SetLeftPane(BoardLeftPane pane)
        {
            _leftPane = pane;
            SetHostActive(IdentityHost, pane == BoardLeftPane.Identity);
            SetHostActive(BagHost, pane == BoardLeftPane.Inventory);
            SetHostActive(ProfileHost, pane == BoardLeftPane.Profile);
            SetSelected(_identityTab, pane == BoardLeftPane.Identity);
            SetSelected(_inventoryTab, pane == BoardLeftPane.Inventory);
            SetSelected(_profileTab, pane == BoardLeftPane.Profile);
            BringTabsToFront();
        }

        public void SetRightPane(BoardRightPane pane)
        {
            _rightPane = pane;
            SetHostActive(StatsHost, pane == BoardRightPane.Stats);
            SetHostActive(SkillsHost, pane == BoardRightPane.Skills);
            SetSelected(_statsTab, pane == BoardRightPane.Stats);
            SetSelected(_skillsTab, pane == BoardRightPane.Skills);
            BringTabsToFront();
        }

        public void SetPanelsImmediate(bool visible)
        {
            var height = visible
                ? PanelHeight
                : 0f;
            LeftMask.sizeDelta = new Vector2(
                Layout.LeftWidth,
                height);
            RightMask.sizeDelta = new Vector2(
                Layout.RightWidth,
                height);
            LeftContentGroup.alpha = visible ? 1f : 0f;
            RightContentGroup.alpha = visible ? 1f : 0f;
            LeftContentGroup.interactable = visible;
            LeftContentGroup.blocksRaycasts = visible;
            RightContentGroup.interactable = visible;
            RightContentGroup.blocksRaycasts = visible;
        }

        public void ShowDragTarget(PawnCheckSourceData source)
        {
            _dragSourceText.text = source.IsValid
                ? $"{source.DisplayName}\n여기에 드롭해 판정 준비"
                : "스탯 또는 기술을 여기에 드롭";
            DropPromptHost.gameObject.SetActive(true);
            RollHost.gameObject.SetActive(false);
            RollOverlayMask.gameObject.SetActive(true);
            RollOverlayGroup.alpha = 1f;
            RollOverlayGroup.interactable = true;
            RollOverlayGroup.blocksRaycasts = true;
            RollOverlayMask.DOKill(true);
            RollOverlayMask.sizeDelta = new Vector2(
                RollOverlayContent.sizeDelta.x,
                Mathf.Max(190f, RollOverlayMask.sizeDelta.y));
        }

        public void HideDragTarget()
        {
            if (RollHost.gameObject.activeSelf)
                return;
            HideRollOverlayImmediate();
        }

        public void PrepareRollPanel(PawnCheckSourceData source)
        {
            DropPromptHost.gameObject.SetActive(false);
            RollHost.gameObject.SetActive(true);
            RollOverlayMask.gameObject.SetActive(true);
            RollOverlayGroup.interactable = true;
            RollOverlayGroup.blocksRaycasts = true;
            SelectSource(source);
        }

        public void ShowEmptyRollPanel()
        {
            DropPromptHost.gameObject.SetActive(false);
            RollHost.gameObject.SetActive(true);
            RollOverlayMask.gameObject.SetActive(true);
            RollOverlayGroup.interactable = true;
            RollOverlayGroup.blocksRaycasts = true;
            ClearSource();
        }

        public void HideRollOverlayImmediate()
        {
            RollOverlayGroup.alpha = 0f;
            RollOverlayGroup.interactable = false;
            RollOverlayGroup.blocksRaycasts = false;
            RollOverlayMask.sizeDelta = new Vector2(
                RollOverlayContent.sizeDelta.x,
                0f);
            RollOverlayMask.gameObject.SetActive(false);
            DropPromptHost.gameObject.SetActive(false);
            RollHost.gameObject.SetActive(false);
        }

        public void SelectSource(PawnCheckSourceData source)
        {
            if (!source.IsValid)
            {
                ClearSource();
                return;
            }

            _selectedSource = source;
            _sourceText.text = source.DisplayName;
            _selectedDifficulty = PawnCheckDifficulty.Regular;
            RefreshDifficulty();
        }

        public void ClearSource()
        {
            _selectedSource = default;
            _sourceText.text = "스탯 또는 기술을 드롭";
            _targetText.text = "목표 —";
            _rollButton.interactable = false;
            SetSelected(_regularButton, false);
            SetSelected(_hardButton, false);
            SetSelected(_extremeButton, false);
        }

        private void Build(RectTransform root, Font font)
        {
            RootRect = root;
            RootCanvasGroup = GetComponent<CanvasGroup>();
            RootCanvasGroup.alpha = 1f;
            RootCanvasGroup.interactable = true;
            RootCanvasGroup.blocksRaycasts = true;
            _font = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");

            LeftMask = CreateSurface("LeftMask", RootRect, Surface);
            LeftMask.gameObject.AddComponent<RectMask2D>();
            LeftMask.anchorMin = Vector2.zero;
            LeftMask.anchorMax = Vector2.zero;
            LeftMask.pivot = Vector2.zero;
            LeftMask.sizeDelta = new Vector2(440f, 0f);

            LeftContent = CreateSurface("LeftContent", LeftMask, Surface);
            LeftContent.anchorMin = Vector2.zero;
            LeftContent.anchorMax = Vector2.zero;
            LeftContent.pivot = Vector2.zero;
            LeftContent.sizeDelta = new Vector2(
                440f,
                900f);
            LeftContentGroup =
                LeftContent.gameObject.AddComponent<CanvasGroup>();
            LeftContentGroup.alpha = 0f;

            RightMask = CreateSurface("RightMask", RootRect, Surface);
            RightMask.gameObject.AddComponent<RectMask2D>();
            RightMask.anchorMin = Vector2.zero;
            RightMask.anchorMax = Vector2.zero;
            RightMask.pivot = Vector2.zero;
            RightMask.sizeDelta = new Vector2(620f, 0f);

            RightContent = CreateSurface("RightContent", RightMask, Surface);
            RightContent.anchorMin = Vector2.zero;
            RightContent.anchorMax = Vector2.zero;
            RightContent.pivot = Vector2.zero;
            RightContent.sizeDelta = new Vector2(
                620f,
                900f);
            RightContentGroup =
                RightContent.gameObject.AddComponent<CanvasGroup>();
            RightContentGroup.alpha = 0f;

            BuildLeftPanel();
            BuildRightPanel();
            BuildCenterRollOverlay();
            SetLeftPane(BoardLeftPane.Identity);
            SetRightPane(BoardRightPane.Stats);
            HideRollOverlayImmediate();
        }

        private void BuildLeftPanel()
        {
            AddBorder(LeftContent);
            _leftTabs = CreateSurface(
                "LeftTabs",
                LeftContent,
                SurfaceElement);
            SetTopStretch(_leftTabs, 0f, 0f, TabBarHeight);

            _identityTab = CreateButton(
                "IdentityTab",
                _leftTabs,
                "인물",
                14);
            _inventoryTab = CreateButton(
                "InventoryTab",
                _leftTabs,
                "가방",
                14);
            _profileTab = CreateButton(
                "ProfileTab",
                _leftTabs,
                "캐릭터 정보",
                14);
            PlaceThird(_identityTab, 0f, 0.25f);
            PlaceThird(_inventoryTab, 0.25f, 0.50f);
            PlaceThird(_profileTab, 0.50f, 1f);
            _identityTab.onClick.AddListener(
                () => LeftPaneRequested?.Invoke(BoardLeftPane.Identity));
            _inventoryTab.onClick.AddListener(
                () => LeftPaneRequested?.Invoke(BoardLeftPane.Inventory));
            _profileTab.onClick.AddListener(
                () => LeftPaneRequested?.Invoke(BoardLeftPane.Profile));

            var portraitHost = CreateRect(
                "PersistentPortraitHost",
                LeftContent);
            portraitHost.anchorMin = new Vector2(0f, 1f);
            portraitHost.anchorMax = new Vector2(1f, 1f);
            portraitHost.pivot = new Vector2(0.5f, 1f);
            portraitHost.anchoredPosition =
                new Vector2(0f, -(TabBarHeight + 8f));
            portraitHost.sizeDelta = new Vector2(0f, 206f);
            BuildPersistentPortrait(portraitHost);

            var body = CreateRect("LeftBody", LeftContent);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(InnerPadding, InnerPadding);
            body.offsetMax = new Vector2(
                -InnerPadding,
                -(TabBarHeight + 222f));
            body.gameObject.AddComponent<RectMask2D>();

            IdentityHost = CreateRect("IdentityHost", body);
            BagHost = CreateRect("BagHost", body);
            ProfileHost = CreateRect("ProfileHost", body);
            Stretch(IdentityHost, 0f);
            Stretch(BagHost, 0f);
            Stretch(ProfileHost, 0f);
            BuildIdentitySummary();
            BringTabsToFront();
        }

        private void BuildPersistentPortrait(RectTransform host)
        {
            var portraitFrame = CreateSurface(
                "PortraitFrame",
                host,
                SurfaceElement);
            portraitFrame.anchorMin = new Vector2(0f, 0.5f);
            portraitFrame.anchorMax = new Vector2(0f, 0.5f);
            portraitFrame.pivot = new Vector2(0f, 0.5f);
            portraitFrame.anchoredPosition = new Vector2(14f, 0f);
            portraitFrame.sizeDelta = new Vector2(176f, 176f);
            AddBorder(portraitFrame);

            _portraitImage = CreateImage(
                "Portrait",
                portraitFrame,
                Color.white);
            Stretch(_portraitImage.rectTransform, 8f);
            _portraitImage.preserveAspect = true;
            _portraitImage.raycastTarget = false;

            _nameText = CreateText(
                "Name",
                host,
                23,
                FontStyle.Bold,
                TextAnchor.UpperLeft,
                TextMain);
            _nameText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _nameText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _nameText.rectTransform.offsetMin = new Vector2(206f, 96f);
            _nameText.rectTransform.offsetMax = new Vector2(-14f, -18f);

            _jobText = CreateText(
                "Job",
                host,
                14,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                TextMuted);
            _jobText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _jobText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _jobText.rectTransform.offsetMin = new Vector2(206f, 48f);
            _jobText.rectTransform.offsetMax = new Vector2(-14f, -58f);

            _movementText = CreateText(
                "Movement",
                host,
                14,
                FontStyle.Bold,
                TextAnchor.LowerLeft,
                Accent);
            _movementText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _movementText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _movementText.rectTransform.offsetMin = new Vector2(206f, 18f);
            _movementText.rectTransform.offsetMax = new Vector2(-14f, -128f);
        }

        private void BuildIdentitySummary()
        {
            var surface = CreateSurface(
                "IdentitySummarySurface",
                IdentityHost,
                SurfaceElement);
            Stretch(surface, 0f);
            AddBorder(surface);

            var title = CreateText(
                "IdentitySummaryTitle",
                surface,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMain);
            title.text = "캐릭터 개요";
            SetTopStretch(title.rectTransform, 16f, 10f, 34f);

            _identityDetailText = CreateText(
                "IdentitySummaryText",
                surface,
                14,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                TextMuted);
            _identityDetailText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _identityDetailText.verticalOverflow =
                VerticalWrapMode.Truncate;
            _identityDetailText.rectTransform.anchorMin = Vector2.zero;
            _identityDetailText.rectTransform.anchorMax = Vector2.one;
            _identityDetailText.rectTransform.offsetMin =
                new Vector2(16f, 16f);
            _identityDetailText.rectTransform.offsetMax =
                new Vector2(-16f, -54f);
        }

        private void BuildRightPanel()
        {
            AddBorder(RightContent);
            _rightTabs = CreateSurface(
                "RightTabs",
                RightContent,
                SurfaceElement);
            SetTopStretch(_rightTabs, 0f, 0f, TabBarHeight);

            _statsTab = CreateButton(
                "StatsTab",
                _rightTabs,
                "능력치",
                14);
            _skillsTab = CreateButton(
                "SkillsTab",
                _rightTabs,
                "기술",
                14);
            PlaceHalf(_statsTab, 0f, 0.5f);
            PlaceHalf(_skillsTab, 0.5f, 1f);
            _statsTab.onClick.AddListener(
                () => RightPaneRequested?.Invoke(BoardRightPane.Stats));
            _skillsTab.onClick.AddListener(
                () => RightPaneRequested?.Invoke(BoardRightPane.Skills));

            var body = CreateRect("RightBody", RightContent);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(InnerPadding, InnerPadding);
            body.offsetMax = new Vector2(
                -InnerPadding,
                -(TabBarHeight + InnerPadding));
            body.gameObject.AddComponent<RectMask2D>();

            StatsHost = CreateRect("StatsHost", body);
            SkillsHost = CreateRect("SkillsHost", body);
            Stretch(StatsHost, 0f);
            Stretch(SkillsHost, 0f);
            BringTabsToFront();
        }

        private void BuildCenterRollOverlay()
        {
            RollOverlayMask = CreateSurface(
                "RollOverlayMask",
                RootRect,
                Surface);
            RollOverlayMask.gameObject.AddComponent<RectMask2D>();
            RollOverlayMask.anchorMin = new Vector2(0.5f, 0.5f);
            RollOverlayMask.anchorMax = new Vector2(0.5f, 0.5f);
            RollOverlayMask.pivot = new Vector2(0.5f, 0.5f);
            RollOverlayMask.anchoredPosition = new Vector2(0f, 22f);
            RollOverlayMask.sizeDelta = new Vector2(RollWidth, 0f);
            RollOverlayGroup =
                RollOverlayMask.gameObject.AddComponent<CanvasGroup>();
            AddBorder(RollOverlayMask);

            RollOverlayContent = CreateSurface(
                "RollOverlayContent",
                RollOverlayMask,
                Surface);
            RollOverlayContent.anchorMin = new Vector2(0.5f, 0f);
            RollOverlayContent.anchorMax = new Vector2(0.5f, 0f);
            RollOverlayContent.pivot = new Vector2(0.5f, 0f);
            RollOverlayContent.anchoredPosition = Vector2.zero;
            RollOverlayContent.sizeDelta = new Vector2(
                RollWidth,
                RollHeight);

            DropPromptHost = CreateRect(
                "DropPromptHost",
                RollOverlayContent);
            Stretch(DropPromptHost, 0f);
            var dropSurface = DropPromptHost.gameObject.AddComponent<Image>();
            dropSurface.color = new Color(0.04f, 0.16f, 0.19f, 0.96f);
            var dropTarget = DropPromptHost.gameObject.AddComponent<
                BoardUiRollDropTarget>();
            dropTarget.SourceDropped += source =>
                SourceDropped?.Invoke(source);

            var dropTitle = CreateText(
                "DropTitle",
                DropPromptHost,
                22,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextMain);
            dropTitle.text = "판정 굴림";
            SetTopStretch(
                dropTitle.rectTransform,
                28f,
                44f,
                42f);

            _dragSourceText = CreateText(
                "DragSource",
                DropPromptHost,
                17,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Accent);
            Stretch(_dragSourceText.rectTransform, 36f);
            _dragSourceText.text = "스탯 또는 기술을 여기에 드롭";

            RollHost = CreateRect("RollHost", RollOverlayContent);
            Stretch(RollHost, 0f);
            BuildRollView();
        }

        private void BuildRollView()
        {
            var title = CreateText(
                "RollTitle",
                RollHost,
                22,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMain);
            title.text = "판정 굴림";
            SetTopStretch(title.rectTransform, 18f, 12f, 40f);

            var sourceSurface = CreateSurface(
                "SourceSurface",
                RollHost,
                SurfaceElement);
            SetTopStretch(sourceSurface, 18f, 58f, 52f);
            _sourceText = CreateText(
                "Source",
                sourceSurface,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMain);
            Stretch(_sourceText.rectTransform, 12f);

            var difficultyLabel = CreateText(
                "DifficultyLabel",
                RollHost,
                13,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMuted);
            difficultyLabel.text = "난이도";
            SetTopStretch(
                difficultyLabel.rectTransform,
                18f,
                120f,
                24f);

            _regularButton = CreateButton(
                "Regular",
                RollHost,
                "보통",
                14);
            _hardButton = CreateButton(
                "Hard",
                RollHost,
                "어려움",
                14);
            _extremeButton = CreateButton(
                "Extreme",
                RollHost,
                "극단",
                14);
            PlaceThreeAtTop(
                _regularButton,
                _hardButton,
                _extremeButton,
                148f,
                42f);
            _regularButton.onClick.AddListener(
                () => SelectDifficulty(PawnCheckDifficulty.Regular));
            _hardButton.onClick.AddListener(
                () => SelectDifficulty(PawnCheckDifficulty.Hard));
            _extremeButton.onClick.AddListener(
                () => SelectDifficulty(PawnCheckDifficulty.Extreme));

            _targetText = CreateText(
                "Target",
                RollHost,
                15,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Accent);
            SetTopStretch(_targetText.rectTransform, 18f, 198f, 30f);

            var penalty = CreateButton(
                "Penalty",
                RollHost,
                "◀ 페널티",
                13);
            var neutral = CreateButton(
                "Neutral",
                RollHost,
                "0",
                14);
            var bonus = CreateButton(
                "Bonus",
                RollHost,
                "보너스 ▶",
                13);
            PlaceThreeAtTop(penalty, neutral, bonus, 234f, 40f);
            penalty.onClick.AddListener(() => ChangeBonusPenalty(-1));
            neutral.onClick.AddListener(() => SetBonusPenalty(0));
            bonus.onClick.AddListener(() => ChangeBonusPenalty(1));

            _bonusPenaltyText = CreateText(
                "BonusPenalty",
                RollHost,
                13,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                TextMuted);
            SetTopStretch(
                _bonusPenaltyText.rectTransform,
                18f,
                278f,
                24f);

            _rollButton = CreateButton(
                "RollButton",
                RollHost,
                "굴리기",
                16,
                Accent);
            var rollRect = _rollButton.transform as RectTransform;
            rollRect.anchorMin = new Vector2(0f, 0f);
            rollRect.anchorMax = new Vector2(1f, 0f);
            rollRect.pivot = new Vector2(0.5f, 0f);
            rollRect.anchoredPosition = new Vector2(0f, 16f);
            rollRect.sizeDelta = new Vector2(-36f, 48f);
            _rollButton.onClick.AddListener(
                () => RollRequested?.Invoke());

            _pushButton = CreateButton(
                "PushButton",
                RollHost,
                "밀어붙이기",
                13);
            _luckButton = CreateButton(
                "LuckButton",
                RollHost,
                "운 사용",
                13);
            _pushButton.gameObject.SetActive(false);
            _luckButton.gameObject.SetActive(false);
            SetBonusPenalty(0);
            ClearSource();
        }

        private void SelectDifficulty(PawnCheckDifficulty difficulty)
        {
            if (!_selectedSource.IsValid)
                return;
            _selectedDifficulty = difficulty;
            RefreshDifficulty();
            DifficultyRequested?.Invoke(difficulty);
        }

        private void RefreshDifficulty()
        {
            SetSelected(
                _regularButton,
                _selectedDifficulty == PawnCheckDifficulty.Regular);
            SetSelected(
                _hardButton,
                _selectedDifficulty == PawnCheckDifficulty.Hard);
            SetSelected(
                _extremeButton,
                _selectedDifficulty == PawnCheckDifficulty.Extreme);
            var target = _selectedSource.IsValid
                ? _selectedSource.GetTarget(_selectedDifficulty)
                : 0;
            _targetText.text = _selectedSource.IsValid
                ? $"목표 {target}"
                : "목표 —";
            _rollButton.interactable = _selectedSource.IsValid;
        }

        private void ChangeBonusPenalty(int delta)
        {
            SetBonusPenalty(_bonusPenalty + delta);
        }

        private void SetBonusPenalty(int value)
        {
            _bonusPenalty = Mathf.Clamp(value, -2, 2);
            _bonusPenaltyText.text = _bonusPenalty < 0
                ? $"페널티 {Mathf.Abs(_bonusPenalty)}"
                : _bonusPenalty > 0
                    ? $"보너스 {_bonusPenalty}"
                    : "보너스·페널티 없음";
            BonusPenaltyChanged?.Invoke(_bonusPenalty);
        }

        public void BringTabsToFront()
        {
            if (_leftTabs != null)
                _leftTabs.SetAsLastSibling();
            if (_rightTabs != null)
                _rightTabs.SetAsLastSibling();
        }

        private void ApplyBand(BoardUiWidthBand band)
        {
            var minimal = band == BoardUiWidthBand.Minimal;
            _identityTab.GetComponentInChildren<Text>().text =
                minimal ? "인물" : "인물";
            _inventoryTab.GetComponentInChildren<Text>().text =
                minimal ? "가방" : "가방";
            _profileTab.GetComponentInChildren<Text>().text =
                minimal ? "정보" : "캐릭터 정보";
            _nameText.fontSize = minimal ? 20 : 24;
            _jobText.fontSize = minimal ? 12 : 14;
        }

        private Button CreateButton(
            string name,
            RectTransform parent,
            string label,
            int size,
            Color? color = null)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = color ?? SurfaceElement;
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;

            var text = CreateText(
                "Label",
                rect,
                size,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextMain);
            Stretch(text.rectTransform, 4f);
            return button;
        }

        private Text CreateText(
            string name,
            RectTransform parent,
            int size,
            FontStyle style,
            TextAnchor alignment,
            Color color)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static RectTransform CreateRect(
            string name,
            RectTransform parent)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform CreateSurface(
            string name,
            RectTransform parent,
            Color color)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            root.GetComponent<Image>().color = color;
            return rect;
        }

        private static Image CreateImage(
            string name,
            RectTransform parent,
            Color color)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void AddBorder(RectTransform target)
        {
            if (target == null)
                return;
            var outline = target.GetComponent<Outline>();
            if (outline == null)
                outline = target.gameObject.AddComponent<Outline>();
            outline.effectColor = Border;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }

        private static void SetHostActive(
            RectTransform host,
            bool active)
        {
            if (host == null)
                return;
            host.gameObject.SetActive(active);
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null)
                return;
            var image = button.targetGraphic as Image;
            if (image != null)
            {
                image.color = selected ? Accent : SurfaceElement;
            }
        }

        private static void PlaceThird(
            Button button,
            float min,
            float max)
        {
            var rect = button.transform as RectTransform;
            rect.anchorMin = new Vector2(min, 0f);
            rect.anchorMax = new Vector2(max, 1f);
            rect.offsetMin = new Vector2(1f, 1f);
            rect.offsetMax = new Vector2(-1f, -1f);
        }

        private static void PlaceHalf(
            Button button,
            float min,
            float max)
        {
            PlaceThird(button, min, max);
        }

        private static void PlaceThreeAtTop(
            Button first,
            Button second,
            Button third,
            float top,
            float height)
        {
            PlaceTopColumn(first, 0f, 1f / 3f, top, height);
            PlaceTopColumn(second, 1f / 3f, 2f / 3f, top, height);
            PlaceTopColumn(third, 2f / 3f, 1f, top, height);
        }

        private static void PlaceTopColumn(
            Button button,
            float min,
            float max,
            float top,
            float height)
        {
            var rect = button.transform as RectTransform;
            rect.anchorMin = new Vector2(min, 1f);
            rect.anchorMax = new Vector2(max, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-8f, height);
        }

        private static void SetBottomLeft(
            RectTransform rect,
            float left,
            float bottom,
            float width,
            float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(left, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetBottomRight(
            RectTransform rect,
            float right,
            float bottom,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-right, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopStretch(
            RectTransform rect,
            float horizontal,
            float top,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-horizontal * 2f, height);
        }

        private static void SetBottomStretch(
            RectTransform rect,
            float horizontal,
            float bottom,
            float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = new Vector2(-horizontal * 2f, height);
        }

        private static void Stretch(
            RectTransform rect,
            float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            rect.localScale = Vector3.one;
        }
    }

    [DisallowMultipleComponent]
    public sealed class BoardUiRollSourceDragRelay : MonoBehaviour,
        IBeginDragHandler,
        IEndDragHandler
    {
        private PawnRollSourceWidget _source;
        private Action<PawnCheckSourceData> _begin;
        private Action _end;

        public void Configure(
            PawnRollSourceWidget source,
            Action<PawnCheckSourceData> begin,
            Action end)
        {
            _source = source;
            _begin = begin;
            _end = end;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_source != null &&
                _source.TryGetData(out var source) &&
                source.IsValid)
            {
                _begin?.Invoke(source);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _end?.Invoke();
        }
    }

    public sealed class BoardUiRollDropTarget : MonoBehaviour,
        IDropHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        private Image _image;
        private Color _baseColor;

        public event Action<PawnCheckSourceData> SourceDropped;

        private void Awake()
        {
            _image = GetComponent<Image>();
            if (_image != null)
                _baseColor = _image.color;
        }

        public void OnDrop(PointerEventData eventData)
        {
            var dragged = eventData != null
                ? eventData.pointerDrag
                : null;
            if (dragged == null)
                return;

            var source = dragged.GetComponent<PawnRollSourceWidget>();
            if (source == null)
            {
                source = dragged.GetComponentInParent<
                    PawnRollSourceWidget>();
            }

            if (source != null &&
                source.TryGetData(out var data) &&
                data.IsValid)
            {
                SourceDropped?.Invoke(data);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_image != null)
                _image.color = AccentColor();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image != null)
                _image.color = _baseColor;
        }

        private static Color AccentColor()
        {
            return new Color(0.05f, 0.30f, 0.36f, 0.98f);
        }
    }
}
