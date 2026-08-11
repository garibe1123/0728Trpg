using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    public sealed class PawnSpriteRuntimeController
    {
        private readonly Transform _ownerRoot;
        private readonly PawnSpriteLibrary _library;
        private readonly PawnIdleMotion _defaultIdleMotion;
        private readonly Camera _camera;
        private readonly int _initialPoolSize;
        private readonly int _maxAcquirePerFrame;
        private readonly int _cullingInterval;
        private readonly float _cellWorldSize;
        private readonly float _enterMarginCells;
        private readonly float _exitMarginCells;
        private readonly int _pixelsPerUnit;
        private readonly float _sortingBandsPerWorldUnit;
        private readonly List<PawnSpriteAnimator> _animators =
            new List<PawnSpriteAnimator>();
        private readonly List<PawnSpriteRig> _allRigs =
            new List<PawnSpriteRig>();
        private readonly Stack<PawnSpriteRig> _freeRigs =
            new Stack<PawnSpriteRig>();
        private readonly Queue<PawnSpriteAnimator> _pendingAcquire =
            new Queue<PawnSpriteAnimator>();
        private readonly HashSet<PawnSpriteAnimator> _pendingSet =
            new HashSet<PawnSpriteAnimator>();

        private GameObject _poolRootObject;
        private Transform _poolRoot;
        private int _frameCounter;

        public PawnSpriteRuntimeController(
            Transform ownerRoot,
            PawnSpriteLibrary library,
            PawnIdleMotion defaultIdleMotion,
            Camera camera,
            int initialPoolSize,
            int maxAcquirePerFrame,
            int cullingInterval,
            float cellWorldSize,
            float enterMarginCells,
            float exitMarginCells,
            int pixelsPerUnit,
            float sortingBandsPerWorldUnit)
        {
            _ownerRoot = ownerRoot;
            _library = library;
            _defaultIdleMotion = defaultIdleMotion;
            _camera = camera;
            _initialPoolSize = Mathf.Max(0, initialPoolSize);
            _maxAcquirePerFrame = Mathf.Max(1, maxAcquirePerFrame);
            _cullingInterval = Mathf.Max(1, cullingInterval);
            _cellWorldSize = Mathf.Max(0.01f, cellWorldSize);
            _enterMarginCells = Mathf.Max(0f, enterMarginCells);
            _exitMarginCells = Mathf.Max(
                _enterMarginCells,
                exitMarginCells);
            _pixelsPerUnit = PixelSnap.NormalizePixelsPerUnit(
                pixelsPerUnit);
            _sortingBandsPerWorldUnit = Mathf.Max(
                0.01f,
                sortingBandsPerWorldUnit);

            if (_library != null)
                BuildPool();
        }

        public void BindPawns(IReadOnlyList<InteractivePawn> pawns)
        {
            ReleaseAllRigs();
            for (var index = 0; index < _animators.Count; index++)
                _animators[index]?.UnbindRuntime();

            _animators.Clear();
            _pendingAcquire.Clear();
            _pendingSet.Clear();

            if (_library == null || pawns == null)
                return;

            for (var index = 0; index < pawns.Count; index++)
                RegisterPawn(pawns[index], false);

            EvaluateCulling(true);
            ProcessAcquireQueue();
        }

        public void RegisterPawn(
            InteractivePawn pawn,
            bool evaluateImmediately = true)
        {
            if (_library == null ||
                pawn == null ||
                pawn.Definition == null ||
                !pawn.UsesModularSpriteMotion)
            {
                return;
            }

            var animator = pawn.GetComponent<PawnSpriteAnimator>();
            if (animator == null)
                animator = pawn.gameObject.AddComponent<PawnSpriteAnimator>();

            animator.BindRuntime(
                FindManager(),
                _library,
                _defaultIdleMotion,
                _pixelsPerUnit,
                _sortingBandsPerWorldUnit);
            if (!_animators.Contains(animator))
                _animators.Add(animator);

            if (!evaluateImmediately)
                return;

            QueueAcquire(animator);
            EvaluateCulling(true);
            ProcessAcquireQueue();
        }

        public void UnregisterPawn(PawnSpriteAnimator animator)
        {
            if (animator == null)
                return;

            ReleaseRig(animator);
            _animators.Remove(animator);
            _pendingSet.Remove(animator);
            animator.UnbindRuntime();
        }

        public void Update(float time)
        {
            if (_library == null || _poolRoot == null)
                return;

            _frameCounter++;
            if (_frameCounter >= _cullingInterval)
            {
                _frameCounter = 0;
                EvaluateCulling(false);
            }

            ProcessAcquireQueue();
            for (var index = 0; index < _animators.Count; index++)
            {
                var animator = _animators[index];
                if (animator != null && animator.HasRuntimeRig)
                    animator.UpdateRuntimeVisual(time);
            }
        }

        public void SetSelection(
            InteractivePawn pawn,
            bool selected,
            Material material,
            float selectedScale)
        {
            if (pawn == null)
                return;

            var animator = pawn.GetComponent<PawnSpriteAnimator>();
            animator?.SetSelected(
                selected,
                material,
                selectedScale);
        }

        public void RefreshAppearance(InteractivePawn pawn)
        {
            if (pawn == null)
                return;

            var animator = pawn.GetComponent<PawnSpriteAnimator>();
            if (animator == null && pawn.UsesModularSpriteMotion)
            {
                RegisterPawn(pawn);
                return;
            }

            animator?.RefreshRuntimeAppearance();
            if (animator != null && !animator.HasRuntimeRig)
            {
                QueueAcquire(animator);
                ProcessAcquireQueue();
            }
        }

        public void ReleaseAnimator(PawnSpriteAnimator animator)
        {
            ReleaseRig(animator);
        }

        public void Dispose()
        {
            ReleaseAllRigs();
            for (var index = 0; index < _animators.Count; index++)
                _animators[index]?.UnbindRuntime();
            _animators.Clear();

            for (var index = 0; index < _allRigs.Count; index++)
                _allRigs[index]?.Destroy();
            _allRigs.Clear();
            _freeRigs.Clear();
            _pendingAcquire.Clear();
            _pendingSet.Clear();

            if (_poolRootObject != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_poolRootObject);
                else
                    Object.DestroyImmediate(_poolRootObject);
                _poolRootObject = null;
                _poolRoot = null;
            }
        }

        private void BuildPool()
        {
            _poolRootObject = new GameObject("PawnSpriteRigPool");
            _poolRoot = _poolRootObject.transform;
            _poolRoot.SetParent(null, false);
            _poolRoot.position = Vector3.zero;
            _poolRoot.rotation = Quaternion.identity;
            _poolRoot.localScale = Vector3.one;

            for (var index = 0; index < _initialPoolSize; index++)
                _freeRigs.Push(CreateRig());
        }

        private PawnSpriteRig CreateRig()
        {
            var rig = new PawnSpriteRig(
                _library,
                _poolRoot,
                $"PawnSpriteRig_{_allRigs.Count:00}",
                HideFlags.None,
                _pixelsPerUnit);
            _allRigs.Add(rig);
            return rig;
        }

        private void EvaluateCulling(bool immediate)
        {
            if (_camera == null || !_camera.orthographic)
            {
                for (var index = 0; index < _animators.Count; index++)
                {
                    var animator = _animators[index];
                    var pawn = animator != null ? animator.Pawn : null;
                    if (animator == null ||
                        !animator.isActiveAndEnabled ||
                        !animator.IsModularEnabled ||
                        pawn == null ||
                        !pawn.IsSelectableForLocalViewer)
                    {
                        ReleaseRig(animator);
                        continue;
                    }

                    QueueAcquire(animator);
                }
                return;
            }

            var center = (Vector2)_camera.transform.position;
            var halfHeight = _camera.orthographicSize;
            var halfWidth = halfHeight * _camera.aspect;
            var enterMargin = _enterMarginCells * _cellWorldSize;
            var exitMargin = _exitMarginCells * _cellWorldSize;
            var enterRect = Rect.MinMaxRect(
                center.x - halfWidth - enterMargin,
                center.y - halfHeight - enterMargin,
                center.x + halfWidth + enterMargin,
                center.y + halfHeight + enterMargin);
            var exitRect = Rect.MinMaxRect(
                center.x - halfWidth - exitMargin,
                center.y - halfHeight - exitMargin,
                center.x + halfWidth + exitMargin,
                center.y + halfHeight + exitMargin);

            for (var index = 0; index < _animators.Count; index++)
            {
                var animator = _animators[index];
                if (animator == null ||
                    !animator.isActiveAndEnabled ||
                    !animator.IsModularEnabled)
                {
                    ReleaseRig(animator);
                    continue;
                }

                var pawn = animator.Pawn;
                if (pawn == null || !pawn.IsSelectableForLocalViewer)
                {
                    ReleaseRig(animator);
                    continue;
                }

                var position = (Vector2)pawn.ModularVisualWorldPosition;
                if (animator.HasRuntimeRig)
                {
                    if (!exitRect.Contains(position))
                        ReleaseRig(animator);
                }
                else if (enterRect.Contains(position))
                {
                    QueueAcquire(animator);
                }
            }

            if (immediate)
                _frameCounter = 0;
        }

        private void QueueAcquire(PawnSpriteAnimator animator)
        {
            if (animator == null ||
                animator.HasRuntimeRig ||
                !_pendingSet.Add(animator))
            {
                return;
            }

            _pendingAcquire.Enqueue(animator);
        }

        private void ProcessAcquireQueue()
        {
            var count = Mathf.Min(
                _maxAcquirePerFrame,
                _pendingAcquire.Count);
            for (var index = 0; index < count; index++)
                AcquireNext();
        }

        private void AcquireNext()
        {
            if (_pendingAcquire.Count == 0)
                return;

            var animator = _pendingAcquire.Dequeue();
            if (!_pendingSet.Remove(animator))
                return;

            if (animator == null ||
                !animator.isActiveAndEnabled ||
                animator.HasRuntimeRig ||
                !animator.IsModularEnabled)
            {
                return;
            }

            var rig = _freeRigs.Count > 0
                ? _freeRigs.Pop()
                : CreateRig();
            animator.AssignRuntimeRig(rig);
            if (!animator.HasRuntimeRig)
                _freeRigs.Push(rig);
        }

        private void ReleaseRig(PawnSpriteAnimator animator)
        {
            if (animator == null)
                return;

            _pendingSet.Remove(animator);
            var rig = animator.DetachRuntimeRig();
            if (rig == null)
                return;

            rig.Release();
            _freeRigs.Push(rig);
        }

        private void ReleaseAllRigs()
        {
            for (var index = 0; index < _animators.Count; index++)
                ReleaseRig(_animators[index]);
        }

        private PawnManager FindManager()
        {
            return _ownerRoot != null
                ? _ownerRoot.GetComponent<PawnManager>()
                : null;
        }
    }
}
