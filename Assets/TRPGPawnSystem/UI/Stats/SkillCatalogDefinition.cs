using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Data.Skills
{
    [CreateAssetMenu(
        menuName = "Trpg/Skills/Skill Catalog",
        fileName = "SkillCatalogDefinition")]
    public sealed class SkillCatalogDefinition : ScriptableObject
    {
        [SerializeField, Tooltip(
            "캐릭터 스킬 추가 UI에서 선택할 수 있는 전체 Skill Definition 목록")]
        private List<SkillDefinition> _skills =
            new List<SkillDefinition>();

        public IReadOnlyList<SkillDefinition> Skills => _skills;

        public bool TryGetById(
            string skillId,
            out SkillDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(skillId) || _skills == null)
                return false;

            for (var index = 0; index < _skills.Count; index++)
            {
                var candidate = _skills[index];
                if (candidate != null &&
                    string.Equals(
                        candidate.Id,
                        skillId,
                        StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_skills == null)
            {
                _skills = new List<SkillDefinition>();
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < _skills.Count; index++)
            {
                var skill = _skills[index];
                if (skill == null)
                {
                    Debug.LogError(
                        $"[{name}] {index}번 Skill Definition이 비어 있습니다.",
                        this);
                    continue;
                }

                if (!ids.Add(skill.Id))
                {
                    Debug.LogError(
                        $"[{name}] 중복 Skill ID: {skill.Id}",
                        this);
                }
            }
        }
#endif
    }
}
