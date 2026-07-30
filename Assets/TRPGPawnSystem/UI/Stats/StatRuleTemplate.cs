using System;
using System.Collections.Generic;
using Trpg.Domain.Stats;
using UnityEngine;

namespace Trpg.Data.Stats
{
    [Serializable]
    public sealed class StatLookupBandRecord : IStatLookupBand
    {
        [SerializeField, Tooltip("처음으로 참이 되는 구간을 사용합니다.")]
        private string _condition = "1";
        [SerializeField] private float _numericValue;
        [SerializeField, Tooltip("주사위처럼 숫자로 표현할 수 없는 결과입니다.")]
        private string _displayText;

        public string Condition => _condition;
        public double NumericValue => _numericValue;
        public string DisplayText => _displayText;

        public StatLookupBandRecord(
            string condition,
            float numericValue,
            string displayText = null)
        {
            _condition = condition;
            _numericValue = numericValue;
            _displayText = displayText;
        }
    }

    [Serializable]
    public sealed class StatDefinitionRecord : IStatDefinition
    {
        [SerializeField, Tooltip("세이브와 네트워크에 사용하는 고유 ID입니다.")]
        private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private string _category = "기본 능력치";
        [SerializeField] private StatValueSource _source;
        [SerializeField] private StatDisplayKind _displayKind;
        [SerializeField] private StatVisibility _visibility = StatVisibility.Self;
        [SerializeField] private int _sortOrder;
        [SerializeField] private bool _showInSummary;
        [SerializeField] private bool _isAdjustable;
        [SerializeField] private float _defaultValue;
        [SerializeField] private float _minValue;
        [SerializeField] private float _maxValue = 9999f;
        [SerializeField, Min(0.01f)] private float _adjustStep = 1f;
        [SerializeField, TextArea(1, 3)] private string _formula;
        [SerializeField, TextArea(1, 3)] private string _initialValueFormula;
        [SerializeField, Tooltip("Current / Max 표시와 동적 상한에 사용할 스탯 ID입니다.")]
        private string _maxStatId;
        [SerializeField] private List<StatLookupBandRecord> _lookupBands =
            new List<StatLookupBandRecord>();

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Category => _category;
        public StatValueSource Source => _source;
        public StatDisplayKind DisplayKind => _displayKind;
        public StatVisibility Visibility => _visibility;
        public int SortOrder => _sortOrder;
        public bool ShowInSummary => _showInSummary;
        public bool IsAdjustable => _isAdjustable;
        public double DefaultValue => _defaultValue;
        public double MinValue => _minValue;
        public double MaxValue => _maxValue;
        public double AdjustStep => _adjustStep;
        public string Formula => _formula;
        public string InitialValueFormula => _initialValueFormula;
        public string MaxStatId => _maxStatId;
        public IReadOnlyList<IStatLookupBand> LookupBands => _lookupBands;

        public StatDefinitionRecord(
            string id,
            string displayName,
            string category,
            StatValueSource source,
            int sortOrder,
            float defaultValue = 0f,
            float minValue = 0f,
            float maxValue = 9999f,
            string formula = null,
            string initialValueFormula = null,
            string maxStatId = null,
            StatDisplayKind displayKind = StatDisplayKind.Number,
            bool isAdjustable = false,
            float adjustStep = 1f,
            bool showInSummary = false,
            StatVisibility visibility = StatVisibility.Self,
            List<StatLookupBandRecord> lookupBands = null)
        {
            _id = id;
            _displayName = displayName;
            _category = category;
            _source = source;
            _sortOrder = sortOrder;
            _defaultValue = defaultValue;
            _minValue = minValue;
            _maxValue = maxValue;
            _formula = formula;
            _initialValueFormula = initialValueFormula;
            _maxStatId = maxStatId;
            _displayKind = displayKind;
            _isAdjustable = isAdjustable;
            _adjustStep = adjustStep;
            _showInSummary = showInSummary;
            _visibility = visibility;
            _lookupBands = lookupBands ?? new List<StatLookupBandRecord>();
        }
    }

    [Serializable]
    public sealed class StatRoleBindingRecord
    {
        [SerializeField] private StatRole _role;
        [SerializeField] private string _statId;

        public StatRole Role => _role;
        public string StatId => _statId;

        public StatRoleBindingRecord(StatRole role, string statId)
        {
            _role = role;
            _statId = statId;
        }
    }

    [CreateAssetMenu(
        menuName = "Trpg/Stats/Custom Stat Rule Template",
        fileName = "CustomStatRuleTemplate")]
    public sealed class StatRuleTemplate : ScriptableObject, IStatRuleTemplate
    {
        [SerializeField] private string _id = "custom_rule";
        [SerializeField] private string _displayName = "Custom Rule";
        [SerializeField, Min(1)] private int _version = 1;
        [SerializeField] private List<StatDefinitionRecord> _stats =
            new List<StatDefinitionRecord>();
        [SerializeField] private List<StatRoleBindingRecord> _roleBindings =
            new List<StatRoleBindingRecord>();

        public string Id => _id;
        public string DisplayName => _displayName;
        public int Version => _version;
        public IReadOnlyList<IStatDefinition> Stats => _stats;
        public IReadOnlyList<StatDefinitionRecord> StatRecords => _stats;

        public string GetStatId(StatRole role)
        {
            for (var i = 0; i < _roleBindings.Count; i++)
            {
                if (_roleBindings[i].Role == role)
                    return _roleBindings[i].StatId;
            }

            return string.Empty;
        }

        [ContextMenu("Copy Built-In CoC 7th Rules")]
        public void CopyBuiltInCocRules()
        {
            _id = "coc7_custom";
            _displayName = "Call of Cthulhu 7th Custom";
            _version = StatRuleTemplateDefaults.Coc7.Version;
            _stats = StatRuleTemplateDefaults.CreateCocStats();
            _roleBindings = StatRuleTemplateDefaults.CreateCocRoles();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
                Debug.LogError($"[{name}] Id가 비어 있습니다.", this);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _stats.Count; i++)
            {
                var stat = _stats[i];
                if (stat == null || string.IsNullOrWhiteSpace(stat.Id))
                {
                    Debug.LogError($"[{name}] {i}번 스탯의 Id가 비어 있습니다.", this);
                    continue;
                }

                if (!ids.Add(stat.Id))
                    Debug.LogError($"[{name}] 중복 스탯 Id: {stat.Id}", this);
                if (stat.MinValue > stat.MaxValue)
                    Debug.LogError($"[{name}] {stat.Id}의 최소값이 최대값보다 큽니다.", this);
                if (stat.Source == StatValueSource.Formula &&
                    string.IsNullOrWhiteSpace(stat.Formula))
                    Debug.LogError($"[{name}] {stat.Id}의 공식이 비어 있습니다.", this);
                if (stat.Source == StatValueSource.LookupTable &&
                    stat.LookupBands.Count == 0)
                    Debug.LogError($"[{name}] {stat.Id}의 조건표가 비어 있습니다.", this);
            }
        }
#endif
    }

    public static class StatRuleTemplateDefaults
    {
        private sealed class BuiltInRuleTemplate : IStatRuleTemplate
        {
            private readonly List<IStatDefinition> _stats;
            private readonly Dictionary<StatRole, string> _roleBindings;

            public string Id { get; }
            public string DisplayName { get; }
            public int Version { get; }
            public IReadOnlyList<IStatDefinition> Stats => _stats;

            public BuiltInRuleTemplate(
                string id,
                string displayName,
                int version,
                List<StatDefinitionRecord> stats,
                List<StatRoleBindingRecord> roleBindings)
            {
                Id = id;
                DisplayName = displayName;
                Version = version;
                _stats = new List<IStatDefinition>(stats.Count);
                _roleBindings = new Dictionary<StatRole, string>();

                for (var i = 0; i < stats.Count; i++)
                    _stats.Add(stats[i]);

                for (var i = 0; i < roleBindings.Count; i++)
                    _roleBindings[roleBindings[i].Role] = roleBindings[i].StatId;
            }

            public string GetStatId(StatRole role)
            {
                return _roleBindings.TryGetValue(role, out var statId)
                    ? statId
                    : string.Empty;
            }
        }

        public static IStatRuleTemplate Coc7 { get; } =
            new BuiltInRuleTemplate(
                "coc7_default",
                "Call of Cthulhu 7th",
                1,
                CreateCocStats(),
                CreateCocRoles());

        public static List<StatDefinitionRecord> CreateCocStats()
        {
            return new List<StatDefinitionRecord>
            {
                Base("coc.str", "근력 STR", 10, 50),
                Base("coc.con", "건강 CON", 20, 50),
                Base("coc.siz", "크기 SIZ", 30, 50),
                Base("coc.dex", "민첩 DEX", 40, 50),
                Base("coc.app", "외모 APP", 50, 50),
                Base("coc.int", "지능 INT", 60, 50),
                Base("coc.pow", "정신력 POW", 70, 50),
                Base("coc.edu", "교육 EDU", 80, 50),
                Base("coc.luck", "운 LUCK", 90, 50),
                Base("coc.cthulhu_mythos", "크툴루 신화", 100, 0, 0, 99),

                Formula(
                    "coc.hp.max",
                    "최대 체력",
                    200,
                    "floor((coc.con + coc.siz) / 10)",
                    0,
                    999,
                    true),
                Formula(
                    "coc.mp.max",
                    "최대 마력",
                    210,
                    "floor(coc.pow / 5)",
                    0,
                    999,
                    true),
                Formula(
                    "coc.san.max",
                    "최대 이성",
                    220,
                    "99 - coc.cthulhu_mythos",
                    0,
                    99,
                    false),
                Formula(
                    "coc.mov",
                    "이동력 MOV",
                    230,
                    "if(coc.str > coc.siz && coc.dex > coc.siz, 9, " +
                    "if(coc.str < coc.siz && coc.dex < coc.siz, 7, 8))",
                    0,
                    99,
                    true),
                Lookup(
                    "coc.build",
                    "체격 Build",
                    240,
                    CreateBuildBands(),
                    StatDisplayKind.Number),
                Lookup(
                    "coc.damage_bonus",
                    "피해 보너스 DB",
                    250,
                    CreateDamageBonusBands(),
                    StatDisplayKind.Dice),
                Formula(
                    "coc.dodge",
                    "회피",
                    260,
                    "floor(coc.dex / 2)",
                    0,
                    100,
                    true),
                Formula(
                    "combat.melee_attack",
                    "근접 공격력",
                    300,
                    "floor(coc.str / 5) + coc.build",
                    0,
                    999,
                    true,
                    "프로젝트 전투 확장"),

                Runtime(
                    "coc.hp.current",
                    "현재 체력",
                    400,
                    "coc.hp.max",
                    "coc.hp.max",
                    true),
                Runtime(
                    "coc.mp.current",
                    "현재 마력",
                    410,
                    "coc.mp.max",
                    "coc.mp.max",
                    false),
                Runtime(
                    "coc.san.current",
                    "현재 이성",
                    420,
                    "coc.pow",
                    "coc.san.max",
                    true),
                Runtime(
                    "coc.luck.current",
                    "현재 운",
                    430,
                    "coc.luck",
                    "coc.luck",
                    false)
            };
        }

        public static List<StatRoleBindingRecord> CreateCocRoles()
        {
            return new List<StatRoleBindingRecord>
            {
                new StatRoleBindingRecord(StatRole.HealthCurrent, "coc.hp.current"),
                new StatRoleBindingRecord(StatRole.HealthMax, "coc.hp.max"),
                new StatRoleBindingRecord(StatRole.MagicCurrent, "coc.mp.current"),
                new StatRoleBindingRecord(StatRole.MagicMax, "coc.mp.max"),
                new StatRoleBindingRecord(StatRole.SanityCurrent, "coc.san.current"),
                new StatRoleBindingRecord(StatRole.SanityMax, "coc.san.max"),
                new StatRoleBindingRecord(StatRole.Movement, "coc.mov"),
                new StatRoleBindingRecord(StatRole.MeleeAttack, "combat.melee_attack"),
                new StatRoleBindingRecord(StatRole.Defense, "coc.dodge"),
                new StatRoleBindingRecord(StatRole.Initiative, "coc.dex"),
                new StatRoleBindingRecord(StatRole.LuckCurrent, "coc.luck.current"),
                new StatRoleBindingRecord(StatRole.LuckMax, "coc.luck"),
                new StatRoleBindingRecord(StatRole.Dexterity, "coc.dex")
            };
        }

        private static List<StatLookupBandRecord> CreateBuildBands()
        {
            return new List<StatLookupBandRecord>
            {
                Band("coc.str + coc.siz <= 64", -2),
                Band("coc.str + coc.siz <= 84", -1),
                Band("coc.str + coc.siz <= 124", 0),
                Band("coc.str + coc.siz <= 164", 1),
                Band("coc.str + coc.siz <= 204", 2),
                Band("coc.str + coc.siz <= 284", 3),
                Band("coc.str + coc.siz <= 364", 4),
                Band("coc.str + coc.siz <= 444", 5),
                Band("coc.str + coc.siz <= 524", 6),
                Band("1", 7)
            };
        }

        private static List<StatLookupBandRecord> CreateDamageBonusBands()
        {
            return new List<StatLookupBandRecord>
            {
                Band("coc.str + coc.siz <= 64", -2, "-2"),
                Band("coc.str + coc.siz <= 84", -1, "-1"),
                Band("coc.str + coc.siz <= 124", 0, "0"),
                Band("coc.str + coc.siz <= 164", 1, "+1D4"),
                Band("coc.str + coc.siz <= 204", 2, "+1D6"),
                Band("coc.str + coc.siz <= 284", 3, "+2D6"),
                Band("coc.str + coc.siz <= 364", 4, "+3D6"),
                Band("coc.str + coc.siz <= 444", 5, "+4D6"),
                Band("coc.str + coc.siz <= 524", 6, "+5D6"),
                Band("1", 7, "+6D6")
            };
        }

        private static StatDefinitionRecord Base(
            string id,
            string displayName,
            int order,
            float defaultValue,
            float min = 0,
            float max = 100)
        {
            return new StatDefinitionRecord(
                id,
                displayName,
                "기본 능력치",
                StatValueSource.Base,
                order,
                defaultValue,
                min,
                max);
        }

        private static StatDefinitionRecord Formula(
            string id,
            string displayName,
            int order,
            string formula,
            float min,
            float max,
            bool showInSummary,
            string category = "파생 능력치")
        {
            return new StatDefinitionRecord(
                id,
                displayName,
                category,
                StatValueSource.Formula,
                order,
                minValue: min,
                maxValue: max,
                formula: formula,
                showInSummary: showInSummary);
        }

        private static StatDefinitionRecord Runtime(
            string id,
            string displayName,
            int order,
            string initialFormula,
            string maxStatId,
            bool showInSummary)
        {
            return new StatDefinitionRecord(
                id,
                displayName,
                "현재 수치",
                StatValueSource.Runtime,
                order,
                minValue: 0,
                maxValue: 9999,
                initialValueFormula: initialFormula,
                maxStatId: maxStatId,
                displayKind: StatDisplayKind.CurrentAndMax,
                isAdjustable: true,
                adjustStep: 1,
                showInSummary: showInSummary);
        }

        private static StatDefinitionRecord Lookup(
            string id,
            string displayName,
            int order,
            List<StatLookupBandRecord> bands,
            StatDisplayKind displayKind)
        {
            return new StatDefinitionRecord(
                id,
                displayName,
                "파생 능력치",
                StatValueSource.LookupTable,
                order,
                minValue: -999,
                maxValue: 999,
                displayKind: displayKind,
                lookupBands: bands);
        }

        private static StatLookupBandRecord Band(
            string condition,
            float numericValue,
            string displayText = null)
        {
            return new StatLookupBandRecord(condition, numericValue, displayText);
        }
    }
}
