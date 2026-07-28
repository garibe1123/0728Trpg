using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    [Serializable]
    public sealed class PawnRollStatEntry
    {
        [SerializeField, Tooltip("능력치 식별 ID. 예: str, dex, sanity")]
        private string _statId = PawnRollStats.DefaultStatId;

        [SerializeField, Range(1, 100), Tooltip("d100 판정 목표값")]
        private int _checkTarget = PawnRollStats.FallbackCheckTarget;

        public string StatId => _statId;
        public int CheckTarget => Mathf.Clamp(_checkTarget, 1, 100);
    }

    [DisallowMultipleComponent]
    public sealed class PawnRollStats : MonoBehaviour
    {
        public const string DefaultStatId = "default";
        public const int FallbackCheckTarget = 50;

        [SerializeField, Range(1, 100), Tooltip(
            "요청한 능력치가 없을 때 사용하는 d100 목표값")]
        private int _fallbackCheckTarget = FallbackCheckTarget;

        [SerializeField, Tooltip(
            "캐릭터 시트 연동 전 인스펙터에서 사용할 능력치별 목표값")]
        private List<PawnRollStatEntry> _checkTargets =
            new List<PawnRollStatEntry>();

        private readonly Dictionary<string, int> _runtimeTargets =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        public int GetCheckTarget(string statId)
        {
            var normalizedId = NormalizeStatId(statId);
            if (_runtimeTargets.TryGetValue(
                    normalizedId,
                    out var runtimeValue))
            {
                return ClampTarget(runtimeValue);
            }

            for (var i = 0; i < _checkTargets.Count; i++)
            {
                var entry = _checkTargets[i];
                if (entry != null &&
                    string.Equals(
                        entry.StatId,
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry.CheckTarget;
                }
            }

            return ClampTarget(_fallbackCheckTarget);
        }

        public void SetRuntimeCheckTarget(
            string statId,
            int checkTarget)
        {
            _runtimeTargets[NormalizeStatId(statId)] =
                ClampTarget(checkTarget);
        }

        public bool RemoveRuntimeCheckTarget(string statId)
        {
            return _runtimeTargets.Remove(NormalizeStatId(statId));
        }

        public void ClearRuntimeCheckTargets()
        {
            _runtimeTargets.Clear();
        }

        private static string NormalizeStatId(string statId)
        {
            return string.IsNullOrWhiteSpace(statId)
                ? DefaultStatId
                : statId.Trim();
        }

        private static int ClampTarget(int value)
        {
            return Mathf.Clamp(value, 1, 100);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _fallbackCheckTarget = ClampTarget(_fallbackCheckTarget);
        }
#endif
    }
}
