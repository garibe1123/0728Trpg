using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using Trpg.UI.Profile;
using Trpg.UI.Skills;
using UnityEngine;

namespace Trpg.Pawns
{
    public struct TRPGNetworkProfileChunk : INetworkStruct
    {
        // Fixed reservation: 128 + 128 + numeric fields. Safely below 512 bytes.
        public NetworkString<_32> PawnDefinitionId;
        public NetworkString<_32> TextChunk;
        public int Section;
        public int Revision;
        public int ChunkIndex;
        public int ChunkCount;
        public NetworkBool IsSnapshot;
    }


    public struct TRPGNetworkSkillSnapshotItem : INetworkStruct
    {
        public NetworkString<_32> PawnDefinitionId;
        public NetworkString<_32> SkillId;
        public NetworkString<_32> DisplayName;
        public int RegularValue;
        public int Index;
        public int ExpectedCount;
        public int Revision;
        public NetworkBool IsCustom;
    }

    public sealed partial class TRPGSessionAuthority
    {
        private const int ProfileChunkCharacterCapacity = 28;
        private const int MaximumProfileTextLength = 16000;
        private const int MaximumProfileChunkCount =
            (MaximumProfileTextLength + ProfileChunkCharacterCapacity - 1) /
            ProfileChunkCharacterCapacity;
        private const int MaximumProfileAssemblies = 96;

        private sealed class ProfileChunkAssembly
        {
            public string PawnDefinitionId;
            public PawnProfileSection Section;
            public int Revision;
            public string[] Chunks;
            public int ReceivedCount;
            public bool IsSnapshot;
        }

        private readonly Dictionary<string, ProfileChunkAssembly>
            _profileChunkAssemblies =
                new Dictionary<string, ProfileChunkAssembly>(
                    StringComparer.Ordinal);
        private readonly Dictionary<string, int> _latestProfileRevisions =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private int _localProfileRevision;
        private int _hostProfileRevision;

        private sealed class SkillSnapshotBuffer
        {
            public int Revision;
            public int ExpectedCount;
            public SkillRuntimeValueSnapshot[] Items;
            public int ReceivedCount;
        }

        private readonly Dictionary<string, SkillSnapshotBuffer>
            _skillSnapshotBuffers =
                new Dictionary<string, SkillSnapshotBuffer>(
                    StringComparer.Ordinal);
        private int _localSkillRevision;
        private int _hostSkillRevision;

        public bool ShouldRouteClientProfileChange =>
            IsOnline && Runner != null && !Runner.IsServer;

        public bool RequestProfileFieldChange(
            InteractivePawn pawn,
            PawnProfileSection section,
            string value)
        {
            if (!ShouldRouteClientProfileChange ||
                !IsGameplayReady ||
                pawn == null ||
                !pawn.HasFullCharacterSheet ||
                pawn.Definition == null ||
                !IsValidProfileSection(section))
            {
                return false;
            }

            var pawnId = NormalizeId(pawn.Definition.Id);
            if (string.IsNullOrWhiteSpace(pawnId))
                return false;

            var revision = NextProfileRevision(ref _localProfileRevision);
            foreach (var packet in CreateProfilePackets(
                         pawnId,
                         section,
                         value,
                         revision,
                         false))
            {
                RPC_RequestProfileChunk(packet);
            }

            LogVerbose(
                $"Profile INPUT · Pawn={pawnId} · " +
                $"Section={section} · Revision={revision}");
            return true;
        }

        public bool PublishHostProfileFieldChange(
            InteractivePawn pawn,
            PawnProfileSection section,
            string value)
        {
            if (!IsLocalGameMaster ||
                !Object.HasStateAuthority ||
                pawn == null ||
                !pawn.HasFullCharacterSheet ||
                pawn.Definition == null ||
                !IsValidProfileSection(section))
            {
                return false;
            }

            var pawnId = NormalizeId(pawn.Definition.Id);
            var revision = NextProfileRevision(ref _hostProfileRevision);
            BroadcastProfileField(
                pawnId,
                section,
                value,
                revision,
                false);
            GameplayRevision++;
            return true;
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestProfileChunk(
            TRPGNetworkProfileChunk packet,
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
                    "프로필",
                    reason);
                return;
            }

            if (!TryReceiveProfileChunk(
                    packet,
                    BuildProfileAssemblyKey(
                        info.Source.PlayerId,
                        pawnId,
                        packet.Section,
                        packet.Revision),
                    out var section,
                    out var text))
            {
                return;
            }

            var state = PawnProfileState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (state == null || !state.TrySetField(section, text))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "프로필",
                    "Host에서 플레이어 정보를 적용하지 못했습니다.");
                return;
            }

            GameplayRevision++;
            BroadcastProfileField(
                pawnId,
                section,
                text,
                packet.Revision,
                false);

            SendCommandResult(
                info.Source,
                true,
                "프로필",
                $"{ResolveProfileSectionName(section)} 저장");
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyProfileChunk(
            TRPGNetworkProfileChunk packet)
        {
            if (Object.HasStateAuthority)
                return;

            ApplyIncomingProfileChunk(packet, -1);
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplyProfileSnapshotChunk(
            [RpcTarget] PlayerRef target,
            TRPGNetworkProfileChunk packet)
        {
            ApplyIncomingProfileChunk(
                packet,
                target.PlayerId);
        }

        private void ApplyIncomingProfileChunk(
            TRPGNetworkProfileChunk packet,
            int targetId)
        {
            var pawnId = packet.PawnDefinitionId.ToString();
            if (!TryReceiveProfileChunk(
                    packet,
                    BuildProfileAssemblyKey(
                        targetId,
                        pawnId,
                        packet.Section,
                        packet.Revision),
                    out var section,
                    out var text))
            {
                return;
            }

            if (!TryResolvePawnByDefinitionId(pawnId, out var pawn) ||
                !pawn.HasFullCharacterSheet)
            {
                return;
            }

            var state = PawnProfileState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (state == null)
                return;

            _applyingRemoteState = true;
            try
            {
                state.TrySetField(section, text);
            }
            finally
            {
                _applyingRemoteState = false;
            }

            LogVerbose(
                $"Profile RPC APPLY · Pawn={pawnId} · " +
                $"Section={section} · Revision={packet.Revision}");
        }

        private void SendProfileSnapshotTo(
            PlayerRef target,
            InteractivePawn pawn)
        {
            if (target == PlayerRef.None ||
                pawn == null ||
                !pawn.HasFullCharacterSheet ||
                pawn.Definition == null)
            {
                return;
            }

            var state = PawnProfileState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (state == null)
                return;

            var pawnId = NormalizeId(pawn.Definition.Id);
            var revision = NextProfileRevision(ref _hostProfileRevision);
            foreach (PawnProfileSection section in Enum.GetValues(
                         typeof(PawnProfileSection)))
            {
                var text = state.GetField(section);
                foreach (var packet in CreateProfilePackets(
                             pawnId,
                             section,
                             text,
                             revision,
                             true))
                {
                    RPC_ApplyProfileSnapshotChunk(target, packet);
                }
            }
        }

        private void BroadcastProfileField(
            string pawnId,
            PawnProfileSection section,
            string value,
            int revision,
            bool isSnapshot)
        {
            foreach (var packet in CreateProfilePackets(
                         pawnId,
                         section,
                         value,
                         revision,
                         isSnapshot))
            {
                RPC_ApplyProfileChunk(packet);
            }
        }

        private IEnumerable<TRPGNetworkProfileChunk> CreateProfilePackets(
            string pawnId,
            PawnProfileSection section,
            string value,
            int revision,
            bool isSnapshot)
        {
            var normalized = NormalizeProfileText(value);
            var chunkCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    normalized.Length /
                    (float)ProfileChunkCharacterCapacity));
            chunkCount = Mathf.Min(chunkCount, MaximumProfileChunkCount);

            for (var index = 0; index < chunkCount; index++)
            {
                var start = index * ProfileChunkCharacterCapacity;
                var length = Mathf.Min(
                    ProfileChunkCharacterCapacity,
                    normalized.Length - start);
                var chunk = length > 0
                    ? normalized.Substring(start, length)
                    : string.Empty;

                yield return new TRPGNetworkProfileChunk
                {
                    PawnDefinitionId = Trim(pawnId, 32),
                    TextChunk = chunk,
                    Section = (int)section,
                    Revision = revision,
                    ChunkIndex = index,
                    ChunkCount = chunkCount,
                    IsSnapshot = isSnapshot
                };
            }
        }

        private bool TryReceiveProfileChunk(
            TRPGNetworkProfileChunk packet,
            string assemblyKey,
            out PawnProfileSection section,
            out string text)
        {
            section = (PawnProfileSection)packet.Section;
            text = string.Empty;

            if (!IsValidProfileSection(section) ||
                packet.Revision <= 0 ||
                packet.ChunkCount <= 0 ||
                packet.ChunkCount > MaximumProfileChunkCount ||
                packet.ChunkIndex < 0 ||
                packet.ChunkIndex >= packet.ChunkCount)
            {
                return false;
            }

            var revisionKey = BuildProfileRevisionKey(
                packet.PawnDefinitionId.ToString(),
                packet.Section);
            if (_latestProfileRevisions.TryGetValue(
                    revisionKey,
                    out var latest) &&
                packet.Revision < latest)
            {
                return false;
            }

            if (packet.Revision > latest)
                _latestProfileRevisions[revisionKey] = packet.Revision;

            if (_profileChunkAssemblies.Count >= MaximumProfileAssemblies &&
                !_profileChunkAssemblies.ContainsKey(assemblyKey))
            {
                _profileChunkAssemblies.Clear();
            }

            if (!_profileChunkAssemblies.TryGetValue(
                    assemblyKey,
                    out var assembly) ||
                assembly.Revision != packet.Revision ||
                assembly.Chunks.Length != packet.ChunkCount)
            {
                assembly = new ProfileChunkAssembly
                {
                    PawnDefinitionId =
                        packet.PawnDefinitionId.ToString(),
                    Section = section,
                    Revision = packet.Revision,
                    Chunks = new string[packet.ChunkCount],
                    IsSnapshot = packet.IsSnapshot
                };
                _profileChunkAssemblies[assemblyKey] = assembly;
            }

            if (assembly.Chunks[packet.ChunkIndex] == null)
            {
                assembly.Chunks[packet.ChunkIndex] =
                    packet.TextChunk.ToString();
                assembly.ReceivedCount++;
            }

            if (assembly.ReceivedCount < assembly.Chunks.Length)
                return false;

            var builder = new StringBuilder(
                Mathf.Min(
                    MaximumProfileTextLength,
                    assembly.Chunks.Length *
                    ProfileChunkCharacterCapacity));
            for (var index = 0;
                 index < assembly.Chunks.Length;
                 index++)
            {
                builder.Append(assembly.Chunks[index]);
            }

            text = NormalizeProfileText(builder.ToString());
            section = assembly.Section;
            _profileChunkAssemblies.Remove(assemblyKey);
            return true;
        }

        private static bool IsValidProfileSection(
            PawnProfileSection section)
        {
            return section >= PawnProfileSection.Appearance &&
                   section <= PawnProfileSection.OtherNotes;
        }

        private static int NextProfileRevision(ref int revision)
        {
            revision = revision == int.MaxValue
                ? 1
                : revision + 1;
            return revision;
        }

        private static string NormalizeProfileText(string value)
        {
            var normalized = (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            return normalized.Length <= MaximumProfileTextLength
                ? normalized
                : normalized.Substring(0, MaximumProfileTextLength);
        }

        private static string BuildProfileAssemblyKey(
            int playerId,
            string pawnId,
            int section,
            int revision)
        {
            return playerId + "|" + pawnId + "|" +
                   section + "|" + revision;
        }

        private static string BuildProfileRevisionKey(
            string pawnId,
            int section)
        {
            return pawnId + "|" + section;
        }

        private static string ResolveProfileSectionName(
            PawnProfileSection section)
        {
            switch (section)
            {
                case PawnProfileSection.Appearance:
                    return "외관";
                case PawnProfileSection.BackgroundAndPersonality:
                    return "배경과 성격";
                case PawnProfileSection.PlayerRelationships:
                    return "플레이어 관계";
                case PawnProfileSection.PhobiasAndManias:
                    return "공포증과 집착증";
                case PawnProfileSection.OtherNotes:
                    return "기타 정보";
                default:
                    return "플레이어 정보";
            }
        }

        public bool ShouldRouteClientSkillChange =>
            IsOnline && Runner != null && !Runner.IsServer;

        public bool RequestSkillSnapshot(InteractivePawn pawn)
        {
            if (!ShouldRouteClientSkillChange ||
                !IsGameplayReady ||
                pawn == null ||
                !pawn.HasFullCharacterSheet ||
                pawn.Definition == null)
            {
                return false;
            }

            var state = PlayerSkillState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (state == null)
                return false;

            var revision = NextSkillRevision(ref _localSkillRevision);
            foreach (var packet in CreateSkillPackets(
                         pawn,
                         state.CreateSnapshot(),
                         revision))
            {
                RPC_RequestSkillSnapshotItem(packet);
            }

            return true;
        }

        public bool PublishHostSkillSnapshot(InteractivePawn pawn)
        {
            if (!IsLocalGameMaster ||
                !Object.HasStateAuthority ||
                pawn == null ||
                !pawn.HasFullCharacterSheet ||
                pawn.Definition == null)
            {
                return false;
            }

            var state = PlayerSkillState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (state == null)
                return false;

            GameplayRevision++;
            BroadcastSkillSnapshot(pawn, state.CreateSnapshot());
            return true;
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        private void RPC_RequestSkillSnapshotItem(
            TRPGNetworkSkillSnapshotItem packet,
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
                    "스킬",
                    reason);
                return;
            }

            var key = "request|" + info.Source.PlayerId + "|" +
                      pawnId + "|" + packet.Revision;
            if (!TryReceiveSkillPacket(packet, key, out var snapshot))
                return;

            var state = PlayerSkillState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            var applyError = string.Empty;
            if (state == null ||
                !state.TryApplySnapshot(snapshot, out applyError))
            {
                SendCommandResult(
                    info.Source,
                    false,
                    "스킬",
                    string.IsNullOrWhiteSpace(applyError)
                        ? "Host에서 스킬 상태를 적용하지 못했습니다."
                        : applyError);
                return;
            }

            GameplayRevision++;
            BroadcastSkillSnapshot(pawn, snapshot);
            SendCommandResult(
                info.Source,
                true,
                "스킬",
                "스킬 상태 저장");
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplySkillSnapshotItem(
            TRPGNetworkSkillSnapshotItem packet)
        {
            if (Object.HasStateAuthority)
                return;

            ApplyIncomingSkillPacket(packet, "broadcast");
        }

        [Rpc(
            RpcSources.StateAuthority,
            RpcTargets.All)]
        private void RPC_ApplySkillSnapshotItemTarget(
            [RpcTarget] PlayerRef target,
            TRPGNetworkSkillSnapshotItem packet)
        {
            ApplyIncomingSkillPacket(
                packet,
                "target|" + target.PlayerId);
        }

        private void ApplyIncomingSkillPacket(
            TRPGNetworkSkillSnapshotItem packet,
            string channel)
        {
            var pawnId = packet.PawnDefinitionId.ToString();
            var key = channel + "|" + pawnId + "|" +
                      packet.Revision;
            if (!TryReceiveSkillPacket(packet, key, out var snapshot))
                return;

            if (!TryResolvePawnByDefinitionId(pawnId, out var pawn) ||
                !pawn.HasFullCharacterSheet)
            {
                return;
            }

            var state = PlayerSkillState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (state == null)
                return;

            _applyingRemoteState = true;
            try
            {
                state.TryApplySnapshot(snapshot, out _);
            }
            finally
            {
                _applyingRemoteState = false;
            }
        }

        private void BroadcastSkillSnapshot(
            InteractivePawn pawn,
            SkillRuntimeSnapshot snapshot)
        {
            var revision = NextSkillRevision(ref _hostSkillRevision);
            foreach (var packet in CreateSkillPackets(
                         pawn,
                         snapshot,
                         revision))
            {
                RPC_ApplySkillSnapshotItem(packet);
            }
        }

        private void SendSkillSnapshotTo(
            PlayerRef target,
            InteractivePawn pawn)
        {
            if (target == PlayerRef.None ||
                pawn == null ||
                !pawn.HasFullCharacterSheet ||
                pawn.Definition == null)
            {
                return;
            }

            var state = PlayerSkillState.ResolveOrCreate(
                pawn.gameObject,
                pawn.Definition);
            if (state == null)
                return;

            var revision = NextSkillRevision(ref _hostSkillRevision);
            foreach (var packet in CreateSkillPackets(
                         pawn,
                         state.CreateSnapshot(),
                         revision))
            {
                RPC_ApplySkillSnapshotItemTarget(target, packet);
            }
        }

        private static IEnumerable<TRPGNetworkSkillSnapshotItem>
            CreateSkillPackets(
                InteractivePawn pawn,
                SkillRuntimeSnapshot snapshot,
                int revision)
        {
            var pawnId = pawn != null && pawn.Definition != null
                ? NormalizeId(pawn.Definition.Id)
                : string.Empty;
            var sourceSkills = snapshot != null && snapshot.Skills != null
                ? snapshot.Skills
                : new List<SkillRuntimeValueSnapshot>();
            var skills = new List<SkillRuntimeValueSnapshot>();
            for (var sourceIndex = 0;
                 sourceIndex < sourceSkills.Count && skills.Count < 128;
                 sourceIndex++)
            {
                if (sourceSkills[sourceIndex] != null)
                    skills.Add(sourceSkills[sourceIndex]);
            }

            var count = skills.Count;
            if (count == 0)
            {
                yield return new TRPGNetworkSkillSnapshotItem
                {
                    PawnDefinitionId = Trim(pawnId, 32),
                    Index = -1,
                    ExpectedCount = 0,
                    Revision = revision
                };
                yield break;
            }

            for (var index = 0; index < count; index++)
            {
                var skill = skills[index];
                if (skill == null)
                    continue;

                yield return new TRPGNetworkSkillSnapshotItem
                {
                    PawnDefinitionId = Trim(pawnId, 32),
                    SkillId = Trim(skill.SkillId, 32),
                    DisplayName = Trim(skill.DisplayName, 32),
                    RegularValue = Mathf.Clamp(
                        skill.RegularValue,
                        0,
                        999),
                    Index = index,
                    ExpectedCount = count,
                    Revision = revision,
                    IsCustom = skill.IsCustom
                };
            }
        }

        private bool TryReceiveSkillPacket(
            TRPGNetworkSkillSnapshotItem packet,
            string key,
            out SkillRuntimeSnapshot snapshot)
        {
            snapshot = null;
            if (packet.Revision <= 0 ||
                packet.ExpectedCount < 0 ||
                packet.ExpectedCount > 128)
            {
                return false;
            }

            if (packet.ExpectedCount == 0)
            {
                snapshot = new SkillRuntimeSnapshot
                {
                    CharacterDefinitionId =
                        packet.PawnDefinitionId.ToString()
                };
                return true;
            }

            if (packet.Index < 0 ||
                packet.Index >= packet.ExpectedCount)
            {
                return false;
            }

            if (!_skillSnapshotBuffers.TryGetValue(key, out var buffer) ||
                buffer.Revision != packet.Revision ||
                buffer.ExpectedCount != packet.ExpectedCount)
            {
                buffer = new SkillSnapshotBuffer
                {
                    Revision = packet.Revision,
                    ExpectedCount = packet.ExpectedCount,
                    Items = new SkillRuntimeValueSnapshot[
                        packet.ExpectedCount]
                };
                _skillSnapshotBuffers[key] = buffer;
            }

            if (buffer.Items[packet.Index] == null)
            {
                buffer.Items[packet.Index] =
                    new SkillRuntimeValueSnapshot
                    {
                        SkillId = packet.SkillId.ToString(),
                        DisplayName = packet.DisplayName.ToString(),
                        RegularValue = packet.RegularValue,
                        IsCustom = packet.IsCustom
                    };
                buffer.ReceivedCount++;
            }

            if (buffer.ReceivedCount < buffer.ExpectedCount)
                return false;

            snapshot = new SkillRuntimeSnapshot
            {
                CharacterDefinitionId =
                    packet.PawnDefinitionId.ToString()
            };
            for (var index = 0; index < buffer.Items.Length; index++)
            {
                if (buffer.Items[index] != null)
                    snapshot.Skills.Add(buffer.Items[index]);
            }

            _skillSnapshotBuffers.Remove(key);
            return snapshot.Skills.Count == buffer.ExpectedCount;
        }

        private static int NextSkillRevision(ref int revision)
        {
            revision = revision == int.MaxValue
                ? 1
                : revision + 1;
            return revision;
        }

    }
}
