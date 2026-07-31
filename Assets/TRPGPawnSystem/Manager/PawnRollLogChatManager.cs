using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 굴림 로그 서비스와 좌측 로그/채팅 Widget을 연결합니다.
    /// 채팅 발신자는 PawnManager의 현재 선택된 Player Pawn을 사용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnRollLogChatManager : MonoBehaviour
    {
        private const int IntegrationFrameLimit = 90;

        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private PawnRollLogChatWidget _widget;

        private GameObject _ownedRuntimeCanvas;
        private Coroutine _integrationRoutine;
        private bool _widgetEventsBound;
        private bool _pawnEventsBound;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            var existing = FindFirst<PawnRollLogChatManager>();
            if (existing != null)
            {
                existing.BeginIntegration();
                return;
            }

            var host = FindFirst<PawnUIManager>();
            if (host != null)
            {
                host.gameObject.AddComponent<PawnRollLogChatManager>();
                return;
            }

            var pawnManager = FindFirst<PawnManager>();
            if (pawnManager != null)
            {
                pawnManager.gameObject.AddComponent<
                    PawnRollLogChatManager>();
            }
        }

        private static T FindFirst<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }

        private void OnEnable()
        {
            PawnRollLogService.EntryAdded += HandleEntryAdded;
            PawnRollLogService.EntriesCleared += HandleEntriesCleared;
            BeginIntegration();
        }

        private void OnDisable()
        {
            PawnRollLogService.EntryAdded -= HandleEntryAdded;
            PawnRollLogService.EntriesCleared -= HandleEntriesCleared;
            StopIntegration();
            UnbindWidgetEvents();
            UnbindPawnEvents();
        }

        private void OnDestroy()
        {
            if (_ownedRuntimeCanvas != null)
            {
                Destroy(_ownedRuntimeCanvas);
                _ownedRuntimeCanvas = null;
            }
        }

        private void BeginIntegration()
        {
            if (!isActiveAndEnabled)
                return;

            StopIntegration();
            if (TryIntegrate())
                return;

            _integrationRoutine = StartCoroutine(
                IntegrateWhenReady());
        }

        private IEnumerator IntegrateWhenReady()
        {
            for (var frame = 0;
                 frame < IntegrationFrameLimit;
                 frame++)
            {
                if (TryIntegrate())
                {
                    _integrationRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _integrationRoutine = null;
            Debug.LogWarning(
                $"[{name}] 로그/채팅 UI를 연결하지 못했습니다.",
                this);
        }

        private bool TryIntegrate()
        {
            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();
            if (_pawnManager == null)
                return false;

            if (_widget == null)
            {
                _widget = FindFirst<PawnRollLogChatWidget>();
                if (_widget == null)
                {
                    var font = ResolveReferenceFont();
                    _widget = PawnRollLogChatWidget.CreateRuntime(
                        font,
                        out _ownedRuntimeCanvas);
                }
            }
            if (_widget == null)
                return false;

            BindWidgetEvents();
            BindPawnEvents();
            _widget.SetEntries(PawnRollLogService.Entries);
            RefreshChatAvailability();
            return true;
        }

        private Font ResolveReferenceFont()
        {
            var infoBar = FindFirst<PawnInfoBarWidget>();
            if (infoBar == null)
                return null;

            var text = infoBar.GetComponentInChildren<Text>(true);
            return text != null ? text.font : null;
        }

        private void BindWidgetEvents()
        {
            if (_widgetEventsBound || _widget == null)
                return;

            _widget.ChatSubmitted += HandleChatSubmitted;
            _widgetEventsBound = true;
        }

        private void UnbindWidgetEvents()
        {
            if (!_widgetEventsBound || _widget == null)
                return;

            _widget.ChatSubmitted -= HandleChatSubmitted;
            _widgetEventsBound = false;
        }

        private void BindPawnEvents()
        {
            if (_pawnEventsBound || _pawnManager == null)
                return;

            _pawnManager.InteractiveSelectionChanged +=
                HandleInteractiveSelectionChanged;
            _pawnEventsBound = true;
        }

        private void UnbindPawnEvents()
        {
            if (!_pawnEventsBound)
                return;

            if (_pawnManager != null)
            {
                _pawnManager.InteractiveSelectionChanged -=
                    HandleInteractiveSelectionChanged;
            }

            _pawnEventsBound = false;
        }

        private void HandleEntryAdded(PawnRollLogEntry entry)
        {
            _widget?.Append(entry);
        }

        private void HandleEntriesCleared()
        {
            _widget?.ClearEntries();
        }

        private void HandleInteractiveSelectionChanged(
            InteractivePawn _)
        {
            RefreshChatAvailability();
        }

        private void HandleChatSubmitted(string message)
        {
            var pawn = ResolveActiveCharacter();
            if (pawn == null)
            {
                RefreshChatAvailability();
                return;
            }

            PawnRollLogService.RecordChat(
                pawn,
                ResolvePawnName(pawn),
                message);
        }

        private void RefreshChatAvailability()
        {
            if (_widget == null)
                return;

            var pawn = ResolveActiveCharacter();
            _widget.SetChatAvailability(
                pawn != null,
                ResolvePawnName(pawn));
        }

        private InteractivePawn ResolveActiveCharacter()
        {
            var candidate = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            return IsPlayerPawn(candidate) ? candidate : null;
        }

        private bool IsPlayerPawn(InteractivePawn pawn)
        {
            if (pawn == null || _pawnManager == null)
                return false;

            var players = _pawnManager.PlayerPawns;
            for (var index = 0; index < players.Count; index++)
            {
                if (players[index] == pawn)
                    return true;
            }

            return false;
        }

        private static string ResolvePawnName(InteractivePawn pawn)
        {
            if (pawn == null)
                return string.Empty;

            var definition = pawn.Definition;
            return definition != null &&
                   !string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.DisplayName
                : pawn.name;
        }

        private void StopIntegration()
        {
            if (_integrationRoutine == null)
                return;

            StopCoroutine(_integrationRoutine);
            _integrationRoutine = null;
        }
    }
}
