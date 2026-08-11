using System;
using Fusion;
using Trpg.UI.Handouts;

namespace Trpg.Pawns
{

    public struct TRPGNetworkSanityState : INetworkStruct
    {
        public NetworkString<_32> PawnDefinitionId;
        public int PeriodStartSan;
        public int PeriodLoss;
        public NetworkBool TemporaryInsanityDetected;
        public NetworkBool IndefiniteInsanityDetected;
        public NetworkBool PermanentInsanityDetected;
    }


    public struct TRPGNetworkHandoutRecord : INetworkStruct
    {
        public NetworkString<_32> PawnDefinitionId;
        public NetworkString<_32> PawnInstanceId;
        public NetworkString<_32> HandoutDefinitionId;
        public NetworkBool IsAvailable;
        public NetworkBool HasOpened;
        public long FirstOpenedTicks;
        public long LastOpenedTicks;
    }

    public sealed partial class TRPGSessionAuthority
    {
        private const string PawnHiddenStateId =
            "trpg.pawn.hidden";
        private const string PawnDeadStateId =
            "trpg.pawn.dead";

        public bool SetHostPawnHidden(
            InteractivePawn pawn,
            bool hidden)
        {
            return SetHostPawnRuntimeState(
                pawn,
                hidden,
                pawn != null && pawn.IsDead,
                true);
        }

        public bool SetHostPawnDead(
            InteractivePawn pawn,
            bool dead)
        {
            return SetHostPawnRuntimeState(
                pawn,
                pawn != null && pawn.IsHidden,
                dead,
                true);
        }

        public bool SetHostPawnRuntimeState(
            InteractivePawn pawn,
            bool hidden,
            bool dead,
            bool recordLog = true)
        {
            if (pawn == null ||
                pawn.Definition == null ||
                !pawn.Definition.IsNpc ||
                !IsLocalGameMaster)
            {
                return false;
            }

            var hiddenChanged = pawn.IsHidden != hidden;
            var deadChanged = pawn.IsDead != dead;
            if (!hiddenChanged && !deadChanged)
                return true;

            pawn.SetRuntimeState(hidden, dead);

            if (IsOnline &&
                Object != null &&
                Object.HasStateAuthority)
            {
                BroadcastPawnRuntimeState(pawn, hidden, dead);
                GameplayRevision++;
            }

            if (recordLog)
            {
                if (hiddenChanged)
                {
                    PawnRollLogService.RecordAction(
                        pawn,
                        hidden ? "NPC 숨김" : "NPC 숨김 해제",
                        hidden
                            ? "GM이 보드에서 NPC를 숨겼습니다."
                            : "GM이 보드에 NPC를 다시 표시했습니다.");
                }

                if (deadChanged)
                {
                    PawnRollLogService.RecordAction(
                        pawn,
                        dead ? "NPC 사망처리" : "NPC 사망 해제",
                        dead
                            ? "GM이 NPC를 사망 상태로 처리했습니다."
                            : "GM이 NPC의 사망 상태를 해제했습니다.");
                }
            }

            PublishStateChanged();
            return true;
        }

        private void BroadcastPawnRuntimeState(
            InteractivePawn pawn,
            bool hidden,
            bool dead)
        {
            var pawnId = Trim(
                NormalizeId(pawn.Definition.Id),
                32);

            RPC_ApplyStat(
                new TRPGNetworkStatPacket
                {
                    PawnDefinitionId = pawnId,
                    StatId = PawnHiddenStateId,
                    PreviousValue = hidden ? 0d : 1d,
                    CurrentValue = hidden ? 1d : 0d,
                    IsSnapshot = false
                });
            RPC_ApplyStat(
                new TRPGNetworkStatPacket
                {
                    PawnDefinitionId = pawnId,
                    StatId = PawnDeadStateId,
                    PreviousValue = dead ? 0d : 1d,
                    CurrentValue = dead ? 1d : 0d,
                    IsSnapshot = false
                });
        }

        private void SendPawnRuntimeStateSnapshotTo(
            Fusion.PlayerRef target,
            InteractivePawn pawn)
        {
            if (target == Fusion.PlayerRef.None ||
                pawn == null ||
                pawn.Definition == null ||
                !pawn.Definition.IsNpc)
            {
                return;
            }

            var pawnId = Trim(
                NormalizeId(pawn.Definition.Id),
                32);
            RPC_ApplyStatSnapshot(
                target,
                new TRPGNetworkStatPacket
                {
                    PawnDefinitionId = pawnId,
                    StatId = PawnHiddenStateId,
                    PreviousValue = pawn.IsHidden ? 1d : 0d,
                    CurrentValue = pawn.IsHidden ? 1d : 0d,
                    IsSnapshot = true
                });
            RPC_ApplyStatSnapshot(
                target,
                new TRPGNetworkStatPacket
                {
                    PawnDefinitionId = pawnId,
                    StatId = PawnDeadStateId,
                    PreviousValue = pawn.IsDead ? 1d : 0d,
                    CurrentValue = pawn.IsDead ? 1d : 0d,
                    IsSnapshot = true
                });
        }

        private static bool TryApplyPawnRuntimeStatePacket(
            TRPGNetworkStatPacket packet,
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
                return false;

            var statId = packet.StatId.ToString();
            if (string.Equals(
                    statId,
                    PawnHiddenStateId,
                    StringComparison.Ordinal))
            {
                pawn.SetRuntimeState(
                    packet.CurrentValue >= 0.5d,
                    pawn.IsDead);
                return true;
            }

            if (string.Equals(
                    statId,
                    PawnDeadStateId,
                    StringComparison.Ordinal))
            {
                pawn.SetRuntimeState(
                    pawn.IsHidden,
                    packet.CurrentValue >= 0.5d);
                return true;
            }

            return false;
        }

        public bool PublishSanityState(
            InteractivePawn pawn,
            CoCSanityRuntimeSnapshot snapshot)
        {
            if (!IsGameplayReady ||
                pawn == null ||
                pawn.Definition == null ||
                snapshot == null)
            {
                return false;
            }

            var packet = CreateSanityPacket(pawn, snapshot);
            if (Runner != null && !Runner.IsServer)
            {
                RPC_RequestSanityState(packet);
                return true;
            }

            if (!IsLocalGameMaster || !Object.HasStateAuthority)
                return false;

            ApplySanityPacket(packet);
            GameplayRevision++;
            RPC_ApplySanityState(packet);
            return true;
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestSanityState(
            TRPGNetworkSanityState packet,
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
                return;

            var pawnId = packet.PawnDefinitionId.ToString();
            if (!TryAuthorizePlayerPawn(
                    info.Source,
                    pawnId,
                    false,
                    out var pawn,
                    out var reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "SAN 상태",
                    reason);
                return;
            }

            packet.PawnDefinitionId = Trim(
                NormalizeId(pawn.Definition.Id),
                32);
            ApplySanityPacket(packet);
            GameplayRevision++;
            RPC_ApplySanityState(packet);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplySanityState(
            TRPGNetworkSanityState packet)
        {
            if (Object.HasStateAuthority)
                return;

            ApplySanityPacket(packet);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplySanityStateTarget(
            [RpcTarget] PlayerRef target,
            TRPGNetworkSanityState packet)
        {
            ApplySanityPacket(packet);
        }

        private void SendSanityStateSnapshotTo(
            PlayerRef target,
            InteractivePawn pawn)
        {
            if (target == PlayerRef.None ||
                pawn == null ||
                pawn.Definition == null)
            {
                return;
            }

            var state = pawn.GetComponent<CoCSanityRuntimeState>();
            if (state == null)
                return;

            RPC_ApplySanityStateTarget(
                target,
                CreateSanityPacket(pawn, state.CreateSnapshot()));
        }

        private static TRPGNetworkSanityState CreateSanityPacket(
            InteractivePawn pawn,
            CoCSanityRuntimeSnapshot snapshot)
        {
            return new TRPGNetworkSanityState
            {
                PawnDefinitionId = Trim(
                    pawn != null && pawn.Definition != null
                        ? NormalizeId(pawn.Definition.Id)
                        : string.Empty,
                    32),
                PeriodStartSan = snapshot != null
                    ? Math.Max(0, snapshot.PeriodStartSan)
                    : 0,
                PeriodLoss = snapshot != null
                    ? Math.Max(0, snapshot.PeriodLoss)
                    : 0,
                TemporaryInsanityDetected =
                    snapshot != null &&
                    snapshot.TemporaryInsanityDetected,
                IndefiniteInsanityDetected =
                    snapshot != null &&
                    snapshot.IndefiniteInsanityDetected,
                PermanentInsanityDetected =
                    snapshot != null &&
                    snapshot.PermanentInsanityDetected
            };
        }

        private void ApplySanityPacket(TRPGNetworkSanityState packet)
        {
            if (!TryResolvePawnByDefinitionId(
                    packet.PawnDefinitionId.ToString(),
                    out var pawn))
            {
                return;
            }

            var state = CoCSanityRuntimeState.ResolveOrCreate(pawn);
            state?.TryApplySnapshot(
                new CoCSanityRuntimeSnapshot
                {
                    PeriodStartSan = Math.Max(
                        0,
                        packet.PeriodStartSan),
                    PeriodLoss = Math.Max(0, packet.PeriodLoss),
                    TemporaryInsanityDetected =
                        packet.TemporaryInsanityDetected,
                    IndefiniteInsanityDetected =
                        packet.IndefiniteInsanityDetected,
                    PermanentInsanityDetected =
                        packet.PermanentInsanityDetected
                });
        }


        public bool PublishHostHandoutRecord(
            InteractivePawn pawn,
            string handoutDefinitionId)
        {
            if (!IsLocalGameMaster ||
                !Object.HasStateAuthority ||
                pawn == null ||
                pawn.Definition == null ||
                string.IsNullOrWhiteSpace(handoutDefinitionId))
            {
                return false;
            }

            var state = PublicHandoutState.ResolveOrCreate(
                _pawnManager != null
                    ? _pawnManager.gameObject
                    : gameObject);
            if (state == null ||
                !state.TryGetRecordSnapshot(
                    pawn,
                    handoutDefinitionId,
                    out var record))
            {
                return false;
            }

            GameplayRevision++;
            if (TryGetControllingPlayer(pawn, out var target))
            {
                RPC_ApplyHandoutRecordTarget(
                    target,
                    CreateHandoutPacket(pawn, record));
            }
            return true;
        }

        public bool RequestHandoutOpened(
            InteractivePawn pawn,
            string handoutDefinitionId)
        {
            if (!IsGameplayReady ||
                pawn == null ||
                pawn.Definition == null ||
                string.IsNullOrWhiteSpace(handoutDefinitionId))
            {
                return false;
            }

            if (Runner != null && !Runner.IsServer)
            {
                RPC_RequestHandoutOpened(
                    new TRPGNetworkHandoutRecord
                    {
                        PawnDefinitionId = Trim(
                            NormalizeId(pawn.Definition.Id),
                            32),
                        PawnInstanceId = Trim(
                            NormalizeId(pawn.InstanceId),
                            32),
                        HandoutDefinitionId = Trim(
                            NormalizeId(handoutDefinitionId),
                            32),
                        IsAvailable = true,
                        HasOpened = true
                    });
                return true;
            }

            if (!IsLocalGameMaster || !Object.HasStateAuthority)
                return false;

            var state = PublicHandoutState.ResolveOrCreate(
                _pawnManager != null
                    ? _pawnManager.gameObject
                    : gameObject);
            if (state == null ||
                !state.MarkOpened(pawn, handoutDefinitionId))
            {
                return false;
            }

            return PublishHostHandoutRecord(
                pawn,
                handoutDefinitionId);
        }

        public void PublishHostHandoutSnapshot()
        {
            if (!IsLocalGameMaster || !Object.HasStateAuthority)
                return;

            for (var index = 0; index < PlayerSlots.Length; index++)
            {
                var slot = PlayerSlots[index];
                if (!slot.IsClaimed ||
                    slot.ClaimedBy == PlayerRef.None ||
                    !TryResolvePawnByDefinitionId(
                        slot.DefinitionId.ToString(),
                        out var pawn))
                {
                    continue;
                }

                SendHandoutSnapshotTo(slot.ClaimedBy, pawn);
            }
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestHandoutOpened(
            TRPGNetworkHandoutRecord packet,
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
                return;

            var pawnId = packet.PawnDefinitionId.ToString();
            if (!TryAuthorizePlayerPawn(
                    info.Source,
                    pawnId,
                    false,
                    out var pawn,
                    out var reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "핸드아웃",
                    reason);
                return;
            }

            var handoutId =
                packet.HandoutDefinitionId.ToString();
            var state = PublicHandoutState.ResolveOrCreate(
                _pawnManager != null
                    ? _pawnManager.gameObject
                    : gameObject);
            if (state == null ||
                !state.MarkOpened(pawn, handoutId) ||
                !state.TryGetRecordSnapshot(
                    pawn,
                    handoutId,
                    out var record))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "핸드아웃",
                    "열람 가능한 핸드아웃 기록이 없습니다.");
                return;
            }

            GameplayRevision++;
            RPC_ApplyHandoutRecordTarget(
                info.Source,
                CreateHandoutPacket(pawn, record));
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyHandoutRecordTarget(
            [RpcTarget] PlayerRef target,
            TRPGNetworkHandoutRecord packet)
        {
            ApplyHandoutPacket(packet);
        }

        private void SendHandoutSnapshotTo(PlayerRef target)
        {
            if (target == PlayerRef.None)
                return;

            for (var index = 0; index < PlayerSlots.Length; index++)
            {
                var slot = PlayerSlots[index];
                if (!slot.IsClaimed ||
                    slot.ClaimedBy != target ||
                    !TryResolvePawnByDefinitionId(
                        slot.DefinitionId.ToString(),
                        out var pawn))
                {
                    continue;
                }

                SendHandoutSnapshotTo(target, pawn);
                return;
            }
        }

        private void SendHandoutSnapshotTo(
            PlayerRef target,
            InteractivePawn pawn)
        {
            if (target == PlayerRef.None || pawn == null)
                return;

            var state = PublicHandoutState.ResolveOrCreate(
                _pawnManager != null
                    ? _pawnManager.gameObject
                    : gameObject);
            if (state == null)
                return;

            var records = state.GetRecordSnapshotsForPawn(pawn);
            for (var index = 0; index < records.Count; index++)
            {
                RPC_ApplyHandoutRecordTarget(
                    target,
                    CreateHandoutPacket(pawn, records[index]));
            }
        }

        private void ApplyHandoutPacket(
            TRPGNetworkHandoutRecord packet)
        {
            if (!TryResolvePawnByDefinitionId(
                    packet.PawnDefinitionId.ToString(),
                    out var pawn))
            {
                return;
            }

            var state = PublicHandoutState.ResolveOrCreate(
                _pawnManager != null
                    ? _pawnManager.gameObject
                    : gameObject);
            state?.ApplyNetworkRecord(
                pawn,
                new PawnHandoutRecordSnapshot
                {
                    PawnInstanceId = pawn.InstanceId,
                    DefinitionId =
                        packet.HandoutDefinitionId.ToString(),
                    IsAvailable = packet.IsAvailable,
                    HasOpened = packet.HasOpened,
                    FirstOpenedUtc = FormatUtcTicks(
                        packet.FirstOpenedTicks),
                    LastOpenedUtc = FormatUtcTicks(
                        packet.LastOpenedTicks)
                });
        }

        private static TRPGNetworkHandoutRecord CreateHandoutPacket(
            InteractivePawn pawn,
            PawnHandoutRecordSnapshot record)
        {
            return new TRPGNetworkHandoutRecord
            {
                PawnDefinitionId = Trim(
                    pawn != null && pawn.Definition != null
                        ? NormalizeId(pawn.Definition.Id)
                        : string.Empty,
                    32),
                PawnInstanceId = Trim(
                    pawn != null
                        ? NormalizeId(pawn.InstanceId)
                        : string.Empty,
                    32),
                HandoutDefinitionId = Trim(
                    record != null
                        ? NormalizeId(record.DefinitionId)
                        : string.Empty,
                    32),
                IsAvailable = record != null && record.IsAvailable,
                HasOpened = record != null && record.HasOpened,
                FirstOpenedTicks = ParseUtcTicks(
                    record != null ? record.FirstOpenedUtc : null),
                LastOpenedTicks = ParseUtcTicks(
                    record != null ? record.LastOpenedUtc : null)
            };
        }

        private bool TryGetControllingPlayer(
            InteractivePawn pawn,
            out PlayerRef target)
        {
            target = PlayerRef.None;
            if (pawn == null || pawn.Definition == null)
                return false;

            for (var index = 0; index < PlayerSlots.Length; index++)
            {
                var slot = PlayerSlots[index];
                if (!slot.IsClaimed ||
                    slot.ClaimedBy == PlayerRef.None)
                {
                    continue;
                }

                if (string.Equals(
                        slot.DefinitionId.ToString(),
                        pawn.Definition.Id,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        slot.PawnInstanceId.ToString(),
                        pawn.InstanceId,
                        StringComparison.Ordinal))
                {
                    target = slot.ClaimedBy;
                    return true;
                }
            }

            return false;
        }

        private static long ParseUtcTicks(string value)
        {
            return DateTime.TryParse(
                value,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed.ToUniversalTime().Ticks
                : 0L;
        }

        private static string FormatUtcTicks(long ticks)
        {
            if (ticks <= 0L || ticks > DateTime.MaxValue.Ticks)
                return string.Empty;

            return new DateTime(
                ticks,
                DateTimeKind.Utc).ToString("O");
        }

    }
}
