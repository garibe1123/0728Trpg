using UnityEngine;

namespace Trpg.Data.Skills
{
    [CreateAssetMenu(
        menuName = "Trpg/Skills/Skill Definition",
        fileName = "SkillDefinition")]
    public sealed class SkillDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("세이브와 네트워크에서 사용하는 고유 기술 ID")]
        private string _id = "skill.new";

        [SerializeField, Tooltip("UI에 표시할 기술 이름")]
        private string _displayName = "새 기술";

        [SerializeField, Tooltip("UI에서 기술을 묶어 표시할 분류")]
        private string _category = "일반";

        [SerializeField, Min(0), Tooltip(
            "기술을 따로 올리지 않은 캐릭터가 사용하는 미훈련 기본 성공값")]
        private int _baseValue = 1;

        [SerializeField, Tooltip(
            "켜면 Keeper가 허용하지 않는 한 미훈련 상태로 판정할 수 없는 기술")]
        private bool _requiresTraining;

        [SerializeField, Tooltip("같은 분류 안에서 낮을수록 먼저 표시")]
        private int _sortOrder;

        [SerializeField, TextArea(2, 5), Tooltip("기술 설명")]
        private string _description;

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Category => _category;
        public int BaseValue => Mathf.Max(0, _baseValue);
        public bool RequiresTraining => _requiresTraining;
        public int SortOrder => _sortOrder;
        public string Description => _description;

#if UNITY_EDITOR
        private void OnValidate()
        {
            _baseValue = Mathf.Max(0, _baseValue);

            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError(
                    $"[{name}] Skill Definition Id가 비어 있습니다.",
                    this);
            }

            if (string.IsNullOrWhiteSpace(_displayName))
            {
                Debug.LogError(
                    $"[{name}] Skill 표시 이름이 비어 있습니다.",
                    this);
            }
        }
#endif
    }
}
