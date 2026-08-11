using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Fusion;
using Trpg.Domain.Stats;
using Trpg.UI.Stats;
using UnityEngine;

namespace Trpg.Pawns
{
    public struct TRPGNetworkMovePacket : INetworkStruct
    {
        public NetworkString<_64> PawnDefinitionId;
        public Vector2 Destination;
        public float MoveCost;
        public float RemainingMeters;
        public float MaximumMeters;
        public int Mode;
        public int CornerCount;
        public Vector2 Corner0;
        public Vector2 Corner1;
        public Vector2 Corner2;
        public Vector2 Corner3;
        public Vector2 Corner4;
        public Vector2 Corner5;
        public Vector2 Corner6;
        public Vector2 Corner7;

        public Vector2 GetCorner(int index)
        {
            switch (index)
            {
                case 0: return Corner0;
                case 1: return Corner1;
                case 2: return Corner2;
                case 3: return Corner3;
                case 4: return Corner4;
                case 5: return Corner5;
                case 6: return Corner6;
                case 7: return Corner7;
                default: return Destination;
            }
        }

        public void SetCorner(int index, Vector2 value)
        {
            switch (index)
            {
                case 0: Corner0 = value; break;
                case 1: Corner1 = value; break;
                case 2: Corner2 = value; break;
                case 3: Corner3 = value; break;
                case 4: Corner4 = value; break;
                case 5: Corner5 = value; break;
                case 6: Corner6 = value; break;
                case 7: Corner7 = value; break;
            }
        }
    }

    public struct TRPGNetworkStatPacket : INetworkStruct
    {
        // RPC payload hard limit is 512 bytes. _32 uses 128 bytes.
        public NetworkString<_32> PawnDefinitionId;
        public NetworkString<_32> StatId;
        public double PreviousValue;
        public double CurrentValue;
        public NetworkBool IsSnapshot;
    }

    public struct TRPGNetworkLogPacket : INetworkStruct
    {
        // Keep the full struct safely below Fusion's 512-byte RPC limit.
        // NetworkString capacity units occupy 4 bytes each.
        public NetworkString<_16> EventId;
        public NetworkString<_32> PawnDefinitionId;
        public NetworkString<_8> PawnName;
        public NetworkString<_8> Title;
        public NetworkString<_16> Expression;
        public NetworkString<_8> Result;
        public NetworkString<_16> Detail;
        public int Kind;
        public int Value;
        public int MinimumValue;
        public int MaximumValue;
        public float DurationSeconds;
        public int ResultTone;
        public byte ColorRed;
        public byte ColorGreen;
        public byte ColorBlue;
        public byte ColorAlpha;
        public NetworkBool ShowRoulette;
        public NetworkBool AnimateRoulette;
        public int Visibility;
        public PlayerRef Roller;
    }

    public struct TRPGPlayerSlotState : INetworkStruct
    {
        public NetworkString<_64> DefinitionId;
        public NetworkString<_64> PawnInstanceId;
        public PlayerRef ClaimedBy;
        public NetworkBool IsClaimed;
    }

    /// <summary>
    /// 현재 씬에서 이미 정상 Spawn되는 단일 NetworkBehaviour입니다.
    /// 캐릭터 점유, 이동, 스탯, 로그, 원격 룰렛 RPC를 모두 이 컴포넌트가 담당합니다.
    /// 별도 NetworkGameManager/NetworkLogManager는 로컬 어댑터만 사용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(
        typeof(NetworkObject),
        typeof(TRPGNetworkGameManager),
        typeof(TRPGNetworkLogManager))]
    public sealed partial class TRPGSessionAuthority : NetworkBehaviour
    {
        public const int MaximumCharacterSlots = 4;

        private const int MaximumReplicatedCorners = 8;
        private const int MaximumPendingPackets = 256;
        private const int MaximumHistoryEntries = 100;
        private const int InitializationFrameLimit = 480;
        private const int PendingApplyFrameLimit = 480;

        private static TRPGSessionAuthority _instance;

        [Header("Auto References")]
        [SerializeField, HideInInspector]
        private PawnManager _pawnManager;
        [SerializeField, HideInInspector]
        private PawnUIManager _pawnUiManager;

        [Header("Player Permission")]
        [SerializeField, Tooltip(
            "ON이면 GM이 Active Actor로 선언한 캐릭터만 Player가 이동할 수 있습니다.")]
        private bool _requireActiveActorForPlayerMovement;

        [Header("Remote Roulette")]
        [SerializeField] private Font _remoteRollFont;
        [SerializeField] private int _remoteRollSortingOrder = 2400;
        [SerializeField, Min(0f)]
        private float _remoteRollHoldSeconds = 2.4f;

        [Header("Diagnostics")]
        [SerializeField] private bool _verboseNetworkLogs = true;

        [Networked, Capacity(MaximumCharacterSlots)]
        public NetworkArray<TRPGPlayerSlotState> PlayerSlots => default;

        [Networked]
        public NetworkString<_64> ActiveActorInstanceId { get; set; }

        [Networked]
        public int StateRevision { get; set; }

        [Networked]
        public int GameplayRevision { get; set; }

        [Networked]
        public NetworkBool GameplayReady { get; set; }

        [Networked]
        public int CampaignRuleSetValue { get; set; }

        private readonly List<TRPGNetworkMovePacket> _pendingMovePackets =
            new List<TRPGNetworkMovePacket>();
        private readonly List<TRPGNetworkStatPacket> _pendingStatPackets =
            new List<TRPGNetworkStatPacket>();
        private readonly List<TRPGNetworkLogPacket> _hostLogHistory =
            new List<TRPGNetworkLogPacket>();
        private readonly HashSet<string> _pendingLocalLogEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<long> _submittedLocalLogSequences =
            new HashSet<long>();

        private PawnMovementManager _movementManager;
        private TRPGNetworkGameManager _gameAdapter;
        private TRPGNetworkLogManager _logAdapter;
        private PawnCheckRollManager _checkRollManager;
        private TRPGRemoteRollSpectatorWidget _spectatorWidget;
        private GameObject _ownedSpectatorCanvas;
        private Coroutine _initializationRoutine;
        private Coroutine _pendingApplyRoutine;
        private bool _spawned;
        private bool _movementEventsBound;
        private bool _logEventsBound;
        private bool _applyingRemoteState;
        private bool _applyingRemoteLog;
        private bool _initialSnapshotRequested;
        private bool _initialSnapshotApplied;
        private bool _localReferencesReady;
        private bool _resolvingLocalReferences;
        private int _lastRenderedStateRevision = int.MinValue;
        private int _lastRenderedGameplayRevision = int.MinValue;

        public static TRPGSessionAuthority Instance => _instance;

        /// <summary>
        /// PawnRollLogService에서 생성된 채팅/행동 로그를 네트워크에 전달합니다.
        /// 굴림은 정확한 룰렛 범위를 보존하기 위해 PublishRoll을 사용합니다.
        /// </summary>
        public static void PublishLogEntryFromService(
            PawnRollLogEntry entry)
        {
            _instance?.SubmitLocalLogEntry(entry);
        }

        public static bool PublishRoll(
            InteractivePawn pawn,
            PawnRollLogKind kind,
            in PawnRollWindowData data,
            bool animate = true)
        {
            return _instance != null &&
                   _instance.PublishRollInternal(
                       pawn,
                       kind,
                       data.Title,
                       data.Expression,
                       data.FinalValue,
                       data.MinimumValue,
                       data.MaximumValue,
                       data.ResultLabel,
                       data.DetailLabel,
                       data.ResultColor,
                       data.DurationSeconds,
                       data.ResultTone,
                       animate);
        }

        public static bool PublishRoll(
            InteractivePawn pawn,
            PawnRollLogKind kind,
            in PawnRollPresentationData data,
            bool animate = true)
        {
            return _instance != null &&
                   _instance.PublishRollInternal(
                       pawn,
                       kind,
                       data.Title,
                       data.Expression,
                       data.FinalValue,
                       data.MinimumValue,
                       data.MaximumValue,
                       data.ResultLabel,
                       data.DetailLabel,
                       data.ResultColor,
                       data.DurationSeconds,
                       ResolveResultTone(data.ResultLabel),
                       animate);
        }

        public event Action<string> LocalMessageChanged;
        public event Action StateChanged;

        public bool IsSpawned => _spawned;

        public bool IsOnline =>
            _spawned &&
            Runner != null &&
            Runner.IsRunning;

        public bool IsGameplayReady =>
            IsOnline &&
            GameplayReady &&
            (Object.HasStateAuthority || _initialSnapshotApplied);

        public bool IsLocalGameMaster =>
            IsOnline && Runner.IsServer;

        public bool ShouldRouteClientMove =>
            IsOnline && !Runner.IsServer;

        public bool ShouldRouteClientStatChange =>
            IsOnline && !Runner.IsServer;

        public bool CanLocalControlCamera =>
            !IsOnline ||
            TRPGNetworkPermissionService.CanAct(
                IsLocalGameMaster,
                GetLocalControlledPawnInstanceId(),
                ActiveActorInstanceId.ToString());

        private void Reset()
        {
            ResolveLocalReferences();
        }

        private void Awake()
        {
            _instance = this;
            TRPGNetworkBootstrap.RegisterAuthority(this);
            ResolveLocalReferences();
        }

        private void OnEnable()
        {
            ResolveLocalReferences();
        }

        private void OnDisable()
        {
            StopInitializationRoutine();
            StopPendingApplyRoutine();
            UnbindMovementEvents();
            UnbindLogEvents();
        }

        private void OnDestroy()
        {
            StopInitializationRoutine();
            StopPendingApplyRoutine();
            UnbindMovementEvents();
            UnbindLogEvents();

            if (_ownedSpectatorCanvas != null)
                Destroy(_ownedSpectatorCanvas);

            TRPGNetworkBootstrap.UnregisterAuthority(this);

            if (_instance == this)
                _instance = null;

            LocalMessageChanged = null;
            StateChanged = null;
        }

        public override void Spawned()
        {
            _instance = this;
            _spawned = true;
            _initialSnapshotRequested = false;
            _initialSnapshotApplied = Object.HasStateAuthority;
            _localReferencesReady = false;
            TRPGNetworkBootstrap.RegisterAuthority(this);
            ResolveLocalReferences();
            if (Object.HasStateAuthority && _pawnUiManager != null)
            {
                CampaignRuleSetValue = (int)_pawnUiManager.RuleSet;
            }
            BindLogEvents();
            BeginInitialization();

            Debug.Log(
                $"[TRPGSessionAuthority] Spawned · " +
                $"Host={Runner.IsServer} · " +
                $"StateAuthority={Object.HasStateAuthority}",
                this);

            PublishStateChanged();
        }

        public override void Despawned(
            NetworkRunner runner,
            bool hasState)
        {
            _spawned = false;
            _initialSnapshotRequested = false;
            _initialSnapshotApplied = false;
            _localReferencesReady = false;
            StopInitializationRoutine();
            StopPendingApplyRoutine();
            UnbindMovementEvents();
            UnbindLogEvents();
            _spectatorWidget?.Clear();
            PublishStateChanged();
        }

        public override void Render()
        {
            if (!_spawned)
                return;

            var stateChanged =
                StateRevision != _lastRenderedStateRevision;
            var gameplayChanged =
                GameplayRevision != _lastRenderedGameplayRevision;

            if (stateChanged)
                _lastRenderedStateRevision = StateRevision;
            if (gameplayChanged)
                _lastRenderedGameplayRevision = GameplayRevision;

            if (stateChanged || gameplayChanged)
                PublishStateChanged();

            ApplyNetworkCampaignRuleSet();

            if (!Object.HasStateAuthority &&
                GameplayReady &&
                _localReferencesReady &&
                !_initialSnapshotRequested)
            {
                _initialSnapshotRequested = true;
                PawnRollLogService.ClearAll();
                RPC_RequestGameplaySnapshot();
            }
        }

        public void PublishHostCampaignRuleSet(
            CampaignRuleSet ruleSet)
        {
            if (!IsLocalGameMaster ||
                !Object.HasStateAuthority)
            {
                return;
            }

            CampaignRuleSetValue = (int)ruleSet;
            GameplayRevision++;
        }

        private void ApplyNetworkCampaignRuleSet()
        {
            if (_pawnUiManager == null ||
                !Enum.IsDefined(
                    typeof(CampaignRuleSet),
                    CampaignRuleSetValue))
            {
                return;
            }

            var ruleSet = (CampaignRuleSet)CampaignRuleSetValue;
            if (_pawnUiManager.RuleSet != ruleSet)
                _pawnUiManager.ApplyCampaignRuleSet(ruleSet);
        }

        public void ConfigureGameplayReferences(
            PawnManager pawnManager,
            PawnUIManager pawnUiManager)
        {
            if (pawnManager != null)
                _pawnManager = pawnManager;
            if (pawnUiManager != null)
                _pawnUiManager = pawnUiManager;

            ResolveLocalReferences();
            BindMovementEvents();
        }

        public void ConfigureLogPresentation(
            PawnManager pawnManager,
            Font uiFont,
            int sortingOrder,
            float resultHoldSeconds)
        {
            if (pawnManager != null)
                _pawnManager = pawnManager;
            if (uiFont != null)
                _remoteRollFont = uiFont;

            _remoteRollSortingOrder = sortingOrder;
            _remoteRollHoldSeconds = Mathf.Max(
                0f,
                resultHoldSeconds);

            ResolveLocalReferences();
        }

        public void RequestLocalCharacterClaim(
            string definitionId)
        {
            if (!IsOnline)
            {
                PublishLocalMessage(
                    "네트워크 세션이 실행 중이 아닙니다.");
                return;
            }

            if (!IsGameplayReady)
            {
                PublishLocalMessage(
                    "게임 상태 스냅숏 적용이 아직 완료되지 않았습니다.");
                return;
            }

            var normalized = NormalizeId(definitionId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                PublishLocalMessage(
                    "PawnDefinition.Id를 입력하십시오.");
                return;
            }

            RPC_RequestCharacterClaim(normalized);
        }

        public bool HostDeclareActiveByDefinitionId(
            string definitionId)
        {
            if (!IsLocalGameMaster ||
                !Object.HasStateAuthority)
            {
                PublishLocalMessage(
                    "GM Host만 활성 캐릭터를 선언할 수 있습니다.");
                return false;
            }

            var normalized = NormalizeId(definitionId);

            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                if (!string.Equals(
                        slot.DefinitionId.ToString(),
                        normalized,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                ActiveActorInstanceId = slot.PawnInstanceId;
                StateRevision++;
                RPC_PublishSystemMessage(
                    Trim(
                        $"활성 캐릭터: {slot.DefinitionId}",
                        32));
                return true;
            }

            PublishLocalMessage(
                $"'{normalized}'에 해당하는 Player Pawn이 없습니다.");
            return false;
        }

        public void HostReleasePlayer(PlayerRef player)
        {
            if (!Object.HasStateAuthority ||
                player == PlayerRef.None)
            {
                return;
            }

            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                if (!slot.IsClaimed || slot.ClaimedBy != player)
                    continue;

                var releasedPawnId =
                    slot.PawnInstanceId.ToString();
                slot.IsClaimed = false;
                slot.ClaimedBy = PlayerRef.None;
                PlayerSlots.Set(index, slot);

                if (string.Equals(
                        ActiveActorInstanceId.ToString(),
                        releasedPawnId,
                        StringComparison.Ordinal))
                {
                    ActiveActorInstanceId = default;
                }

                StateRevision++;
                RPC_PublishSystemMessage(
                    Trim(
                        $"Player {player.PlayerId}의 " +
                        "캐릭터 점유가 해제되었습니다.",
                        32));
                return;
            }
        }

        public bool RequestMove(
            InteractivePawn pawn,
            Vector2 requestedDestination)
        {
            if (!ShouldRouteClientMove ||
                !IsGameplayReady ||
                pawn == null ||
                !pawn.IsMoveable ||
                pawn.Definition == null)
            {
                PublishLocalMessage(
                    "이동 요청을 보낼 수 없는 네트워크 상태입니다.");
                return false;
            }

            var definitionId =
                NormalizeId(pawn.Definition.Id);
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                PublishLocalMessage(
                    "이동 Pawn의 Definition.Id가 비어 있습니다.");
                return false;
            }

            LogVerbose(
                $"Move INPUT · Pawn={definitionId} · " +
                $"Destination={requestedDestination}");
            RPC_RequestMove(
                definitionId,
                requestedDestination);
            return true;
        }

        public bool RequestStatChange(
            InteractivePawn pawn,
            string statId,
            double requestedValue)
        {
            if (!ShouldRouteClientStatChange ||
                !IsGameplayReady ||
                pawn == null ||
                pawn.Definition == null ||
                string.IsNullOrWhiteSpace(statId) ||
                double.IsNaN(requestedValue) ||
                double.IsInfinity(requestedValue))
            {
                PublishLocalMessage(
                    "스탯 변경 요청을 보낼 수 없는 상태입니다.");
                return false;
            }

            var definitionId =
                NormalizeId(pawn.Definition.Id);
            var normalizedStatId = NormalizeId(statId);

            LogVerbose(
                $"Stat INPUT · Pawn={definitionId} · " +
                $"Stat={normalizedStatId} · Value={requestedValue}");
            RPC_RequestStatChange(
                Trim(definitionId, 32),
                Trim(normalizedStatId, 32),
                requestedValue);
            return true;
        }

        public void PublishHostStatChange(
            InteractivePawn pawn,
            string statId,
            double previousValue,
            double currentValue)
        {
            if (!IsLocalGameMaster ||
                _applyingRemoteState ||
                pawn == null ||
                !pawn.HasStats ||
                pawn.Definition == null ||
                string.IsNullOrWhiteSpace(statId))
            {
                return;
            }

            var packet = new TRPGNetworkStatPacket
            {
                PawnDefinitionId = Trim(
                    NormalizeId(pawn.Definition.Id),
                    32),
                StatId = Trim(
                    NormalizeId(statId),
                    32),
                PreviousValue = previousValue,
                CurrentValue = currentValue,
                IsSnapshot = false
            };

            GameplayRevision++;
            RPC_ApplyStat(packet);
            RecordStatLog(
                pawn,
                statId,
                previousValue,
                currentValue);
            PublishMovementBudgetSnapshot(pawn);
        }

        public bool CanLocalMovePawn(InteractivePawn pawn)
        {
            return pawn != null &&
                   pawn.IsMoveable &&
                   CanLocalActOnPawn(pawn);
        }

        public bool CanLocalRollPawn(InteractivePawn pawn)
        {
            return pawn != null &&
                   pawn.HasStats &&
                   !pawn.IsDead &&
                   CanLocalActOnPawn(pawn);
        }

        public bool CanLocalViewFullCharacter(
            InteractivePawn pawn)
        {
            if (pawn == null ||
                pawn.Definition == null ||
                !pawn.HasFullCharacterSheet)
            {
                return false;
            }

            if (!IsOnline || IsLocalGameMaster)
                return true;

            return string.Equals(
                GetLocalControlledDefinitionId(),
                pawn.Definition.Id,
                StringComparison.Ordinal);
        }

        public bool TryGetLocalControlledPawn(
            out InteractivePawn pawn)
        {
            pawn = null;
            ResolveLocalReferences();
            if (_pawnManager == null)
                return false;

            var controlledDefinitionId =
                GetLocalControlledDefinitionId();
            var controlledInstanceId =
                GetLocalControlledPawnInstanceId();
            var players = _pawnManager.PlayerPawns;

            for (var index = 0;
                 index < players.Count;
                 index++)
            {
                var candidate = players[index];
                if (candidate == null)
                    continue;

                if (candidate.Definition != null &&
                    !string.IsNullOrWhiteSpace(
                        controlledDefinitionId) &&
                    string.Equals(
                        candidate.Definition.Id,
                        controlledDefinitionId,
                        StringComparison.Ordinal))
                {
                    pawn = candidate;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(
                        controlledInstanceId) &&
                    string.Equals(
                        candidate.InstanceId,
                        controlledInstanceId,
                        StringComparison.Ordinal))
                {
                    pawn = candidate;
                    return true;
                }
            }

            return false;
        }

        public string GetLocalControlledPawnInstanceId()
        {
            if (!IsOnline)
                return string.Empty;

            var localPlayer = Runner.LocalPlayer;
            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                if (slot.IsClaimed &&
                    slot.ClaimedBy == localPlayer)
                {
                    return slot.PawnInstanceId.ToString();
                }
            }

            return string.Empty;
        }

        public string GetLocalControlledDefinitionId()
        {
            if (!IsOnline || IsLocalGameMaster)
                return string.Empty;

            var localPlayer = Runner.LocalPlayer;
            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                if (slot.IsClaimed &&
                    slot.ClaimedBy == localPlayer)
                {
                    return slot.DefinitionId.ToString();
                }
            }

            return string.Empty;
        }

        public string GetGameplayReadinessLabel()
        {
            if (!IsOnline)
                return "Transport 연결 대기";

            if (!GameplayReady)
                return "Host 게임 상태 초기화 대기";

            if (Object.HasStateAuthority)
                return "Host 게임 상태 준비 완료";

            if (!_localReferencesReady)
                return "Client 로컬 Manager 연결 대기";

            if (!_initialSnapshotRequested)
                return "초기 스냅숏 요청 대기";

            if (!_initialSnapshotApplied)
                return "초기 스냅숏 수신 중";

            return "게임 상태 준비 완료";
        }

        public string GetLocalStateLabel()
        {
            if (!IsOnline)
                return "네트워크 상태: Offline";

            var role = IsLocalGameMaster
                ? "GM Host"
                : "Player Client";
            var controlled =
                GetLocalControlledPawnInstanceId();
            var active =
                ActiveActorInstanceId.ToString();

            return $"역할: {role}\n" +
                   $"Gameplay: {(GameplayReady ? "Ready" : "Waiting")}\n" +
                   $"내 Pawn: " +
                   $"{(string.IsNullOrEmpty(controlled) ? "미점유" : controlled)}\n" +
                   $"Active: " +
                   $"{(string.IsNullOrEmpty(active) ? "미선언" : active)}";
        }

        public string GetSlotSummary()
        {
            if (!_spawned)
                return "슬롯 상태: 아직 생성되지 않음";

            var builder = new StringBuilder("슬롯");
            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                var definitionId =
                    slot.DefinitionId.ToString();
                if (string.IsNullOrWhiteSpace(definitionId))
                    continue;

                builder.Append("\n");
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(definitionId);
                builder.Append(slot.IsClaimed
                    ? $" · Player {slot.ClaimedBy.PlayerId}"
                    : " · 비어 있음");
            }

            return builder.ToString();
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestCharacterClaim(
            NetworkString<_64> requestedDefinitionId,
            RpcInfo info = default)
        {
            var source = info.Source;
            var requestedId = NormalizeId(
                requestedDefinitionId.ToString());

            if (source == PlayerRef.None)
                return;

            if (Runner.IsServer &&
                source == Runner.LocalPlayer)
            {
                RPC_ClaimResult(
                    source,
                    false,
                    Trim(
                        "GM Host는 Player 캐릭터 슬롯을 점유하지 않습니다.",
                        32));
                return;
            }

            var requestedIndex = -1;
            var sourceAlreadyOwnsSlot = false;

            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                if (slot.IsClaimed &&
                    slot.ClaimedBy == source)
                {
                    sourceAlreadyOwnsSlot = true;
                }

                if (string.Equals(
                        slot.DefinitionId.ToString(),
                        requestedId,
                        StringComparison.Ordinal))
                {
                    requestedIndex = index;
                }
            }

            if (sourceAlreadyOwnsSlot)
            {
                RPC_ClaimResult(
                    source,
                    false,
                    Trim(
                        "이미 다른 캐릭터를 점유하고 있습니다.",
                        32));
                return;
            }

            if (requestedIndex < 0)
            {
                RPC_ClaimResult(
                    source,
                    false,
                    Trim(
                        "해당 PawnDefinition.Id의 Player Pawn이 없습니다.",
                        32));
                return;
            }

            var requestedSlot = PlayerSlots[requestedIndex];
            if (requestedSlot.IsClaimed)
            {
                RPC_ClaimResult(
                    source,
                    false,
                    Trim(
                        "이미 다른 플레이어가 사용 중인 캐릭터입니다.",
                        32));
                return;
            }

            requestedSlot.IsClaimed = true;
            requestedSlot.ClaimedBy = source;
            PlayerSlots.Set(requestedIndex, requestedSlot);
            StateRevision++;

            RPC_ClaimResult(
                source,
                true,
                Trim(
                    $"캐릭터 점유 완료: " +
                    $"{requestedSlot.DefinitionId}",
                    32));
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestMove(
            NetworkString<_64> pawnDefinitionId,
            Vector2 requestedDestination,
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
                return;

            LogVerbose(
                $"Move RPC RECEIVED · Source={info.Source.PlayerId} · " +
                $"Pawn={pawnDefinitionId} · " +
                $"Destination={requestedDestination}");

            if (!TryAuthorizePlayerPawn(
                    info.Source,
                    pawnDefinitionId.ToString(),
                    _requireActiveActorForPlayerMovement,
                    out var pawn,
                    out var reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "이동",
                    reason);
                return;
            }

            ResolveLocalReferences();
            if (_movementManager == null ||
                !_movementManager.TryMovePawnTo(
                    pawn,
                    requestedDestination,
                    out _))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "이동",
                    "경로가 없거나 남은 이동 거리를 초과했습니다.");
                return;
            }

            SendCommandResult(
                info.Source,
                true,
                "이동",
                "Host가 이동을 승인했습니다.");
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestStatChange(
            NetworkString<_32> pawnDefinitionId,
            NetworkString<_32> statId,
            double requestedValue,
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
                return;

            LogVerbose(
                $"Stat RPC RECEIVED · Source={info.Source.PlayerId} · " +
                $"Pawn={pawnDefinitionId} · Stat={statId} · " +
                $"Value={requestedValue}");

            if (double.IsNaN(requestedValue) ||
                double.IsInfinity(requestedValue))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "스탯 변경",
                    "유효하지 않은 숫자입니다.");
                return;
            }

            if (!TryAuthorizePlayerPawn(
                    info.Source,
                    pawnDefinitionId.ToString(),
                    false,
                    out var pawn,
                    out var reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "스탯 변경",
                    reason);
                return;
            }

            var state = ResolveStatState(pawn);
            var normalizedStatId = statId.ToString();

            if (info.Source != PlayerRef.None &&
                info.Source != Runner.LocalPlayer &&
                !CanPlayerEditStat(
                    state,
                    normalizedStatId,
                    out var statPermissionReason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "스탯 변경",
                    statPermissionReason);
                return;
            }

            if (state?.Runtime == null ||
                !TryGetStatNumber(
                    state,
                    normalizedStatId,
                    out var previousValue) ||
                !state.TrySetAuthoritativeDisplayedValue(
                    normalizedStatId,
                    requestedValue) ||
                !TryGetStatNumber(
                    state,
                    normalizedStatId,
                    out var currentValue))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "스탯 변경",
                    $"변경 가능한 스탯이 아닙니다: {normalizedStatId}");
                return;
            }

            var packet = new TRPGNetworkStatPacket
            {
                PawnDefinitionId = Trim(
                    NormalizeId(pawn.Definition.Id),
                    32),
                StatId = Trim(
                    NormalizeId(normalizedStatId),
                    32),
                PreviousValue = previousValue,
                CurrentValue = currentValue,
                IsSnapshot = false
            };

            GameplayRevision++;
            RPC_ApplyStat(packet);
            RecordStatLog(
                pawn,
                normalizedStatId,
                previousValue,
                currentValue);
            PublishMovementBudgetSnapshot(pawn);

            SendCommandResult(
                info.Source,
                true,
                "스탯 변경",
                $"{normalizedStatId} = {currentValue:0.##}");
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyMove(TRPGNetworkMovePacket packet)
        {
            if (Object.HasStateAuthority)
                return;

            LogVerbose(
                $"Move RPC APPLY · Pawn={packet.PawnDefinitionId} · " +
                $"Mode={packet.Mode} · Destination={packet.Destination}");

            if (!TryApplyMovePacket(packet))
                QueueMovePacket(packet);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyStat(TRPGNetworkStatPacket packet)
        {
            if (Object.HasStateAuthority)
                return;

            LogVerbose(
                $"Stat RPC APPLY · Pawn={packet.PawnDefinitionId} · " +
                $"Stat={packet.StatId} · Value={packet.CurrentValue}");

            if (!TryApplyStatPacket(packet))
                QueueStatPacket(packet);
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestGameplaySnapshot(
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority ||
                info.Source == PlayerRef.None ||
                !GameplayReady)
            {
                return;
            }

            LogVerbose(
                $"Snapshot REQUEST · Source={info.Source.PlayerId}");
            SendGameplaySnapshotTo(info.Source);
            SendLogHistoryTo(info.Source);
            RPC_CompleteGameplaySnapshot(
                info.Source,
                GameplayRevision);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_CompleteGameplaySnapshot(
            [RpcTarget] PlayerRef target,
            int revision)
        {
            _initialSnapshotApplied = true;
            LogVerbose(
                $"Snapshot COMPLETE · Revision={revision}");
            PublishLocalMessage(
                "게임 상태 동기화가 완료되었습니다.");
            PublishStateChanged();
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyMoveSnapshot(
            [RpcTarget] PlayerRef target,
            TRPGNetworkMovePacket packet)
        {
            if (!TryApplyMovePacket(packet))
                QueueMovePacket(packet);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyStatSnapshot(
            [RpcTarget] PlayerRef target,
            TRPGNetworkStatPacket packet)
        {
            if (!TryApplyStatPacket(packet))
                QueueStatPacket(packet);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_CommandResult(
            [RpcTarget] PlayerRef target,
            NetworkBool success,
            NetworkString<_32> message)
        {
            var text = message.ToString();
            PublishLocalMessage(text);

            if (success)
                LogVerbose(text);
            else
                Debug.LogWarning($"[TRPG Network] {text}", this);
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_SubmitLog(
            TRPGNetworkLogPacket packet,
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
                return;

            if (!TryCanonicalizeClientLog(
                    info.Source,
                    ref packet,
                    out var reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "로그",
                    reason);
                return;
            }

            LogVerbose(
                $"Log RPC RECEIVED · Source={info.Source.PlayerId} · " +
                $"Kind={(PawnRollLogKind)packet.Kind} · " +
                $"Title={packet.Title}");

            ApplyRemoteLog(packet, true);
            AddHostHistory(packet);
            if (IsSecretPacket(packet))
            {
                if (info.Source != PlayerRef.None &&
                    info.Source != Runner.LocalPlayer)
                {
                    RPC_PrivateLog(info.Source, packet);
                }
            }
            else
            {
                RPC_BroadcastLog(packet);
            }
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_BroadcastLog(TRPGNetworkLogPacket packet)
        {
            if (Object.HasStateAuthority)
                return;

            var eventId = packet.EventId.ToString();
            if (_pendingLocalLogEventIds.Remove(eventId))
                return;

            ApplyRemoteLog(packet, true);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_PrivateLog(
            [RpcTarget] PlayerRef target,
            TRPGNetworkLogPacket packet)
        {
            if (Object.HasStateAuthority)
                return;

            var eventId = packet.EventId.ToString();
            if (_pendingLocalLogEventIds.Remove(eventId))
                return;

            ApplyRemoteLog(packet, true);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyHistoryEntry(
            [RpcTarget] PlayerRef target,
            TRPGNetworkLogPacket packet)
        {
            ApplyRemoteLog(packet, false);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ClaimResult(
            [RpcTarget] PlayerRef target,
            NetworkBool success,
            NetworkString<_32> message)
        {
            PublishLocalMessage(message.ToString());
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_PublishSystemMessage(
            NetworkString<_32> message)
        {
            PublishLocalMessage(message.ToString());
        }

        private void BeginInitialization()
        {
            StopInitializationRoutine();
            _initializationRoutine = StartCoroutine(
                InitializeWhenReady());
        }

        private IEnumerator InitializeWhenReady()
        {
            for (var frame = 0;
                 frame < InitializationFrameLimit;
                 frame++)
            {
                ResolveLocalReferences();

                var localReady =
                    _pawnManager != null &&
                    _pawnUiManager != null &&
                    _movementManager != null &&
                    _pawnManager.InteractivePawns != null &&
                    _pawnManager.InteractivePawns.Count > 0;

                if (!localReady)
                {
                    yield return null;
                    continue;
                }

                _localReferencesReady = true;

                if (Object.HasStateAuthority)
                {
                    HostInitializeSlots();
                    BindMovementEvents();
                    GameplayReady = true;
                    GameplayRevision++;
                    LogVerbose("Gameplay READY · Host initialized");
                }
                else
                {
                    LogVerbose("Local references READY · waiting Host state");
                }

                _initializationRoutine = null;
                PublishStateChanged();
                yield break;
            }

            Debug.LogError(
                "[TRPGSessionAuthority] 게임 동기화 초기화 실패. " +
                $"PawnManager={_pawnManager != null}, " +
                $"PawnUIManager={_pawnUiManager != null}, " +
                $"MovementManager={_movementManager != null}, " +
                $"PawnCount={(_pawnManager != null ? _pawnManager.InteractivePawns.Count : 0)}",
                this);

            if (Object.HasStateAuthority)
            {
                GameplayReady = false;
                GameplayRevision++;
            }

            _initializationRoutine = null;
            PublishStateChanged();
        }

        private void StopInitializationRoutine()
        {
            if (_initializationRoutine == null)
                return;

            StopCoroutine(_initializationRoutine);
            _initializationRoutine = null;
        }

        private void ResolveLocalReferences()
        {
            if (_resolvingLocalReferences)
                return;

            _resolvingLocalReferences = true;
            try
            {
                if (_pawnManager == null)
                    _pawnManager = FindFirst<PawnManager>();
                if (_pawnUiManager == null)
                    _pawnUiManager = FindFirst<PawnUIManager>();
                if (_checkRollManager == null)
                    _checkRollManager = FindFirst<PawnCheckRollManager>();

                _movementManager = _pawnManager != null
                    ? _pawnManager.MovementManager
                    : null;

                if (_gameAdapter == null)
                {
                    _gameAdapter =
                        GetComponent<TRPGNetworkGameManager>();
                    if (_gameAdapter == null)
                    {
                        _gameAdapter = gameObject.AddComponent<
                            TRPGNetworkGameManager>();
                    }
                }

                if (_logAdapter == null)
                {
                    _logAdapter =
                        GetComponent<TRPGNetworkLogManager>();
                    if (_logAdapter == null)
                    {
                        _logAdapter = gameObject.AddComponent<
                            TRPGNetworkLogManager>();
                    }
                }

                _pawnManager?.ConfigureNetworkManager(
                    _gameAdapter);
                _pawnUiManager?.ConfigureNetworkManager(
                    _gameAdapter);
            }
            finally
            {
                _resolvingLocalReferences = false;
            }
        }

        private void BindMovementEvents()
        {
            if (!_spawned ||
                Object == null ||
                !Object.HasStateAuthority ||
                _movementEventsBound ||
                _movementManager == null)
            {
                return;
            }

            _movementManager.MovementCommitted +=
                HandleMovementCommitted;
            _movementManager.DoorTransferred +=
                HandleDoorTransferred;
            _movementEventsBound = true;
        }

        private void UnbindMovementEvents()
        {
            if (!_movementEventsBound ||
                _movementManager == null)
            {
                return;
            }

            _movementManager.MovementCommitted -=
                HandleMovementCommitted;
            _movementManager.DoorTransferred -=
                HandleDoorTransferred;
            _movementEventsBound = false;
        }

        private void HandleMovementCommitted(
            PawnMovementCommitData commit)
        {
            if (!IsLocalGameMaster ||
                _applyingRemoteState ||
                commit.Pawn == null ||
                commit.Pawn.Definition == null)
            {
                return;
            }

            var packet = CreateMovePacket(commit);
            GameplayRevision++;
            RPC_ApplyMove(packet);

            PawnRollLogService.RecordAction(
                commit.Pawn,
                "이동",
                $"{commit.MoveCost:0.0}m 이동 / " +
                $"남은 이동 {commit.RemainingMeters:0.0}m");
        }

        private void HandleDoorTransferred(
            InteractivePawn pawn,
            Vector2 destination)
        {
            if (!IsLocalGameMaster ||
                _applyingRemoteState ||
                pawn == null ||
                pawn.Definition == null)
            {
                return;
            }

            var packet = new TRPGNetworkMovePacket
            {
                PawnDefinitionId = NormalizeId(pawn.Definition.Id),
                Destination = destination,
                Mode = 1
            };

            GameplayRevision++;
            RPC_ApplyMove(packet);

            PawnRollLogService.RecordAction(
                pawn,
                "문 이동",
                $"목적지 ({destination.x:0.0}, " +
                $"{destination.y:0.0})");
        }

        private void BindLogEvents()
        {
            if (_logEventsBound)
                return;

            PawnRollLogService.EntryAdded +=
                HandleLocalLogEntryAdded;
            _logEventsBound = true;
        }

        private void UnbindLogEvents()
        {
            if (!_logEventsBound)
                return;

            PawnRollLogService.EntryAdded -=
                HandleLocalLogEntryAdded;
            _logEventsBound = false;
        }

        private void HandleLocalLogEntryAdded(
            PawnRollLogEntry entry)
        {
            SubmitLocalLogEntry(entry);
        }

        private void SubmitLocalLogEntry(
            PawnRollLogEntry entry)
        {
            if (_applyingRemoteLog ||
                PawnRollLogService.IsRestoringSnapshot ||
                !IsGameplayReady ||
                entry.Sequence <= 0)
            {
                return;
            }

            if (_submittedLocalLogSequences.Count >=
                MaximumPendingPackets)
            {
                _submittedLocalLogSequences.Clear();
            }

            if (!_submittedLocalLogSequences.Add(entry.Sequence))
                return;

            var packet = CreateLogPacket(
                entry,
                IsRollLogKind(entry.Kind));
            PublishLocalLogPacket(packet);
        }

        private bool PublishRollInternal(
            InteractivePawn pawn,
            PawnRollLogKind kind,
            string title,
            string expression,
            int finalValue,
            int minimumValue,
            int maximumValue,
            string result,
            string detail,
            Color resultColor,
            float durationSeconds,
            PawnRollResultTone resultTone,
            bool animate)
        {
            // v7부터 PawnRollLogService.EntryAdded가 모든 굴림의
            // 단일 네트워크 진입점입니다. 기존 호출부 호환을 위해
            // 이 메서드는 유효성만 확인하고 중복 패킷을 보내지 않습니다.
            return !_applyingRemoteLog &&
                   IsGameplayReady &&
                   pawn != null &&
                   pawn.Definition != null &&
                   IsRollLogKind(kind);
        }

        private void PublishLocalLogPacket(
            TRPGNetworkLogPacket packet)
        {
            if (Object.HasStateAuthority)
            {
                AddHostHistory(packet);
                if (!IsSecretPacket(packet))
                    RPC_BroadcastLog(packet);
                return;
            }

            var eventId = packet.EventId.ToString();
            if (_pendingLocalLogEventIds.Count >=
                MaximumPendingPackets)
            {
                _pendingLocalLogEventIds.Clear();
            }

            _pendingLocalLogEventIds.Add(eventId);
            RPC_SubmitLog(packet);
        }

        private void HostInitializeSlots()
        {
            PlayerSlots.Clear();
            ActiveActorInstanceId = default;

            ResolveLocalReferences();
            if (_pawnManager == null)
            {
                Debug.LogError(
                    $"[{name}] PawnManager가 연결되지 않았습니다.",
                    this);
                StateRevision++;
                return;
            }

            var players = _pawnManager.PlayerPawns;
            var count = Mathf.Min(
                MaximumCharacterSlots,
                players.Count);

            for (var index = 0; index < count; index++)
            {
                var pawn = players[index];
                if (pawn == null || pawn.Definition == null)
                    continue;

                var definitionId =
                    NormalizeId(pawn.Definition.Id);
                var instanceId =
                    NormalizeId(pawn.InstanceId);

                if (string.IsNullOrWhiteSpace(definitionId) ||
                    string.IsNullOrWhiteSpace(instanceId))
                {
                    Debug.LogError(
                        $"[{pawn.name}] Definition.Id 또는 " +
                        "InstanceId가 비어 있습니다.",
                        pawn);
                    continue;
                }

                PlayerSlots.Set(
                    index,
                    new TRPGPlayerSlotState
                    {
                        DefinitionId = definitionId,
                        PawnInstanceId = instanceId,
                        ClaimedBy = PlayerRef.None,
                        IsClaimed = false
                    });
            }

            if (players.Count > MaximumCharacterSlots)
            {
                Debug.LogWarning(
                    $"Player Pawn이 {players.Count}개입니다. " +
                    $"첫 {MaximumCharacterSlots}개만 등록합니다.",
                    this);
            }

            StateRevision++;
        }

        private void SendGameplaySnapshotTo(PlayerRef target)
        {
            ResolveLocalReferences();
            if (_pawnManager == null ||
                _movementManager == null)
            {
                return;
            }

            var pawns = _pawnManager.InteractivePawns;
            for (var index = 0; index < pawns.Count; index++)
            {
                var pawn = pawns[index];
                if (pawn == null || pawn.Definition == null)
                    continue;

                if (pawn.Definition != null &&
                    pawn.Definition.CanMove)
                {
                    _movementManager.TryGetMovementPosition(
                        pawn,
                        out var position);
                    _movementManager.TryGetMovementBudget(
                        pawn,
                        out var remaining,
                        out var maximum);

                    var movePacket = new TRPGNetworkMovePacket
                    {
                        PawnDefinitionId = NormalizeId(
                            pawn.Definition.Id),
                        Destination = position,
                        RemainingMeters = remaining,
                        MaximumMeters = maximum,
                        Mode = 2
                    };
                    RPC_ApplyMoveSnapshot(target, movePacket);
                }

                SendStatSnapshotTo(target, pawn);
                SendInventorySnapshotTo(target, pawn);
                SendProfileSnapshotTo(target, pawn);
                SendSkillSnapshotTo(target, pawn);
                SendSanityStateSnapshotTo(target, pawn);
            }

            SendHandoutSnapshotTo(target);
        }

        private void SendStatSnapshotTo(
            PlayerRef target,
            InteractivePawn pawn)
        {
            if (pawn == null)
                return;

            SendPawnRuntimeStateSnapshotTo(target, pawn);
            if (!pawn.HasStats)
                return;

            var state = ResolveStatState(pawn);
            var runtime = state?.Runtime;
            var stats = runtime?.Template?.Stats;
            if (stats == null)
                return;

            for (var index = 0; index < stats.Count; index++)
            {
                var definition = stats[index];
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.Id))
                {
                    continue;
                }

                if (definition.Source != StatValueSource.Base &&
                    definition.Source != StatValueSource.Runtime)
                {
                    continue;
                }

                if (!TryGetStatNumber(
                        state,
                        definition.Id,
                        out var value))
                {
                    continue;
                }

                var packet = new TRPGNetworkStatPacket
                {
                    PawnDefinitionId = NormalizeId(
                        pawn.Definition.Id),
                    StatId = NormalizeId(definition.Id),
                    PreviousValue = value,
                    CurrentValue = value,
                    IsSnapshot = true
                };
                RPC_ApplyStatSnapshot(target, packet);
            }
        }

        public void PublishHostRollLogSnapshot()
        {
            if (!IsLocalGameMaster ||
                !Object.HasStateAuthority ||
                !IsGameplayReady)
            {
                return;
            }

            _hostLogHistory.Clear();
            RPC_ResetRollLog();

            var entries = PawnRollLogService.Entries;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var packet = CreateLogPacket(entry, false);

                if (IsSecretPacket(packet) &&
                    TryGetControllingPlayer(entry.Pawn, out var target))
                {
                    packet.Roller = target;
                    AddHostHistory(packet);
                    RPC_PrivateLog(target, packet);
                    continue;
                }

                // GM 전용 NPC 비밀 굴림은 다른 클라이언트에 보내지 않습니다.
                AddHostHistory(packet);
                if (!IsSecretPacket(packet))
                    RPC_BroadcastLog(packet);
            }
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ResetRollLog()
        {
            if (Object.HasStateAuthority)
                return;

            _applyingRemoteLog = true;
            try
            {
                PawnRollLogService.ClearAll();
            }
            finally
            {
                _applyingRemoteLog = false;
            }
        }

        private void SendLogHistoryTo(PlayerRef target)
        {
            for (var index = 0;
                 index < _hostLogHistory.Count;
                 index++)
            {
                var packet = _hostLogHistory[index];
                if (IsSecretPacket(packet) && packet.Roller != target)
                    continue;
                packet.ShowRoulette = false;
                RPC_ApplyHistoryEntry(target, packet);
            }
        }

        private bool TryApplyMovePacket(
            TRPGNetworkMovePacket packet)
        {
            ResolveLocalReferences();
            if (!TryResolvePawnByDefinitionId(
                    packet.PawnDefinitionId.ToString(),
                    out var pawn) ||
                _movementManager == null)
            {
                return false;
            }

            _applyingRemoteState = true;
            try
            {
                if (packet.Mode == 1)
                {
                    return _movementManager
                        .ApplyReplicatedDoorTransfer(
                            pawn,
                            packet.Destination);
                }

                if (packet.Mode == 2)
                {
                    return _movementManager
                        .ApplyReplicatedSnapshot(
                            pawn,
                            packet.Destination,
                            packet.RemainingMeters,
                            packet.MaximumMeters);
                }

                var count = Mathf.Clamp(
                    packet.CornerCount,
                    0,
                    MaximumReplicatedCorners);
                var corners = new Vector3[count];
                for (var index = 0; index < count; index++)
                {
                    var corner = packet.GetCorner(index);
                    corners[index] = new Vector3(
                        corner.x,
                        corner.y,
                        0f);
                }

                return _movementManager.ApplyReplicatedMove(
                    pawn,
                    corners,
                    packet.Destination,
                    packet.RemainingMeters,
                    packet.MaximumMeters);
            }
            finally
            {
                _applyingRemoteState = false;
            }
        }

        private bool TryApplyStatPacket(
            TRPGNetworkStatPacket packet)
        {
            ResolveLocalReferences();
            if (!TryResolvePawnByDefinitionId(
                    packet.PawnDefinitionId.ToString(),
                    out var pawn))
            {
                return false;
            }

            if (TryApplyPawnRuntimeStatePacket(packet, pawn))
                return true;

            var state = ResolveStatState(pawn);
            if (state == null)
                return false;

            _applyingRemoteState = true;
            try
            {
                var applied =
                    state.TrySetAuthoritativeDisplayedValue(
                        packet.StatId.ToString(),
                        packet.CurrentValue);

                if (applied && _movementManager != null)
                {
                    _movementManager.RefreshMovementBudgetFromStats(
                        pawn,
                        true);
                }

                return applied;
            }
            finally
            {
                _applyingRemoteState = false;
            }
        }

        private TRPGNetworkMovePacket CreateMovePacket(
            PawnMovementCommitData commit)
        {
            var packet = new TRPGNetworkMovePacket
            {
                PawnDefinitionId = NormalizeId(
                    commit.Pawn.Definition.Id),
                Destination = commit.Destination,
                MoveCost = commit.MoveCost,
                RemainingMeters = commit.RemainingMeters,
                MaximumMeters = commit.MaximumMeters,
                Mode = 0
            };

            var sourceCount = commit.CornerCount;
            var count = Mathf.Clamp(
                sourceCount,
                0,
                MaximumReplicatedCorners);
            packet.CornerCount = count;

            for (var index = 0; index < count; index++)
            {
                var sourceIndex = index;
                if (sourceCount > MaximumReplicatedCorners &&
                    index == MaximumReplicatedCorners - 1)
                {
                    sourceIndex = sourceCount - 1;
                }

                var corner = commit.GetCorner(sourceIndex);
                packet.SetCorner(
                    index,
                    new Vector2(corner.x, corner.y));
            }

            return packet;
        }

        private bool TryAuthorizePlayerPawn(
            PlayerRef source,
            string requestedDefinitionId,
            bool requireActiveActor,
            out InteractivePawn pawn,
            out string reason)
        {
            pawn = null;
            reason = string.Empty;

            if (Runner == null || !Runner.IsServer)
            {
                reason = "Host가 아닙니다.";
                return false;
            }

            if (source == PlayerRef.None ||
                source == Runner.LocalPlayer)
            {
                if (TryResolvePawnByDefinitionId(
                        requestedDefinitionId,
                        out pawn))
                {
                    return true;
                }

                reason = "Host 씬에서 Pawn을 찾지 못했습니다.";
                return false;
            }

            TRPGPlayerSlotState ownedSlot = default;
            var found = false;
            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                if (!slot.IsClaimed ||
                    slot.ClaimedBy != source)
                {
                    continue;
                }

                ownedSlot = slot;
                found = true;
                break;
            }

            if (!found)
            {
                reason = "점유한 캐릭터가 없습니다.";
                return false;
            }

            var ownedDefinitionId =
                ownedSlot.DefinitionId.ToString();
            if (!string.Equals(
                    requestedDefinitionId,
                    ownedDefinitionId,
                    StringComparison.Ordinal))
            {
                reason =
                    "자신이 점유한 캐릭터만 조작할 수 있습니다.";
                return false;
            }

            if (requireActiveActor)
            {
                var activeInstanceId =
                    ActiveActorInstanceId.ToString();
                if (!string.IsNullOrWhiteSpace(activeInstanceId) &&
                    !string.Equals(
                        activeInstanceId,
                        ownedSlot.PawnInstanceId.ToString(),
                        StringComparison.Ordinal))
                {
                    reason = "현재 활성 캐릭터가 아닙니다.";
                    return false;
                }
            }

            if (TryResolvePawnByDefinitionId(
                    ownedDefinitionId,
                    out pawn))
            {
                return true;
            }

            reason =
                "Host 씬에서 점유 캐릭터를 찾지 못했습니다.";
            return false;
        }

        private bool TryCanonicalizeClientLog(
            PlayerRef source,
            ref TRPGNetworkLogPacket packet,
            out string reason)
        {
            reason = string.Empty;

            if (Runner == null || !Runner.IsServer)
            {
                reason = "Host가 아닙니다.";
                return false;
            }

            if (source == PlayerRef.None ||
                source == Runner.LocalPlayer)
            {
                return true;
            }

            packet.Roller = source;
            packet.Visibility = packet.Visibility ==
                (int)RollVisibility.RollerAndGameMaster
                    ? (int)RollVisibility.RollerAndGameMaster
                    : (int)RollVisibility.Public;

            var kind = (PawnRollLogKind)packet.Kind;
            if (kind == PawnRollLogKind.System)
            {
                reason =
                    "Player Client는 System 로그를 직접 만들 수 없습니다.";
                return false;
            }

            TRPGPlayerSlotState ownedSlot = default;
            var found = false;
            for (var index = 0;
                 index < PlayerSlots.Length;
                 index++)
            {
                var slot = PlayerSlots[index];
                if (!slot.IsClaimed ||
                    slot.ClaimedBy != source)
                {
                    continue;
                }

                ownedSlot = slot;
                found = true;
                break;
            }

            if (!found)
            {
                reason = "점유한 캐릭터가 없습니다.";
                return false;
            }

            var ownedDefinitionId =
                ownedSlot.DefinitionId.ToString();
            var requestedDefinitionId =
                packet.PawnDefinitionId.ToString();

            if (kind != PawnRollLogKind.Chat &&
                !string.Equals(
                    requestedDefinitionId,
                    ownedDefinitionId,
                    StringComparison.Ordinal))
            {
                reason =
                    "점유 캐릭터와 로그 소유자가 일치하지 않습니다.";
                return false;
            }

            packet.PawnDefinitionId =
                Trim(
                    NormalizeId(ownedDefinitionId),
                    32);

            if (TryResolvePawnByDefinitionId(
                    ownedDefinitionId,
                    out var pawn))
            {
                packet.PawnName = Trim(
                    ResolvePawnName(pawn),
                    8);
            }

            return true;
        }

        private bool TryResolvePawnByDefinitionId(
            string definitionId,
            out InteractivePawn pawn)
        {
            pawn = null;
            ResolveLocalReferences();
            if (_pawnManager == null ||
                string.IsNullOrWhiteSpace(definitionId))
            {
                return false;
            }

            var pawns = _pawnManager.InteractivePawns;
            for (var index = 0; index < pawns.Count; index++)
            {
                var candidate = pawns[index];
                var definition = candidate != null
                    ? candidate.Definition
                    : null;
                if (definition != null &&
                    string.Equals(
                        definition.Id,
                        definitionId,
                        StringComparison.Ordinal))
                {
                    pawn = candidate;
                    return true;
                }
            }

            return false;
        }

        private static PlayerStatState ResolveStatState(
            InteractivePawn pawn)
        {
            if (pawn == null ||
                !pawn.HasStats ||
                pawn.Definition == null)
            {
                return null;
            }

            return PlayerStatState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
        }

        private static bool CanPlayerEditStat(
            PlayerStatState state,
            string statId,
            out string reason)
        {
            reason = string.Empty;

            if (state?.Runtime == null ||
                string.IsNullOrWhiteSpace(statId) ||
                !state.Runtime.TryGetDefinition(
                    statId,
                    out var definition))
            {
                reason =
                    $"스탯 정의를 찾지 못했습니다: {statId}";
                return false;
            }

            if (definition.Source != StatValueSource.Runtime)
            {
                reason =
                    "플레이어는 기본 능력치가 아니라 " +
                    "자기 캐릭터의 현재 수치만 변경할 수 있습니다.";
                return false;
            }

            var template = state.Runtime.Template;
            var healthId = template.GetStatId(
                StatRole.HealthCurrent);
            var magicId = template.GetStatId(
                StatRole.MagicCurrent);
            var sanityId = template.GetStatId(
                StatRole.SanityCurrent);
            var luckId = template.GetStatId(
                StatRole.LuckCurrent);

            if (string.Equals(statId, healthId, StringComparison.Ordinal) ||
                string.Equals(statId, magicId, StringComparison.Ordinal) ||
                string.Equals(statId, sanityId, StringComparison.Ordinal) ||
                string.Equals(statId, luckId, StringComparison.Ordinal) ||
                definition.IsAdjustable)
            {
                return true;
            }

            reason =
                "플레이어는 자기 캐릭터의 현재 체력·마력·이성·운과 " +
                "명시적으로 조절 가능한 Runtime 수치만 변경할 수 있습니다.";
            return false;
        }

        private static bool TryGetStatNumber(
            PlayerStatState state,
            string statId,
            out double value)
        {
            value = 0d;
            if (state?.Runtime == null ||
                string.IsNullOrWhiteSpace(statId))
            {
                return false;
            }

            try
            {
                value = state.Runtime.GetNumber(statId);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void PublishHostMovementSnapshot(
            InteractivePawn pawn)
        {
            if (!IsLocalGameMaster ||
                _applyingRemoteState ||
                pawn == null)
            {
                return;
            }

            GameplayRevision++;
            PublishMovementBudgetSnapshot(pawn);
        }

        private void PublishMovementBudgetSnapshot(
            InteractivePawn pawn)
        {
            if (!IsLocalGameMaster ||
                pawn == null ||
                pawn.Definition == null ||
                _movementManager == null ||
                !_movementManager.TryGetMovementBudget(
                    pawn,
                    out var remaining,
                    out var maximum) ||
                !_movementManager.TryGetMovementPosition(
                    pawn,
                    out var position))
            {
                return;
            }

            var packet = new TRPGNetworkMovePacket
            {
                PawnDefinitionId = NormalizeId(
                    pawn.Definition.Id),
                Destination = position,
                RemainingMeters = remaining,
                MaximumMeters = maximum,
                Mode = 2
            };
            RPC_ApplyMove(packet);
        }

        private static void RecordStatLog(
            InteractivePawn pawn,
            string statId,
            double previousValue,
            double currentValue)
        {
            PawnRollLogService.RecordAction(
                pawn,
                "스탯 변경",
                $"{statId} {previousValue:0.##} → " +
                $"{currentValue:0.##}");
        }

        private void SendCommandResult(
            PlayerRef target,
            bool success,
            string command,
            string detail)
        {
            var message = success
                ? $"{command} 승인: {detail}"
                : $"{command} 거절: {detail}";

            if (!success)
            {
                Debug.LogWarning(
                    $"[TRPGSessionAuthority] {message}",
                    this);
            }
            else
            {
                LogVerbose(message);
            }

            if (target != PlayerRef.None)
            {
                RPC_CommandResult(
                    target,
                    success,
                    Trim(message, 32));
            }
        }

        private void QueueMovePacket(
            TRPGNetworkMovePacket packet)
        {
            if (_pendingMovePackets.Count >=
                MaximumPendingPackets)
            {
                _pendingMovePackets.RemoveAt(0);
            }

            _pendingMovePackets.Add(packet);
            BeginPendingApplyRoutine();
        }

        private void QueueStatPacket(
            TRPGNetworkStatPacket packet)
        {
            if (_pendingStatPackets.Count >=
                MaximumPendingPackets)
            {
                _pendingStatPackets.RemoveAt(0);
            }

            _pendingStatPackets.Add(packet);
            BeginPendingApplyRoutine();
        }

        private void BeginPendingApplyRoutine()
        {
            if (_pendingApplyRoutine == null &&
                isActiveAndEnabled)
            {
                _pendingApplyRoutine = StartCoroutine(
                    ApplyPendingWhenReady());
            }
        }

        private IEnumerator ApplyPendingWhenReady()
        {
            for (var frame = 0;
                 frame < PendingApplyFrameLimit;
                 frame++)
            {
                ResolveLocalReferences();

                for (var index =
                         _pendingMovePackets.Count - 1;
                     index >= 0;
                     index--)
                {
                    if (TryApplyMovePacket(
                            _pendingMovePackets[index]))
                    {
                        _pendingMovePackets.RemoveAt(index);
                    }
                }

                for (var index =
                         _pendingStatPackets.Count - 1;
                     index >= 0;
                     index--)
                {
                    if (TryApplyStatPacket(
                            _pendingStatPackets[index]))
                    {
                        _pendingStatPackets.RemoveAt(index);
                    }
                }

                if (_pendingMovePackets.Count == 0 &&
                    _pendingStatPackets.Count == 0)
                {
                    _pendingApplyRoutine = null;
                    yield break;
                }

                yield return null;
            }

            Debug.LogError(
                "[TRPGSessionAuthority] 일부 네트워크 상태를 " +
                "로컬 Pawn에 적용하지 못했습니다. " +
                $"Move={_pendingMovePackets.Count}, " +
                $"Stat={_pendingStatPackets.Count}",
                this);
            _pendingApplyRoutine = null;
        }

        private void StopPendingApplyRoutine()
        {
            if (_pendingApplyRoutine == null)
                return;

            StopCoroutine(_pendingApplyRoutine);
            _pendingApplyRoutine = null;
        }

        private TRPGNetworkLogPacket CreateLogPacket(
            PawnRollLogEntry entry,
            bool showRoulette)
        {
            var definitionId = entry.Pawn != null &&
                               entry.Pawn.Definition != null
                ? entry.Pawn.Definition.Id
                : string.Empty;

            var playerId = Runner != null
                ? Runner.LocalPlayer.PlayerId
                : 0;

            var minimum = 0;
            var maximum = 0;
            var duration = 0f;
            var tone = PawnRollResultTone.Standard;
            var color = new Color32(
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue,
                0);

            if (showRoulette)
            {
                minimum = 1;
                maximum = 100;
                duration = entry.Kind == PawnRollLogKind.Effect
                    ? 1.35f
                    : 1.55f;

                if (entry.Kind == PawnRollLogKind.Effect)
                {
                    ResolveEffectRange(
                        entry.Expression,
                        entry.Value,
                        out minimum,
                        out maximum);
                }

                tone = ResolveResultTone(entry.Result);
                color = (Color32)ResolveResultColor(
                    entry.Kind,
                    entry.Result);
            }

            return new TRPGNetworkLogPacket
            {
                EventId = Trim(
                    $"{playerId}:{Guid.NewGuid():N}",
                    16),
                PawnDefinitionId = Trim(definitionId, 32),
                PawnName = Trim(entry.PawnName, 8),
                Title = Trim(entry.Title, 8),
                Expression = Trim(entry.Expression, 16),
                Result = Trim(entry.Result, 8),
                Detail = Trim(entry.Detail, 16),
                Kind = (int)entry.Kind,
                Value = entry.Value,
                MinimumValue = minimum,
                MaximumValue = maximum,
                DurationSeconds = duration,
                ResultTone = (int)tone,
                ColorRed = color.r,
                ColorGreen = color.g,
                ColorBlue = color.b,
                ColorAlpha = color.a,
                ShowRoulette = showRoulette,
                AnimateRoulette = showRoulette,
                Visibility = (int)entry.Visibility,
                Roller = Runner != null
                    ? Runner.LocalPlayer
                    : PlayerRef.None
            };
        }

        private static bool IsRollLogKind(PawnRollLogKind kind)
        {
            return kind == PawnRollLogKind.PureD100 ||
                   kind == PawnRollLogKind.Check ||
                   kind == PawnRollLogKind.Challenge ||
                   kind == PawnRollLogKind.Effect ||
                   kind == PawnRollLogKind.Luck;
        }

        private TRPGNetworkLogPacket CreateRollPacket(
            InteractivePawn pawn,
            PawnRollLogKind kind,
            string title,
            string expression,
            int finalValue,
            int minimumValue,
            int maximumValue,
            string result,
            string detail,
            Color resultColor,
            float durationSeconds,
            PawnRollResultTone resultTone,
            bool animate)
        {
            var playerId = Runner != null
                ? Runner.LocalPlayer.PlayerId
                : 0;
            var color = (Color32)resultColor;

            return new TRPGNetworkLogPacket
            {
                EventId = Trim(
                    $"{playerId}:{Guid.NewGuid():N}",
                    16),
                PawnDefinitionId = Trim(
                    pawn.Definition.Id,
                    32),
                PawnName = Trim(
                    ResolvePawnName(pawn),
                    8),
                Title = Trim(title, 8),
                Expression = Trim(expression, 16),
                Result = Trim(result, 8),
                Detail = Trim(detail, 16),
                Kind = (int)kind,
                Value = finalValue,
                MinimumValue = minimumValue,
                MaximumValue = maximumValue,
                DurationSeconds = Mathf.Max(
                    0.25f,
                    durationSeconds),
                ResultTone = (int)resultTone,
                ColorRed = color.r,
                ColorGreen = color.g,
                ColorBlue = color.b,
                ColorAlpha = color.a,
                ShowRoulette = true,
                AnimateRoulette = animate,
                Visibility = (int)RollVisibility.Public,
                Roller = Runner != null
                    ? Runner.LocalPlayer
                    : PlayerRef.None
            };
        }

        private static bool IsSecretPacket(
            TRPGNetworkLogPacket packet)
        {
            return packet.Visibility ==
                   (int)RollVisibility.RollerAndGameMaster;
        }

        private void AddHostHistory(
            TRPGNetworkLogPacket packet)
        {
            packet.ShowRoulette = false;
            if (_hostLogHistory.Count >= MaximumHistoryEntries)
                _hostLogHistory.RemoveAt(0);
            _hostLogHistory.Add(packet);
        }

        private void ApplyRemoteLog(
            TRPGNetworkLogPacket packet,
            bool allowRoulette)
        {
            var pawn = ResolvePawnByDefinitionId(
                packet.PawnDefinitionId.ToString());

            _applyingRemoteLog = true;
            try
            {
                PawnRollLogService.RecordRemote(
                    (PawnRollLogKind)packet.Kind,
                    pawn,
                    packet.PawnName.ToString(),
                    packet.Title.ToString(),
                    packet.Expression.ToString(),
                    packet.Value,
                    packet.Result.ToString(),
                    packet.Detail.ToString(),
                    packet.Visibility ==
                    (int)RollVisibility.RollerAndGameMaster
                        ? RollVisibility.RollerAndGameMaster
                        : RollVisibility.Public);
            }
            finally
            {
                _applyingRemoteLog = false;
            }

            if (!allowRoulette || !packet.ShowRoulette)
                return;

            var data = CreateRemotePresentation(packet);
            ResolveLocalReferences();

            if (_checkRollManager != null &&
                _checkRollManager.PresentRemoteRoll(
                    pawn,
                    data,
                    packet.AnimateRoulette))
            {
                return;
            }

            EnsureSpectatorWidget();
            if (_spectatorWidget == null)
                return;

            _spectatorWidget.Enqueue(
                data,
                packet.AnimateRoulette,
                IsLocalGameMaster,
                _remoteRollHoldSeconds);
        }

        private InteractivePawn ResolvePawnByDefinitionId(
            string definitionId)
        {
            return TryResolvePawnByDefinitionId(
                definitionId,
                out var pawn)
                    ? pawn
                    : null;
        }

        private PawnRollWindowData CreateRemotePresentation(
            TRPGNetworkLogPacket packet)
        {
            var kind = (PawnRollLogKind)packet.Kind;
            var minimum = packet.MinimumValue;
            var maximum = packet.MaximumValue;
            var duration = packet.DurationSeconds;

            if (minimum >= maximum)
            {
                minimum = 1;
                maximum = 100;

                if (kind == PawnRollLogKind.Effect)
                {
                    ResolveEffectRange(
                        packet.Expression.ToString(),
                        packet.Value,
                        out minimum,
                        out maximum);
                }
            }

            if (duration < 0.25f)
            {
                duration = kind == PawnRollLogKind.Effect
                    ? 1.35f
                    : 1.55f;
            }

            var pawnName = packet.PawnName.ToString();
            var title = packet.Title.ToString();
            if (!string.IsNullOrWhiteSpace(pawnName))
                title = $"{pawnName} · {title}";

            var result = packet.Result.ToString();
            var color = new Color32(
                packet.ColorRed,
                packet.ColorGreen,
                packet.ColorBlue,
                packet.ColorAlpha);

            if (packet.ColorAlpha == 0)
                color = (Color32)ResolveResultColor(kind, result);

            var toneValue = packet.ResultTone;
            var tone = Enum.IsDefined(
                typeof(PawnRollResultTone),
                toneValue)
                    ? (PawnRollResultTone)toneValue
                    : ResolveResultTone(result);

            return new PawnRollWindowData(
                title,
                packet.Expression.ToString(),
                packet.Value,
                minimum,
                maximum,
                result,
                packet.Detail.ToString(),
                color,
                duration,
                tone);
        }

        private void EnsureSpectatorWidget()
        {
            if (_spectatorWidget != null)
                return;

            var font = _remoteRollFont;
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }

            _spectatorWidget =
                TRPGRemoteRollSpectatorWidget.CreateRuntime(
                    font,
                    _remoteRollSortingOrder,
                    out _ownedSpectatorCanvas);
        }

        private bool CanLocalActOnPawn(
            InteractivePawn pawn)
        {
            if (!IsOnline || IsLocalGameMaster)
                return true;

            if (pawn == null || pawn.Definition == null)
                return false;

            var controlledDefinitionId =
                GetLocalControlledDefinitionId();
            if (string.IsNullOrWhiteSpace(
                    controlledDefinitionId) ||
                !string.Equals(
                    controlledDefinitionId,
                    pawn.Definition.Id,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var activeActorId =
                ActiveActorInstanceId.ToString();
            if (string.IsNullOrWhiteSpace(activeActorId))
                return true;

            return string.Equals(
                GetLocalControlledPawnInstanceId(),
                activeActorId,
                StringComparison.Ordinal);
        }

        private void PublishLocalMessage(string message)
        {
            LocalMessageChanged?.Invoke(message);
        }

        private void PublishStateChanged()
        {
            StateChanged?.Invoke();
        }

        private void LogVerbose(string message)
        {
            if (_verboseNetworkLogs)
            {
                Debug.Log(
                    $"[TRPG NET TRACE] {message}",
                    this);
            }
        }

        private static void ResolveEffectRange(
            string expression,
            int result,
            out int minimum,
            out int maximum)
        {
            minimum = Mathf.Min(0, result);
            maximum = Mathf.Max(1, result);

            if (string.IsNullOrWhiteSpace(expression))
                return;

            var normalized = expression
                .Replace(" ", string.Empty)
                .Replace("×", "*")
                .Replace("(", string.Empty)
                .Replace(")", string.Empty)
                .ToLowerInvariant();

            var multiplier = 1;
            var multiplyIndex = normalized.LastIndexOf('*');
            if (multiplyIndex > 0 &&
                multiplyIndex < normalized.Length - 1 &&
                int.TryParse(
                    normalized.Substring(multiplyIndex + 1),
                    out var parsedMultiplier))
            {
                multiplier = Mathf.Max(1, parsedMultiplier);
                normalized = normalized.Substring(0, multiplyIndex);
            }

            var dIndex = normalized.IndexOf('d');
            if (dIndex <= 0 ||
                dIndex >= normalized.Length - 1)
            {
                return;
            }

            if (!int.TryParse(
                    normalized.Substring(0, dIndex),
                    out var count))
            {
                return;
            }

            var modifierIndex = -1;
            for (var index = dIndex + 1;
                 index < normalized.Length;
                 index++)
            {
                if (normalized[index] == '+' ||
                    normalized[index] == '-')
                {
                    modifierIndex = index;
                    break;
                }
            }

            var sidesText = modifierIndex >= 0
                ? normalized.Substring(
                    dIndex + 1,
                    modifierIndex - dIndex - 1)
                : normalized.Substring(dIndex + 1);
            if (!int.TryParse(sidesText, out var sides))
                return;

            var modifier = 0;
            if (modifierIndex >= 0)
            {
                int.TryParse(
                    normalized.Substring(modifierIndex),
                    out modifier);
            }

            count = Mathf.Max(1, count);
            sides = Mathf.Max(2, sides);
            minimum = (count + modifier) * multiplier;
            maximum = (count * sides + modifier) * multiplier;
            if (minimum > maximum)
            {
                var swap = minimum;
                minimum = maximum;
                maximum = swap;
            }
            if (minimum == maximum)
                maximum = minimum + 1;
        }

        private static Color ResolveResultColor(
            PawnRollLogKind kind,
            string result)
        {
            if (!string.IsNullOrWhiteSpace(result))
            {
                if (result.Contains("대성공"))
                    return new Color(0.20f, 0.95f, 0.38f);
                if (result.Contains("대실패") ||
                    result.Contains("펌블"))
                {
                    return Color.black;
                }
                if (result.Contains("실패"))
                    return new Color(1f, 0.36f, 0.28f);
            }

            return kind == PawnRollLogKind.Effect
                ? new Color(1f, 0.78f, 0.22f)
                : new Color(0.24f, 0.84f, 1f);
        }

        private static PawnRollResultTone ResolveResultTone(
            string result)
        {
            if (!string.IsNullOrWhiteSpace(result))
            {
                if (result.Contains("대성공"))
                    return PawnRollResultTone.Critical;
                if (result.Contains("대실패") ||
                    result.Contains("펌블"))
                {
                    return PawnRollResultTone.Fumble;
                }
            }

            return PawnRollResultTone.Standard;
        }

        private static string ResolvePawnName(
            InteractivePawn pawn)
        {
            if (pawn == null)
                return "캐릭터";

            var definition = pawn.Definition;
            return definition != null &&
                   !string.IsNullOrWhiteSpace(
                       definition.DisplayName)
                ? definition.DisplayName
                : pawn.name;
        }

        private static string NormalizeId(string value)
        {
            var normalized =
                (value ?? string.Empty).Trim();
            return normalized.Length <= 64
                ? normalized
                : normalized.Substring(0, 64);
        }

        private static string Trim(
            string value,
            int maximumLength)
        {
            var normalized = (value ?? string.Empty)
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
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
    }

    internal static class TRPGNetworkPermissionService
    {
        public static bool CanAct(
            bool isGameMaster,
            string controlledPawnId,
            string activeActorId)
        {
            if (isGameMaster)
                return true;

            if (string.IsNullOrWhiteSpace(controlledPawnId))
                return false;

            return string.IsNullOrWhiteSpace(activeActorId) ||
                   string.Equals(
                       controlledPawnId,
                       activeActorId,
                       StringComparison.Ordinal);
        }

        public static bool CanActOnPawn(
            bool isGameMaster,
            string controlledPawnId,
            string activeActorId,
            string requestedPawnId)
        {
            if (isGameMaster)
                return true;

            return CanAct(
                       false,
                       controlledPawnId,
                       activeActorId) &&
                   string.Equals(
                       controlledPawnId,
                       requestedPawnId,
                       StringComparison.Ordinal);
        }
    }
}
