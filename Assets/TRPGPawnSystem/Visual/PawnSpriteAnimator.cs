using UnityEngine;

namespace Trpg.Pawns
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractivePawn))]
    public sealed class PawnSpriteAnimator : MonoBehaviour
    {
        [SerializeField, Tooltip(
            "켜면 InteractivePawnDefinition의 기본 외형을 사용합니다.")]
        private bool _useDefinitionAppearance = true;
        [SerializeField] private PawnAppearance _appearance = PawnAppearance.Default;
        [SerializeField, Tooltip(
            "비어 있으면 PawnManager의 기본 Idle Motion을 사용합니다.")]
        private PawnIdleMotion _idleMotionOverride;

        private InteractivePawn _pawn;
        private PawnManager _manager;
        private PawnSpriteLibrary _library;
        private PawnIdleMotion _runtimeIdleMotion;
        private PawnSpriteRig _runtimeRig;
        private PawnSpriteRig _editorPreviewRig;
        private float _phase;
        private float _speedMultiplier = 1f;
        private int _lastKey = -1;
        private bool _selected;
        private Material _selectionMaterial;
        private float _selectedScale = 1f;
        private int _pixelsPerUnit = PixelSnap.DefaultPixelsPerUnit;
        private float _sortingBandsPerWorldUnit = 4f;
        private bool _legacyHidden;
        private bool _hasWorldSortOverride;
        private int _worldSortLayerId;
        private int _worldSortReferenceOrder;
        private bool _worldSortInFront;

        public InteractivePawn Pawn => EnsurePawn();
        public bool UsesDefinitionAppearance => _useDefinitionAppearance;
        public bool HasAppearanceOverride => !_useDefinitionAppearance;
        public PawnAppearance Appearance => ResolveAppearance();
        public PawnIdleMotion IdleMotionOverride => _idleMotionOverride;
        public PawnIdleMotion EffectiveIdleMotion =>
            _idleMotionOverride != null ? _idleMotionOverride : _runtimeIdleMotion;
        public bool IsModularEnabled =>
            EnsurePawn() != null &&
            _pawn.UsesModularSpriteMotion;
        public bool HasRuntimeRig => _runtimeRig != null;
        public bool HasRuntimeBinding =>
            _manager != null && _library != null;
        public float Phase => _phase;
        public float SpeedMultiplier => _speedMultiplier;

        public void BindRuntime(
            PawnManager manager,
            PawnSpriteLibrary library,
            PawnIdleMotion defaultIdleMotion,
            int pixelsPerUnit,
            float sortingBandsPerWorldUnit)
        {
            _manager = manager;
            _library = library;
            _runtimeIdleMotion = defaultIdleMotion;
            _pixelsPerUnit = PixelSnap.NormalizePixelsPerUnit(
                pixelsPerUnit);
            _sortingBandsPerWorldUnit = Mathf.Max(
                0.01f,
                sortingBandsPerWorldUnit);
            _lastKey = -1;
            ResolveDeterministicTiming();
            SetLegacyRenderersHidden(IsModularEnabled);
        }

        public void UnbindRuntime()
        {
            ReleaseRuntimeRig();
            _manager = null;
            _library = null;
            _runtimeIdleMotion = null;
            _pixelsPerUnit = PixelSnap.DefaultPixelsPerUnit;
            _sortingBandsPerWorldUnit = 4f;
            _lastKey = -1;
            _hasWorldSortOverride = false;
            _worldSortLayerId = 0;
            _worldSortReferenceOrder = 0;
            _worldSortInFront = false;
            SetLegacyRenderersHidden(false);
        }

        public void AssignRuntimeRig(PawnSpriteRig rig)
        {
            if (_runtimeRig == rig)
                return;

            ReleaseRuntimeRig();
            _runtimeRig = rig;
            if (_runtimeRig == null)
                return;

            _runtimeRig.Assign(this, ResolveAppearance());
            _runtimeRig.SetFacingLeft(Pawn != null && Pawn.FacingLeft);
            SetLegacyRenderersHidden(true);
            _runtimeRig.SetSelectionPresentation(
                _selected,
                _selectionMaterial,
                _selectedScale);
            ApplyWorldSortOverrideToRig(_runtimeRig);
            _lastKey = -1;
        }

        public PawnSpriteRig DetachRuntimeRig()
        {
            var rig = _runtimeRig;
            _runtimeRig = null;
            _lastKey = -1;
            return rig;
        }

        public void ReleaseRuntimeRig()
        {
            if (_runtimeRig == null)
                return;

            var rig = _runtimeRig;
            _runtimeRig = null;
            _lastKey = -1;
            rig.Release();
        }

        public void SetWorldSortOverride(
            bool enabled,
            int sortingLayerId,
            int referenceOrder,
            bool inFront)
        {
            _hasWorldSortOverride = enabled;
            _worldSortLayerId = sortingLayerId;
            _worldSortReferenceOrder = referenceOrder;
            _worldSortInFront = inFront;
            ApplyWorldSortOverrideToRig(_runtimeRig);
        }

        private void ApplyWorldSortOverrideToRig(PawnSpriteRig rig)
        {
            if (rig == null)
                return;

            if (_hasWorldSortOverride)
            {
                rig.SetWorldSortOverride(
                    _worldSortLayerId,
                    _worldSortReferenceOrder,
                    _worldSortInFront);
            }
            else
            {
                rig.ClearWorldSortOverride();
            }
        }

        public void UpdateRuntimeVisual(float time)
        {
            if (_runtimeRig == null || !IsModularEnabled)
                return;

            var position = Pawn.ModularVisualWorldPosition;
            _runtimeRig.SetWorldPosition(position);
            _runtimeRig.SetSortingBand(
                PawnSpriteRig.CalculateSortingBand(
                    position.y,
                    _sortingBandsPerWorldUnit));
            ApplyWorldSortOverrideToRig(_runtimeRig);

            var motion = EffectiveIdleMotion;
            var key = motion != null
                ? motion.EvaluateKey(time, _phase, _speedMultiplier)
                : 0;
            if (key == _lastKey)
                return;

            _lastKey = key;
            _runtimeRig.ApplyKey(motion, key);
        }

        public void ApplyAppearance(in PawnAppearance appearance)
        {
            _useDefinitionAppearance = false;
            _appearance = appearance.WithVisibleColorDefaults();
            _lastKey = -1;
            _runtimeRig?.ApplyAppearance(_appearance);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                RefreshEditorPreviewFromScene();
#endif
        }

        public void ApplyDefinitionAppearance()
        {
            var pawn = EnsurePawn();
            if (pawn == null || pawn.Definition == null)
                return;

            _useDefinitionAppearance = true;
            _appearance = pawn.Definition.DefaultAppearance;
            _lastKey = -1;
            _runtimeRig?.ApplyAppearance(_appearance);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                RefreshEditorPreviewFromScene();
#endif
        }

        public bool TryGetAppearanceOverride(
            out PawnAppearance appearance)
        {
            appearance = _appearance.WithVisibleColorDefaults();
            return !_useDefinitionAppearance;
        }

        public void RestoreAppearance(
            bool hasOverride,
            in PawnAppearance appearance)
        {
            if (hasOverride)
                ApplyAppearance(appearance);
            else
                ApplyDefinitionAppearance();
        }

        public Sprite ResolvePortrait()
        {
            var pawn = EnsurePawn();
            var library = _library;
            if (library == null)
            {
                var manager = FindFirstObjectByType<PawnManager>(
                    FindObjectsInactive.Include);
                library = manager != null
                    ? manager.PawnSpriteLibrary
                    : null;
            }

            var portrait = library != null
                ? library.ResolvePortrait(ResolveAppearance().PortraitId)
                : null;
            return portrait != null
                ? portrait
                : pawn != null && pawn.Definition != null
                    ? pawn.Definition.Portrait
                    : null;
        }

        public void SetFacingLeft(bool facingLeft)
        {
            _runtimeRig?.SetFacingLeft(facingLeft);
#if UNITY_EDITOR
            _editorPreviewRig?.SetFacingLeft(facingLeft);
#endif
        }

        public void SetSelected(
            bool selected,
            Material selectionMaterial,
            float selectedScale = 1f)
        {
            _selected = selected;
            _selectionMaterial = selectionMaterial;
            _selectedScale = selected
                ? Mathf.Max(1f, selectedScale)
                : 1f;
            _runtimeRig?.SetSelectionPresentation(
                selected,
                selectionMaterial,
                _selectedScale);
#if UNITY_EDITOR
            _editorPreviewRig?.SetSelectionPresentation(
                selected,
                selectionMaterial,
                _selectedScale);
#endif
        }

        public void RefreshRuntimeAppearance()
        {
            _lastKey = -1;
            if (_runtimeRig != null)
            {
                _runtimeRig.ApplyAppearance(ResolveAppearance());
                _runtimeRig.SetFacingLeft(Pawn != null && Pawn.FacingLeft);
                _runtimeRig.SetSelectionPresentation(
                    _selected,
                    _selectionMaterial,
                    _selectedScale);
            }
        }

        public void SetLegacyRenderersHidden(bool hidden)
        {
            _legacyHidden = hidden;
            var renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || IsEditorPreviewRenderer(renderer))
                    continue;

                renderer.forceRenderingOff = hidden;
            }
        }

#if UNITY_EDITOR
        public void RefreshEditorPreview(
            PawnSpriteLibrary library,
            PawnIdleMotion defaultIdleMotion,
            int pixelsPerUnit = PixelSnap.DefaultPixelsPerUnit,
            float sortingBandsPerWorldUnit = 4f)
        {
            if (Application.isPlaying)
                return;

            DestroyEditorPreview();
            if (!IsModularEnabled || library == null)
            {
                SetLegacyRenderersHidden(false);
                return;
            }

            _library = library;
            _runtimeIdleMotion = defaultIdleMotion;
            _pixelsPerUnit = PixelSnap.NormalizePixelsPerUnit(
                pixelsPerUnit);
            _sortingBandsPerWorldUnit = Mathf.Max(
                0.01f,
                sortingBandsPerWorldUnit);
            SetLegacyRenderersHidden(true);
            _editorPreviewRig = new PawnSpriteRig(
                library,
                transform,
                $"__PawnSpritePreview_{name}",
                HideFlags.HideAndDontSave,
                _pixelsPerUnit);
            _editorPreviewRig.Assign(this, ResolveAppearance());
            _editorPreviewRig.SetFacingLeft(Pawn != null && Pawn.FacingLeft);
            _editorPreviewRig.SetWorldPosition(transform.position);
            _editorPreviewRig.SetSortingBand(
                PawnSpriteRig.CalculateSortingBand(
                    transform.position.y,
                    _sortingBandsPerWorldUnit));
            _editorPreviewRig.ApplyKey(EffectiveIdleMotion, 0);
            _editorPreviewRig.SetSelectionPresentation(
                _selected,
                _selectionMaterial,
                _selectedScale);
        }

        public void DestroyEditorPreview()
        {
            if (_editorPreviewRig == null)
                return;

            _editorPreviewRig.Destroy();
            _editorPreviewRig = null;
        }

        public void RefreshEditorPreviewFromScene()
        {
            var manager = FindFirstObjectByType<PawnManager>(
                FindObjectsInactive.Include);
            RefreshEditorPreview(
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

        private InteractivePawn EnsurePawn()
        {
            if (_pawn == null)
                _pawn = GetComponent<InteractivePawn>();
            return _pawn;
        }

        private PawnAppearance ResolveAppearance()
        {
            var pawn = EnsurePawn();
            if (_useDefinitionAppearance &&
                pawn != null &&
                pawn.Definition != null)
            {
                return pawn.Definition.DefaultAppearance;
            }

            return _appearance.WithVisibleColorDefaults();
        }

        private void ResolveDeterministicTiming()
        {
            var pawn = EnsurePawn();
            var token = pawn != null ? pawn.InstanceId : string.Empty;
            var hash = StableHash(token);
            var motion = EffectiveIdleMotion;
            var duration = motion != null ? motion.Duration : 1f;
            _phase = (hash & 0xFFu) / 255f * duration;
            _speedMultiplier =
                0.9f + ((hash >> 8) & 0xFFu) / 255f * 0.2f;
        }

        private static uint StableHash(string value)
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

        private bool IsEditorPreviewRenderer(SpriteRenderer renderer)
        {
#if UNITY_EDITOR
            if (_editorPreviewRig != null &&
                renderer.transform.IsChildOf(
                    _editorPreviewRig.RootObject.transform))
            {
                return true;
            }
#endif
            return false;
        }

        private void Awake()
        {
            EnsurePawn();
            if (Application.isPlaying)
                DestroyEditorPreviewIfPresentInPlayMode();
        }

        private void OnEnable()
        {
            EnsurePawn();
            if (Application.isPlaying && HasRuntimeBinding)
                SetLegacyRenderersHidden(IsModularEnabled);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall +=
                    DelayedEditorRefresh;
            }
#endif
        }

        private void OnDisable()
        {
            if (Application.isPlaying && _manager != null)
                _manager.ReleasePawnSpriteRig(this);
            else
                ReleaseRuntimeRig();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -=
                DelayedEditorRefresh;
            DestroyEditorPreview();
#endif
            SetLegacyRenderersHidden(false);
        }

        private void OnDestroy()
        {
            if (Application.isPlaying && _manager != null)
                _manager.ReleasePawnSpriteRig(this);
            else
                ReleaseRuntimeRig();
#if UNITY_EDITOR
            DestroyEditorPreview();
#endif
        }

        private void DestroyEditorPreviewIfPresentInPlayMode()
        {
#if UNITY_EDITOR
            DestroyEditorPreview();
#endif
        }

#if UNITY_EDITOR
        private void DelayedEditorRefresh()
        {
            if (this == null || Application.isPlaying)
                return;

            RefreshEditorPreviewFromScene();
        }

        private void OnValidate()
        {
            _appearance = _appearance.WithVisibleColorDefaults();
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -=
                    DelayedEditorRefresh;
                UnityEditor.EditorApplication.delayCall +=
                    DelayedEditorRefresh;
            }
        }
#endif
    }
}
