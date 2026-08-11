using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    public enum PawnVisualMode : byte
    {
        Legacy = 0,
        ModularCharacter = 1,
        SimpleSprite = 2
    }

    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Simple Pawn Visual",
        fileName = "SimplePawnVisual")]
    public sealed class SimplePawnVisualDefinition : ScriptableObject
    {
        [SerializeField, Tooltip(
            "월드에 표시할 단일 Sprite. 파츠 조립과 Idle Motion을 사용하지 않습니다.")]
        private Sprite _worldSprite;

        [SerializeField, Tooltip(
            "정보창에 표시할 Portrait. 비어 있으면 InteractivePawnDefinition의 Portrait를 사용합니다.")]
        private Sprite _portrait;

        [System.NonSerialized]
        private Dictionary<int, Sprite> _ppuCache;

        public Sprite WorldSprite => _worldSprite;
        public Sprite Portrait => _portrait;

        public Sprite ResolveWorldSprite(int pixelsPerUnit)
        {
            if (_worldSprite == null || _worldSprite.texture == null)
                return null;

            var ppu = PixelSnap.NormalizePixelsPerUnit(pixelsPerUnit);
            if (Mathf.RoundToInt(_worldSprite.pixelsPerUnit) == ppu)
                return _worldSprite;

            if (_ppuCache == null)
                _ppuCache = new Dictionary<int, Sprite>();
            if (_ppuCache.TryGetValue(ppu, out var cached) && cached != null)
                return cached;

            Rect rect;
            try
            {
                rect = _worldSprite.textureRect;
            }
            catch (UnityException)
            {
                return _worldSprite;
            }

            var sourceSize = _worldSprite.rect.size;
            var pivot = new Vector2(
                _worldSprite.pivot.x / Mathf.Max(1f, sourceSize.x),
                _worldSprite.pivot.y / Mathf.Max(1f, sourceSize.y));
            var sprite = Sprite.Create(
                _worldSprite.texture,
                rect,
                pivot,
                ppu,
                0,
                SpriteMeshType.FullRect,
                _worldSprite.border);
            sprite.name = $"{_worldSprite.name}_PPU{ppu}";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _ppuCache[ppu] = sprite;
            return sprite;
        }

        private void OnDisable()
        {
            if (_ppuCache == null)
                return;

            foreach (var pair in _ppuCache)
            {
                if (pair.Value == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(pair.Value);
                else
                    DestroyImmediate(pair.Value);
            }
            _ppuCache.Clear();
        }
    }
}
