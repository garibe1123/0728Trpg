using System;
using System.Collections.Generic;
using TMPro;
using Trpg.Domain.Stats;
using Trpg.Pawns;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.UI.Stats
{
    public readonly struct StatEntryViewData
    {
        public readonly string StatId;
        public readonly string DisplayName;
        public readonly string ValueLabel;
        public readonly string Tooltip;
        public readonly bool CanAdjust;
        public readonly bool CanDirectEdit;
        public readonly double AdjustStep;
        public readonly double EditableValue;

        public StatEntryViewData(
            string statId,
            string displayName,
            string valueLabel,
            string tooltip,
            bool canAdjust,
            bool canDirectEdit,
            double adjustStep,
            double editableValue)
        {
            StatId = statId;
            DisplayName = displayName;
            ValueLabel = valueLabel;
            Tooltip = tooltip;
            CanAdjust = canAdjust;
            CanDirectEdit = canDirectEdit;
            AdjustStep = adjustStep;
            EditableValue = editableValue;
        }
    }

    public sealed class StatGroupViewData
    {
        public string Title;
        public readonly List<StatEntryViewData> Entries = new List<StatEntryViewData>();
    }

    public sealed class StatSheetViewData
    {
        public readonly List<StatGroupViewData> BaseGroups = new List<StatGroupViewData>();
        public readonly List<StatGroupViewData> RuntimeGroups = new List<StatGroupViewData>();
    }

    public sealed class CharacterStatController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private PlayerStatState _initialState;
        [SerializeField, Tooltip(
            "PlayerStatState.SetActive()가 호출되면 활성 Pawn의 스탯을 자동 표시합니다.")]
        private bool _bindActiveStateAutomatically = true;
        [SerializeField, Tooltip(
            "활성 Pawn이 없을 때 감출 스탯 패널의 자식 Root입니다. Controller 오브젝트 자체는 지정하지 마십시오.")]
        private GameObject _visibleRoot;

        [Header("Tabs")]
        [SerializeField] private Button _baseTabButton;
        [SerializeField] private Button _runtimeTabButton;
        [SerializeField] private GameObject _baseTabRoot;
        [SerializeField] private GameObject _runtimeTabRoot;
        [SerializeField] private Transform _baseContentRoot;
        [SerializeField] private Transform _runtimeContentRoot;

        [Header("List Prefabs")]
        [SerializeField, Tooltip("분류 제목에 사용할 TMP_Text 프리팹입니다.")]
        private TMP_Text _groupTitlePrefab;
        [SerializeField] private StatEntryWidget _entryPrefab;

        [Header("Tooltip")]
        [SerializeField] private RectTransform _tooltipPanel;
        [SerializeField] private TMP_Text _tooltipBodyText;
        [SerializeField] private Vector2 _tooltipOffset = new Vector2(18f, -18f);

        [Header("Permission")]
        [SerializeField, Tooltip(
            "현재는 테스트를 위해 true입니다. 추후 GM 권한 판정 결과로 SetGmMode를 호출하십시오.")]
        private bool _isGmMode = true;

        private readonly List<TMP_Text> _groupTitlePool = new List<TMP_Text>();
        private readonly List<StatEntryWidget> _entryPool = new List<StatEntryWidget>();
        private PlayerStatState _boundState;
        private int _usedGroupTitleCount;
        private int _usedEntryCount;

        private void Awake()
        {
            HideTooltip();
            SetPanelVisible(false);
        }

        private void Start()
        {
            if (_boundState != null)
                return;

            if (_bindActiveStateAutomatically && PlayerStatState.ActiveState != null)
                Bind(PlayerStatState.ActiveState);
            else if (_initialState != null)
                Bind(_initialState);
        }

        private void OnEnable()
        {
            if (_bindActiveStateAutomatically)
                PlayerStatState.ActiveStateChanged += OnActiveStateChanged;

            _baseTabButton.onClick.AddListener(ShowBaseTab);
            _runtimeTabButton.onClick.AddListener(ShowRuntimeTab);
            ShowBaseTab();

            if (_bindActiveStateAutomatically && PlayerStatState.ActiveState != null)
                Bind(PlayerStatState.ActiveState);

            if (_boundState != null)
            {
                _boundState.Changed -= Refresh;
                _boundState.Changed += Refresh;
                Refresh();
            }
            else
            {
                SetPanelVisible(false);
            }
        }

        private void OnDisable()
        {
            if (_bindActiveStateAutomatically)
                PlayerStatState.ActiveStateChanged -= OnActiveStateChanged;

            _baseTabButton.onClick.RemoveListener(ShowBaseTab);
            _runtimeTabButton.onClick.RemoveListener(ShowRuntimeTab);
            if (_boundState != null)
                _boundState.Changed -= Refresh;
            HideTooltip();
        }

        public void Bind(PlayerStatState state)
        {
            if (ReferenceEquals(_boundState, state))
            {
                Refresh();
                return;
            }

            if (_boundState != null)
                _boundState.Changed -= Refresh;

            _boundState = state;
            if (_boundState == null)
            {
                ClearList();
                HideTooltip();
                SetPanelVisible(false);
                return;
            }

            if (!_boundState.IsInitialized)
                _boundState.Initialize();

            if (!_boundState.IsInitialized)
            {
                ClearList();
                SetPanelVisible(false);
                return;
            }

            if (isActiveAndEnabled)
                _boundState.Changed += Refresh;

            SetPanelVisible(true);
            Refresh();
        }

        public void Unbind()
        {
            if (_boundState != null)
                _boundState.Changed -= Refresh;
            _boundState = null;
            ClearList();
            HideTooltip();
            SetPanelVisible(false);
        }

        [Obsolete("SetGmMode를 사용하십시오.")]
        public void SetCanAdjust(bool canAdjust)
        {
            SetGmMode(canAdjust);
        }

        public void SetGmMode(bool isGmMode)
        {
            if (_isGmMode == isGmMode)
                return;

            _isGmMode = isGmMode;
            Refresh();
        }

        private void Refresh()
        {
            if (_boundState == null || !_boundState.IsInitialized)
                return;

            try
            {
                Render(BuildViewData(_boundState.Runtime));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private StatSheetViewData BuildViewData(StatRuntimeState runtime)
        {
            var viewData = new StatSheetViewData();
            var presentation = new StatPresentationService(runtime);
            var authority = TRPGSessionAuthority.Instance;
            var effectiveGmMode =
                authority != null && authority.IsOnline
                    ? authority.IsLocalGameMaster
                    : _isGmMode;
            var baseGroups = new Dictionary<string, StatGroupViewData>(StringComparer.Ordinal);
            var runtimeGroups = new Dictionary<string, StatGroupViewData>(StringComparer.Ordinal);
            var baseOrder = new List<string>();
            var runtimeOrder = new List<string>();
            var stats = new List<IStatDefinition>(runtime.Template.Stats);
            stats.Sort((left, right) => left.SortOrder.CompareTo(right.SortOrder));

            for (var i = 0; i < stats.Count; i++)
            {
                var definition = stats[i];
                var isRuntime = definition.Source == StatValueSource.Runtime;
                var groups = isRuntime ? runtimeGroups : baseGroups;
                var order = isRuntime ? runtimeOrder : baseOrder;
                var category = string.IsNullOrWhiteSpace(definition.Category)
                    ? "기타"
                    : definition.Category;

                if (!groups.TryGetValue(category, out var group))
                {
                    group = new StatGroupViewData { Title = category };
                    groups.Add(category, group);
                    order.Add(category);
                }

                var canPlayerEdit =
                    isRuntime &&
                    IsPlayerEditableCurrentStat(runtime, definition);
                var canEdit =
                    effectiveGmMode
                        ? definition.Source == StatValueSource.Base ||
                          isRuntime
                        : canPlayerEdit;

                group.Entries.Add(new StatEntryViewData(
                    definition.Id,
                    definition.DisplayName,
                    presentation.FormatValue(definition.Id),
                    presentation.BuildTooltip(definition.Id),
                    canEdit && isRuntime,
                    canEdit,
                    definition.AdjustStep,
                    runtime.GetNumber(definition.Id)));
            }

            AppendGroups(viewData.BaseGroups, baseGroups, baseOrder);
            AppendGroups(viewData.RuntimeGroups, runtimeGroups, runtimeOrder);
            return viewData;
        }

        private void Render(StatSheetViewData viewData)
        {
            ClearList();
            RenderGroups(viewData.BaseGroups, _baseContentRoot);
            RenderGroups(viewData.RuntimeGroups, _runtimeContentRoot);
        }

        private void RenderGroups(List<StatGroupViewData> groups, Transform parent)
        {
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var title = GetGroupTitle(parent);
                title.text = group.Title;
                title.gameObject.SetActive(true);

                for (var entryIndex = 0; entryIndex < group.Entries.Count; entryIndex++)
                {
                    var entry = GetEntry(parent);
                    entry.Bind(group.Entries[entryIndex]);
                    entry.AdjustmentRequested += OnAdjustmentRequested;
                    entry.ValueEditRequested += OnValueEditRequested;
                    entry.TooltipOpened += ShowTooltip;
                    entry.TooltipClosed += HideTooltip;
                }
            }
        }

        private TMP_Text GetGroupTitle(Transform parent)
        {
            TMP_Text title;
            if (_usedGroupTitleCount < _groupTitlePool.Count)
            {
                title = _groupTitlePool[_usedGroupTitleCount];
            }
            else
            {
                title = Instantiate(_groupTitlePrefab, parent);
                _groupTitlePool.Add(title);
            }

            _usedGroupTitleCount++;
            title.transform.SetParent(parent, false);
            return title;
        }

        private StatEntryWidget GetEntry(Transform parent)
        {
            StatEntryWidget entry;
            if (_usedEntryCount < _entryPool.Count)
            {
                entry = _entryPool[_usedEntryCount];
            }
            else
            {
                entry = Instantiate(_entryPrefab, parent);
                _entryPool.Add(entry);
            }

            _usedEntryCount++;
            entry.transform.SetParent(parent, false);
            return entry;
        }

        private void ClearList()
        {
            for (var i = 0; i < _entryPool.Count; i++)
                _entryPool[i].Unbind();

            for (var i = 0; i < _groupTitlePool.Count; i++)
            {
                _groupTitlePool[i].text = string.Empty;
                _groupTitlePool[i].gameObject.SetActive(false);
            }

            _usedEntryCount = 0;
            _usedGroupTitleCount = 0;
        }

        private void OnAdjustmentRequested(
            string statId,
            double delta)
        {
            if (_boundState?.Runtime == null)
                return;

            double current;
            try
            {
                current = _boundState.Runtime.GetNumber(statId);
            }
            catch (Exception)
            {
                return;
            }

            SubmitStatValue(statId, current + delta);
        }

        private void OnValueEditRequested(
            string statId,
            double value)
        {
            SubmitStatValue(statId, value);
        }

        private void SubmitStatValue(
            string statId,
            double value)
        {
            if (_boundState == null ||
                string.IsNullOrWhiteSpace(statId))
            {
                return;
            }

            var pawn = _boundState.GetComponent<InteractivePawn>();
            if (pawn == null)
            {
                pawn = _boundState.GetComponentInParent<
                    InteractivePawn>(true);
            }
            if (pawn == null)
            {
                pawn = _boundState.GetComponentInChildren<
                    InteractivePawn>(true);
            }

            var authority = TRPGSessionAuthority.Instance;
            if (authority != null &&
                authority.ShouldRouteClientStatChange)
            {
                authority.RequestStatChange(
                    pawn,
                    statId,
                    value);
                return;
            }

            _boundState.TrySetAuthoritativeDisplayedValue(
                statId,
                value);
        }

        private static bool IsPlayerEditableCurrentStat(
            StatRuntimeState runtime,
            IStatDefinition definition)
        {
            if (runtime == null ||
                definition == null ||
                definition.Source != StatValueSource.Runtime)
            {
                return false;
            }

            var id = definition.Id;
            var template = runtime.Template;
            return definition.IsAdjustable ||
                   string.Equals(
                       id,
                       template.GetStatId(StatRole.HealthCurrent),
                       StringComparison.Ordinal) ||
                   string.Equals(
                       id,
                       template.GetStatId(StatRole.MagicCurrent),
                       StringComparison.Ordinal) ||
                   string.Equals(
                       id,
                       template.GetStatId(StatRole.SanityCurrent),
                       StringComparison.Ordinal) ||
                   string.Equals(
                       id,
                       template.GetStatId(StatRole.LuckCurrent),
                       StringComparison.Ordinal);
        }

        private void OnActiveStateChanged(PlayerStatState state)
        {
            Bind(state);
        }

        private void SetPanelVisible(bool visible)
        {
            if (_visibleRoot != null && _visibleRoot != gameObject)
            {
                _visibleRoot.SetActive(visible);
                return;
            }

            if (!visible)
            {
                _baseTabRoot.SetActive(false);
                _runtimeTabRoot.SetActive(false);
                return;
            }

            ShowBaseTab();
        }

        private void ShowTooltip(string text, Vector2 screenPosition)
        {
            if (_tooltipPanel == null || _tooltipBodyText == null)
                return;

            _tooltipBodyText.text = text;
            _tooltipPanel.position = screenPosition + _tooltipOffset;
            _tooltipPanel.gameObject.SetActive(true);
            ClampTooltipToCanvas();
        }

        private void HideTooltip()
        {
            if (_tooltipPanel != null)
                _tooltipPanel.gameObject.SetActive(false);
        }

        private void ClampTooltipToCanvas()
        {
            var canvas = _tooltipPanel.GetComponentInParent<Canvas>();
            var canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null)
                return;

            var tooltipCorners = new Vector3[4];
            var canvasCorners = new Vector3[4];
            _tooltipPanel.GetWorldCorners(tooltipCorners);
            canvasRect.GetWorldCorners(canvasCorners);

            var offset = Vector3.zero;
            if (tooltipCorners[2].x > canvasCorners[2].x)
                offset.x = canvasCorners[2].x - tooltipCorners[2].x;
            if (tooltipCorners[0].x < canvasCorners[0].x)
                offset.x = canvasCorners[0].x - tooltipCorners[0].x;
            if (tooltipCorners[2].y > canvasCorners[2].y)
                offset.y = canvasCorners[2].y - tooltipCorners[2].y;
            if (tooltipCorners[0].y < canvasCorners[0].y)
                offset.y = canvasCorners[0].y - tooltipCorners[0].y;

            _tooltipPanel.position += offset;
        }

        private void ShowBaseTab()
        {
            _baseTabRoot.SetActive(true);
            _runtimeTabRoot.SetActive(false);
            _baseTabButton.interactable = false;
            _runtimeTabButton.interactable = true;
        }

        private void ShowRuntimeTab()
        {
            _baseTabRoot.SetActive(false);
            _runtimeTabRoot.SetActive(true);
            _baseTabButton.interactable = true;
            _runtimeTabButton.interactable = false;
        }

        private static void AppendGroups(
            List<StatGroupViewData> destination,
            Dictionary<string, StatGroupViewData> source,
            List<string> order)
        {
            for (var i = 0; i < order.Count; i++)
                destination.Add(source[order[i]]);
        }
    }
}
