using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    [DisallowMultipleComponent]
    public sealed class PawnBoardOverlayGraphic : MaskableGraphic
    {
        private const int MaximumUiVertices = 60000;
        private const int LineSegmentVertexCount = 4;

        private static readonly IReadOnlyList<Vector3> EmptyVertices =
            new Vector3[0];
        private static readonly IReadOnlyList<int> EmptyTriangles =
            new int[0];
        private static readonly IReadOnlyList<int> EmptyEdges =
            new int[0];

        private IReadOnlyList<Vector3> _rangeWorldVertices = EmptyVertices;
        private IReadOnlyList<int> _rangeTriangles = EmptyTriangles;
        private IReadOnlyList<int> _rangeBoundaryEdges = EmptyEdges;
        private IReadOnlyList<Vector3> _pathWorldCorners = EmptyVertices;
        private Camera _boardCamera;
        private float _remainingMeters;
        private bool _showRange;
        private bool _showPath;
        private Color _rangeColor =
            new Color(0.2f, 0.7f, 1f, 0.28f);
        private Color _rangeEdgeColor =
            new Color(0.35f, 0.9f, 1f, 0.9f);
        private Color _reachablePathColor =
            new Color(0.2f, 0.9f, 1f, 1f);
        private Color _overBudgetPathColor =
            new Color(1f, 0.2f, 0.15f, 1f);
        private float _pathWidth = 8f;
        private float _rangeEdgeWidth = 3f;
        private Matrix4x4 _lastViewProjection;
        private Vector2 _lastRectSize;

        public void Configure(
            Color rangeColor,
            Color reachablePathColor,
            Color overBudgetPathColor,
            float pathWidth)
        {
            _rangeColor = rangeColor;
            _rangeEdgeColor = new Color(
                rangeColor.r,
                rangeColor.g,
                rangeColor.b,
                Mathf.Max(0.8f, rangeColor.a));
            _reachablePathColor = reachablePathColor;
            _overBudgetPathColor = overBudgetPathColor;
            _pathWidth = Mathf.Max(1f, pathWidth);
            _rangeEdgeWidth = Mathf.Max(1f, pathWidth * 0.375f);
            raycastTarget = false;
            color = Color.white;
            canvasRenderer.cullTransparentMesh = false;
            SetVerticesDirty();
        }

        public void SetRange(
            PawnMovementRangeData data,
            Camera boardCamera)
        {
            _boardCamera = boardCamera;
            _showRange = data.IsVisible && boardCamera != null;
            _rangeWorldVertices =
                data.WorldVertices ?? EmptyVertices;
            _rangeTriangles =
                data.Triangles ?? EmptyTriangles;
            _rangeBoundaryEdges = _showRange
                ? BuildBoundaryEdges(_rangeTriangles)
                : EmptyEdges;
            CaptureProjectionState();
            SetVerticesDirty();
        }

        public void SetPath(
            PawnPathPreviewData data,
            Camera boardCamera)
        {
            _boardCamera = boardCamera;
            _showPath =
                data.IsVisible &&
                data.HasPath &&
                data.WorldCorners != null &&
                data.WorldCorners.Count >= 2 &&
                boardCamera != null;
            _pathWorldCorners =
                data.WorldCorners ?? EmptyVertices;
            _remainingMeters = Mathf.Max(0f, data.RemainingMeters);
            CaptureProjectionState();
            SetVerticesDirty();
        }

        public void ClearAll()
        {
            _showRange = false;
            _showPath = false;
            _rangeWorldVertices = EmptyVertices;
            _rangeTriangles = EmptyTriangles;
            _rangeBoundaryEdges = EmptyEdges;
            _pathWorldCorners = EmptyVertices;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (_boardCamera == null)
            {
                return;
            }

            if (_showRange)
            {
                AddRange(helper);
            }

            if (_showPath)
            {
                AddPath(helper);
            }
        }

        private void LateUpdate()
        {
            if (_boardCamera == null || (!_showRange && !_showPath))
            {
                return;
            }

            var viewProjection =
                _boardCamera.projectionMatrix *
                _boardCamera.worldToCameraMatrix;
            var rectSize = rectTransform.rect.size;
            if (viewProjection == _lastViewProjection &&
                rectSize == _lastRectSize)
            {
                return;
            }

            _lastViewProjection = viewProjection;
            _lastRectSize = rectSize;
            SetVerticesDirty();
        }

        private void AddRange(VertexHelper helper)
        {
            var startVertex = helper.currentVertCount;
            for (var index = 0;
                 index < _rangeWorldVertices.Count;
                 index++)
            {
                if (!TryWorldToCanvas(
                        _rangeWorldVertices[index],
                        out var local))
                {
                    local = Vector2.zero;
                }

                AddVertex(helper, local, _rangeColor);
            }

            for (var index = 0;
                 index + 2 < _rangeTriangles.Count;
                 index += 3)
            {
                var first = _rangeTriangles[index];
                var second = _rangeTriangles[index + 1];
                var third = _rangeTriangles[index + 2];
                if (!IsRangeIndexValid(first) ||
                    !IsRangeIndexValid(second) ||
                    !IsRangeIndexValid(third))
                {
                    continue;
                }

                helper.AddTriangle(
                    startVertex + first,
                    startVertex + second,
                    startVertex + third);
            }

            AddRangeBoundary(helper);
        }

        private void AddRangeBoundary(VertexHelper helper)
        {
            var reservedPathVertices = GetReservedPathVertexCount();
            for (var index = 0;
                 index + 1 < _rangeBoundaryEdges.Count;
                 index += 2)
            {
                if (helper.currentVertCount +
                    LineSegmentVertexCount +
                    reservedPathVertices >
                    MaximumUiVertices)
                {
                    break;
                }

                var first = _rangeBoundaryEdges[index];
                var second = _rangeBoundaryEdges[index + 1];
                if (!IsRangeIndexValid(first) ||
                    !IsRangeIndexValid(second) ||
                    !TryWorldToCanvas(
                        _rangeWorldVertices[first],
                        out var from) ||
                    !TryWorldToCanvas(
                        _rangeWorldVertices[second],
                        out var to))
                {
                    continue;
                }

                AddLineSegment(
                    helper,
                    from,
                    to,
                    _rangeEdgeColor,
                    _rangeEdgeWidth);
            }
        }

        private void AddPath(VertexHelper helper)
        {
            var travelled = 0f;
            for (var index = 1;
                 index < _pathWorldCorners.Count;
                 index++)
            {
                if (helper.currentVertCount +
                    LineSegmentVertexCount >
                    MaximumUiVertices)
                {
                    break;
                }

                var worldFrom = _pathWorldCorners[index - 1];
                var worldTo = _pathWorldCorners[index];
                var segmentLength = Vector3.Distance(worldFrom, worldTo);
                if (segmentLength <= Mathf.Epsilon ||
                    !TryWorldToCanvas(worldFrom, out var from) ||
                    !TryWorldToCanvas(worldTo, out var to))
                {
                    continue;
                }

                var segmentEnd = travelled + segmentLength;
                if (segmentEnd <= _remainingMeters)
                {
                    AddLineSegment(
                        helper,
                        from,
                        to,
                        _reachablePathColor,
                        _pathWidth);
                }
                else if (travelled >= _remainingMeters)
                {
                    AddLineSegment(
                        helper,
                        from,
                        to,
                        _overBudgetPathColor,
                        _pathWidth);
                }
                else
                {
                    var ratio =
                        (_remainingMeters - travelled) / segmentLength;
                    var split = Vector2.Lerp(from, to, ratio);
                    AddLineSegment(
                        helper,
                        from,
                        split,
                        _reachablePathColor,
                        _pathWidth);
                    if (helper.currentVertCount +
                        LineSegmentVertexCount >
                        MaximumUiVertices)
                    {
                        break;
                    }

                    AddLineSegment(
                        helper,
                        split,
                        to,
                        _overBudgetPathColor,
                        _pathWidth);
                }

                travelled = segmentEnd;
            }
        }

        private int GetReservedPathVertexCount()
        {
            if (!_showPath || _pathWorldCorners == null)
            {
                return 0;
            }

            var segmentCount = Mathf.Max(
                0,
                _pathWorldCorners.Count - 1);
            return Mathf.Min(
                segmentCount * LineSegmentVertexCount * 2,
                MaximumUiVertices);
        }

        private void AddLineSegment(
            VertexHelper helper,
            Vector2 from,
            Vector2 to,
            Color lineColor,
            float width)
        {
            var direction = to - from;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            direction.Normalize();
            var normal =
                new Vector2(-direction.y, direction.x) *
                (width * 0.5f);
            var start = helper.currentVertCount;
            AddVertex(helper, from - normal, lineColor);
            AddVertex(helper, from + normal, lineColor);
            AddVertex(helper, to + normal, lineColor);
            AddVertex(helper, to - normal, lineColor);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }

        private bool TryWorldToCanvas(
            Vector3 worldPosition,
            out Vector2 localPosition)
        {
            var screenPosition =
                _boardCamera.WorldToScreenPoint(worldPosition);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                screenPosition,
                null,
                out localPosition);
        }

        private bool IsRangeIndexValid(int index)
        {
            return index >= 0 && index < _rangeWorldVertices.Count;
        }

        private static IReadOnlyList<int> BuildBoundaryEdges(
            IReadOnlyList<int> triangles)
        {
            if (triangles == null || triangles.Count < 3)
            {
                return EmptyEdges;
            }

            var edgeCounts = new Dictionary<RangeEdge, int>();
            for (var index = 0;
                 index + 2 < triangles.Count;
                 index += 3)
            {
                CountEdge(
                    edgeCounts,
                    triangles[index],
                    triangles[index + 1]);
                CountEdge(
                    edgeCounts,
                    triangles[index + 1],
                    triangles[index + 2]);
                CountEdge(
                    edgeCounts,
                    triangles[index + 2],
                    triangles[index]);
            }

            var boundary = new List<int>();
            foreach (var pair in edgeCounts)
            {
                if (pair.Value != 1)
                {
                    continue;
                }

                boundary.Add(pair.Key.First);
                boundary.Add(pair.Key.Second);
            }

            return boundary;
        }

        private static void CountEdge(
            IDictionary<RangeEdge, int> counts,
            int first,
            int second)
        {
            var edge = new RangeEdge(first, second);
            counts.TryGetValue(edge, out var count);
            counts[edge] = count + 1;
        }

        private void CaptureProjectionState()
        {
            if (_boardCamera == null)
            {
                return;
            }

            _lastViewProjection =
                _boardCamera.projectionMatrix *
                _boardCamera.worldToCameraMatrix;
            _lastRectSize = rectTransform.rect.size;
        }

        private static void AddVertex(
            VertexHelper helper,
            Vector2 position,
            Color vertexColor)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = vertexColor;
            helper.AddVert(vertex);
        }

        private readonly struct RangeEdge : System.IEquatable<RangeEdge>
        {
            public RangeEdge(int first, int second)
            {
                First = Mathf.Min(first, second);
                Second = Mathf.Max(first, second);
            }

            public int First { get; }
            public int Second { get; }

            public bool Equals(RangeEdge other)
            {
                return First == other.First &&
                       Second == other.Second;
            }

            public override bool Equals(object value)
            {
                return value is RangeEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (First * 397) ^ Second;
                }
            }
        }
    }
}
