using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 기존 스탯/스킬 행에 붙어 클릭 선택과 드래그를 제공합니다.
    /// 기존 Button, InputField, 레이아웃 데이터는 변경하지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnRollSourceWidget : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerClickHandler
    {
        private PawnCheckSourceData _data;
        private bool _isBound;
        private bool _interactionEnabled;
        private CanvasGroup _sourceCanvasGroup;
        private bool _previousBlocksRaycasts;
        private RectTransform _dragGhost;
        private RectTransform _rootCanvasRect;

        public static event Action<PawnCheckSourceData> SourceSelected;

        public bool IsBound => _isBound;

        public bool TryGetData(out PawnCheckSourceData data)
        {
            data = _data;
            return _isBound && data.IsValid;
        }

        public void Bind(in PawnCheckSourceData data)
        {
            _data = data;
            _isBound = data.IsValid;
            enabled = _isBound && _interactionEnabled;
        }

        public void Unbind()
        {
            EndDragVisual();
            _data = default;
            _isBound = false;
            _interactionEnabled = false;
            enabled = false;
        }

        public void SetInteractionEnabled(bool value)
        {
            _interactionEnabled = value;
            enabled = _isBound && _interactionEnabled;
            if (!enabled)
                EndDragVisual();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!CanUse(eventData) ||
                eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            SourceSelected?.Invoke(_data);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!CanUse(eventData))
                return;

            var canvas = GetComponentInParent<Canvas>();
            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            _rootCanvasRect = rootCanvas != null
                ? rootCanvas.transform as RectTransform
                : null;
            if (_rootCanvasRect == null)
                return;

            _sourceCanvasGroup = GetComponent<CanvasGroup>();
            if (_sourceCanvasGroup == null)
            {
                _sourceCanvasGroup =
                    gameObject.AddComponent<CanvasGroup>();
            }

            _previousBlocksRaycasts =
                _sourceCanvasGroup.blocksRaycasts;
            _sourceCanvasGroup.blocksRaycasts = false;
            _dragGhost = CreateGhost(_rootCanvasRect);
            PositionGhost(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_interactionEnabled)
                PositionGhost(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            EndDragVisual();
        }

        private bool CanUse(PointerEventData eventData)
        {
            return _isBound &&
                   _interactionEnabled &&
                   !IsInputFieldInteraction(eventData);
        }

        private static bool IsInputFieldInteraction(
            PointerEventData eventData)
        {
            if (eventData == null)
                return false;

            var target = eventData.pointerPressRaycast.gameObject;
            if (target == null)
                target = eventData.pointerCurrentRaycast.gameObject;

            return target != null &&
                   target.GetComponentInParent<InputField>() != null;
        }

        private void PositionGhost(PointerEventData eventData)
        {
            if (_dragGhost == null ||
                _rootCanvasRect == null ||
                eventData == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rootCanvasRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var localPoint))
            {
                _dragGhost.anchoredPosition =
                    localPoint + new Vector2(18f, -18f);
            }
        }

        private RectTransform CreateGhost(RectTransform parent)
        {
            var root = new GameObject(
                "CheckSourceDragGhost",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(270f, 66f);
            rect.SetAsLastSibling();

            root.GetComponent<Image>().color =
                new Color(0.035f, 0.18f, 0.22f, 0.97f);
            var group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 6f);
            labelRect.offsetMax = new Vector2(-12f, -6f);

            var label = labelObject.GetComponent<Text>();
            label.font = ResolveFont();
            label.fontSize = 15;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text =
                $"{_data.DisplayName}\n" +
                $"일반 {_data.Regular} · 어려움 {_data.Hard} · " +
                $"극단적 {_data.Extreme}";
            return rect;
        }

        private Font ResolveFont()
        {
            var ownText = GetComponentInChildren<Text>(true);
            if (ownText != null && ownText.font != null)
                return ownText.font;

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

        private void EndDragVisual()
        {
            if (_sourceCanvasGroup != null)
            {
                _sourceCanvasGroup.blocksRaycasts =
                    _previousBlocksRaycasts;
            }

            if (_dragGhost != null)
                Destroy(_dragGhost.gameObject);

            _dragGhost = null;
            _rootCanvasRect = null;
            _sourceCanvasGroup = null;
        }

        private void OnDisable()
        {
            EndDragVisual();
        }
    }

    /// <summary>
    /// 기존 공개 UI 데이터 구조를 교체하지 않고 생성된 스탯/스킬 행을
    /// 판정 원본으로 연결합니다.
    /// </summary>
    internal static class PawnCheckSourceRuntimeBinder
    {
        private static readonly string[] IgnoredLabels =
        {
            "스탯", "스킬", "기술", "보통", "일반", "어려움",
            "극단", "극단적", "추가", "+", "캐릭터 스탯"
        };

        public static void BindExistingRows(
            Canvas rootCanvas,
            List<PawnRollSourceWidget> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            for (var index = 0; index < destination.Count; index++)
            {
                if (destination[index] != null)
                    destination[index].SetInteractionEnabled(false);
            }
            destination.Clear();

            if (rootCanvas == null)
                return;

            var panels = rootCanvas.GetComponentsInChildren<
                PawnStatPanelWidget>(true);
            for (var panelIndex = 0;
                 panelIndex < panels.Length;
                 panelIndex++)
            {
                ArrangeDerivedStatRows(panels[panelIndex]);
                BindPanel(panels[panelIndex], destination);
            }
        }

        private static void BindPanel(
            PawnStatPanelWidget panel,
            ICollection<PawnRollSourceWidget> destination)
        {
            if (panel == null)
                return;

            var buttons = panel.GetComponentsInChildren<Button>(true);
            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                if (button == null)
                    continue;

                if (!TryReadRow(button, out var source))
                {
                    button.GetComponent<PawnRollSourceWidget>()?.Unbind();
                    continue;
                }

                var widget = button.GetComponent<PawnRollSourceWidget>();
                if (widget == null)
                {
                    widget =
                        button.gameObject.AddComponent<
                            PawnRollSourceWidget>();
                }

                widget.Bind(source);
                destination.Add(widget);
            }
        }

        private static void ArrangeDerivedStatRows(
            PawnStatPanelWidget panel)
        {
            if (panel == null)
                return;

            var content = FindStatContent(panel);
            if (content == null)
                return;

            var existingPairs = new List<RectTransform>();
            for (var index = 0; index < content.childCount; index++)
            {
                var child = content.GetChild(index) as RectTransform;
                if (child != null &&
                    child.gameObject.name.StartsWith(
                        "DerivedStatPair",
                        StringComparison.Ordinal))
                {
                    existingPairs.Add(child);
                }
            }

            // 이전 갱신에서 묶였던 행을 먼저 원본 Content로 되돌린다.
            for (var pairIndex = 0;
                 pairIndex < existingPairs.Count;
                 pairIndex++)
            {
                var pair = existingPairs[pairIndex];
                while (pair.childCount > 0)
                    pair.GetChild(0).SetParent(content, false);
                pair.gameObject.SetActive(false);
            }

            var statRows = new List<Button>();
            var buttons = panel.GetComponentsInChildren<Button>(true);
            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                if (button == null ||
                    !string.Equals(
                        button.gameObject.name,
                        "StatEntry",
                        StringComparison.OrdinalIgnoreCase) ||
                    HasAncestorComponentNamed(
                        button.transform,
                        "PawnSkillPanelWidget"))
                {
                    continue;
                }

                statRows.Add(button);
            }

            var checkableRows = new List<Button>();
            var derivedRows = new List<Button>();
            for (var index = 0; index < statRows.Count; index++)
            {
                var row = statRows[index];
                row.transform.SetParent(content, false);
                if (HasExplicitDifficulty(row))
                {
                    ConfigureCheckableRow(row);
                    checkableRows.Add(row);
                }
                else
                {
                    ConfigureDerivedRow(row);
                    row.GetComponent<PawnRollSourceWidget>()?.Unbind();
                    derivedRows.Add(row);
                }
            }

            // 판정 가능한 스탯은 기존 4열 표를 그대로 유지한다.
            for (var index = 0; index < checkableRows.Count; index++)
            {
                checkableRows[index].transform.SetSiblingIndex(index);
            }

            var pairCount = (derivedRows.Count + 1) / 2;
            for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                var pair = GetOrCreateDerivedPair(
                    content,
                    existingPairs,
                    pairIndex);
                pair.gameObject.SetActive(true);
                pair.SetAsLastSibling();

                var first = pairIndex * 2;
                derivedRows[first].transform.SetParent(pair, false);
                if (first + 1 < derivedRows.Count)
                    derivedRows[first + 1].transform.SetParent(pair, false);
            }

            for (var index = pairCount;
                 index < existingPairs.Count;
                 index++)
            {
                existingPairs[index].gameObject.SetActive(false);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private static RectTransform FindStatContent(
            PawnStatPanelWidget panel)
        {
            var grids = panel.GetComponentsInChildren<GridLayoutGroup>(true);
            for (var index = 0; index < grids.Length; index++)
            {
                var grid = grids[index];
                if (grid != null &&
                    string.Equals(
                        grid.gameObject.name,
                        "Content",
                        StringComparison.Ordinal) &&
                    !HasAncestorComponentNamed(
                        grid.transform,
                        "PawnSkillPanelWidget"))
                {
                    return grid.transform as RectTransform;
                }
            }

            return null;
        }

        private static bool HasExplicitDifficulty(Button row)
        {
            var texts = row.GetComponentsInChildren<Text>(true);
            var inputs = row.GetComponentsInChildren<InputField>(true);
            return ReadNamedNumber(texts, inputs, "Regular") >= 1 &&
                   ReadNamedNumber(texts, inputs, "Hard") >= 1 &&
                   ReadNamedNumber(texts, inputs, "Extreme") >= 1;
        }

        private static RectTransform GetOrCreateDerivedPair(
            RectTransform content,
            IList<RectTransform> existingPairs,
            int index)
        {
            if (index < existingPairs.Count)
                return existingPairs[index];

            var pairObject = new GameObject(
                $"DerivedStatPair{index + 1}",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            var pair = pairObject.GetComponent<RectTransform>();
            pair.SetParent(content, false);
            var layout = pairObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            existingPairs.Add(pair);
            return pair;
        }

        private static void ConfigureCheckableRow(Button row)
        {
            SetColumn(row.transform, "Name", 0f, 0.34f, true);
            SetColumn(row.transform, "Regular", 0.34f, 0.56f, true);
            SetColumn(row.transform, "Hard", 0.56f, 0.78f, true);
            SetColumn(row.transform, "Extreme", 0.78f, 1f, true);
            SetColumn(row.transform, "RegularInput", 0.34f, 0.56f, true);
            SetRowLayoutWidth(row, 1f);
        }

        private static void ConfigureDerivedRow(Button row)
        {
            SetColumn(row.transform, "Name", 0f, 0.64f, true);
            SetColumn(row.transform, "Regular", 0.64f, 1f, true);
            SetColumn(row.transform, "Hard", 0f, 0f, false);
            SetColumn(row.transform, "Extreme", 0f, 0f, false);
            SetColumn(row.transform, "RegularInput", 0.64f, 1f, true);
            SetRowLayoutWidth(row, 1f);

            var image = row.targetGraphic as Image;
            if (image != null)
            {
                image.color = new Color(
                    0.075f,
                    0.115f,
                    0.125f,
                    0.98f);
            }
        }

        private static void SetRowLayoutWidth(Button row, float flexible)
        {
            var element = row.GetComponent<LayoutElement>();
            if (element == null)
                element = row.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.flexibleWidth = flexible;
            element.preferredHeight = 40f;
        }

        private static void SetColumn(
            Transform row,
            string objectName,
            float minimumX,
            float maximumX,
            bool visible)
        {
            var target = row.Find(objectName) as RectTransform;
            if (target == null)
                return;

            var preserveActivation = string.Equals(
                objectName,
                "RegularInput",
                StringComparison.Ordinal);
            if (!preserveActivation)
                target.gameObject.SetActive(visible);
            if (!visible)
                return;

            target.anchorMin = new Vector2(minimumX, 0f);
            target.anchorMax = new Vector2(maximumX, 1f);
            target.offsetMin = new Vector2(4f, 3f);
            target.offsetMax = new Vector2(-4f, -3f);
        }

        private static bool TryReadRow(
            Button button,
            out PawnCheckSourceData source)
        {
            source = default;

            var isSkill = HasAncestorComponentNamed(
                button.transform,
                "PawnSkillPanelWidget");
            var isStatEntry = string.Equals(
                button.gameObject.name,
                "StatEntry",
                StringComparison.OrdinalIgnoreCase);
            if (!isSkill && !isStatEntry)
                return false;

            var texts = button.GetComponentsInChildren<Text>(true);
            var inputs = button.GetComponentsInChildren<InputField>(true);
            if (texts.Length == 0 && inputs.Length == 0)
                return false;

            var displayName = ReadDisplayName(texts);
            if (string.IsNullOrWhiteSpace(displayName) ||
                IsIgnoredLabel(displayName))
            {
                return false;
            }

            var regular = ReadNamedNumber(texts, inputs, "Regular");
            var hard = ReadNamedNumber(texts, inputs, "Hard");
            var extreme = ReadNamedNumber(texts, inputs, "Extreme");

            if (regular < 0 && isSkill)
            {
                var numbers = ReadAllNumbers(texts, inputs);
                if (numbers.Count > 0)
                    regular = numbers[0];
                if (numbers.Count > 1)
                    hard = numbers[1];
                if (numbers.Count > 2)
                    extreme = numbers[2];
            }

            if (regular < PawnCheckRollRules.MinimumTarget ||
                regular > PawnCheckRollRules.MaximumTarget)
            {
                return false;
            }

            // 스탯 행은 UI에 일반/어려움/극단 값이 모두 명시된
            // 실제 D100 판정 항목만 허용한다. MOV, Build, DB처럼
            // 어려움/극단 칸이 '—'인 파생 특성은 판정 원본이 아니다.
            if (isStatEntry &&
                (hard < PawnCheckRollRules.MinimumTarget ||
                 extreme < PawnCheckRollRules.MinimumTarget))
            {
                return false;
            }

            // 스킬은 기존 데이터 구조가 단일 최종값만 제공할 수 있어
            // 그 경우에만 절반/5분의 1 값을 보완한다.
            if (hard < PawnCheckRollRules.MinimumTarget)
                hard = Math.Max(1, regular / 2);
            if (extreme < PawnCheckRollRules.MinimumTarget)
                extreme = Math.Max(1, regular / 5);

            var kind = isSkill
                ? PawnRollSourceKind.Skill
                : PawnRollSourceKind.Stat;
            source = new PawnCheckSourceData(
                ResolveSourceId(displayName, kind),
                displayName,
                kind,
                regular,
                Mathf.Clamp(hard, 1, 100),
                Mathf.Clamp(extreme, 1, 100));
            return source.IsValid;
        }

        private static string ReadDisplayName(Text[] texts)
        {
            for (var index = 0; index < texts.Length; index++)
            {
                var text = texts[index];
                if (text == null ||
                    text.gameObject.name.IndexOf(
                        "Name",
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var value = CleanLabel(text.text);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            for (var index = 0; index < texts.Length; index++)
            {
                var value = CleanLabel(texts[index]?.text);
                if (string.IsNullOrWhiteSpace(value) ||
                    TryParsePercent(value, out _) ||
                    IsIgnoredLabel(value))
                {
                    continue;
                }

                return value;
            }

            return string.Empty;
        }

        private static int ReadNamedNumber(
            Text[] texts,
            InputField[] inputs,
            string token)
        {
            for (var index = 0; index < inputs.Length; index++)
            {
                var input = inputs[index];
                if (input != null &&
                    input.gameObject.name.IndexOf(
                        token,
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    TryParsePercent(input.text, out var value))
                {
                    return value;
                }
            }

            for (var index = 0; index < texts.Length; index++)
            {
                var text = texts[index];
                if (text != null &&
                    text.gameObject.name.IndexOf(
                        token,
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    TryParsePercent(text.text, out var value))
                {
                    return value;
                }
            }

            return -1;
        }

        private static List<int> ReadAllNumbers(
            Text[] texts,
            InputField[] inputs)
        {
            var values = new List<int>(3);
            for (var index = 0; index < inputs.Length; index++)
            {
                if (TryParsePercent(inputs[index]?.text, out var value))
                    AddUnique(values, value);
            }

            for (var index = 0; index < texts.Length; index++)
            {
                if (TryParsePercent(texts[index]?.text, out var value))
                    AddUnique(values, value);
            }

            return values;
        }

        private static void AddUnique(List<int> values, int value)
        {
            if (value >= 1 && value <= 100 && !values.Contains(value))
                values.Add(value);
        }

        private static bool TryParsePercent(
            string text,
            out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var builder = new StringBuilder();
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (char.IsDigit(character) ||
                    character == '-' && builder.Length == 0)
                {
                    builder.Append(character);
                }
                else if (builder.Length > 0)
                {
                    break;
                }
            }

            return int.TryParse(
                       builder.ToString(),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   value >= 1 && value <= 100;
        }

        private static string CleanLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("\n", " ").Trim();
        }

        private static bool IsIgnoredLabel(string value)
        {
            for (var index = 0; index < IgnoredLabels.Length; index++)
            {
                if (string.Equals(
                        value,
                        IgnoredLabels[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAncestorComponentNamed(
            Transform transform,
            string typeName)
        {
            var current = transform;
            while (current != null)
            {
                var behaviours = current.GetComponents<MonoBehaviour>();
                for (var index = 0;
                     index < behaviours.Length;
                     index++)
                {
                    var behaviour = behaviours[index];
                    if (behaviour != null &&
                        string.Equals(
                            behaviour.GetType().Name,
                            typeName,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                current = current.parent;
            }

            return false;
        }

        private static string ResolveSourceId(
            string displayName,
            PawnRollSourceKind kind)
        {
            var upper = displayName.ToUpperInvariant();
            if (upper.Contains("STR") || displayName.Contains("근력"))
                return "coc.str";
            if (upper.Contains("CON") ||
                displayName.Contains("건강") ||
                displayName.Contains("체질"))
            {
                return "coc.con";
            }
            if (upper.Contains("SIZ") ||
                displayName.Contains("크기") ||
                displayName.Contains("체격"))
            {
                return "coc.siz";
            }
            if (upper.Contains("DEX") || displayName.Contains("민첩"))
                return "coc.dex";
            if (upper.Contains("APP") || displayName.Contains("외모"))
                return "coc.app";
            if (upper.Contains("INT") || displayName.Contains("지능"))
                return "coc.int";
            if (upper.Contains("POW") || displayName.Contains("정신력"))
                return "coc.pow";
            if (upper.Contains("EDU") || displayName.Contains("교육"))
                return "coc.edu";
            if (upper.Contains("LUK") || displayName.Contains("운"))
                return "coc.luck";

            var prefix = kind == PawnRollSourceKind.Skill
                ? "skill.ui."
                : "stat.ui.";
            return prefix + ComputeStableHash(displayName);
        }

        private static string ComputeStableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (var index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= 16777619;
                }

                return hash.ToString("X8", CultureInfo.InvariantCulture);
            }
        }
    }
}
