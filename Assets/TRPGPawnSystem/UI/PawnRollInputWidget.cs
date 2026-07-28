using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public readonly struct PawnCheckRollRequest
    {
        public PawnCheckRollRequest(int target)
        {
            Target = target;
        }

        public int Target { get; }
    }

    public readonly struct PawnEffectRollRequest
    {
        public PawnEffectRollRequest(
            int diceCount,
            int diceSides,
            int modifier)
        {
            DiceCount = diceCount;
            DiceSides = diceSides;
            Modifier = modifier;
        }

        public int DiceCount { get; }
        public int DiceSides { get; }
        public int Modifier { get; }
    }

    public sealed class PawnRollInputWidget : MonoBehaviour
    {
        private const float OpenDuration = 0.24f;

        [SerializeField] private RectTransform _panel;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _promptText;
        [SerializeField] private GameObject _checkInputRoot;
        [SerializeField] private InputField _targetInput;
        [SerializeField] private GameObject _effectInputRoot;
        [SerializeField] private InputField _diceCountInput;
        [SerializeField] private InputField _diceSidesInput;
        [SerializeField] private Text _validationText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        private Coroutine _openRoutine;
        private InputMode _mode;
        private int _modifier;
        private bool _listenersBound;

        public event Action<PawnCheckRollRequest> CheckConfirmed;
        public event Action<PawnEffectRollRequest> EffectConfirmed;
        public event Action Cancelled;

        public static PawnRollInputWidget CreateRuntime(
            RectTransform parent,
            Font font)
        {
            var panel = CreateRect(
                "RollInputPanel",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, -63f),
                new Vector2(348f, 126f));
            var widget =
                panel.gameObject.AddComponent<PawnRollInputWidget>();
            widget._panel = panel;
            widget.BuildUi(font);
            widget.BindListeners();
            widget.HideImmediate();
            return widget;
        }

        public void OpenCheck(int defaultTarget)
        {
            _mode = InputMode.Check;
            _modifier = 0;
            SetText(_promptText, "목표로 하는 넘버 입력");
            SetText(_validationText, string.Empty);
            _checkInputRoot.SetActive(true);
            _effectInputRoot.SetActive(false);
            _targetInput.text = Mathf.Clamp(
                defaultTarget,
                1,
                100).ToString();
            ShowAnimated(_targetInput);
        }

        public void OpenEffect(
            int defaultDiceCount,
            int defaultDiceSides,
            int modifier)
        {
            _mode = InputMode.Effect;
            _modifier = modifier;
            SetText(_promptText, "굴릴 NdN 입력");
            SetText(_validationText, string.Empty);
            _checkInputRoot.SetActive(false);
            _effectInputRoot.SetActive(true);
            _diceCountInput.text = Mathf.Clamp(
                defaultDiceCount,
                1,
                PawnRollService.MaximumDiceCount).ToString();
            _diceSidesInput.text = Mathf.Clamp(
                defaultDiceSides,
                2,
                PawnRollService.MaximumDiceSides).ToString();
            ShowAnimated(_diceCountInput);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            if (_confirmButton != null)
            {
                _confirmButton.interactable = enabled;
            }

            if (_cancelButton != null)
            {
                _cancelButton.interactable = enabled;
            }

            if (_targetInput != null)
            {
                _targetInput.interactable = enabled;
            }

            if (_diceCountInput != null)
            {
                _diceCountInput.interactable = enabled;
            }

            if (_diceSidesInput != null)
            {
                _diceSidesInput.interactable = enabled;
            }
        }

        public void HideImmediate()
        {
            CancelOpenRoutine();
            _mode = InputMode.None;
            if (_panel != null)
            {
                _panel.gameObject.SetActive(false);
                _panel.localScale = Vector3.one;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        private void BuildUi(Font requestedFont)
        {
            var font = ResolveFont(requestedFont);
            var background = _panel.gameObject.AddComponent<Image>();
            background.color =
                new Color(0.025f, 0.035f, 0.045f, 0.985f);
            _canvasGroup = _panel.gameObject.AddComponent<CanvasGroup>();

            _promptText = CreateText(
                _panel,
                "Prompt",
                new Vector2(0f, 42f),
                new Vector2(312f, 24f),
                font,
                15,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);

            var checkRect = CreateRect(
                "CheckInput",
                _panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 7f),
                new Vector2(220f, 34f));
            _checkInputRoot = checkRect.gameObject;
            _targetInput = CreateInputField(
                checkRect,
                "TargetNumber",
                Vector2.zero,
                new Vector2(220f, 34f),
                font,
                "1~100");

            var effectRect = CreateRect(
                "EffectInput",
                _panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 7f),
                new Vector2(300f, 34f));
            _effectInputRoot = effectRect.gameObject;
            CreateText(
                effectRect,
                "CountLabel",
                new Vector2(-132f, 0f),
                new Vector2(34f, 30f),
                font,
                13,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(0.72f, 0.82f, 0.88f)).text = "N";
            _diceCountInput = CreateInputField(
                effectRect,
                "DiceCount",
                new Vector2(-82f, 0f),
                new Vector2(66f, 34f),
                font,
                "개수");
            CreateText(
                effectRect,
                "DiceMark",
                new Vector2(-28f, 0f),
                new Vector2(30f, 30f),
                font,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.78f, 0.22f)).text = "d";
            _diceSidesInput = CreateInputField(
                effectRect,
                "DiceSides",
                new Vector2(48f, 0f),
                new Vector2(102f, 34f),
                font,
                "면수");

            _validationText = CreateText(
                _panel,
                "Validation",
                new Vector2(0f, -17f),
                new Vector2(312f, 18f),
                font,
                11,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.38f, 0.30f));

            _cancelButton = CreateButton(
                _panel,
                "CancelButton",
                new Vector2(-58f, -45f),
                new Vector2(96f, 28f),
                new Color(0.12f, 0.14f, 0.16f, 0.98f),
                font,
                "취소");
            _confirmButton = CreateButton(
                _panel,
                "ConfirmButton",
                new Vector2(58f, -45f),
                new Vector2(96f, 28f),
                new Color(0.10f, 0.38f, 0.48f, 0.98f),
                font,
                "굴리기");
        }

        private void ShowAnimated(InputField focusTarget)
        {
            CancelOpenRoutine();
            _panel.gameObject.SetActive(true);
            _panel.localScale = new Vector3(0.92f, 0.06f, 1f);
            _canvasGroup.alpha = 0f;
            _openRoutine = StartCoroutine(
                AnimateOpen(focusTarget));
        }

        private IEnumerator AnimateOpen(InputField focusTarget)
        {
            var elapsed = 0f;
            while (elapsed < OpenDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(
                    elapsed / OpenDuration);
                var eased =
                    1f - Mathf.Pow(1f - normalized, 3f);
                _panel.localScale = new Vector3(
                    Mathf.Lerp(0.92f, 1f, eased),
                    Mathf.Lerp(0.06f, 1f, eased),
                    1f);
                _canvasGroup.alpha = eased;
                yield return null;
            }

            _panel.localScale = Vector3.one;
            _canvasGroup.alpha = 1f;
            _openRoutine = null;
            if (focusTarget != null)
            {
                focusTarget.Select();
                focusTarget.ActivateInputField();
            }
        }

        private void HandleConfirmClicked()
        {
            switch (_mode)
            {
                case InputMode.Check:
                    ConfirmCheck();
                    break;
                case InputMode.Effect:
                    ConfirmEffect();
                    break;
            }
        }

        private void ConfirmCheck()
        {
            if (!TryParseInRange(
                    _targetInput.text,
                    1,
                    100,
                    out var target))
            {
                SetText(
                    _validationText,
                    "목표 수치는 1~100 사이로 입력해줘.");
                return;
            }

            SetText(_validationText, string.Empty);
            CheckConfirmed?.Invoke(
                new PawnCheckRollRequest(target));
        }

        private void ConfirmEffect()
        {
            if (!TryParseInRange(
                    _diceCountInput.text,
                    1,
                    PawnRollService.MaximumDiceCount,
                    out var diceCount))
            {
                SetText(
                    _validationText,
                    "주사위 개수 N을 1 이상으로 입력해줘.");
                return;
            }

            if (!TryParseInRange(
                    _diceSidesInput.text,
                    2,
                    PawnRollService.MaximumDiceSides,
                    out var diceSides))
            {
                SetText(
                    _validationText,
                    "주사위 면수는 2 이상으로 입력해줘.");
                return;
            }

            SetText(_validationText, string.Empty);
            EffectConfirmed?.Invoke(
                new PawnEffectRollRequest(
                    diceCount,
                    diceSides,
                    _modifier));
        }

        private void HandleCancelClicked()
        {
            HideImmediate();
            Cancelled?.Invoke();
        }

        private void OnEnable()
        {
            BindListeners();
        }

        private void OnDisable()
        {
            UnbindListeners();
            CancelOpenRoutine();
        }

        private void OnDestroy()
        {
            UnbindListeners();
            CheckConfirmed = null;
            EffectConfirmed = null;
            Cancelled = null;
        }

        private void BindListeners()
        {
            if (_listenersBound ||
                _confirmButton == null ||
                _cancelButton == null)
            {
                return;
            }

            _confirmButton.onClick.AddListener(
                HandleConfirmClicked);
            _cancelButton.onClick.AddListener(
                HandleCancelClicked);
            _listenersBound = true;
        }

        private void UnbindListeners()
        {
            if (!_listenersBound)
            {
                return;
            }

            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(
                    HandleConfirmClicked);
            }

            if (_cancelButton != null)
            {
                _cancelButton.onClick.RemoveListener(
                    HandleCancelClicked);
            }

            _listenersBound = false;
        }

        private void CancelOpenRoutine()
        {
            if (_openRoutine == null)
            {
                return;
            }

            StopCoroutine(_openRoutine);
            _openRoutine = null;
        }

        private static bool TryParseInRange(
            string text,
            int minimum,
            int maximum,
            out int value)
        {
            return int.TryParse(text, out value) &&
                   value >= minimum &&
                   value <= maximum;
        }

        private static InputField CreateInputField(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            Font font,
            string placeholder)
        {
            var background = CreateImage(
                parent,
                objectName,
                anchoredPosition,
                size,
                new Color(0.06f, 0.08f, 0.10f, 1f));
            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.contentType = InputField.ContentType.IntegerNumber;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 7;

            var placeholderText = CreateText(
                background.rectTransform,
                "Placeholder",
                Vector2.zero,
                new Vector2(size.x - 18f, size.y - 6f),
                font,
                13,
                FontStyle.Italic,
                TextAnchor.MiddleCenter,
                new Color(0.48f, 0.54f, 0.58f, 0.8f));
            placeholderText.text = placeholder;

            var valueText = CreateText(
                background.rectTransform,
                "Value",
                Vector2.zero,
                new Vector2(size.x - 18f, size.y - 6f),
                font,
                14,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            valueText.raycastTarget = true;
            input.placeholder = placeholderText;
            input.textComponent = valueText;
            input.caretColor = new Color(1f, 0.78f, 0.22f);
            input.selectionColor =
                new Color(0.15f, 0.55f, 0.72f, 0.55f);
            return input;
        }

        private static Button CreateButton(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color,
            Font font,
            string label)
        {
            var image = CreateImage(
                parent,
                objectName,
                anchoredPosition,
                size,
                color);
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = color * 1.18f;
            colors.pressedColor = color * 0.78f;
            colors.disabledColor = new Color(
                color.r,
                color.g,
                color.b,
                0.42f);
            button.colors = colors;

            var labelText = CreateText(
                image.rectTransform,
                "Label",
                Vector2.zero,
                size,
                font,
                12,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            labelText.text = label;
            return button;
        }

        private static Image CreateImage(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            var rect = CreateRect(
                objectName,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                anchoredPosition,
                size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color)
        {
            var rect = CreateRect(
                objectName,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                anchoredPosition,
                size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static RectTransform CreateRect(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            var child = new GameObject(
                objectName,
                typeof(RectTransform));
            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Font ResolveFont(Font requestedFont)
        {
            if (requestedFont != null)
            {
                return requestedFont;
            }

            try
            {
                var builtInFont = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
                if (builtInFont != null)
                {
                    return builtInFont;
                }
            }
            catch (ArgumentException)
            {
            }

            return Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
        }

        private enum InputMode
        {
            None,
            Check,
            Effect
        }
    }
}
