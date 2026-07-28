using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;

namespace Trpg.Pawns
{
    public sealed class FieldPawn : Pawn
    {
        [SerializeField, Tooltip("Floor 또는 Obstacle 데이터. Portrait 데이터는 포함하지 않음")]
        private FieldPawnDefinition _definition;

        [SerializeField, Tooltip("2D NavMesh Bake 형상에 사용할 Collider2D")]
        private Collider2D _navigationCollider;

        [SerializeField, Tooltip("Walkable 또는 Not Walkable을 적용할 Modifier")]
        private NavMeshModifier _navigationModifier;

        public FieldPawnDefinition Definition => _definition;
        public FieldPawnKind Kind =>
            _definition != null ? _definition.Kind : FieldPawnKind.Floor;
        public bool IsFloor => Kind == FieldPawnKind.Floor;
        public bool IsObstacle => Kind == FieldPawnKind.Obstacle;

        public override void Bind()
        {
            EnsureNavigationComponents();
            base.Bind();
        }

        public override void Unbind()
        {
            base.Unbind();
        }

        public void PrepareNavigation()
        {
            EnsureNavigationComponents();
        }

        private void Awake()
        {
            EnsureNavigationComponents();
        }

        private void Reset()
        {
            EnsureNavigationComponents();
        }

        private void EnsureNavigationComponents()
        {
            if (_definition == null)
            {
                return;
            }

            if (_navigationCollider == null)
            {
                _navigationCollider =
                    GetComponentInChildren<Collider2D>(true);
            }

            if (_navigationCollider == null)
            {
                _navigationCollider =
                    gameObject.AddComponent<BoxCollider2D>();
            }

            if (_navigationModifier == null)
            {
                _navigationModifier = GetComponent<NavMeshModifier>();
            }

            if (_navigationModifier == null)
            {
                _navigationModifier =
                    gameObject.AddComponent<NavMeshModifier>();
            }

            var areaName = IsObstacle ? "Not Walkable" : "Walkable";
            var fallbackArea = IsObstacle ? 1 : 0;
            var area = NavMesh.GetAreaFromName(areaName);
            _navigationModifier.overrideArea = true;
            _navigationModifier.area = area >= 0 ? area : fallbackArea;

        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureNavigationComponents();

            if (_definition == null)
            {
                Debug.LogError($"[{name}] Field Definition이 비어 있습니다.", this);
            }
        }
#endif
    }
}
