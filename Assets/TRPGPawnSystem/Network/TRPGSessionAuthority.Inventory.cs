using System;
using System.Collections.Generic;
using Fusion;
using Trpg.Data.Inventory;
using Trpg.UI.Inventory;
using UnityEngine;

namespace Trpg.Pawns
{
    public enum TRPGInventoryCommandType
    {
        Add = 0,
        Remove = 1,
        SetQuantity = 2,
        Move = 3
    }

    public struct TRPGNetworkInventoryCommand : INetworkStruct
    {
        // Compact schema: safely below Fusion's 512-byte RPC limit.
        public NetworkString<_16> PawnDefinitionId;
        public NetworkString<_32> RuntimeId;
        public NetworkString<_32> ItemDefinitionId;
        public NetworkString<_16> DisplayName;
        public int CommandType;
        public int ItemType;
        public int Quantity;
        public float UnitWeight;
        public int TargetIndex;
        public NetworkBool IsCustom;
    }

    public struct TRPGNetworkInventoryItem : INetworkStruct
    {
        public NetworkString<_32> RuntimeId;
        public NetworkString<_32> ItemDefinitionId;
        public NetworkString<_16> DisplayName;
        public int ItemType;
        public int Quantity;
        public float UnitWeight;
        public int Index;
        public NetworkBool IsCustom;
    }

    public sealed partial class TRPGSessionAuthority
    {
        private const int MaximumInventoryItems = 64;
        private const int MaximumInventoryQuantity = 9999;
        private const float MaximumInventoryUnitWeight = 100000f;

        private sealed class InventoryReceiveBuffer
        {
            public int Revision;
            public int ExpectedCount;
            public readonly List<TRPGNetworkInventoryItem> Items =
                new List<TRPGNetworkInventoryItem>();
        }

        private readonly Dictionary<string, InventoryReceiveBuffer>
            _inventoryReceiveBuffers =
                new Dictionary<string, InventoryReceiveBuffer>(
                    StringComparer.Ordinal);

        public bool ShouldRouteClientInventoryChange =>
            IsOnline && !Runner.IsServer;

        public bool RequestInventoryAdd(
            InteractivePawn pawn,
            InventoryItemDraft draft)
        {
            if (!CanSendInventoryRequest(pawn))
                return false;

            var definition = draft.Definition;
            var command = new TRPGNetworkInventoryCommand
            {
                PawnDefinitionId = Trim(
                    NormalizeId(pawn.Definition.Id),
                    16),
                ItemDefinitionId = Trim(
                    NormalizeId(
                    definition != null
                        ? definition.Id
                        : string.Empty),
                    32),
                DisplayName = Trim(
                    definition != null
                        ? definition.DisplayName
                        : draft.DisplayName,
                    16),
                CommandType = (int)TRPGInventoryCommandType.Add,
                ItemType = (int)(
                    definition != null
                        ? definition.Type
                        : draft.Type),
                Quantity = Mathf.Clamp(
                    draft.Quantity,
                    1,
                    MaximumInventoryQuantity),
                UnitWeight = Mathf.Clamp(
                    definition != null
                        ? definition.UnitWeight
                        : draft.UnitWeight,
                    0f,
                    MaximumInventoryUnitWeight),
                IsCustom = definition == null
            };

            LogVerbose(
                $"Inventory INPUT · Add · " +
                $"Pawn={command.PawnDefinitionId} · " +
                $"Item={command.DisplayName} · Qty={command.Quantity}");
            RPC_RequestInventoryCommand(command);
            return true;
        }

        public bool RequestInventoryRemove(
            InteractivePawn pawn,
            string runtimeId)
        {
            return SendSimpleInventoryCommand(
                pawn,
                TRPGInventoryCommandType.Remove,
                runtimeId,
                0,
                0);
        }

        public bool RequestInventoryQuantity(
            InteractivePawn pawn,
            string runtimeId,
            int quantity)
        {
            return SendSimpleInventoryCommand(
                pawn,
                TRPGInventoryCommandType.SetQuantity,
                runtimeId,
                Mathf.Clamp(
                    quantity,
                    1,
                    MaximumInventoryQuantity),
                0);
        }

        public bool RequestInventoryMove(
            InteractivePawn pawn,
            string runtimeId,
            int targetIndex)
        {
            return SendSimpleInventoryCommand(
                pawn,
                TRPGInventoryCommandType.Move,
                runtimeId,
                0,
                Mathf.Max(0, targetIndex));
        }

        public void PublishHostInventorySnapshot(
            InteractivePawn pawn,
            string title,
            string detail)
        {
            if (!IsLocalGameMaster ||
                _applyingRemoteState ||
                pawn == null ||
                pawn.Definition == null)
            {
                return;
            }

            var state = ResolveInventoryState(pawn);
            if (state == null)
                return;

            GameplayRevision++;
            BroadcastInventorySnapshot(pawn, state);

            PawnRollLogService.RecordAction(
                pawn,
                string.IsNullOrWhiteSpace(title)
                    ? "인벤토리 변경"
                    : title,
                detail ?? string.Empty);
        }

        private bool CanSendInventoryRequest(
            InteractivePawn pawn)
        {
            if (!ShouldRouteClientInventoryChange ||
                !IsGameplayReady ||
                pawn == null ||
                pawn.Definition == null)
            {
                PublishLocalMessage(
                    "인벤토리 변경 요청을 보낼 수 없는 상태입니다.");
                return false;
            }

            return true;
        }

        private bool SendSimpleInventoryCommand(
            InteractivePawn pawn,
            TRPGInventoryCommandType type,
            string runtimeId,
            int quantity,
            int targetIndex)
        {
            if (!CanSendInventoryRequest(pawn) ||
                string.IsNullOrWhiteSpace(runtimeId))
            {
                return false;
            }

            var command = new TRPGNetworkInventoryCommand
            {
                PawnDefinitionId = Trim(
                    NormalizeId(pawn.Definition.Id),
                    16),
                RuntimeId = Trim(NormalizeId(runtimeId), 32),
                CommandType = (int)type,
                Quantity = quantity,
                TargetIndex = targetIndex
            };

            LogVerbose(
                $"Inventory INPUT · {type} · " +
                $"Pawn={command.PawnDefinitionId} · " +
                $"RuntimeId={command.RuntimeId}");
            RPC_RequestInventoryCommand(command);
            return true;
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestInventoryCommand(
            TRPGNetworkInventoryCommand command,
            RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
                return;

            LogVerbose(
                $"Inventory RPC RECEIVED · " +
                $"Source={info.Source.PlayerId} · " +
                $"Command={(TRPGInventoryCommandType)command.CommandType} · " +
                $"Pawn={command.PawnDefinitionId}");

            if (!TryAuthorizePlayerPawn(
                    info.Source,
                    command.PawnDefinitionId.ToString(),
                    false,
                    out var pawn,
                    out var reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "인벤토리",
                    reason);
                return;
            }

            if (!TryApplyInventoryCommand(
                    pawn,
                    command,
                    out var title,
                    out var detail,
                    out reason))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "인벤토리",
                    reason);
                return;
            }

            var state = ResolveInventoryState(pawn);
            GameplayRevision++;
            BroadcastInventorySnapshot(pawn, state);
            PawnRollLogService.RecordAction(
                pawn,
                title,
                detail);

            SendCommandResult(
                info.Source,
                true,
                "인벤토리",
                detail);
        }

        private bool TryApplyInventoryCommand(
            InteractivePawn pawn,
            TRPGNetworkInventoryCommand command,
            out string title,
            out string detail,
            out string reason)
        {
            title = "인벤토리 변경";
            detail = string.Empty;
            reason = string.Empty;

            var state = ResolveInventoryState(pawn);
            if (state == null)
            {
                reason = "Host에서 인벤토리 상태를 찾지 못했습니다.";
                return false;
            }

            var snapshot = state.CreateSnapshot();
            if (snapshot == null || snapshot.Items == null)
            {
                reason = "Host 인벤토리 Snapshot을 만들지 못했습니다.";
                return false;
            }

            var commandType =
                (TRPGInventoryCommandType)command.CommandType;
            var runtimeId = command.RuntimeId.ToString();

            switch (commandType)
            {
                case TRPGInventoryCommandType.Add:
                    if (!TryApplyInventoryAdd(
                            snapshot,
                            command,
                            out title,
                            out detail,
                            out reason))
                    {
                        return false;
                    }
                    break;

                case TRPGInventoryCommandType.Remove:
                    if (!TryFindInventoryItem(
                            snapshot,
                            runtimeId,
                            out var removeIndex))
                    {
                        reason = "삭제할 아이템을 찾지 못했습니다.";
                        return false;
                    }

                    var removed = snapshot.Items[removeIndex];
                    snapshot.Items.RemoveAt(removeIndex);
                    title = "아이템 제거";
                    detail = $"{removed.DisplayName} 제거";
                    break;

                case TRPGInventoryCommandType.SetQuantity:
                    if (!TryFindInventoryItem(
                            snapshot,
                            runtimeId,
                            out var quantityIndex))
                    {
                        reason = "수량을 바꿀 아이템을 찾지 못했습니다.";
                        return false;
                    }

                    var quantity = Mathf.Clamp(
                        command.Quantity,
                        1,
                        MaximumInventoryQuantity);
                    var quantityItem = snapshot.Items[quantityIndex];
                    var previousQuantity = quantityItem.Quantity;
                    quantityItem.Quantity = quantity;
                    title = "아이템 수량";
                    detail =
                        $"{quantityItem.DisplayName} " +
                        $"{previousQuantity} → {quantity}";
                    break;

                case TRPGInventoryCommandType.Move:
                    if (!TryFindInventoryItem(
                            snapshot,
                            runtimeId,
                            out var sourceIndex))
                    {
                        reason = "이동할 아이템을 찾지 못했습니다.";
                        return false;
                    }

                    if (snapshot.Items.Count <= 1)
                    {
                        reason = "이동할 아이템이 하나뿐입니다.";
                        return false;
                    }

                    var targetIndex = Mathf.Clamp(
                        command.TargetIndex,
                        0,
                        snapshot.Items.Count - 1);
                    var moved = snapshot.Items[sourceIndex];
                    snapshot.Items.RemoveAt(sourceIndex);
                    if (sourceIndex < targetIndex)
                        targetIndex--;
                    targetIndex = Mathf.Clamp(
                        targetIndex,
                        0,
                        snapshot.Items.Count);
                    snapshot.Items.Insert(targetIndex, moved);
                    title = "아이템 정렬";
                    detail =
                        $"{moved.DisplayName} → 슬롯 {targetIndex + 1}";
                    break;

                default:
                    reason = "알 수 없는 인벤토리 명령입니다.";
                    return false;
            }

            if (snapshot.Items.Count > MaximumInventoryItems)
            {
                reason =
                    $"인벤토리는 최대 {MaximumInventoryItems}개까지 지원합니다.";
                return false;
            }

            if (!state.TryApplySnapshot(snapshot, out reason))
                return false;

            return true;
        }

        private static bool TryApplyInventoryAdd(
            InventoryRuntimeSnapshot snapshot,
            TRPGNetworkInventoryCommand command,
            out string title,
            out string detail,
            out string reason)
        {
            title = "아이템 추가";
            detail = string.Empty;
            reason = string.Empty;

            var displayName =
                command.DisplayName.ToString().Trim();
            var definitionId =
                command.ItemDefinitionId.ToString().Trim();
            var quantity = Mathf.Clamp(
                command.Quantity,
                1,
                MaximumInventoryQuantity);
            var unitWeight = command.UnitWeight;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                reason = "아이템 이름이 비어 있습니다.";
                return false;
            }

            if (float.IsNaN(unitWeight) ||
                float.IsInfinity(unitWeight) ||
                unitWeight < 0f ||
                unitWeight > MaximumInventoryUnitWeight)
            {
                reason = "아이템 무게가 유효하지 않습니다.";
                return false;
            }

            if (!command.IsCustom &&
                !string.IsNullOrWhiteSpace(definitionId))
            {
                for (var index = 0;
                     index < snapshot.Items.Count;
                     index++)
                {
                    var existing = snapshot.Items[index];
                    if (existing != null &&
                        !existing.IsCustom &&
                        string.Equals(
                            existing.DefinitionId,
                            definitionId,
                            StringComparison.Ordinal))
                    {
                        existing.Quantity = Mathf.Clamp(
                            existing.Quantity + quantity,
                            1,
                            MaximumInventoryQuantity);
                        detail =
                            $"{existing.DisplayName} +" +
                            $"{quantity} / 총 {existing.Quantity}";
                        return true;
                    }
                }
            }

            if (snapshot.Items.Count >= MaximumInventoryItems)
            {
                reason =
                    $"인벤토리는 최대 {MaximumInventoryItems}개까지 지원합니다.";
                return false;
            }

            snapshot.Items.Add(
                new InventoryItemSnapshot
                {
                    RuntimeId = Guid.NewGuid().ToString("N"),
                    DefinitionId = definitionId,
                    Type = (InventoryItemType)command.ItemType,
                    DisplayName = displayName,
                    Quantity = quantity,
                    UnitWeight = unitWeight,
                    IsCustom = command.IsCustom
                });

            detail = $"{displayName} ×{quantity} 추가";
            return true;
        }

        private static bool TryFindInventoryItem(
            InventoryRuntimeSnapshot snapshot,
            string runtimeId,
            out int index)
        {
            index = -1;
            if (snapshot?.Items == null ||
                string.IsNullOrWhiteSpace(runtimeId))
            {
                return false;
            }

            for (var itemIndex = 0;
                 itemIndex < snapshot.Items.Count;
                 itemIndex++)
            {
                var item = snapshot.Items[itemIndex];
                if (item != null &&
                    string.Equals(
                        item.RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    index = itemIndex;
                    return true;
                }
            }

            return false;
        }

        private static PlayerInventoryState ResolveInventoryState(
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
                return null;

            return PlayerInventoryState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
        }

        private void BroadcastInventorySnapshot(
            InteractivePawn pawn,
            PlayerInventoryState state)
        {
            if (pawn == null ||
                pawn.Definition == null ||
                state == null)
            {
                return;
            }

            var snapshot = state.CreateSnapshot();
            var definitionId = Trim(
                NormalizeId(pawn.Definition.Id),
                16);
            var revision = GameplayRevision;
            var count = Mathf.Min(
                snapshot.Items.Count,
                MaximumInventoryItems);

            RPC_InventorySnapshotBegin(
                definitionId,
                revision,
                count);

            for (var index = 0; index < count; index++)
            {
                RPC_InventorySnapshotItem(
                    definitionId,
                    revision,
                    CreateInventoryItemPacket(
                        snapshot.Items[index],
                        index));
            }

            RPC_InventorySnapshotEnd(
                definitionId,
                revision);
        }

        private void SendInventorySnapshotTo(
            PlayerRef target,
            InteractivePawn pawn)
        {
            var state = ResolveInventoryState(pawn);
            if (state == null ||
                pawn == null ||
                pawn.Definition == null)
            {
                return;
            }

            var snapshot = state.CreateSnapshot();
            var definitionId = Trim(
                NormalizeId(pawn.Definition.Id),
                16);
            var revision = GameplayRevision;
            var count = Mathf.Min(
                snapshot.Items.Count,
                MaximumInventoryItems);

            RPC_InventorySnapshotBeginTarget(
                target,
                definitionId,
                revision,
                count);

            for (var index = 0; index < count; index++)
            {
                RPC_InventorySnapshotItemTarget(
                    target,
                    definitionId,
                    revision,
                    CreateInventoryItemPacket(
                        snapshot.Items[index],
                        index));
            }

            RPC_InventorySnapshotEndTarget(
                target,
                definitionId,
                revision);
        }

        private static TRPGNetworkInventoryItem
            CreateInventoryItemPacket(
                InventoryItemSnapshot item,
                int index)
        {
            return new TRPGNetworkInventoryItem
            {
                RuntimeId = Trim(item.RuntimeId, 32),
                ItemDefinitionId = Trim(item.DefinitionId, 32),
                DisplayName = Trim(item.DisplayName, 16),
                ItemType = (int)item.Type,
                Quantity = item.Quantity,
                UnitWeight = item.UnitWeight,
                Index = index,
                IsCustom = item.IsCustom
            };
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_InventorySnapshotBegin(
            NetworkString<_16> pawnDefinitionId,
            int revision,
            int expectedCount)
        {
            if (Object.HasStateAuthority)
                return;

            BeginInventorySnapshot(
                pawnDefinitionId.ToString(),
                revision,
                expectedCount);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_InventorySnapshotItem(
            NetworkString<_16> pawnDefinitionId,
            int revision,
            TRPGNetworkInventoryItem item)
        {
            if (Object.HasStateAuthority)
                return;

            AddInventorySnapshotItem(
                pawnDefinitionId.ToString(),
                revision,
                item);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_InventorySnapshotEnd(
            NetworkString<_16> pawnDefinitionId,
            int revision)
        {
            if (Object.HasStateAuthority)
                return;

            EndInventorySnapshot(
                pawnDefinitionId.ToString(),
                revision);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_InventorySnapshotBeginTarget(
            [RpcTarget] PlayerRef target,
            NetworkString<_16> pawnDefinitionId,
            int revision,
            int expectedCount)
        {
            BeginInventorySnapshot(
                pawnDefinitionId.ToString(),
                revision,
                expectedCount);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_InventorySnapshotItemTarget(
            [RpcTarget] PlayerRef target,
            NetworkString<_16> pawnDefinitionId,
            int revision,
            TRPGNetworkInventoryItem item)
        {
            AddInventorySnapshotItem(
                pawnDefinitionId.ToString(),
                revision,
                item);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_InventorySnapshotEndTarget(
            [RpcTarget] PlayerRef target,
            NetworkString<_16> pawnDefinitionId,
            int revision)
        {
            EndInventorySnapshot(
                pawnDefinitionId.ToString(),
                revision);
        }

        private void BeginInventorySnapshot(
            string pawnDefinitionId,
            int revision,
            int expectedCount)
        {
            var key = NormalizeId(pawnDefinitionId);
            if (string.IsNullOrWhiteSpace(key))
                return;

            _inventoryReceiveBuffers[key] =
                new InventoryReceiveBuffer
                {
                    Revision = revision,
                    ExpectedCount = Mathf.Clamp(
                        expectedCount,
                        0,
                        MaximumInventoryItems)
                };
        }

        private void AddInventorySnapshotItem(
            string pawnDefinitionId,
            int revision,
            TRPGNetworkInventoryItem item)
        {
            var key = NormalizeId(pawnDefinitionId);
            if (!_inventoryReceiveBuffers.TryGetValue(
                    key,
                    out var buffer) ||
                buffer.Revision != revision ||
                buffer.Items.Count >= MaximumInventoryItems)
            {
                return;
            }

            buffer.Items.Add(item);
        }

        private void EndInventorySnapshot(
            string pawnDefinitionId,
            int revision)
        {
            var key = NormalizeId(pawnDefinitionId);
            if (!_inventoryReceiveBuffers.TryGetValue(
                    key,
                    out var buffer) ||
                buffer.Revision != revision)
            {
                return;
            }

            _inventoryReceiveBuffers.Remove(key);
            buffer.Items.Sort(
                (left, right) =>
                    left.Index.CompareTo(right.Index));

            if (!TryResolvePawnByDefinitionId(
                    key,
                    out var pawn))
            {
                Debug.LogError(
                    $"[TRPG Network] 인벤토리 Pawn을 찾지 못했습니다: {key}",
                    this);
                return;
            }

            var state = ResolveInventoryState(pawn);
            if (state == null)
                return;

            var snapshot = new InventoryRuntimeSnapshot
            {
                CharacterDefinitionId = key
            };

            var count = Mathf.Min(
                buffer.Items.Count,
                buffer.ExpectedCount);
            for (var index = 0; index < count; index++)
            {
                var item = buffer.Items[index];
                snapshot.Items.Add(
                    new InventoryItemSnapshot
                    {
                        RuntimeId = item.RuntimeId.ToString(),
                        DefinitionId =
                            item.ItemDefinitionId.ToString(),
                        Type = (InventoryItemType)item.ItemType,
                        DisplayName = item.DisplayName.ToString(),
                        Quantity = Mathf.Clamp(
                            item.Quantity,
                            1,
                            MaximumInventoryQuantity),
                        UnitWeight = Mathf.Clamp(
                            item.UnitWeight,
                            0f,
                            MaximumInventoryUnitWeight),
                        IsCustom = item.IsCustom
                    });
            }

            _applyingRemoteState = true;
            try
            {
                if (!state.TryApplySnapshot(
                        snapshot,
                        out var error))
                {
                    Debug.LogError(
                        $"[TRPG Network] 인벤토리 Snapshot 적용 실패: {error}",
                        state);
                }
                else
                {
                    LogVerbose(
                        $"Inventory RPC APPLY · Pawn={key} · " +
                        $"Items={snapshot.Items.Count}");
                }
            }
            finally
            {
                _applyingRemoteState = false;
            }
        }
    }
}
