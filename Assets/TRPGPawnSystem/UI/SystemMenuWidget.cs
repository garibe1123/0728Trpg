using System;
using System.Collections.Generic;
using Trpg.Save;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public sealed class SystemMenuWidget : MonoBehaviour
    {
        private readonly List<GameObject> _slotRows =
            new List<GameObject>();

        private CanvasGroup _canvasGroup;
        private GameObject _mainPanel;
        private GameObject _savePanel;
        private GameObject _settingsPanel;
        private GameObject _resetConfirmation;
        private RectTransform _slotContent;
        private InputField _saveNameInput;
        private Text _statusText;
        private Font _font;
        private string _pendingDeleteId;
        private bool _isVisible;

        public event Action<string> SaveRequested;
        public event Action<string> LoadRequested;
        public event Action<string> DeleteRequested;
        public event Action ResetAllRequested;
        public event Action SettingsRequested;
        public event Action ExitRequested;

        public bool IsVisible => _isVisible;

        public static SystemMenuWidget CreateRuntime(Font requestedFont)
        {
            var canvasObject = new GameObject(
                "SystemMenuCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = new GameObject(
                "SystemMenu",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(canvasObject.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color =
                new Color(0f, 0f, 0f, 0.64f);

            var widget = root.AddComponent<SystemMenuWidget>();
            widget._canvasGroup = root.GetComponent<CanvasGroup>();
            widget._font = ResolveFont(requestedFont);
            widget.Build();
            widget.Hide();
            return widget;
        }

        public void Show()
        {
            _pendingDeleteId = null;
            gameObject.SetActive(true);
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            _isVisible = true;
            ShowMainPanel();
        }

        public void Hide()
        {
            _pendingDeleteId = null;
            HideResetConfirmation();
            _isVisible = false;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }

        public void ShowMainPanel()
        {
            HideResetConfirmation();
            SetPanel(_mainPanel);
            SetStatus(string.Empty, false);
        }

        public void ShowSavePanel()
        {
            HideResetConfirmation();
            SetPanel(_savePanel);
            _pendingDeleteId = null;
            SetStatus(string.Empty, false);
            if (_saveNameInput != null)
            {
                _saveNameInput.text = string.Empty;
                _saveNameInput.ActivateInputField();
            }
        }

        public void ShowSettingsPanel()
        {
            HideResetConfirmation();
            SetPanel(_settingsPanel);
            SettingsRequested?.Invoke();
        }

        public bool TryCancelResetConfirmation()
        {
            if (_resetConfirmation == null ||
                !_resetConfirmation.activeSelf)
            {
                return false;
            }

            HideResetConfirmation();
            return true;
        }

        public void BindSlots(IReadOnlyList<SaveSlotInfo> slots)
        {
            ClearSlotRows();
            if (_slotContent == null)
                return;

            if (slots == null || slots.Count == 0)
            {
                var empty = CreateText(
                    "Empty",
                    _slotContent,
                    16,
                    TextAnchor.MiddleCenter,
                    new Color(0.68f, 0.74f, 0.78f));
                empty.text = "저장된 데이터가 없습니다.";
                empty.rectTransform.sizeDelta =
                    new Vector2(0f, 56f);
                _slotRows.Add(empty.gameObject);
                return;
            }

            for (var index = 0; index < slots.Count; index++)
                BuildSlotRow(slots[index]);
        }

        public void SetStatus(string message, bool isError)
        {
            if (_statusText == null)
                return;

            _statusText.text = message ?? string.Empty;
            _statusText.color = isError
                ? new Color(1f, 0.38f, 0.30f)
                : new Color(0.42f, 0.90f, 0.72f);
        }

        private void Build()
        {
            _mainPanel = BuildMainPanel();
            _savePanel = BuildSavePanel();
            _settingsPanel = BuildSettingsPanel();
        }

        private GameObject BuildMainPanel()
        {
            var panel = CreatePanel(
                "MainPanel",
                new Vector2(390f, 500f));
            CreateTitle(panel.transform, "메뉴");
            CreateButton(
                panel.transform,
                "SettingsButton",
                "설정",
                new Vector2(0f, 80f),
                HandleSettingsClicked);
            CreateButton(
                panel.transform,
                "SaveButton",
                "저장",
                Vector2.zero,
                HandleSaveMenuClicked);
            CreateButton(
                panel.transform,
                "ExitButton",
                "종료",
                new Vector2(0f, -80f),
                HandleExitClicked,
                new Color(0.34f, 0.10f, 0.11f, 0.98f));
            var hint = CreateText(
                "Hint",
                panel.transform,
                13,
                TextAnchor.MiddleCenter,
                new Color(0.58f, 0.66f, 0.70f));
            hint.text = "ESC로 닫기";
            hint.rectTransform.anchorMin =
                new Vector2(0.5f, 0f);
            hint.rectTransform.anchorMax =
                new Vector2(0.5f, 0f);
            hint.rectTransform.pivot =
                new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition =
                new Vector2(0f, 22f);
            hint.rectTransform.sizeDelta =
                new Vector2(280f, 24f);
            return panel;
        }

        private GameObject BuildSavePanel()
        {
            var panel = CreatePanel(
                "SavePanel",
                new Vector2(720f, 720f));
            CreateTitle(panel.transform, "저장 데이터");

            _saveNameInput = CreateInput(
                panel.transform,
                "SaveNameInput",
                "저장 이름",
                new Vector2(-88f, 246f),
                new Vector2(430f, 48f));
            _saveNameInput.characterLimit = 40;
            CreateButton(
                panel.transform,
                "CreateSaveButton",
                "새 저장",
                new Vector2(252f, 246f),
                HandleCreateSaveClicked,
                new Color(0.07f, 0.30f, 0.27f, 0.98f),
                new Vector2(130f, 48f));

            var viewport = CreateRect(
                "SlotViewport",
                panel.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.one * 0.5f,
                new Vector2(0f, -12f),
                new Vector2(650f, 430f));
            var viewportImage =
                viewport.gameObject.AddComponent<Image>();
            viewportImage.color =
                new Color(0.015f, 0.035f, 0.045f, 0.92f);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            _slotContent = CreateRect(
                "SlotContent",
                viewport,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0.5f, 1f),
                Vector2.zero,
                Vector2.zero);
            _slotContent.offsetMin = new Vector2(8f, 0f);
            _slotContent.offsetMax = new Vector2(-8f, 0f);
            var layout =
                _slotContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 8, 8);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter =
                _slotContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _slotContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            _statusText = CreateText(
                "Status",
                panel.transform,
                14,
                TextAnchor.MiddleLeft,
                Color.white);
            _statusText.rectTransform.anchoredPosition =
                new Vector2(-76f, -276f);
            _statusText.rectTransform.sizeDelta =
                new Vector2(460f, 32f);

            CreateButton(
                panel.transform,
                "ResetAllButton",
                "모든 기록 리셋",
                new Vector2(-246f, -282f),
                ShowResetConfirmation,
                new Color(0.34f, 0.10f, 0.11f, 0.98f),
                new Vector2(190f, 44f));

            _statusText.rectTransform.anchoredPosition =
                new Vector2(18f, -276f);
            _statusText.rectTransform.sizeDelta =
                new Vector2(270f, 32f);

            CreateButton(
                panel.transform,
                "BackButton",
                "뒤로",
                new Vector2(252f, -282f),
                ShowMainPanel,
                new Color(0.09f, 0.16f, 0.20f, 0.98f),
                new Vector2(130f, 44f));

            _resetConfirmation =
                BuildResetConfirmation(panel.transform);
            _resetConfirmation.SetActive(false);
            return panel;
        }

        private GameObject BuildResetConfirmation(Transform parent)
        {
            var overlay = new GameObject(
                "ResetAllConfirmation",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.SetParent(parent, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color =
                new Color(0f, 0f, 0f, 0.76f);

            var dialog = CreateRect(
                "Dialog",
                overlayRect,
                Vector2.one * 0.5f,
                Vector2.one * 0.5f,
                Vector2.one * 0.5f,
                Vector2.zero,
                new Vector2(440f, 220f));
            var dialogImage = dialog.gameObject.AddComponent<Image>();
            dialogImage.color =
                new Color(0.035f, 0.075f, 0.09f, 1f);

            var message = CreateText(
                "Message",
                dialog,
                22,
                TextAnchor.MiddleCenter,
                Color.white);
            message.text = "정말 삭제 하시겠습니까?";
            message.fontStyle = FontStyle.Bold;
            message.rectTransform.anchoredPosition =
                new Vector2(0f, 42f);
            message.rectTransform.sizeDelta =
                new Vector2(380f, 48f);

            CreateButton(
                dialog,
                "ConfirmButton",
                "삭제",
                new Vector2(-92f, -52f),
                HandleResetConfirmed,
                new Color(0.42f, 0.08f, 0.09f, 1f),
                new Vector2(150f, 48f));
            CreateButton(
                dialog,
                "CancelButton",
                "취소",
                new Vector2(92f, -52f),
                HideResetConfirmation,
                new Color(0.09f, 0.16f, 0.20f, 1f),
                new Vector2(150f, 48f));
            return overlay;
        }

        private GameObject BuildSettingsPanel()
        {
            var panel = CreatePanel(
                "SettingsPanel",
                new Vector2(520f, 420f));
            CreateTitle(panel.transform, "설정");
            var guide = CreateText(
                "Guide",
                panel.transform,
                16,
                TextAnchor.MiddleCenter,
                new Color(0.72f, 0.80f, 0.84f));
            guide.text =
                "설정 항목은 SettingsRequested 이벤트에\n" +
                "프로젝트 설정 UI를 연결해 확장할 수 있습니다.";
            guide.rectTransform.anchoredPosition =
                new Vector2(0f, 20f);
            guide.rectTransform.sizeDelta =
                new Vector2(420f, 100f);
            CreateButton(
                panel.transform,
                "BackButton",
                "뒤로",
                new Vector2(0f, -120f),
                ShowMainPanel);
            return panel;
        }

        private void BuildSlotRow(in SaveSlotInfo slot)
        {
            var saveId = slot.SaveId;
            var row = new GameObject(
                "SaveSlot_" + saveId,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            var rect = row.GetComponent<RectTransform>();
            rect.SetParent(_slotContent, false);
            row.GetComponent<Image>().color =
                new Color(0.045f, 0.105f, 0.13f, 0.98f);
            row.GetComponent<LayoutElement>().preferredHeight = 72f;
            _slotRows.Add(row);

            var name = CreateText(
                "Name",
                rect,
                17,
                TextAnchor.MiddleLeft,
                Color.white);
            name.text = slot.SaveName;
            name.fontStyle = FontStyle.Bold;
            SetRowRect(
                name.rectTransform,
                14f,
                240f,
                -5f,
                34f);

            var time = CreateText(
                "Time",
                rect,
                12,
                TextAnchor.MiddleLeft,
                new Color(0.62f, 0.70f, 0.74f));
            time.text = slot.SavedAtUtc
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm");
            SetRowRect(
                time.rectTransform,
                14f,
                240f,
                -38f,
                26f);

            CreateRowButton(
                rect,
                "Load",
                "불러오기",
                394f,
                () => LoadRequested?.Invoke(saveId),
                new Color(0.07f, 0.26f, 0.32f, 0.98f));
            CreateRowButton(
                rect,
                "Delete",
                "삭제",
                528f,
                () => HandleDeleteClicked(saveId),
                new Color(0.34f, 0.10f, 0.11f, 0.98f));
        }

        private void HandleCreateSaveClicked()
        {
            SaveRequested?.Invoke(
                _saveNameInput != null
                    ? _saveNameInput.text
                    : string.Empty);
        }

        private void HandleDeleteClicked(string saveId)
        {
            if (!string.Equals(
                    _pendingDeleteId,
                    saveId,
                    StringComparison.Ordinal))
            {
                _pendingDeleteId = saveId;
                SetStatus(
                    "같은 저장의 삭제 버튼을 한 번 더 누르면 삭제됩니다.",
                    true);
                return;
            }

            _pendingDeleteId = null;
            DeleteRequested?.Invoke(saveId);
        }

        private void ShowResetConfirmation()
        {
            _pendingDeleteId = null;
            if (_resetConfirmation != null)
                _resetConfirmation.SetActive(true);
        }

        private void HideResetConfirmation()
        {
            if (_resetConfirmation != null)
                _resetConfirmation.SetActive(false);
        }

        private void HandleResetConfirmed()
        {
            HideResetConfirmation();
            ResetAllRequested?.Invoke();
        }

        private void HandleSettingsClicked()
        {
            ShowSettingsPanel();
        }

        private void HandleSaveMenuClicked()
        {
            ShowSavePanel();
        }

        private void HandleExitClicked()
        {
            ExitRequested?.Invoke();
        }

        private void SetPanel(GameObject activePanel)
        {
            if (_mainPanel != null)
                _mainPanel.SetActive(activePanel == _mainPanel);
            if (_savePanel != null)
                _savePanel.SetActive(activePanel == _savePanel);
            if (_settingsPanel != null)
                _settingsPanel.SetActive(activePanel == _settingsPanel);
        }

        private void ClearSlotRows()
        {
            for (var index = 0; index < _slotRows.Count; index++)
            {
                if (_slotRows[index] != null)
                    Destroy(_slotRows[index]);
            }
            _slotRows.Clear();
        }

        private GameObject CreatePanel(string name, Vector2 size)
        {
            var panel = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            panel.GetComponent<Image>().color =
                new Color(0.025f, 0.065f, 0.082f, 0.995f);
            return panel;
        }

        private void CreateTitle(Transform parent, string value)
        {
            var title = CreateText(
                "Title",
                parent,
                26,
                TextAnchor.MiddleCenter,
                Color.white);
            title.text = value;
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin =
                new Vector2(0.5f, 1f);
            title.rectTransform.anchorMax =
                new Vector2(0.5f, 1f);
            title.rectTransform.pivot =
                new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition =
                new Vector2(0f, -24f);
            title.rectTransform.sizeDelta =
                new Vector2(420f, 42f);
        }

        private Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 position,
            Action clicked,
            Color? color = null,
            Vector2? size = null)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size ?? new Vector2(280f, 58f);
            var image = root.GetComponent<Image>();
            image.color =
                color ?? new Color(0.07f, 0.20f, 0.25f, 0.98f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => clicked?.Invoke());
            var text = CreateText(
                "Label",
                rect,
                18,
                TextAnchor.MiddleCenter,
                Color.white);
            text.text = label;
            text.fontStyle = FontStyle.Bold;
            Stretch(text.rectTransform, 0f);
            return button;
        }

        private InputField CreateInput(
            Transform parent,
            string name,
            string placeholder,
            Vector2 position,
            Vector2 size)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(InputField));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            root.GetComponent<Image>().color =
                new Color(0.015f, 0.035f, 0.045f, 0.98f);

            var text = CreateText(
                "Text",
                rect,
                16,
                TextAnchor.MiddleLeft,
                Color.white);
            Stretch(text.rectTransform, 14f);
            var hint = CreateText(
                "Placeholder",
                rect,
                16,
                TextAnchor.MiddleLeft,
                new Color(0.48f, 0.56f, 0.60f));
            hint.text = placeholder;
            Stretch(hint.rectTransform, 14f);

            var input = root.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private void CreateRowButton(
            RectTransform parent,
            string name,
            string label,
            float x,
            Action clicked,
            Color color)
        {
            var button = CreateButton(
                parent,
                name,
                label,
                Vector2.zero,
                clicked,
                color,
                new Vector2(116f, 42f));
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
        }

        private Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var text = root.GetComponent<Text>();
            text.rectTransform.SetParent(parent, false);
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
        }

        private static void SetRowRect(
            RectTransform rect,
            float x,
            float width,
            float y,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Font ResolveFont(Font requestedFont)
        {
            if (requestedFont != null)
                return requestedFont;

            return Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
        }

        private void OnDestroy()
        {
            SaveRequested = null;
            LoadRequested = null;
            DeleteRequested = null;
            ResetAllRequested = null;
            SettingsRequested = null;
            ExitRequested = null;
        }
    }
}
