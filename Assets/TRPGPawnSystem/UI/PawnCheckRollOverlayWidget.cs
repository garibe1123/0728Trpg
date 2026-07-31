using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// Root Canvas 중앙에 고정되는 판정 패널입니다.
    /// 포인터 위치를 배치 계산에 사용하지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnCheckRollOverlayWidget : MonoBehaviour,
        IDropHandler
    {
        private const float MaximumWidth = 590f;
        private const float MaximumHeight = 650f;
        private const float MinimumWidth = 360f;
        private const float MinimumHeight = 420f;
        private const float RollDuration = 1.25f;

        private RectTransform _rootRect;
        private RectTransform _safeAreaRect;
        private RectTransform _panelRect;
        private RectTransform _contentRect;
        private ScrollRect _scrollRect;
        private Font _font;

        private Text _statusText;
        private Text _sourceText;
        private GameObject _sourceSection;
        private GameObject _difficultySection;
        private Button _regularButton;
        private Button _hardButton;
        private Button _extremeButton;
        private Text _regularButtonText;
        private Text _hardButtonText;
        private Text _extremeButtonText;
        private GameObject _resultSection;
        private Text _counterText;
        private Text _resultText;
        private Text _detailText;
        private GameObject _failureSection;
        private Button _acceptButton;
        private Button _challengeButton;
        private Button _luckButton;
        private Text _challengeButtonText;
        private Text _luckButtonText;
        private GameObject _confirmationSection;
        private Text _confirmationTitleText;
        private Text _confirmationBodyText;
        private Text _confirmationAcceptText;
        private PawnCheckConfirmationKind _confirmationKind;
        private Coroutine _rollRoutine;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        public event Action PureRollRequested;
        public event Action<PawnCheckSourceData> SourceDropped;
        public event Action<PawnCheckDifficulty>
            DifficultyRequested;
        public event Action ClearSourceRequested;
        public event Action AcceptRequested;
        public event Action ChallengeRequested;
        public event Action LuckRequested;
        public event Action<PawnCheckConfirmationKind>
            ConfirmationAccepted;
        public event Action CloseRequested;

        public static PawnCheckRollOverlayWidget CreateRuntime(
            Canvas rootCanvas,
            Font font)
        {
            if (rootCanvas == null)
                throw new ArgumentNullException(nameof(rootCanvas));

            var root = new GameObject(
                "PawnCheckRollOverlay",
                typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(rootCanvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var widget = root.AddComponent<
                PawnCheckRollOverlayWidget>();
            widget.Build(rect, font);
            root.SetActive(false);
            return widget;
        }

        public void OpenWaiting()
        {
            StopRollRoutine();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _confirmationKind = PawnCheckConfirmationKind.None;
            SetText(_statusText,
                "D100을 바로 굴리거나 스탯·스킬을 선택하세요.");
            _sourceSection.SetActive(false);
            _difficultySection.SetActive(false);
            _resultSection.SetActive(false);
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            RefreshSafeArea(true);
            ForceCenter();
            RebuildContent();
        }

        public void BindSource(
            in PawnCheckSourceData source)
        {
            _sourceSection.SetActive(true);
            _difficultySection.SetActive(true);
            _resultSection.SetActive(false);
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetText(_statusText,
                "난이도를 선택하면 해당 수치로 판정합니다.");
            SetText(
                _sourceText,
                $"{source.DisplayName}\n" +
                $"일반 {source.Regular} · 어려움 {source.Hard} · " +
                $"극단적 {source.Extreme}");
            SetText(_regularButtonText, $"일반\n≤ {source.Regular}");
            SetText(_hardButtonText, $"어려움\n≤ {source.Hard}");
            SetText(_extremeButtonText, $"극단적\n≤ {source.Extreme}");
            SetDifficultyInteractable(true);
            RebuildContent();
        }

        public void ClearSource()
        {
            _sourceSection.SetActive(false);
            _difficultySection.SetActive(false);
            _resultSection.SetActive(false);
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetText(_statusText,
                "D100을 바로 굴리거나 스탯·스킬을 선택하세요.");
            RebuildContent();
        }

        public void PlayRawRoll(int roll, Action completed)
        {
            PlayRoll(
                roll,
                "순수 D100",
                "목표값 없음",
                $"D100 결과 {roll}",
                "스탯·스킬을 선택하지 않은 일반 굴림입니다.",
                Color.white,
                completed);
        }

        public void PlayCheckResult(
            in PawnCheckEvaluation evaluation,
            Action completed)
        {
            var difficulty =
                PawnCheckRollRules.GetDifficultyLabel(
                    evaluation.Difficulty);
            var resultColor = evaluation.IsSuccessForDifficulty
                ? new Color(0.25f, 0.90f, 1f)
                : evaluation.Grade == PawnCheckOutcomeGrade.Fumble
                    ? new Color(0.15f, 0.15f, 0.15f)
                    : new Color(1f, 0.34f, 0.25f);
            var gradeLabel = PawnCheckRollRules.GetGradeLabel(
                evaluation.Grade);
            PlayRoll(
                evaluation.Roll,
                $"{evaluation.Source.DisplayName} · {difficulty}",
                $"D100 / 목표 {evaluation.RequiredTarget}",
                evaluation.IsSuccessForDifficulty
                    ? $"{difficulty} 판정 성공"
                    : $"{difficulty} 판정 실패",
                $"굴림 {evaluation.Roll} / 목표 " +
                $"{evaluation.RequiredTarget}\n판정 등급: {gradeLabel}",
                resultColor,
                completed);
        }

        public void ShowFailureActions(
            in PawnCheckEvaluation evaluation,
            int currentLuck,
            bool challengeAvailable,
            bool luckAvailable)
        {
            // 실패 후 선택지는 룰렛 결과창이 전담한다.
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetText(
                _statusText,
                "실패했습니다. 룰렛 결과창 하단에서 후속 행동을 선택하세요.");
            RebuildContent();
        }

        public void ShowFinalOnly(string status)
        {
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetText(_statusText, status);
            RebuildContent();
        }

        public void ShowStatusOnly(string status)
        {
            StopRollRoutine();
            _resultSection.SetActive(false);
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetDifficultyInteractable(_sourceSection.activeSelf);
            SetText(_statusText, status);
            RebuildContent();
        }

        public void ShowLuckApplied(
            in PawnCheckEvaluation evaluation,
            int luckSpent,
            int remainingLuck)
        {
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetText(
                _statusText,
                $"운 {luckSpent} 사용 · 남은 운 {remainingLuck}. 결과는 룰렛 창에 저장되었습니다.");
            RebuildContent();
        }

        public void ShowConfirmation(
            PawnCheckConfirmationKind kind,
            string title,
            string body,
            string acceptLabel)
        {
            // 하위 호환용 API. 실제 확인 UI는 룰렛 결과창에서 표시한다.
            _confirmationKind = PawnCheckConfirmationKind.None;
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetText(_statusText, "확인은 룰렛 결과창에서 진행하세요.");
            RebuildContent();
        }

        public void HideConfirmation()
        {
            _confirmationKind = PawnCheckConfirmationKind.None;
            _confirmationSection.SetActive(false);
            RebuildContent();
        }

        public void Hide()
        {
            StopRollRoutine();
            _confirmationKind = PawnCheckConfirmationKind.None;
            gameObject.SetActive(false);
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (!gameObject.activeInHierarchy ||
                eventData.pointerDrag == null)
            {
                return;
            }

            var sourceWidget = eventData.pointerDrag.GetComponentInParent<
                PawnRollSourceWidget>();
            if (sourceWidget != null &&
                sourceWidget.TryGetData(out var source))
            {
                SourceDropped?.Invoke(source);
            }
        }

        private void PlayRoll(
            int roll,
            string title,
            string expression,
            string result,
            string detail,
            Color resultColor,
            Action completed)
        {
            StopRollRoutine();
            _resultSection.SetActive(true);
            _failureSection.SetActive(false);
            _confirmationSection.SetActive(false);
            SetDifficultyInteractable(false);
            SetText(_statusText, "D100 판정 중...");
            SetText(_counterText, "—");
            SetText(_resultText, string.Empty);
            SetText(_detailText, string.Empty);
            RebuildContent();
            _rollRoutine = StartCoroutine(
                PlayRollRoutine(
                    roll,
                    title,
                    expression,
                    result,
                    detail,
                    resultColor,
                    completed));
        }

        private IEnumerator PlayRollRoutine(
            int roll,
            string title,
            string expression,
            string result,
            string detail,
            Color resultColor,
            Action completed)
        {
            var elapsed = 0f;
            var displayed = 1;
            while (elapsed < RollDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                displayed += 7;
                if (displayed > 100)
                    displayed -= 100;
                SetText(_counterText, displayed.ToString());
                _counterText.color = Color.white;
                yield return null;
            }

            SetText(_counterText, roll.ToString());
            _counterText.color = resultColor;
            SetText(_resultText, result);
            _resultText.color = resultColor;
            SetText(_detailText, $"{title}\n{expression}\n{detail}");
            SetText(_statusText, "판정 결과");
            _rollRoutine = null;
            completed?.Invoke();
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
                "CenteredPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _panelRect = panelObject.GetComponent<RectTransform>();
            _panelRect.SetParent(_safeAreaRect, false);
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(
                MaximumWidth,
                MaximumHeight);
            panelObject.GetComponent<Image>().color =
                new Color(0.018f, 0.055f, 0.067f, 0.985f);

            BuildHeader();
            BuildScrollContent();
            RefreshSafeArea(true);
        }

        private void BuildHeader()
        {
            var title = CreateText(
                "Title",
                _panelRect,
                22,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            title.text = "판정 굴림";
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(18f, -48f);
            titleRect.offsetMax = new Vector2(-64f, -8f);

            var close = CreateButton(
                "Close",
                _panelRect,
                "×",
                new Color(0.30f, 0.08f, 0.07f, 1f),
                out _);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = Vector2.one;
            closeRect.anchorMax = Vector2.one;
            closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-10f, -10f);
            closeRect.sizeDelta = new Vector2(40f, 34f);
            close.onClick.AddListener(
                () => CloseRequested?.Invoke());
        }

        private void BuildScrollContent()
        {
            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D));
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(_panelRect, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12f, 12f);
            viewport.offsetMax = new Vector2(-12f, -58f);
            viewportObject.GetComponent<Image>().color =
                new Color(1f, 1f, 1f, 0.01f);

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            _contentRect = contentObject.GetComponent<RectTransform>();
            _contentRect.SetParent(viewport, false);
            _contentRect.anchorMin = new Vector2(0f, 1f);
            _contentRect.anchorMax = new Vector2(1f, 1f);
            _contentRect.pivot = new Vector2(0.5f, 1f);
            _contentRect.anchoredPosition = Vector2.zero;
            _contentRect.sizeDelta = Vector2.zero;

            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scrollRect = _panelRect.gameObject.AddComponent<ScrollRect>();
            _scrollRect.viewport = viewport;
            _scrollRect.content = _contentRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 22f;

            _statusText = CreateSectionText(
                "Status",
                48f,
                15,
                TextAnchor.MiddleLeft,
                new Color(0.05f, 0.12f, 0.14f, 1f));

            var dropButton = CreateSectionButton(
                "PureD100",
                58f,
                "D100 바로 굴리기\n목표값 없이 숫자만 확인",
                new Color(0.06f, 0.25f, 0.31f, 1f));
            dropButton.onClick.AddListener(
                () => PureRollRequested?.Invoke());

            var dropZone = CreateSection(
                "DropZone",
                62f,
                new Color(0.035f, 0.12f, 0.15f, 1f));
            var dropOutline = dropZone.gameObject.AddComponent<Outline>();
            dropOutline.effectColor =
                new Color(0.15f, 0.78f, 0.92f, 0.9f);
            dropOutline.effectDistance = new Vector2(1f, -1f);
            var dropText = CreateText(
                "Label",
                dropZone,
                15,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            dropText.text = "스탯·스킬을 여기에 드롭하거나 행을 클릭";
            Stretch(dropText.rectTransform, 8f);

            var sourceRect = CreateSection(
                "SourceSection",
                78f,
                new Color(0.05f, 0.15f, 0.18f, 1f));
            _sourceSection = sourceRect.gameObject;
            _sourceText = CreateText(
                "Source",
                sourceRect,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            var sourceTextRect = _sourceText.rectTransform;
            sourceTextRect.anchorMin = Vector2.zero;
            sourceTextRect.anchorMax = Vector2.one;
            sourceTextRect.offsetMin = new Vector2(12f, 6f);
            sourceTextRect.offsetMax = new Vector2(-120f, -6f);
            var clearButton = CreateButton(
                "ClearSource",
                sourceRect,
                "선택 해제",
                new Color(0.18f, 0.10f, 0.08f, 1f),
                out _);
            var clearRect = clearButton.GetComponent<RectTransform>();
            clearRect.anchorMin = new Vector2(1f, 0.5f);
            clearRect.anchorMax = new Vector2(1f, 0.5f);
            clearRect.pivot = new Vector2(1f, 0.5f);
            clearRect.anchoredPosition = new Vector2(-8f, 0f);
            clearRect.sizeDelta = new Vector2(102f, 42f);
            clearButton.onClick.AddListener(
                () => ClearSourceRequested?.Invoke());

            var difficultyRect = CreateSection(
                "Difficulty",
                78f,
                new Color(0.03f, 0.10f, 0.12f, 1f));
            _difficultySection = difficultyRect.gameObject;
            var difficultyLayout = difficultyRect.gameObject.AddComponent<
                HorizontalLayoutGroup>();
            difficultyLayout.padding = new RectOffset(8, 8, 8, 8);
            difficultyLayout.spacing = 8f;
            difficultyLayout.childControlWidth = true;
            difficultyLayout.childControlHeight = true;
            difficultyLayout.childForceExpandWidth = true;
            difficultyLayout.childForceExpandHeight = true;
            _regularButton = CreateButton(
                "Regular",
                difficultyRect,
                "일반",
                new Color(0.06f, 0.28f, 0.34f, 1f),
                out _regularButtonText);
            _hardButton = CreateButton(
                "Hard",
                difficultyRect,
                "어려움",
                new Color(0.29f, 0.20f, 0.05f, 1f),
                out _hardButtonText);
            _extremeButton = CreateButton(
                "Extreme",
                difficultyRect,
                "극단적",
                new Color(0.32f, 0.08f, 0.08f, 1f),
                out _extremeButtonText);
            _regularButton.onClick.AddListener(
                () => DifficultyRequested?.Invoke(
                    PawnCheckDifficulty.Regular));
            _hardButton.onClick.AddListener(
                () => DifficultyRequested?.Invoke(
                    PawnCheckDifficulty.Hard));
            _extremeButton.onClick.AddListener(
                () => DifficultyRequested?.Invoke(
                    PawnCheckDifficulty.Extreme));

            var resultRect = CreateSection(
                "Result",
                150f,
                new Color(0.025f, 0.075f, 0.09f, 1f));
            _resultSection = resultRect.gameObject;
            _counterText = CreateText(
                "Counter",
                resultRect,
                42,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            var counterRect = _counterText.rectTransform;
            counterRect.anchorMin = new Vector2(0f, 0f);
            counterRect.anchorMax = new Vector2(0.30f, 1f);
            counterRect.offsetMin = new Vector2(8f, 8f);
            counterRect.offsetMax = new Vector2(-4f, -8f);
            _resultText = CreateText(
                "ResultLabel",
                resultRect,
                20,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            var resultTextRect = _resultText.rectTransform;
            resultTextRect.anchorMin = new Vector2(0.30f, 0.55f);
            resultTextRect.anchorMax = Vector2.one;
            resultTextRect.offsetMin = new Vector2(8f, 0f);
            resultTextRect.offsetMax = new Vector2(-10f, -10f);
            _detailText = CreateText(
                "Detail",
                resultRect,
                13,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            var detailRect = _detailText.rectTransform;
            detailRect.anchorMin = new Vector2(0.30f, 0f);
            detailRect.anchorMax = new Vector2(1f, 0.58f);
            detailRect.offsetMin = new Vector2(8f, 8f);
            detailRect.offsetMax = new Vector2(-10f, -4f);

            var failureRect = CreateSection(
                "FailureActions",
                112f,
                new Color(0.04f, 0.09f, 0.11f, 1f));
            _failureSection = failureRect.gameObject;
            var failureLayout = failureRect.gameObject.AddComponent<
                HorizontalLayoutGroup>();
            failureLayout.padding = new RectOffset(8, 8, 8, 8);
            failureLayout.spacing = 8f;
            failureLayout.childControlWidth = true;
            failureLayout.childControlHeight = true;
            failureLayout.childForceExpandWidth = true;
            failureLayout.childForceExpandHeight = true;
            _acceptButton = CreateButton(
                "Accept",
                failureRect,
                "결과에 승복한다",
                new Color(0.10f, 0.16f, 0.18f, 1f),
                out _);
            _challengeButton = CreateButton(
                "Challenge",
                failureRect,
                "대항한다",
                new Color(0.28f, 0.17f, 0.05f, 1f),
                out _challengeButtonText);
            _luckButton = CreateButton(
                "Luck",
                failureRect,
                "운을 사용한다",
                new Color(0.08f, 0.24f, 0.15f, 1f),
                out _luckButtonText);
            _acceptButton.onClick.AddListener(
                () => AcceptRequested?.Invoke());
            _challengeButton.onClick.AddListener(
                () => ChallengeRequested?.Invoke());
            _luckButton.onClick.AddListener(
                () => LuckRequested?.Invoke());

            var confirmationRect = CreateSection(
                "Confirmation",
                166f,
                new Color(0.11f, 0.055f, 0.04f, 1f));
            _confirmationSection = confirmationRect.gameObject;
            _confirmationTitleText = CreateText(
                "ConfirmationTitle",
                confirmationRect,
                17,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            var confirmationTitleRect =
                _confirmationTitleText.rectTransform;
            confirmationTitleRect.anchorMin = new Vector2(0f, 0.72f);
            confirmationTitleRect.anchorMax = Vector2.one;
            confirmationTitleRect.offsetMin = new Vector2(12f, 0f);
            confirmationTitleRect.offsetMax = new Vector2(-12f, -8f);
            _confirmationBodyText = CreateText(
                "ConfirmationBody",
                confirmationRect,
                13,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            var confirmationBodyRect =
                _confirmationBodyText.rectTransform;
            confirmationBodyRect.anchorMin = new Vector2(0f, 0.28f);
            confirmationBodyRect.anchorMax = new Vector2(1f, 0.74f);
            confirmationBodyRect.offsetMin = new Vector2(12f, 4f);
            confirmationBodyRect.offsetMax = new Vector2(-12f, -4f);
            var cancelConfirmation = CreateButton(
                "CancelConfirmation",
                confirmationRect,
                "취소",
                new Color(0.13f, 0.13f, 0.13f, 1f),
                out _);
            var cancelRect = cancelConfirmation.GetComponent<RectTransform>();
            cancelRect.anchorMin = new Vector2(0f, 0f);
            cancelRect.anchorMax = new Vector2(0.48f, 0.25f);
            cancelRect.offsetMin = new Vector2(8f, 8f);
            cancelRect.offsetMax = new Vector2(-4f, -2f);
            var confirmButton = CreateButton(
                "AcceptConfirmation",
                confirmationRect,
                "확인",
                new Color(0.35f, 0.14f, 0.05f, 1f),
                out _confirmationAcceptText);
            var confirmRect = confirmButton.GetComponent<RectTransform>();
            confirmRect.anchorMin = new Vector2(0.52f, 0f);
            confirmRect.anchorMax = new Vector2(1f, 0.25f);
            confirmRect.offsetMin = new Vector2(4f, 8f);
            confirmRect.offsetMax = new Vector2(-8f, -2f);
            cancelConfirmation.onClick.AddListener(HideConfirmation);
            confirmButton.onClick.AddListener(
                HandleConfirmationAccepted);
        }

        private Text CreateSectionText(
            string name,
            float height,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            var section = CreateSection(name, height, color);
            var text = CreateText(
                "Text",
                section,
                fontSize,
                FontStyle.Normal,
                alignment);
            Stretch(text.rectTransform, 12f);
            return text;
        }

        private Button CreateSectionButton(
            string name,
            float height,
            string label,
            Color color)
        {
            var section = CreateSection(name, height, color);
            var button = section.gameObject.AddComponent<Button>();
            button.targetGraphic = section.GetComponent<Image>();
            var text = CreateText(
                "Label",
                section,
                15,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            text.text = label;
            Stretch(text.rectTransform, 8f);
            return button;
        }

        private RectTransform CreateSection(
            string name,
            float height,
            Color color)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(_contentRect, false);
            root.GetComponent<Image>().color = color;
            var element = root.GetComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return rect;
        }

        private Button CreateButton(
            string name,
            Transform parent,
            string label,
            Color color,
            out Text labelText)
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
            labelText = CreateText(
                "Label",
                rect,
                14,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            labelText.text = label;
            Stretch(labelText.rectTransform, 5f);
            return button;
        }

        private Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            FontStyle fontStyle,
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
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = Color.white;
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
            Vector2 sizeDelta)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        private void HandleConfirmationAccepted()
        {
            var kind = _confirmationKind;
            _confirmationKind = PawnCheckConfirmationKind.None;
            _confirmationSection.SetActive(false);
            ConfirmationAccepted?.Invoke(kind);
            RebuildContent();
        }

        private void SetDifficultyInteractable(bool value)
        {
            _regularButton.interactable = value;
            _hardButton.interactable = value;
            _extremeButton.interactable = value;
        }

        private void Update()
        {
            RefreshSafeArea(false);
        }

        private void OnRectTransformDimensionsChange()
        {
            RefreshSafeArea(true);
            ForceCenter();
        }

        private void RefreshSafeArea(bool force)
        {
            if (_safeAreaRect == null || _panelRect == null)
                return;

            var safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (!force &&
                safeArea == _lastSafeArea &&
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
            var available = _safeAreaRect.rect.size;
            var panelWidth = Mathf.Clamp(
                available.x - 24f,
                MinimumWidth,
                MaximumWidth);
            var panelHeight = Mathf.Clamp(
                available.y - 24f,
                MinimumHeight,
                MaximumHeight);
            panelWidth = Mathf.Min(panelWidth, available.x);
            panelHeight = Mathf.Min(panelHeight, available.y);
            _panelRect.sizeDelta = new Vector2(
                Mathf.Max(1f, panelWidth),
                Mathf.Max(1f, panelHeight));
            ForceCenter();
            RebuildContent();
        }

        private void ForceCenter()
        {
            if (_panelRect == null)
                return;

            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.localPosition = new Vector3(
                _panelRect.localPosition.x,
                _panelRect.localPosition.y,
                0f);
        }

        private void RebuildContent()
        {
            if (_contentRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 1f;
        }

        private void StopRollRoutine()
        {
            if (_rollRoutine == null)
                return;

            StopCoroutine(_rollRoutine);
            _rollRoutine = null;
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
                target.text = value ?? string.Empty;
        }

        private static Font ResolveFont(Font requestedFont)
        {
            if (requestedFont != null)
                return requestedFont;

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
    }
}
