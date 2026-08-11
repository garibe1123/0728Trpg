using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    [Serializable]
    public sealed class PawnChannelTrack
    {
        [SerializeField] private ChannelId _channel;
        [SerializeField] private Vector2Int[] _offsets =
            Array.Empty<Vector2Int>();

        public ChannelId Channel => _channel;
        public IReadOnlyList<Vector2Int> Offsets => _offsets;

        public PawnChannelTrack()
        {
        }

        public PawnChannelTrack(
            ChannelId channel,
            params Vector2Int[] offsets)
        {
            _channel = channel;
            _offsets = offsets ?? Array.Empty<Vector2Int>();
        }

        public Vector2Int GetOffset(int keyIndex)
        {
            if (_offsets == null ||
                keyIndex < 0 ||
                keyIndex >= _offsets.Length)
            {
                return Vector2Int.zero;
            }

            return _offsets[keyIndex];
        }

#if UNITY_EDITOR
        public void EditorSetOffset(int keyIndex, Vector2Int value)
        {
            if (_offsets == null ||
                keyIndex < 0 ||
                keyIndex >= _offsets.Length)
            {
                return;
            }

            _offsets[keyIndex] = value;
        }

        public void EditorResize(int keyCount)
        {
            keyCount = Mathf.Max(1, keyCount);
            if (_offsets != null && _offsets.Length == keyCount)
                return;

            var resized = new Vector2Int[keyCount];
            if (_offsets != null)
            {
                Array.Copy(
                    _offsets,
                    resized,
                    Mathf.Min(_offsets.Length, resized.Length));
            }

            _offsets = resized;
        }
#endif
    }

    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Pawn Offset Animation Clip",
        fileName = "PawnOffsetAnimationClip")]
    public sealed class PawnOffsetAnimationClip : ScriptableObject
    {
        [SerializeField, Min(1)] private int _keyCount = 5;
        [SerializeField, Min(0.01f)] private float _keyDuration = 0.125f;
        [SerializeField] private bool _loop = true;
        [SerializeField] private List<PawnChannelTrack> _tracks =
            new List<PawnChannelTrack>();

        public int KeyCount => Mathf.Max(1, _keyCount);
        public float KeyDuration => Mathf.Max(0.01f, _keyDuration);
        public bool Loop => _loop;
        public IReadOnlyList<PawnChannelTrack> Tracks => _tracks;
        public float Duration => KeyCount * KeyDuration;

        public int EvaluateKey(
            float time,
            float phase,
            float speedMultiplier)
        {
            var normalizedTime =
                Mathf.Max(0f, time * Mathf.Max(0.01f, speedMultiplier) + phase);
            var rawKey = Mathf.FloorToInt(normalizedTime / KeyDuration);
            if (_loop)
                return rawKey % KeyCount;

            return Mathf.Clamp(rawKey, 0, KeyCount - 1);
        }

        public Vector2Int EvaluateOffset(
            ChannelId channel,
            int keyIndex)
        {
            if (_tracks == null)
                return Vector2Int.zero;

            for (var index = 0; index < _tracks.Count; index++)
            {
                var track = _tracks[index];
                if (track != null && track.Channel == channel)
                    return track.GetOffset(keyIndex);
            }

            return Vector2Int.zero;
        }

        [ContextMenu("Reset To 5-Key Idle Example")]
        public void ResetToIdleExample()
        {
            _keyCount = 5;
            _keyDuration = 0.125f;
            _loop = true;
            _tracks = new List<PawnChannelTrack>
            {
                new PawnChannelTrack(
                    ChannelId.Torso,
                    new Vector2Int(0, 0),
                    new Vector2Int(0, -1),
                    new Vector2Int(0, -1),
                    new Vector2Int(0, 0),
                    new Vector2Int(0, 0)),
                new PawnChannelTrack(
                    ChannelId.Head,
                    new Vector2Int(0, 0),
                    new Vector2Int(0, 0),
                    new Vector2Int(0, -1),
                    new Vector2Int(0, -1),
                    new Vector2Int(0, 0)),
                new PawnChannelTrack(
                    ChannelId.Hair,
                    new Vector2Int(0, 0),
                    new Vector2Int(0, 0),
                    new Vector2Int(0, 0),
                    new Vector2Int(0, -1),
                    new Vector2Int(0, 0))
            };
        }

        public bool ValidateTracks(out string error)
        {
            if (_keyCount < 1)
            {
                error = "keyCount는 1 이상이어야 합니다.";
                return false;
            }

            var channels = new bool[(int)ChannelId.Count];
            if (_tracks != null)
            {
                for (var index = 0; index < _tracks.Count; index++)
                {
                    var track = _tracks[index];
                    if (track == null)
                    {
                        error = $"{index}번 Track이 비어 있습니다.";
                        return false;
                    }

                    var channelIndex = (int)track.Channel;
                    if (channelIndex < 0 ||
                        channelIndex >= channels.Length ||
                        channels[channelIndex])
                    {
                        error = $"중복되거나 잘못된 Channel: {track.Channel}";
                        return false;
                    }

                    channels[channelIndex] = true;
                    if (track.Offsets == null ||
                        track.Offsets.Count != _keyCount)
                    {
                        error =
                            $"{track.Channel} Track의 Offset 길이는 keyCount({_keyCount})와 같아야 합니다.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

#if UNITY_EDITOR
        public PawnChannelTrack EditorFindTrack(ChannelId channel)
        {
            if (_tracks == null)
                return null;

            for (var index = 0; index < _tracks.Count; index++)
            {
                var track = _tracks[index];
                if (track != null && track.Channel == channel)
                    return track;
            }

            return null;
        }

        public PawnChannelTrack EditorGetOrCreateTrack(ChannelId channel)
        {
            var existing = EditorFindTrack(channel);
            if (existing != null)
                return existing;

            if (_tracks == null)
                _tracks = new List<PawnChannelTrack>();

            var offsets = new Vector2Int[KeyCount];
            var created = new PawnChannelTrack(channel, offsets);
            _tracks.Add(created);
            return created;
        }

        private void OnValidate()
        {
            _keyCount = Mathf.Max(1, _keyCount);
            _keyDuration = Mathf.Max(0.01f, _keyDuration);
            if (_tracks == null)
                return;

            for (var index = 0; index < _tracks.Count; index++)
                _tracks[index]?.EditorResize(_keyCount);
        }
#endif
    }
}
