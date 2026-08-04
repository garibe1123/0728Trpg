using System;
using Fusion;
using Trpg.Domain.Stats;
using UnityEngine;

namespace Trpg.Pawns
{
    public sealed partial class TRPGSessionAuthority
    {
        public bool RequestCocStatReroll(
            InteractivePawn pawn,
            string statId)
        {
            if (pawn == null ||
                !pawn.HasStats ||
                pawn.Definition == null ||
                string.IsNullOrWhiteSpace(statId) ||
                !CoCStatGenerationRules.TryGetFormula(
                    statId,
                    out _,
                    out _,
                    out _))
            {
                PublishLocalMessage(
                    "CoC 스탯 재굴림 대상이 올바르지 않습니다.");
                return false;
            }

            if (!IsOnline || Runner == null || !Runner.IsRunning)
            {
                return TryExecuteCocStatReroll(
                    pawn,
                    statId,
                    false,
                    PlayerRef.None,
                    out _);
            }

            if (!IsGameplayReady)
            {
                PublishLocalMessage(
                    "게임 동기화 준비 전에는 재굴림할 수 없습니다.");
                return false;
            }

            if (Object != null && Object.HasStateAuthority)
            {
                return TryExecuteCocStatReroll(
                    pawn,
                    statId,
                    false,
                    Runner.LocalPlayer,
                    out _);
            }

            if (!CanLocalViewFullCharacter(pawn))
            {
                PublishLocalMessage(
                    "자신이 점유한 플레이어 Pawn만 재굴림할 수 있습니다.");
                return false;
            }

            RPC_RequestCocStatReroll(
                Trim(NormalizeId(pawn.Definition.Id), 32),
                Trim(NormalizeId(statId), 32));
            LogVerbose(
                $"CoC REROLL INPUT · Pawn={pawn.Definition.Id} · " +
                $"Stat={statId}");
            return true;
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestCocStatReroll(
            NetworkString<_32> pawnDefinitionId,
            NetworkString<_32> statId,
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
                return;

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
                    "재굴림",
                    reason);
                return;
            }

            if (!TryExecuteCocStatReroll(
                    pawn,
                    statId.ToString(),
                    true,
                    info.Source,
                    out reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "재굴림",
                    reason);
                return;
            }

            SendCommandResult(
                info.Source,
                true,
                "재굴림",
                "스탯 재굴림을 적용했습니다.");
        }

        private bool TryExecuteCocStatReroll(
            InteractivePawn pawn,
            string statId,
            bool consumePlayerPoint,
            PlayerRef requestSource,
            out string reason)
        {
            reason = string.Empty;
            if (pawn == null ||
                !pawn.HasStats ||
                pawn.IsDead ||
                pawn.Definition == null)
            {
                reason = "Player 또는 NPC Pawn만 스탯을 재굴림할 수 있습니다.";
                return false;
            }

            var normalizedStatId = NormalizeId(statId);
            if (!CoCStatGenerationRules.TryRoll(
                    normalizedStatId,
                    out var rolledValue,
                    out var expression,
                    out var minimum,
                    out var maximum))
            {
                reason = "CoC 기본 능력치 생성 규칙이 없는 스탯입니다.";
                return false;
            }

            var state = ResolveStatState(pawn);
            if (state?.Runtime == null ||
                !state.Runtime.TryGetDefinition(
                    normalizedStatId,
                    out var definition) ||
                definition.Source != StatValueSource.Base)
            {
                reason = "재굴림 가능한 기본 능력치가 아닙니다.";
                return false;
            }

            if (!TryGetStatNumber(
                    state,
                    normalizedStatId,
                    out var previousValue))
            {
                reason = "기존 스탯 값을 읽지 못했습니다.";
                return false;
            }

            var remainingPoints =
                CoCStatGenerationRules.DefaultPlayerRerollPoints;
            var nextRemainingPoints = remainingPoints;
            double pointValue =
                CoCStatGenerationRules.DefaultPlayerRerollPoints;
            var hasPointStat = state.Runtime.TryGetDefinition(
                CoCStatGenerationRules.RerollPointsStatId,
                out var pointDefinition) &&
                pointDefinition.Source == StatValueSource.Runtime &&
                TryGetStatNumber(
                    state,
                    CoCStatGenerationRules.RerollPointsStatId,
                    out pointValue);

            if (hasPointStat)
            {
                remainingPoints =
                    CoCStatGenerationRules.ClampPlayerRerollPoints(
                        pointValue);
                nextRemainingPoints = remainingPoints;
            }

            if (consumePlayerPoint)
            {
                if (!hasPointStat)
                {
                    reason =
                        "재굴림 포인트 스탯이 없습니다. " +
                        "CoC 룰 템플릿을 갱신해야 합니다.";
                    return false;
                }

                if (remainingPoints <= 0)
                {
                    reason = "재굴림 포인트를 모두 소진했습니다.";
                    return false;
                }

                nextRemainingPoints = remainingPoints - 1;
            }

            if (!state.TrySetAuthoritativeDisplayedValue(
                    normalizedStatId,
                    rolledValue))
            {
                reason = "Host에서 재굴림 결과를 적용하지 못했습니다.";
                return false;
            }

            if (consumePlayerPoint &&
                !state.TrySetAuthoritativeDisplayedValue(
                    CoCStatGenerationRules.RerollPointsStatId,
                    nextRemainingPoints))
            {
                state.TrySetAuthoritativeDisplayedValue(
                    normalizedStatId,
                    previousValue);
                reason = "재굴림 포인트를 차감하지 못했습니다.";
                return false;
            }

            var canBroadcast =
                IsOnline &&
                Object != null &&
                Object.HasStateAuthority;
            if (canBroadcast)
            {
                var resultPacket = CreateStatPacket(
                    pawn,
                    normalizedStatId,
                    previousValue,
                    rolledValue);
                RPC_ApplyStat(resultPacket);

                if (consumePlayerPoint)
                {
                    var pointPacket = CreateStatPacket(
                        pawn,
                        CoCStatGenerationRules.RerollPointsStatId,
                        remainingPoints,
                        nextRemainingPoints);
                    RPC_ApplyStat(pointPacket);
                }

                GameplayRevision++;
                PublishMovementBudgetSnapshot(pawn);
            }

            var title =
                CoCStatGenerationRules.GetAbbreviation(
                    normalizedStatId) + " 재굴림";
            var detail = consumePlayerPoint
                ? $"{previousValue:0}→{rolledValue} / RP {nextRemainingPoints}"
                : $"{previousValue:0}→{rolledValue} / GM∞";
            var presentation = new PawnRollWindowData(
                title,
                expression,
                rolledValue,
                minimum,
                maximum,
                rolledValue.ToString(),
                detail,
                new Color(0.78f, 0.60f, 1f, 1f),
                1.35f,
                PawnRollResultTone.Standard);

            PawnRollLogService.RecordRoll(
                PawnRollLogKind.Effect,
                pawn,
                presentation.Title,
                presentation.Expression,
                presentation.FinalValue,
                presentation.ResultLabel,
                presentation.DetailLabel);
            PresentStatRerollLocally(pawn, presentation);

            var sourceId = requestSource == PlayerRef.None
                ? -1
                : requestSource.PlayerId;
            LogVerbose(
                $"CoC REROLL APPLY · Source={sourceId} · " +
                $"Pawn={pawn.Definition.Id} · Stat={normalizedStatId} · " +
                $"Value={rolledValue} · Remaining={nextRemainingPoints}");
            return true;
        }

        private static TRPGNetworkStatPacket CreateStatPacket(
            InteractivePawn pawn,
            string statId,
            double previousValue,
            double currentValue)
        {
            return new TRPGNetworkStatPacket
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
        }

        private void PresentStatRerollLocally(
            InteractivePawn pawn,
            in PawnRollWindowData presentation)
        {
            ResolveLocalReferences();
            if (_checkRollManager != null &&
                _checkRollManager.PresentRemoteRoll(
                    pawn,
                    presentation,
                    true))
            {
                return;
            }

            EnsureSpectatorWidget();
            _spectatorWidget?.Enqueue(
                presentation,
                true,
                IsLocalGameMaster,
                _remoteRollHoldSeconds);
        }
    }
}
