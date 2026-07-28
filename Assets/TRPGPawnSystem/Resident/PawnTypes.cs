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

    public enum MoveablePawnKind
    {
        Player,
        Monster
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
