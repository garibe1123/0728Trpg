using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace Trpg.Pawns
{
    public sealed class InteractivePawn : Pawn
    {
        [SerializeField, Tooltip("이름, 설명, Portrait, 역할 및 이동 데이터")]
        private InteractivePawnDefinition _definition;

        [SerializeField, Tooltip(
            "이동 연출용 Sprite 자식. 비어 있으면 Pawn 본체를 사용")]
        private Transform _visualRoot;

        [Header("Door")]
        [SerializeField, Tooltip("연결할 반대편 Door의 Instance Id")]
        private string _linkedDoorInstanceId;

        [SerializeField, Tooltip("이 Door로 들어왔을 때 도착시킬 위치")]
        private Transform _arrivalPoint;

        [SerializeField, Tooltip("Door 겹침을 감지할 Trigger Collider2D")]
        private Collider2D _doorTrigger;

        private Sequence _movementTween;
        private Vector3 _visualRestLocalPosition;
        private bool _hasVisualRestPosition;
        private Transform _movementTarget;
        private Quaternion _movementStartLocalRotation;

        public event Action<InteractivePawn> MovementPresentationCompleted;
        public event Action<InteractivePawn, InteractivePawn> DoorEntered;

        public InteractivePawnDefinition Definition => _definition;
        public InteractivePawnKind Kind =>
            _definition != null ? _definition.Kind : InteractivePawnKind.Npc;
        public bool IsMoveable => Kind == InteractivePawnKind.Moveable;
        public bool IsDoor => Kind == InteractivePawnKind.Door;
        public string LinkedDoorInstanceId => _linkedDoorInstanceId;
        public Vector2 ArrivalPosition =>
            _arrivalPoint != null ? _arrivalPoint.position : transform.position;

        public override void Bind()
        {
            EnsureCollider();
            CaptureVisualRestPosition();
            base.Bind();
        }

        public override void Unbind()
        {
            KillMovementTween();
            SetSelected(false);
            MovementPresentationCompleted = null;
            DoorEntered = null;
            base.Unbind();
        }

        public void SetSelected(bool selected)
        {
            var scale = selected && _definition != null
                ? _definition.SelectedScale
                : 1f;
            transform.localScale = Vector3.one * scale;
        }

        public void PresentMovement(IReadOnlyList<Vector3> corners)
        {
            if (!IsMoveable || corners == null || corners.Count < 2)
            {
                return;
            }

            KillMovementTween();

            var destination = corners[corners.Count - 1];
            destination.z = transform.position.z;
            var pathDistances = BuildPathDistances(
                corners,
                out var totalDistance);
            if (totalDistance <= Mathf.Epsilon)
            {
                transform.position = destination;
                MovementPresentationCompleted?.Invoke(this);
                return;
            }

            _movementTarget = _visualRoot != null
                ? _visualRoot
                : transform;
            _movementStartLocalRotation = _movementTarget.localRotation;
            var visualOffset =
                _movementTarget.position - transform.position;
            var duration = _definition != null
                ? _definition.PresentationDurationSeconds
                : 0.5f;
            var hopHeight = _definition != null
                ? _definition.PresentationHopHeight
                : 0.2f;
            var rotationDegrees = _definition != null
                ? _definition.PresentationRotationDegrees
                : 7f;
            var progress = 0f;
            _movementTween = DOTween.Sequence();
            _movementTween.Append(
                DOTween.To(
                        () => progress,
                        value =>
                        {
                            progress = value;
                            ApplyMovementFrame(
                                corners,
                                pathDistances,
                                totalDistance,
                                visualOffset,
                                hopHeight,
                                rotationDegrees,
                                value);
                        },
                        1f,
                        duration)
                    .SetEase(Ease.InOutSine));
            _movementTween.OnComplete(() =>
            {
                _movementTween = null;
                transform.position = destination;
                RestoreMovementPose();
                MovementPresentationCompleted?.Invoke(this);
            });
        }

        public void TeleportTo(Vector2 destination)
        {
            KillMovementTween();
            transform.position = new Vector3(
                destination.x,
                destination.y,
                transform.position.z);
            RestoreMovementPose();
        }

        private void Awake()
        {
            EnsureCollider();
        }

        private void Reset()
        {
            EnsureCollider();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsDoor || other == null)
            {
                return;
            }

            var moveable = other.GetComponentInParent<InteractivePawn>();
            if (moveable != null && moveable != this && moveable.IsMoveable)
            {
                DoorEntered?.Invoke(this, moveable);
            }
        }

        private void EnsureCollider()
        {
            var collider = GetComponentInChildren<Collider2D>(true);
            if (collider == null)
            {
                collider = gameObject.AddComponent<BoxCollider2D>();
            }

            if (!IsDoor)
            {
                return;
            }

            if (_doorTrigger == null)
            {
                _doorTrigger = collider;
            }

            _doorTrigger.isTrigger = true;

            var body = GetComponent<Rigidbody2D>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody2D>();
            }

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
        }

        private void CaptureVisualRestPosition()
        {
            _hasVisualRestPosition =
                _visualRoot != null && _visualRoot != transform;
            if (_hasVisualRestPosition)
            {
                _visualRestLocalPosition = _visualRoot.localPosition;
            }
        }

        private void RestoreMovementPose()
        {
            if (_hasVisualRestPosition &&
                _visualRoot != null &&
                _visualRoot != transform)
            {
                _visualRoot.localPosition = _visualRestLocalPosition;
            }

            if (_movementTarget != null)
            {
                _movementTarget.localRotation =
                    _movementStartLocalRotation;
            }

            _movementTarget = null;
        }

        private void KillMovementTween()
        {
            _movementTween?.Kill();
            _movementTween = null;
            RestoreMovementPose();
        }

        private static float[] BuildPathDistances(
            IReadOnlyList<Vector3> corners,
            out float totalDistance)
        {
            var distances = new float[corners.Count];
            totalDistance = 0f;

            for (var index = 1; index < corners.Count; index++)
            {
                totalDistance += Vector3.Distance(
                    corners[index - 1],
                    corners[index]);
                distances[index] = totalDistance;
            }

            return distances;
        }

        private void ApplyMovementFrame(
            IReadOnlyList<Vector3> corners,
            IReadOnlyList<float> pathDistances,
            float totalDistance,
            Vector3 visualOffset,
            float hopHeight,
            float rotationDegrees,
            float progress)
        {
            if (_movementTarget == null)
            {
                return;
            }

            var travelled = totalDistance * progress;
            var pathPosition = EvaluatePathPosition(
                corners,
                pathDistances,
                travelled);
            var hop = EvaluateHop(progress) * hopHeight;
            _movementTarget.position = new Vector3(
                pathPosition.x + visualOffset.x,
                pathPosition.y + visualOffset.y + hop,
                _movementTarget.position.z);

            var rotation = Mathf.Sin(progress * Mathf.PI) *
                           rotationDegrees;
            _movementTarget.localRotation =
                _movementStartLocalRotation *
                Quaternion.Euler(0f, 0f, rotation);
        }

        private static Vector3 EvaluatePathPosition(
            IReadOnlyList<Vector3> corners,
            IReadOnlyList<float> pathDistances,
            float travelled)
        {
            for (var index = 1; index < corners.Count; index++)
            {
                if (travelled > pathDistances[index])
                {
                    continue;
                }

                var segmentStart = pathDistances[index - 1];
                var segmentLength =
                    pathDistances[index] - segmentStart;
                var segmentProgress = segmentLength > Mathf.Epsilon
                    ? (travelled - segmentStart) / segmentLength
                    : 1f;
                return Vector3.LerpUnclamped(
                    corners[index - 1],
                    corners[index],
                    segmentProgress);
            }

            return corners[corners.Count - 1];
        }

        private static float EvaluateHop(float progress)
        {
            const float landingProgress = 0.82f;
            if (progress <= landingProgress)
            {
                var normalized = progress / landingProgress;
                return Mathf.Sin(normalized * Mathf.PI);
            }

            var settle =
                (progress - landingProgress) /
                (1f - landingProgress);
            return Mathf.Sin(settle * Mathf.PI) * 0.08f;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureCollider();

            if (_definition == null)
            {
                Debug.LogError($"[{name}] Interactive Definition이 비어 있습니다.", this);
            }
        }
#endif
    }
}
