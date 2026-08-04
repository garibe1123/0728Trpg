using System;

namespace Trpg.Pawns
{
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
    }
}
