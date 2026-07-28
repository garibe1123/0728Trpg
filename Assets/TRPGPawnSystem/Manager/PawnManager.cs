using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
namespace Trpg.Pawns
{
    public sealed class PawnManager : MonoBehaviour
    {
        [SerializeField, Tooltip("Pawn 자식들을 한 번 자동 수집할 부모")]
        private Transform _pawnRoot;
        [SerializeField, Tooltip("2D 보드를 보여주는 Orthographic Camera")]
        private Camera _boardCamera;
        [FormerlySerializedAs("_pawnLayerMask")]
        [SerializeField, Tooltip("선택할 InteractivePawn Collider2D의 Layer")]
        private LayerMask _interactivePawnLayerMask = ~0;
        [Header("Selection Materials")]
        [SerializeField, Tooltip("선택되지 않은 Interactive Pawn에 적용할 Material")]
        private Material _defaultMaterial;
        [SerializeField, Tooltip("선택된 Interactive Pawn에 적용할 Material")]
        private Material _activeMaterial;
        [SerializeField] private PawnMovementManager _movementManager;
        private readonly List<Pawn> _pawns = new List<Pawn>();
        private readonly List<InteractivePawn> _interactivePawns =
            new List<InteractivePawn>();
        private readonly Dictionary<InteractivePawn, SpriteRenderer[]>
            _interactiveRenderers =
                new Dictionary<InteractivePawn, SpriteRenderer[]>();
        private InputAction _selectAction;
        private InputAction _moveAction;
        private InputAction _pointAction;
        private InteractivePawn _selectedInteractive;
        public event Action<InteractivePawn> InteractiveSelectionChanged;
        public event Action<InteractivePawn> InteractionRequested;
        public InteractivePawn SelectedInteractive => _selectedInteractive;
        public PawnMovementManager MovementManager => _movementManager;
        public Camera BoardCamera => _boardCamera;
        public void ClearSelection()
        {
            if (_selectedInteractive == null)
            {
                return;
            }
            ApplySelectionPresentation(
                _selectedInteractive,
                _defaultMaterial,
                false);
            _selectedInteractive = null;
            _movementManager.ClearMover();
            InteractiveSelectionChanged?.Invoke(null);
        }
        private void Awake()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }
            IndexPawns();
            ValidateSelectionMaterials();
            _selectAction = new InputAction(
                "PawnSelect",
                InputActionType.Button,
                "<Pointer>/leftButton");
            _moveAction = new InputAction(
                "PawnMove",
                InputActionType.Button,
                "<Pointer>/rightButton");
            _pointAction = new InputAction(
                "PawnPoint",
                InputActionType.PassThrough,
                "<Pointer>/position");
        }
        private void OnEnable()
        {
            if (_selectAction == null ||
                _moveAction == null ||
                _pointAction == null)
            {
                return;
            }
            for (var index = 0; index < _pawns.Count; index++)
            {
                _pawns[index].Bind();
            }
            ApplyDefaultPresentations();
            ApplySelectionPresentation(
                _selectedInteractive,
                _activeMaterial,
                true);
            _movementManager.Bind(_interactivePawns);
            _selectAction.performed += HandleSelectPerformed;
            _moveAction.performed += HandleMovePerformed;
            _pointAction.performed += HandlePointPerformed;
            _selectAction.Enable();
            _moveAction.Enable();
            _pointAction.Enable();
        }
        private void OnDisable()
        {
            if (_selectAction != null)
            {
                _selectAction.performed -= HandleSelectPerformed;
                _selectAction.Disable();
            }
            if (_moveAction != null)
            {
                _moveAction.performed -= HandleMovePerformed;
                _moveAction.Disable();
            }
            if (_pointAction != null)
            {
                _pointAction.performed -= HandlePointPerformed;
                _pointAction.Disable();
            }
            ApplySelectionPresentation(
                _selectedInteractive,
                _defaultMaterial,
                false);
            _movementManager.Unbind();
            for (var index = 0; index < _pawns.Count; index++)
            {
                if (_pawns[index] != null)
                {
                    _pawns[index].Unbind();
                }
            }
        }
        private void OnDestroy()
        {
            _selectAction?.Dispose();
            _moveAction?.Dispose();
            _pointAction?.Dispose();
        }
        private void HandleSelectPerformed(
            InputAction.CallbackContext context)
        {
            if (!TryGetPointerWorldPosition(out var worldPosition))
            {
                return;
            }
            var colliders = Physics2D.OverlapPointAll(
                worldPosition,
                _interactivePawnLayerMask);
            var target = ResolveInteractiveTarget(colliders);
            if (target != null)
            {
                SelectInteractive(target);
                return;
            }
            ClearSelection();
        }
        private void HandleMovePerformed(
            InputAction.CallbackContext context)
        {
            if (TryGetPointerWorldPosition(out var worldPosition))
            {
                _movementManager.TryMoveSelectedTo(worldPosition);
            }
        }
        private void HandlePointPerformed(
            InputAction.CallbackContext context)
        {
            RefreshPointerPreview();
        }
        private InteractivePawn ResolveInteractiveTarget(
            IReadOnlyList<Collider2D> colliders)
        {
            for (var index = 0; index < colliders.Count; index++)
            {
                var pawn = colliders[index].GetComponentInParent<Pawn>();
                if (pawn == null || !pawn.IsBound)
                {
                    continue;
                }
                if (pawn is InteractivePawn interactive)
                {
                    return interactive;
                }
            }
            return null;
        }
        private bool TryGetPointerWorldPosition(out Vector2 worldPosition)
        {
            return TryGetPointerWorldPosition(
                out worldPosition,
                out _);
        }
        private bool TryGetPointerWorldPosition(
            out Vector2 worldPosition,
            out Vector2 screenPosition)
        {
            worldPosition = default;
            screenPosition = default;
            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return false;
            }
            var pointer = Pointer.current;
            if (pointer == null)
            {
                return false;
            }
            screenPosition = pointer.position.ReadValue();
            var world = _boardCamera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, 0f));
            worldPosition = new Vector2(world.x, world.y);
            return true;
        }
        private void SelectInteractive(InteractivePawn pawn)
        {
            if (_selectedInteractive != null &&
                _selectedInteractive != pawn)
            {
                ApplySelectionPresentation(
                    _selectedInteractive,
                    _defaultMaterial,
                    false);
            }
            _selectedInteractive = pawn;
            ApplySelectionPresentation(
                _selectedInteractive,
                _activeMaterial,
                true);
            InteractiveSelectionChanged?.Invoke(_selectedInteractive);
            if (pawn.IsMoveable)
            {
                _movementManager.SelectMover(pawn);
                RefreshPointerPreview();
                return;
            }
            _movementManager.ClearMover();
            InteractionRequested?.Invoke(pawn);
        }
        private void RefreshPointerPreview()
        {
            if (_selectedInteractive == null ||
                !_selectedInteractive.IsMoveable ||
                !TryGetPointerWorldPosition(
                    out var worldPosition,
                    out var screenPosition))
            {
                _movementManager.HidePathPreview();
                return;
            }
            _movementManager.PreviewSelectedPath(
                worldPosition,
                screenPosition);
        }
        private void IndexPawns()
        {
            _pawns.Clear();
            _interactivePawns.Clear();
            _interactiveRenderers.Clear();
            var found = _pawnRoot.GetComponentsInChildren<Pawn>(true);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < found.Length; index++)
            {
                var pawn = found[index];
                _pawns.Add(pawn);
                if (string.IsNullOrWhiteSpace(pawn.InstanceId) ||
                    !ids.Add(pawn.InstanceId))
                {
                    Debug.LogError(
                        $"[{pawn.name}] 비어 있거나 중복된 Pawn Instance Id입니다.",
                        pawn);
                }
                if (pawn is InteractivePawn interactive)
                {
                    _interactivePawns.Add(interactive);
                    CacheInteractiveRenderers(interactive);
                }
            }
        }
        private void CacheInteractiveRenderers(InteractivePawn pawn)
        {
            var renderers =
                pawn.GetComponentsInChildren<SpriteRenderer>(true);
            _interactiveRenderers[pawn] = renderers;
            if (renderers.Length == 0)
            {
                Debug.LogWarning(
                    $"[{pawn.name}] 선택 Material을 적용할 " +
                    "SpriteRenderer가 없습니다.",
                    pawn);
            }
        }
        private void ApplyDefaultPresentations()
        {
            for (var index = 0;
                 index < _interactivePawns.Count;
                 index++)
            {
                ApplySelectionPresentation(
                    _interactivePawns[index],
                    _defaultMaterial,
                    false);
            }
        }
        private void ApplySelectionPresentation(
            InteractivePawn pawn,
            Material material,
            bool selected)
        {
            if (pawn == null)
            {
                return;
            }
            pawn.SetSelected(selected);
            if (material == null)
            {
                return;
            }
            if (!_interactiveRenderers.TryGetValue(pawn, out var renderers) ||
                renderers == null ||
                renderers.Length == 0)
            {
                renderers =
                    pawn.GetComponentsInChildren<SpriteRenderer>(true);
                _interactiveRenderers[pawn] = renderers;
            }
            if (renderers.Length == 0)
            {
                return;
            }
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].sharedMaterial = material;
                }
            }
        }
        private void ValidateSelectionMaterials()
        {
            if (_defaultMaterial != null && _activeMaterial != null)
            {
                return;
            }
            Debug.LogWarning(
                $"[{name}] Default Material 또는 Active Material이 " +
                "비어 있어 선택 Material 표시가 적용되지 않습니다.",
                this);
        }
        private bool HasRequiredReferences()
        {
            var valid =
                _pawnRoot != null &&
                _boardCamera != null &&
                _movementManager != null;
            if (!valid)
            {
                Debug.LogError(
                    $"[{name}] PawnManager 필수 참조가 비어 있습니다.",
                    this);
            }
            return valid;
        }
    }
}
