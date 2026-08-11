using System;
using UnityEngine;

namespace Trpg.Pawns
{
    public sealed class PawnSpriteRig
    {
        public const int SortingBandSize = 128;

        private readonly PawnSpriteLibrary _library;
        private readonly GameObject _rootObject;
        private readonly Transform _rootTransform;
        private readonly Transform[] _slotTransforms;
        private readonly SpriteRenderer[] _renderers;
        private readonly Material[] _defaultRendererMaterials;
        private readonly Material[] _restoredMaterials;
        private PawnAppearance _appearance;
        private bool _isSelected;
        private Material _selectionMaterialTemplate;
        private Material _selectionMaterial;
        private bool _isVisible;
        private readonly int _pixelsPerUnit;
        private float _presentationScale = 1f;
        private bool _facingLeft;

        public PawnSpriteAnimator Owner { get; private set; }
        public GameObject RootObject => _rootObject;
        public bool IsAssigned => Owner != null;
        public bool IsVisible => _isVisible;

        public PawnSpriteRig(
            PawnSpriteLibrary library,
            Transform parent,
            string name,
            HideFlags hideFlags = HideFlags.None,
            int pixelsPerUnit = PixelSnap.DefaultPixelsPerUnit)
        {
            _library = library ??
                throw new ArgumentNullException(nameof(library));
            _pixelsPerUnit = PixelSnap.NormalizePixelsPerUnit(
                pixelsPerUnit);
            _rootObject = new GameObject(name)
            {
                hideFlags = hideFlags
            };
            _rootTransform = _rootObject.transform;
            _rootTransform.SetParent(parent, false);
            _rootTransform.localPosition = Vector3.zero;
            _rootTransform.localRotation = Quaternion.identity;
            _rootTransform.localScale = Vector3.one;

            var count = (int)PartSlot.Count;
            _slotTransforms = new Transform[count];
            _renderers = new SpriteRenderer[count];
            _defaultRendererMaterials = new Material[count];
            _restoredMaterials = new Material[count];

            CreateSlots(hideFlags);
            ApplyFixedHierarchy();
            SetVisible(false);
        }

        public void Assign(
            PawnSpriteAnimator owner,
            in PawnAppearance appearance)
        {
            Owner = owner;
            _isSelected = false;
            _selectionMaterialTemplate = null;
            _selectionMaterial = null;
            _presentationScale = 1f;
            _facingLeft = owner != null &&
                          owner.Pawn != null &&
                          owner.Pawn.FacingLeft;
            ApplyRootScale();
            ApplyAppearance(appearance);
            SetVisible(true);
        }

        public void Release()
        {
            Owner = null;
            _isSelected = false;
            _selectionMaterialTemplate = null;
            _selectionMaterial = null;
            _presentationScale = 1f;
            _facingLeft = false;
            ApplyRootScale();
            SetVisible(false);
            _rootTransform.localPosition = Vector3.zero;
            _rootTransform.localRotation = Quaternion.identity;
            _rootTransform.localScale = Vector3.one;
        }

        public void ApplyAppearance(in PawnAppearance appearance)
        {
            _appearance = appearance.WithVisibleColorDefaults();
            var paletteMaterial =
                _library.ResolvePaletteMaterial(_appearance);

            for (var slotIndex = 0;
                 slotIndex < (int)PartSlot.Count;
                 slotIndex++)
            {
                var slot = (PartSlot)slotIndex;
                var renderer = _renderers[slotIndex];
                if (renderer == null)
                    continue;

                var sprite = _library.ResolveSprite(
                    slot,
                    _appearance.GetPartId(slot),
                    _appearance.Height,
                    _appearance.BroadShoulders,
                    _pixelsPerUnit);

                renderer.sprite = sprite;
                renderer.enabled = sprite != null;

                _restoredMaterials[slotIndex] =
                    paletteMaterial != null
                        ? paletteMaterial
                        : _defaultRendererMaterials[slotIndex];
            }

            if (_isSelected && _selectionMaterialTemplate != null)
            {
                _selectionMaterial = _library.ResolveSelectionMaterial(
                    _selectionMaterialTemplate,
                    _appearance);
            }
            ApplyCurrentMaterials();
        }

        public void ApplyKey(
            PawnIdleMotion motion,
            int keyIndex)
        {
            for (var slotIndex = 0;
                 slotIndex < (int)PartSlot.Count;
                 slotIndex++)
            {
                var slot = (PartSlot)slotIndex;
                var target = _slotTransforms[slotIndex];
                if (target == null)
                    continue;

                var offset = Vector2Int.zero;
                if (PawnPartSlotRules.HasAnimationChannel(slot))
                {
                    var channel =
                        PawnPartSlotRules.GetAnimationChannel(slot);
                    offset = _library.GetBodyOffset(
                        _appearance.Height,
                        channel);
                    if (motion != null)
                    {
                        offset += motion.EvaluateOffset(
                            channel,
                            keyIndex);
                    }
                }

                var local = PixelSnap.PixelsToUnits(
                    offset,
                    _pixelsPerUnit);
                target.localPosition = new Vector3(local.x, local.y, 0f);
                target.localRotation = Quaternion.identity;
                target.localScale = Vector3.one;
            }
        }

        public void SetWorldPosition(Vector3 worldPosition)
        {
            _rootTransform.position = PixelSnap.SnapWorld(
                worldPosition,
                _pixelsPerUnit);
            _rootTransform.rotation = Quaternion.identity;
            ApplyRootScale();
        }

        public static int CalculateSortingBand(
            float worldY,
            float bandsPerWorldUnit)
        {
            var precision = Mathf.Max(0.01f, bandsPerWorldUnit);
            var value = -(double)worldY * precision;
            var maxBand = (int.MaxValue - (SortingBandSize - 1)) /
                          SortingBandSize;
            var minBand = (int.MinValue + (SortingBandSize - 1)) /
                          SortingBandSize;
            if (value >= maxBand)
                return maxBand;
            if (value <= minBand)
                return minBand;
            return Mathf.RoundToInt((float)value);
        }

        public void SetSortingBand(int yBand)
        {
            var baseOrder = yBand * SortingBandSize;
            for (var slotIndex = 0;
                 slotIndex < (int)PartSlot.Count;
                 slotIndex++)
            {
                var renderer = _renderers[slotIndex];
                if (renderer == null)
                    continue;

                var slot = (PartSlot)slotIndex;
                renderer.sortingOrder = baseOrder +
                    PawnPartSlotRules.GetSortingOrder(slot);
            }
        }

        public void SetFacingLeft(bool facingLeft)
        {
            if (_facingLeft == facingLeft)
                return;

            _facingLeft = facingLeft;
            ApplyRootScale();
        }

        public void SetSelectionPresentation(
            bool selected,
            Material selectionMaterial,
            float selectedScale)
        {
            _isSelected = selected && selectionMaterial != null;
            _selectionMaterialTemplate = selectionMaterial;
            _selectionMaterial = _isSelected
                ? _library.ResolveSelectionMaterial(
                    selectionMaterial,
                    _appearance)
                : null;
            _presentationScale = selected
                ? Mathf.Max(1f, selectedScale)
                : 1f;
            ApplyRootScale();
            ApplyCurrentMaterials();
        }

        public void SetSelectionMaterial(
            bool selected,
            Material selectionMaterial)
        {
            SetSelectionPresentation(
                selected,
                selectionMaterial,
                1f);
        }

        public void SetVisible(bool visible)
        {
            _isVisible = visible;
            _rootObject.SetActive(visible);
        }

        public void Destroy()
        {
            if (_rootObject == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_rootObject);
            else
                UnityEngine.Object.DestroyImmediate(_rootObject);
        }

        private void ApplyRootScale()
        {
            var scale = Mathf.Max(0.01f, _presentationScale);
            _rootTransform.localScale = new Vector3(
                _facingLeft ? -scale : scale,
                scale,
                1f);
        }

        private void CreateSlots(HideFlags hideFlags)
        {
            for (var slotIndex = 0;
                 slotIndex < (int)PartSlot.Count;
                 slotIndex++)
            {
                var slot = (PartSlot)slotIndex;
                var child = new GameObject(slot.ToString())
                {
                    hideFlags = hideFlags
                };
                var childTransform = child.transform;
                childTransform.SetParent(_rootTransform, false);
                childTransform.localPosition = Vector3.zero;
                childTransform.localRotation = Quaternion.identity;
                childTransform.localScale = Vector3.one;
                var renderer = child.AddComponent<SpriteRenderer>();
                renderer.enabled = false;
                _slotTransforms[slotIndex] = childTransform;
                _renderers[slotIndex] = renderer;
                _defaultRendererMaterials[slotIndex] = renderer.sharedMaterial;
            }
        }

        private void ApplyFixedHierarchy()
        {
            for (var slotIndex = 0;
                 slotIndex < (int)PartSlot.Count;
                 slotIndex++)
            {
                var slot = (PartSlot)slotIndex;
                var target = _slotTransforms[slotIndex];
                if (PawnPartSlotRules.ParentIsRigRoot(slot))
                {
                    target.SetParent(_rootTransform, false);
                }
                else
                {
                    var parentSlot =
                        PawnPartSlotRules.GetParentSlot(slot);
                    var parentIndex = (int)parentSlot;
                    var parent = parentIndex >= 0 &&
                                 parentIndex < _slotTransforms.Length
                        ? _slotTransforms[parentIndex]
                        : _rootTransform;
                    target.SetParent(parent, false);
                }

                target.localPosition = Vector3.zero;
                target.localRotation = Quaternion.identity;
                target.localScale = Vector3.one;
            }
        }

        private void ApplyCurrentMaterials()
        {
            if (_isSelected && _selectionMaterial != null)
            {
                for (var index = 0; index < _renderers.Length; index++)
                {
                    var renderer = _renderers[index];
                    if (renderer != null && renderer.enabled)
                        renderer.sharedMaterial = _selectionMaterial;
                }

                return;
            }

            RestoreMaterials();
        }

        private void RestoreMaterials()
        {
            for (var index = 0; index < _renderers.Length; index++)
            {
                var renderer = _renderers[index];
                if (renderer != null)
                    renderer.sharedMaterial = _restoredMaterials[index];
            }
        }
    }
}
