using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    public enum InteractivePawnKind
    {
        Moveable,
        Npc,
        Door
    }

    /// <summary>
    /// 기존 SO 직렬화 호환을 위한 내부 값입니다.
    /// 1번 값은 이전 비플레이어 Moveable 데이터를 Walkable NPC로 마이그레이션합니다.
    /// </summary>
    public enum MoveablePawnKind
    {
        Player = 0,
        LegacyWalkableNpc = 1
    }

    public enum NpcMovementMode
    {
        Fixed = 0,
        Walkable = 1
    }

    /// <summary>
    /// Inspector와 런타임에서 사용하는 최종 Interactive Pawn 역할입니다.
    /// 비플레이어 캐릭터는 모두 NPC로 통합합니다.
    /// </summary>
    public enum InteractivePawnRole
    {
        Player,
        Npc,
        Door
    }

    public enum FieldPawnKind
    {
        Floor,
        Obstacle
    }

    public readonly struct PawnPathPreviewData
    {
        private PawnPathPreviewData(
            bool isVisible,
            bool hasPath,
            bool canMove,
            Vector2 screenPosition,
            float distanceMeters,
            IReadOnlyList<Vector3> worldCorners,
            float remainingMeters)
        {
            IsVisible = isVisible;
            HasPath = hasPath;
            CanMove = canMove;
            ScreenPosition = screenPosition;
            DistanceMeters = distanceMeters;
            WorldCorners = worldCorners;
            RemainingMeters = remainingMeters;
        }

        public bool IsVisible { get; }
        public bool HasPath { get; }
        public bool CanMove { get; }
        public Vector2 ScreenPosition { get; }
        public float DistanceMeters { get; }
        public IReadOnlyList<Vector3> WorldCorners { get; }
        public float RemainingMeters { get; }

        public static PawnPathPreviewData Hidden =>
            new PawnPathPreviewData(
                false,
                false,
                false,
                default,
                0f,
                Array.Empty<Vector3>(),
                0f);

        public static PawnPathPreviewData Unreachable(Vector2 screenPosition)
        {
            return new PawnPathPreviewData(
                true,
                false,
                false,
                screenPosition,
                0f,
                Array.Empty<Vector3>(),
                0f);
        }

        public static PawnPathPreviewData Reachable(
            Vector2 screenPosition,
            float distanceMeters,
            bool canMove,
            IReadOnlyList<Vector3> worldCorners,
            float remainingMeters)
        {
            return new PawnPathPreviewData(
                true,
                true,
                canMove,
                screenPosition,
                distanceMeters,
                worldCorners,
                remainingMeters);
        }
    }

    public readonly struct PawnMovementRangeData
    {
        public PawnMovementRangeData(
            IReadOnlyList<Vector3> worldVertices,
            IReadOnlyList<int> triangles)
        {
            WorldVertices = worldVertices;
            Triangles = triangles;
            IsVisible =
                worldVertices != null &&
                triangles != null &&
                worldVertices.Count > 0 &&
                triangles.Count >= 3;
        }

        public bool IsVisible { get; }
        public IReadOnlyList<Vector3> WorldVertices { get; }
        public IReadOnlyList<int> Triangles { get; }

        public static PawnMovementRangeData Hidden =>
            new PawnMovementRangeData(
                Array.Empty<Vector3>(),
                Array.Empty<int>());
    }

    internal sealed class PawnMovementState
    {
        public PawnMovementState(
            Vector2 position,
            float remainingMeters,
            float maximumMeters)
        {
            Position = position;
            RemainingMeters = remainingMeters;
            MaximumMeters = maximumMeters;
        }

        public Vector2 Position { get; set; }
        public float RemainingMeters { get; set; }
        public float MaximumMeters { get; }
    }
}
