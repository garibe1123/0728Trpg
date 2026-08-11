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

        [SerializeField, HideInInspector]
        private bool _facingLeft;

        [SerializeField, HideInInspector]
        private bool _facingInitialized;

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
        private Vector3 _modularVisualWorldPosition;
        private SpriteRenderer[] _runtimeRenderers;
        private Collider2D[] _runtimeColliders;
        private bool[] _runtimeColliderDefaults;
        private MaterialPropertyBlock _runtimePropertyBlock;
        private GameObject _simpleVisualObject;
        private SpriteRenderer _simpleVisualRenderer;
        private float _lastMovementFacingX;
        private bool _selectedForPresentation;

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
        public PawnVisualMode VisualMode =>
            _definition != null && !_definition.IsDoor
                ? _definition.VisualMode
                : PawnVisualMode.Legacy;
        public bool UsesModularSpriteMotion =>
            VisualMode == PawnVisualMode.ModularCharacter;
        public bool UsesSimpleSpriteVisual =>
            VisualMode == PawnVisualMode.SimpleSprite;
        public bool ShowsInformationOnly =>
            _definition != null &&
            _definition.ShowsInformationOnly;
        public bool IsDoor => Role == InteractivePawnRole.Door;
        public bool IsHidden => _isHidden;
        public bool IsDead => _isDead;
        public bool FacingLeft => _facingLeft;
        public Sprite Portrait
        {
            get
            {
                if (UsesModularSpriteMotion)
                {
                    var animator = GetComponent<PawnSpriteAnimator>();
                    var portrait = animator != null
                        ? animator.ResolvePortrait()
                        : null;
                    if (portrait != null)
                        return portrait;
                }

                return _definition != null
                    ? _definition.Portrait
                    : null;
            }
        }
        public bool IsSelectableForLocalViewer =>
            !_isHidden || IsLocalGameMasterOrOffline();
        public string LinkedDoorInstanceId => _linkedDoorInstanceId;
        public Vector2 ArrivalPosition =>
            _arrivalPoint != null ? _arrivalPoint.position : transform.position;
        public Vector3 PresentationWorldPosition =>
            _movementTween != null
                ? _movementCameraWorldPosition
                : transform.position;
        public Vector3 ModularVisualWorldPosition =>
            _movementTween != null
                ? _modularVisualWorldPosition
                : transform.position;

        public override void Bind()
        {
            EnsureCollider();
            RefreshVisualDefinition();
            CacheRuntimeTargets();
            CaptureVisualRestPosition();
            _modularVisualWorldPosition = transform.position;
            InitializeFacingIfNeeded();
            ApplyFacingToVisuals();
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
            _selectedForPresentation =
                selected && IsSelectableForLocalViewer;

            if (UsesModularSpriteMotion)
            {
                // 모듈형은 PawnSpriteRig가 선택 배율과 좌우 방향을 담당한다.
                transform.localScale = Vector3.one;
                ApplyFacingToVisuals();
                return;
            }

            ApplyFacingToVisuals();
        }


        public bool TryGetRuntimeAppearance(
            out PawnAppearance appearance)
        {
            appearance = _definition != null
                ? _definition.DefaultAppearance
                : PawnAppearance.Default;
            if (!UsesModularSpriteMotion)
                return false;

            var animator = EnsureModularSpriteAnimator();
            return animator != null &&
                   animator.TryGetAppearanceOverride(out appearance);
        }

        public PawnAppearance GetCurrentAppearance()
        {
            if (!UsesModularSpriteMotion)
            {
                return _definition != null
                    ? _definition.DefaultAppearance
                    : PawnAppearance.Default;
            }

            var animator = EnsureModularSpriteAnimator();
            return animator != null
                ? animator.Appearance
                : _definition != null
                    ? _definition.DefaultAppearance
                    : PawnAppearance.Default;
        }

        public void ApplyRuntimeAppearance(
            in PawnAppearance appearance)
        {
            if (!UsesModularSpriteMotion)
                return;

            var animator = EnsureModularSpriteAnimator();
            animator?.ApplyAppearance(appearance);
        }

        public void RestoreRuntimeAppearance(
            bool hasOverride,
            in PawnAppearance appearance)
        {
            if (!UsesModularSpriteMotion)
                return;

            var animator = EnsureModularSpriteAnimator();
            animator?.RestoreAppearance(hasOverride, appearance);
        }

        public void ResetRuntimeAppearance()
        {
            if (!UsesModularSpriteMotion)
                return;

            EnsureModularSpriteAnimator()?.ApplyDefinitionAppearance();
        }

        public void SetFacingLeft(
            bool facingLeft,
            bool notifyVisuals = true)
        {
            _facingInitialized = true;
            if (_facingLeft == facingLeft && notifyVisuals)
            {
                ApplyFacingToVisuals();
                return;
            }

            _facingLeft = facingLeft;
            if (notifyVisuals)
                ApplyFacingToVisuals();
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

            var spriteAnimator = GetComponent<PawnSpriteAnimator>();
            var modularVisualActive =
                UsesModularSpriteMotion &&
                spriteAnimator != null &&
                spriteAnimator.HasRuntimeRig;
            var simpleVisualActive =
                UsesSimpleSpriteVisual &&
                _simpleVisualRenderer != null &&
                _simpleVisualRenderer.sprite != null;

            if (_runtimeRenderers != null)
            {
                for (var index = 0;
                     index < _runtimeRenderers.Length;
                     index++)
                {
                    var renderer = _runtimeRenderers[index];
                    if (renderer == null)
                        continue;

                    var isSimpleRenderer =
                        renderer == _simpleVisualRenderer;
                    var hiddenByVisualMode = modularVisualActive ||
                        (simpleVisualActive
                            ? !isSimpleRenderer
                            : isSimpleRenderer);
                    renderer.forceRenderingOff =
                        hiddenByVisualMode || hiddenFromViewer;
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

            _movementTarget = UsesModularSpriteMotion
                ? null
                : UsesSimpleSpriteVisual && _simpleVisualRenderer != null
                    ? _simpleVisualRenderer.transform
                    : _visualRoot != null
                        ? _visualRoot
                        : transform;
            _movementCameraWorldPosition = corners[0];
            _modularVisualWorldPosition = corners[0];
            _lastMovementFacingX = corners[0].x;
            ApplyInitialPathFacing(corners);
            _movementStartLocalRotation = _movementTarget != null
                ? _movementTarget.localRotation
                : Quaternion.identity;
            var visualOffset = _movementTarget != null
                ? _movementTarget.position - transform.position
                : Vector3.zero;
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
                _modularVisualWorldPosition = destination;
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
            _modularVisualWorldPosition = transform.position;
            RestoreMovementPose();
        }

        private void Awake()
        {
            EnsureCollider();
            RefreshVisualDefinition();
            CacheRuntimeTargets();
            _modularVisualWorldPosition = transform.position;
            InitializeFacingIfNeeded();
            ApplyFacingToVisuals();
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

        public void RefreshVisualDefinition(PawnManager manager = null)
        {
            if (_definition == null || IsDoor)
            {
                DestroySimpleVisual();
                InvalidateRuntimeRenderers();
                RefreshRuntimePresentation();
                return;
            }

            if (UsesSimpleSpriteVisual)
            {
                if (manager == null)
                {
                    manager = FindFirstObjectByType<PawnManager>(
                        FindObjectsInactive.Include);
                }

                EnsureSimpleVisualRenderer();
                _simpleVisualRenderer.sprite =
                    _definition.SimpleVisual != null
                        ? _definition.SimpleVisual.ResolveWorldSprite(
                            manager != null
                                ? manager.PawnSpritePixelsPerUnit
                                : PixelSnap.DefaultPixelsPerUnit)
                        : null;
                _simpleVisualRenderer.enabled =
                    _simpleVisualRenderer.sprite != null;
                // 단일 Sprite가 가진 원래 Material을 유지합니다.
                // 선택 상태 Material 교체는 PawnManager의 기존 선택 표시 경로가
                // 담당하므로 DefaultPawnMaterial 공개 프로퍼티에 의존하지 않습니다.
                _simpleVisualRenderer.flipX = false;
                ApplyFacingToVisuals();
                UpdateSimpleVisualSorting(
                    manager != null
                        ? manager.PawnSpriteSortingBandsPerWorldUnit
                        : 4f);

                var animator = GetComponent<PawnSpriteAnimator>();
                if (animator != null)
                {
                    animator.UnbindRuntime();
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        animator.DestroyEditorPreview();
#endif
                    animator.SetLegacyRenderersHidden(false);
                }
            }
            else
            {
                DestroySimpleVisual();
                if (UsesModularSpriteMotion)
                {
                    var animator = EnsureModularSpriteAnimator();
                    if (manager == null)
                    {
                        manager = FindFirstObjectByType<PawnManager>(
                            FindObjectsInactive.Include);
                    }

                    if (Application.isPlaying)
                        manager?.RegisterModularPawn(this);
#if UNITY_EDITOR
                    else if (animator != null)
                    {
                        animator.RefreshEditorPreview(
                            manager != null ? manager.PawnSpriteLibrary : null,
                            manager != null ? manager.DefaultPawnIdleMotion : null,
                            manager != null
                                ? manager.PawnSpritePixelsPerUnit
                                : PixelSnap.DefaultPixelsPerUnit,
                            manager != null
                                ? manager.PawnSpriteSortingBandsPerWorldUnit
                                : 4f);
                    }
#endif
                }
                else
                {
                    var animator = GetComponent<PawnSpriteAnimator>();
                    if (animator != null)
                    {
                        animator.UnbindRuntime();
#if UNITY_EDITOR
                        if (!Application.isPlaying)
                            animator.DestroyEditorPreview();
#endif
                        animator.SetLegacyRenderersHidden(false);
                    }
                }
            }

            InvalidateRuntimeRenderers();
            CacheRuntimeTargets();
            ApplyFacingToVisuals();
            RefreshRuntimePresentation();
        }

        public void UpdateSimpleVisualSorting(float bandsPerWorldUnit)
        {
            if (!UsesSimpleSpriteVisual || _simpleVisualRenderer == null)
                return;

            var worldY = _simpleVisualRenderer.transform.position.y;
            var yBand = PawnSpriteRig.CalculateSortingBand(
                worldY,
                bandsPerWorldUnit);
            _simpleVisualRenderer.sortingOrder =
                yBand * PawnSpriteRig.SortingBandSize + 64;
        }

        private void EnsureSimpleVisualRenderer()
        {
            if (_simpleVisualRenderer != null)
                return;

            var existing = transform.Find("__SimplePawnVisual");
            if (existing == null)
                existing = transform.Find("__SimplePawnVisualPreview");

            if (existing == null)
            {
                _simpleVisualObject = new GameObject(
                    Application.isPlaying
                        ? "__SimplePawnVisual"
                        : "__SimplePawnVisualPreview");
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    _simpleVisualObject.hideFlags = HideFlags.DontSave;
#endif
                existing = _simpleVisualObject.transform;
                existing.SetParent(transform, false);
            }
            else
            {
                _simpleVisualObject = existing.gameObject;
            }

            existing.localPosition = Vector3.zero;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            _simpleVisualRenderer =
                existing.GetComponent<SpriteRenderer>();
            if (_simpleVisualRenderer == null)
                _simpleVisualRenderer =
                    existing.gameObject.AddComponent<SpriteRenderer>();

            if (_visualRoot == null || _visualRoot == transform)
                _visualRoot = existing;
            _visualRestLocalPosition = Vector3.zero;
            _hasVisualRestPosition = true;
        }

        private void DestroySimpleVisual()
        {
            if (_simpleVisualObject == null &&
                _simpleVisualRenderer != null)
            {
                _simpleVisualObject =
                    _simpleVisualRenderer.gameObject;
            }

            if (_simpleVisualObject != null)
            {
                if (_visualRoot == _simpleVisualObject.transform)
                    _visualRoot = null;

                if (Application.isPlaying)
                    Destroy(_simpleVisualObject);
                else
                    DestroyImmediate(_simpleVisualObject);
            }

            _simpleVisualObject = null;
            _simpleVisualRenderer = null;
        }

        private void InvalidateRuntimeRenderers()
        {
            _runtimeRenderers = null;
        }

        private PawnSpriteAnimator EnsureModularSpriteAnimator()
        {
            var animator = GetComponent<PawnSpriteAnimator>();
            if (!UsesModularSpriteMotion)
                return animator;

            if (animator == null && Application.isPlaying)
                animator = gameObject.AddComponent<PawnSpriteAnimator>();
            return animator;
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
            var travelled = totalDistance * progress;
            var pathPosition = EvaluatePathPosition(
                corners,
                pathDistances,
                travelled);
            var horizontalDirection = ResolveHorizontalPathDirection(
                corners,
                pathDistances,
                travelled);
            if (Mathf.Abs(horizontalDirection) > 0.0001f)
                SetFacingLeft(horizontalDirection < 0f);
            _lastMovementFacingX = pathPosition.x;
            _movementCameraWorldPosition = pathPosition;
            var hop = EvaluateHop(progress) * hopHeight;
            _modularVisualWorldPosition = new Vector3(
                pathPosition.x,
                pathPosition.y + hop,
                0f);
            if (_movementTarget != null)
            {
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
        }

        private void InitializeFacingIfNeeded()
        {
            if (_facingInitialized)
                return;

            var token = !string.IsNullOrWhiteSpace(InstanceId)
                ? InstanceId
                : name;
            _facingLeft = (StableFacingHash(token) & 1u) != 0u;
            _facingInitialized = true;
        }

        private void ApplyInitialPathFacing(
            IReadOnlyList<Vector3> corners)
        {
            if (corners == null || corners.Count < 2)
                return;

            for (var index = 1; index < corners.Count; index++)
            {
                var deltaX = corners[index].x - corners[index - 1].x;
                if (Mathf.Abs(deltaX) <= 0.0001f)
                    continue;

                SetFacingLeft(deltaX < 0f);
                return;
            }
        }

        private void ApplyFacingToVisuals()
        {
            var animator = GetComponent<PawnSpriteAnimator>();
            if (UsesModularSpriteMotion)
            {
                animator?.SetFacingLeft(_facingLeft);
                return;
            }

            var scale = _selectedForPresentation && _definition != null
                ? Mathf.Max(0.01f, _definition.SelectedScale)
                : 1f;
            var facingScaleX = _facingLeft ? -scale : scale;

            Transform visualTransform;
            if (UsesSimpleSpriteVisual && _simpleVisualRenderer != null)
            {
                visualTransform = _simpleVisualRenderer.transform;
            }
            else
            {
                visualTransform =
                    _visualRoot != null && _visualRoot != transform
                        ? _visualRoot
                        : transform;
            }

            if (visualTransform != null)
            {
                visualTransform.localScale = new Vector3(
                    facingScaleX,
                    scale,
                    1f);

                // scale.x가 단일 좌우 방향 소스가 되도록 flipX는 해제한다.
                var renderers =
                    visualTransform.GetComponentsInChildren<SpriteRenderer>(true);
                for (var index = 0; index < renderers.Length; index++)
                {
                    if (renderers[index] != null)
                        renderers[index].flipX = false;
                }
            }
        }

        private static uint StableFacingHash(string value)
        {
            unchecked
            {
                const uint offset = 2166136261u;
                const uint prime = 16777619u;
                var hash = offset;
                if (!string.IsNullOrEmpty(value))
                {
                    for (var index = 0; index < value.Length; index++)
                    {
                        hash ^= value[index];
                        hash *= prime;
                    }
                }

                return hash;
            }
        }

        private static float ResolveHorizontalPathDirection(
            IReadOnlyList<Vector3> corners,
            IReadOnlyList<float> pathDistances,
            float travelled)
        {
            if (corners == null || corners.Count < 2)
                return 0f;

            var segmentIndex = corners.Count - 1;
            for (var index = 1; index < corners.Count; index++)
            {
                if (travelled <= pathDistances[index] + 0.0001f)
                {
                    segmentIndex = index;
                    break;
                }
            }

            var deltaX =
                corners[segmentIndex].x - corners[segmentIndex - 1].x;
            if (Mathf.Abs(deltaX) > 0.0001f)
                return deltaX;

            // 현재 구간이 수직이면 직전 또는 다음의 유효한 수평 방향을 사용한다.
            for (var index = segmentIndex - 1; index >= 1; index--)
            {
                deltaX = corners[index].x - corners[index - 1].x;
                if (Mathf.Abs(deltaX) > 0.0001f)
                    return deltaX;
            }

            for (var index = segmentIndex + 1; index < corners.Count; index++)
            {
                deltaX = corners[index].x - corners[index - 1].x;
                if (Mathf.Abs(deltaX) > 0.0001f)
                    return deltaX;
            }

            return 0f;
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
