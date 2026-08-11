using System;
using UnityEngine;

namespace Trpg.Pawns
{
    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Pawn Idle Motion",
        fileName = "PawnIdleMotion")]
    public sealed class PawnIdleMotion : ScriptableObject
    {
        public const int FixedKeyCount = 5;
        private const int CurrentDataVersion = 1;

        [SerializeField, Min(0.01f), Tooltip(
            "한 키를 유지하는 시간입니다. 기본값 0.125초는 8fps입니다.")]
        private float _keyDuration = 0.125f;

        [Header("Fixed 5-Key Pixel Offsets")]
        [SerializeField] private Vector2Int[] _legs = CreateZeroTrack();
        [SerializeField] private Vector2Int[] _feet = CreateZeroTrack();
        [SerializeField] private Vector2Int[] _torso = CreateDefaultTorsoTrack();
        [SerializeField] private Vector2Int[] _eyes = CreateZeroTrack();
        [SerializeField] private Vector2Int[] _head = CreateDefaultHeadTrack();
        [SerializeField] private Vector2Int[] _hair = CreateDefaultHairTrack();

        [SerializeField, HideInInspector] private int _dataVersion =
            CurrentDataVersion;

        public int KeyCount => FixedKeyCount;
        public float KeyDuration => Mathf.Max(0.01f, _keyDuration);
        public bool Loop => true;
        public float Duration => FixedKeyCount * KeyDuration;

        public int EvaluateKey(
            float time,
            float phase,
            float speedMultiplier)
        {
            var normalizedTime = Mathf.Max(
                0f,
                time * Mathf.Max(0.01f, speedMultiplier) + phase);
            var rawKey = Mathf.FloorToInt(normalizedTime / KeyDuration);
            return rawKey % FixedKeyCount;
        }

        public Vector2Int EvaluateOffset(
            ChannelId channel,
            int keyIndex)
        {
            var normalizedKey = Mathf.Clamp(
                keyIndex,
                0,
                FixedKeyCount - 1);
            var track = GetTrack(channel);
            return track != null && normalizedKey < track.Length
                ? track[normalizedKey]
                : Vector2Int.zero;
        }

        public bool ValidateTracks(out string error)
        {
            if (!HasCorrectLength(_legs) ||
                !HasCorrectLength(_feet) ||
                !HasCorrectLength(_torso) ||
                !HasCorrectLength(_eyes) ||
                !HasCorrectLength(_head) ||
                !HasCorrectLength(_hair))
            {
                error = "모든 Idle 채널은 정확히 5개의 Vector2Int 키를 가져야 합니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        [ContextMenu("Reset To Default 5-Key Idle")]
        public void ResetToDefaultIdle()
        {
            _keyDuration = 0.125f;
            _legs = CreateZeroTrack();
            _feet = CreateZeroTrack();
            _torso = CreateDefaultTorsoTrack();
            _eyes = CreateZeroTrack();
            _head = CreateDefaultHeadTrack();
            _hair = CreateDefaultHairTrack();
            _dataVersion = CurrentDataVersion;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        public Vector2Int EditorGetOffset(
            ChannelId channel,
            int keyIndex)
        {
            return EvaluateOffset(channel, keyIndex);
        }

        public void EditorSetOffset(
            ChannelId channel,
            int keyIndex,
            Vector2Int value)
        {
            if (keyIndex < 0 || keyIndex >= FixedKeyCount)
                return;

            EnsureTracks();
            var track = GetTrack(channel);
            if (track == null)
                return;

            track[keyIndex] = value;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private Vector2Int[] GetTrack(ChannelId channel)
        {
            switch (channel)
            {
                case ChannelId.Legs:
                    return _legs;
                case ChannelId.Feet:
                    return _feet;
                case ChannelId.Torso:
                    return _torso;
                case ChannelId.Eyes:
                    return _eyes;
                case ChannelId.Head:
                    return _head;
                case ChannelId.Hair:
                    return _hair;
                default:
                    return null;
            }
        }

        private void OnEnable()
        {
            if (_dataVersion < CurrentDataVersion)
            {
                ResetToDefaultIdle();
                return;
            }

            EnsureTracks();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _keyDuration = Mathf.Max(0.01f, _keyDuration);
            EnsureTracks();
            _dataVersion = CurrentDataVersion;
        }
#endif

        private void EnsureTracks()
        {
            _legs = ResizePreservingValues(_legs);
            _feet = ResizePreservingValues(_feet);
            _torso = ResizePreservingValues(
                _torso,
                CreateDefaultTorsoTrack());
            _eyes = ResizePreservingValues(_eyes);
            _head = ResizePreservingValues(
                _head,
                CreateDefaultHeadTrack());
            _hair = ResizePreservingValues(
                _hair,
                CreateDefaultHairTrack());
        }

        private static Vector2Int[] ResizePreservingValues(
            Vector2Int[] source,
            Vector2Int[] fallback = null)
        {
            if (source != null && source.Length == FixedKeyCount)
                return source;

            var result = fallback != null &&
                         fallback.Length == FixedKeyCount
                ? (Vector2Int[])fallback.Clone()
                : CreateZeroTrack();
            if (source != null)
            {
                Array.Copy(
                    source,
                    result,
                    Mathf.Min(source.Length, result.Length));
            }

            return result;
        }

        private static bool HasCorrectLength(Vector2Int[] track)
        {
            return track != null && track.Length == FixedKeyCount;
        }

        private static Vector2Int[] CreateZeroTrack()
        {
            return new Vector2Int[FixedKeyCount];
        }

        private static Vector2Int[] CreateDefaultTorsoTrack()
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(0, -1),
                new Vector2Int(0, -1),
                new Vector2Int(0, 0),
                new Vector2Int(0, 0)
            };
        }

        private static Vector2Int[] CreateDefaultHeadTrack()
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(0, 0),
                new Vector2Int(0, -1),
                new Vector2Int(0, -1),
                new Vector2Int(0, 0)
            };
        }

        private static Vector2Int[] CreateDefaultHairTrack()
        {
            return new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(0, 0),
                new Vector2Int(0, 0),
                new Vector2Int(0, -1),
                new Vector2Int(0, 0)
            };
        }
    }
}
