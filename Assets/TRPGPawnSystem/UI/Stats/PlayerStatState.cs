using System;
using Trpg.Data.Stats;
using Trpg.Domain.Stats;
using UnityEngine;

namespace Trpg.UI.Stats
{
    public sealed class PlayerStatState : MonoBehaviour
    {
        [SerializeField, Tooltip("Player Pawn이 사용할 캐릭터별 기본 스탯 SO입니다.")]
        private CharacterStatDefinition _definition;
        [SerializeField] private bool _initializeOnAwake = true;

        private StatRuntimeState _runtime;

        public static event Action<PlayerStatState> ActiveStateChanged;
        public event Action Changed;

        public static PlayerStatState ActiveState { get; private set; }
        public CharacterStatDefinition Definition => _definition;
        public StatRuntimeState Runtime => _runtime;
        public bool IsInitialized => _runtime != null;

        private void Awake()
        {
            if (_initializeOnAwake && _definition != null)
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
                Debug.LogError($"[{name}] Character Stat Definition이 없습니다.", this);
                return;
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

        public bool Configure(CharacterStatDefinition definition)
        {
            if (_runtime != null || definition == null)
                return false;
            _definition = definition;
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

        public void Activate()
        {
            if (!IsInitialized)
                Initialize();

            if (IsInitialized)
                SetActive(this);
        }

        public static bool SetActiveFrom(GameObject selectedObject)
        {
            if (selectedObject == null)
            {
                SetActive(null);
                return false;
            }

            var state = selectedObject.GetComponentInParent<PlayerStatState>();
            if (state == null)
                state = selectedObject.GetComponentInChildren<PlayerStatState>();
            if (state == null)
            {
                SetActive(null);
                return false;
            }

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
    }
}
