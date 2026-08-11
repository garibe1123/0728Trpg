using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public readonly struct PawnRollPresentationData
    {
        public PawnRollPresentationData(
            string title,
            string expression,
            int finalValue,
            int minimumValue,
            int maximumValue,
            string resultLabel,
            string detailLabel,
            Color resultColor,
            float durationSeconds,
            int checkTarget = 0,
            float pointerRotations = 7f,
            float decelerationExponent = 4f,
            float resultHoldSeconds = 0.22f)
        {
            Title = title;
            Expression = expression;
            FinalValue = finalValue;
            MinimumValue = minimumValue;
            MaximumValue = maximumValue;
            ResultLabel = resultLabel;
            DetailLabel = detailLabel;
            ResultColor = resultColor;
            DurationSeconds = durationSeconds;
            CheckTarget = checkTarget;
            PointerRotations = pointerRotations;
            DecelerationExponent = decelerationExponent;
            ResultHoldSeconds = resultHoldSeconds;
        }

        public string Title { get; }
        public string Expression { get; }
        public int FinalValue { get; }
        public int MinimumValue { get; }
        public int MaximumValue { get; }
        public string ResultLabel { get; }
        public string DetailLabel { get; }
        public Color ResultColor { get; }
        public float DurationSeconds { get; }
        public int CheckTarget { get; }
        public float PointerRotations { get; }
        public float DecelerationExponent { get; }
        public float ResultHoldSeconds { get; }
    }

    public sealed class PawnRollWidget : MonoBehaviour
    {
        private const float MinimumPresentationDuration = 0.25f;
        private const float SequentialCounterStepSeconds = 0.035f;
        private const float PanelOpenDuration = 0.24f;
        private const int MaximumVisibleOutcomeLabels = 100;
        private const float MinimumActionButtonSize = 54f;
        private const float MaximumActionButtonSize = 102f;

        [SerializeField] private Button _checkButton;
        [SerializeField] private Text _checkButtonText;
        [SerializeField] private Button _effectButton;
        [SerializeField] private Text _effectButtonText;
        [SerializeField] private PawnRollInputWidget _inputWidget;
        [SerializeField] private GameObject _presentationRoot;
        [SerializeField] private RectTransform _presentationRect;
        [SerializeField] private CanvasGroup _presentationCanvasGroup;
        [SerializeField] private RectTransform _rouletteRect;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _expressionText;
        [SerializeField] private Text _counterText;
        [SerializeField] private Text _resultText;
        [SerializeField] private Text _detailText;
        [SerializeField] private Text _nearMissText;
        [SerializeField] private Text _thresholdLegendText;
        [SerializeField] private RectTransform _pointer;
        [SerializeField] private PawnRouletteGraphic _rouletteGraphic;
        [SerializeField] private PawnRouletteLabelRing _rouletteLabelRing;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _tickClip;
        [SerializeField] private AudioClip _finishClip;

        private Coroutine _presentationRoutine;
        private bool _listenersBound;
        private bool _ownsRuntimeAudioClips;
        private bool _buttonsOpen;
        private bool _actionButtonsVisible = true;
        private RectTransform _canvasRect;
        private RectTransform _hostPanelRect;
        private RectTransform _moveButtonRect;
        private RectTransform _checkButtonRect;
        private RectTransform _effectButtonRect;
        private Vector2 _closedMovePosition;
        private Vector2 _closedCheckPosition;
        private Vector2 _closedEffectPosition;
        private float _reservedRightWidth;
        private int _defaultCheckTarget = PawnRollStats.FallbackCheckTarget;
        private int _defaultDiceCount = 2;
        private int _defaultDiceSides = 6;
        private int _defaultDiceModifier;
        private bool _showsCheckThresholds;
        private D100CheckThresholds _activeCheckThresholds;
        private int _presentationSerial;
        private int _lastPresentedSelection = int.MinValue;
        private bool _sequentialCounterActive;
        private float _sequentialCounterStartedAt;
        private float _sequentialCounterDuration;
        private int _sequentialCounterMinimum;
        private int _sequentialCounterMaximum;
        private int _sequentialCounterFinal;
        private int _sequentialCounterCurrent;

        public event Action RollInputOpened;
        public event Action<PawnCheckRollRequest> CheckRollRequested;
        public event Action<PawnEffectRollRequest> EffectRollRequested;
        public event Action<PawnResourceRollRequest> ResourceRollRequested;
        public event Action PresentationCompleted;
        public float ReservedRightWidth => _reservedRightWidth;

        public static PawnRollWidget CreateRuntime(
            RectTransform parent,
            Font font)
        {
            var root = CreateRect(
                "PawnRollWidget",
                parent,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            var widget = root.gameObject.AddComponent<PawnRollWidget>();
            widget.BuildRuntimeUi(font);
            widget.BindListeners();
            widget.SetButtonsEnabled(false);
            widget.CancelPresentation();
            return widget;
        }

        public void ConfigureOverlayCanvas(
            RectTransform canvasRect)
        {
            _canvasRect = ResolveCanvasRect(
                canvasRect,
                transform as RectTransform);
            if (_canvasRect == null)
                return;

            if (_presentationRect != null &&
                _presentationRect.parent != _canvasRect)
            {
                _presentationRect.SetParent(_canvasRect, false);
            }

            if (_presentationRect != null)
            {
                _presentationRect.anchorMin =
                    new Vector2(0.5f, 0.5f);
                _presentationRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                _presentationRect.pivot =
                    new Vector2(0.5f, 0.5f);
                _presentationRect.anchoredPosition = Vector2.zero;
            }

            _inputWidget?.SetOverlayParent(_canvasRect);
            RefreshPresentationLayout();
        }

        public void ConfigureResponsiveLayout(
            Button moveButton,
            RectTransform canvasRect,
            RectTransform hostPanelRect)
        {
            _hostPanelRect = hostPanelRect;
            _canvasRect = ResolveCanvasRect(
                canvasRect,
                hostPanelRect);
            _moveButtonRect = moveButton != null
                ? moveButton.GetComponent<RectTransform>()
                : null;

            if (_moveButtonRect != null &&
                _moveButtonRect.parent != transform)
            {
                _moveButtonRect.SetParent(transform, false);
            }

            if (_presentationRect != null &&
                _canvasRect != null &&
                _presentationRect.parent != _canvasRect)
            {
                _presentationRect.SetParent(_canvasRect, false);
                _presentationRect.anchorMin =
                    new Vector2(0.5f, 0.5f);
                _presentationRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                _presentationRect.pivot =
                    new Vector2(0.5f, 0.5f);
                _presentationRect.anchoredPosition = Vector2.zero;
            }

            _inputWidget?.SetOverlayParent(_canvasRect);
            RefreshResponsiveLayout();
        }

        public void RefreshResponsiveLayout()
        {
            RefreshActionButtonLayout();
            RefreshPresentationLayout();
        }

        public void SetButtonsEnabled(bool enabled)
        {
            if (_checkButton != null)
            {
                _checkButton.interactable = enabled;
            }

            if (_effectButton != null)
            {
                _effectButton.interactable = enabled;
            }

            _inputWidget?.SetInteractionEnabled(enabled);
        }

        public void SetActionButtonsVisible(bool visible)
        {
            _actionButtonsVisible = visible;
            SetButtonPositions(_buttonsOpen);
        }

        public void SetInputDefaults(
            int checkTarget,
            int diceCount,
            int diceSides,
            int modifier)
        {
            _defaultCheckTarget = Mathf.Clamp(
                checkTarget,
                1,
                100);
            _defaultDiceCount = Mathf.Clamp(
                diceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _defaultDiceSides = Mathf.Clamp(
                diceSides,
                2,
                PawnRollService.MaximumDiceSides);
            _defaultDiceModifier = modifier;
        }

        public void SetButtonLabels(
            string checkLabel,
            string effectLabel)
        {
            if (_checkButtonText != null)
            {
                _checkButtonText.text = checkLabel ?? string.Empty;
            }

            if (_effectButtonText != null)
            {
                _effectButtonText.text = effectLabel ?? string.Empty;
            }
        }

        public void OpenResourceRoll(
            in PawnResourceValueData resource,
            bool useCallOfCthulhuRules)
        {
            CancelRoutine();
            if (_presentationRoot != null)
            {
                _presentationRoot.SetActive(false);
            }

            RollInputOpened?.Invoke();
            MoveButtons(true);
            _inputWidget?.OpenResource(
                resource,
                useCallOfCthulhuRules);
        }

        public void Play(in PawnRollPresentationData data)
        {
            CancelRoutine();
            _inputWidget?.HideImmediate();
            MoveButtons(true);
            if (_presentationRoot != null)
            {
                _presentationRoot.SetActive(true);
            }

            if (_presentationRect != null)
            {
                _presentationRect.localScale =
                    new Vector3(0.92f, 0.06f, 1f);
            }

            if (_presentationCanvasGroup != null)
            {
                _presentationCanvasGroup.alpha = 0f;
            }

            _presentationRoutine =
                StartCoroutine(PlayPresentation(data, true));
        }

        public void PlayModifiedD100(
            D100ModifiedRollResult modifiedRoll,
            in PawnRollPresentationData finalData)
        {
            if (modifiedRoll == null ||
                !modifiedRoll.HasAdditionalTensDice)
            {
                Play(finalData);
                return;
            }

            CancelRoutine();
            _inputWidget?.HideImmediate();
            MoveButtons(true);
            if (_presentationRoot != null)
                _presentationRoot.SetActive(true);

            if (_presentationRect != null)
            {
                _presentationRect.localScale =
                    new Vector3(0.92f, 0.06f, 1f);
            }

            if (_presentationCanvasGroup != null)
                _presentationCanvasGroup.alpha = 0f;

            _presentationRoutine = StartCoroutine(
                PlayModifiedD100Sequence(modifiedRoll, finalData));
        }

        public void CancelPresentation()
        {
            CancelRoutine();
            _inputWidget?.HideImmediate();
            if (_presentationRoot != null)
            {
                _presentationRoot.SetActive(false);
            }

            if (_presentationRect != null)
            {
                _presentationRect.localScale = Vector3.one;
            }

            if (_presentationCanvasGroup != null)
            {
                _presentationCanvasGroup.alpha = 0f;
            }

            if (_pointer != null)
            {
                _pointer.localEulerAngles = Vector3.zero;
            }

            SetButtonPositions(false);
        }

        private IEnumerator PlayPresentation(
            PawnRollPresentationData data,
            bool complete)
        {
            yield return AnimatePresentationOpen();

            var minimum = Mathf.Min(
                data.MinimumValue,
                data.MaximumValue);
            var maximum = Mathf.Max(
                data.MinimumValue,
                data.MaximumValue);
            var finalValue = Mathf.Clamp(
                data.FinalValue,
                minimum,
                maximum);
            var outcomeCount = Mathf.Max(
                1,
                maximum - minimum + 1);
            var counterDistance = Mathf.Abs(finalValue - minimum);
            var duration = Mathf.Max(
                MinimumPresentationDuration,
                data.DurationSeconds,
                counterDistance * SequentialCounterStepSeconds);
            var pointerRotations = Mathf.Max(
                0.25f,
                data.PointerRotations);
            var decelerationExponent = Mathf.Max(
                1f,
                data.DecelerationExponent);
            var resultHoldSeconds = Mathf.Max(
                0f,
                data.ResultHoldSeconds);

            _showsCheckThresholds =
                minimum == 1 &&
                maximum == 100 &&
                data.CheckTarget > 0;
            _activeCheckThresholds = _showsCheckThresholds
                ? PawnRollService.GetD100Thresholds(
                    data.CheckTarget)
                : default;

            _rouletteGraphic?.SetPresentation(
                outcomeCount,
                _showsCheckThresholds,
                _activeCheckThresholds);
            _rouletteLabelRing?.SetRange(
                minimum,
                maximum,
                MaximumVisibleOutcomeLabels,
                _showsCheckThresholds,
                _activeCheckThresholds);
            UpdateThresholdLegend();
            SetText(_titleText, data.Title);
            SetText(_expressionText, data.Expression);
            SetText(_resultText, string.Empty);
            SetText(_detailText, string.Empty);
            SetText(_nearMissText, string.Empty);
            if (_counterText != null)
            {
                _counterText.color = _showsCheckThresholds
                    ? PawnRouletteGradePalette.GetLabelColor(
                        _activeCheckThresholds.GetGrade(minimum))
                    : Color.white;
                _counterText.text = minimum.ToString();
                _counterText.rectTransform.localScale = Vector3.one;
            }

            BeginSequentialCounter(
                minimum,
                maximum,
                finalValue,
                duration);

            var visualRandom = CreateVisualRandom(finalValue);
            var finalIndex = finalValue - minimum;
            var finalRatio =
                (finalIndex + 0.5f) / outcomeCount;
            var slotAngle = 360f / outcomeCount;
            var approachSlots = ResolveApproachSlotCount(
                visualRandom,
                outcomeCount);
            var hesitationSlots = ResolveHesitationSlotDistance(
                visualRandom,
                outcomeCount,
                approachSlots);
            var overshootSlots = outcomeCount > 1
                ? NextFloat(visualRandom, 0.34f, 0.48f)
                : 0f;
            var extraRotations = visualRandom.Next(0, 3);
            var spinRotations = Mathf.Max(
                1f,
                Mathf.Ceil(pointerRotations) + extraRotations);

            var finalAngle =
                -360f * (spinRotations + finalRatio);
            var approachStartAngle =
                finalAngle + slotAngle * approachSlots;
            var hesitationAngle =
                finalAngle + slotAngle * hesitationSlots;
            var overshootAngle =
                finalAngle - slotAngle * overshootSlots;

            var spinWeight =
                NextFloat(visualRandom, 0.46f, 0.52f);
            var approachWeight =
                NextFloat(visualRandom, 0.23f, 0.29f);
            var hesitationWeight =
                NextFloat(visualRandom, 0.035f, 0.055f);
            var revealWeight =
                NextFloat(visualRandom, 0.15f, 0.20f);
            var settleWeight =
                NextFloat(visualRandom, 0.055f, 0.080f);
            var totalWeight =
                spinWeight +
                approachWeight +
                hesitationWeight +
                revealWeight +
                settleWeight;

            var spinDuration = duration * spinWeight / totalWeight;
            var approachDuration =
                duration * approachWeight / totalWeight;
            var hesitationDuration =
                duration * hesitationWeight / totalWeight;
            var revealDuration =
                duration * revealWeight / totalWeight;
            var settleDuration =
                duration * settleWeight / totalWeight;

            _lastPresentedSelection = minimum;
            yield return AnimatePointerSegment(
                0f,
                approachStartAngle,
                spinDuration,
                minimum,
                maximum,
                outcomeCount,
                NextFloat(visualRandom, 1.22f, 1.58f),
                false,
                slotAngle * NextFloat(
                    visualRandom,
                    0.035f,
                    0.075f),
                visualRandom.Next(8, 14),
                0.26f,
                1.04f);

            yield return AnimatePointerSegment(
                approachStartAngle,
                hesitationAngle,
                approachDuration,
                minimum,
                maximum,
                outcomeCount,
                NextFloat(visualRandom, 2.0f, 2.8f),
                false,
                slotAngle * NextFloat(
                    visualRandom,
                    0.010f,
                    0.028f),
                visualRandom.Next(3, 6),
                0.34f,
                1.07f);

            yield return AnimatePointerHesitation(
                hesitationAngle,
                hesitationDuration,
                minimum,
                maximum,
                outcomeCount,
                slotAngle * NextFloat(
                    visualRandom,
                    0.045f,
                    0.085f),
                NextFloat(visualRandom, 1.15f, 1.85f));

            yield return AnimatePointerSegment(
                hesitationAngle,
                overshootAngle,
                revealDuration,
                minimum,
                maximum,
                outcomeCount,
                Mathf.Clamp(
                    decelerationExponent * NextFloat(
                        visualRandom,
                        0.72f,
                        0.95f),
                    2.2f,
                    5.5f),
                false,
                slotAngle * NextFloat(
                    visualRandom,
                    0.004f,
                    0.014f),
                visualRandom.Next(1, 3),
                0.46f,
                1.11f);

            yield return AnimatePointerSegment(
                overshootAngle,
                finalAngle,
                settleDuration,
                minimum,
                maximum,
                outcomeCount,
                1f,
                true,
                0f,
                0,
                0.40f,
                1.08f);

            ApplyPointerPresentation(
                finalAngle,
                minimum,
                maximum,
                outcomeCount,
                0f,
                1f,
                false);

            while (_sequentialCounterCurrent != finalValue)
            {
                AdvanceSequentialCounter(
                    finalValue,
                    0.52f);
                yield return null;
            }
            _sequentialCounterActive = false;

            if (_counterText != null)
            {
                _counterText.text = finalValue.ToString();
                _counterText.color = data.ResultColor;
                _counterText.rectTransform.localScale =
                    Vector3.one * 1.18f;
            }
            UpdateNearMissLabel(
                minimum,
                maximum,
                finalValue);

            if (_resultText != null)
            {
                _resultText.text = data.ResultLabel ?? string.Empty;
                _resultText.color = data.ResultColor;
            }

            SetText(_detailText, data.DetailLabel);
            PlayOneShot(_finishClip, 1f);

            if (resultHoldSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(
                    resultHoldSeconds);
            }
            if (_counterText != null)
            {
                _counterText.rectTransform.localScale = Vector3.one;
            }

            if (complete)
            {
                _presentationRoutine = null;
                MoveButtons(false);
                PresentationCompleted?.Invoke();
            }
        }

        private IEnumerator PlayModifiedD100Sequence(
            D100ModifiedRollResult modifiedRoll,
            PawnRollPresentationData finalData)
        {
            var baseData = new PawnRollPresentationData(
                finalData.Title,
                "기본 d100",
                modifiedRoll.BaseRoll,
                1,
                100,
                $"기본 결과 {modifiedRoll.BaseRoll}",
                "추가 십의 자리 주사위를 굴립니다.",
                finalData.ResultColor,
                1.05f,
                finalData.CheckTarget);
            yield return PlayPresentation(baseData, false);

            for (var index = 1;
                 index < modifiedRoll.TensDice.Count;
                 index++)
            {
                var tens = modifiedRoll.TensDice[index];
                var candidate = modifiedRoll.CandidateRolls[index];
                var extraData = new PawnRollPresentationData(
                    finalData.Title,
                    $"{modifiedRoll.ModifierLabel} · 추가 십의 자리",
                    tens,
                    0,
                    9,
                    $"추가 후보 {candidate}",
                    modifiedRoll.GetCandidateLabel(),
                    finalData.ResultColor,
                    0.72f);
                yield return PlayPresentation(extraData, false);
            }

            yield return PlayPresentation(finalData, true);
        }

        private IEnumerator AnimatePointerSegment(
            float startAngle,
            float endAngle,
            float duration,
            int minimum,
            int maximum,
            int outcomeCount,
            float easeExponent,
            bool smoothStep,
            float wobbleAmplitude,
            int wobbleCycles,
            float tickVolume,
            float pulseScale)
        {
            if (duration <= 0f)
            {
                ApplyPointerPresentation(
                    endAngle,
                    minimum,
                    maximum,
                    outcomeCount,
                    tickVolume,
                    pulseScale,
                    true);
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var eased = smoothStep
                    ? normalized * normalized *
                        (3f - 2f * normalized)
                    : 1f - Mathf.Pow(
                        1f - normalized,
                        Mathf.Max(1f, easeExponent));
                var angle = Mathf.LerpUnclamped(
                    startAngle,
                    endAngle,
                    eased);

                if (wobbleAmplitude > 0f && wobbleCycles > 0)
                {
                    var envelope =
                        Mathf.Sin(normalized * Mathf.PI);
                    angle += Mathf.Sin(
                        normalized * Mathf.PI * 2f *
                        wobbleCycles) *
                        wobbleAmplitude *
                        envelope;
                }

                ApplyPointerPresentation(
                    angle,
                    minimum,
                    maximum,
                    outcomeCount,
                    tickVolume,
                    pulseScale,
                    true);
                RelaxCounterScale();
                yield return null;
            }

            ApplyPointerPresentation(
                endAngle,
                minimum,
                maximum,
                outcomeCount,
                tickVolume,
                pulseScale,
                true);
        }

        private IEnumerator AnimatePointerHesitation(
            float centerAngle,
            float duration,
            int minimum,
            int maximum,
            int outcomeCount,
            float wobbleAmplitude,
            float wobbleCycles)
        {
            if (duration <= 0f)
            {
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var envelope =
                    Mathf.Sin(normalized * Mathf.PI);
                var angle = centerAngle +
                    Mathf.Sin(
                        normalized * Mathf.PI * 2f *
                        wobbleCycles) *
                    wobbleAmplitude *
                    envelope;

                ApplyPointerPresentation(
                    angle,
                    minimum,
                    maximum,
                    outcomeCount,
                    0.38f,
                    1.08f,
                    true);
                RelaxCounterScale();
                yield return null;
            }

            ApplyPointerPresentation(
                centerAngle,
                minimum,
                maximum,
                outcomeCount,
                0.38f,
                1.08f,
                true);
        }

        private void ApplyPointerPresentation(
            float pointerAngle,
            int minimum,
            int maximum,
            int outcomeCount,
            float tickVolume,
            float pulseScale,
            bool playTick)
        {
            if (_pointer != null)
            {
                _pointer.localEulerAngles = new Vector3(
                    0f,
                    0f,
                    pointerAngle);
            }

            UpdateSequentialCounter(
                Mathf.Max(tickVolume, 0.32f),
                Mathf.Max(1f, pulseScale));
        }

        private void BeginSequentialCounter(
            int minimum,
            int maximum,
            int finalValue,
            float duration)
        {
            _sequentialCounterMinimum = minimum;
            _sequentialCounterMaximum = maximum;
            _sequentialCounterFinal = finalValue;
            _sequentialCounterCurrent = minimum;
            _sequentialCounterDuration = Mathf.Max(
                MinimumPresentationDuration,
                duration);
            _sequentialCounterStartedAt = Time.unscaledTime;
            _sequentialCounterActive = true;
            _lastPresentedSelection = minimum;
            UpdateCounterPresentation(minimum, false, 0f, 1f);
        }

        private void UpdateSequentialCounter(
            float tickVolume,
            float pulseScale)
        {
            if (!_sequentialCounterActive)
                return;

            var elapsed = Mathf.Max(
                0f,
                Time.unscaledTime - _sequentialCounterStartedAt);
            var normalized = Mathf.Clamp01(
                elapsed / _sequentialCounterDuration);
            var targetValue = Mathf.RoundToInt(
                Mathf.Lerp(
                    _sequentialCounterMinimum,
                    _sequentialCounterFinal,
                    normalized));
            AdvanceSequentialCounter(
                targetValue,
                tickVolume,
                pulseScale);
        }

        private void AdvanceSequentialCounter(
            int targetValue,
            float tickVolume,
            float pulseScale = 1.08f)
        {
            if (_sequentialCounterCurrent == targetValue)
                return;

            _sequentialCounterCurrent += Math.Sign(
                targetValue - _sequentialCounterCurrent);
            UpdateCounterPresentation(
                _sequentialCounterCurrent,
                true,
                tickVolume,
                pulseScale);
        }

        private void UpdateCounterPresentation(
            int value,
            bool playTick,
            float tickVolume,
            float pulseScale)
        {
            _lastPresentedSelection = value;
            if (_counterText != null)
            {
                _counterText.text = value.ToString();
                _counterText.color = _showsCheckThresholds
                    ? PawnRouletteGradePalette.GetLabelColor(
                        _activeCheckThresholds.GetGrade(value))
                    : Color.white;
                _counterText.rectTransform.localScale =
                    Vector3.one * Mathf.Max(1f, pulseScale);
            }

            UpdateNearMissLabel(
                _sequentialCounterMinimum,
                _sequentialCounterMaximum,
                value);

            if (playTick && tickVolume > 0f)
                PlayOneShot(_tickClip, tickVolume);
        }

        private void RelaxCounterScale()
        {
            if (_counterText == null)
            {
                return;
            }

            _counterText.rectTransform.localScale =
                Vector3.Lerp(
                    _counterText.rectTransform.localScale,
                    Vector3.one,
                    Mathf.Clamp01(
                        Time.unscaledDeltaTime * 18f));
        }

        private System.Random CreateVisualRandom(int finalValue)
        {
            _presentationSerial++;
            var seed = unchecked(
                Environment.TickCount ^
                GetInstanceID() ^
                finalValue * 73856093 ^
                _presentationSerial * 19349663);
            return new System.Random(seed);
        }

        private static int ResolveApproachSlotCount(
            System.Random random,
            int outcomeCount)
        {
            if (outcomeCount <= 1)
            {
                return 0;
            }

            int minimumSlots;
            int maximumSlots;
            if (outcomeCount >= 50)
            {
                minimumSlots = 6;
                maximumSlots = 12;
            }
            else if (outcomeCount >= 20)
            {
                minimumSlots = 4;
                maximumSlots = 8;
            }
            else if (outcomeCount >= 8)
            {
                minimumSlots = 3;
                maximumSlots = Mathf.Min(6, outcomeCount - 1);
            }
            else
            {
                minimumSlots = 1;
                maximumSlots = Mathf.Max(1, outcomeCount - 1);
            }

            return random.Next(
                minimumSlots,
                maximumSlots + 1);
        }

        private static float ResolveHesitationSlotDistance(
            System.Random random,
            int outcomeCount,
            int approachSlots)
        {
            if (outcomeCount <= 1 || approachSlots <= 0)
            {
                return 0f;
            }

            var maximumDistance = Mathf.Min(
                2.65f,
                Mathf.Max(0.68f, approachSlots - 0.65f));
            var minimumDistance = Mathf.Min(
                maximumDistance,
                outcomeCount <= 3 ? 0.62f : 1.15f);
            return NextFloat(
                random,
                minimumDistance,
                maximumDistance);
        }

        private static float NextFloat(
            System.Random random,
            float minimum,
            float maximum)
        {
            if (maximum <= minimum)
            {
                return minimum;
            }

            return Mathf.Lerp(
                minimum,
                maximum,
                (float)random.NextDouble());
        }

        private static int ResolvePointerValue(
            int minimum,
            int outcomeCount,
            float pointerAngle)
        {
            var normalizedTurn = Mathf.Repeat(
                -pointerAngle / 360f,
                1f);
            var index = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedTurn * outcomeCount),
                0,
                outcomeCount - 1);
            return minimum + index;
        }

        private void BuildRuntimeUi(Font requestedFont)
        {
            var font = ResolveFont(requestedFont);
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
            _tickClip = CreateToneClip(
                "PawnRollTick",
                880f,
                0.022f,
                0.11f);
            _finishClip = CreateToneClip(
                "PawnRollFinish",
                1320f,
                0.09f,
                0.20f);
            _ownsRuntimeAudioClips = true;

            _checkButton = CreateButton(
                transform,
                "CheckRollButton",
                Vector2.zero,
                Vector2.one * 92f,
                new Color(0.08f, 0.20f, 0.25f, 0.96f),
                font,
                "판정 굴림\nD100",
                out _checkButtonText);
            _checkButtonRect =
                _checkButton.GetComponent<RectTransform>();
            _effectButton = CreateButton(
                transform,
                "EffectRollButton",
                Vector2.zero,
                Vector2.one * 92f,
                new Color(0.24f, 0.16f, 0.05f, 0.96f),
                font,
                "효과 굴림\nNdN",
                out _effectButtonText);
            _effectButtonRect =
                _effectButton.GetComponent<RectTransform>();

            var presentationRect = CreateRect(
                "RollPresentation",
                transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(1044f, 600f));
            _presentationRect = presentationRect;
            _presentationRoot = presentationRect.gameObject;
            _presentationCanvasGroup =
                presentationRect.gameObject.AddComponent<CanvasGroup>();
            var background = CreateImage(
                presentationRect,
                "Background",
                Vector2.zero,
                Vector2.zero,
                new Color(0.025f, 0.035f, 0.045f, 0.97f));
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.sizeDelta = Vector2.zero;
            background.rectTransform.SetAsFirstSibling();

            var rouletteRect = CreateRect(
                "Roulette",
                presentationRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-250f, 0f),
                new Vector2(500f, 500f));
            _rouletteRect = rouletteRect;
            _rouletteGraphic =
                rouletteRect.gameObject.AddComponent<PawnRouletteGraphic>();
            _rouletteGraphic.raycastTarget = false;

            var labelRoot = CreateRect(
                "OutcomeLabels",
                rouletteRect,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            _rouletteLabelRing =
                labelRoot.gameObject.AddComponent<PawnRouletteLabelRing>();
            _rouletteLabelRing.Configure(font);

            var pointerImage = CreateImage(
                rouletteRect,
                "Pointer",
                Vector2.zero,
                new Vector2(8f, 190f),
                new Color(1f, 0.84f, 0.28f, 1f));
            _pointer = pointerImage.rectTransform;
            _pointer.pivot = new Vector2(0.5f, 0.05f);
            _pointer.anchoredPosition = Vector2.zero;

            _counterText = CreateText(
                rouletteRect,
                "Counter",
                Vector2.zero,
                new Vector2(220f, 96f),
                font,
                58,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            var counterOutline =
                _counterText.gameObject.AddComponent<Outline>();
            counterOutline.effectColor =
                new Color(0.9f, 0.95f, 1f, 0.9f);
            counterOutline.effectDistance = new Vector2(1f, -1f);

            _titleText = CreateText(
                presentationRect,
                "Title",
                new Vector2(260f, 180f),
                new Vector2(390f, 52f),
                font,
                30,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);
            _expressionText = CreateText(
                presentationRect,
                "Expression",
                new Vector2(260f, 122f),
                new Vector2(390f, 40f),
                font,
                22,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Color(0.68f, 0.76f, 0.82f));
            _resultText = CreateText(
                presentationRect,
                "Result",
                new Vector2(260f, 36f),
                new Vector2(390f, 72f),
                font,
                38,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);
            var resultOutline =
                _resultText.gameObject.AddComponent<Outline>();
            resultOutline.effectColor =
                new Color(0.9f, 0.95f, 1f, 0.75f);
            resultOutline.effectDistance = new Vector2(1f, -1f);
            _detailText = CreateText(
                presentationRect,
                "Detail",
                new Vector2(260f, -40f),
                new Vector2(430f, 78f),
                font,
                20,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Color(0.76f, 0.80f, 0.84f));
            _detailText.horizontalOverflow =
                HorizontalWrapMode.Wrap;

            _nearMissText = CreateText(
                presentationRect,
                "NearMiss",
                new Vector2(260f, -150f),
                new Vector2(430f, 64f),
                font,
                25,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.28f));

            _thresholdLegendText = CreateText(
                presentationRect,
                "ThresholdLegend",
                new Vector2(0f, -270f),
                new Vector2(720f, 58f),
                font,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            _thresholdLegendText.supportRichText = true;
            _thresholdLegendText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _thresholdLegendText.verticalOverflow =
                VerticalWrapMode.Truncate;
            var thresholdOutline =
                _thresholdLegendText.gameObject.AddComponent<Outline>();
            thresholdOutline.effectColor =
                new Color(0f, 0f, 0f, 0.95f);
            thresholdOutline.effectDistance =
                new Vector2(1.5f, -1.5f);

            _inputWidget = PawnRollInputWidget.CreateRuntime(
                transform as RectTransform,
                font);
        }

        private void OnEnable()
        {
            BindListeners();
            RefreshResponsiveLayout();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_checkButtonRect != null)
            {
                RefreshResponsiveLayout();
            }
        }

        private void OnDisable()
        {
            UnbindListeners();
            CancelPresentation();
        }

        private void OnDestroy()
        {
            UnbindListeners();
            if (_inputWidget != null &&
                _inputWidget.transform.parent != transform)
            {
                Destroy(_inputWidget.gameObject);
                _inputWidget = null;
            }

            if (_presentationRoot != null &&
                _presentationRoot.transform.parent != transform)
            {
                Destroy(_presentationRoot);
                _presentationRoot = null;
                _presentationRect = null;
            }

            if (_ownsRuntimeAudioClips)
            {
                Destroy(_tickClip);
                Destroy(_finishClip);
            }

            RollInputOpened = null;
            CheckRollRequested = null;
            EffectRollRequested = null;
            ResourceRollRequested = null;
            PresentationCompleted = null;
        }

        private void BindListeners()
        {
            if (_listenersBound ||
                _checkButton == null ||
                _effectButton == null)
            {
                return;
            }

            _checkButton.onClick.AddListener(HandleCheckClicked);
            _effectButton.onClick.AddListener(HandleEffectClicked);
            if (_inputWidget != null)
            {
                _inputWidget.CheckConfirmed += HandleCheckConfirmed;
                _inputWidget.EffectConfirmed += HandleEffectConfirmed;
                _inputWidget.ResourceConfirmed += HandleResourceConfirmed;
                _inputWidget.Cancelled += HandleInputCancelled;
            }

            _listenersBound = true;
        }

        private void UnbindListeners()
        {
            if (!_listenersBound)
            {
                return;
            }

            if (_checkButton != null)
            {
                _checkButton.onClick.RemoveListener(HandleCheckClicked);
            }

            if (_effectButton != null)
            {
                _effectButton.onClick.RemoveListener(HandleEffectClicked);
            }

            if (_inputWidget != null)
            {
                _inputWidget.CheckConfirmed -= HandleCheckConfirmed;
                _inputWidget.EffectConfirmed -= HandleEffectConfirmed;
                _inputWidget.ResourceConfirmed -= HandleResourceConfirmed;
                _inputWidget.Cancelled -= HandleInputCancelled;
            }

            _listenersBound = false;
        }

        private void HandleCheckClicked()
        {
            CancelRoutine();
            if (_presentationRoot != null)
            {
                _presentationRoot.SetActive(false);
            }

            RollInputOpened?.Invoke();
            MoveButtons(true);
            _inputWidget?.OpenCheck(_defaultCheckTarget);
        }

        private void HandleEffectClicked()
        {
            CancelRoutine();
            if (_presentationRoot != null)
            {
                _presentationRoot.SetActive(false);
            }

            RollInputOpened?.Invoke();
            MoveButtons(true);
            _inputWidget?.OpenEffect(
                _defaultDiceCount,
                _defaultDiceSides,
                _defaultDiceModifier);
        }

        private void HandleCheckConfirmed(
            PawnCheckRollRequest request)
        {
            _defaultCheckTarget = request.Target;
            CheckRollRequested?.Invoke(request);
        }

        private void HandleEffectConfirmed(
            PawnEffectRollRequest request)
        {
            _defaultDiceCount = request.DiceCount;
            _defaultDiceSides = request.DiceSides;
            _defaultDiceModifier = request.Modifier;
            EffectRollRequested?.Invoke(request);
        }

        private void HandleResourceConfirmed(
            PawnResourceRollRequest request)
        {
            ResourceRollRequested?.Invoke(request);
        }

        private void HandleInputCancelled()
        {
            MoveButtons(false);
        }

        private IEnumerator AnimatePresentationOpen()
        {
            if (_presentationRect == null ||
                _presentationCanvasGroup == null)
            {
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < PanelOpenDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(
                    elapsed / PanelOpenDuration);
                var eased =
                    1f - Mathf.Pow(1f - normalized, 3f);
                _presentationRect.localScale = new Vector3(
                    Mathf.Lerp(0.92f, 1f, eased),
                    Mathf.Lerp(0.06f, 1f, eased),
                    1f);
                _presentationCanvasGroup.alpha = eased;
                yield return null;
            }

            _presentationRect.localScale = Vector3.one;
            _presentationCanvasGroup.alpha = 1f;
        }

        private void RefreshActionButtonLayout()
        {
            if (_checkButtonRect == null ||
                _effectButtonRect == null)
            {
                return;
            }

            var canvasWidth = _canvasRect != null
                ? _canvasRect.rect.width
                : 1920f;
            var panelHeight = _hostPanelRect != null
                ? _hostPanelRect.rect.height
                : 150f;
            var buttonSize = Mathf.Clamp(
                Mathf.Min(
                    panelHeight * 0.68f,
                    canvasWidth * 0.055f),
                MinimumActionButtonSize,
                MaximumActionButtonSize);
            var gap = Mathf.Clamp(
                buttonSize * 0.08f,
                4f,
                10f);

            ConfigureActionRect(
                _moveButtonRect,
                buttonSize);
            ConfigureActionRect(
                _checkButtonRect,
                buttonSize);
            ConfigureActionRect(
                _effectButtonRect,
                buttonSize);

            var step = buttonSize + gap;
            _closedMovePosition = new Vector2(-step, 0f);
            _closedCheckPosition = Vector2.zero;
            _closedEffectPosition = new Vector2(step, 0f);
            _reservedRightWidth = 0f;

            SetButtonPositions(_buttonsOpen);
            ConfigureButtonLabel(
                _moveButtonRect,
                Mathf.RoundToInt(buttonSize * 0.17f));
            ConfigureButtonLabel(
                _checkButtonRect,
                Mathf.RoundToInt(buttonSize * 0.17f));
            ConfigureButtonLabel(
                _effectButtonRect,
                Mathf.RoundToInt(buttonSize * 0.17f));
        }

        private void RefreshPresentationLayout()
        {
            if (_presentationRect == null)
            {
                return;
            }

            var canvasSize = _canvasRect != null
                ? _canvasRect.rect.size
                : new Vector2(1920f, 1080f);
            var availableWidth = Mathf.Max(
                420f,
                canvasSize.x - 48f);
            var availableHeight = Mathf.Max(
                360f,
                canvasSize.y - 48f);
            var panelWidth = Mathf.Min(
                availableWidth,
                Mathf.Clamp(
                    canvasSize.x * 0.68f,
                    760f,
                    1120f));
            var panelHeight = Mathf.Min(
                availableHeight,
                Mathf.Clamp(
                    canvasSize.y * 0.64f,
                    480f,
                    680f));
            var panelSize =
                new Vector2(panelWidth, panelHeight);
            _presentationRect.sizeDelta = panelSize;
            _presentationRect.anchorMin =
                new Vector2(0.5f, 0.5f);
            _presentationRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            _presentationRect.pivot =
                new Vector2(0.5f, 0.5f);
            _presentationRect.anchoredPosition = Vector2.zero;

            if (_rouletteRect == null)
            {
                return;
            }

            var rouletteSize = Mathf.Min(
                panelHeight - 128f,
                panelWidth * 0.52f);
            _rouletteRect.sizeDelta =
                Vector2.one * rouletteSize;
            _rouletteRect.anchoredPosition = Vector2.zero;

            if (_pointer != null)
            {
                _pointer.sizeDelta = new Vector2(
                    Mathf.Clamp(
                        rouletteSize * 0.016f,
                        5f,
                        10f),
                    rouletteSize * 0.38f);
            }

            if (_counterText != null)
            {
                _counterText.rectTransform.sizeDelta =
                    new Vector2(
                        rouletteSize * 0.46f,
                        rouletteSize * 0.20f);
                _counterText.fontSize = Mathf.RoundToInt(
                    Mathf.Clamp(
                        rouletteSize * 0.12f,
                        36f,
                        66f));
            }

            var textCenterX = 0f;
            var textWidth = Mathf.Min(
                panelWidth * 0.72f,
                720f);

            if (_thresholdLegendText != null)
            {
                _thresholdLegendText.rectTransform.anchoredPosition =
                    new Vector2(
                        textCenterX,
                        -panelHeight * 0.455f);
                _thresholdLegendText.rectTransform.sizeDelta =
                    new Vector2(
                        textWidth,
                        panelHeight * 0.085f);
                _thresholdLegendText.fontSize = Mathf.RoundToInt(
                    Mathf.Clamp(
                        panelHeight * 0.025f,
                        13f,
                        19f));
            }

            ConfigurePresentationText(
                _titleText,
                new Vector2(
                    textCenterX,
                    panelHeight * 0.41f),
                new Vector2(textWidth, panelHeight * 0.075f),
                Mathf.RoundToInt(panelHeight * 0.046f));
            ConfigurePresentationText(
                _expressionText,
                new Vector2(
                    textCenterX,
                    panelHeight * 0.34f),
                new Vector2(textWidth, panelHeight * 0.06f),
                Mathf.RoundToInt(panelHeight * 0.030f));
            ConfigurePresentationText(
                _resultText,
                new Vector2(
                    textCenterX,
                    -panelHeight * 0.25f),
                new Vector2(textWidth, panelHeight * 0.10f),
                Mathf.RoundToInt(panelHeight * 0.052f));
            ConfigurePresentationText(
                _detailText,
                new Vector2(
                    textCenterX,
                    -panelHeight * 0.34f),
                new Vector2(textWidth, panelHeight * 0.09f),
                Mathf.RoundToInt(panelHeight * 0.027f));
            ConfigurePresentationText(
                _nearMissText,
                new Vector2(
                    textCenterX,
                    -panelHeight * 0.16f),
                new Vector2(textWidth, panelHeight * 0.075f),
                Mathf.RoundToInt(panelHeight * 0.036f));

            SetPresentationTextAlignment(_titleText);
            SetPresentationTextAlignment(_expressionText);
            SetPresentationTextAlignment(_resultText);
            SetPresentationTextAlignment(_detailText);
            SetPresentationTextAlignment(_nearMissText);

            _rouletteLabelRing?.RefreshLayout();
        }

        private static void SetPresentationTextAlignment(Text target)
        {
            if (target != null)
            {
                target.alignment = TextAnchor.MiddleCenter;
            }
        }

        private static void ConfigureActionRect(
            RectTransform target,
            float size)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = new Vector2(0.5f, 0.5f);
            target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0.5f, 0.5f);
            target.sizeDelta = Vector2.one * size;
            target.localScale = Vector3.one;
        }

        private static void ConfigureButtonLabel(
            RectTransform buttonRect,
            int preferredFontSize)
        {
            if (buttonRect == null)
            {
                return;
            }

            var label =
                buttonRect.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                return;
            }

            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = Mathf.Clamp(
                preferredFontSize,
                12,
                20);
            label.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            label.verticalOverflow =
                VerticalWrapMode.Truncate;
        }

        private static void ConfigurePresentationText(
            Text target,
            Vector2 position,
            Vector2 size,
            int fontSize)
        {
            if (target == null)
            {
                return;
            }

            target.rectTransform.anchoredPosition = position;
            target.rectTransform.sizeDelta = size;
            target.fontSize = Mathf.Clamp(
                fontSize,
                12,
                46);
        }

        private void UpdateCurrentSelection(
            int minimum,
            int maximum,
            int outcomeCount,
            float pointerAngle)
        {
            var normalizedTurn = Mathf.Repeat(
                -pointerAngle / 360f,
                1f);
            var index = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedTurn * outcomeCount),
                0,
                outcomeCount - 1);
            var value = minimum + index;

            if (_counterText != null)
            {
                _counterText.text = value.ToString();
                _counterText.color = _showsCheckThresholds
                    ? PawnRouletteGradePalette.GetLabelColor(
                        _activeCheckThresholds.GetGrade(value))
                    : Color.white;
                _counterText.rectTransform.localScale =
                    Vector3.one * 1.06f;
            }

            UpdateNearMissLabel(
                minimum,
                maximum,
                value);
        }

        private void UpdateNearMissLabel(
            int minimum,
            int maximum,
            int value)
        {
            var previous = value > minimum
                ? value - 1
                : maximum;
            var next = value < maximum
                ? value + 1
                : minimum;
            SetText(
                _nearMissText,
                $"{previous}   ◀  {value}  ▶   {next}");
        }

        private void UpdateThresholdLegend()
        {
            if (_thresholdLegendText == null)
            {
                return;
            }

            if (!_showsCheckThresholds)
            {
                _thresholdLegendText.text = string.Empty;
                _thresholdLegendText.gameObject.SetActive(false);
                return;
            }

            _thresholdLegendText.gameObject.SetActive(true);
            var thresholds = _activeCheckThresholds;
            var entries = new List<string>(6);
            AddThresholdEntry(
                entries,
                CheckRollGrade.Critical,
                "대성공",
                1,
                1);
            AddThresholdEntry(
                entries,
                CheckRollGrade.ExtremeSuccess,
                "극단",
                2,
                thresholds.ExtremeMaximum);
            AddThresholdEntry(
                entries,
                CheckRollGrade.HardSuccess,
                "어려움",
                thresholds.ExtremeMaximum + 1,
                thresholds.HardMaximum);
            AddThresholdEntry(
                entries,
                CheckRollGrade.Success,
                "성공",
                thresholds.HardMaximum + 1,
                Mathf.Min(
                    thresholds.Target,
                    thresholds.FumbleMinimum - 1));
            AddThresholdEntry(
                entries,
                CheckRollGrade.Failure,
                "실패",
                thresholds.Target + 1,
                thresholds.FumbleMinimum - 1);
            AddThresholdEntry(
                entries,
                CheckRollGrade.Fumble,
                "대실패",
                thresholds.FumbleMinimum,
                100);

            var firstRowCount = Mathf.Min(3, entries.Count);
            var firstRow = string.Join(
                "   ",
                entries.GetRange(0, firstRowCount));
            var secondRow = entries.Count > firstRowCount
                ? string.Join(
                    "   ",
                    entries.GetRange(
                        firstRowCount,
                        entries.Count - firstRowCount))
                : string.Empty;
            _thresholdLegendText.text =
                string.IsNullOrEmpty(secondRow)
                    ? firstRow
                    : firstRow + "\n" + secondRow;
        }

        private static void AddThresholdEntry(
            List<string> entries,
            CheckRollGrade grade,
            string label,
            int minimum,
            int maximum)
        {
            if (minimum > maximum)
            {
                return;
            }

            var range = minimum == maximum
                ? minimum.ToString()
                : $"{minimum}-{maximum}";
            var color = ColorUtility.ToHtmlStringRGB(
                PawnRouletteGradePalette.GetLabelColor(grade));
            entries.Add(
                $"<color=#{color}>■ {label} {range}</color>");
        }

        private void MoveButtons(bool open)
        {
            _buttonsOpen = open;
            SetButtonPositions(open);
        }

        private void SetButtonPositions(bool open)
        {
            _buttonsOpen = open;
            if (_moveButtonRect != null)
            {
                _moveButtonRect.anchoredPosition =
                    _closedMovePosition;
                _moveButtonRect.gameObject.SetActive(!open);
            }

            if (_checkButtonRect != null)
            {
                _checkButtonRect.anchoredPosition =
                    _closedCheckPosition;
                _checkButtonRect.gameObject.SetActive(
                    _actionButtonsVisible && !open);
            }

            if (_effectButtonRect != null)
            {
                _effectButtonRect.anchoredPosition =
                    _closedEffectPosition;
                _effectButtonRect.gameObject.SetActive(
                    _actionButtonsVisible && !open);
            }
        }

        private static RectTransform ResolveCanvasRect(
            RectTransform requestedCanvasRect,
            RectTransform hostPanelRect)
        {
            var source = requestedCanvasRect != null
                ? requestedCanvasRect
                : hostPanelRect;
            var canvas = source != null
                ? source.GetComponentInParent<Canvas>()
                : null;
            var rootCanvas = canvas != null
                ? canvas.rootCanvas
                : null;
            var rootRect = rootCanvas != null
                ? rootCanvas.transform as RectTransform
                : null;
            return rootRect != null
                ? rootRect
                : source;
        }

        private void CancelRoutine()
        {
            if (_presentationRoutine == null)
            {
                return;
            }

            StopCoroutine(_presentationRoutine);
            _presentationRoutine = null;
            _sequentialCounterActive = false;
        }

        private void PlayOneShot(AudioClip clip, float volume)
        {
            if (_audioSource != null && clip != null)
            {
                _audioSource.PlayOneShot(clip, volume);
            }
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
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

        private static AudioClip CreateToneClip(
            string clipName,
            float frequency,
            float durationSeconds,
            float amplitude)
        {
            const int sampleRate = 44100;
            var sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(sampleRate * durationSeconds));
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                var normalized = i / (float)sampleCount;
                var envelope = 1f - normalized;
                samples[i] =
                    Mathf.Sin(
                        Mathf.PI * 2f *
                        frequency *
                        i /
                        sampleRate) *
                    envelope *
                    amplitude;
            }

            var clip = AudioClip.Create(
                clipName,
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static Button CreateButton(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color,
            Font font,
            string label,
            out Text labelText)
        {
            var buttonImage = CreateImage(
                parent,
                objectName,
                anchoredPosition,
                size,
                color);
            var button = buttonImage.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = color * 1.18f;
            colors.pressedColor = color * 0.78f;
            colors.disabledColor = new Color(
                color.r,
                color.g,
                color.b,
                0.45f);
            button.colors = colors;

            labelText = CreateText(
                buttonImage.rectTransform,
                "Label",
                Vector2.zero,
                size,
                font,
                13,
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
    }

    internal sealed class PawnRouletteLabelRing : MonoBehaviour
    {
        private readonly List<Text> _labels = new List<Text>();
        private Font _font;
        private int _minimum = 1;
        private int _maximum = 100;
        private int _maximumVisibleLabels = 100;
        private bool _showCheckGrades;
        private D100CheckThresholds _checkThresholds;

        public void Configure(Font font)
        {
            _font = font;
        }

        public void SetRange(
            int minimum,
            int maximum,
            int maximumVisibleLabels,
            bool showCheckGrades,
            D100CheckThresholds checkThresholds)
        {
            var resolvedMinimum = Mathf.Min(
                minimum,
                maximum);
            var resolvedMaximum = Mathf.Max(
                minimum,
                maximum);
            minimum = resolvedMinimum;
            maximum = resolvedMaximum;
            maximumVisibleLabels = Mathf.Max(
                1,
                maximumVisibleLabels);

            if (_minimum == minimum &&
                _maximum == maximum &&
                _maximumVisibleLabels == maximumVisibleLabels &&
                _showCheckGrades == showCheckGrades &&
                (!_showCheckGrades ||
                 _checkThresholds.Target == checkThresholds.Target) &&
                _labels.Count > 0)
            {
                RefreshLayout();
                return;
            }

            _minimum = minimum;
            _maximum = maximum;
            _maximumVisibleLabels = maximumVisibleLabels;
            _showCheckGrades = showCheckGrades;
            _checkThresholds = checkThresholds;
            RebuildLabels();
        }

        public void RefreshLayout()
        {
            if (_labels.Count == 0)
            {
                return;
            }

            var rectTransform = transform as RectTransform;
            if (rectTransform == null)
            {
                return;
            }

            var size = rectTransform.rect.size;
            var radius = Mathf.Min(size.x, size.y) * 0.425f;
            var circumference = Mathf.PI * 2f * radius;
            var fontSize = Mathf.Clamp(
                Mathf.FloorToInt(
                    circumference /
                    Mathf.Max(1, _labels.Count) *
                    0.62f),
                8,
                18);
            var outcomeCount = Mathf.Max(
                1,
                _maximum - _minimum + 1);

            for (var i = 0; i < _labels.Count; i++)
            {
                var label = _labels[i];
                if (label == null)
                {
                    continue;
                }

                var value = ResolveVisibleValue(
                    i,
                    _labels.Count,
                    _minimum,
                    _maximum);
                var valueIndex = value - _minimum;
                var ratio =
                    (valueIndex + 0.5f) / outcomeCount;
                var angle = Mathf.PI * 2f * ratio;
                var direction = new Vector2(
                    Mathf.Sin(angle),
                    Mathf.Cos(angle));
                label.rectTransform.anchoredPosition =
                    direction * radius;
                label.rectTransform.sizeDelta =
                    new Vector2(
                        Mathf.Max(32f, fontSize * 4.4f),
                        Mathf.Max(16f, fontSize * 1.5f));
                label.fontSize = fontSize;
                label.text = value.ToString();
                label.color = _showCheckGrades
                    ? PawnRouletteGradePalette.GetLabelColor(
                        _checkThresholds.GetGrade(value))
                    : i % 2 == 0
                        ? new Color(0.88f, 0.98f, 1f, 0.96f)
                        : new Color(1f, 0.88f, 0.48f, 0.96f);
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            RefreshLayout();
        }

        private void RebuildLabels()
        {
            for (var i = 0; i < _labels.Count; i++)
            {
                if (_labels[i] != null)
                {
                    Destroy(_labels[i].gameObject);
                }
            }

            _labels.Clear();
            var outcomeCount = Mathf.Max(
                1,
                _maximum - _minimum + 1);
            var labelCount = Mathf.Min(
                outcomeCount,
                _maximumVisibleLabels);

            for (var i = 0; i < labelCount; i++)
            {
                var labelObject = new GameObject(
                    $"OutcomeLabel_{i}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                var labelRect =
                    labelObject.GetComponent<RectTransform>();
                labelRect.SetParent(transform, false);
                labelRect.anchorMin =
                    new Vector2(0.5f, 0.5f);
                labelRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                labelRect.pivot =
                    new Vector2(0.5f, 0.5f);

                var label = labelObject.GetComponent<Text>();
                label.font = _font;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.raycastTarget = false;
                label.horizontalOverflow =
                    HorizontalWrapMode.Overflow;
                label.verticalOverflow =
                    VerticalWrapMode.Overflow;
                _labels.Add(label);
            }

            RefreshLayout();
        }

        private static int ResolveVisibleValue(
            int labelIndex,
            int labelCount,
            int minimum,
            int maximum)
        {
            if (labelCount <= 1)
            {
                return minimum;
            }

            var normalized =
                labelIndex / (float)(labelCount - 1);
            return Mathf.RoundToInt(
                Mathf.Lerp(minimum, maximum, normalized));
        }
    }

    internal static class PawnRouletteGradePalette
    {
        public static Color32 GetBandColor(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return new Color32(126, 232, 82, 255);
                case CheckRollGrade.ExtremeSuccess:
                    return new Color32(24, 190, 145, 255);
                case CheckRollGrade.HardSuccess:
                    return new Color32(38, 151, 211, 255);
                case CheckRollGrade.Success:
                    return new Color32(73, 101, 204, 255);
                case CheckRollGrade.Fumble:
                    return new Color32(114, 16, 54, 255);
                default:
                    return new Color32(211, 83, 54, 255);
            }
        }

        public static Color GetLabelColor(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return new Color(0.62f, 1f, 0.38f, 1f);
                case CheckRollGrade.ExtremeSuccess:
                    return new Color(0.24f, 1f, 0.76f, 1f);
                case CheckRollGrade.HardSuccess:
                    return new Color(0.33f, 0.82f, 1f, 1f);
                case CheckRollGrade.Success:
                    return new Color(0.55f, 0.68f, 1f, 1f);
                case CheckRollGrade.Fumble:
                    return new Color(1f, 0.22f, 0.48f, 1f);
                default:
                    return new Color(1f, 0.47f, 0.33f, 1f);
            }
        }
    }

    internal sealed class PawnRouletteGraphic : MaskableGraphic
    {
        private const float InnerRadiusRatio = 0.36f;
        private const float OuterRadiusRatio = 0.49f;
        private int _segmentCount = 100;
        private bool _showCheckBands;
        private D100CheckThresholds _checkThresholds;

        public void SetPresentation(
            int outcomeCount,
            bool showCheckBands,
            D100CheckThresholds checkThresholds)
        {
            var segmentCount = Mathf.Clamp(
                outcomeCount,
                1,
                240);
            if (_segmentCount == segmentCount &&
                _showCheckBands == showCheckBands &&
                (!_showCheckBands ||
                 _checkThresholds.Target == checkThresholds.Target))
            {
                return;
            }

            _segmentCount = segmentCount;
            _showCheckBands = showCheckBands;
            _checkThresholds = checkThresholds;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(rect.width, rect.height);
            var innerRadius = radius * InnerRadiusRatio;
            var outerRadius = radius * OuterRadiusRatio;
            var center = rect.center;

            for (var i = 0; i < _segmentCount; i++)
            {
                var startAngle =
                    Mathf.PI * 2f * i / _segmentCount;
                var endAngle =
                    Mathf.PI * 2f * (i + 1) / _segmentCount;
                var segmentColor = _showCheckBands
                    ? PawnRouletteGradePalette.GetBandColor(
                        _checkThresholds.GetGrade(i + 1))
                    : i % 2 == 0
                        ? new Color32(42, 174, 196, 255)
                        : new Color32(209, 156, 45, 255);
                var gap = Mathf.Min(
                    (endAngle - startAngle) * 0.12f,
                    0.0025f);

                AddRingSegment(
                    vertexHelper,
                    center,
                    innerRadius,
                    outerRadius,
                    startAngle + gap,
                    endAngle - gap,
                    segmentColor);
            }

            if (_showCheckBands)
            {
                AddCheckBoundaryLines(
                    vertexHelper,
                    center,
                    innerRadius,
                    outerRadius);
            }
        }

        private void AddCheckBoundaryLines(
            VertexHelper vertexHelper,
            Vector2 center,
            float innerRadius,
            float outerRadius)
        {
            var boundaryValues = new[]
            {
                0,
                1,
                _checkThresholds.ExtremeMaximum,
                _checkThresholds.HardMaximum,
                _checkThresholds.Target,
                _checkThresholds.FumbleMinimum - 1,
                100
            };
            var drawn = new bool[101];
            for (var i = 0; i < boundaryValues.Length; i++)
            {
                var value = Mathf.Clamp(
                    boundaryValues[i],
                    0,
                    100);
                if (drawn[value])
                {
                    continue;
                }

                drawn[value] = true;
                var angle =
                    Mathf.PI * 2f * value / 100f;
                AddRadialLine(
                    vertexHelper,
                    center,
                    innerRadius * 0.94f,
                    outerRadius * 1.015f,
                    angle,
                    Mathf.Max(1.8f, outerRadius * 0.012f),
                    new Color32(244, 247, 250, 255));
            }
        }

        private static void AddRadialLine(
            VertexHelper vertexHelper,
            Vector2 center,
            float innerRadius,
            float outerRadius,
            float angle,
            float width,
            Color32 color)
        {
            var direction = new Vector2(
                Mathf.Sin(angle),
                Mathf.Cos(angle));
            var perpendicular = new Vector2(
                direction.y,
                -direction.x) * (width * 0.5f);
            var startIndex = vertexHelper.currentVertCount;
            AddVertex(
                vertexHelper,
                center + direction * innerRadius - perpendicular,
                color);
            AddVertex(
                vertexHelper,
                center + direction * outerRadius - perpendicular,
                color);
            AddVertex(
                vertexHelper,
                center + direction * outerRadius + perpendicular,
                color);
            AddVertex(
                vertexHelper,
                center + direction * innerRadius + perpendicular,
                color);
            vertexHelper.AddTriangle(
                startIndex,
                startIndex + 1,
                startIndex + 2);
            vertexHelper.AddTriangle(
                startIndex,
                startIndex + 2,
                startIndex + 3);
        }

        private static void AddRingSegment(
            VertexHelper vertexHelper,
            Vector2 center,
            float innerRadius,
            float outerRadius,
            float startAngle,
            float endAngle,
            Color32 color)
        {
            var startDirection = new Vector2(
                Mathf.Sin(startAngle),
                Mathf.Cos(startAngle));
            var endDirection = new Vector2(
                Mathf.Sin(endAngle),
                Mathf.Cos(endAngle));
            var startIndex = vertexHelper.currentVertCount;

            AddVertex(
                vertexHelper,
                center + startDirection * innerRadius,
                color);
            AddVertex(
                vertexHelper,
                center + startDirection * outerRadius,
                color);
            AddVertex(
                vertexHelper,
                center + endDirection * outerRadius,
                color);
            AddVertex(
                vertexHelper,
                center + endDirection * innerRadius,
                color);

            vertexHelper.AddTriangle(
                startIndex,
                startIndex + 1,
                startIndex + 2);
            vertexHelper.AddTriangle(
                startIndex,
                startIndex + 2,
                startIndex + 3);
        }

        private static void AddVertex(
            VertexHelper vertexHelper,
            Vector2 position,
            Color32 color)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertexHelper.AddVert(vertex);
        }
    }
}
