using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    [DisallowMultipleComponent]
    public sealed class PawnMovementRangeManager : MonoBehaviour
    {
        private const float PathEpsilon = 0.001f;

        [SerializeField, Min(0.1f), Tooltip("A* 도달 범위 UI의 샘플 간격(m)")]
        private float _sampleSpacing = 0.25f;

        [SerializeField, Min(256), Tooltip("한 번에 계산할 최대 A* 샘플 수")]
        private int _maximumSamples = 8192;

        public bool TryBuild(
            Vector2 origin,
            float maximumPathMeters,
            float maximumProjectionMeters,
            PawnNavMeshManager navMeshManager,
            out PawnMovementRangeData data)
        {
            data = PawnMovementRangeData.Hidden;
            if (navMeshManager == null ||
                maximumPathMeters <= PathEpsilon)
            {
                return false;
            }

            var projection = Mathf.Max(
                0.01f,
                maximumProjectionMeters);
            if (!navMeshManager.TryProject(
                    origin,
                    projection,
                    out var projectedOrigin))
            {
                return false;
            }

            origin = projectedOrigin;
            var spacing = ResolveSpacing(maximumPathMeters);
            var radius = Mathf.CeilToInt(maximumPathMeters / spacing);
            var side = radius * 2 + 1;
            var samples = new ReachableSample[side * side];
            var vertices = new List<Vector3>(side * side);
            var triangles = new List<int>(side * side * 3);
            var maximumSqr = maximumPathMeters * maximumPathMeters;

            SampleReachableVertices(
                origin,
                maximumPathMeters,
                maximumSqr,
                projection,
                spacing,
                radius,
                side,
                navMeshManager,
                samples,
                vertices);
            BuildTriangles(
                radius,
                side,
                navMeshManager,
                samples,
                triangles);

            if (triangles.Count == 0)
            {
                return false;
            }

            data = new PawnMovementRangeData(
                vertices.ToArray(),
                triangles.ToArray());
            return true;
        }

        private static void SampleReachableVertices(
            Vector2 origin,
            float maximumPathMeters,
            float maximumSqr,
            float projection,
            float spacing,
            int radius,
            int side,
            PawnNavMeshManager navMeshManager,
            ReachableSample[] samples,
            ICollection<Vector3> vertices)
        {
            for (var y = -radius; y <= radius; y++)
            {
                for (var x = -radius; x <= radius; x++)
                {
                    var offset = new Vector2(x * spacing, y * spacing);
                    if (offset.sqrMagnitude > maximumSqr + PathEpsilon)
                    {
                        continue;
                    }

                    var candidate = origin + offset;
                    if (!TryResolveReachablePoint(
                            origin,
                            candidate,
                            maximumPathMeters,
                            projection,
                            navMeshManager,
                            out var projected))
                    {
                        continue;
                    }

                    var sampleIndex = ToIndex(x, y, radius, side);
                    samples[sampleIndex] = new ReachableSample(
                        vertices.Count,
                        projected);
                    vertices.Add(new Vector3(
                        projected.x,
                        projected.y,
                        navMeshManager.NavigationPlaneZ));
                }
            }
        }

        private static bool TryResolveReachablePoint(
            Vector2 origin,
            Vector2 candidate,
            float maximumPathMeters,
            float projection,
            PawnNavMeshManager navMeshManager,
            out Vector2 projected)
        {
            projected = default;
            if (!navMeshManager.TryCalculatePath(
                    origin,
                    candidate,
                    projection,
                    out var corners,
                    out var length) ||
                corners.Length == 0 ||
                length > maximumPathMeters + PathEpsilon)
            {
                return false;
            }

            projected = new Vector2(
                corners[corners.Length - 1].x,
                corners[corners.Length - 1].y);
            return true;
        }

        private static void BuildTriangles(
            int radius,
            int side,
            PawnNavMeshManager navMeshManager,
            IReadOnlyList<ReachableSample> samples,
            ICollection<int> triangles)
        {
            for (var y = -radius; y < radius; y++)
            {
                for (var x = -radius; x < radius; x++)
                {
                    var lowerLeft =
                        samples[ToIndex(x, y, radius, side)];
                    var lowerRight =
                        samples[ToIndex(x + 1, y, radius, side)];
                    var upperLeft =
                        samples[ToIndex(x, y + 1, radius, side)];
                    var upperRight =
                        samples[ToIndex(x + 1, y + 1, radius, side)];

                    TryAddTriangle(
                        lowerLeft,
                        lowerRight,
                        upperLeft,
                        navMeshManager,
                        triangles);
                    TryAddTriangle(
                        lowerRight,
                        upperRight,
                        upperLeft,
                        navMeshManager,
                        triangles);
                }
            }
        }

        private static void TryAddTriangle(
            ReachableSample first,
            ReachableSample second,
            ReachableSample third,
            PawnNavMeshManager navMeshManager,
            ICollection<int> triangles)
        {
            if (!first.IsValid ||
                !second.IsValid ||
                !third.IsValid ||
                !navMeshManager.IsDirectlyWalkable(
                    first.Position,
                    second.Position) ||
                !navMeshManager.IsDirectlyWalkable(
                    second.Position,
                    third.Position) ||
                !navMeshManager.IsDirectlyWalkable(
                    third.Position,
                    first.Position))
            {
                return;
            }

            triangles.Add(first.VertexIndex);
            triangles.Add(second.VertexIndex);
            triangles.Add(third.VertexIndex);
        }

        private float ResolveSpacing(float radius)
        {
            var configured = Mathf.Max(0.1f, _sampleSpacing);
            var maximumSamples = Mathf.Max(256, _maximumSamples);
            var estimated =
                Mathf.PI * radius * radius /
                (configured * configured);
            return estimated <= maximumSamples
                ? configured
                : Mathf.Sqrt(
                    Mathf.PI * radius * radius / maximumSamples);
        }

        private static int ToIndex(
            int x,
            int y,
            int radius,
            int side)
        {
            return (y + radius) * side + x + radius;
        }

        private readonly struct ReachableSample
        {
            public ReachableSample(int vertexIndex, Vector2 position)
            {
                VertexIndex = vertexIndex;
                Position = position;
                IsValid = true;
            }

            public int VertexIndex { get; }
            public Vector2 Position { get; }
            public bool IsValid { get; }
        }
    }
}
