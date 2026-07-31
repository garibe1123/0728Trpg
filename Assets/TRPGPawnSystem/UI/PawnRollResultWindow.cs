using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public enum PawnRollResultTone
    {
        Standard,
        Critical,
        Fumble
    }

    public readonly struct PawnRollWindowData
    {
        public PawnRollWindowData(
            string title,
            string expression,
            int finalValue,
            int minimumValue,
            int maximumValue,
            string resultLabel,
            string detailLabel,
            Color resultColor,
            float durationSeconds,
            PawnRollResultTone resultTone =
                PawnRollResultTone.Standard)
        {
            Title = title ?? string.Empty;
            Expression = expression ?? string.Empty;
            FinalValue = finalValue;
            MinimumValue = minimumValue;
            MaximumValue = maximumValue;
            ResultLabel = resultLabel ?? string.Empty;
            DetailLabel = detailLabel ?? string.Empty;
            ResultColor = resultColor;
            DurationSeconds = durationSeconds;
            ResultTone = resultTone;
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
        public PawnRollResultTone ResultTone { get; }
    }

    /// <summary>
    /// 판정 결정 후 별도로 열리는 룰렛 결과 창입니다.
    /// 기존 PawnRollWidget의 감속, 떨림, 틱 효과음을 독립 창으로 복원합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnRollResultWindow : MonoBehaviour
    {
        private const float MinimumDuration = 0.25f;
        private const float FastTickSeconds = 0.022f;
        private const float SlowTickSeconds = 0.16f;
        private const float PointerRotations = 6f;

        private RectTransform _rootRect;
        private RectTransform _safeAreaRect;
        private RectTransform _panelRect;
        private PawnUiDragHandle _dragHandle;
        private Text _titleText;
        private Text _expressionText;
        private Text _counterText;
        private Text _resultText;
        private Text _detailText;
        private RectTransform _pointer;
        private GameObject _failureActionsRoot;
        private RectTransform _failureActionsRect;
        private Button _acceptButton;
        private Button _challengeButton;
        private Button _luckButton;
        private Text _challengeButtonText;
        private Text _luckButtonText;
        private GameObject _confirmationRoot;
        private RectTransform _confirmationRect;
        private Text _confirmationTitleText;
        private Text _confirmationBodyText;
        private Text _confirmationAcceptText;
        private PawnCheckConfirmationKind _confirmationKind;
        private int _lastLuckCost;
        private int _lastCurrentLuck;
        private bool _lastChallengeAvailable;
        private bool _lastLuckAvailable;
        private AudioSource _audioSource;
        private AudioClip _tickClip;
        private AudioClip _finishClip;
        private AudioClip _criticalClip;
        private AudioClip _fumbleClip;
        private AudioClip _decisionClip;
        private Coroutine _routine;
        private Coroutine _failureActionRoutine;
        private Font _font;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        public event Action Closed;
        public event Action PresentationCompleted;
        public event Action AcceptRequested;
        public event Action ChallengeRequested;
        public event Action LuckRequested;
        public event Action<PawnCheckConfirmationKind>
            ConfirmationAccepted;

        public Vector2 WindowPosition =>
            _dragHandle != null
                ? _dragHandle.AnchoredPosition
                : Vector2.zero;

        public static PawnRollResultWindow CreateRuntime(
            Canvas rootCanvas,
            Font font)
        {
            if (rootCanvas == null)
                throw new ArgumentNullException(nameof(rootCanvas));

            var root = new GameObject(
                "PawnRollResultWindow",
                typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(rootCanvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            var window = root.AddComponent<PawnRollResultWindow>();
            window.Build(rect, font);
            root.SetActive(false);
            return window;
        }

        public void Play(
            in PawnRollWindowData data,
            Action completed)
        {
            StopRoutine();
            StopFailureActionRoutine();
            HideConfirmationImmediate();
            HideFailureActionsImmediate();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RefreshSafeArea(true);
            _panelRect.SetAsLastSibling();
            _routine = StartCoroutine(PlayRoutine(data, completed));
        }

        public void ShowInstant(in PawnRollWindowData data)
        {
            StopRoutine();
            StopFailureActionRoutine();
            HideConfirmationImmediate();
            HideFailureActionsImmediate();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RefreshSafeArea(true);
            ApplyFinal(data);
        }

        public void Hide()
        {
            StopRoutine();
            StopFailureActionRoutine();
            HideConfirmationImmediate();
            gameObject.SetActive(false);
        }

        public void ShowFailureActions(
            int luckCost,
            int currentLuck,
            bool challengeAvailable,
            bool luckAvailable)
        {
            if (_failureActionsRoot == null)
                return;

            _lastLuckCost = luckCost;
            _lastCurrentLuck = currentLuck;
            _lastChallengeAvailable = challengeAvailable;
            _lastLuckAvailable = luckAvailable;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _panelRect.SetAsLastSibling();
            HideConfirmationImmediate();
            _acceptButton.interactable = true;
            _challengeButton.interactable = challengeAvailable;
            _luckButton.interactable = luckAvailable;
            SetText(
                _challengeButtonText,
                challengeAvailable
                    ? "대항한다"
                    : "대항 사용 완료");
            SetText(
                _luckButtonText,
                luckCost > 0
                    ? $"운을 사용한다\n필요 {luckCost} / 보유 {currentLuck}"
                    : "운 사용 불가");

            StopFailureActionRoutine();
            _failureActionsRoot.SetActive(true);
            ApplyFailureActionLayout(true);
            _failureActionRoutine = StartCoroutine(
                AnimateFailureActions());
            PlayOneShot(_decisionClip, 0.9f);
        }

        public void ShowConfirmation(
            PawnCheckConfirmationKind kind,
            string title,
            string body,
            string acceptLabel)
        {
            if (_confirmationRoot == null ||
                kind == PawnCheckConfirmationKind.None)
            {
                return;
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _panelRect.SetAsLastSibling();
            StopFailureActionRoutine();
            if (_failureActionsRoot != null)
                _failureActionsRoot.SetActive(false);

            _confirmationKind = kind;
            SetText(_confirmationTitleText, title);
            SetText(_confirmationBodyText, body);
            _confirmationRoot.SetActive(true);
            ApplyConfirmationAcceptLabel(kind, acceptLabel);
            ApplyFailureActionLayout(true);
            Canvas.ForceUpdateCanvases();
            PlayOneShot(_decisionClip, 0.75f);
        }


        private void ApplyConfirmationAcceptLabel(
            PawnCheckConfirmationKind kind,
            string requestedLabel)
        {
            if (_confirmationAcceptText == null &&
                _confirmationRoot != null)
            {
                var labelTransform = _confirmationRoot.transform.Find(
                    "AcceptConfirmation/Label");
                if (labelTransform != null)
                    _confirmationAcceptText =
                        labelTransform.GetComponent<Text>();
            }

            if (_confirmationAcceptText == null)
                return;

            var fallbackLabel = kind == PawnCheckConfirmationKind.Challenge
                ? "강행한다"
                : "운을 사용한다";
            var resolvedLabel = string.IsNullOrWhiteSpace(requestedLabel)
                ? fallbackLabel
                : requestedLabel;

            _confirmationAcceptText.gameObject.SetActive(true);
            _confirmationAcceptText.text = resolvedLabel;
            _confirmationAcceptText.color = Color.white;
            _confirmationAcceptText.fontSize = 18;
            _confirmationAcceptText.fontStyle = FontStyle.Bold;
            _confirmationAcceptText.alignment = TextAnchor.MiddleCenter;
            _confirmationAcceptText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _confirmationAcceptText.verticalOverflow =
                VerticalWrapMode.Overflow;
            _confirmationAcceptText.raycastTarget = false;
            _confirmationAcceptText.rectTransform.SetAsLastSibling();
        }

        public void HideConfirmation()
        {
            HideConfirmationImmediate();
            if (_failureActionsRoot != null)
            {
                ShowFailureActions(
                    _lastLuckCost,
                    _lastCurrentLuck,
                    _lastChallengeAvailable,
                    _lastLuckAvailable);
            }
        }

        public void HideFailureActions()
        {
            StopFailureActionRoutine();
            HideFailureActionsImmediate();
        }

        public void ShowLuckApplied(
            in PawnRollWindowData data)
        {
            StopRoutine();
            StopFailureActionRoutine();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RefreshSafeArea(true);
            HideConfirmationImmediate();
            HideFailureActionsImmediate();
            ApplyFinal(data);
            PlayOneShot(_finishClip, 0.9f);
        }

        public void SetWindowPosition(Vector2 position)
        {
            _dragHandle?.SetAnchoredPosition(position);
        }

        private IEnumerator PlayRoutine(
            PawnRollWindowData data,
            Action completed)
        {
            var minimum = Mathf.Min(data.MinimumValue, data.MaximumValue);
            var maximum = Mathf.Max(data.MinimumValue, data.MaximumValue);
            var finalValue = Mathf.Clamp(data.FinalValue, minimum, maximum);
            var duration = Mathf.Max(MinimumDuration, data.DurationSeconds);

            SetText(_titleText, data.Title);
            SetText(_expressionText, data.Expression);
            SetText(_resultText, string.Empty);
            SetText(_detailText, string.Empty);
            SetText(_counterText, minimum.ToString());
            _counterText.color = Color.white;
            _counterText.rectTransform.localScale = Vector3.one;

            var range = Mathf.Max(1, maximum - minimum);
            var finalRatio = (finalValue - minimum) / (float)range;
            var finalAngle = -360f * (PointerRotations + finalRatio);
            var elapsed = 0f;
            var nextTickAt = 0f;
            var currentValue = minimum - 1;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var eased = 1f - Mathf.Pow(1f - normalized, 3f);

                if (elapsed >= nextTickAt)
                {
                    currentValue++;
                    if (currentValue > maximum)
                        currentValue = minimum;

                    SetText(_counterText, currentValue.ToString());
                    _counterText.rectTransform.localScale =
                        Vector3.one * 1.08f;
                    PlayOneShot(_tickClip, 0.34f);
                    var tickBlend = normalized * normalized;
                    nextTickAt = elapsed + Mathf.Lerp(
                        FastTickSeconds,
                        SlowTickSeconds,
                        tickBlend);
                }

                _counterText.rectTransform.localScale = Vector3.Lerp(
                    _counterText.rectTransform.localScale,
                    Vector3.one,
                    Mathf.Clamp01(Time.unscaledDeltaTime * 20f));

                var remaining = 1f - normalized;
                var vibration =
                    Mathf.Sin(normalized * Mathf.PI * 48f) *
                    remaining * 3.5f;
                _pointer.localEulerAngles = new Vector3(
                    0f,
                    0f,
                    finalAngle * eased + vibration);
                yield return null;
            }

            _pointer.localEulerAngles = new Vector3(0f, 0f, finalAngle);
            ApplyFinal(data);
            PlayCompletionSound(data.ResultTone);
            _routine = null;
            PresentationCompleted?.Invoke();
            completed?.Invoke();
        }

        private void ApplyFinal(in PawnRollWindowData data)
        {
            SetText(_titleText, data.Title);
            SetText(_expressionText, data.Expression);
            SetText(_counterText, data.FinalValue.ToString());
            _counterText.color = data.ResultColor;
            _counterText.rectTransform.localScale = Vector3.one * 1.12f;
            SetText(_resultText, data.ResultLabel);
            _resultText.color = data.ResultColor;
            SetText(_detailText, data.DetailLabel);

            var minimum = Mathf.Min(data.MinimumValue, data.MaximumValue);
            var maximum = Mathf.Max(data.MinimumValue, data.MaximumValue);
            var range = Mathf.Max(1, maximum - minimum);
            var finalRatio =
                (Mathf.Clamp(data.FinalValue, minimum, maximum) - minimum) /
                (float)range;
            _pointer.localEulerAngles = new Vector3(
                0f,
                0f,
                -360f * finalRatio);
        }

        private void Build(RectTransform rootRect, Font requestedFont)
        {
            _rootRect = rootRect;
            _font = ResolveFont(requestedFont);

            _safeAreaRect = CreateRect(
                "SafeArea",
                _rootRect,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero);
            _safeAreaRect.offsetMin = Vector2.zero;
            _safeAreaRect.offsetMax = Vector2.zero;

            var panelObject = new GameObject(
                "ResultPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _panelRect = panelObject.GetComponent<RectTransform>();
            _panelRect.SetParent(_safeAreaRect, false);
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(460f, 420f);
            panelObject.GetComponent<Image>().color =
                new Color(0.018f, 0.04f, 0.055f, 0.995f);

            BuildHeader();
            BuildRoulette();
            BuildTexts();
            BuildFailureActions();
            BuildConfirmation();
            BuildAudio();
            _dragHandle = PawnUiDragHandle.Attach(
                _panelRect,
                _safeAreaRect,
                58f,
                8f);
            RefreshSafeArea(true);
        }

        private void BuildHeader()
        {
            var header = CreateImage(
                "Header",
                _panelRect,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0.5f, 1f),
                new Vector2(0f, -58f),
                new Color(0.04f, 0.12f, 0.15f, 1f));
            header.rectTransform.offsetMin = new Vector2(0f, -58f);
            header.rectTransform.offsetMax = Vector2.zero;

            _titleText = CreateText(
                "Title",
                header.transform,
                21,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            Stretch(_titleText.rectTransform, 18f, 64f, 8f, 8f);

            var close = CreateButton(
                "Close",
                header.transform,
                "×",
                new Color(0.30f, 0.08f, 0.07f, 1f));
            var rect = close.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-10f, -10f);
            rect.sizeDelta = new Vector2(40f, 36f);
            close.onClick.AddListener(() =>
            {
                Hide();
                Closed?.Invoke();
            });
        }

        private void BuildRoulette()
        {
            var rouletteRect = CreateRect(
                "Roulette",
                _panelRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(250f, 250f));
            rouletteRect.anchoredPosition = new Vector2(0f, 44f);
            var graphic =
                rouletteRect.gameObject.AddComponent<PawnCheckRouletteGraphic>();
            graphic.raycastTarget = false;

            var pointer = CreateImage(
                "Pointer",
                rouletteRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.05f),
                new Vector2(6f, 96f),
                new Color(1f, 0.84f, 0.28f, 1f));
            _pointer = pointer.rectTransform;
            _pointer.anchoredPosition = Vector2.zero;

            _counterText = CreateText(
                "Counter",
                rouletteRect,
                52,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            _counterText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _counterText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _counterText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _counterText.rectTransform.sizeDelta = new Vector2(150f, 80f);
            _counterText.rectTransform.anchoredPosition = Vector2.zero;
            var outline = _counterText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.9f, 0.95f, 1f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private void BuildTexts()
        {
            _expressionText = CreateText(
                "Expression",
                _panelRect,
                15,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            _expressionText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _expressionText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _expressionText.rectTransform.pivot = new Vector2(0.5f, 0f);
            _expressionText.rectTransform.offsetMin = new Vector2(18f, 100f);
            _expressionText.rectTransform.offsetMax = new Vector2(-18f, 126f);
            _expressionText.color = new Color(0.68f, 0.78f, 0.84f, 1f);

            _resultText = CreateText(
                "Result",
                _panelRect,
                24,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            _resultText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _resultText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _resultText.rectTransform.pivot = new Vector2(0.5f, 0f);
            _resultText.rectTransform.offsetMin = new Vector2(18f, 62f);
            _resultText.rectTransform.offsetMax = new Vector2(-18f, 98f);

            _detailText = CreateText(
                "Detail",
                _panelRect,
                14,
                FontStyle.Normal,
                TextAnchor.UpperCenter);
            _detailText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _detailText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _detailText.rectTransform.pivot = new Vector2(0.5f, 0f);
            _detailText.rectTransform.offsetMin = new Vector2(18f, 12f);
            _detailText.rectTransform.offsetMax = new Vector2(-18f, 58f);
            _detailText.color = new Color(0.78f, 0.82f, 0.86f, 1f);
        }

        private void BuildFailureActions()
        {
            _failureActionsRoot = new GameObject(
                "FailureActions",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(HorizontalLayoutGroup));
            _failureActionsRect =
                _failureActionsRoot.GetComponent<RectTransform>();
            _failureActionsRect.SetParent(_panelRect, false);
            _failureActionsRect.anchorMin = new Vector2(0f, 0f);
            _failureActionsRect.anchorMax = new Vector2(1f, 0f);
            _failureActionsRect.pivot = new Vector2(0.5f, 0f);
            _failureActionsRect.offsetMin = new Vector2(12f, 10f);
            _failureActionsRect.offsetMax = new Vector2(-12f, 82f);
            _failureActionsRoot.GetComponent<Image>().color =
                new Color(0.025f, 0.075f, 0.095f, 0.98f);

            var layout =
                _failureActionsRoot.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            _acceptButton = CreateButton(
                "Accept",
                _failureActionsRect,
                "결과에 승복한다",
                new Color(0.10f, 0.17f, 0.20f, 1f));
            _challengeButton = CreateButton(
                "Challenge",
                _failureActionsRect,
                "대항한다",
                new Color(0.34f, 0.19f, 0.03f, 1f));
            _luckButton = CreateButton(
                "Luck",
                _failureActionsRect,
                "운을 사용한다",
                new Color(0.06f, 0.30f, 0.17f, 1f));

            _challengeButtonText =
                _challengeButton.GetComponentInChildren<Text>(true);
            _luckButtonText =
                _luckButton.GetComponentInChildren<Text>(true);
            _acceptButton.onClick.AddListener(
                () => AcceptRequested?.Invoke());
            _challengeButton.onClick.AddListener(
                () => ChallengeRequested?.Invoke());
            _luckButton.onClick.AddListener(
                () => LuckRequested?.Invoke());
            _failureActionsRoot.SetActive(false);
        }

        private void BuildConfirmation()
        {
            _confirmationRoot = new GameObject(
                "ResultConfirmation",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _confirmationRect =
                _confirmationRoot.GetComponent<RectTransform>();
            _confirmationRect.SetParent(_panelRect, false);
            _confirmationRect.anchorMin = new Vector2(0f, 0f);
            _confirmationRect.anchorMax = new Vector2(1f, 0f);
            _confirmationRect.pivot = new Vector2(0.5f, 0f);
            _confirmationRect.offsetMin = new Vector2(12f, 10f);
            _confirmationRect.offsetMax = new Vector2(-12f, 116f);
            _confirmationRoot.GetComponent<Image>().color =
                new Color(0.12f, 0.055f, 0.035f, 0.995f);

            _confirmationTitleText = CreateText(
                "ConfirmationTitle",
                _confirmationRect,
                17,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetRect(
                _confirmationTitleText.rectTransform,
                new Vector2(0f, 0.66f),
                Vector2.one,
                new Vector2(12f, 0f),
                new Vector2(-12f, -4f));

            _confirmationBodyText = CreateText(
                "ConfirmationBody",
                _confirmationRect,
                13,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetRect(
                _confirmationBodyText.rectTransform,
                new Vector2(0f, 0.30f),
                new Vector2(1f, 0.66f),
                new Vector2(12f, 2f),
                new Vector2(-12f, -2f));

            var cancel = CreateButton(
                "CancelConfirmation",
                _confirmationRect,
                "취소",
                new Color(0.12f, 0.15f, 0.17f, 1f));
            SetRect(
                cancel.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0.5f, 0.30f),
                new Vector2(10f, 7f),
                new Vector2(-4f, -4f));
            cancel.onClick.AddListener(HideConfirmation);

            var accept = CreateButton(
                "AcceptConfirmation",
                _confirmationRect,
                "확인",
                new Color(0.43f, 0.18f, 0.045f, 1f));
            SetRect(
                accept.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(1f, 0.30f),
                new Vector2(4f, 7f),
                new Vector2(-10f, -4f));
            _confirmationAcceptText =
                accept.GetComponentInChildren<Text>(true);
            accept.onClick.AddListener(HandleConfirmationAccepted);
            _confirmationRoot.SetActive(false);
        }

        private void HandleConfirmationAccepted()
        {
            var kind = _confirmationKind;
            HideConfirmationImmediate();
            ConfirmationAccepted?.Invoke(kind);
        }

        private void HideConfirmationImmediate()
        {
            _confirmationKind = PawnCheckConfirmationKind.None;
            if (_confirmationRoot != null)
                _confirmationRoot.SetActive(false);
        }

        private IEnumerator AnimateFailureActions()
        {
            if (_failureActionsRect == null)
                yield break;

            _failureActionsRect.localScale = Vector3.one * 0.82f;
            var elapsed = 0f;
            const float duration = 0.28f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var overshoot = t < 0.72f
                    ? Mathf.Lerp(0.82f, 1.08f, t / 0.72f)
                    : Mathf.Lerp(1.08f, 1f, (t - 0.72f) / 0.28f);
                _failureActionsRect.localScale =
                    Vector3.one * overshoot;
                yield return null;
            }

            _failureActionsRect.localScale = Vector3.one;
            _failureActionRoutine = null;
        }

        private void HideFailureActionsImmediate()
        {
            HideConfirmationImmediate();
            if (_failureActionsRoot != null)
                _failureActionsRoot.SetActive(false);
            if (_failureActionsRect != null)
                _failureActionsRect.localScale = Vector3.one;
            ApplyFailureActionLayout(false);
        }

        private void ApplyFailureActionLayout(bool visible)
        {
            if (_panelRect == null)
                return;

            _panelRect.sizeDelta = new Vector2(
                _panelRect.sizeDelta.x,
                visible ? 520f : 420f);
            if (_expressionText != null)
            {
                _expressionText.rectTransform.offsetMin =
                    new Vector2(18f, visible ? 182f : 100f);
                _expressionText.rectTransform.offsetMax =
                    new Vector2(-18f, visible ? 208f : 126f);
            }
            if (_resultText != null)
            {
                _resultText.rectTransform.offsetMin =
                    new Vector2(18f, visible ? 144f : 62f);
                _resultText.rectTransform.offsetMax =
                    new Vector2(-18f, visible ? 180f : 98f);
            }
            if (_detailText != null)
            {
                _detailText.rectTransform.offsetMin =
                    new Vector2(18f, visible ? 94f : 12f);
                _detailText.rectTransform.offsetMax =
                    new Vector2(-18f, visible ? 140f : 58f);
            }

            var roulette = _panelRect.Find("Roulette") as RectTransform;
            if (roulette != null)
                roulette.anchoredPosition =
                    new Vector2(0f, visible ? 86f : 44f);

            Canvas.ForceUpdateCanvases();
            _dragHandle?.ClampToBounds();
        }

        private void StopFailureActionRoutine()
        {
            if (_failureActionRoutine == null)
                return;

            StopCoroutine(_failureActionRoutine);
            _failureActionRoutine = null;
        }

        private void BuildAudio()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
            _tickClip = CreateToneClip(
                "CheckRollTick", 880f, 0.022f, 0.11f);
            _finishClip = CreateToneClip(
                "CheckRollFinish", 1320f, 0.09f, 0.20f);
            _criticalClip = CreateSequenceClip(
                "CheckRollCritical",
                new[] { 784f, 988f, 1319f, 1568f },
                0.095f,
                0.30f);
            _fumbleClip = CreateSequenceClip(
                "CheckRollFumble",
                new[] { 392f, 311f, 233f, 165f },
                0.13f,
                0.34f);
            _decisionClip = CreateSequenceClip(
                "CheckRollDecision",
                new[] { 740f, 988f },
                0.08f,
                0.22f);
        }

        private void Update()
        {
            RefreshSafeArea(false);
        }

        private void RefreshSafeArea(bool force)
        {
            if (_safeAreaRect == null || _panelRect == null)
                return;

            var safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (!force && safeArea == _lastSafeArea &&
                screenSize == _lastScreenSize)
            {
                return;
            }

            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;
            var width = Mathf.Max(1f, Screen.width);
            var height = Mathf.Max(1f, Screen.height);
            _safeAreaRect.anchorMin = new Vector2(
                safeArea.xMin / width,
                safeArea.yMin / height);
            _safeAreaRect.anchorMax = new Vector2(
                safeArea.xMax / width,
                safeArea.yMax / height);
            _safeAreaRect.offsetMin = Vector2.zero;
            _safeAreaRect.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            _dragHandle?.ClampToBounds();
        }

        private void StopRoutine()
        {
            if (_routine == null)
                return;

            StopCoroutine(_routine);
            _routine = null;
        }

        private void PlayOneShot(AudioClip clip, float volume)
        {
            if (_audioSource != null && clip != null)
                _audioSource.PlayOneShot(clip, volume);
        }

        private void PlayCompletionSound(PawnRollResultTone tone)
        {
            switch (tone)
            {
                case PawnRollResultTone.Critical:
                    PlayOneShot(_criticalClip, 1f);
                    break;
                case PawnRollResultTone.Fumble:
                    PlayOneShot(_fumbleClip, 1f);
                    break;
                default:
                    PlayOneShot(_finishClip, 1f);
                    break;
            }
        }

        private static AudioClip CreateSequenceClip(
            string name,
            float[] frequencies,
            float noteDuration,
            float amplitude)
        {
            const int sampleRate = 44100;
            if (frequencies == null || frequencies.Length == 0)
                return null;

            var gapDuration = 0.018f;
            var totalDuration =
                frequencies.Length * (noteDuration + gapDuration);
            var sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(sampleRate * totalDuration));
            var samples = new float[sampleCount];
            var noteSamples = Mathf.Max(
                1,
                Mathf.CeilToInt(sampleRate * noteDuration));
            var stepSamples = Mathf.Max(
                noteSamples + 1,
                Mathf.CeilToInt(
                    sampleRate * (noteDuration + gapDuration)));

            for (var note = 0; note < frequencies.Length; note++)
            {
                var start = note * stepSamples;
                var frequency = frequencies[note];
                for (var index = 0;
                     index < noteSamples && start + index < sampleCount;
                     index++)
                {
                    var t = index / (float)sampleRate;
                    var normalized = index / (float)noteSamples;
                    var envelope = Mathf.Sin(
                        Mathf.PI * Mathf.Clamp01(normalized));
                    samples[start + index] +=
                        Mathf.Sin(Mathf.PI * 2f * frequency * t) *
                        amplitude * envelope;
                }
            }

            var clip = AudioClip.Create(
                name,
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateToneClip(
            string name,
            float frequency,
            float duration,
            float amplitude)
        {
            const int sampleRate = 44100;
            var sampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * duration));
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var t = i / (float)sampleRate;
                var envelope = 1f - i / (float)sampleCount;
                samples[i] = Mathf.Sin(Mathf.PI * 2f * frequency * t) *
                             amplitude * envelope;
            }

            var clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            FontStyle style,
            TextAnchor alignment)
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
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Color color)
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
            image.color = color;
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelObject.GetComponent<Text>();
            text.font = ResolveFont(null);
            text.text = label;
            text.fontSize = 20;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return button;
        }

        private static Image CreateImage(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 size,
            Color color)
        {
            var rect = CreateRect(name, parent, anchorMin, anchorMax, pivot, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            return rect;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Stretch(
            RectTransform rect,
            float left,
            float right,
            float bottom,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
                target.text = value ?? string.Empty;
        }

        private static Font ResolveFont(Font requested)
        {
            if (requested != null)
                return requested;

            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                    return font;
            }
            catch (ArgumentException)
            {
            }

            return Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Arial" },
                20);
        }

        private void OnDestroy()
        {
            StopRoutine();
            StopFailureActionRoutine();
            if (_tickClip != null)
                Destroy(_tickClip);
            if (_finishClip != null)
                Destroy(_finishClip);
            if (_criticalClip != null)
                Destroy(_criticalClip);
            if (_fumbleClip != null)
                Destroy(_fumbleClip);
            if (_decisionClip != null)
                Destroy(_decisionClip);
            Closed = null;
            PresentationCompleted = null;
            AcceptRequested = null;
            ChallengeRequested = null;
            LuckRequested = null;
            ConfirmationAccepted = null;
        }
    }

    internal sealed class PawnCheckRouletteGraphic : MaskableGraphic
    {
        private const int SegmentCount = 48;
        private const float InnerRadiusRatio = 0.30f;
        private const float OuterRadiusRatio = 0.49f;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(rect.width, rect.height);
            var inner = radius * InnerRadiusRatio;
            var outer = radius * OuterRadiusRatio;
            var center = rect.center;

            for (var i = 0; i < SegmentCount; i++)
            {
                var start = Mathf.PI * 2f * i / SegmentCount;
                var end = Mathf.PI * 2f * (i + 1) / SegmentCount;
                var color = i % 2 == 0
                    ? new Color32(42, 174, 196, 255)
                    : new Color32(209, 156, 45, 255);
                AddRingSegment(vertexHelper, center, inner, outer, start, end, color);
            }
        }

        private static void AddRingSegment(
            VertexHelper helper,
            Vector2 center,
            float inner,
            float outer,
            float start,
            float end,
            Color32 color)
        {
            var startDirection = new Vector2(Mathf.Sin(start), Mathf.Cos(start));
            var endDirection = new Vector2(Mathf.Sin(end), Mathf.Cos(end));
            var index = helper.currentVertCount;
            AddVertex(helper, center + startDirection * inner, color);
            AddVertex(helper, center + startDirection * outer, color);
            AddVertex(helper, center + endDirection * outer, color);
            AddVertex(helper, center + endDirection * inner, color);
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index, index + 2, index + 3);
        }

        private static void AddVertex(
            VertexHelper helper,
            Vector2 position,
            Color32 color)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            helper.AddVert(vertex);
        }
    }
}
