using System;
using System.Collections.Generic;
using System.Text;
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

        private sealed class LoadIssue
        {
            public string Code;
            public string PawnName;
            public string Message;

            public override string ToString()
            {
                var target = string.IsNullOrWhiteSpace(PawnName)
                    ? string.Empty
                    : $" {PawnName}";
                return $"[{Code}]{target} {Message}";
            }
        }

        private sealed class LoadPlan
        {
            public CampaignSnapshot Snapshot;
            public readonly List<PawnLoadBinding> Bindings =
                new List<PawnLoadBinding>();
            public readonly List<LoadIssue> Issues =
                new List<LoadIssue>();
            public bool HasMeaningfulDifference;
            public int StoredPawnCount;
            public int CurrentPawnCount;
        }

        private sealed class LoadResult
        {
            public readonly List<LoadIssue> Issues =
                new List<LoadIssue>();
            public int TotalStoredPawnCount;
            public int BoundPawnCount;
            public int FullyRestoredPawnCount;
            public int PartiallyRestoredPawnCount;
            public int SkippedPawnCount;
            public int PositionRestoredCount;
            public int StatRestoredCount;
            public int SkillRestoredCount;
            public bool CheckHistoryRestored;
        }

        private CampaignSaveService _saveService;
        private CoCCheckHistoryService _checkHistory;
        private InputAction _escapeAction;
        private GameObject _ownedRuntimeMenuRoot;
        private GameObject _loadConfirmationRoot;
        private Text _loadConfirmationBodyText;
        private Button _loadConfirmationAcceptButton;
        private Button _loadConfirmationCancelButton;
        private LoadPlan _pendingLoadPlan;
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
            _pendingLoadPlan = null;
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
                _menu.SetStatus(
                    "[LOAD-101] 저장 파일을 읽지 못했습니다. " + error,
                    true);
                return;
            }

            if (!TryCaptureSnapshot(out var rollback, out error))
            {
                _menu.SetStatus(
                    "[LOAD-105] 현재 상태를 백업하지 못해 " +
                    "불러오기를 중단했습니다. " + error,
                    true);
                return;
            }

            if (!TryBuildLoadPlan(
                    snapshot,
                    rollback,
                    out var plan,
                    out error))
            {
                _menu.SetStatus(error, true);
                return;
            }

            if (plan.HasMeaningfulDifference)
            {
                ShowLoadConfirmation(plan, rollback);
                return;
            }

            ExecuteLoad(plan, rollback);
        }

        private void ExecuteLoad(
            LoadPlan plan,
            CampaignSnapshot rollback)
        {
            HideLoadConfirmation();

            if (plan == null || plan.Snapshot == null)
            {
                _menu.SetStatus(
                    "[LOAD-102] 불러오기 계획 또는 Snapshot이 없습니다.",
                    true);
                return;
            }

            var result = ApplyLoadPlan(plan, rollback);
            var hasIssues = result.Issues.Count > 0;
            var message = BuildLoadResultMessage(result);
            LogLoadIssues(result.Issues);
            _menu.SetStatus(message, hasIssues);
        }

        private void ShowLoadConfirmation(
            LoadPlan plan,
            CampaignSnapshot rollback)
        {
            EnsureLoadConfirmation();
            if (_loadConfirmationRoot == null)
            {
                _menu.SetStatus(
                    "[LOAD-106] 불러오기 확인창을 만들지 못했습니다.",
                    true);
                return;
            }

            _pendingLoadPlan = plan;
            _pendingRollbackSnapshot = rollback;

            if (_loadConfirmationBodyText != null)
                _loadConfirmationBodyText.text =
                    BuildLoadConfirmationMessage(plan);

            if (_loadConfirmationAcceptButton != null)
            {
                var label = _loadConfirmationAcceptButton
                    .GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = plan.Issues.Count > 0
                        ? "확인된 항목 불러오기"
                        : "불러오기";
                }
            }

            _loadConfirmationRoot.SetActive(true);
            _loadConfirmationRoot.transform.SetAsLastSibling();
        }

        private void HandleLoadConfirmationAccepted()
        {
            var plan = _pendingLoadPlan;
            var rollback = _pendingRollbackSnapshot;
            if (plan == null || rollback == null)
            {
                HideLoadConfirmation();
                _menu.SetStatus(
                    "[LOAD-107] 불러오기 대기 데이터가 사라졌습니다.",
                    true);
                return;
            }

            ExecuteLoad(plan, rollback);
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

        private bool TryBuildLoadPlan(
            CampaignSnapshot snapshot,
            CampaignSnapshot current,
            out LoadPlan plan,
            out string error)
        {
            plan = null;
            error = string.Empty;

            if (snapshot == null || snapshot.Pawns == null)
            {
                error =
                    "[LOAD-102] Pawn 저장 데이터가 비어 있습니다.";
                return false;
            }

            if (!TryCollectPawnRecords(
                    out var currentRecords,
                    out var collectError))
            {
                error =
                    "[LOAD-104] 현재 씬의 Pawn 식별 상태가 유효하지 " +
                    "않습니다. " + collectError;
                return false;
            }

            plan = new LoadPlan
            {
                Snapshot = snapshot,
                StoredPawnCount = snapshot.Pawns.Count,
                CurrentPawnCount = currentRecords.Count
            };

            var bySaveKey = new Dictionary<string, PawnSaveRecord>(
                StringComparer.Ordinal);
            for (var index = 0; index < currentRecords.Count; index++)
            {
                var record = currentRecords[index];
                bySaveKey.Add(record.SaveKey, record);
            }

            var storedKeyCounts = new Dictionary<string, int>(
                StringComparer.Ordinal);
            for (var index = 0; index < snapshot.Pawns.Count; index++)
            {
                var stored = snapshot.Pawns[index];
                if (stored == null ||
                    string.IsNullOrWhiteSpace(stored.InstanceId))
                {
                    continue;
                }

                if (!storedKeyCounts.TryGetValue(
                        stored.InstanceId,
                        out var count))
                {
                    count = 0;
                }

                storedKeyCounts[stored.InstanceId] = count + 1;
            }

            var used = new HashSet<InteractivePawn>();
            for (var index = 0; index < snapshot.Pawns.Count; index++)
            {
                var stored = snapshot.Pawns[index];
                if (stored == null)
                {
                    AddLoadIssue(
                        plan.Issues,
                        "LOAD-204",
                        "(null)",
                        "저장 데이터의 Pawn 항목이 null이라 건너뜁니다.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(stored.InstanceId))
                {
                    AddLoadIssue(
                        plan.Issues,
                        "LOAD-204",
                        ResolveStoredPawnLabel(stored),
                        "저장 식별 키가 비어 있어 건너뜁니다.");
                    continue;
                }

                if (storedKeyCounts.TryGetValue(
                        stored.InstanceId,
                        out var duplicateCount) &&
                    duplicateCount > 1)
                {
                    AddLoadIssue(
                        plan.Issues,
                        "LOAD-204",
                        ResolveStoredPawnLabel(stored),
                        $"저장 식별 키가 {duplicateCount}번 중복되어 " +
                        "정확한 대상을 판단할 수 없습니다. " +
                        $"Key={stored.InstanceId}");
                    continue;
                }

                PawnSaveRecord record = null;
                var usedFallback = false;
                if (bySaveKey.TryGetValue(
                        stored.InstanceId,
                        out var direct) &&
                    !used.Contains(direct.Pawn) &&
                    IsDefinitionCompatible(stored, direct))
                {
                    record = direct;
                }
                else
                {
                    var candidates = CollectFallbackCandidates(
                        stored,
                        currentRecords,
                        used);
                    if (candidates.Count == 1)
                    {
                        record = candidates[0];
                        usedFallback = true;
                    }
                    else if (candidates.Count > 1)
                    {
                        AddLoadIssue(
                            plan.Issues,
                            "LOAD-204",
                            ResolveStoredPawnLabel(stored),
                            $"대응 가능한 Pawn이 {candidates.Count}개라 " +
                            "정확한 개체를 판단할 수 없어 건너뜁니다. " +
                            $"Key={stored.InstanceId}");
                        continue;
                    }
                }

                if (record == null)
                {
                    var directExists = bySaveKey.TryGetValue(
                        stored.InstanceId,
                        out var mismatchedDirect);
                    var code = directExists &&
                               mismatchedDirect != null &&
                               !IsDefinitionCompatible(
                                   stored,
                                   mismatchedDirect)
                        ? "LOAD-203"
                        : "LOAD-201";
                    var message = code == "LOAD-203"
                        ? "저장된 Definition과 현재 Pawn Definition이 " +
                          "달라 해당 Pawn을 건너뜁니다."
                        : "저장된 Pawn을 현재 씬에서 찾지 못해 " +
                          "건너뜁니다.";
                    AddLoadIssue(
                        plan.Issues,
                        code,
                        ResolveStoredPawnLabel(stored),
                        message + $" Key={stored.InstanceId}");
                    continue;
                }

                if (usedFallback)
                {
                    AddLoadIssue(
                        plan.Issues,
                        "LOAD-206",
                        record.Pawn != null
                            ? record.Pawn.name
                            : ResolveStoredPawnLabel(stored),
                        "저장 키가 변경되어 Definition/구형 ID 기준으로 " +
                        "대체 연결했습니다.");
                }

                if (string.IsNullOrWhiteSpace(stored.DefinitionId))
                {
                    stored.DefinitionId = record.DefinitionId;
                    AddLoadIssue(
                        plan.Issues,
                        "LOAD-207",
                        record.Pawn.name,
                        "구버전 저장의 빈 DefinitionId를 현재 Pawn " +
                        "Definition으로 보완했습니다.");
                }

                used.Add(record.Pawn);
                plan.Bindings.Add(
                    new PawnLoadBinding
                    {
                        Stored = stored,
                        Current = record
                    });
            }

            for (var index = 0; index < currentRecords.Count; index++)
            {
                var record = currentRecords[index];
                if (record == null ||
                    record.Pawn == null ||
                    used.Contains(record.Pawn))
                {
                    continue;
                }

                AddLoadIssue(
                    plan.Issues,
                    "LOAD-202",
                    record.Pawn.name,
                    "현재 씬에만 존재하므로 현재 상태를 유지합니다.");
            }

            plan.HasMeaningfulDifference = plan.Issues.Count > 0 ||
                                           plan.Bindings.Count !=
                                           snapshot.Pawns.Count;

            if (!plan.HasMeaningfulDifference)
            {
                for (var index = 0; index < plan.Bindings.Count; index++)
                {
                    var binding = plan.Bindings[index];
                    var currentPawn = CreatePawnSnapshot(binding.Current);
                    if (!ArePawnSnapshotsEquivalent(
                            currentPawn,
                            binding.Stored))
                    {
                        plan.HasMeaningfulDifference = true;
                        break;
                    }
                }
            }

            if (!plan.HasMeaningfulDifference && current != null)
            {
                plan.HasMeaningfulDifference =
                    !AreJsonSnapshotsEquivalent(
                        current.CheckHistory,
                        snapshot.CheckHistory);
            }

            return true;
        }

        private LoadResult ApplyLoadPlan(
            LoadPlan plan,
            CampaignSnapshot rollback)
        {
            var result = new LoadResult
            {
                TotalStoredPawnCount = plan.StoredPawnCount,
                BoundPawnCount = plan.Bindings.Count
            };
            result.Issues.AddRange(plan.Issues);

            _pawnManager.ClearSelection();
            for (var index = 0; index < plan.Bindings.Count; index++)
            {
                ApplyPawnBinding(plan.Bindings[index], result);
            }

            result.SkippedPawnCount +=
                Mathf.Max(0, plan.StoredPawnCount - plan.Bindings.Count);

            if (_checkHistory != null)
            {
                if (_checkHistory.TryRestore(
                        plan.Snapshot.CheckHistory,
                        out var historyError))
                {
                    result.CheckHistoryRestored = true;
                }
                else
                {
                    AddLoadIssue(
                        result.Issues,
                        "LOAD-601",
                        string.Empty,
                        "판정 기록을 복원하지 못해 기존 기록을 " +
                        "유지합니다. " + historyError);

                    if (rollback != null)
                    {
                        _checkHistory.TryRestore(
                            rollback.CheckHistory,
                            out _);
                    }
                }
            }

            return result;
        }

        private void ApplyPawnBinding(
            PawnLoadBinding binding,
            LoadResult result)
        {
            var stored = binding.Stored;
            var record = binding.Current;
            var pawn = record != null ? record.Pawn : null;
            if (stored == null || pawn == null)
            {
                result.SkippedPawnCount++;
                AddLoadIssue(
                    result.Issues,
                    "LOAD-201",
                    ResolveStoredPawnLabel(stored),
                    "적용 직전에 Pawn 연결이 사라져 건너뜁니다.");
                return;
            }

            PawnSnapshot before = null;
            try
            {
                before = CreatePawnSnapshot(record);
            }
            catch (Exception exception)
            {
                result.SkippedPawnCount++;
                AddLoadIssue(
                    result.Issues,
                    "LOAD-901",
                    pawn.name,
                    "현재 Pawn 상태를 백업하지 못해 이 Pawn을 " +
                    "건너뜁니다. " + exception.Message);
                return;
            }

            var requestedAreaCount = 1;
            if (stored.Stats != null)
                requestedAreaCount++;
            if (stored.Skills != null)
                requestedAreaCount++;

            var restoredAreaCount = 0;
            var pawnHasIssue = false;

            if (stored.Stats != null)
            {
                if (TryRestoreStats(
                        pawn,
                        stored.Stats,
                        before != null ? before.Stats : null,
                        result.Issues))
                {
                    result.StatRestoredCount++;
                    restoredAreaCount++;
                }
                else
                {
                    pawnHasIssue = true;
                }
            }

            if (stored.Skills != null)
            {
                if (TryRestoreSkills(
                        pawn,
                        stored.Skills,
                        before != null ? before.Skills : null,
                        result.Issues))
                {
                    result.SkillRestoredCount++;
                    restoredAreaCount++;
                }
                else
                {
                    pawnHasIssue = true;
                }
            }

            if (TryRestorePosition(
                    pawn,
                    stored,
                    before,
                    result.Issues,
                    out var positionHadWarning))
            {
                result.PositionRestoredCount++;
                restoredAreaCount++;
                pawnHasIssue |= positionHadWarning;
            }
            else
            {
                pawnHasIssue = true;
            }

            if (restoredAreaCount <= 0)
            {
                result.SkippedPawnCount++;
            }
            else if (pawnHasIssue ||
                     restoredAreaCount < requestedAreaCount)
            {
                result.PartiallyRestoredPawnCount++;
            }
            else
            {
                result.FullyRestoredPawnCount++;
            }
        }

        private static bool TryRestoreStats(
            InteractivePawn pawn,
            Trpg.Domain.Stats.StatRuntimeSnapshot target,
            Trpg.Domain.Stats.StatRuntimeSnapshot rollback,
            ICollection<LoadIssue> issues)
        {
            if (!PlayerStatState.TryResolveOrCreateForSnapshot(
                    pawn.gameObject,
                    pawn.Definition,
                    target,
                    out var state,
                    out var prepareError))
            {
                AddLoadIssue(
                    issues,
                    "LOAD-301",
                    pawn.name,
                    "PlayerStatState를 준비하지 못해 스탯을 " +
                    "건너뜁니다. " + prepareError);
                return false;
            }

            if (state.TryApplySnapshot(target, out var applyError))
                return true;

            AddLoadIssue(
                issues,
                "LOAD-302",
                pawn.name,
                "스탯 Snapshot 적용에 실패해 기존 스탯을 " +
                "유지합니다. " + applyError);

            if (rollback != null &&
                !state.TryApplySnapshot(rollback, out var restoreError))
            {
                AddLoadIssue(
                    issues,
                    "LOAD-309",
                    pawn.name,
                    "스탯 적용 실패 후 기존 스탯 복원에도 " +
                    "실패했습니다. " + restoreError);
            }

            return false;
        }

        private static bool TryRestoreSkills(
            InteractivePawn pawn,
            SkillRuntimeSnapshot target,
            SkillRuntimeSnapshot rollback,
            ICollection<LoadIssue> issues)
        {
            var state = ResolveSkillState(pawn);
            if (state == null)
            {
                AddLoadIssue(
                    issues,
                    "LOAD-401",
                    pawn.name,
                    "PlayerSkillState를 준비하지 못해 스킬을 " +
                    "건너뜁니다.");
                return false;
            }

            if (state.TryApplySnapshot(target, out var applyError))
                return true;

            AddLoadIssue(
                issues,
                "LOAD-402",
                pawn.name,
                "스킬 Snapshot 적용에 실패해 기존 스킬을 " +
                "유지합니다. " + applyError);

            if (rollback != null &&
                !state.TryApplySnapshot(rollback, out var restoreError))
            {
                AddLoadIssue(
                    issues,
                    "LOAD-409",
                    pawn.name,
                    "스킬 적용 실패 후 기존 스킬 복원에도 " +
                    "실패했습니다. " + restoreError);
            }

            return false;
        }

        private bool TryRestorePosition(
            InteractivePawn pawn,
            PawnSnapshot target,
            PawnSnapshot rollback,
            ICollection<LoadIssue> issues,
            out bool hadWarning)
        {
            hadWarning = false;
            if (!IsFinite(target.PositionX) ||
                !IsFinite(target.PositionY) ||
                !IsFinite(target.PositionZ) ||
                !IsFinite(target.RotationZ))
            {
                AddLoadIssue(
                    issues,
                    "LOAD-501",
                    pawn.name,
                    "위치 또는 회전 값이 NaN/Infinity라 기존 위치를 " +
                    "유지합니다.");
                return false;
            }

            try
            {
                var restoredPosition = new Vector2(
                    target.PositionX,
                    target.PositionY);
                var movementManager = _pawnManager.MovementManager;
                if (movementManager != null)
                {
                    movementManager.RefreshMovementBudgetFromStats(
                        pawn,
                        false);
                    if (!movementManager.RestorePawnPosition(
                            pawn,
                            restoredPosition))
                    {
                        pawn.TeleportTo(restoredPosition);
                        hadWarning = true;
                        AddLoadIssue(
                            issues,
                            "LOAD-503",
                            pawn.name,
                            "MovementManager 내부 위치 동기화에 실패해 " +
                            "Transform 위치만 복원했습니다.");
                    }
                }
                else
                {
                    pawn.TeleportTo(restoredPosition);
                    hadWarning = true;
                    AddLoadIssue(
                        issues,
                        "LOAD-503",
                        pawn.name,
                        "PawnMovementManager가 없어 Transform 위치만 " +
                        "복원했습니다.");
                }

                var position = pawn.transform.position;
                position.z = target.PositionZ;
                pawn.transform.position = position;
                pawn.transform.rotation = Quaternion.Euler(
                    0f,
                    0f,
                    target.RotationZ);
                return true;
            }
            catch (Exception exception)
            {
                AddLoadIssue(
                    issues,
                    "LOAD-502",
                    pawn.name,
                    "위치 복원 중 예외가 발생해 기존 위치를 " +
                    "유지합니다. " + exception.Message);

                if (rollback != null)
                    RestorePositionBestEffort(pawn, rollback);
                return false;
            }
        }

        private void RestorePositionBestEffort(
            InteractivePawn pawn,
            PawnSnapshot snapshot)
        {
            if (pawn == null || snapshot == null)
                return;

            try
            {
                var position2D = new Vector2(
                    snapshot.PositionX,
                    snapshot.PositionY);
                var movementManager = _pawnManager.MovementManager;
                if (movementManager != null)
                    movementManager.RestorePawnPosition(pawn, position2D);
                else
                    pawn.TeleportTo(position2D);

                var position = pawn.transform.position;
                position.z = snapshot.PositionZ;
                pawn.transform.position = position;
                pawn.transform.rotation = Quaternion.Euler(
                    0f,
                    0f,
                    snapshot.RotationZ);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[{pawn.name}] 위치 롤백 실패: {exception.Message}",
                    pawn);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static bool IsDefinitionCompatible(
            PawnSnapshot stored,
            PawnSaveRecord record)
        {
            if (stored == null || record == null)
                return false;

            return string.IsNullOrWhiteSpace(stored.DefinitionId) ||
                   string.Equals(
                       stored.DefinitionId,
                       record.DefinitionId,
                       StringComparison.Ordinal);
        }

        private static List<PawnSaveRecord> CollectFallbackCandidates(
            PawnSnapshot stored,
            IReadOnlyList<PawnSaveRecord> records,
            ISet<InteractivePawn> used)
        {
            var result = new List<PawnSaveRecord>();
            var groupHint = ResolveGroupHint(stored.InstanceId);
            var legacyId = ResolveLegacyId(stored.InstanceId);

            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                if (record == null ||
                    record.Pawn == null ||
                    used.Contains(record.Pawn) ||
                    !IsDefinitionCompatible(stored, record))
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

                result.Add(record);
            }

            if (result.Count == 0 && !groupHint.HasValue)
            {
                for (var index = 0; index < records.Count; index++)
                {
                    var record = records[index];
                    if (record == null ||
                        record.Pawn == null ||
                        record.Group == PawnSaveGroup.Player ||
                        used.Contains(record.Pawn) ||
                        !IsDefinitionCompatible(stored, record))
                    {
                        continue;
                    }

                    result.Add(record);
                }
            }

            return result;
        }

        private static string ResolveStoredPawnLabel(
            PawnSnapshot stored)
        {
            if (stored == null)
                return "(null)";
            if (!string.IsNullOrWhiteSpace(stored.DefinitionId))
                return stored.DefinitionId;
            if (!string.IsNullOrWhiteSpace(stored.InstanceId))
                return stored.InstanceId;
            return "(식별 불가 Pawn)";
        }

        private static void AddLoadIssue(
            ICollection<LoadIssue> issues,
            string code,
            string pawnName,
            string message)
        {
            if (issues == null)
                return;

            issues.Add(
                new LoadIssue
                {
                    Code = code,
                    PawnName = pawnName ?? string.Empty,
                    Message = message ?? string.Empty
                });
        }

        private static string BuildLoadConfirmationMessage(LoadPlan plan)
        {
            var missing = CountIssues(plan.Issues, "LOAD-201");
            var currentOnly = CountIssues(plan.Issues, "LOAD-202");
            var incompatible = CountIssues(plan.Issues, "LOAD-203");
            var ambiguous = CountIssues(plan.Issues, "LOAD-204");

            var builder = new StringBuilder(320);
            builder.AppendLine("저장 데이터를 검사했습니다.");
            builder.Append("복원 가능: ")
                .Append(plan.Bindings.Count)
                .Append(" / 저장 Pawn: ")
                .Append(plan.StoredPawnCount)
                .Append(" / 현재 씬 Pawn: ")
                .AppendLine(plan.CurrentPawnCount.ToString());

            if (plan.Issues.Count > 0)
            {
                builder.Append("누락 ")
                    .Append(missing)
                    .Append(" · 현재 씬 전용 ")
                    .Append(currentOnly)
                    .Append(" · 정의 불일치 ")
                    .Append(incompatible)
                    .Append(" · 모호함 ")
                    .AppendLine(ambiguous.ToString());
                builder.Append("경고 코드: ")
                    .AppendLine(BuildIssueCodeSummary(plan.Issues));
            }

            builder.AppendLine();
            builder.AppendLine(
                "현재 저장하지 않은 데이터가 소실될 수 있습니다.");
            builder.Append(
                plan.Issues.Count > 0
                    ? "확인된 항목만 불러오시겠습니까?"
                    : "불러오시겠습니까?");
            return builder.ToString();
        }

        private static string BuildLoadResultMessage(LoadResult result)
        {
            var builder = new StringBuilder(640);
            builder.AppendLine(
                result.Issues.Count > 0
                    ? "부분 불러오기 완료"
                    : "불러왔습니다.");
            builder.Append("저장 Pawn ")
                .Append(result.TotalStoredPawnCount)
                .Append(" · 연결 ")
                .AppendLine(result.BoundPawnCount.ToString());
            builder.Append("Pawn 완전 복원 ")
                .Append(result.FullyRestoredPawnCount)
                .Append(" · 부분 복원 ")
                .Append(result.PartiallyRestoredPawnCount)
                .Append(" · 건너뜀 ")
                .AppendLine(result.SkippedPawnCount.ToString());
            builder.Append("위치 ")
                .Append(result.PositionRestoredCount)
                .Append(" · 스탯 ")
                .Append(result.StatRestoredCount)
                .Append(" · 스킬 ")
                .AppendLine(result.SkillRestoredCount.ToString());

            if (result.Issues.Count > 0)
            {
                builder.Append("경고 ")
                    .Append(result.Issues.Count)
                    .AppendLine("건");

                var displayCount = Mathf.Min(6, result.Issues.Count);
                for (var index = 0; index < displayCount; index++)
                    builder.AppendLine(result.Issues[index].ToString());

                if (result.Issues.Count > displayCount)
                {
                    builder.Append("외 ")
                        .Append(result.Issues.Count - displayCount)
                        .AppendLine("건 — Console에서 전체 확인");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildIssueCodeSummary(
            IReadOnlyList<LoadIssue> issues)
        {
            var counts = new Dictionary<string, int>(
                StringComparer.Ordinal);
            var order = new List<string>();
            for (var index = 0; index < issues.Count; index++)
            {
                var code = issues[index].Code ?? "LOAD-000";
                if (!counts.TryGetValue(code, out var count))
                {
                    counts.Add(code, 1);
                    order.Add(code);
                }
                else
                {
                    counts[code] = count + 1;
                }
            }

            var builder = new StringBuilder(96);
            for (var index = 0; index < order.Count; index++)
            {
                if (index > 0)
                    builder.Append(", ");

                var code = order[index];
                builder.Append(code)
                    .Append('×')
                    .Append(counts[code]);
            }

            return builder.ToString();
        }

        private static int CountIssues(
            IReadOnlyList<LoadIssue> issues,
            string code)
        {
            var count = 0;
            for (var index = 0; index < issues.Count; index++)
            {
                if (string.Equals(
                        issues[index].Code,
                        code,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static void LogLoadIssues(
            IReadOnlyList<LoadIssue> issues)
        {
            for (var index = 0; index < issues.Count; index++)
                Debug.LogWarning(issues[index].ToString());
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
            panelRect.sizeDelta = new Vector2(700f, 360f);
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

            _loadConfirmationBodyText = CreateConfirmationText(
                "Body",
                panelRect,
                font,
                18,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            _loadConfirmationBodyText.text =
                "저장 데이터를 검사하고 있습니다.";
            _loadConfirmationBodyText.rectTransform.anchorMin =
                new Vector2(0f, 0f);
            _loadConfirmationBodyText.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            _loadConfirmationBodyText.rectTransform.offsetMin =
                new Vector2(34f, 78f);
            _loadConfirmationBodyText.rectTransform.offsetMax =
                new Vector2(-34f, -72f);

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

            _pendingLoadPlan = null;
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
            _loadConfirmationBodyText = null;
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
