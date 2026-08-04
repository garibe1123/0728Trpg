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

        [Header("Runtime State")]
        [SerializeField, HideInInspector]
        private bool _isHidden;

        [SerializeField, HideInInspector]
        private bool _isDead;

        private static readonly int HiddenAmountId =
            Shader.PropertyToID("_TRPGHiddenAmount");
        private static readonly int DeadAmountId =
            Shader.PropertyToID("_TRPGDeadAmount");

        private Sequence _movementTween;
        private Vector3 _visualRestLocalPosition;
        private bool _hasVisualRestPosition;
        private Transform _movementTarget;
        private Quaternion _movementStartLocalRotation;
        private Vector3 _movementCameraWorldPosition;
        private SpriteRenderer[] _runtimeRenderers;
        private Collider2D[] _runtimeColliders;
        private bool[] _runtimeColliderDefaults;
        private MaterialPropertyBlock _runtimePropertyBlock;

        public event Action<InteractivePawn> MovementPresentationCompleted;
        public event Action<InteractivePawn, InteractivePawn> DoorEntered;
        public event Action<InteractivePawn> RuntimeStateChanged;

        public InteractivePawnDefinition Definition => _definition;
        public InteractivePawnKind Kind =>
            _definition != null ? _definition.Kind : InteractivePawnKind.Npc;
        public InteractivePawnRole Role =>
            _definition != null
                ? _definition.Role
                : InteractivePawnRole.Npc;
        public bool IsPlayer => Role == InteractivePawnRole.Player;
        public bool IsNpc => Role == InteractivePawnRole.Npc;
        public bool IsMoveable =>
            _definition != null &&
            _definition.CanMove &&
            !_isDead;
        public bool HasFullCharacterSheet =>
            _definition != null &&
            _definition.SupportsFullCharacterSheet;
        public bool HasStats =>
            _definition != null && _definition.SupportsStats;
        public bool HasSkills =>
            _definition != null && _definition.SupportsSkills;
        public bool HasInventory =>
            _definition != null && _definition.SupportsInventory;
        public bool HasProfile =>
            _definition != null && _definition.SupportsProfile;
        public bool CanRoll =>
            _definition != null && _definition.SupportsRolls;
        public bool CanRerollStats =>
            _definition != null &&
            _definition.SupportsCocStatReroll;
        public bool ShowsInformationOnly =>
            _definition != null &&
            _definition.ShowsInformationOnly;
        public bool IsDoor => Role == InteractivePawnRole.Door;
        public bool IsHidden => _isHidden;
        public bool IsDead => _isDead;
        public bool IsSelectableForLocalViewer =>
            !_isHidden || IsLocalGameMasterOrOffline();
        public string LinkedDoorInstanceId => _linkedDoorInstanceId;
        public Vector2 ArrivalPosition =>
            _arrivalPoint != null ? _arrivalPoint.position : transform.position;
        public Vector3 PresentationWorldPosition =>
            _movementTween != null
                ? _movementCameraWorldPosition
                : transform.position;

        public override void Bind()
        {
            EnsureCollider();
            CacheRuntimeTargets();
            CaptureVisualRestPosition();
            base.Bind();
            RefreshRuntimePresentation();
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
            selected &= IsSelectableForLocalViewer;
            var scale = selected && _definition != null
                ? _definition.SelectedScale
                : 1f;
            transform.localScale = Vector3.one * scale;
        }


        public void SetRuntimeState(
            bool hidden,
            bool dead,
            bool notify = true)
        {
            var changed = _isHidden != hidden || _isDead != dead;
            _isHidden = hidden;
            _isDead = dead;

            if (_isDead)
                KillMovementTween();

            RefreshRuntimePresentation();
            if (changed && notify)
                RuntimeStateChanged?.Invoke(this);
        }

        public void RefreshRuntimePresentation()
        {
            CacheRuntimeTargets();
            var localGameMaster = IsLocalGameMasterOrOffline();
            var hiddenFromViewer = _isHidden && !localGameMaster;
            var hiddenShaderAmount = !_isHidden
                ? 0f
                : localGameMaster
                    ? 0.65f
                    : 1f;
            var deadShaderAmount = _isDead ? 1f : 0f;

            if (_runtimePropertyBlock == null)
                _runtimePropertyBlock = new MaterialPropertyBlock();

            if (_runtimeRenderers != null)
            {
                for (var index = 0;
                     index < _runtimeRenderers.Length;
                     index++)
                {
                    var renderer = _runtimeRenderers[index];
                    if (renderer == null)
                        continue;

                    renderer.forceRenderingOff = hiddenFromViewer;
                    renderer.GetPropertyBlock(_runtimePropertyBlock);
                    _runtimePropertyBlock.SetFloat(
                        HiddenAmountId,
                        hiddenShaderAmount);
                    _runtimePropertyBlock.SetFloat(
                        DeadAmountId,
                        deadShaderAmount);
                    renderer.SetPropertyBlock(_runtimePropertyBlock);
                }
            }

            if (_runtimeColliders != null)
            {
                for (var index = 0;
                     index < _runtimeColliders.Length;
                     index++)
                {
                    var collider = _runtimeColliders[index];
                    if (collider == null)
                        continue;

                    var defaultEnabled =
                        _runtimeColliderDefaults != null &&
                        index < _runtimeColliderDefaults.Length
                            ? _runtimeColliderDefaults[index]
                            : true;
                    collider.enabled =
                        defaultEnabled && !hiddenFromViewer;
                }
            }
        }

        private void CacheRuntimeTargets()
        {
            if (_runtimeRenderers == null ||
                _runtimeRenderers.Length == 0)
            {
                _runtimeRenderers =
                    GetComponentsInChildren<SpriteRenderer>(true);
            }

            if (_runtimeColliders != null &&
                _runtimeColliderDefaults != null &&
                _runtimeColliders.Length ==
                _runtimeColliderDefaults.Length)
            {
                return;
            }

            _runtimeColliders =
                GetComponentsInChildren<Collider2D>(true);
            _runtimeColliderDefaults =
                new bool[_runtimeColliders.Length];
            for (var index = 0;
                 index < _runtimeColliders.Length;
                 index++)
            {
                _runtimeColliderDefaults[index] =
                    _runtimeColliders[index] != null &&
                    _runtimeColliders[index].enabled;
            }
        }

        private static bool IsLocalGameMasterOrOffline()
        {
            var authority = TRPGSessionAuthority.Instance;
            return authority == null ||
                   !authority.IsOnline ||
                   authority.IsLocalGameMaster;
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
            _movementCameraWorldPosition = corners[0];
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
                _movementCameraWorldPosition = destination;
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
            _movementCameraWorldPosition = transform.position;
            RestoreMovementPose();
        }

        private void Awake()
        {
            EnsureCollider();
            CacheRuntimeTargets();
            RefreshRuntimePresentation();
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
            _movementCameraWorldPosition = pathPosition;
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

        private void OnDestroy()
        {
            RuntimeStateChanged = null;
            MovementPresentationCompleted = null;
            DoorEntered = null;
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
