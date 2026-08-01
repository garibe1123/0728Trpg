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
    [DisallowMultipleComponent]
    public sealed class CampaignSaveManager : MonoBehaviour
    {
        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private Font _menuFont;
        [SerializeField] private SystemMenuWidget _menu;

        private enum PawnSaveGroup
        {
            Player,
            Monster,
            Npc
        }

        private sealed class PawnSaveRecord
        {
            public InteractivePawn Pawn;
            public PawnSaveGroup Group;
            public string SaveKey;
            public string LegacyInstanceId;
            public string DefinitionId;
        }

        private sealed class PawnLoadBinding
        {
            public PawnSnapshot Stored;
            public PawnSaveRecord Current;
        }

        private CampaignSaveService _saveService;
        private CoCCheckHistoryService _checkHistory;
        private InputAction _escapeAction;
        private GameObject _ownedRuntimeMenuRoot;
        private GameObject _loadConfirmationRoot;
        private Button _loadConfirmationAcceptButton;
        private Button _loadConfirmationCancelButton;
        private CampaignSnapshot _pendingLoadSnapshot;
        private CampaignSnapshot _pendingRollbackSnapshot;
        private bool _isInitialized;
        private bool _initializationErrorLogged;
        private int _lastEscapeFrame = -1;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            var pawnManager = FindFirst<PawnManager>();
            if (pawnManager == null)
            {
                Debug.LogError(
                    "CampaignSaveManager를 설치할 PawnManager를 " +
                    "찾지 못했습니다.");
                return;
            }

            var managers = FindAll<CampaignSaveManager>();
            var keeper = pawnManager.GetComponent<
                CampaignSaveManager>();

            if (keeper == null)
            {
                for (var index = 0; index < managers.Length; index++)
                {
                    var candidate = managers[index];
                    if (candidate == null ||
                        !candidate.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    keeper = candidate;
                    break;
                }
            }

            if (keeper == null)
            {
                keeper = pawnManager.gameObject.AddComponent<
                    CampaignSaveManager>();
            }

            keeper._pawnManager = pawnManager;
            keeper.enabled = true;

            for (var index = 0; index < managers.Length; index++)
            {
                var duplicate = managers[index];
                if (duplicate == null || duplicate == keeper)
                    continue;

                duplicate.enabled = false;
                UnityEngine.Object.Destroy(duplicate);
            }

            keeper.Reconnect();
        }

        private static T FindFirst<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }

        private static T[] FindAll<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<T>(true);
#endif
        }

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
            ResolvePawnManager();
            TryInitialize();
        }

        private void Start()
        {
            ResolvePawnManager();
            TryInitialize();
        }

        private void OnEnable()
        {
            ResolvePawnManager();
            TryInitialize();
            _escapeAction?.Enable();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null ||
                !keyboard.escapeKey.wasPressedThisFrame ||
                _lastEscapeFrame == Time.frameCount)
            {
                return;
            }

            _lastEscapeFrame = Time.frameCount;
            ToggleSystemMenu();
        }

        private void OnDisable()
        {
            _escapeAction?.Disable();
            HideLoadConfirmation();
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

            UnbindLoadConfirmationButtons();
            if (_loadConfirmationRoot != null)
            {
                Destroy(_loadConfirmationRoot);
                _loadConfirmationRoot = null;
            }
            _pendingLoadSnapshot = null;
            _pendingRollbackSnapshot = null;

            if (_ownedRuntimeMenuRoot != null)
            {
                Destroy(_ownedRuntimeMenuRoot);
                _ownedRuntimeMenuRoot = null;
            }

            _menu = null;
        }

        [ContextMenu("Reconnect Campaign Save System")]
        public void Reconnect()
        {
            ResolvePawnManager();
            TryInitialize();
            if (_isInitialized)
                RefreshSlots();
        }

        private void ResolvePawnManager()
        {
            if (_pawnManager != null)
                return;

            _pawnManager = GetComponent<PawnManager>();
            if (_pawnManager == null)
                _pawnManager = GetComponentInParent<PawnManager>();
            if (_pawnManager == null)
                _pawnManager = FindFirstObjectByType<PawnManager>();
        }

        private void TryInitialize()
        {
            if (_isInitialized)
                return;

            if (_pawnManager == null)
            {
                if (!_initializationErrorLogged)
                {
                    Debug.LogError(
                        $"[{name}] CampaignSaveManager가 PawnManager를 " +
                        "찾지 못해 ESC 저장 메뉴를 초기화하지 못했습니다.",
                        this);
                    _initializationErrorLogged = true;
                }
                return;
            }

            _saveService = new CampaignSaveService(
                Application.persistentDataPath);

            if (_menu == null)
            {
                _menu = SystemMenuWidget.CreateRuntime(_menuFont);
                if (_menu != null)
                {
                    var canvas = _menu.GetComponentInParent<Canvas>();
                    _ownedRuntimeMenuRoot = canvas != null
                        ? canvas.gameObject
                        : _menu.gameObject;
                }
            }

            if (_menu == null)
            {
                Debug.LogError(
                    $"[{name}] SystemMenuWidget을 생성하거나 연결하지 " +
                    "못했습니다.",
                    this);
                return;
            }

            PrepareMenuCanvas();
            BindMenu();

            _escapeAction = new InputAction(
                "ToggleSystemMenu",
                InputActionType.Button,
                "<Keyboard>/escape");
            _escapeAction.performed += HandleEscapePerformed;
            if (isActiveAndEnabled)
                _escapeAction.Enable();

            _initializationErrorLogged = false;
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
            if (_lastEscapeFrame == Time.frameCount)
                return;

            _lastEscapeFrame = Time.frameCount;
            ToggleSystemMenu();
        }

        private void ToggleSystemMenu()
        {
            if (!_isInitialized)
            {
                ResolvePawnManager();
                TryInitialize();
            }

            if (_menu == null)
            {
                Debug.LogError(
                    $"[{name}] ESC 입력은 감지했지만 " +
                    "SystemMenuWidget이 없습니다.",
                    this);
                return;
            }

            if (_loadConfirmationRoot != null &&
                _loadConfirmationRoot.activeSelf)
            {
                HideLoadConfirmation();
                return;
            }

            if (_menu.IsVisible)
            {
                if (_menu.TryCancelResetConfirmation())
                    return;

                _menu.Hide();
                return;
            }

            ClearFocusedUiSelection();
            PrepareMenuCanvas();
            RefreshSlots();
            _menu.Show();
        }

        private void HandleSaveRequested(string saveName)
        {
            if (!TryCaptureSnapshot(out var snapshot, out var error))
            {
                _menu.SetStatus(error, true);
                return;
            }

            if (!_saveService.TrySaveNew(
                    saveName,
                    snapshot,
                    out _,
                    out error))
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

            if (!TryCaptureSnapshot(out var rollback, out error))
            {
                _menu.SetStatus(
                    "현재 상태를 비교하지 못해 불러오기를 중단했습니다. " +
                    error,
                    true);
                return;
            }

            if (!TryHasMeaningfulDifference(
                    rollback,
                    snapshot,
                    out var hasDifference,
                    out error))
            {
                _menu.SetStatus(
                    "불러올 데이터를 현재 씬과 연결하지 못했습니다. " +
                    error,
                    true);
                return;
            }

            if (hasDifference)
            {
                ShowLoadConfirmation(snapshot, rollback);
                return;
            }

            ExecuteLoad(snapshot, rollback);
        }

        private void ExecuteLoad(
            CampaignSnapshot snapshot,
            CampaignSnapshot rollback)
        {
            HideLoadConfirmation();

            if (!TryApplySnapshot(snapshot, out var error))
            {
                Debug.LogError(
                    "저장 데이터 불러오기에 실패했습니다. " + error,
                    this);

                var rollbackSucceeded =
                    TryApplySnapshot(rollback, out var rollbackError);
                if (!rollbackSucceeded)
                {
                    Debug.LogError(
                        "불러오기 실패 후 현재 상태 복원에도 " +
                        "실패했습니다. " + rollbackError,
                        this);
                }

                _menu.SetStatus(
                    rollbackSucceeded
                        ? "불러오기에 실패했습니다. 현재 상태는 " +
                          "이전 상태로 복원되었습니다. " + error
                        : "불러오기와 이전 상태 복원에 모두 실패했습니다. " +
                          "불러오기 오류: " + error +
                          " / 복원 오류: " + rollbackError,
                    true);
                return;
            }

            _menu.SetStatus("불러왔습니다.", false);
        }

        private void ShowLoadConfirmation(
            CampaignSnapshot snapshot,
            CampaignSnapshot rollback)
        {
            EnsureLoadConfirmation();
            if (_loadConfirmationRoot == null)
            {
                _menu.SetStatus(
                    "불러오기 확인창을 만들지 못했습니다.",
                    true);
                return;
            }

            _pendingLoadSnapshot = snapshot;
            _pendingRollbackSnapshot = rollback;
            _loadConfirmationRoot.SetActive(true);
            _loadConfirmationRoot.transform.SetAsLastSibling();
        }

        private void HandleLoadConfirmationAccepted()
        {
            var snapshot = _pendingLoadSnapshot;
            var rollback = _pendingRollbackSnapshot;
            if (snapshot == null || rollback == null)
            {
                HideLoadConfirmation();
                _menu.SetStatus(
                    "불러오기 대기 데이터가 사라졌습니다.",
                    true);
                return;
            }

            ExecuteLoad(snapshot, rollback);
        }

        private void HandleLoadConfirmationCancelled()
        {
            HideLoadConfirmation();
            _menu.SetStatus("불러오기를 취소했습니다.", false);
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

        private bool TryCaptureSnapshot(
            out CampaignSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;

            if (_pawnManager == null)
            {
                error = "PawnManager가 연결되지 않았습니다.";
                return false;
            }

            if (!TryCollectPawnRecords(out var records, out error))
                return false;

            snapshot = new CampaignSnapshot
            {
                AppVersion = Application.version,
                CheckHistory = _checkHistory != null
                    ? _checkHistory.CreateSnapshot()
                    : new CoCCheckHistorySnapshot()
            };

            for (var index = 0; index < records.Count; index++)
            {
                snapshot.Pawns.Add(
                    CreatePawnSnapshot(records[index]));
            }

            return true;
        }

        private PawnSnapshot CreatePawnSnapshot(
            PawnSaveRecord record)
        {
            var pawn = record.Pawn;
            var position = pawn.transform.position;
            var movementManager = _pawnManager.MovementManager;
            if (movementManager != null &&
                movementManager.TryGetMovementPosition(
                    pawn,
                    out var movementPosition))
            {
                position.x = movementPosition.x;
                position.y = movementPosition.y;
            }

            var stored = new PawnSnapshot
            {
                InstanceId = record.SaveKey,
                DefinitionId = record.DefinitionId,
                PositionX = position.x,
                PositionY = position.y,
                PositionZ = position.z,
                RotationZ = pawn.transform.eulerAngles.z
            };

            var statState = PlayerStatState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            stored.Stats = statState != null
                ? statState.CreateSnapshot()
                : null;

            var skillState = ResolveSkillState(pawn);
            stored.Skills = skillState != null
                ? skillState.CreateSnapshot()
                : null;

            return stored;
        }

        private bool TryApplySnapshot(
            CampaignSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (!TryBuildLoadBindings(
                    snapshot,
                    out var bindings,
                    out error))
            {
                return false;
            }

            if (!TryPrepareStatStates(
                    bindings,
                    out var statStates,
                    out error))
            {
                Debug.LogError(error, this);
                return false;
            }

            _pawnManager.ClearSelection();
            var movementManager = _pawnManager.MovementManager;

            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                var stored = binding.Stored;
                var pawn = binding.Current.Pawn;

                if (stored.Stats != null)
                {
                    if (!statStates.TryGetValue(
                            pawn,
                            out var statState))
                    {
                        error =
                            $"[{pawn.name}] 스탯 복원 실패: " +
                            "사전 검증된 PlayerStatState를 찾지 못했습니다.";
                        Debug.LogError(error, pawn);
                        return false;
                    }

                    if (!statState.TryApplySnapshot(
                            stored.Stats,
                            out var statError))
                    {
                        error =
                            $"[{pawn.name}] 스탯 복원 실패: " +
                            statError;
                        Debug.LogError(error, pawn);
                        return false;
                    }
                }

                if (stored.Skills != null)
                {
                    var skillState = ResolveSkillState(pawn);
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

                var restoredPosition = new Vector2(
                    stored.PositionX,
                    stored.PositionY);
                if (movementManager != null)
                {
                    movementManager.RefreshMovementBudgetFromStats(
                        pawn,
                        false);
                    movementManager.RestorePawnPosition(
                        pawn,
                        restoredPosition);
                }
                else
                {
                    pawn.TeleportTo(restoredPosition);
                }

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

        private static bool TryPrepareStatStates(
            IReadOnlyList<PawnLoadBinding> bindings,
            out Dictionary<InteractivePawn, PlayerStatState> states,
            out string error)
        {
            states = new Dictionary<InteractivePawn, PlayerStatState>();
            error = string.Empty;

            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                var stored = binding.Stored;
                var pawn = binding.Current.Pawn;
                if (stored.Stats == null)
                    continue;

                var hadMissingMetadata =
                    string.IsNullOrWhiteSpace(
                        stored.Stats.CharacterDefinitionId) ||
                    string.IsNullOrWhiteSpace(
                        stored.Stats.RuleTemplateId) ||
                    stored.Stats.RuleTemplateVersion <= 0;

                if (!PlayerStatState.TryResolveOrCreateForSnapshot(
                        pawn.gameObject,
                        pawn.Definition,
                        stored.Stats,
                        out var state,
                        out var statError))
                {
                    error =
                        $"[{pawn.name}] PlayerStatState 준비 실패: " +
                        statError;
                    return false;
                }

                if (hadMissingMetadata)
                {
                    Debug.LogWarning(
                        $"[{pawn.name}] 구버전 스탯 저장 데이터의 " +
                        "누락된 식별 정보를 현재 InteractivePawnDefinition으로 " +
                        "보완했습니다. 불러온 뒤 새 슬롯으로 다시 저장하면 " +
                        "최신 형식으로 기록됩니다.",
                        pawn);
                }

                states.Add(pawn, state);
            }

            return true;
        }

        private bool TryHasMeaningfulDifference(
            CampaignSnapshot current,
            CampaignSnapshot target,
            out bool hasDifference,
            out string error)
        {
            hasDifference = true;
            error = string.Empty;

            if (!TryBuildLoadBindings(
                    target,
                    out var bindings,
                    out error))
            {
                return false;
            }

            if (!TryCollectPawnRecords(
                    out var currentRecords,
                    out error))
            {
                return false;
            }

            if (current == null || current.Pawns == null ||
                currentRecords.Count != target.Pawns.Count)
            {
                hasDifference = true;
                return true;
            }

            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                var currentPawn = CreatePawnSnapshot(binding.Current);
                if (!ArePawnSnapshotsEquivalent(
                        currentPawn,
                        binding.Stored))
                {
                    hasDifference = true;
                    return true;
                }
            }

            hasDifference = !AreJsonSnapshotsEquivalent(
                current.CheckHistory,
                target.CheckHistory);
            return true;
        }

        private bool TryBuildLoadBindings(
            CampaignSnapshot snapshot,
            out List<PawnLoadBinding> bindings,
            out string error)
        {
            bindings = new List<PawnLoadBinding>();
            error = string.Empty;
            if (snapshot == null || snapshot.Pawns == null)
            {
                error = "Pawn 저장 데이터가 비어 있습니다.";
                return false;
            }

            if (!TryCollectPawnRecords(
                    out var currentRecords,
                    out error))
            {
                return false;
            }

            var bySaveKey = new Dictionary<string, PawnSaveRecord>(
                StringComparer.Ordinal);
            for (var index = 0; index < currentRecords.Count; index++)
            {
                var record = currentRecords[index];
                bySaveKey.Add(record.SaveKey, record);
            }

            var used = new HashSet<InteractivePawn>();
            for (var index = 0; index < snapshot.Pawns.Count; index++)
            {
                var stored = snapshot.Pawns[index];
                if (stored == null ||
                    string.IsNullOrWhiteSpace(stored.InstanceId))
                {
                    error =
                        "저장 데이터에 식별 키가 비어 있는 Pawn이 있습니다.";
                    return false;
                }

                PawnSaveRecord record = null;
                if (bySaveKey.TryGetValue(
                        stored.InstanceId,
                        out var direct) &&
                    !used.Contains(direct.Pawn))
                {
                    record = direct;
                }
                else
                {
                    record = ResolveLegacyOrMovedRecord(
                        stored,
                        currentRecords,
                        used);
                }

                if (record == null)
                {
                    error =
                        "씬에서 저장된 Pawn을 찾지 못했습니다: " +
                        stored.InstanceId;
                    return false;
                }

                if (!string.Equals(
                        stored.DefinitionId,
                        record.DefinitionId,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Pawn 정의가 변경되었습니다: {stored.InstanceId}";
                    return false;
                }

                used.Add(record.Pawn);
                bindings.Add(
                    new PawnLoadBinding
                    {
                        Stored = stored,
                        Current = record
                    });
            }

            return true;
        }

        private static PawnSaveRecord ResolveLegacyOrMovedRecord(
            PawnSnapshot stored,
            IReadOnlyList<PawnSaveRecord> records,
            ISet<InteractivePawn> used)
        {
            var groupHint = ResolveGroupHint(stored.InstanceId);
            var legacyId = ResolveLegacyId(stored.InstanceId);

            PawnSaveRecord firstNonPlayer = null;
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                if (record == null ||
                    record.Pawn == null ||
                    used.Contains(record.Pawn) ||
                    !string.Equals(
                        record.DefinitionId,
                        stored.DefinitionId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (groupHint.HasValue &&
                    record.Group != groupHint.Value)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(legacyId) &&
                    !string.Equals(
                        record.LegacyInstanceId,
                        legacyId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (record.Group == PawnSaveGroup.Player)
                    return record;

                if (firstNonPlayer == null)
                    firstNonPlayer = record;
            }

            if (firstNonPlayer != null)
                return firstNonPlayer;

            // 아주 오래된 저장은 Monster/NPC 키에 그룹 정보가 없습니다.
            // 같은 Definition을 가진 미사용 비플레이어를 순서대로 연결합니다.
            if (!groupHint.HasValue)
            {
                for (var index = 0; index < records.Count; index++)
                {
                    var record = records[index];
                    if (record == null ||
                        record.Pawn == null ||
                        record.Group == PawnSaveGroup.Player ||
                        used.Contains(record.Pawn) ||
                        !string.Equals(
                            record.DefinitionId,
                            stored.DefinitionId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return record;
                }
            }

            return null;
        }

        private bool TryCollectPawnRecords(
            out List<PawnSaveRecord> records,
            out string error)
        {
            records = new List<PawnSaveRecord>();
            error = string.Empty;

            var playerIds = new Dictionary<string, InteractivePawn>(
                StringComparer.Ordinal);
            var playerDefinitions = new Dictionary<string, InteractivePawn>(
                StringComparer.Ordinal);
            var saveKeys = new HashSet<string>(StringComparer.Ordinal);

            if (!TryAddPawnRecords(
                    _pawnManager.PlayerPawns,
                    PawnSaveGroup.Player,
                    records,
                    saveKeys,
                    playerIds,
                    playerDefinitions,
                    out error))
            {
                return false;
            }

            if (!TryAddPawnRecords(
                    _pawnManager.MonsterPawns,
                    PawnSaveGroup.Monster,
                    records,
                    saveKeys,
                    playerIds,
                    playerDefinitions,
                    out error))
            {
                return false;
            }

            return TryAddPawnRecords(
                _pawnManager.NpcPawns,
                PawnSaveGroup.Npc,
                records,
                saveKeys,
                playerIds,
                playerDefinitions,
                out error);
        }

        private static bool TryAddPawnRecords(
            IReadOnlyList<InteractivePawn> source,
            PawnSaveGroup group,
            ICollection<PawnSaveRecord> destination,
            ISet<string> saveKeys,
            IDictionary<string, InteractivePawn> playerIds,
            IDictionary<string, InteractivePawn> playerDefinitions,
            out string error)
        {
            error = string.Empty;
            if (source == null)
                return true;

            for (var index = 0; index < source.Count; index++)
            {
                var pawn = source[index];
                if (pawn == null)
                    continue;

                var definitionId = pawn.Definition != null
                    ? pawn.Definition.Id
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(definitionId))
                {
                    error = $"[{group}] {pawn.name}의 DefinitionId가 " +
                            "비어 있습니다.";
                    return false;
                }

                if (group == PawnSaveGroup.Player)
                {
                    if (string.IsNullOrWhiteSpace(pawn.InstanceId))
                    {
                        error =
                            $"[Player] {pawn.name}의 InstanceId가 " +
                            "비어 있습니다.";
                        return false;
                    }

                    if (playerIds.TryGetValue(
                            pawn.InstanceId,
                            out var duplicateId))
                    {
                        error =
                            $"Player Pawn의 InstanceId가 중복됩니다: " +
                            $"{pawn.InstanceId} " +
                            $"({duplicateId.name}, {pawn.name})";
                        return false;
                    }

                    if (playerDefinitions.TryGetValue(
                            definitionId,
                            out var duplicateDefinition))
                    {
                        error =
                            $"같은 Player 캐릭터 Definition을 두 번 " +
                            $"사용할 수 없습니다: {definitionId} " +
                            $"({duplicateDefinition.name}, {pawn.name})";
                        return false;
                    }

                    playerIds.Add(pawn.InstanceId, pawn);
                    playerDefinitions.Add(definitionId, pawn);
                }

                var saveKey = BuildSaveKey(pawn, group);
                if (!saveKeys.Add(saveKey))
                {
                    error =
                        $"저장용 Pawn 키가 중복됩니다: {saveKey}";
                    return false;
                }

                destination.Add(
                    new PawnSaveRecord
                    {
                        Pawn = pawn,
                        Group = group,
                        SaveKey = saveKey,
                        LegacyInstanceId = pawn.InstanceId ?? string.Empty,
                        DefinitionId = definitionId
                    });
            }

            return true;
        }

        private static string BuildSaveKey(
            InteractivePawn pawn,
            PawnSaveGroup group)
        {
            if (group == PawnSaveGroup.Player)
                return "@TRPG:P:" + pawn.InstanceId;

            var prefix = group == PawnSaveGroup.Monster
                ? "@TRPG:M:"
                : "@TRPG:N:";
            var sceneName = pawn.gameObject.scene.name ?? string.Empty;
            return prefix + sceneName + ":" +
                   BuildHierarchySiblingPath(pawn.transform);
        }

        private static string BuildHierarchySiblingPath(
            Transform target)
        {
            var indices = new List<int>();
            var current = target;
            while (current != null)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            indices.Reverse();
            return string.Join(".", indices);
        }

        private static PawnSaveGroup? ResolveGroupHint(string saveKey)
        {
            if (saveKey.StartsWith(
                    "@TRPG:P:",
                    StringComparison.Ordinal))
            {
                return PawnSaveGroup.Player;
            }

            if (saveKey.StartsWith(
                    "@TRPG:M:",
                    StringComparison.Ordinal))
            {
                return PawnSaveGroup.Monster;
            }

            if (saveKey.StartsWith(
                    "@TRPG:N:",
                    StringComparison.Ordinal))
            {
                return PawnSaveGroup.Npc;
            }

            return null;
        }

        private static string ResolveLegacyId(string saveKey)
        {
            const string playerPrefix = "@TRPG:P:";
            if (saveKey.StartsWith(
                    playerPrefix,
                    StringComparison.Ordinal))
            {
                return saveKey.Substring(playerPrefix.Length);
            }

            return ResolveGroupHint(saveKey).HasValue
                ? string.Empty
                : saveKey;
        }

        private static bool ArePawnSnapshotsEquivalent(
            PawnSnapshot current,
            PawnSnapshot target)
        {
            if (current == null || target == null)
                return current == target;

            return string.Equals(
                       current.DefinitionId,
                       target.DefinitionId,
                       StringComparison.Ordinal) &&
                   Mathf.Abs(current.PositionX - target.PositionX) <= 0.001f &&
                   Mathf.Abs(current.PositionY - target.PositionY) <= 0.001f &&
                   Mathf.Abs(current.PositionZ - target.PositionZ) <= 0.001f &&
                   Mathf.Abs(Mathf.DeltaAngle(
                       current.RotationZ,
                       target.RotationZ)) <= 0.01f &&
                   AreJsonSnapshotsEquivalent(
                       current.Stats,
                       target.Stats) &&
                   AreJsonSnapshotsEquivalent(
                       current.Skills,
                       target.Skills);
        }

        private static bool AreJsonSnapshotsEquivalent(
            object left,
            object right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            return string.Equals(
                JsonUtility.ToJson(left),
                JsonUtility.ToJson(right),
                StringComparison.Ordinal);
        }

        private static PlayerSkillState ResolveSkillState(
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
                return null;

            return PlayerSkillState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
        }

        private void EnsureLoadConfirmation()
        {
            if (_loadConfirmationRoot != null || _menu == null)
                return;

            var canvas = _menu.GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            var root = new GameObject(
                "LoadConfirmation",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(canvas.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color =
                new Color(0f, 0f, 0f, 0.76f);

            var panel = new GameObject(
                "Panel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(rootRect, false);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(600f, 270f);
            panel.GetComponent<Image>().color =
                new Color(0.025f, 0.075f, 0.095f, 0.99f);

            var font = ResolveMenuFont();
            var title = CreateConfirmationText(
                "Title",
                panelRect,
                font,
                25,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            title.text = "저장 데이터 불러오기";
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(24f, -66f);
            title.rectTransform.offsetMax = new Vector2(-24f, -16f);

            var body = CreateConfirmationText(
                "Body",
                panelRect,
                font,
                18,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            body.text =
                "현재 데이터와 불러올 저장 데이터가 다릅니다.\n" +
                "현재 저장하지 않은 데이터가 소실될 것입니다.\n" +
                "괜찮으신가요?";
            body.rectTransform.anchorMin = new Vector2(0f, 0f);
            body.rectTransform.anchorMax = new Vector2(1f, 1f);
            body.rectTransform.offsetMin = new Vector2(34f, 78f);
            body.rectTransform.offsetMax = new Vector2(-34f, -72f);

            _loadConfirmationCancelButton =
                CreateConfirmationButton(
                    "CancelButton",
                    panelRect,
                    font,
                    "취소",
                    new Vector2(-112f, 26f),
                    new Color(0.13f, 0.17f, 0.19f, 0.98f));
            _loadConfirmationAcceptButton =
                CreateConfirmationButton(
                    "LoadButton",
                    panelRect,
                    font,
                    "불러오기",
                    new Vector2(112f, 26f),
                    new Color(0.07f, 0.38f, 0.48f, 0.98f));

            _loadConfirmationCancelButton.onClick.AddListener(
                HandleLoadConfirmationCancelled);
            _loadConfirmationAcceptButton.onClick.AddListener(
                HandleLoadConfirmationAccepted);

            _loadConfirmationRoot = root;
            _loadConfirmationRoot.SetActive(false);
        }

        private Font ResolveMenuFont()
        {
            if (_menuFont != null)
                return _menuFont;

            var menuText = _menu != null
                ? _menu.GetComponentInChildren<Text>(true)
                : null;
            return menuText != null ? menuText.font : null;
        }

        private static Text CreateConfirmationText(
            string objectName,
            Transform parent,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateConfirmationButton(
            string objectName,
            RectTransform parent,
            Font font,
            string label,
            Vector2 position,
            Color color)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(190f, 54f);

            var image = root.GetComponent<Image>();
            image.color = color;
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;

            var text = CreateConfirmationText(
                "Label",
                rect,
                font,
                18,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            text.text = label;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private void HideLoadConfirmation()
        {
            if (_loadConfirmationRoot != null)
                _loadConfirmationRoot.SetActive(false);

            _pendingLoadSnapshot = null;
            _pendingRollbackSnapshot = null;
        }

        private void UnbindLoadConfirmationButtons()
        {
            if (_loadConfirmationAcceptButton != null)
            {
                _loadConfirmationAcceptButton.onClick.RemoveListener(
                    HandleLoadConfirmationAccepted);
            }

            if (_loadConfirmationCancelButton != null)
            {
                _loadConfirmationCancelButton.onClick.RemoveListener(
                    HandleLoadConfirmationCancelled);
            }

            _loadConfirmationAcceptButton = null;
            _loadConfirmationCancelButton = null;
        }

        private void RefreshSlots()
        {
            if (_menu == null || _saveService == null)
                return;

            _menu.BindSlots(_saveService.ListSlots());
        }

        private void PrepareMenuCanvas()
        {
            if (_menu == null)
                return;

            var canvas = _menu.GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            if (!canvas.gameObject.activeSelf)
                canvas.gameObject.SetActive(true);

            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
        }

        private static void ClearFocusedUiSelection()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return;

            var selected = eventSystem.currentSelectedGameObject;
            if (selected != null)
            {
                var inputField = selected.GetComponent<InputField>();
                inputField?.DeactivateInputField();
            }

            eventSystem.SetSelectedGameObject(null);
        }
    }
}
