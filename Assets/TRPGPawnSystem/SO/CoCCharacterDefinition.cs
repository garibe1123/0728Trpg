using UnityEngine;

namespace Trpg.Data.Coc
{
    [CreateAssetMenu(
        menuName = "Trpg/CoC/Character Definition",
        fileName = "CoCCharacter")]
    public sealed class CoCCharacterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Tooltip("저장 및 네트워크 연결에 사용하는 고유 ID")]
        private string _id = "coc_character_new";

        [SerializeField, Tooltip("화면에 표시할 캐릭터 이름")]
        private string _displayName = "New Investigator";

        [SerializeField, Min(15), Tooltip("MOV 연령 보정에 사용하는 나이")]
        private int _age = 25;

        [Header("Base Stats")]
        [SerializeField, Range(0, 100)] private int _strength = 50;
        [SerializeField, Range(0, 100)] private int _constitution = 50;
        [SerializeField, Range(0, 100)] private int _size = 50;
        [SerializeField, Range(0, 100)] private int _dexterity = 50;
        [SerializeField, Range(0, 100)] private int _appearance = 50;
        [SerializeField, Range(0, 100)] private int _intelligence = 50;
        [SerializeField, Range(0, 100)] private int _power = 50;
        [SerializeField, Range(0, 100)] private int _education = 50;
        [SerializeField, Range(0, 100)] private int _luck = 50;

        public string Id => _id;
        public string DisplayName => _displayName;
        public int Age => _age;

        public int Strength => _strength;
        public int Constitution => _constitution;
        public int Size => _size;
        public int Dexterity => _dexterity;
        public int Appearance => _appearance;
        public int Intelligence => _intelligence;
        public int Power => _power;
        public int Education => _education;
        public int Luck => _luck;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError($"[{name}] Character Id가 비어 있습니다.", this);
            }

            _age = Mathf.Max(15, _age);
        }
#endif
    }
}
