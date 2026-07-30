using System;
using System.Collections.Generic;
using Trpg.Data.Stats;
using Trpg.Domain.Stats;
using UnityEngine;

namespace Trpg.UI.Stats
{
    public sealed class PlayerStatState :
        MonoBehaviour,
        IStatValueProvider
    {
        [SerializeField] private bool _initializeOnAwake = true;

        private StatRuntimeState _runtime;
        private ICharacterStatDefinition _definition;

        public static event Action<PlayerStatState> ActiveStateChanged;
        public event Action Changed;

        public static PlayerStatState ActiveState { get; private set; }
        public ICharacterStatDefinition Definition => _definition;
        public StatRuntimeState Runtime => _runtime;
        public bool IsInitialized => _runtime != null;

        private void Awake()
        {
            if (_initializeOnAwake)
                Initialize();
        }

        private void OnDestroy()
        {
            if (_runtime != null)
                _runtime.Changed -= OnRuntimeChanged;

            if (ReferenceEquals(ActiveState, this))
                SetActive(null);
        }

        private void OnDisable()
        {
            if (ReferenceEquals(ActiveState, this))
                SetActive(null);
        }

        public void Initialize()
        {
            if (_runtime != null)
                return;
            if (_definition == null)
            {
                ConfigureDefaultCoc(name);
            }

            try
            {
                _runtime = new StatRuntimeState(_definition);
                _runtime.Changed += OnRuntimeChanged;
                Changed?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        public bool Configure(ICharacterStatDefinition definition)
        {
            if (definition == null)
                return false;

            if (ReferenceEquals(_definition, definition))
                return true;

            ResetRuntime();
            _definition = definition;
            return true;
        }

        public bool ConfigureDefaultCoc(string characterId)
        {
            if (_runtime != null &&
                _definition is DefaultCocCharacterDefinition)
            {
                return true;
            }

            if (_runtime != null)
                return false;

            _definition = new DefaultCocCharacterDefinition(
                characterId);
            return true;
        }

        public bool TryAdjust(string statId, double delta)
        {
            return _runtime != null && _runtime.TryAdjust(statId, delta);
        }

        public bool TrySetRuntimeValue(string statId, double value)
        {
            return _runtime != null && _runtime.TrySetRuntimeValue(statId, value);
        }

        public bool TrySetDisplayedValue(string statId, double value)
        {
            return _runtime != null && _runtime.TrySetDisplayedValue(statId, value);
        }

        public bool TryGetNumber(string statId, out double value)
        {
            value = 0d;
            if (!EnsureInitialized() ||
                string.IsNullOrWhiteSpace(statId) ||
                !_runtime.TryGetDefinition(statId, out _))
            {
                return false;
            }

            try
            {
                value = _runtime.GetNumber(statId);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public bool TryGetRoleNumber(
            StatRole role,
            out double value)
        {
            value = 0d;
            if (!EnsureInitialized())
            {
                return false;
            }

            var statId = _runtime.Template.GetStatId(role);
            return !string.IsNullOrWhiteSpace(statId) &&
                   TryGetNumber(statId, out value);
        }

        public int ResolveMovementScore(
            int fallback,
            float multiplier)
        {
            if (!TryGetRoleNumber(
                    StatRole.Movement,
                    out var movement))
            {
                return Mathf.Clamp(fallback, 10, 100);
            }

            return Mathf.Clamp(
                Mathf.RoundToInt(
                    (float)movement *
                    Mathf.Max(0.01f, multiplier)),
                10,
                100);
        }

        public void Activate()
        {
            if (!IsInitialized)
                Initialize();

            if (IsInitialized)
                SetActive(this);
        }

        public static bool SetActiveFrom(GameObject selectedObject)
        {
            return SetActiveFrom(
                selectedObject,
                null,
                selectedObject != null
                    ? selectedObject.name
                    : string.Empty);
        }

        public static bool SetActiveFrom(
            GameObject selectedObject,
            ICharacterStatDefinition definition,
            string fallbackCharacterId)
        {
            if (selectedObject == null)
            {
                SetActive(null);
                return false;
            }

            var state = selectedObject.GetComponent<PlayerStatState>();
            if (state == null)
                state = selectedObject.GetComponentInChildren<PlayerStatState>();
            if (state == null)
                state = selectedObject.GetComponentInParent<PlayerStatState>();
            if (state == null)
                state = selectedObject.AddComponent<PlayerStatState>();

            if (definition != null)
            {
                state.Configure(definition);
            }
            else if (!state.IsInitialized)
                state.ConfigureDefaultCoc(fallbackCharacterId);

            state.Activate();
            return state.IsInitialized;
        }

        public static void SetActive(PlayerStatState state)
        {
            if (ReferenceEquals(ActiveState, state))
                return;

            ActiveState = state;
            ActiveStateChanged?.Invoke(ActiveState);
        }

        public bool AddModifier(string statId, string sourceId, double amount)
        {
            return _runtime != null && _runtime.AddModifier(statId, sourceId, amount);
        }

        public bool RemoveModifier(string statId, string sourceId)
        {
            return _runtime != null && _runtime.RemoveModifier(statId, sourceId);
        }

        public bool SetGmManualModifier(
            string statId,
            double amount)
        {
            if (_runtime == null ||
                string.IsNullOrWhiteSpace(statId) ||
                double.IsNaN(amount) ||
                double.IsInfinity(amount))
            {
                return false;
            }

            if (Math.Abs(amount) <= 0.0001d)
            {
                _runtime.RemoveModifier(
                    statId,
                    StatRuntimeState.DirectEditModifierSourceId);
                return true;
            }

            return _runtime.AddModifier(
                statId,
                StatRuntimeState.DirectEditModifierSourceId,
                amount);
        }

        public StatRuntimeSnapshot CreateSnapshot()
        {
            return _runtime?.CreateSnapshot();
        }

        public bool TryApplySnapshot(StatRuntimeSnapshot snapshot, out string error)
        {
            if (_runtime == null)
            {
                error = "스탯 런타임이 초기화되지 않았습니다.";
                return false;
            }
            return _runtime.TryApplySnapshot(snapshot, out error);
        }

        private void OnRuntimeChanged()
        {
            Changed?.Invoke();
        }

        private bool EnsureInitialized()
        {
            if (!IsInitialized)
                Initialize();

            return IsInitialized;
        }

        private void ResetRuntime()
        {
            if (_runtime == null)
                return;

            _runtime.Changed -= OnRuntimeChanged;
            _runtime = null;
        }

        private sealed class DefaultCocCharacterDefinition :
            ICharacterStatDefinition
        {
            private readonly List<StatBaseValue> _baseValues =
                new List<StatBaseValue>
                {
                    new StatBaseValue("coc.str", 50d),
                    new StatBaseValue("coc.con", 50d),
                    new StatBaseValue("coc.siz", 50d),
                    new StatBaseValue("coc.dex", 50d),
                    new StatBaseValue("coc.app", 50d),
                    new StatBaseValue("coc.int", 50d),
                    new StatBaseValue("coc.pow", 50d),
                    new StatBaseValue("coc.edu", 50d),
                    new StatBaseValue("coc.luck", 50d),
                    new StatBaseValue("coc.cthulhu_mythos", 0d)
                };

            public string Id { get; }
            public IStatRuleTemplate RuleTemplate =>
                StatRuleTemplateDefaults.Coc7;
            public IReadOnlyList<StatBaseValue> BaseValues =>
                _baseValues;

            public DefaultCocCharacterDefinition(
                string characterId)
            {
                Id = string.IsNullOrWhiteSpace(characterId)
                    ? "runtime_coc_character"
                    : $"runtime_coc_{characterId.Trim()}";
            }
        }
    }
}
