using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public readonly struct PawnCheckRollRequest
    {
        public PawnCheckRollRequest(
            int target,
            int bonusPenaltyLevel = 0,
            RollVisibility visibility = RollVisibility.Public)
        {
            Target = target;
            BonusPenaltyLevel = Mathf.Clamp(
                bonusPenaltyLevel,
                -2,
                2);
            Visibility = visibility;
        }

        public int Target { get; }
        public int BonusPenaltyLevel { get; }
        public RollVisibility Visibility { get; }
    }

    public readonly struct PawnEffectRollRequest
    {
        public PawnEffectRollRequest(
            int diceCount,
            int diceSides,
            int modifier,
            RollVisibility visibility = RollVisibility.Public)
        {
            DiceCount = diceCount;
            DiceSides = diceSides;
            Modifier = modifier;
            Visibility = visibility;
        }

        public int DiceCount { get; }
        public int DiceSides { get; }
        public int Modifier { get; }
        public RollVisibility Visibility { get; }
    }

    public enum PawnResourceRollMode
    {
        Damage,
        Healing,
        Sanity
    }

    public readonly struct PawnResourceRollRequest
    {
        public PawnResourceRollRequest(
            PawnResourceValueData resource,
            PawnResourceRollMode mode,
            string expression,
            string successExpression,
            string failureExpression,
            int target,
            int bonusPenaltyLevel = 0,
            RollVisibility visibility = RollVisibility.Public)
        {
            Resource = resource;
            Mode = mode;
            Expression = expression ?? string.Empty;
            SuccessExpression = successExpression ?? string.Empty;
            FailureExpression = failureExpression ?? string.Empty;
            Target = target;
            BonusPenaltyLevel = Mathf.Clamp(
                bonusPenaltyLevel,
                -2,
                2);
            Visibility = visibility;
        }

        public PawnResourceValueData Resource { get; }
        public PawnResourceRollMode Mode { get; }
        public string Expression { get; }
        public string SuccessExpression { get; }
        public string FailureExpression { get; }
        public int Target { get; }
        public int BonusPenaltyLevel { get; }
        public RollVisibility Visibility { get; }
    }

    public sealed class PawnRollInputWidget : MonoBehaviour
    {
        private const float OpenDuration = 0.24f;

        [SerializeField] private RectTransform _panel;
        [SerializeField] private RectTransform _blockerRect;
        [SerializeField] private Button _blockerButton;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _promptText;
        [SerializeField] private GameObject _checkInputRoot;
        [SerializeField] private InputField _targetInput;
        [SerializeField] private GameObject _effectInputRoot;
        [SerializeField] private InputField _diceCountInput;
        [SerializeField] private InputField _diceSidesInput;
        [SerializeField] private GameObject _resourceInputRoot;
        [SerializeField] private GameObject _resourceSimpleRoot;
        [SerializeField] private GameObject _resourceSanityRoot;
        [SerializeField] private Button _resourceModeButton;
        [SerializeField] private Text _resourceModeText;
        [SerializeField] private InputField _resourceExpressionInput;
        [SerializeField] private InputField _sanityTargetInput;
        [SerializeField] private InputField _sanitySuccessInput;
        [SerializeField] private InputField _sanityFailureInput;
        [SerializeField] private GameObject _d100ModifierRoot;
        [SerializeField] private Text _d100ModifierText;
        [SerializeField] private Button _visibilityButton;
        [SerializeField] private Text _visibilityText;
        [SerializeField] private Text _validationText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        private Coroutine _openRoutine;
        private InputMode _mode;
        private int _modifier;
        private PawnResourceValueData _resourceData;
        private PawnResourceRollMode _resourceMode;
        private int _bonusPenaltyLevel;
        private RollVisibility _visibility = RollVisibility.Public;
        private bool _listenersBound;

        public event Action<PawnCheckRollRequest> CheckConfirmed;
        public event Action<PawnEffectRollRequest> EffectConfirmed;
        public event Action<PawnResourceRollRequest> ResourceConfirmed;
        public event Action Cancelled;

        public RectTransform RootRect => _panel;

        public static PawnRollInputWidget CreateRuntime(
            RectTransform parent,
            Font font)
        {
            var panel = CreateRect(
                "RollInputPanel",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(348f, 126f));
            var widget =
                panel.gameObject.AddComponent<PawnRollInputWidget>();
            widget._panel = panel;
            widget.BuildUi(font);
            widget.BindListeners();
            widget.HideImmediate();
            return widget;
        }


        public void SetOverlayParent(RectTransform parent)
        {
            if (_panel == null || parent == null)
                return;

            EnsureBlocker(parent);
            if (_panel.parent != parent)
                _panel.SetParent(parent, false);

            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
        }

        private void EnsureBlocker(RectTransform parent)
        {
            if (_blockerRect == null)
            {
                var blockerObject = new GameObject(
                    "RollInputBlocker",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                _blockerRect =
                    blockerObject.GetComponent<RectTransform>();
                var image = blockerObject.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.36f);
                _blockerButton = blockerObject.GetComponent<Button>();
                _blockerButton.targetGraphic = image;
                _blockerButton.transition =
                    Selectable.Transition.None;
                _blockerButton.onClick.AddListener(
                    HandleCancelClicked);
            }

            if (_blockerRect.parent != parent)
                _blockerRect.SetParent(parent, false);
            _blockerRect.anchorMin = Vector2.zero;
            _blockerRect.anchorMax = Vector2.one;
            _blockerRect.offsetMin = Vector2.zero;
            _blockerRect.offsetMax = Vector2.zero;
            _blockerRect.SetAsLastSibling();
            _panel.SetAsLastSibling();
            _blockerRect.gameObject.SetActive(false);
        }

        public void OpenCheck(int defaultTarget)
        {
            _mode = InputMode.Check;
            _modifier = 0;
            SetText(_promptText, "목표로 하는 넘버 입력");
            SetText(_validationText, string.Empty);
            ResetRollOptions(true);
            SetCompactLayout();
            _checkInputRoot.SetActive(true);
            _effectInputRoot.SetActive(false);
            _resourceInputRoot.SetActive(false);
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
            ResetRollOptions(false);
            SetCompactLayout();
            _checkInputRoot.SetActive(false);
            _effectInputRoot.SetActive(true);
            _resourceInputRoot.SetActive(false);
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

        public void OpenResource(
            PawnResourceValueData resource,
            bool useCallOfCthulhuRules)
        {
            _mode = InputMode.Resource;
            _modifier = 0;
            _resourceData = resource;
            _resourceMode = string.Equals(
                                resource.Label,
                                "이성",
                                StringComparison.Ordinal) &&
                            useCallOfCthulhuRules
                ? PawnResourceRollMode.Sanity
                : PawnResourceRollMode.Damage;
            SetText(_validationText, string.Empty);
            ResetRollOptions(
                _resourceMode == PawnResourceRollMode.Sanity);
            _checkInputRoot.SetActive(false);
            _effectInputRoot.SetActive(false);
            _resourceInputRoot.SetActive(true);
            _resourceSimpleRoot.SetActive(
                _resourceMode != PawnResourceRollMode.Sanity);
            _resourceSanityRoot.SetActive(
                _resourceMode == PawnResourceRollMode.Sanity);
            SetExpandedLayout();

            if (_resourceMode == PawnResourceRollMode.Sanity)
            {
                SetText(_promptText, "이성 판정과 성공/실패 손실식");
                _sanityTargetInput.text = Mathf.Clamp(
                    Mathf.RoundToInt((float)resource.Current),
                    1,
                    100).ToString();
                _sanitySuccessInput.text = "0";
                _sanityFailureInput.text = "1d6";
                RefreshResourceModeLabel();
                ShowAnimated(_sanityTargetInput);
            }
            else
            {
                SetText(
                    _promptText,
                    $"{resource.Label}에 적용할 감소/회복 굴림");
                _resourceExpressionInput.text = "1d6";
                RefreshResourceModeLabel();
                ShowAnimated(_resourceExpressionInput);
            }
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
            if (_resourceModeButton != null)
                _resourceModeButton.interactable = enabled;
            if (_resourceExpressionInput != null)
                _resourceExpressionInput.interactable = enabled;
            if (_sanityTargetInput != null)
                _sanityTargetInput.interactable = enabled;
            if (_sanitySuccessInput != null)
                _sanitySuccessInput.interactable = enabled;
            if (_sanityFailureInput != null)
                _sanityFailureInput.interactable = enabled;
            if (_visibilityButton != null)
                _visibilityButton.interactable = enabled;
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

            if (_blockerRect != null)
                _blockerRect.gameObject.SetActive(false);

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

            var resourceRect = CreateRect(
                "ResourceInput",
                _panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f),
                new Vector2(320f, 116f));
            _resourceInputRoot = resourceRect.gameObject;

            var simpleRect = CreateRect(
                "SimpleResource",
                resourceRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(310f, 72f));
            _resourceSimpleRoot = simpleRect.gameObject;
            _resourceModeButton = CreateButton(
                simpleRect,
                "ResourceModeButton",
                new Vector2(-82f, 0f),
                new Vector2(126f, 34f),
                new Color(0.12f, 0.26f, 0.30f, 0.98f),
                font,
                "피해");
            _resourceModeText = _resourceModeButton
                .GetComponentInChildren<Text>();
            _resourceExpressionInput = CreateInputField(
                simpleRect,
                "ResourceExpression",
                new Vector2(64f, 0f),
                new Vector2(154f, 34f),
                font,
                "예: 1d6+2",
                true);

            var sanityRect = CreateRect(
                "SanityResource",
                resourceRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(310f, 112f));
            _resourceSanityRoot = sanityRect.gameObject;
            CreateText(
                sanityRect, "SanTargetLabel",
                new Vector2(-112f, 34f), new Vector2(78f, 24f),
                font, 12, FontStyle.Bold, TextAnchor.MiddleRight,
                new Color(0.72f, 0.82f, 0.88f)).text = "현재 SAN";
            _sanityTargetInput = CreateInputField(
                sanityRect, "SanityTarget",
                new Vector2(-20f, 34f), new Vector2(96f, 30f),
                font, "1~100");
            CreateText(
                sanityRect, "SanSuccessLabel",
                new Vector2(-112f, 0f), new Vector2(78f, 24f),
                font, 12, FontStyle.Bold, TextAnchor.MiddleRight,
                new Color(0.72f, 0.82f, 0.88f)).text = "성공 손실";
            _sanitySuccessInput = CreateInputField(
                sanityRect, "SanitySuccess",
                new Vector2(20f, 0f), new Vector2(174f, 30f),
                font, "0 또는 1d4", true);
            CreateText(
                sanityRect, "SanFailureLabel",
                new Vector2(-112f, -34f), new Vector2(78f, 24f),
                font, 12, FontStyle.Bold, TextAnchor.MiddleRight,
                new Color(0.72f, 0.82f, 0.88f)).text = "실패 손실";
            _sanityFailureInput = CreateInputField(
                sanityRect, "SanityFailure",
                new Vector2(20f, -34f), new Vector2(174f, 30f),
                font, "예: 1d6", true);

            var modifierRect = CreateRect(
                "D100Modifier",
                _panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-52f, -7f),
                new Vector2(210f, 30f));
            _d100ModifierRoot = modifierRect.gameObject;
            var penaltyButton = CreateButton(
                modifierRect,
                "PenaltyButton",
                new Vector2(-75f, 0f),
                new Vector2(58f, 28f),
                new Color(0.34f, 0.10f, 0.08f, 1f),
                font,
                "페널티");
            var modifierButton = CreateButton(
                modifierRect,
                "ModifierButton",
                Vector2.zero,
                new Vector2(82f, 28f),
                new Color(0.08f, 0.20f, 0.24f, 1f),
                font,
                "보통");
            _d100ModifierText = modifierButton.GetComponentInChildren<Text>();
            var bonusButton = CreateButton(
                modifierRect,
                "BonusButton",
                new Vector2(75f, 0f),
                new Vector2(58f, 28f),
                new Color(0.08f, 0.28f, 0.18f, 1f),
                font,
                "보너스");
            penaltyButton.onClick.AddListener(
                () => ChangeBonusPenalty(-1));
            modifierButton.onClick.AddListener(
                () => SetBonusPenalty(0));
            bonusButton.onClick.AddListener(
                () => ChangeBonusPenalty(1));

            _visibilityButton = CreateButton(
                _panel,
                "VisibilityButton",
                new Vector2(112f, -7f),
                new Vector2(104f, 28f),
                new Color(0.12f, 0.20f, 0.28f, 1f),
                font,
                "전체 공개");
            _visibilityText = _visibilityButton.GetComponentInChildren<Text>();
            _visibilityButton.onClick.AddListener(ToggleVisibility);

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
            if (_blockerRect != null)
            {
                _blockerRect.gameObject.SetActive(true);
                _blockerRect.SetAsLastSibling();
            }
            _panel.SetAsLastSibling();
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
                case InputMode.Resource:
                    ConfirmResource();
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
                new PawnCheckRollRequest(
                    target,
                    _bonusPenaltyLevel,
                    _visibility));
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
                    _modifier,
                    _visibility));
        }

        private void ConfirmResource()
        {
            if (_resourceMode == PawnResourceRollMode.Sanity)
            {
                if (!TryParseInRange(
                        _sanityTargetInput.text,
                        1,
                        100,
                        out var target))
                {
                    SetText(_validationText, "현재 SAN은 1~100으로 입력해줘.");
                    return;
                }
                if (!PawnRollService.TryParseExpression(
                        _sanitySuccessInput.text,
                        out _, out _, out _))
                {
                    SetText(_validationText, "성공 손실식을 확인해줘. 예: 0, 1d4");
                    return;
                }
                if (!PawnRollService.TryParseExpression(
                        _sanityFailureInput.text,
                        out _, out _, out _))
                {
                    SetText(_validationText, "실패 손실식을 확인해줘. 예: 1d6");
                    return;
                }

                SetText(_validationText, string.Empty);
                ResourceConfirmed?.Invoke(new PawnResourceRollRequest(
                    _resourceData,
                    PawnResourceRollMode.Sanity,
                    string.Empty,
                    _sanitySuccessInput.text,
                    _sanityFailureInput.text,
                    target,
                    _bonusPenaltyLevel,
                    _visibility));
                return;
            }

            if (!PawnRollService.TryParseExpression(
                    _resourceExpressionInput.text,
                    out _, out _, out _))
            {
                SetText(_validationText, "주사위 식을 확인해줘. 예: 1d6+2");
                return;
            }

            SetText(_validationText, string.Empty);
            ResourceConfirmed?.Invoke(new PawnResourceRollRequest(
                _resourceData,
                _resourceMode,
                _resourceExpressionInput.text,
                string.Empty,
                string.Empty,
                0,
                0,
                _visibility));
        }

        private void HandleResourceModeClicked()
        {
            if (_resourceMode == PawnResourceRollMode.Sanity)
                return;
            _resourceMode = _resourceMode == PawnResourceRollMode.Damage
                ? PawnResourceRollMode.Healing
                : PawnResourceRollMode.Damage;
            RefreshResourceModeLabel();
        }

        private void RefreshResourceModeLabel()
        {
            if (_resourceModeText == null)
                return;
            _resourceModeText.text = _resourceMode == PawnResourceRollMode.Healing
                ? "회복 굴림"
                : _resourceMode == PawnResourceRollMode.Sanity
                    ? "이성 굴림"
                    : "피해 굴림";
        }

        private void ResetRollOptions(bool usesD100)
        {
            _bonusPenaltyLevel = 0;
            _visibility = RollVisibility.Public;
            if (_d100ModifierRoot != null)
                _d100ModifierRoot.SetActive(usesD100);
            RefreshRollOptions();
        }

        private void ChangeBonusPenalty(int delta)
        {
            SetBonusPenalty(_bonusPenaltyLevel + delta);
        }

        private void SetBonusPenalty(int value)
        {
            _bonusPenaltyLevel = Mathf.Clamp(value, -2, 2);
            RefreshRollOptions();
        }

        private void ToggleVisibility()
        {
            _visibility = _visibility == RollVisibility.Public
                ? RollVisibility.RollerAndGameMaster
                : RollVisibility.Public;
            RefreshRollOptions();
        }

        private void RefreshRollOptions()
        {
            if (_d100ModifierText != null)
            {
                _d100ModifierText.text = _bonusPenaltyLevel > 0
                    ? $"보너스 {_bonusPenaltyLevel}"
                    : _bonusPenaltyLevel < 0
                        ? $"페널티 {-_bonusPenaltyLevel}"
                        : "보통";
            }

            if (_visibilityText != null)
            {
                _visibilityText.text =
                    _visibility == RollVisibility.RollerAndGameMaster
                        ? "비밀 굴림"
                        : "전체 공개";
            }
        }

        private void SetCompactLayout()
        {
            if (_panel != null)
                _panel.sizeDelta = new Vector2(348f, 184f);
            if (_promptText != null)
                _promptText.rectTransform.anchoredPosition =
                    new Vector2(0f, 70f);
            SetAnchoredPosition(_checkInputRoot, new Vector2(0f, 34f));
            SetAnchoredPosition(_effectInputRoot, new Vector2(0f, 34f));
            SetAnchoredPosition(_d100ModifierRoot, new Vector2(-52f, -6f));
            SetAnchoredPosition(_visibilityButton, new Vector2(112f, -6f));
            if (_d100ModifierRoot != null && !_d100ModifierRoot.activeSelf)
                SetAnchoredPosition(_visibilityButton, new Vector2(0f, -6f));
            if (_validationText != null)
                _validationText.rectTransform.anchoredPosition =
                    new Vector2(0f, -40f);
            SetAnchoredPosition(_cancelButton, new Vector2(-58f, -70f));
            SetAnchoredPosition(_confirmButton, new Vector2(58f, -70f));
        }

        private void SetExpandedLayout()
        {
            if (_panel != null)
                _panel.sizeDelta = new Vector2(348f, 280f);
            if (_promptText != null)
                _promptText.rectTransform.anchoredPosition =
                    new Vector2(0f, 116f);
            SetAnchoredPosition(_resourceInputRoot, new Vector2(0f, 28f));
            SetAnchoredPosition(_d100ModifierRoot, new Vector2(-52f, -64f));
            SetAnchoredPosition(_visibilityButton, new Vector2(112f, -64f));
            if (_d100ModifierRoot != null && !_d100ModifierRoot.activeSelf)
                SetAnchoredPosition(_visibilityButton, new Vector2(0f, -64f));
            if (_validationText != null)
                _validationText.rectTransform.anchoredPosition =
                    new Vector2(0f, -96f);
            SetAnchoredPosition(_cancelButton, new Vector2(-58f, -124f));
            SetAnchoredPosition(_confirmButton, new Vector2(58f, -124f));
        }

        private static void SetAnchoredPosition(
            Component component,
            Vector2 position)
        {
            if (component == null)
                return;
            var rect = component.transform as RectTransform;
            if (rect != null)
                rect.anchoredPosition = position;
        }

        private static void SetAnchoredPosition(
            GameObject target,
            Vector2 position)
        {
            if (target == null)
                return;
            var rect = target.transform as RectTransform;
            if (rect != null)
                rect.anchoredPosition = position;
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
            if (_blockerButton != null)
            {
                _blockerButton.onClick.RemoveListener(
                    HandleCancelClicked);
            }
            if (_blockerRect != null)
                Destroy(_blockerRect.gameObject);
            CheckConfirmed = null;
            EffectConfirmed = null;
            ResourceConfirmed = null;
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
            _resourceModeButton?.onClick.AddListener(
                HandleResourceModeClicked);
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
            if (_resourceModeButton != null)
            {
                _resourceModeButton.onClick.RemoveListener(
                    HandleResourceModeClicked);
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
            string placeholder,
            bool allowExpression = false)
        {
            var background = CreateImage(
                parent,
                objectName,
                anchoredPosition,
                size,
                new Color(0.06f, 0.08f, 0.10f, 1f));
            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.contentType = allowExpression
                ? InputField.ContentType.Standard
                : InputField.ContentType.IntegerNumber;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = allowExpression ? 24 : 7;

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
            Effect,
            Resource
        }
    }
}
