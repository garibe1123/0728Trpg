using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trpg.Pawns
{
    public static class PawnPartSlotRules
    {
        public static bool ParentIsRigRoot(PartSlot slot)
        {
            return slot == PartSlot.Legs;
        }

        public static PartSlot GetParentSlot(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.HairBack:
                case PartSlot.Eyes:
                case PartSlot.HairFront:
                case PartSlot.Hat:
                    return PartSlot.Head;
                case PartSlot.Bottom:
                case PartSlot.Shoes:
                case PartSlot.Torso:
                    return PartSlot.Legs;
                case PartSlot.Top:
                case PartSlot.Head:
                    return PartSlot.Torso;
                default:
                    return PartSlot.Legs;
            }
        }

        public static bool HasAnimationChannel(PartSlot slot)
        {
            return slot != PartSlot.Bottom &&
                   slot != PartSlot.Top &&
                   slot != PartSlot.Hat;
        }

        public static ChannelId GetAnimationChannel(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Legs:
                    return ChannelId.Legs;
                case PartSlot.Shoes:
                    return ChannelId.Feet;
                case PartSlot.Torso:
                    return ChannelId.Torso;
                case PartSlot.Eyes:
                    return ChannelId.Eyes;
                case PartSlot.Head:
                    return ChannelId.Head;
                case PartSlot.HairBack:
                case PartSlot.HairFront:
                    return ChannelId.Hair;
                default:
                    return ChannelId.Root;
            }
        }

        public static int GetSortingOrder(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.HairBack:
                    return 10;
                case PartSlot.Legs:
                    return 20;
                case PartSlot.Bottom:
                    return 30;
                case PartSlot.Shoes:
                    return 40;
                case PartSlot.Torso:
                    return 50;
                case PartSlot.Top:
                    return 60;
                case PartSlot.Eyes:
                    return 70;
                case PartSlot.Head:
                    return 80;
                case PartSlot.HairFront:
                    return 90;
                case PartSlot.Hat:
                    return 100;
                default:
                    return 0;
            }
        }

        public static PaletteChannel GetPaletteChannel(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Head:
                case PartSlot.Torso:
                case PartSlot.Legs:
                    return PaletteChannel.Skin;
                case PartSlot.HairBack:
                case PartSlot.HairFront:
                    return PaletteChannel.Hair;
                case PartSlot.Eyes:
                    return PaletteChannel.Eye;
                default:
                    return PaletteChannel.None;
            }
        }

        public static bool UsesShoulderMorph(PartSlot slot)
        {
            return slot == PartSlot.Torso || slot == PartSlot.Top;
        }

        public static bool UsesLowerBodyHeightMorph(PartSlot slot)
        {
            return slot == PartSlot.Legs || slot == PartSlot.Bottom;
        }

        public static bool UsesUpperBodyHeightMorph(PartSlot slot)
        {
            return slot == PartSlot.Torso || slot == PartSlot.Top;
        }

        // 기존 호출부와의 호환성을 유지합니다.
        public static bool UsesHeightMorph(PartSlot slot)
        {
            return UsesLowerBodyHeightMorph(slot);
        }

        // 기존 이름은 유지하되 Tall뿐 아니라 Short에도 사용하는 상체 규칙입니다.
        public static bool UsesTallTorsoMorph(PartSlot slot)
        {
            return UsesUpperBodyHeightMorph(slot);
        }
    }

    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Pawn Sprite Library",
        fileName = "PawnSpriteLibrary")]
    public sealed class PawnSpriteLibrary : ScriptableObject
    {
        public const int ExpectedCanvasSize = 64;
        public const int HeightPixelDelta = 2;
        public const int TallTorsoPixelDelta = 1;
        public const int ShoulderPixelDelta = 2;
        public const int ShoulderCenterX = 32;
        // 하체 Sprite가 64x64 캔버스 안의 어느 높이에 배치됐는지
        // 자동 탐색하지 못했을 때만 사용하는 안전한 fallback 값입니다.
        public const int LegStretchCenterY = 18;
        public const int TorsoStretchCenterY = 31;
        private const byte MorphAlphaThreshold = 8;
        private const float LowerBodySearchStartRatio = 0.30f;
        private const float LowerBodySearchEndRatio = 0.72f;
        private const float LowerBodyTargetRatio = 0.52f;

        private static readonly int SkinShadowId =
            Shader.PropertyToID("_SkinShadow");
        private static readonly int SkinBaseId =
            Shader.PropertyToID("_SkinBase");
        private static readonly int SkinHighlightId =
            Shader.PropertyToID("_SkinHighlight");
        private static readonly int HairShadowId =
            Shader.PropertyToID("_HairShadow");
        private static readonly int HairBaseId =
            Shader.PropertyToID("_HairBase");
        private static readonly int HairHighlightId =
            Shader.PropertyToID("_HairHighlight");
        private static readonly int EyeShadowId =
            Shader.PropertyToID("_EyeShadow");
        private static readonly int EyeBaseId =
            Shader.PropertyToID("_EyeBase");
        private static readonly int EyeHighlightId =
            Shader.PropertyToID("_EyeHighlight");

        [Header("Body - Default Shape Source")]
        [SerializeField, Tooltip(
            "기본 키·기본 어깨 기준 Torso Sprite. 넓은 어깨는 코드가 좌우 1px씩 자동 확장합니다.")]
        private Sprite _torso;

        [SerializeField, Tooltip(
            "기본 키 기준 하체 후보. 키가 크거나 작으면 불투명 픽셀 범위에서 허벅지 구간을 자동 탐색해 ±2px 변형합니다.")]
        private Sprite[] _legs = Array.Empty<Sprite>();

        [Header("Head / Eyes - Optional")]
        [SerializeField] private Sprite[] _heads = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _eyes = Array.Empty<Sprite>();

        [Header("Hair")]
        [SerializeField] private Sprite[] _hairFronts = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _hairBacks = Array.Empty<Sprite>();

        [Header("Clothes - Default Shape Source")]
        [SerializeField, Tooltip(
            "기본 어깨 기준 상의. 넓은 어깨에서 Torso와 같은 방식으로 좌우 1px씩 자동 확장합니다.")]
        private Sprite[] _tops = Array.Empty<Sprite>();

        [SerializeField, Tooltip(
            "기본 키 기준 하의. 키 변화 시 Legs와 같은 방식으로 세로 ±2px 자동 변형합니다.")]
        private Sprite[] _bottoms = Array.Empty<Sprite>();

        [SerializeField] private Sprite[] _shoes = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _hats = Array.Empty<Sprite>();

        [Header("Portraits")]
        [SerializeField, Tooltip(
            "모듈형 Pawn이 런타임 커스터마이징에서 선택할 Portrait 목록")]
        private Sprite[] _portraits = Array.Empty<Sprite>();

        [Header("Palette")]
        [SerializeField, Tooltip(
            "인덱스 컬러 치환 Shader를 사용하는 공용 Material 템플릿")]
        private Material _paletteMaterialTemplate;

        [SerializeField, Range(0.1f, 1f)]
        private float _shadowMultiplier = 0.65f;

        [SerializeField, Range(1f, 2f)]
        private float _highlightMultiplier = 1.25f;

        [NonSerialized] private Dictionary<PaletteMaterialKey, Material>
            _paletteMaterialCache;
        [NonSerialized] private Dictionary<SelectionMaterialKey, Material>
            _selectionMaterialCache;
        [NonSerialized] private Dictionary<SpriteVariantKey, Sprite>
            _spriteVariantCache;
        [NonSerialized] private List<Texture2D> _generatedTextures;
        [NonSerialized] private HashSet<int> _unreadableWarnings;

        public Material PaletteMaterialTemplate => _paletteMaterialTemplate;
        public Sprite Torso => _torso;
        public int PortraitCount => _portraits != null ? _portraits.Length : 0;

        public Sprite GetPortrait(int index)
        {
            return _portraits != null && index >= 0 && index < _portraits.Length
                ? _portraits[index]
                : null;
        }

        public string GetPortraitDisplayName(int index)
        {
            var portrait = GetPortrait(index);
            return portrait != null && !string.IsNullOrWhiteSpace(portrait.name)
                ? portrait.name
                : $"Portrait {index}";
        }

        public Sprite ResolvePortrait(byte portraitId)
        {
            return portraitId == PawnAppearance.NonePartId
                ? null
                : GetPortrait(portraitId);
        }

        public int GetPartCount(PartSlot slot)
        {
            if (slot == PartSlot.Torso)
                return _torso != null ? 1 : 0;

            var sprites = GetPartArray(slot);
            return sprites != null ? sprites.Length : 0;
        }

        public Sprite GetPartSprite(PartSlot slot, int index)
        {
            if (slot == PartSlot.Torso)
                return index == 0 ? _torso : null;

            var sprites = GetPartArray(slot);
            if (sprites == null || index < 0 || index >= sprites.Length)
                return null;

            return sprites[index];
        }

        public bool TryGetPart(
            PartSlot slot,
            byte partId,
            out Sprite sprite)
        {
            sprite = null;
            if (partId == PawnAppearance.NonePartId)
                return false;

            if (slot == PartSlot.Torso)
            {
                sprite = _torso;
                return sprite != null;
            }

            var sprites = GetPartArray(slot);
            if (sprites == null || partId >= sprites.Length)
                return false;

            sprite = sprites[partId];
            return sprite != null;
        }

        public Sprite ResolveSprite(
            PartSlot slot,
            byte partId,
            BodyHeight height,
            bool broadShoulders,
            int pixelsPerUnit = PixelSnap.DefaultPixelsPerUnit)
        {
            Sprite source;
            if (slot == PartSlot.Torso)
            {
                source = _torso;
            }
            else if (!TryGetPart(slot, partId, out source))
            {
                return null;
            }

            if (source == null)
                return null;

            var applyShoulder = broadShoulders &&
                                PawnPartSlotRules.UsesShoulderMorph(slot);
            var applyLowerBodyHeight = height != BodyHeight.Default &&
                                       PawnPartSlotRules
                                           .UsesLowerBodyHeightMorph(slot);
            var applyUpperBodyHeight = height != BodyHeight.Default &&
                                       PawnPartSlotRules
                                           .UsesUpperBodyHeightMorph(slot);
            var normalizedPpu = PixelSnap.NormalizePixelsPerUnit(
                pixelsPerUnit);
            var remapPpu = Mathf.RoundToInt(source.pixelsPerUnit) !=
                           normalizedPpu;
            if (!applyShoulder && !applyLowerBodyHeight &&
                !applyUpperBodyHeight && !remapPpu)
            {
                return source;
            }

            if (_spriteVariantCache == null)
            {
                _spriteVariantCache =
                    new Dictionary<SpriteVariantKey, Sprite>();
            }

            var key = new SpriteVariantKey(
                source.GetInstanceID(),
                slot,
                height,
                broadShoulders,
                normalizedPpu);
            if (_spriteVariantCache.TryGetValue(key, out var cached) &&
                cached != null)
            {
                return cached;
            }

            Sprite generated;
            if (applyShoulder || applyLowerBodyHeight ||
                applyUpperBodyHeight)
            {
                generated = CreatePixelPerfectVariant(
                    source,
                    slot,
                    height,
                    broadShoulders,
                    normalizedPpu);
            }
            else
            {
                generated = CreatePixelsPerUnitVariant(
                    source,
                    normalizedPpu);
            }

            if (generated == null)
                return source;

            _spriteVariantCache[key] = generated;
            return generated;
        }

        public string GetPartDisplayName(PartSlot slot, int index)
        {
            var sprite = GetPartSprite(slot, index);
            return sprite != null && !string.IsNullOrWhiteSpace(sprite.name)
                ? sprite.name
                : $"{GetSlotDisplayName(slot)} {index}";
        }

        public Vector2Int GetBodyOffset(
            BodyHeight height,
            ChannelId channel)
        {
            if (channel == ChannelId.Torso)
            {
                switch (height)
                {
                    case BodyHeight.Short:
                        return new Vector2Int(0, -HeightPixelDelta);
                    case BodyHeight.Tall:
                        return new Vector2Int(0, HeightPixelDelta);
                }
            }

            if (channel == ChannelId.Head)
            {
                switch (height)
                {
                    case BodyHeight.Short:
                        return new Vector2Int(0, -TallTorsoPixelDelta);
                    case BodyHeight.Tall:
                        return new Vector2Int(0, TallTorsoPixelDelta);
                }
            }

            return Vector2Int.zero;
        }

        public Material ResolvePaletteMaterial(
            in PawnAppearance appearance)
        {
            if (_paletteMaterialTemplate == null)
                return null;

            if (_paletteMaterialCache == null)
            {
                _paletteMaterialCache =
                    new Dictionary<PaletteMaterialKey, Material>();
            }

            var normalized = appearance.WithVisibleColorDefaults();
            var key = new PaletteMaterialKey(
                normalized.SkinColor,
                normalized.HairColor,
                normalized.EyeColor);
            if (_paletteMaterialCache.TryGetValue(key, out var material) &&
                material != null)
            {
                return material;
            }

            material = new Material(_paletteMaterialTemplate)
            {
                name = $"PawnPalette_{key.GetHashCode():X8}",
                hideFlags = HideFlags.HideAndDontSave
            };
            ApplyPalette(material, normalized);
            _paletteMaterialCache[key] = material;
            return material;
        }

        public Material ResolveSelectionMaterial(
            Material template,
            in PawnAppearance appearance)
        {
            if (template == null)
                return null;

            // Edit Mode 미리보기에서는 임시 Material을 직렬화 대상으로
            // 붙이지 않습니다. 씬 종료 시 DontSave assertion을 방지합니다.
            if (!Application.isPlaying)
                return template;

            if (_selectionMaterialCache == null)
            {
                _selectionMaterialCache =
                    new Dictionary<SelectionMaterialKey, Material>();
            }

            var normalized = appearance.WithVisibleColorDefaults();
            var paletteKey = new PaletteMaterialKey(
                normalized.SkinColor,
                normalized.HairColor,
                normalized.EyeColor);
            var key = new SelectionMaterialKey(
                template.GetInstanceID(),
                paletteKey);

            if (_selectionMaterialCache.TryGetValue(
                    key,
                    out var material) &&
                material != null)
            {
                return material;
            }

            material = new Material(template)
            {
                name = $"PawnActive_{template.name}_{key.GetHashCode():X8}",
                hideFlags = HideFlags.HideAndDontSave
            };
            ApplyPalette(material, normalized);
            _selectionMaterialCache[key] = material;
            return material;
        }

        public bool ValidateConfiguration(out string error)
        {
            if (_torso == null)
            {
                error = "기본 Torso Sprite가 비어 있습니다.";
                return false;
            }

            if (!HasAtLeastOneSprite(_legs))
            {
                error = "Legs 배열에 최소 1개의 Sprite가 필요합니다.";
                return false;
            }

            if (HasTooManyParts(_legs) ||
                HasTooManyParts(_heads) ||
                HasTooManyParts(_eyes) ||
                HasTooManyParts(_hairFronts) ||
                HasTooManyParts(_hairBacks) ||
                HasTooManyParts(_hats) ||
                HasTooManyParts(_tops) ||
                HasTooManyParts(_bottoms) ||
                HasTooManyParts(_shoes) ||
                HasTooManyParts(_portraits))
            {
                error = "각 파츠 배열은 byte ID 범위 때문에 255개를 넘길 수 없습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void ReleaseRuntimeCaches()
        {
            if (_paletteMaterialCache != null)
            {
                foreach (var pair in _paletteMaterialCache)
                    DestroyObject(pair.Value);
                _paletteMaterialCache.Clear();
            }

            if (_selectionMaterialCache != null)
            {
                foreach (var pair in _selectionMaterialCache)
                    DestroyObject(pair.Value);
                _selectionMaterialCache.Clear();
            }

            if (_spriteVariantCache != null)
            {
                foreach (var pair in _spriteVariantCache)
                    DestroyObject(pair.Value);
                _spriteVariantCache.Clear();
            }

            if (_generatedTextures != null)
            {
                for (var index = 0; index < _generatedTextures.Count; index++)
                    DestroyObject(_generatedTextures[index]);
                _generatedTextures.Clear();
            }

            _unreadableWarnings?.Clear();
        }

        private Sprite[] GetPartArray(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.HairBack:
                    return _hairBacks;
                case PartSlot.Legs:
                    return _legs;
                case PartSlot.Bottom:
                    return _bottoms;
                case PartSlot.Shoes:
                    return _shoes;
                case PartSlot.Top:
                    return _tops;
                case PartSlot.Eyes:
                    return _eyes;
                case PartSlot.Head:
                    return _heads;
                case PartSlot.HairFront:
                    return _hairFronts;
                case PartSlot.Hat:
                    return _hats;
                default:
                    return null;
            }
        }

        private Sprite CreatePixelPerfectVariant(
            Sprite source,
            PartSlot slot,
            BodyHeight height,
            bool broadShoulders,
            int pixelsPerUnit)
        {
            if (!TryReadSourcePixels(source, out var pixels))
                return null;

            var transformed = pixels;
            if (broadShoulders &&
                PawnPartSlotRules.UsesShoulderMorph(slot))
            {
                transformed = ExpandHorizontallyTwoPixels(transformed);
            }

            if (PawnPartSlotRules.UsesLowerBodyHeightMorph(slot))
            {
                // 고정 Y 좌표를 사용하면 Sprite가 캔버스 안에서 조금만
                // 다르게 배치돼도 발이나 투명 영역이 늘어날 수 있습니다.
                // 각 Legs/Bottom Sprite의 실제 불투명 영역을 기준으로
                // 허벅지 쪽의 안정적인 행을 자동 선택합니다.
                var lowerBodyMorphY = FindLowerBodyMorphRow(transformed);

                if (height == BodyHeight.Tall)
                {
                    transformed = ExtendLowerBodyRows(
                        transformed,
                        lowerBodyMorphY,
                        HeightPixelDelta);
                }
                else if (height == BodyHeight.Short)
                {
                    transformed = ShortenLowerBodyRows(
                        transformed,
                        lowerBodyMorphY,
                        HeightPixelDelta);
                }
            }

            if (PawnPartSlotRules.UsesUpperBodyHeightMorph(slot))
            {
                if (height == BodyHeight.Tall)
                    transformed = ExtendTorsoOnePixel(transformed);
                else if (height == BodyHeight.Short)
                    transformed = ShortenTorsoOnePixel(transformed);
            }

            var variantName = $"{source.name}_{slot}_{height}_" +
                              (broadShoulders ? "Broad" : "Default");
            return CreateGeneratedSprite(
                source,
                transformed,
                variantName,
                pixelsPerUnit);
        }

        private Sprite CreateGeneratedSprite(
            Sprite source,
            Color32[] pixels,
            string spriteName,
            int pixelsPerUnit)
        {
            if (source == null || pixels == null ||
                pixels.Length != ExpectedCanvasSize * ExpectedCanvasSize)
            {
                return null;
            }

            var texture = new Texture2D(
                ExpectedCanvasSize,
                ExpectedCanvasSize,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = spriteName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            if (_generatedTextures == null)
                _generatedTextures = new List<Texture2D>();
            _generatedTextures.Add(texture);

            var sourceSize = source.rect.size;
            var pivot = new Vector2(
                source.pivot.x / Mathf.Max(1f, sourceSize.x),
                source.pivot.y / Mathf.Max(1f, sourceSize.y));
            var sprite = Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    ExpectedCanvasSize,
                    ExpectedCanvasSize),
                pivot,
                PixelSnap.NormalizePixelsPerUnit(pixelsPerUnit),
                0,
                SpriteMeshType.FullRect,
                Vector4.zero);
            sprite.name = spriteName;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private Sprite CreatePixelsPerUnitVariant(
            Sprite source,
            int pixelsPerUnit)
        {
            if (source == null || source.texture == null)
                return null;

            Rect packedRect;
            Vector2 packedOffset;
            try
            {
                packedRect = source.textureRect;
                packedOffset = source.textureRectOffset;
            }
            catch (UnityException)
            {
                return null;
            }

            // 회전 패킹이 없는 경우에는 원본 Texture를 그대로 재사용합니다.
            // Tight Sprite의 Pivot은 textureRectOffset만큼 보정해야
            // 64x64 논리 캔버스에서의 원래 위치가 유지됩니다.
            var rotation = source.packed
                ? source.packingRotation
                : SpritePackingRotation.None;
            if (rotation == SpritePackingRotation.None)
            {
                var packedWidth = Mathf.Max(1f, packedRect.width);
                var packedHeight = Mathf.Max(1f, packedRect.height);
                var localPivot = source.pivot - packedOffset;
                var pivot = new Vector2(
                    localPivot.x / packedWidth,
                    localPivot.y / packedHeight);
                var sprite = Sprite.Create(
                    source.texture,
                    packedRect,
                    pivot,
                    PixelSnap.NormalizePixelsPerUnit(pixelsPerUnit),
                    0,
                    SpriteMeshType.FullRect,
                    Vector4.zero);
                sprite.name = $"{source.name}_PPU{pixelsPerUnit}";
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }

            // 회전/반전 패킹 Sprite는 64x64 논리 캔버스로 복원한 뒤
            // 새 Sprite를 만들어야 방향과 Pivot이 보존됩니다.
            if (!TryReadSourcePixels(source, out var pixels))
                return null;

            return CreateGeneratedSprite(
                source,
                pixels,
                $"{source.name}_PPU{pixelsPerUnit}",
                pixelsPerUnit);
        }

        private bool TryReadSourcePixels(
            Sprite source,
            out Color32[] pixels)
        {
            pixels = null;
            if (source == null || source.texture == null)
                return false;

            var logicalRect = source.rect;
            var logicalWidth = Mathf.RoundToInt(logicalRect.width);
            var logicalHeight = Mathf.RoundToInt(logicalRect.height);
            if (logicalWidth != ExpectedCanvasSize ||
                logicalHeight != ExpectedCanvasSize)
            {
                WarnOnce(
                    source,
                    $"[{source.name}] 체형 자동 변형은 논리 Sprite Rect가 " +
                    $"{ExpectedCanvasSize}x{ExpectedCanvasSize}인 Sprite만 " +
                    $"지원합니다. 현재 Sprite Rect: " +
                    $"{logicalWidth}x{logicalHeight}");
                return false;
            }

            Rect packedRect;
            Vector2 packedOffset;
            SpritePackingRotation rotation;
            try
            {
                packedRect = source.textureRect;
                packedOffset = source.textureRectOffset;
                rotation = source.packed
                    ? source.packingRotation
                    : SpritePackingRotation.None;
            }
            catch (UnityException)
            {
                WarnOnce(
                    source,
                    $"[{source.name}] Sprite의 실제 Texture 영역을 " +
                    "읽지 못했습니다.");
                return false;
            }

            if (rotation == SpritePackingRotation.Any)
            {
                WarnOnce(
                    source,
                    $"[{source.name}] Any 회전 패킹 Sprite는 자동 픽셀 " +
                    "변형을 지원하지 않습니다. Atlas 회전 패킹을 끄십시오.");
                return false;
            }

            var packedWidth = Mathf.RoundToInt(packedRect.width);
            var packedHeight = Mathf.RoundToInt(packedRect.height);
            var packedStartX = Mathf.RoundToInt(packedRect.x);
            var packedStartY = Mathf.RoundToInt(packedRect.y);
            var targetStartX = Mathf.RoundToInt(packedOffset.x);
            var targetStartY = Mathf.RoundToInt(packedOffset.y);

            if (packedWidth <= 0 || packedHeight <= 0)
            {
                WarnOnce(
                    source,
                    $"[{source.name}] Sprite Texture 영역의 크기가 " +
                    "올바르지 않습니다.");
                return false;
            }

            try
            {
                var texture = source.texture;
                var texturePixels = texture.GetPixels32();
                var textureWidth = texture.width;
                var textureHeight = texture.height;

                if (packedStartX < 0 || packedStartY < 0 ||
                    packedStartX + packedWidth > textureWidth ||
                    packedStartY + packedHeight > textureHeight)
                {
                    WarnOnce(
                        source,
                        $"[{source.name}] Sprite Texture 영역이 원본 " +
                        "Texture 범위를 벗어났습니다.");
                    return false;
                }

                // textureRect는 Tight Sprite에서 11x15처럼 반환될 수 있습니다.
                // 먼저 전체 64x64 투명 캔버스를 만들고,
                // textureRectOffset을 사용해 원래 위치로 복원합니다.
                pixels = new Color32[
                    ExpectedCanvasSize * ExpectedCanvasSize];

                for (var packedY = 0; packedY < packedHeight; packedY++)
                {
                    for (var packedX = 0; packedX < packedWidth; packedX++)
                    {
                        var sourceIndex =
                            (packedStartY + packedY) * textureWidth +
                            packedStartX + packedX;

                        GetLogicalPixelCoordinate(
                            rotation,
                            packedX,
                            packedY,
                            packedWidth,
                            packedHeight,
                            out var localX,
                            out var localY);

                        var targetX = targetStartX + localX;
                        var targetY = targetStartY + localY;
                        if (targetX < 0 ||
                            targetX >= ExpectedCanvasSize ||
                            targetY < 0 ||
                            targetY >= ExpectedCanvasSize)
                        {
                            continue;
                        }

                        pixels[targetY * ExpectedCanvasSize + targetX] =
                            texturePixels[sourceIndex];
                    }
                }

                return true;
            }
            catch (ArgumentException)
            {
                WarnOnce(
                    source,
                    $"[{source.name}] 키·어깨 자동 픽셀 변형을 " +
                    "사용하려면 Texture Import Settings의 " +
                    "Read/Write를 켜야 합니다.");
                return false;
            }
            catch (UnityException)
            {
                WarnOnce(
                    source,
                    $"[{source.name}] 키·어깨 자동 픽셀 변형을 " +
                    "사용하려면 Texture Import Settings의 " +
                    "Read/Write를 켜야 합니다.");
                return false;
            }
        }

        private static void GetLogicalPixelCoordinate(
            SpritePackingRotation rotation,
            int packedX,
            int packedY,
            int packedWidth,
            int packedHeight,
            out int logicalX,
            out int logicalY)
        {
            switch (rotation)
            {
                case SpritePackingRotation.FlipHorizontal:
                    logicalX = packedWidth - 1 - packedX;
                    logicalY = packedY;
                    return;

                case SpritePackingRotation.FlipVertical:
                    logicalX = packedX;
                    logicalY = packedHeight - 1 - packedY;
                    return;

                case SpritePackingRotation.Rotate180:
                    logicalX = packedWidth - 1 - packedX;
                    logicalY = packedHeight - 1 - packedY;
                    return;

                default:
                    logicalX = packedX;
                    logicalY = packedY;
                    return;
            }
        }

        private static Color32[] ExpandHorizontallyTwoPixels(
            Color32[] source)
        {
            var result = new Color32[source.Length];
            var leftBoundary = ShoulderCenterX - 1;
            var rightBoundary = ShoulderCenterX;

            for (var y = 0; y < ExpectedCanvasSize; y++)
            {
                var row = y * ExpectedCanvasSize;
                for (var x = 0; x < ExpectedCanvasSize; x++)
                {
                    int sourceX;
                    if (x < leftBoundary)
                        sourceX = x + 1;
                    else if (x == leftBoundary)
                        sourceX = leftBoundary;
                    else if (x == rightBoundary)
                        sourceX = rightBoundary;
                    else
                        sourceX = x - 1;

                    result[row + x] = source[row + sourceX];
                }
            }

            return result;
        }

        private static int FindLowerBodyMorphRow(Color32[] source)
        {
            if (source == null ||
                source.Length != ExpectedCanvasSize * ExpectedCanvasSize)
            {
                return LegStretchCenterY;
            }

            var minY = ExpectedCanvasSize;
            var maxY = -1;

            for (var y = 0; y < ExpectedCanvasSize; y++)
            {
                if (CountOpaquePixelsInRow(source, y) <= 0)
                    continue;

                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }

            if (maxY < minY)
                return LegStretchCenterY;

            var occupiedHeight = maxY - minY + 1;
            if (occupiedHeight <= HeightPixelDelta + 2)
            {
                return Mathf.Clamp(
                    minY + occupiedHeight / 2,
                    1,
                    ExpectedCanvasSize - HeightPixelDelta - 2);
            }

            var searchStart = Mathf.Clamp(
                minY + Mathf.RoundToInt(
                    (occupiedHeight - 1) * LowerBodySearchStartRatio),
                minY,
                maxY);
            var searchEnd = Mathf.Clamp(
                minY + Mathf.RoundToInt(
                    (occupiedHeight - 1) * LowerBodySearchEndRatio),
                searchStart,
                maxY);
            var targetY = Mathf.Clamp(
                minY + Mathf.RoundToInt(
                    (occupiedHeight - 1) * LowerBodyTargetRatio),
                searchStart,
                searchEnd);

            var bestY = targetY;
            var bestScore = int.MinValue;

            for (var y = searchStart; y <= searchEnd; y++)
            {
                var current = CountOpaquePixelsInRow(source, y);
                if (current <= 0)
                    continue;

                var below = y > minY
                    ? CountOpaquePixelsInRow(source, y - 1)
                    : 0;
                var above = y < maxY
                    ? CountOpaquePixelsInRow(source, y + 1)
                    : 0;

                // 폭이 넓고 앞뒤 행에도 픽셀이 이어지는 행을 우선합니다.
                // 이렇게 하면 발끝이나 한쪽 다리 한 줄이 길어지는 현상을
                // 피하고 허벅지/골반 사이의 안정적인 행을 선택합니다.
                var continuity = Mathf.Min(
                    current,
                    Mathf.Min(below, above));
                var distancePenalty = Mathf.Abs(y - targetY);
                var score = current * 16 +
                            continuity * 8 -
                            distancePenalty * 3;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestY = y;
            }

            return Mathf.Clamp(
                bestY,
                1,
                ExpectedCanvasSize - HeightPixelDelta - 2);
        }

        private static int CountOpaquePixelsInRow(
            Color32[] source,
            int y)
        {
            if (source == null || y < 0 || y >= ExpectedCanvasSize)
                return 0;

            var count = 0;
            var rowStart = y * ExpectedCanvasSize;
            for (var x = 0; x < ExpectedCanvasSize; x++)
            {
                if (source[rowStart + x].a > MorphAlphaThreshold)
                    count++;
            }

            return count;
        }

        private static Color32[] ExtendLowerBodyRows(
            Color32[] source,
            int morphY,
            int pixelDelta)
        {
            var result = new Color32[source.Length];
            var safeMorphY = Mathf.Clamp(
                morphY,
                0,
                ExpectedCanvasSize - pixelDelta - 1);

            for (var targetY = 0;
                 targetY < ExpectedCanvasSize;
                 targetY++)
            {
                int sourceY;

                // 발 아래쪽은 그대로 둡니다.
                if (targetY <= safeMorphY)
                {
                    sourceY = targetY;
                }
                // 선택한 허벅지 행을 pixelDelta만큼 복제합니다.
                else if (targetY <= safeMorphY + pixelDelta)
                {
                    sourceY = safeMorphY;
                }
                // 골반과 상단 영역만 위로 밀어 올립니다.
                else
                {
                    sourceY = targetY - pixelDelta;
                }

                CopyRow(source, sourceY, result, targetY);
            }

            return result;
        }

        private static Color32[] ShortenLowerBodyRows(
            Color32[] source,
            int morphY,
            int pixelDelta)
        {
            var result = new Color32[source.Length];
            var safeMorphY = Mathf.Clamp(
                morphY,
                0,
                ExpectedCanvasSize - pixelDelta - 1);

            for (var targetY = 0;
                 targetY < ExpectedCanvasSize;
                 targetY++)
            {
                // 발과 선택 행은 유지하고, 그 바로 위의 행들을 제거해
                // 골반과 상단 영역만 아래로 당깁니다.
                var sourceY = targetY <= safeMorphY
                    ? targetY
                    : targetY + pixelDelta;

                if (sourceY >= ExpectedCanvasSize)
                    continue;

                CopyRow(source, sourceY, result, targetY);
            }

            return result;
        }

        private static Color32[] ExtendTorsoOnePixel(Color32[] source)
        {
            var result = new Color32[source.Length];
            for (var y = 0; y < ExpectedCanvasSize; y++)
            {
                int sourceY;
                if (y < TorsoStretchCenterY)
                    sourceY = y;
                else if (y == TorsoStretchCenterY)
                    sourceY = TorsoStretchCenterY;
                else
                    sourceY = y - TallTorsoPixelDelta;

                CopyRow(source, sourceY, result, y);
            }

            return result;
        }

        private static Color32[] ShortenTorsoOnePixel(Color32[] source)
        {
            var result = new Color32[source.Length];
            for (var y = 0; y < ExpectedCanvasSize; y++)
            {
                var sourceY = y < TorsoStretchCenterY
                    ? y
                    : y + TallTorsoPixelDelta;
                if (sourceY >= ExpectedCanvasSize)
                    continue;

                CopyRow(source, sourceY, result, y);
            }

            return result;
        }

        private static void CopyRow(
            Color32[] source,
            int sourceY,
            Color32[] target,
            int targetY)
        {
            Array.Copy(
                source,
                sourceY * ExpectedCanvasSize,
                target,
                targetY * ExpectedCanvasSize,
                ExpectedCanvasSize);
        }

        private void ApplyPalette(
            Material material,
            in PawnAppearance appearance)
        {
            SetTriplet(
                material,
                SkinShadowId,
                SkinBaseId,
                SkinHighlightId,
                appearance.SkinColor);
            SetTriplet(
                material,
                HairShadowId,
                HairBaseId,
                HairHighlightId,
                appearance.HairColor);
            SetTriplet(
                material,
                EyeShadowId,
                EyeBaseId,
                EyeHighlightId,
                appearance.EyeColor);
        }

        private void SetTriplet(
            Material material,
            int shadowId,
            int baseId,
            int highlightId,
            Color32 baseColor)
        {
            if (material.HasProperty(shadowId))
            {
                material.SetColor(
                    shadowId,
                    ScaleColor(baseColor, _shadowMultiplier));
            }

            if (material.HasProperty(baseId))
                material.SetColor(baseId, baseColor);

            if (material.HasProperty(highlightId))
            {
                material.SetColor(
                    highlightId,
                    ScaleColor(baseColor, _highlightMultiplier));
            }
        }

        private static Color ScaleColor(Color32 color, float multiplier)
        {
            return new Color(
                Mathf.Clamp01(color.r / 255f * multiplier),
                Mathf.Clamp01(color.g / 255f * multiplier),
                Mathf.Clamp01(color.b / 255f * multiplier),
                1f);
        }

        private void WarnOnce(Sprite source, string message)
        {
            if (_unreadableWarnings == null)
                _unreadableWarnings = new HashSet<int>();

            var id = source.GetInstanceID();
            if (_unreadableWarnings.Add(id))
                Debug.LogWarning(message, source);
        }

        private static bool HasAtLeastOneSprite(Sprite[] sprites)
        {
            if (sprites == null)
                return false;

            for (var index = 0; index < sprites.Length; index++)
            {
                if (sprites[index] != null)
                    return true;
            }

            return false;
        }

        private static bool HasTooManyParts(Sprite[] sprites)
        {
            return sprites != null && sprites.Length > byte.MaxValue;
        }

        private static string GetSlotDisplayName(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.HairBack:
                    return "뒷머리";
                case PartSlot.Legs:
                    return "하체";
                case PartSlot.Bottom:
                    return "하의";
                case PartSlot.Shoes:
                    return "신발";
                case PartSlot.Torso:
                    return "상체";
                case PartSlot.Top:
                    return "상의";
                case PartSlot.Eyes:
                    return "눈동자";
                case PartSlot.Head:
                    return "머리";
                case PartSlot.HairFront:
                    return "앞머리";
                case PartSlot.Hat:
                    return "모자";
                default:
                    return slot.ToString();
            }
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ReleaseRuntimeCaches();
        }
#endif

        private void OnDisable()
        {
            ReleaseRuntimeCaches();
        }

        private readonly struct PaletteMaterialKey :
            IEquatable<PaletteMaterialKey>
        {
            private readonly Color32 _skin;
            private readonly Color32 _hair;
            private readonly Color32 _eye;

            public PaletteMaterialKey(
                Color32 skin,
                Color32 hair,
                Color32 eye)
            {
                _skin = skin;
                _hair = hair;
                _eye = eye;
            }

            public bool Equals(PaletteMaterialKey other)
            {
                return _skin.Equals(other._skin) &&
                       _hair.Equals(other._hair) &&
                       _eye.Equals(other._eye);
            }

            public override bool Equals(object obj)
            {
                return obj is PaletteMaterialKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + _skin.GetHashCode();
                    hash = hash * 31 + _hair.GetHashCode();
                    hash = hash * 31 + _eye.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct SelectionMaterialKey :
            IEquatable<SelectionMaterialKey>
        {
            private readonly int _templateId;
            private readonly PaletteMaterialKey _palette;

            public SelectionMaterialKey(
                int templateId,
                PaletteMaterialKey palette)
            {
                _templateId = templateId;
                _palette = palette;
            }

            public bool Equals(SelectionMaterialKey other)
            {
                return _templateId == other._templateId &&
                       _palette.Equals(other._palette);
            }

            public override bool Equals(object obj)
            {
                return obj is SelectionMaterialKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_templateId * 397) ^ _palette.GetHashCode();
                }
            }
        }

        private readonly struct SpriteVariantKey :
            IEquatable<SpriteVariantKey>
        {
            private readonly int _sourceId;
            private readonly PartSlot _slot;
            private readonly BodyHeight _height;
            private readonly bool _broad;
            private readonly int _pixelsPerUnit;

            public SpriteVariantKey(
                int sourceId,
                PartSlot slot,
                BodyHeight height,
                bool broad,
                int pixelsPerUnit)
            {
                _sourceId = sourceId;
                _slot = slot;
                _height = height;
                _broad = broad;
                _pixelsPerUnit = pixelsPerUnit;
            }

            public bool Equals(SpriteVariantKey other)
            {
                return _sourceId == other._sourceId &&
                       _slot == other._slot &&
                       _height == other._height &&
                       _broad == other._broad &&
                       _pixelsPerUnit == other._pixelsPerUnit;
            }

            public override bool Equals(object obj)
            {
                return obj is SpriteVariantKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _sourceId;
                    hash = hash * 397 ^ (int)_slot;
                    hash = hash * 397 ^ (int)_height;
                    hash = hash * 397 ^ (_broad ? 1 : 0);
                    hash = hash * 397 ^ _pixelsPerUnit;
                    return hash;
                }
            }
        }
    }
}
