using System;
using System.Collections.Generic;
using Trpg.Domain.Stats;
using UnityEngine;

namespace Trpg.Data.Stats
{
    [Serializable]
    public sealed class CharacterBaseStatRecord
    {
        [SerializeField] private string _statId;
        [SerializeField] private float _value;

        public string StatId => _statId;
        public float Value => _value;

        public CharacterBaseStatRecord(string statId, float value)
        {
            _statId = statId;
            _value = value;
        }
    }

    [CreateAssetMenu(menuName = "Trpg/Stats/Character Stat Definition", fileName = "CharacterStats")]
    public sealed class CharacterStatDefinition : ScriptableObject, ICharacterStatDefinition
    {
        [SerializeField] private string _id = "character_stats_default";
        [SerializeField, Tooltip("비워두면 내장 CoC 7판 규칙을 사용합니다.")]
        private StatRuleTemplate _ruleTemplate;
        [SerializeField] private List<CharacterBaseStatRecord> _baseValues =
            CreateDefaultCocValues();

        private readonly List<StatBaseValue> _baseValueCache = new List<StatBaseValue>();

        public string Id => _id;
        public StatRuleTemplate RuleTemplateAsset => _ruleTemplate;
        public IStatRuleTemplate EffectiveRuleTemplate =>
            _ruleTemplate != null
                ? _ruleTemplate
                : StatRuleTemplateDefaults.Coc7;
        public bool UsesBuiltInCocRule => _ruleTemplate == null;
        IStatRuleTemplate ICharacterStatDefinition.RuleTemplate => EffectiveRuleTemplate;

        public IReadOnlyList<StatBaseValue> BaseValues
        {
            get
            {
                _baseValueCache.Clear();
                for (var i = 0; i < _baseValues.Count; i++)
                {
                    var value = _baseValues[i];
                    if (value != null)
                        _baseValueCache.Add(new StatBaseValue(value.StatId, value.Value));
                }
                return _baseValueCache;
            }
        }

        [ContextMenu("Reset Character Values To CoC Defaults")]
        public void ResetToCocDefaults()
        {
            _ruleTemplate = null;
            _baseValues = CreateDefaultCocValues();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public static List<CharacterBaseStatRecord> CreateDefaultCocValues()
        {
            return new List<CharacterBaseStatRecord>
            {
                new CharacterBaseStatRecord("coc.str", 50),
                new CharacterBaseStatRecord("coc.con", 50),
                new CharacterBaseStatRecord("coc.siz", 50),
                new CharacterBaseStatRecord("coc.dex", 50),
                new CharacterBaseStatRecord("coc.app", 50),
                new CharacterBaseStatRecord("coc.int", 50),
                new CharacterBaseStatRecord("coc.pow", 50),
                new CharacterBaseStatRecord("coc.edu", 50),
                new CharacterBaseStatRecord("coc.luck", 50),
                new CharacterBaseStatRecord("coc.cthulhu_mythos", 0)
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
                Debug.LogError($"[{name}] Id가 비어 있습니다.", this);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _baseValues.Count; i++)
            {
                var value = _baseValues[i];
                if (value == null || string.IsNullOrWhiteSpace(value.StatId))
                {
                    Debug.LogError($"[{name}] {i}번 기본 스탯 Id가 비어 있습니다.", this);
                    continue;
                }
                if (!ids.Add(value.StatId))
                    Debug.LogError($"[{name}] 중복 기본 스탯 Id: {value.StatId}", this);
            }
        }
#endif
    }
}
