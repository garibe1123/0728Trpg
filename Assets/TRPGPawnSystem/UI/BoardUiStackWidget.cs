using System;
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
        public const float TopMargin = 24f;
        public const float DefaultBottomOffset = 147f;
        public const float MinimumPanelHeight = 420f;

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

            float desiredSide;
            float minimumCenter;
            switch (band)
            {
                case BoardUiWidthBand.Base:
                    desiredSide = 420f;
                    minimumCenter = 650f;
                    break;
                case BoardUiWidthBand.Compact:
                    desiredSide = 370f;
                    minimumCenter = 380f;
                    break;
                default:
                    desiredSide = Mathf.Clamp(
                        usable * 0.28f,
                        278f,
                        330f);
                    minimumCenter = 220f;
                    break;
            }

            // 좌우 패널은 항상 같은 너비를 사용한다. 중앙 여유 공간이
            // 부족할 때도 한쪽만 줄이지 않고 동일한 폭으로 축소한다.
            var maximumEqualSide = Mathf.Max(
                220f,
                (usable - minimumCenter) * 0.5f);
            var side = Mathf.Min(desiredSide, maximumEqualSide);
            if (side * 2f > usable)
                side = usable * 0.5f;

            var left = side;
            var right = side;
            var center = Mathf.Max(0f, usable - side * 2f);
            return new BoardUiLayout(
                band,
                effective,
                left,
                right,
                center);
        }
    }

    /// <summary>
    /// 하단 정보 바는 그대로 두고 화면 좌우에 부착되는 캐릭터 대시보드입니다.
    /// 좌측은 인물/가방/캐릭터 정보, 우측은 능력치/기술을 같은 자리에서
    /// 전환하며 중앙은 판정 소스를 드래그할 때만 사용합니다.
    /// </summary>
    public sealed class BoardUiStackWidget : MonoBehaviour
    {
        private static readonly Color Surface =
            new Color(0.028f, 0.047f, 0.056f, 0.985f);
        private static readonly Color SurfaceElement =
            new Color(0.050f, 0.086f, 0.101f, 0.985f);
        private static readonly Color Border =
            new Color(0.20f, 0.42f, 0.48f, 0.48f);
        private static readonly Color Accent =
            new Color(0.08f, 0.48f, 0.60f, 1f);
        private static readonly Color TextMain =
            new Color(0.92f, 0.96f, 0.98f, 1f);
        private static readonly Color TextMuted =
            new Color(0.58f, 0.70f, 0.74f, 1f);

        private const float TabBarHeight = 56f;
        private const float ActionBarHeight = 48f;
        private const float IdentityHeaderHeight = 188f;
        private const float InnerPadding = 12f;
        private const float RollWidth = 470f;
        private const float RollHeight = 380f;

        private Font _font;
        private Image _portraitImage;
        private Text _nameText;
        private Text _jobText;
        private Text _movementText;
        private Text _identityDetailText;
        private Button _identityTab;
        private Button _inventoryTab;
        private Button _profileTab;
        private Button _statsTab;
        private Button _skillsTab;
        private Button _checkRollButton;
        private Button _effectRollButton;
        private RectTransform _leftTabs;
        private RectTransform _rightTabs;
        private RectTransform _rightActionBar;
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
        private RectTransform _effectRollHost;
        private InputField _effectDiceCountInput;
        private InputField _effectDiceSidesInput;
        private InputField _effectModifierInput;
        private Text _effectValidationText;
        private PawnCheckSourceData _selectedSource;
        private PawnCheckDifficulty _selectedDifficulty;
        private int _bonusPenalty;
        private BoardLeftPane _leftPane = BoardLeftPane.Identity;
        private BoardRightPane _rightPane = BoardRightPane.Stats;
        private bool _supportsStats;
        private bool _supportsSkills;
        private bool _supportsInventory;
        private bool _supportsProfile;
        private bool _canRoll;
        private bool _showMovement;
        private bool _showGmInstructions;
        private string _gmInstructions;

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
        public float LeftPanelHeight { get; private set; } =
            BoardUiLayoutCalculator.MinimumPanelHeight;
        public float RightPanelHeight { get; private set; } =
            BoardUiLayoutCalculator.MinimumPanelHeight;
        public float PanelHeight => LeftPanelHeight;
        public float PanelBottomOffset { get; private set; } =
            BoardUiLayoutCalculator.DefaultBottomOffset;
        public BoardLeftPane LeftPane => _leftPane;
        public BoardRightPane RightPane => _rightPane;
        public PawnCheckSourceData SelectedSource => _selectedSource;
        public PawnCheckDifficulty SelectedDifficulty => _selectedDifficulty;
        public int BonusPenalty => _bonusPenalty;
        public bool ShowsRightPanel =>
            _supportsStats || _supportsSkills;

        public event Action<BoardLeftPane> LeftPaneRequested;
        public event Action<BoardRightPane> RightPaneRequested;
        public event Action CheckRollRequested;
        public event Action EffectRollRequested;
        public event Action<PawnEffectRollRequest> EffectRollConfirmed;
        public event Action RollOverlayCloseRequested;
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

        public void ApplyLayout(
            BoardUiLayout layout,
            float bottomOffset,
            float leftPanelHeight,
            float rightPanelHeight)
        {
            Layout = layout;
            PanelBottomOffset = Mathf.Max(0f, bottomOffset);
            LeftPanelHeight = Mathf.Max(
                BoardUiLayoutCalculator.MinimumPanelHeight,
                leftPanelHeight);
            RightPanelHeight = Mathf.Max(
                320f,
                rightPanelHeight);

            var leftOpen = LeftMask.sizeDelta.y > 0.5f;
            var rightOpen = RightMask.sizeDelta.y > 0.5f;

            SetBottomLeft(
                LeftMask,
                BoardUiLayoutCalculator.HorizontalMargin,
                PanelBottomOffset,
                layout.LeftWidth,
                leftOpen ? LeftPanelHeight : 0f);
            LeftContent.sizeDelta = new Vector2(
                layout.LeftWidth,
                LeftPanelHeight);

            SetBottomRight(
                RightMask,
                BoardUiLayoutCalculator.HorizontalMargin,
                PanelBottomOffset,
                layout.RightWidth,
                rightOpen ? RightPanelHeight : 0f);
            RightContent.sizeDelta = new Vector2(
                layout.RightWidth,
                RightPanelHeight);

            var rollWidth = Mathf.Min(
                RollWidth,
                Mathf.Max(340f, layout.CenterWidth - 24f));
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
            _supportsStats = data.HasStats;
            _supportsSkills = data.HasSkills;
            _supportsInventory = data.HasInventory;
            _supportsProfile = data.HasProfile;
            _canRoll = data.CanRoll;
            _showMovement = data.ShowMovement;
            _showGmInstructions = data.ShowGmInstructions;
            _gmInstructions = data.ShowGmInstructions
                ? data.GmInstructions
                : string.Empty;
            ApplyCapabilities();

            _portraitImage.sprite = data.Portrait;
            _portraitImage.enabled = data.Portrait != null;
            _nameText.text = string.IsNullOrWhiteSpace(data.DisplayName)
                ? "캐릭터"
                : data.DisplayName;
            _jobText.text = string.IsNullOrWhiteSpace(data.Description)
                ? "인물 설명 없음"
                : data.Description;
            _movementText.text = data.ShowMovement
                ? "이동 정보 갱신 중"
                : string.Empty;
            RefreshIdentitySummary();
        }


        private void ApplyCapabilities()
        {
            if (_identityTab != null)
            {
                _identityTab.gameObject.SetActive(true);
                var identityRect = _identityTab.GetComponent<RectTransform>();
                if (_supportsInventory || _supportsProfile)
                {
                    PlaceThird(_identityTab, 0f, 0.25f);
                }
                else if (identityRect != null)
                {
                    identityRect.anchorMin = new Vector2(0f, 0f);
                    identityRect.anchorMax = new Vector2(1f, 1f);
                    identityRect.offsetMin = Vector2.zero;
                    identityRect.offsetMax = Vector2.zero;
                }
            }

            if (_inventoryTab != null)
                _inventoryTab.gameObject.SetActive(_supportsInventory);
            if (_profileTab != null)
                _profileTab.gameObject.SetActive(_supportsProfile);
            if (_statsTab != null)
                _statsTab.gameObject.SetActive(_supportsStats);
            if (_skillsTab != null)
                _skillsTab.gameObject.SetActive(_supportsSkills);
            if (_rightActionBar != null)
                _rightActionBar.gameObject.SetActive(_canRoll);

            if (_statsTab != null)
            {
                if (_supportsStats && !_supportsSkills)
                {
                    var rect = _statsTab.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        rect.anchorMin = new Vector2(0f, 0f);
                        rect.anchorMax = new Vector2(1f, 1f);
                        rect.offsetMin = Vector2.zero;
                        rect.offsetMax = Vector2.zero;
                    }
                }
                else
                {
                    PlaceHalf(_statsTab, 0f, 0.5f);
                }
            }

            if (_skillsTab != null && _supportsSkills)
                PlaceHalf(_skillsTab, 0.5f, 1f);

            if (!_supportsInventory && _leftPane == BoardLeftPane.Inventory ||
                !_supportsProfile && _leftPane == BoardLeftPane.Profile)
            {
                SetLeftPane(BoardLeftPane.Identity);
            }

            if (!_supportsSkills && _rightPane == BoardRightPane.Skills)
                SetRightPane(BoardRightPane.Stats);
        }

        public void ClearInfo()
        {
            _portraitImage.sprite = null;
            _portraitImage.enabled = false;
            _nameText.text = string.Empty;
            _jobText.text = string.Empty;
            _movementText.text = string.Empty;
            _identityDetailText.text = string.Empty;
            _showMovement = false;
            _showGmInstructions = false;
            _gmInstructions = string.Empty;
            ClearSource();
        }

        public void SetMovement(float remaining, float maximum)
        {
            if (!_showMovement)
            {
                _movementText.text = string.Empty;
                RefreshIdentitySummary();
                return;
            }

            _movementText.text = maximum > 0.0001f
                ? $"이동 {remaining:0.0}m 남음"
                : "이동 정보 없음";
            RefreshIdentitySummary();
        }

        public void SetLeftPane(BoardLeftPane pane)
        {
            if (pane == BoardLeftPane.Inventory && !_supportsInventory ||
                pane == BoardLeftPane.Profile && !_supportsProfile)
            {
                pane = BoardLeftPane.Identity;
            }

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
            if (pane == BoardRightPane.Skills && !_supportsSkills)
                pane = BoardRightPane.Stats;

            _rightPane = pane;
            SetHostActive(StatsHost, pane == BoardRightPane.Stats);
            SetHostActive(SkillsHost, pane == BoardRightPane.Skills);
            SetSelected(_statsTab, pane == BoardRightPane.Stats);
            SetSelected(_skillsTab, pane == BoardRightPane.Skills);
            BringTabsToFront();
        }

        public void SetPanelsImmediate(bool visible)
        {
            SetPanelsImmediate(visible, ShowsRightPanel);
        }

        public void SetPanelsImmediate(
            bool visible,
            bool showRightPanel)
        {
            var leftHeight = visible ? LeftPanelHeight : 0f;
            var rightVisible = visible && showRightPanel;
            var rightHeight = rightVisible ? RightPanelHeight : 0f;
            LeftMask.gameObject.SetActive(visible);
            RightMask.gameObject.SetActive(rightVisible);
            LeftMask.sizeDelta = new Vector2(
                Layout.LeftWidth,
                leftHeight);
            RightMask.sizeDelta = new Vector2(
                Layout.RightWidth,
                rightHeight);
            LeftContentGroup.alpha = visible ? 1f : 0f;
            RightContentGroup.alpha = rightVisible ? 1f : 0f;
            SetPanelInput(visible);
        }

        public void SetPanelInput(bool enabled)
        {
            LeftContentGroup.interactable = enabled;
            LeftContentGroup.blocksRaycasts = enabled;
            var rightEnabled =
                enabled && RightMask.gameObject.activeSelf;
            RightContentGroup.interactable = rightEnabled;
            RightContentGroup.blocksRaycasts = rightEnabled;
        }

        public void ShowDragTarget(PawnCheckSourceData source)
        {
            _dragSourceText.text = source.IsValid
                ? $"{source.DisplayName}\n여기에 드롭해 판정 준비"
                : "능력치 또는 기술 블록을 여기에 드롭";
            DropPromptHost.gameObject.SetActive(true);
            RollHost.gameObject.SetActive(false);
            _effectRollHost.gameObject.SetActive(false);
            RollOverlayMask.gameObject.SetActive(true);
            RollOverlayGroup.alpha = 1f;
            RollOverlayGroup.interactable = true;
            RollOverlayGroup.blocksRaycasts = true;
            RollOverlayMask.sizeDelta = new Vector2(
                RollOverlayContent.sizeDelta.x,
                210f);
            RollOverlayMask.SetAsLastSibling();
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
            _effectRollHost.gameObject.SetActive(false);
            RollOverlayMask.gameObject.SetActive(true);
            RollOverlayGroup.alpha = 1f;
            RollOverlayGroup.interactable = true;
            RollOverlayGroup.blocksRaycasts = true;
            SelectSource(source);
            RollOverlayMask.SetAsLastSibling();
        }

        public void ShowEmptyRollPanel()
        {
            DropPromptHost.gameObject.SetActive(false);
            RollHost.gameObject.SetActive(true);
            _effectRollHost.gameObject.SetActive(false);
            RollOverlayMask.gameObject.SetActive(true);
            RollOverlayGroup.alpha = 1f;
            RollOverlayGroup.interactable = true;
            RollOverlayGroup.blocksRaycasts = true;
            ClearSource();
            RollOverlayMask.SetAsLastSibling();
        }

        public void ShowEffectRollPanel(
            int defaultDiceCount,
            int defaultDiceSides,
            int modifier)
        {
            DropPromptHost.gameObject.SetActive(false);
            RollHost.gameObject.SetActive(false);
            _effectRollHost.gameObject.SetActive(true);
            RollOverlayMask.gameObject.SetActive(true);
            RollOverlayGroup.alpha = 1f;
            RollOverlayGroup.interactable = true;
            RollOverlayGroup.blocksRaycasts = true;
            _effectDiceCountInput.SetTextWithoutNotify(
                Mathf.Clamp(
                    defaultDiceCount,
                    1,
                    PawnRollService.MaximumDiceCount).ToString());
            _effectDiceSidesInput.SetTextWithoutNotify(
                Mathf.Clamp(
                    defaultDiceSides,
                    2,
                    PawnRollService.MaximumDiceSides).ToString());
            _effectModifierInput.SetTextWithoutNotify(modifier.ToString());
            _effectValidationText.text = string.Empty;
            RollOverlayMask.SetAsLastSibling();
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
            _effectRollHost.gameObject.SetActive(false);
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
            _sourceText.text = "능력치 또는 기술 블록을 드롭";
            _targetText.text = "목표 —";
            _rollButton.interactable = false;
            SetSelected(_regularButton, false);
            SetSelected(_hardButton, false);
            SetSelected(_extremeButton, false);
        }

        public void BringTabsToFront()
        {
            if (_leftTabs != null)
                _leftTabs.SetAsLastSibling();
            if (_rightTabs != null)
                _rightTabs.SetAsLastSibling();
            if (_rightActionBar != null)
                _rightActionBar.SetAsLastSibling();
            if (RollOverlayMask != null &&
                RollOverlayMask.gameObject.activeSelf)
            {
                RollOverlayMask.SetAsLastSibling();
            }
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
            LeftMask.sizeDelta = new Vector2(420f, 0f);
            LeftMask.gameObject.SetActive(false);

            LeftContent = CreateSurface("LeftContent", LeftMask, Surface);
            LeftContent.anchorMin = Vector2.zero;
            LeftContent.anchorMax = Vector2.zero;
            LeftContent.pivot = Vector2.zero;
            LeftContent.sizeDelta = new Vector2(420f, 800f);
            LeftContentGroup =
                LeftContent.gameObject.AddComponent<CanvasGroup>();
            BuildLeftPanel();

            RightMask = CreateSurface("RightMask", RootRect, Surface);
            RightMask.gameObject.AddComponent<RectMask2D>();
            RightMask.anchorMin = Vector2.zero;
            RightMask.anchorMax = Vector2.zero;
            RightMask.pivot = Vector2.zero;
            RightMask.sizeDelta = new Vector2(420f, 0f);
            RightMask.gameObject.SetActive(false);

            RightContent = CreateSurface("RightContent", RightMask, Surface);
            RightContent.anchorMin = Vector2.zero;
            RightContent.anchorMax = Vector2.zero;
            RightContent.pivot = Vector2.zero;
            RightContent.sizeDelta = new Vector2(420f, 800f);
            RightContentGroup =
                RightContent.gameObject.AddComponent<CanvasGroup>();
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

            var portraitHost = CreateSurface(
                "PersistentPortraitHost",
                LeftContent,
                Surface);
            portraitHost.anchorMin = new Vector2(0f, 1f);
            portraitHost.anchorMax = new Vector2(1f, 1f);
            portraitHost.pivot = new Vector2(0.5f, 1f);
            portraitHost.anchoredPosition =
                new Vector2(0f, -TabBarHeight);
            portraitHost.sizeDelta =
                new Vector2(0f, IdentityHeaderHeight);
            BuildPersistentPortrait(portraitHost);

            var body = CreateRect("LeftBody", LeftContent);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(InnerPadding, InnerPadding);
            body.offsetMax = new Vector2(
                -InnerPadding,
                -(TabBarHeight + IdentityHeaderHeight + InnerPadding));
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
            portraitFrame.anchoredPosition = new Vector2(12f, 0f);
            portraitFrame.sizeDelta = new Vector2(160f, 160f);
            AddBorder(portraitFrame);

            _portraitImage = CreateImage(
                "Portrait",
                portraitFrame,
                Color.white);
            Stretch(_portraitImage.rectTransform, 6f);
            _portraitImage.preserveAspect = true;
            _portraitImage.raycastTarget = false;

            _nameText = CreateText(
                "Name",
                host,
                24,
                FontStyle.Bold,
                TextAnchor.UpperLeft,
                TextMain);
            _nameText.rectTransform.anchorMin = Vector2.zero;
            _nameText.rectTransform.anchorMax = Vector2.one;
            _nameText.rectTransform.offsetMin = new Vector2(188f, 112f);
            _nameText.rectTransform.offsetMax = new Vector2(-14f, -18f);

            _jobText = CreateText(
                "Job",
                host,
                14,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                TextMuted);
            _jobText.rectTransform.anchorMin = Vector2.zero;
            _jobText.rectTransform.anchorMax = Vector2.one;
            _jobText.rectTransform.offsetMin = new Vector2(188f, 64f);
            _jobText.rectTransform.offsetMax = new Vector2(-14f, -56f);

            _movementText = CreateText(
                "Movement",
                host,
                14,
                FontStyle.Bold,
                TextAnchor.LowerLeft,
                Accent);
            _movementText.rectTransform.anchorMin = Vector2.zero;
            _movementText.rectTransform.anchorMax = Vector2.one;
            _movementText.rectTransform.offsetMin = new Vector2(188f, 18f);
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
            title.text = "인물 설명";
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
                "상세보기",
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

            _rightActionBar = CreateSurface(
                "RightActionBar",
                RightContent,
                SurfaceElement);
            SetBottomStretch(
                _rightActionBar,
                0f,
                0f,
                ActionBarHeight);

            _checkRollButton = CreateButton(
                "CheckRollButton",
                _rightActionBar,
                "판정 굴림",
                14);
            _effectRollButton = CreateButton(
                "EffectRollButton",
                _rightActionBar,
                "효과 굴림",
                14);
            PlaceHalf(_checkRollButton, 0f, 0.5f);
            PlaceHalf(_effectRollButton, 0.5f, 1f);
            _checkRollButton.onClick.AddListener(
                () => CheckRollRequested?.Invoke());
            _effectRollButton.onClick.AddListener(
                () => EffectRollRequested?.Invoke());

            var body = CreateRect("RightBody", RightContent);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(
                InnerPadding,
                ActionBarHeight + InnerPadding);
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

            DropPromptHost = CreateSurface(
                "DropPromptHost",
                RollOverlayContent,
                SurfaceElement);
            Stretch(DropPromptHost, 16f);
            var dropTarget = DropPromptHost.gameObject.AddComponent<
                BoardUiRollDropTarget>();
            dropTarget.SourceDropped +=
                source => SourceDropped?.Invoke(source);

            _dragSourceText = CreateText(
                "DragSourceText",
                DropPromptHost,
                19,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextMain);
            Stretch(_dragSourceText.rectTransform, 20f);
            _dragSourceText.text =
                "능력치 또는 기술 블록을 여기에 드롭";

            RollHost = CreateRect("RollHost", RollOverlayContent);
            Stretch(RollHost, 16f);
            BuildRollView();

            _effectRollHost = CreateRect(
                "EffectRollHost",
                RollOverlayContent);
            Stretch(_effectRollHost, 16f);
            BuildEffectRollView();
        }

        private void BuildRollView()
        {
            var title = CreateText(
                "RollTitle",
                RollHost,
                21,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMain);
            title.text = "판정 굴림";
            SetTopStretch(title.rectTransform, 0f, 0f, 38f);

            var sourceSurface = CreateSurface(
                "SourceSurface",
                RollHost,
                SurfaceElement);
            SetTopStretch(sourceSurface, 0f, 48f, 54f);
            _sourceText = CreateText(
                "Source",
                sourceSurface,
                15,
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
            SetTopStretch(difficultyLabel.rectTransform, 0f, 112f, 24f);

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
                142f,
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
                14,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Accent);
            SetTopStretch(_targetText.rectTransform, 0f, 194f, 28f);

            var diceLabel = CreateText(
                "DiceLabel",
                RollHost,
                13,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMuted);
            diceLabel.text = "보너스/패널티 주사위";
            SetTopSpan(
                diceLabel.rectTransform,
                0f,
                0.62f,
                228f,
                24f);

            _bonusPenaltyText = CreateText(
                "BonusPenaltyValue",
                RollHost,
                13,
                FontStyle.Bold,
                TextAnchor.MiddleRight,
                TextMuted);
            SetTopSpan(
                _bonusPenaltyText.rectTransform,
                0.58f,
                1f,
                228f,
                24f);

            var penalty = CreateButton(
                "Penalty",
                RollHost,
                "◀ 패널티",
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
            PlaceThreeAtTop(
                penalty,
                neutral,
                bonus,
                258f,
                38f);
            penalty.onClick.AddListener(() => ChangeBonusPenalty(-1));
            neutral.onClick.AddListener(() => SetBonusPenalty(0));
            bonus.onClick.AddListener(() => ChangeBonusPenalty(1));

            _rollButton = CreateButton(
                "RollButton",
                RollHost,
                "굴리기",
                16,
                Accent);
            SetBottomLeft(
                _rollButton.transform as RectTransform,
                0f,
                0f,
                174f,
                42f);
            _rollButton.onClick.AddListener(
                () => RollRequested?.Invoke());

            _pushButton = CreateButton(
                "PushButton",
                RollHost,
                "밀어붙이기",
                13);
            SetBottomRight(
                _pushButton.transform as RectTransform,
                98f,
                0f,
                118f,
                42f);
            _luckButton = CreateButton(
                "LuckButton",
                RollHost,
                "운 사용",
                13);
            SetBottomRight(
                _luckButton.transform as RectTransform,
                0f,
                0f,
                90f,
                42f);
            _pushButton.interactable = false;
            _luckButton.interactable = false;
            SetBonusPenalty(0);
            ClearSource();
        }

        private void BuildEffectRollView()
        {
            var title = CreateText(
                "EffectRollTitle",
                _effectRollHost,
                21,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMain);
            title.text = "효과 굴림";
            SetTopStretch(title.rectTransform, 0f, 0f, 38f);

            var prompt = CreateText(
                "EffectRollPrompt",
                _effectRollHost,
                13,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMuted);
            prompt.text = "주사위 식과 보정치를 입력";
            SetTopStretch(prompt.rectTransform, 0f, 42f, 28f);

            _effectDiceCountInput = CreateIntegerInput(
                "DiceCountInput",
                _effectRollHost,
                "개수 N",
                0f,
                0.31f,
                82f);
            _effectDiceSidesInput = CreateIntegerInput(
                "DiceSidesInput",
                _effectRollHost,
                "면수 d",
                0.345f,
                0.655f,
                82f);
            _effectModifierInput = CreateIntegerInput(
                "ModifierInput",
                _effectRollHost,
                "보정",
                0.69f,
                1f,
                82f);

            var expressionHelp = CreateText(
                "ExpressionHelp",
                _effectRollHost,
                14,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextMain);
            expressionHelp.text = "N d 면수 + 보정";
            SetTopStretch(expressionHelp.rectTransform, 0f, 150f, 34f);

            _effectValidationText = CreateText(
                "EffectValidation",
                _effectRollHost,
                12,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Color(1f, 0.48f, 0.40f, 1f));
            SetTopStretch(
                _effectValidationText.rectTransform,
                0f,
                194f,
                42f);

            var cancelButton = CreateButton(
                "EffectCancelButton",
                _effectRollHost,
                "취소",
                14);
            SetBottomLeft(
                cancelButton.transform as RectTransform,
                0f,
                0f,
                132f,
                46f);
            cancelButton.onClick.AddListener(
                () => RollOverlayCloseRequested?.Invoke());

            var confirmButton = CreateButton(
                "EffectConfirmButton",
                _effectRollHost,
                "굴리기",
                16,
                Accent);
            SetBottomRight(
                confirmButton.transform as RectTransform,
                0f,
                0f,
                176f,
                46f);
            confirmButton.onClick.AddListener(ConfirmEffectRoll);
        }

        private InputField CreateIntegerInput(
            string name,
            RectTransform parent,
            string placeholder,
            float anchorMinX,
            float anchorMaxX,
            float top)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(InputField));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(anchorMinX, 1f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-4f, 56f);

            var image = root.GetComponent<Image>();
            image.color = SurfaceElement;
            AddBorder(rect);

            var valueText = CreateText(
                "Text",
                rect,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextMain);
            valueText.resizeTextForBestFit = true;
            valueText.resizeTextMinSize = 11;
            valueText.resizeTextMaxSize = 16;
            Stretch(valueText.rectTransform, 6f);

            var placeholderText = CreateText(
                "Placeholder",
                rect,
                12,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                TextMuted);
            placeholderText.text = placeholder;
            placeholderText.fontStyle = FontStyle.Italic;
            Stretch(placeholderText.rectTransform, 6f);

            var input = root.GetComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = valueText;
            input.placeholder = placeholderText;
            input.contentType = InputField.ContentType.IntegerNumber;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 5;
            return input;
        }

        private void ConfirmEffectRoll()
        {
            if (!TryParseEffectValue(
                    _effectDiceCountInput.text,
                    1,
                    PawnRollService.MaximumDiceCount,
                    out var diceCount))
            {
                _effectValidationText.text =
                    $"주사위 개수는 1~{PawnRollService.MaximumDiceCount} 사이로 입력해줘.";
                return;
            }

            if (!TryParseEffectValue(
                    _effectDiceSidesInput.text,
                    2,
                    PawnRollService.MaximumDiceSides,
                    out var diceSides))
            {
                _effectValidationText.text =
                    $"주사위 면수는 2~{PawnRollService.MaximumDiceSides} 사이로 입력해줘.";
                return;
            }

            if (!int.TryParse(
                    _effectModifierInput.text,
                    out var modifier))
            {
                _effectValidationText.text =
                    "보정치는 정수로 입력해줘.";
                return;
            }

            _effectValidationText.text = string.Empty;
            EffectRollConfirmed?.Invoke(
                new PawnEffectRollRequest(
                    diceCount,
                    diceSides,
                    Mathf.Clamp(modifier, -999, 999)));
        }

        private static bool TryParseEffectValue(
            string text,
            int minimum,
            int maximum,
            out int value)
        {
            return int.TryParse(text, out value) &&
                   value >= minimum &&
                   value <= maximum;
        }

        private void RefreshIdentitySummary()
        {
            if (_identityDetailText == null)
                return;

            var builder = new System.Text.StringBuilder(512);
            builder.AppendLine(_nameText.text);
            builder.AppendLine();
            builder.AppendLine(_jobText.text);

            if (_showMovement &&
                !string.IsNullOrWhiteSpace(_movementText.text))
            {
                builder.AppendLine();
                builder.AppendLine(_movementText.text);
            }

            if (_showGmInstructions &&
                !string.IsNullOrWhiteSpace(_gmInstructions))
            {
                builder.AppendLine();
                builder.AppendLine("[GM 운용 지침]");
                builder.Append(_gmInstructions.Trim());
            }

            _identityDetailText.text = builder.ToString().TrimEnd();
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
            if (_bonusPenalty < 0)
            {
                _bonusPenaltyText.text =
                    $"{Mathf.Abs(_bonusPenalty)} 패널티";
                _bonusPenaltyText.color =
                    new Color(1f, 0.36f, 0.28f, 1f);
            }
            else if (_bonusPenalty > 0)
            {
                _bonusPenaltyText.text = $"{_bonusPenalty} 보너스";
                _bonusPenaltyText.color = Accent;
            }
            else
            {
                _bonusPenaltyText.text = "없음";
                _bonusPenaltyText.color = TextMuted;
            }
            BonusPenaltyChanged?.Invoke(_bonusPenalty);
        }

        private void ApplyBand(BoardUiWidthBand band)
        {
            var minimal = band == BoardUiWidthBand.Minimal;
            SetButtonLabel(_identityTab, "인물");
            SetButtonLabel(_inventoryTab, "가방");
            SetButtonLabel(
                _profileTab,
                minimal ? "정보" : "캐릭터 정보");
            SetButtonLabel(_statsTab, "상세보기");
            SetButtonLabel(_skillsTab, "기술");
            SetButtonLabel(
                _checkRollButton,
                minimal ? "판정" : "판정 굴림");
            SetButtonLabel(
                _effectRollButton,
                minimal ? "효과" : "효과 굴림");
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
            button.transition = Selectable.Transition.ColorTint;

            var text = CreateText(
                "Label",
                rect,
                size,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextMain);
            text.text = label;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = size;
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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null)
                return;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = value;
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
            var group = host.GetComponent<CanvasGroup>();
            if (group == null)
                group = host.gameObject.AddComponent<CanvasGroup>();
            group.alpha = active ? 1f : 0f;
            group.interactable = active;
            group.blocksRaycasts = active;
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null)
                return;
            var image = button.targetGraphic as Image;
            if (image != null)
                image.color = selected ? Accent : SurfaceElement;
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

        private static void SetTopSpan(
            RectTransform rect,
            float anchorMinX,
            float anchorMaxX,
            float top,
            float height)
        {
            rect.anchorMin = new Vector2(anchorMinX, 1f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-4f, height);
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
            {
                _image.color = new Color(
                    0.05f,
                    0.30f,
                    0.36f,
                    0.98f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image != null)
                _image.color = _baseColor;
        }
    }
}
