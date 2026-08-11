using System;
using UnityEngine;

namespace Trpg.Pawns
{
    public enum PartSlot : byte
    {
        HairBack = 0,
        Legs = 1,
        Bottom = 2,
        Shoes = 3,
        Torso = 4,
        Top = 5,
        Eyes = 6,
        Head = 7,
        HairFront = 8,
        Hat = 9,
        Count = 10
    }

    public enum ChannelId : byte
    {
        Root = 0,
        Legs = 1,
        Feet = 2,
        Torso = 3,
        Eyes = 4,
        Head = 5,
        Hair = 6,
        Count = 7
    }

    public enum BodyHeight : byte
    {
        Default = 0,
        Short = 1,
        Tall = 2
    }

    public enum PaletteChannel : byte
    {
        None = 0,
        Skin = 1,
        Hair = 2,
        Eye = 3
    }

    [Serializable]
    public struct PawnAppearance : IEquatable<PawnAppearance>
    {
        public const byte NonePartId = byte.MaxValue;

        [SerializeField] private BodyHeight _height;
        [SerializeField] private bool _broadShoulders;
        [SerializeField] private byte _headId;
        [SerializeField] private byte _hairFrontId;
        [SerializeField] private byte _hairBackId;
        [SerializeField] private byte _hatId;
        [SerializeField] private byte _topId;
        [SerializeField] private byte _bottomId;
        [SerializeField] private byte _shoesId;
        [SerializeField] private byte _eyesId;
        [SerializeField] private byte _legsId;
        // 0은 Definition 기본 Portrait, 1..255는 Library index + 1입니다.
        // 기존 에셋에 이 필드가 없어도 0으로 역직렬화되어 호환됩니다.
        [SerializeField] private byte _portraitId;
        [SerializeField] private Color32 _skinColor;
        [SerializeField] private Color32 _hairColor;
        [SerializeField] private Color32 _eyeColor;

        public BodyHeight Height => NormalizeHeight(_height);
        public bool BroadShoulders => _broadShoulders;
        public byte PortraitId => _portraitId == 0
            ? NonePartId
            : (byte)(_portraitId - 1);
        public Color32 SkinColor => _skinColor;
        public Color32 HairColor => _hairColor;
        public Color32 EyeColor => _eyeColor;

        public static PawnAppearance Default
        {
            get
            {
                return new PawnAppearance
                {
                    _height = BodyHeight.Default,
                    _broadShoulders = false,
                    _headId = 0,
                    _hairFrontId = 0,
                    _hairBackId = 0,
                    _hatId = NonePartId,
                    _topId = NonePartId,
                    _bottomId = NonePartId,
                    _shoesId = NonePartId,
                    _eyesId = 0,
                    _legsId = 0,
                    _portraitId = 0,
                    _skinColor = new Color32(224, 174, 148, 255),
                    _hairColor = new Color32(110, 58, 45, 255),
                    _eyeColor = new Color32(45, 88, 90, 255)
                };
            }
        }

        public byte GetPartId(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.HairBack:
                    return _hairBackId;
                case PartSlot.Legs:
                    return _legsId;
                case PartSlot.Bottom:
                    return _bottomId;
                case PartSlot.Shoes:
                    return _shoesId;
                case PartSlot.Torso:
                    return NonePartId;
                case PartSlot.Top:
                    return _topId;
                case PartSlot.Eyes:
                    return _eyesId;
                case PartSlot.Head:
                    return _headId;
                case PartSlot.HairFront:
                    return _hairFrontId;
                case PartSlot.Hat:
                    return _hatId;
                default:
                    return NonePartId;
            }
        }

        public PawnAppearance WithBodyShape(
            BodyHeight height,
            bool broadShoulders)
        {
            var copy = this;
            copy._height = NormalizeHeight(height);
            copy._broadShoulders = broadShoulders;
            return copy;
        }

        public PawnAppearance WithPart(PartSlot slot, byte partId)
        {
            var copy = this;
            switch (slot)
            {
                case PartSlot.HairBack:
                    copy._hairBackId = partId;
                    break;
                case PartSlot.Legs:
                    copy._legsId = partId;
                    break;
                case PartSlot.Bottom:
                    copy._bottomId = partId;
                    break;
                case PartSlot.Shoes:
                    copy._shoesId = partId;
                    break;
                case PartSlot.Top:
                    copy._topId = partId;
                    break;
                case PartSlot.Eyes:
                    copy._eyesId = partId;
                    break;
                case PartSlot.Head:
                    copy._headId = partId;
                    break;
                case PartSlot.HairFront:
                    copy._hairFrontId = partId;
                    break;
                case PartSlot.Hat:
                    copy._hatId = partId;
                    break;
            }

            return copy;
        }

        public PawnAppearance WithPortrait(byte portraitId)
        {
            var copy = this;
            copy._portraitId = portraitId == NonePartId
                ? (byte)0
                : (byte)(portraitId + 1);
            return copy;
        }

        public PawnAppearance WithColor(
            PaletteChannel channel,
            Color32 color)
        {
            var copy = this;
            color.a = 255;
            switch (channel)
            {
                case PaletteChannel.Skin:
                    copy._skinColor = color;
                    break;
                case PaletteChannel.Hair:
                    copy._hairColor = color;
                    break;
                case PaletteChannel.Eye:
                    copy._eyeColor = color;
                    break;
            }

            return copy;
        }

        public PawnAppearance WithVisibleColorDefaults()
        {
            var copy = this;
            copy._height = NormalizeHeight(copy._height);
            if (copy._skinColor.a == 0)
                copy._skinColor = Default._skinColor;
            if (copy._hairColor.a == 0)
                copy._hairColor = Default._hairColor;
            if (copy._eyeColor.a == 0)
                copy._eyeColor = Default._eyeColor;
            return copy;
        }

        public bool Equals(PawnAppearance other)
        {
            return Height == other.Height &&
                   _broadShoulders == other._broadShoulders &&
                   _headId == other._headId &&
                   _hairFrontId == other._hairFrontId &&
                   _hairBackId == other._hairBackId &&
                   _hatId == other._hatId &&
                   _topId == other._topId &&
                   _bottomId == other._bottomId &&
                   _shoesId == other._shoesId &&
                   _eyesId == other._eyesId &&
                   _legsId == other._legsId &&
                   _portraitId == other._portraitId &&
                   _skinColor.Equals(other._skinColor) &&
                   _hairColor.Equals(other._hairColor) &&
                   _eyeColor.Equals(other._eyeColor);
        }

        public override bool Equals(object obj)
        {
            return obj is PawnAppearance other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (int)Height;
                hash = hash * 31 + (_broadShoulders ? 1 : 0);
                hash = hash * 31 + _headId;
                hash = hash * 31 + _hairFrontId;
                hash = hash * 31 + _hairBackId;
                hash = hash * 31 + _hatId;
                hash = hash * 31 + _topId;
                hash = hash * 31 + _bottomId;
                hash = hash * 31 + _shoesId;
                hash = hash * 31 + _eyesId;
                hash = hash * 31 + _legsId;
                hash = hash * 31 + _portraitId;
                hash = hash * 31 + ColorHash(_skinColor);
                hash = hash * 31 + ColorHash(_hairColor);
                hash = hash * 31 + ColorHash(_eyeColor);
                return hash;
            }
        }

        public static bool operator ==(
            PawnAppearance left,
            PawnAppearance right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            PawnAppearance left,
            PawnAppearance right)
        {
            return !left.Equals(right);
        }

        private static BodyHeight NormalizeHeight(BodyHeight height)
        {
            return height == BodyHeight.Short ||
                   height == BodyHeight.Tall
                ? height
                : BodyHeight.Default;
        }

        private static int ColorHash(Color32 color)
        {
            unchecked
            {
                return color.r |
                       (color.g << 8) |
                       (color.b << 16) |
                       (color.a << 24);
            }
        }
    }
}
