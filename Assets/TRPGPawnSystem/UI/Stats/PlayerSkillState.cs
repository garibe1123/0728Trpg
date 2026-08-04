using System;
using System.Collections.Generic;
using Trpg.Data.Skills;
using Trpg.Pawns;
using UnityEngine;

namespace Trpg.UI.Skills
{
    public readonly struct SkillRuntimeValue
    {
        public SkillRuntimeValue(
            SkillDefinition definition,
            int regularValue,
            bool usesBaseValue)
            : this(
                definition,
                definition != null ? definition.Id : string.Empty,
                definition != null
                    ? definition.DisplayName
                    : string.Empty,
                definition != null ? definition.Category : string.Empty,
                regularValue,
                usesBaseValue,
                definition != null && definition.RequiresTraining,
                definition != null ? definition.SortOrder : 0)
        {
        }

        public SkillRuntimeValue(
            SkillDefinition definition,
            string skillId,
            string displayName,
            string category,
            int regularValue,
            bool usesBaseValue,
            bool requiresTraining,
            int sortOrder)
        {
            Definition = definition;
            SkillId = skillId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Category = category ?? string.Empty;
            RegularValue = Mathf.Clamp(regularValue, 0, 999);
            UsesBaseValue = usesBaseValue;
            RequiresTraining = requiresTraining;
            SortOrder = sortOrder;
        }

        public SkillDefinition Definition { get; }
        public string SkillId { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public int RegularValue { get; }
        public bool UsesBaseValue { get; }
        public bool RequiresTraining { get; }
        public int SortOrder { get; }
        public bool IsCustom => Definition == null;

        public SkillRuntimeValue WithDisplayName(string displayName)
        {
            return new SkillRuntimeValue(
                Definition,
                SkillId,
                displayName,
                Category,
                RegularValue,
                UsesBaseValue,
                RequiresTraining,
                SortOrder);
        }

        public SkillRuntimeValue WithRegularValue(int regularValue)
        {
            return new SkillRuntimeValue(
                Definition,
                SkillId,
                DisplayName,
                Category,
                regularValue,
                false,
                RequiresTraining,
                SortOrder);
        }
    }

    [Serializable]
    public sealed class SkillRuntimeValueSnapshot
    {
        public string SkillId;
        public string DisplayName;
        public int RegularValue;
        public bool IsCustom;
    }

    [Serializable]
    public sealed class SkillRuntimeSnapshot
    {
        public string CharacterDefinitionId;
        public List<SkillRuntimeValueSnapshot> Skills =
            new List<SkillRuntimeValueSnapshot>();
    }

    public sealed class PlayerSkillState : MonoBehaviour
    {
        [SerializeField] private bool _initializeOnAwake = true;

        private readonly List<SkillRuntimeValue> _skills =
            new List<SkillRuntimeValue>();
        private InteractivePawnDefinition _definition;
        private bool _isInitialized;

        public event Action Changed;

        public InteractivePawnDefinition Definition => _definition;
        public IReadOnlyList<SkillRuntimeValue> Skills => _skills;
        public bool IsInitialized => _isInitialized;

        private void Awake()
        {
            if (_initializeOnAwake && _definition != null)
                Initialize();
        }

        public bool Configure(InteractivePawnDefinition definition)
        {
            if (definition == null)
                return false;

            if (ReferenceEquals(_definition, definition))
                return true;

            _definition = definition;
            _skills.Clear();
            _isInitialized = false;
            return true;
        }

        public void Initialize()
        {
            if (_isInitialized || _definition == null)
                return;

            _skills.Clear();
            var defaults = _definition.Skills;
            var count = defaults != null ? defaults.Count : 0;
            for (var index = 0; index < count; index++)
            {
                var record = defaults[index];
                var skill = record != null
                    ? record.Definition
                    : null;
                if (skill == null || Contains(skill.Id))
                    continue;

                _skills.Add(
                    new SkillRuntimeValue(
                        skill,
                        record.RegularValue,
                        record.UsesBaseValue));
            }

            _isInitialized = true;
            Changed?.Invoke();
        }

        public bool TryAdd(
            string skillId,
            int regularValue,
            out string error)
        {
            error = string.Empty;
            if (!EnsureInitialized())
            {
                error = "스킬 상태가 초기화되지 않았습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(skillId))
            {
                error = "추가할 스킬 ID가 비어 있습니다.";
                return false;
            }

            if (Contains(skillId))
            {
                error = "이미 보유한 스킬입니다.";
                return false;
            }

            if (!TryResolveDefinition(skillId, out var skill))
            {
                error = $"스킬 카탈로그에서 '{skillId}'을 찾지 못했습니다.";
                return false;
            }

            _skills.Add(
                new SkillRuntimeValue(
                    skill,
                    Mathf.Clamp(regularValue, 0, 999),
                    false));
            Changed?.Invoke();
            return true;
        }

        public bool TryAddCustom(
            string displayName,
            int regularValue,
            out string skillId,
            out string error)
        {
            skillId = string.Empty;
            error = string.Empty;
            if (!EnsureInitialized())
            {
                error = "스킬 상태가 초기화되지 않았습니다.";
                return false;
            }

            skillId = CreateCustomSkillId();
            _skills.Add(
                new SkillRuntimeValue(
                    null,
                    skillId,
                    NormalizeDisplayName(displayName),
                    string.Empty,
                    regularValue,
                    false,
                    false,
                    _skills.Count));
            Changed?.Invoke();
            return true;
        }

        public bool TrySetDisplayName(
            string skillId,
            string displayName)
        {
            if (!EnsureInitialized() ||
                string.IsNullOrWhiteSpace(skillId))
            {
                return false;
            }

            var normalized = NormalizeDisplayName(displayName);
            for (var index = 0; index < _skills.Count; index++)
            {
                var current = _skills[index];
                if (!string.Equals(
                        current.SkillId,
                        skillId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(
                        current.DisplayName,
                        normalized,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                _skills[index] =
                    current.WithDisplayName(normalized);
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        public bool TrySetRegularValue(
            string skillId,
            int regularValue)
        {
            if (!EnsureInitialized())
                return false;

            for (var index = 0; index < _skills.Count; index++)
            {
                var current = _skills[index];
                if (!string.Equals(
                        current.SkillId,
                        skillId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var clamped = Mathf.Clamp(regularValue, 0, 999);
                if (current.RegularValue == clamped)
                    return true;

                _skills[index] =
                    current.WithRegularValue(clamped);
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        public bool TryRemove(string skillId)
        {
            if (!EnsureInitialized() ||
                string.IsNullOrWhiteSpace(skillId))
            {
                return false;
            }

            for (var index = 0; index < _skills.Count; index++)
            {
                if (!string.Equals(
                        _skills[index].SkillId,
                        skillId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                _skills.RemoveAt(index);
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        public SkillRuntimeSnapshot CreateSnapshot()
        {
            EnsureInitialized();
            var snapshot = new SkillRuntimeSnapshot
            {
                CharacterDefinitionId =
                    _definition != null ? _definition.Id : string.Empty
            };

            for (var index = 0; index < _skills.Count; index++)
            {
                var value = _skills[index];
                if (string.IsNullOrWhiteSpace(value.SkillId))
                    continue;

                snapshot.Skills.Add(
                    new SkillRuntimeValueSnapshot
                    {
                        SkillId = value.SkillId,
                        DisplayName = value.DisplayName,
                        RegularValue = value.RegularValue,
                        IsCustom = value.IsCustom
                    });
            }

            return snapshot;
        }

        public bool TryApplySnapshot(
            SkillRuntimeSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (_definition == null)
            {
                error = "캐릭터 정의가 연결되지 않았습니다.";
                return false;
            }

            if (snapshot == null || snapshot.Skills == null)
            {
                error = "스킬 스냅샷이 비어 있습니다.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    snapshot.CharacterDefinitionId) &&
                !string.Equals(
                    snapshot.CharacterDefinitionId,
                    _definition.Id,
                    StringComparison.Ordinal))
            {
                error = "다른 캐릭터 정의의 스킬 스냅샷입니다.";
                return false;
            }

            var restored = new List<SkillRuntimeValue>(
                snapshot.Skills.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0;
                 index < snapshot.Skills.Count;
                 index++)
            {
                var stored = snapshot.Skills[index];
                if (stored == null ||
                    string.IsNullOrWhiteSpace(stored.SkillId) ||
                    !ids.Add(stored.SkillId))
                {
                    error = "스킬 스냅샷에 비어 있거나 중복된 ID가 있습니다.";
                    return false;
                }

                if (TryResolveDefinition(
                        stored.SkillId,
                        out var definition))
                {
                    var displayName =
                        string.IsNullOrWhiteSpace(stored.DisplayName)
                            ? definition.DisplayName
                            : stored.DisplayName.Trim();
                    restored.Add(
                        new SkillRuntimeValue(
                            definition,
                            definition.Id,
                            displayName,
                            definition.Category,
                            stored.RegularValue,
                            false,
                            definition.RequiresTraining,
                            definition.SortOrder));
                    continue;
                }

                if (!stored.IsCustom &&
                    string.IsNullOrWhiteSpace(stored.DisplayName))
                {
                    error =
                        $"스킬 정의를 찾지 못했습니다: {stored.SkillId}";
                    return false;
                }

                restored.Add(
                    new SkillRuntimeValue(
                        null,
                        stored.SkillId,
                        NormalizeDisplayName(stored.DisplayName),
                        string.Empty,
                        stored.RegularValue,
                        false,
                        false,
                        index));
            }

            _skills.Clear();
            _skills.AddRange(restored);
            _isInitialized = true;
            Changed?.Invoke();
            return true;
        }

        public static PlayerSkillState ResolveOrCreate(
            GameObject selectedObject,
            InteractivePawnDefinition definition)
        {
            if (selectedObject == null ||
                definition == null ||
                !definition.SupportsFullCharacterSheet)
            {
                return null;
            }

            var state =
                selectedObject.GetComponent<PlayerSkillState>();
            if (state == null)
            {
                state = selectedObject
                    .GetComponentInChildren<PlayerSkillState>();
            }
            if (state == null)
            {
                state = selectedObject
                    .GetComponentInParent<PlayerSkillState>();
            }
            if (state == null)
                state = selectedObject.AddComponent<PlayerSkillState>();

            if (!state.Configure(definition))
                return null;

            state.Initialize();
            return state;
        }

        private bool EnsureInitialized()
        {
            if (!_isInitialized)
                Initialize();
            return _isInitialized;
        }

        private bool Contains(string skillId)
        {
            for (var index = 0; index < _skills.Count; index++)
            {
                if (string.Equals(
                        _skills[index].SkillId,
                        skillId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private string CreateCustomSkillId()
        {
            string skillId;
            do
            {
                skillId =
                    $"skill.custom.{Guid.NewGuid():N}";
            }
            while (Contains(skillId));

            return skillId;
        }

        private static string NormalizeDisplayName(string displayName)
        {
            var value = displayName != null
                ? displayName.Trim()
                : string.Empty;
            return string.IsNullOrWhiteSpace(value)
                ? "새 스킬"
                : value;
        }

        private bool TryResolveDefinition(
            string skillId,
            out SkillDefinition definition)
        {
            definition = null;
            var defaults = _definition != null
                ? _definition.Skills
                : null;
            var count = defaults != null ? defaults.Count : 0;
            for (var index = 0; index < count; index++)
            {
                var skill = defaults[index]?.Definition;
                if (skill != null &&
                    string.Equals(
                        skill.Id,
                        skillId,
                        StringComparison.Ordinal))
                {
                    definition = skill;
                    return true;
                }
            }

            return _definition != null &&
                   _definition.SkillCatalog != null &&
                   _definition.SkillCatalog.TryGetById(
                       skillId,
                       out definition);
        }

        private void OnDestroy()
        {
            Changed = null;
        }
    }
}
