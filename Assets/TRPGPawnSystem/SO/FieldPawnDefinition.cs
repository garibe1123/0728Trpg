using UnityEngine;

namespace Trpg.Pawns
{
    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Field Pawn Definition",
        fileName = "FieldPawnDefinition")]
    public sealed class FieldPawnDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("콘텐츠 식별 ID")]
        private string _id = "field_new";

        [SerializeField, Tooltip("Floor 또는 Obstacle 구분")]
        private FieldPawnKind _kind = FieldPawnKind.Floor;

        public string Id => _id;
        public FieldPawnKind Kind => _kind;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError($"[{name}] Definition Id가 비어 있습니다.", this);
            }
        }
#endif
    }
}
