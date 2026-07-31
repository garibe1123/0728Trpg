using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    [DisallowMultipleComponent]
    public sealed class PlayerInteractionBarManager : MonoBehaviour
    {
        private const int StartupRefreshFrameLimit = 180;
        private const string RuntimeCanvasName =
            "PlayerInteractionBarCanvas";
        private const string LegacySummaryObjectName =
            "CharacterSummary";
        private const int StatPanelResolveFrameLimit = 8;

        private const float CompactBarWidth = 190f;
        private const float CompactPanelPadding = 6f;
        private const float CompactTitleHeight = 24f;
        private const float CompactSlotSpacing = 4f;
        private const float ActiveSlotWidth = 178f;
        private const float ActiveSlotHeight = 58f;
        private const float InactiveSlotSize = 30f;
        private const float ActivePawnSpriteSize = 42f;
        private const float InactivePawnSpriteSize = 22f;

        private static readonly Vector2 CompactRightTopOffset =
            new Vector2(-12f, -12f);

        [Header("References")]
        [SerializeField, Tooltip(
            "Player 목록과 선택 상태를 제공하는 PawnManager")]
        private PawnManager _pawnManager;

        [Header("Bar Presentation")]
        [SerializeField]
        private PlayerInteractionBarStyle _barStyle =
            PlayerInteractionBarStyle.Default;

        private readonly List<PlayerInteractionSlotData> _slotData =
            new List<PlayerInteractionSlotData>();

        private PlayerInteractionBarWidget _barWidget;
        private GameObject _ownedRuntimeCanvas;
        private Coroutine _startupRefreshRoutine;
        private Coroutine _statPanelResolveRoutine;
        private bool _pawnEventsBound;
        private int _lastBoundPlayerCount = -1;

        public event Action<InteractivePawn> InteractionPlayerChanged;

        public InteractivePawn InteractionPlayer { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            var pawnManager = FindFirst<PawnManager>();
            if (pawnManager == null)
            {
                return;
            }

            var managers = FindAll<PlayerInteractionBarManager>();
            PlayerInteractionBarManager keeper = null;

            for (var index = 0; index < managers.Length; index++)
            {
                var candidate = managers[index];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.gameObject == pawnManager.gameObject)
                {
                    keeper = candidate;
                    break;
                }

                if (keeper == null)
                {
                    keeper = candidate;
                }
            }

            if (keeper == null)
            {
                keeper = pawnManager.gameObject.AddComponent<
                    PlayerInteractionBarManager>();
            }

            keeper._pawnManager = pawnManager;

            for (var index = 0; index < managers.Length; index++)
            {
                var duplicate = managers[index];
                if (duplicate == null || duplicate == keeper)
                {
                    continue;
                }

                duplicate.enabled = false;
                Destroy(duplicate);
            }

            keeper.enabled = true;
            keeper.BeginIntegration();
        }

        private static T FindFirst<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }

        private static T[] FindAll<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<T>(true);
#endif
        }

        private void Awake()
        {
            ResolvePawnManager();
        }

        private void OnEnable()
        {
            BeginIntegration();
        }

        private void OnDisable()
        {
            StopStartupRefresh();
            StopStatPanelResolve();
            UnbindPawnEvents();
            UnbindBar();
            _lastBoundPlayerCount = -1;
        }

        private void OnDestroy()
        {
            StopStartupRefresh();
            StopStatPanelResolve();
            UnbindPawnEvents();
            UnbindBar();

            if (_ownedRuntimeCanvas != null)
            {
                Destroy(_ownedRuntimeCanvas);
                _ownedRuntimeCanvas = null;
            }

            InteractionPlayerChanged = null;
        }

        [ContextMenu("Refresh Player Interaction Bar")]
        public void RefreshBar()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!TryIntegrate())
            {
                BeginIntegration();
            }
        }

        private void BeginIntegration()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StopStartupRefresh();
            TryIntegrate();
            _startupRefreshRoutine = StartCoroutine(
                IntegrateWhenReady());
        }

        private IEnumerator IntegrateWhenReady()
        {
            for (var frame = 0;
                 frame < StartupRefreshFrameLimit;
                 frame++)
            {
                if (TryIntegrate())
                {
                    RemoveForeignPlayerBarUi();

                    if (_pawnManager.PlayerPawns.Count > 0)
                    {
                        _startupRefreshRoutine = null;
                        yield break;
                    }
                }

                yield return null;
            }

            _startupRefreshRoutine = null;
        }

        private bool TryIntegrate()
        {
            if (!ResolvePawnManager())
            {
                return false;
            }

            EnsureOwnedRuntimeBar();
            if (_barWidget == null)
            {
                return false;
            }

            RemoveForeignPlayerBarUi();
            SuppressLegacyRightStatSummary();
            BindPawnEvents();

            var playerCount = _pawnManager.PlayerPawns.Count;
            if (playerCount != _lastBoundPlayerCount)
            {
                RebindBar();
            }
            else
            {
                RefreshInteractionState(false);
            }

            return true;
        }

        private bool ResolvePawnManager()
        {
            if (_pawnManager == null)
            {
                _pawnManager = FindFirst<PawnManager>();
            }

            return _pawnManager != null;
        }

        private void EnsureOwnedRuntimeBar()
        {
            var ownsCurrentBar =
                _ownedRuntimeCanvas != null &&
                _barWidget != null &&
                _barWidget.transform.IsChildOf(
                    _ownedRuntimeCanvas.transform);
            if (ownsCurrentBar)
            {
                return;
            }

            DisableAndDestroyAllPlayerBarUi();

            _barWidget = PlayerInteractionBarWidget.CreateRuntime(
                _barStyle,
                out _ownedRuntimeCanvas);
            if (_ownedRuntimeCanvas != null)
            {
                _ownedRuntimeCanvas.name = RuntimeCanvasName;
            }
        }

        private void DisableAndDestroyAllPlayerBarUi()
        {
            var widgets = FindAll<PlayerInteractionBarWidget>();
            for (var index = 0; index < widgets.Length; index++)
            {
                var widget = widgets[index];
                if (widget == null)
                {
                    continue;
                }

                DisableAndDestroyWidgetRoot(widget);
            }

            var slots = FindAll<PlayerInteractionSlotWidget>();
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot == null)
                {
                    continue;
                }

                slot.gameObject.SetActive(false);
                Destroy(slot.gameObject);
            }

            var canvases = FindAll<Canvas>();
            for (var index = 0; index < canvases.Length; index++)
            {
                var canvas = canvases[index];
                if (canvas == null ||
                    !IsPlayerBarObjectName(canvas.gameObject.name))
                {
                    continue;
                }

                canvas.gameObject.SetActive(false);
                Destroy(canvas.gameObject);
            }

            _barWidget = null;
            _ownedRuntimeCanvas = null;
        }

        private void RemoveForeignPlayerBarUi()
        {
            if (_barWidget == null || _ownedRuntimeCanvas == null)
            {
                return;
            }

            var widgets = FindAll<PlayerInteractionBarWidget>();
            for (var index = 0; index < widgets.Length; index++)
            {
                var widget = widgets[index];
                if (widget == null || widget == _barWidget)
                {
                    continue;
                }

                DisableAndDestroyWidgetRoot(widget);
            }

            var slots = FindAll<PlayerInteractionSlotWidget>();
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot == null ||
                    slot.transform.IsChildOf(_barWidget.transform))
                {
                    continue;
                }

                slot.gameObject.SetActive(false);
                Destroy(slot.gameObject);
            }

            var canvases = FindAll<Canvas>();
            for (var index = 0; index < canvases.Length; index++)
            {
                var canvas = canvases[index];
                if (canvas == null ||
                    canvas.gameObject == _ownedRuntimeCanvas ||
                    !IsPlayerBarObjectName(canvas.gameObject.name))
                {
                    continue;
                }

                canvas.gameObject.SetActive(false);
                Destroy(canvas.gameObject);
            }
        }

        private static bool IsPlayerBarObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            return objectName.IndexOf(
                       "PlayerInteractionBar",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf(
                       "PlayerInteractionProfile",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf(
                       "PlayerProfile",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DisableAndDestroyWidgetRoot(
            PlayerInteractionBarWidget widget)
        {
            if (widget == null)
            {
                return;
            }

            var canvas = widget.GetComponentInParent<Canvas>();
            var target = canvas != null
                ? canvas.gameObject
                : widget.gameObject;
            target.SetActive(false);
            Destroy(target);
        }

        private void BindPawnEvents()
        {
            if (_pawnEventsBound || _pawnManager == null)
            {
                return;
            }

            _pawnManager.InteractiveSelectionChanged +=
                HandleInteractiveSelectionChanged;
            _pawnManager.TurnGroupChanged += HandleTurnGroupChanged;
            _pawnEventsBound = true;
        }

        private void UnbindPawnEvents()
        {
            if (!_pawnEventsBound)
            {
                return;
            }

            if (_pawnManager != null)
            {
                _pawnManager.InteractiveSelectionChanged -=
                    HandleInteractiveSelectionChanged;
                _pawnManager.TurnGroupChanged -=
                    HandleTurnGroupChanged;
            }

            _pawnEventsBound = false;
        }

        private void RebindBar()
        {
            if (_barWidget == null || _pawnManager == null)
            {
                return;
            }

            _barWidget.PlayerClicked -= HandlePlayerClicked;
            _barWidget.Unbind();
            BuildSlotData();
            _barWidget.Bind(_slotData, _barStyle);
            _barWidget.PlayerClicked += HandlePlayerClicked;
            _lastBoundPlayerCount = _slotData.Count;
            RefreshInteractionState(false);
        }

        private void UnbindBar()
        {
            if (_barWidget == null)
            {
                return;
            }

            _barWidget.PlayerClicked -= HandlePlayerClicked;
            _barWidget.Unbind();
        }

        private void BuildSlotData()
        {
            _slotData.Clear();
            var players = _pawnManager.PlayerPawns;
            var selected = ResolveSelectedPlayer(
                _pawnManager.SelectedInteractive);

            for (var index = 0; index < players.Count; index++)
            {
                var pawn = players[index];
                if (pawn == null)
                {
                    continue;
                }

                var definition = pawn.Definition;
                var displayName =
                    definition != null &&
                    !string.IsNullOrWhiteSpace(definition.DisplayName)
                        ? definition.DisplayName
                        : pawn.name;

                _slotData.Add(
                    new PlayerInteractionSlotData(
                        pawn,
                        _slotData.Count + 1,
                        displayName,
                        ResolvePawnSprite(pawn),
                        pawn == selected));
            }
        }

        private static Sprite ResolvePawnSprite(InteractivePawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            // PlayerInteractionBar는 캐릭터 Portrait가 아니라
            // 보드 위 Pawn이 실제로 사용하는 Sprite를 표시합니다.
            var rootRenderer = pawn.GetComponent<SpriteRenderer>();
            if (rootRenderer != null && rootRenderer.sprite != null)
            {
                return rootRenderer.sprite;
            }

            var renderers =
                pawn.GetComponentsInChildren<SpriteRenderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer != null && renderer.sprite != null)
                {
                    return renderer.sprite;
                }
            }

            return null;
        }

        private void HandlePlayerClicked(InteractivePawn pawn)
        {
            if (pawn == null ||
                _pawnManager == null ||
                !IsRegisteredPlayer(pawn))
            {
                return;
            }

            var wasAlreadySelected =
                _pawnManager.SelectedInteractive == pawn;

            _pawnManager.SelectAndFocusInteractive(pawn);

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            RefreshInteractionState(true);
            OpenStatDrawerFromPlayerBar(wasAlreadySelected);
        }

        private void OpenStatDrawerFromPlayerBar(
            bool toggleIfAlreadySelected)
        {
            StopStatPanelResolve();
            _statPanelResolveRoutine = StartCoroutine(
                ResolveAndOpenStatPanel(toggleIfAlreadySelected));
        }

        private IEnumerator ResolveAndOpenStatPanel(
            bool toggleIfAlreadySelected)
        {
            for (var frame = 0;
                 frame < StatPanelResolveFrameLimit;
                 frame++)
            {
                var statPanel = FindFirst<PawnStatPanelWidget>();
                if (statPanel != null)
                {
                    SuppressLegacyRightStatSummary(statPanel);

                    if (toggleIfAlreadySelected)
                    {
                        statPanel.ToggleExpanded();
                    }
                    else
                    {
                        statPanel.SetExpanded(true);
                    }

                    _statPanelResolveRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _statPanelResolveRoutine = null;
        }

        private void SuppressLegacyRightStatSummary()
        {
            var statPanels = FindAll<PawnStatPanelWidget>();
            for (var index = 0; index < statPanels.Length; index++)
            {
                SuppressLegacyRightStatSummary(statPanels[index]);
            }
        }

        private static void SuppressLegacyRightStatSummary(
            PawnStatPanelWidget statPanel)
        {
            if (statPanel == null)
            {
                return;
            }

            var summary = FindDescendantByName(
                statPanel.transform,
                LegacySummaryObjectName);
            if (summary == null)
            {
                return;
            }

            var canvasGroup = summary.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = summary.gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var graphics = summary.GetComponentsInChildren<Graphic>(true);
            for (var index = 0; index < graphics.Length; index++)
            {
                if (graphics[index] != null)
                {
                    graphics[index].raycastTarget = false;
                }
            }
        }

        private static Transform FindDescendantByName(
            Transform root,
            string objectName)
        {
            if (root == null || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (string.Equals(
                        child.name,
                        objectName,
                        StringComparison.Ordinal))
                {
                    return child;
                }

                var nested = FindDescendantByName(child, objectName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private void HandleInteractiveSelectionChanged(
            InteractivePawn selectedPawn)
        {
            SuppressLegacyRightStatSummary();
            StartCoroutine(SuppressLegacySummaryNextFrame());

            if (_pawnManager == null || _barWidget == null)
            {
                return;
            }

            if (_pawnManager.PlayerPawns.Count != _lastBoundPlayerCount)
            {
                RebindBar();
                return;
            }

            ApplyInteractionPlayer(
                ResolveSelectedPlayer(selectedPawn),
                true);
        }

        private IEnumerator SuppressLegacySummaryNextFrame()
        {
            yield return null;
            SuppressLegacyRightStatSummary();
        }

        private void HandleTurnGroupChanged(
            PawnManager.TurnGroup group,
            IReadOnlyList<InteractivePawn> groupPawns)
        {
            RebindBar();
        }

        private void RefreshInteractionState(bool animate)
        {
            if (_pawnManager == null)
            {
                ApplyInteractionPlayer(null, animate);
                return;
            }

            ApplyInteractionPlayer(
                ResolveSelectedPlayer(
                    _pawnManager.SelectedInteractive),
                animate);
        }

        private void ApplyInteractionPlayer(
            InteractivePawn selectedPlayer,
            bool animate)
        {
            InteractionPlayer = selectedPlayer;
            _barWidget?.SetInteractionTarget(selectedPlayer, animate);
            ApplyCompactBarLayout(selectedPlayer);
            InteractionPlayerChanged?.Invoke(selectedPlayer);
        }

        private void ApplyCompactBarLayout(
            InteractivePawn selectedPlayer)
        {
            if (_barWidget == null)
            {
                return;
            }

            var panelRect =
                _barWidget.GetComponent<RectTransform>();
            if (panelRect == null)
            {
                return;
            }

            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = CompactRightTopOffset;

            var titleTransform = FindDescendantByName(
                _barWidget.transform,
                "Title");
            var titleText = titleTransform != null
                ? titleTransform.GetComponent<Text>()
                : null;
            var titleRect = titleTransform as RectTransform;
            if (titleText != null)
            {
                titleText.fontSize = 13;
                titleText.alignment = TextAnchor.MiddleLeft;
            }

            var contentTransform = FindDescendantByName(
                _barWidget.transform,
                "Content");
            var contentRect = contentTransform as RectTransform;
            var contentLayout = contentTransform != null
                ? contentTransform.GetComponent<VerticalLayoutGroup>()
                : null;
            if (contentLayout != null)
            {
                contentLayout.spacing = CompactSlotSpacing;
                contentLayout.childAlignment = TextAnchor.UpperRight;
                contentLayout.childControlWidth = false;
                contentLayout.childForceExpandWidth = false;
                contentLayout.childControlHeight = false;
                contentLayout.childForceExpandHeight = false;
            }

            var slots = _barWidget.GetComponentsInChildren<
                PlayerInteractionSlotWidget>(true);
            var visibleCount = 0;
            var slotsHeight = 0f;

            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot == null || !slot.gameObject.activeSelf)
                {
                    continue;
                }

                var active =
                    selectedPlayer != null &&
                    slot.BoundPawn == selectedPlayer;
                ApplyCompactSlotLayout(slot, active);

                slotsHeight += active
                    ? ActiveSlotHeight
                    : InactiveSlotSize;
                visibleCount++;
            }

            if (visibleCount > 1)
            {
                slotsHeight +=
                    (visibleCount - 1) * CompactSlotSpacing;
            }

            var panelHeight =
                CompactPanelPadding * 2f +
                CompactTitleHeight +
                slotsHeight;
            panelRect.sizeDelta = new Vector2(
                CompactBarWidth,
                panelHeight);

            if (titleRect != null)
            {
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.pivot = new Vector2(0.5f, 1f);
                titleRect.anchoredPosition = new Vector2(
                    0f,
                    -CompactPanelPadding);
                titleRect.sizeDelta = new Vector2(
                    -CompactPanelPadding * 2f,
                    CompactTitleHeight);
            }

            if (contentRect != null)
            {
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = new Vector2(
                    0f,
                    -CompactPanelPadding - CompactTitleHeight);
                contentRect.sizeDelta = new Vector2(
                    -CompactPanelPadding * 2f,
                    slotsHeight);

                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    contentRect);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }

        private static void ApplyCompactSlotLayout(
            PlayerInteractionSlotWidget slot,
            bool active)
        {
            var slotRect = slot.GetComponent<RectTransform>();
            var layout = slot.GetComponent<LayoutElement>();
            var targetWidth = active
                ? ActiveSlotWidth
                : InactiveSlotSize;
            var targetHeight = active
                ? ActiveSlotHeight
                : InactiveSlotSize;

            if (layout != null)
            {
                layout.preferredWidth = targetWidth;
                layout.minWidth = targetWidth;
                layout.flexibleWidth = 0f;
                layout.preferredHeight = targetHeight;
                layout.minHeight = targetHeight;
                layout.flexibleHeight = 0f;
            }

            if (slotRect != null)
            {
                slotRect.sizeDelta = new Vector2(
                    targetWidth,
                    targetHeight);
            }

            var portraitTransform = FindDescendantByName(
                slot.transform,
                "Portrait");
            var portraitRect = portraitTransform as RectTransform;
            if (portraitRect != null)
            {
                var spriteSize = active
                    ? ActivePawnSpriteSize
                    : InactivePawnSpriteSize;
                portraitRect.anchorMin = active
                    ? new Vector2(0f, 0.5f)
                    : new Vector2(0.5f, 0.5f);
                portraitRect.anchorMax = portraitRect.anchorMin;
                portraitRect.pivot = active
                    ? new Vector2(0f, 0.5f)
                    : new Vector2(0.5f, 0.5f);
                portraitRect.anchoredPosition = active
                    ? new Vector2(8f, 0f)
                    : Vector2.zero;
                portraitRect.sizeDelta =
                    Vector2.one * spriteSize;
            }

            var orderTransform = FindDescendantByName(
                slot.transform,
                "Order");
            var orderText = orderTransform != null
                ? orderTransform.GetComponent<Text>()
                : null;
            if (orderText != null)
            {
                orderText.enabled = active;
                orderText.fontSize = 11;
            }

            var orderRect = orderTransform as RectTransform;
            if (orderRect != null && active)
            {
                orderRect.anchorMin = new Vector2(0f, 0f);
                orderRect.anchorMax = new Vector2(0f, 1f);
                orderRect.pivot = new Vector2(0f, 0.5f);
                orderRect.anchoredPosition = new Vector2(54f, 0f);
                orderRect.sizeDelta = new Vector2(26f, 0f);
            }

            var nameTransform = FindDescendantByName(
                slot.transform,
                "Name");
            var nameText = nameTransform != null
                ? nameTransform.GetComponent<Text>()
                : null;
            if (nameText != null)
            {
                nameText.enabled = active;
                nameText.fontSize = 15;
                nameText.resizeTextMinSize = 10;
                nameText.resizeTextMaxSize = 15;
            }

            var nameRect = nameTransform as RectTransform;
            if (nameRect != null && active)
            {
                nameRect.anchorMin = new Vector2(0f, 0f);
                nameRect.anchorMax = new Vector2(1f, 1f);
                nameRect.offsetMin = new Vector2(80f, 6f);
                nameRect.offsetMax = new Vector2(-8f, -6f);
            }

            var accentTransform = FindDescendantByName(
                slot.transform,
                "InteractionAccent");
            var accentRect = accentTransform as RectTransform;
            if (accentRect != null)
            {
                accentRect.sizeDelta = new Vector2(
                    active ? 3f : 2f,
                    0f);
            }
        }

        private InteractivePawn ResolveSelectedPlayer(
            InteractivePawn selectedPawn)
        {
            return IsRegisteredPlayer(selectedPawn)
                ? selectedPawn
                : null;
        }

        private bool IsRegisteredPlayer(InteractivePawn pawn)
        {
            if (pawn == null || _pawnManager == null)
            {
                return false;
            }

            var players = _pawnManager.PlayerPawns;
            for (var index = 0; index < players.Count; index++)
            {
                if (players[index] == pawn)
                {
                    return true;
                }
            }

            return false;
        }

        private void StopStatPanelResolve()
        {
            if (_statPanelResolveRoutine == null)
            {
                return;
            }

            StopCoroutine(_statPanelResolveRoutine);
            _statPanelResolveRoutine = null;
        }

        private void StopStartupRefresh()
        {
            if (_startupRefreshRoutine == null)
            {
                return;
            }

            StopCoroutine(_startupRefreshRoutine);
            _startupRefreshRoutine = null;
        }
    }
}
