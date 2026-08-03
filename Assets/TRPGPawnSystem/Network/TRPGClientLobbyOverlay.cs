using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// Player Client용 접속 대기실 UI입니다.
    ///
    /// - 연결 전: 90% 검정 오버레이와 서버 검색 상태
    /// - 연결 종료: 오버레이 복귀 및 재검색 상태
    /// - 연결 완료: 오버레이 페이드아웃
    /// - 미점유 Player: 캐릭터 선택 패널
    /// - 점유 완료 Player: 내 캐릭터 표시
    /// - GM Host: 모든 Player 전용 UI 숨김
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TRPGClientLobbyOverlay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private TRPGNetworkBootstrap _bootstrap;
        [SerializeField]
        private Font _uiFont;

        [Header("Client UI")]
        [SerializeField]
        private bool _showInEditor;
        [SerializeField, Range(0f, 1f)]
        private float _overlayOpacity = 0.9f;
        [SerializeField, Min(0.05f)]
        private float _fadeDuration = 0.8f;
        [SerializeField]
        private int _hudSortingOrder = 1400;
        [SerializeField]
        private int _blockingSortingOrder = 30000;

        [Header("Refresh")]
        [SerializeField, Min(0.05f)]
        private float _refreshInterval = 0.2f;

        private Canvas _hudCanvas;
        private Canvas _blockingCanvas;
        private GameObject _blockingOverlay;
        private CanvasGroup _blockingCanvasGroup;
        private Text _titleText;
        private Text _statusText;

        private GameObject _selectionPanel;
        private RectTransform _selectionContent;
        private Text _selectionMessage;

        private GameObject _characterBadge;
        private Text _characterBadgeText;

        private TRPGSessionAuthority _authority;
        private Coroutine _fadeRoutine;
        private float _nextRefreshTime;
        private int _lastRenderedRevision = int.MinValue;

        private void Awake()
        {
            if (_bootstrap == null)
            {
                _bootstrap =
                    GetComponent<TRPGNetworkBootstrap>();
            }

            if (_bootstrap == null)
            {
                _bootstrap =
                    TRPGNetworkBootstrap.Instance;
            }

            CreateRuntimeUi();
        }

        private void OnEnable()
        {
            BindBootstrap();
            RefreshImmediate();
        }

        private void OnDisable()
        {
            UnbindBootstrap();
            BindAuthority(null);
        }

        private void OnDestroy()
        {
            UnbindBootstrap();
            BindAuthority(null);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
                return;

            _nextRefreshTime =
                Time.unscaledTime + _refreshInterval;

            ResolveReferences();
            RefreshVisualState();
        }

        private void ResolveReferences()
        {
            if (_bootstrap == null)
            {
                _bootstrap =
                    TRPGNetworkBootstrap.Instance;

                BindBootstrap();
            }

            var nextAuthority =
                _bootstrap != null
                    ? _bootstrap.Authority
                    : null;

            if (_authority != nextAuthority)
                BindAuthority(nextAuthority);
        }

        private void BindBootstrap()
        {
            if (_bootstrap == null)
                return;

            _bootstrap.StatusChanged -=
                HandleBootstrapStatusChanged;
            _bootstrap.StatusChanged +=
                HandleBootstrapStatusChanged;

            _bootstrap.AuthorityChanged -=
                HandleAuthorityChanged;
            _bootstrap.AuthorityChanged +=
                HandleAuthorityChanged;

            BindAuthority(_bootstrap.Authority);
        }

        private void UnbindBootstrap()
        {
            if (_bootstrap == null)
                return;

            _bootstrap.StatusChanged -=
                HandleBootstrapStatusChanged;

            _bootstrap.AuthorityChanged -=
                HandleAuthorityChanged;
        }

        private void BindAuthority(
            TRPGSessionAuthority authority)
        {
            if (_authority != null)
            {
                _authority.StateChanged -=
                    HandleAuthorityStateChanged;
            }

            _authority = authority;
            _lastRenderedRevision = int.MinValue;

            if (_authority != null)
            {
                _authority.StateChanged +=
                    HandleAuthorityStateChanged;
            }
        }

        private void HandleBootstrapStatusChanged(
            string message)
        {
            RefreshImmediate();
        }

        private void HandleAuthorityChanged(
            TRPGSessionAuthority authority)
        {
            BindAuthority(authority);
            RefreshImmediate();
        }

        private void HandleAuthorityStateChanged()
        {
            RefreshImmediate();
        }

        private void RefreshImmediate()
        {
            _nextRefreshTime = 0f;
            ResolveReferences();
            RefreshVisualState();
        }

        private void RefreshVisualState()
        {
            if (_hudCanvas == null ||
                _blockingCanvas == null)
            {
                return;
            }

            var showClientUi =
                ShouldShowClientUi();

            _hudCanvas.gameObject.SetActive(showClientUi);
            _blockingCanvas.gameObject.SetActive(showClientUi);

            if (!showClientUi)
                return;

            var transportConnected =
                _bootstrap != null &&
                _bootstrap.IsRunning &&
                _authority != null &&
                _authority.IsOnline;

            if (!transportConnected)
            {
                ShowDisconnectedOverlay();
                SetSelectionVisible(false);
                SetCharacterBadgeVisible(false);
                return;
            }

            if (!_authority.IsGameplayReady)
            {
                ShowGameplayInitializingOverlay();
                SetSelectionVisible(false);
                SetCharacterBadgeVisible(false);
                return;
            }

            FadeOutBlockingOverlay();

            var controlledDefinitionId =
                _authority.GetLocalControlledDefinitionId();

            var hasCharacter =
                !string.IsNullOrWhiteSpace(
                    controlledDefinitionId);

            SetSelectionVisible(!hasCharacter);
            SetCharacterBadgeVisible(hasCharacter);

            if (hasCharacter)
            {
                _characterBadgeText.text =
                    $"내 캐릭터 · {controlledDefinitionId}";
            }

            if (!hasCharacter &&
                _authority.StateRevision !=
                _lastRenderedRevision)
            {
                _lastRenderedRevision =
                    _authority.StateRevision;

                RebuildCharacterButtons();
            }
        }

        private bool ShouldShowClientUi()
        {
            if (Application.isEditor && !_showInEditor)
                return false;

            if (_bootstrap != null && _bootstrap.IsHost)
                return false;

            if (_authority != null &&
                _authority.IsLocalGameMaster)
            {
                return false;
            }

            return true;
        }

        private void ShowGameplayInitializingOverlay()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (!_blockingOverlay.activeSelf)
                _blockingOverlay.SetActive(true);

            _blockingCanvasGroup.alpha = 1f;
            _blockingCanvasGroup.blocksRaycasts = true;
            _blockingCanvasGroup.interactable = true;
            _titleText.text = "게임 상태 초기화 중";
            var readiness = _authority != null
                ? _authority.GetGameplayReadinessLabel()
                : "Session Authority 대기";

            _statusText.text =
                "서버 연결은 완료되었습니다.\n" +
                "Pawn, 스탯, 로그 동기화 계층을 준비하고 있습니다...\n\n" +
                readiness;
        }

        private void ShowDisconnectedOverlay()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (!_blockingOverlay.activeSelf)
                _blockingOverlay.SetActive(true);

            _blockingCanvasGroup.alpha = 1f;
            _blockingCanvasGroup.blocksRaycasts = true;
            _blockingCanvasGroup.interactable = true;

            var disconnected =
                _bootstrap != null &&
                _bootstrap.HasEverConnected;

            _titleText.text = disconnected
                ? "서버 연결 끊김"
                : "대기실";

            var mainMessage = disconnected
                ? "서버가 끊어진 상태입니다...\n" +
                  "서버를 다시 검색하고 있습니다."
                : "서버를 검색하고 있습니다...";

            var detail =
                _bootstrap != null
                    ? _bootstrap.Status
                    : "네트워크 초기화 대기 중";

            _statusText.text =
                $"{mainMessage}\n\n{detail}";
        }

        private void FadeOutBlockingOverlay()
        {
            if (!_blockingOverlay.activeSelf ||
                _fadeRoutine != null)
            {
                return;
            }

            _titleText.text = "접속 완료";
            _statusText.text =
                "서버에 연결되었습니다.";

            _fadeRoutine = StartCoroutine(
                FadeOutRoutine());
        }

        private IEnumerator FadeOutRoutine()
        {
            var startAlpha =
                _blockingCanvasGroup.alpha;

            var elapsed = 0f;
            var duration =
                Mathf.Max(0.05f, _fadeDuration);

            while (elapsed < duration)
            {
                if (_bootstrap == null ||
                    !_bootstrap.IsRunning ||
                    _authority == null ||
                    !_authority.IsOnline)
                {
                    _fadeRoutine = null;
                    ShowDisconnectedOverlay();
                    yield break;
                }

                if (!_authority.IsGameplayReady)
                {
                    _fadeRoutine = null;
                    ShowGameplayInitializingOverlay();
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;

                _blockingCanvasGroup.alpha =
                    Mathf.Lerp(
                        startAlpha,
                        0f,
                        elapsed / duration);

                yield return null;
            }

            _blockingCanvasGroup.alpha = 0f;
            _blockingCanvasGroup.blocksRaycasts = false;
            _blockingCanvasGroup.interactable = false;
            _blockingOverlay.SetActive(false);
            _fadeRoutine = null;
        }

        private void RebuildCharacterButtons()
        {
            ClearChildren(_selectionContent);

            if (_authority == null ||
                !_authority.IsOnline)
            {
                return;
            }

            var createdCount = 0;

            for (var index = 0;
                 index < _authority.PlayerSlots.Length;
                 index++)
            {
                var slot =
                    _authority.PlayerSlots[index];

                var definitionId =
                    slot.DefinitionId.ToString();

                if (string.IsNullOrWhiteSpace(
                        definitionId))
                {
                    continue;
                }

                var isClaimed =
                    slot.IsClaimed;

                var button = CreateSelectionButton(
                    _selectionContent,
                    isClaimed
                        ? $"{definitionId} · 사용 중"
                        : definitionId);

                button.interactable = !isClaimed;

                var capturedId = definitionId;
                button.onClick.AddListener(
                    () => RequestCharacterClaim(
                        capturedId));

                createdCount++;
            }

            _selectionMessage.text =
                createdCount > 0
                    ? "사용할 캐릭터를 선택하십시오."
                    : "선택할 수 있는 Player Pawn이 없습니다.";
        }

        private void RequestCharacterClaim(
            string definitionId)
        {
            if (_authority == null ||
                !_authority.IsOnline)
            {
                return;
            }

            _selectionMessage.text =
                $"{definitionId} 점유 요청 중...";

            _authority.RequestLocalCharacterClaim(
                definitionId);
        }

        private void SetSelectionVisible(bool visible)
        {
            if (_selectionPanel != null &&
                _selectionPanel.activeSelf != visible)
            {
                _selectionPanel.SetActive(visible);
            }
        }

        private void SetCharacterBadgeVisible(bool visible)
        {
            if (_characterBadge != null &&
                _characterBadge.activeSelf != visible)
            {
                _characterBadge.SetActive(visible);
            }
        }

        private void CreateRuntimeUi()
        {
            var font = ResolveFont();

            _hudCanvas = CreateRuntimeCanvas(
                "TRPGClientHudCanvas",
                _hudSortingOrder);

            _blockingCanvas = CreateRuntimeCanvas(
                "TRPGClientConnectionCanvas",
                _blockingSortingOrder);

            CreateBlockingOverlay(font);
            CreateSelectionPanel(font);
            CreateCharacterBadge(font);
        }

        private Canvas CreateRuntimeCanvas(
            string objectName,
            int sortingOrder)
        {
            var canvasObject =
                new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));

            canvasObject.transform.SetParent(
                transform,
                false);

            var canvas =
                canvasObject.GetComponent<Canvas>();

            canvas.renderMode =
                RenderMode.ScreenSpaceOverlay;

            canvas.sortingOrder =
                sortingOrder;

            var scaler =
                canvasObject.GetComponent<CanvasScaler>();

            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;

            scaler.referenceResolution =
                new Vector2(1920f, 1080f);

            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        private void CreateBlockingOverlay(Font font)
        {
            _blockingOverlay = CreateUiObject(
                "ConnectionOverlay",
                _blockingCanvas.transform,
                typeof(Image),
                typeof(CanvasGroup));

            StretchFull(
                _blockingOverlay.GetComponent<RectTransform>());

            var image =
                _blockingOverlay.GetComponent<Image>();

            image.color =
                new Color(
                    0f,
                    0f,
                    0f,
                    Mathf.Clamp01(_overlayOpacity));

            _blockingCanvasGroup =
                _blockingOverlay.GetComponent<CanvasGroup>();

            _titleText = CreateText(
                "Title",
                _blockingOverlay.transform,
                font,
                44,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            SetRect(
                _titleText.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(760f, 80f),
                new Vector2(0f, 90f));

            _statusText = CreateText(
                "Status",
                _blockingOverlay.transform,
                font,
                24,
                FontStyle.Normal,
                TextAnchor.UpperCenter);

            SetRect(
                _statusText.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(920f, 220f),
                new Vector2(0f, -70f));
        }

        private void CreateSelectionPanel(Font font)
        {
            _selectionPanel = CreateUiObject(
                "CharacterSelectionPanel",
                _hudCanvas.transform,
                typeof(Image));

            SetRect(
                _selectionPanel.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(540f, 430f),
                Vector2.zero);

            var background =
                _selectionPanel.GetComponent<Image>();

            background.color =
                new Color(0.04f, 0.04f, 0.04f, 0.94f);

            var title = CreateText(
                "SelectionTitle",
                _selectionPanel.transform,
                font,
                30,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            title.text = "캐릭터 선택";

            SetRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(480f, 60f),
                new Vector2(0f, -42f));

            _selectionMessage = CreateText(
                "SelectionMessage",
                _selectionPanel.transform,
                font,
                18,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);

            SetRect(
                _selectionMessage.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(480f, 50f),
                new Vector2(0f, -96f));

            var contentObject = CreateUiObject(
                "CharacterButtons",
                _selectionPanel.transform,
                typeof(VerticalLayoutGroup));

            _selectionContent =
                contentObject.GetComponent<RectTransform>();

            SetRect(
                _selectionContent,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(460f, 270f),
                new Vector2(0f, -250f));

            var layout =
                contentObject.GetComponent<VerticalLayoutGroup>();

            layout.padding =
                new RectOffset(0, 0, 0, 0);

            layout.spacing = 12f;
            layout.childAlignment =
                TextAnchor.UpperCenter;

            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            _selectionPanel.SetActive(false);
        }

        private void CreateCharacterBadge(Font font)
        {
            _characterBadge = CreateUiObject(
                "LocalCharacterBadge",
                _hudCanvas.transform,
                typeof(Image));

            SetRect(
                _characterBadge.GetComponent<RectTransform>(),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(380f, 58f),
                new Vector2(-214f, 48f));

            var background =
                _characterBadge.GetComponent<Image>();

            background.color =
                new Color(0f, 0f, 0f, 0.78f);

            _characterBadgeText = CreateText(
                "CharacterLabel",
                _characterBadge.transform,
                font,
                20,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);

            StretchFull(
                _characterBadgeText.rectTransform);

            _characterBadgeText.rectTransform.offsetMin =
                new Vector2(18f, 0f);

            _characterBadgeText.rectTransform.offsetMax =
                new Vector2(-18f, 0f);

            _characterBadge.SetActive(false);
        }

        private Button CreateSelectionButton(
            Transform parent,
            string label)
        {
            var buttonObject = CreateUiObject(
                "CharacterButton",
                parent,
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));

            var layout =
                buttonObject.GetComponent<LayoutElement>();

            layout.preferredHeight = 52f;
            layout.minHeight = 52f;

            var image =
                buttonObject.GetComponent<Image>();

            image.color =
                new Color(0.16f, 0.16f, 0.18f, 1f);

            var button =
                buttonObject.GetComponent<Button>();

            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor =
                new Color(0.16f, 0.16f, 0.18f, 1f);
            colors.highlightedColor =
                new Color(0.25f, 0.25f, 0.28f, 1f);
            colors.pressedColor =
                new Color(0.10f, 0.10f, 0.12f, 1f);
            colors.disabledColor =
                new Color(0.10f, 0.10f, 0.10f, 0.65f);
            button.colors = colors;

            var text = CreateText(
                "Label",
                buttonObject.transform,
                ResolveFont(),
                20,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            text.text = label;
            StretchFull(text.rectTransform);

            return button;
        }

        private Font ResolveFont()
        {
            if (_uiFont != null)
                return _uiFont;

            return Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
        }

        private static Text CreateText(
            string objectName,
            Transform parent,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment)
        {
            var textObject = CreateUiObject(
                objectName,
                parent,
                typeof(Text),
                typeof(Outline));

            var text =
                textObject.GetComponent<Text>();

            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            text.verticalOverflow =
                VerticalWrapMode.Overflow;

            var outline =
                textObject.GetComponent<Outline>();

            outline.effectColor =
                new Color(0f, 0f, 0f, 0.85f);

            outline.effectDistance =
                new Vector2(1f, -1f);

            return text;
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent,
            params System.Type[] components)
        {
            var gameObject =
                new GameObject(
                    objectName,
                    typeof(RectTransform));

            gameObject.transform.SetParent(
                parent,
                false);

            for (var index = 0;
                 index < components.Length;
                 index++)
            {
                gameObject.AddComponent(
                    components[index]);
            }

            return gameObject;
        }

        private static void StretchFull(
            RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot =
                new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void SetRect(
            RectTransform rectTransform,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.pivot =
                new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition =
                anchoredPosition;
        }

        private static void ClearChildren(
            Transform parent)
        {
            for (var index =
                     parent.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(
                    parent.GetChild(index).gameObject);
            }
        }
    }
}
