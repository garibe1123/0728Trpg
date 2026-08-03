using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using Trpg.UI.Profile;
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

            if (!TryResolvePawnByDefinitionId(pawnId, out var pawn))
                return;

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
    }
}
