using System;
using System.Collections.Generic;
using Trpg.Domain.Dice;
using Trpg.Save;
using Trpg.UI.Skills;
using Trpg.UI.Stats;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    public sealed class CampaignSaveManager : MonoBehaviour
    {
        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private Font _menuFont;
        [SerializeField] private SystemMenuWidget _menu;

        private CampaignSaveService _saveService;
        private CoCCheckHistoryService _checkHistory;
        private InputAction _escapeAction;
        private bool _isInitialized;

        public void Configure(
            PawnManager pawnManager,
            CoCCheckHistoryService checkHistory = null)
        {
            if (pawnManager != null)
                _pawnManager = pawnManager;
            if (checkHistory != null)
                _checkHistory = checkHistory;

            TryInitialize();
        }

        private void Awake()
        {
            TryInitialize();
        }

        private void OnEnable()
        {
            TryInitialize();
            _escapeAction?.Enable();
        }

        private void OnDisable()
        {
            _escapeAction?.Disable();
            _menu?.Hide();
        }

        private void OnDestroy()
        {
            UnbindMenu();
            if (_escapeAction != null)
            {
                _escapeAction.performed -= HandleEscapePerformed;
                _escapeAction.Dispose();
                _escapeAction = null;
            }

            if (_menu != null)
            {
                var canvasObject = _menu.transform.parent != null
                    ? _menu.transform.parent.gameObject
                    : _menu.gameObject;
                Destroy(canvasObject);
                _menu = null;
            }
        }

        private void TryInitialize()
        {
            if (_isInitialized || _pawnManager == null)
                return;

            _saveService = new CampaignSaveService(
                Application.persistentDataPath);
            if (_menu == null)
                _menu = SystemMenuWidget.CreateRuntime(_menuFont);

            BindMenu();
            _escapeAction = new InputAction(
                "ToggleSystemMenu",
                InputActionType.Button,
                "<Keyboard>/escape");
            _escapeAction.performed += HandleEscapePerformed;
            if (isActiveAndEnabled)
                _escapeAction.Enable();

            _isInitialized = true;
        }

        private void BindMenu()
        {
            if (_menu == null)
                return;

            UnbindMenu();
            _menu.SaveRequested += HandleSaveRequested;
            _menu.LoadRequested += HandleLoadRequested;
            _menu.DeleteRequested += HandleDeleteRequested;
            _menu.ResetAllRequested += HandleResetAllRequested;
            _menu.SettingsRequested += HandleSettingsRequested;
            _menu.ExitRequested += HandleExitRequested;
        }

        private void UnbindMenu()
        {
            if (_menu == null)
                return;

            _menu.SaveRequested -= HandleSaveRequested;
            _menu.LoadRequested -= HandleLoadRequested;
            _menu.DeleteRequested -= HandleDeleteRequested;
            _menu.ResetAllRequested -= HandleResetAllRequested;
            _menu.SettingsRequested -= HandleSettingsRequested;
            _menu.ExitRequested -= HandleExitRequested;
        }

        private void HandleEscapePerformed(
            InputAction.CallbackContext context)
        {
            if (_menu == null)
                return;

            if (_menu.IsVisible)
            {
                if (_menu.TryCancelResetConfirmation())
                    return;

                _menu.Hide();
                return;
            }

            if (IsEditingInputField())
                return;

            RefreshSlots();
            _menu.Show();
        }

        private void HandleSaveRequested(string saveName)
        {
            var snapshot = CaptureSnapshot();
            if (!_saveService.TrySaveNew(
                    saveName,
                    snapshot,
                    out _,
                    out var error))
            {
                _menu.SetStatus(error, true);
                return;
            }

            RefreshSlots();
            _menu.SetStatus("저장했습니다.", false);
        }

        private void HandleLoadRequested(string saveId)
        {
            if (!_saveService.TryLoad(
                    saveId,
                    out var snapshot,
                    out var error))
            {
                _menu.SetStatus(error, true);
                return;
            }

            var rollback = CaptureSnapshot();
            if (!TryApplySnapshot(snapshot, out error))
            {
                TryApplySnapshot(rollback, out _);
                _menu.SetStatus(
                    "불러오기를 취소하고 이전 상태로 복원했습니다. " +
                    error,
                    true);
                return;
            }

            _menu.SetStatus("불러왔습니다.", false);
        }

        private void HandleDeleteRequested(string saveId)
        {
            if (!_saveService.TryDelete(saveId, out var error))
            {
                _menu.SetStatus(error, true);
                return;
            }

            RefreshSlots();
            _menu.SetStatus("저장 데이터를 삭제했습니다.", false);
        }

        private void HandleResetAllRequested()
        {
            var succeeded = _saveService.TryResetAll(
                out var deletedCount,
                out var error);
            RefreshSlots();
            if (!succeeded)
            {
                _menu.SetStatus(error, true);
                return;
            }

            _menu.SetStatus(
                deletedCount > 0
                    ? $"모든 저장 기록을 삭제했습니다. ({deletedCount}개)"
                    : "삭제할 저장 기록이 없습니다.",
                false);
        }

        private void HandleSettingsRequested()
        {
            // 프로젝트 고유 설정 UI는 이 이벤트 지점에 연결한다.
        }

        private void HandleExitRequested()
        {
            Application.Quit();
        }

        private CampaignSnapshot CaptureSnapshot()
        {
            var snapshot = new CampaignSnapshot
            {
                AppVersion = Application.version,
                CheckHistory = _checkHistory != null
                    ? _checkHistory.CreateSnapshot()
                    : new CoCCheckHistorySnapshot()
            };
            var pawns = CollectCharacterPawns();
            for (var index = 0; index < pawns.Count; index++)
            {
                var pawn = pawns[index];
                if (pawn == null ||
                    string.IsNullOrWhiteSpace(pawn.InstanceId))
                {
                    continue;
                }

                var position = pawn.transform.position;
                var stored = new PawnSnapshot
                {
                    InstanceId = pawn.InstanceId,
                    DefinitionId = pawn.Definition != null
                        ? pawn.Definition.Id
                        : string.Empty,
                    PositionX = position.x,
                    PositionY = position.y,
                    PositionZ = position.z,
                    RotationZ = pawn.transform.eulerAngles.z
                };

                var statState = EnsureStatState(pawn);
                stored.Stats = statState?.CreateSnapshot();
                var skillState = EnsureSkillState(pawn);
                stored.Skills = skillState?.CreateSnapshot();

                snapshot.Pawns.Add(stored);
            }

            return snapshot;
        }

        private bool TryApplySnapshot(
            CampaignSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (snapshot == null || snapshot.Pawns == null)
            {
                error = "Pawn 저장 데이터가 비어 있습니다.";
                return false;
            }

            var currentPawns = CollectCharacterPawns();
            var byId = new Dictionary<string, InteractivePawn>(
                StringComparer.Ordinal);
            for (var index = 0; index < currentPawns.Count; index++)
            {
                var pawn = currentPawns[index];
                if (pawn != null &&
                    !string.IsNullOrWhiteSpace(pawn.InstanceId))
                {
                    byId[pawn.InstanceId] = pawn;
                }
            }

            for (var index = 0; index < snapshot.Pawns.Count; index++)
            {
                var stored = snapshot.Pawns[index];
                if (stored == null ||
                    string.IsNullOrWhiteSpace(stored.InstanceId) ||
                    !byId.TryGetValue(
                        stored.InstanceId,
                        out var pawn))
                {
                    error = "씬에서 저장된 Pawn을 찾지 못했습니다: " +
                            (stored != null
                                ? stored.InstanceId
                                : "(null)");
                    return false;
                }

                var currentDefinitionId =
                    pawn.Definition != null
                        ? pawn.Definition.Id
                        : string.Empty;
                if (!string.Equals(
                        stored.DefinitionId,
                        currentDefinitionId,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Pawn 정의가 변경되었습니다: {stored.InstanceId}";
                    return false;
                }
            }

            _pawnManager.ClearSelection();
            for (var index = 0; index < snapshot.Pawns.Count; index++)
            {
                var stored = snapshot.Pawns[index];
                var pawn = byId[stored.InstanceId];

                if (stored.Stats != null)
                {
                    var statState = EnsureStatState(pawn);
                    if (statState == null ||
                        !statState.TryApplySnapshot(
                            stored.Stats,
                            out error))
                    {
                        error =
                            $"[{pawn.name}] 스탯 복원 실패: {error}";
                        return false;
                    }
                }

                if (stored.Skills != null)
                {
                    var skillState = EnsureSkillState(pawn);
                    if (skillState == null ||
                        !skillState.TryApplySnapshot(
                            stored.Skills,
                            out error))
                    {
                        error =
                            $"[{pawn.name}] 스킬 복원 실패: {error}";
                        return false;
                    }
                }

                pawn.TeleportTo(
                    new Vector2(
                        stored.PositionX,
                        stored.PositionY));
                var position = pawn.transform.position;
                position.z = stored.PositionZ;
                pawn.transform.position = position;
                pawn.transform.rotation = Quaternion.Euler(
                    0f,
                    0f,
                    stored.RotationZ);
            }

            if (_checkHistory != null &&
                !_checkHistory.TryRestore(
                    snapshot.CheckHistory,
                    out error))
            {
                error = "판정 기록 복원 실패: " + error;
                return false;
            }

            return true;
        }

        private List<InteractivePawn> CollectCharacterPawns()
        {
            var result = new List<InteractivePawn>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            AddPawns(_pawnManager.PlayerPawns, result, ids);
            AddPawns(_pawnManager.MonsterPawns, result, ids);
            AddPawns(_pawnManager.NpcPawns, result, ids);
            return result;
        }

        private static void AddPawns(
            IReadOnlyList<InteractivePawn> source,
            List<InteractivePawn> destination,
            HashSet<string> ids)
        {
            if (source == null)
                return;

            for (var index = 0; index < source.Count; index++)
            {
                var pawn = source[index];
                if (pawn == null ||
                    string.IsNullOrWhiteSpace(pawn.InstanceId) ||
                    !ids.Add(pawn.InstanceId))
                {
                    continue;
                }

                destination.Add(pawn);
            }
        }

        private static PlayerStatState EnsureStatState(
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
                return null;

            var state = pawn.GetComponent<PlayerStatState>();
            if (state == null)
                state = pawn.gameObject.AddComponent<PlayerStatState>();
            state.Configure(pawn.Definition);
            state.Initialize();
            return state;
        }

        private static PlayerSkillState EnsureSkillState(
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
                return null;

            var state = pawn.GetComponent<PlayerSkillState>();
            if (state == null)
                state = pawn.gameObject.AddComponent<PlayerSkillState>();
            state.Configure(pawn.Definition);
            state.Initialize();
            return state;
        }

        private void RefreshSlots()
        {
            _menu?.BindSlots(_saveService.ListSlots());
        }

        private static bool IsEditingInputField()
        {
            var eventSystem = EventSystem.current;
            var selected = eventSystem != null
                ? eventSystem.currentSelectedGameObject
                : null;
            return selected != null &&
                   selected.GetComponent<InputField>() != null;
        }
    }
}
