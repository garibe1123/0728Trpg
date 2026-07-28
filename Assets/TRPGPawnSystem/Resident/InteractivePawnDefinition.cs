using UnityEngine;
using UnityEngine.Serialization;

namespace Trpg.Pawns
{
    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Interactive Pawn Definition",
        fileName = "InteractivePawnDefinition")]
    public sealed class InteractivePawnDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("콘텐츠 식별 ID")]
        private string _id = "interactive_new";

        [SerializeField, Tooltip("이 Pawn의 상호작용 종류")]
        private InteractivePawnKind _kind = InteractivePawnKind.Npc;

        [SerializeField, Tooltip("Moveable일 때 Player 또는 Monster 구분")]
        private MoveablePawnKind _moveableKind = MoveablePawnKind.Player;

        [Header("Info Bar")]
        [SerializeField, Tooltip("하단 정보 바에 표시할 이름")]
        private string _displayName;

        [SerializeField, TextArea(2, 5), Tooltip("하단 정보 바에 표시할 설명")]
        private string _description;

        [SerializeField, Tooltip("하단 정보 바 왼쪽에 표시할 Portrait")]
        private Sprite _portrait;

        [Header("Moveable")]
        [FormerlySerializedAs("_moveMeters")]
        [SerializeField, HideInInspector]
        private float _legacyMoveMeters;

        [FormerlySerializedAs("_moveMetersPerTurn")]
        [SerializeField, HideInInspector]
        private int _legacyMoveMetersPerTurn;

        [SerializeField, Range(10, 100), Tooltip(
            "이동 능력치. 실제 턴당 거리는 PawnSystemSettings의 환산값을 사용")]
        private int _movementScore = 40;

        [SerializeField, HideInInspector]
        private float _presentationMetersPerSecond = 6f;

        [Header("Presentation")]
        [SerializeField, Min(0.01f), Tooltip(
            "이동 거리에 관계없이 사용하는 전체 이동 연출 시간(초)")]
        private float _presentationDurationSeconds = 0.5f;

        [SerializeField, Min(0f), Tooltip(
            "체스 말을 집어 옮기듯 이동할 때 들어 올리는 높이")]
        private float _presentationHopHeight = 0.2f;

        [SerializeField, Range(0f, 30f), Tooltip(
            "이동 중 Pawn이 기울어지는 최대 Z 회전 각도")]
        private float _presentationRotationDegrees = 7f;

        [SerializeField, Range(1f, 2f), Tooltip(
            "선택 상태에서 적용할 Pawn 균일 확대 배율")]
        private float _selectedScale = 1.08f;

        public string Id => _id;
        public InteractivePawnKind Kind => _kind;
        public MoveablePawnKind MoveableKind => _moveableKind;
        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Portrait => _portrait;
        public int MovementScore
        {
            get
            {
                var legacyMeters = ResolveLegacyMoveMeters();
                return legacyMeters > 0f
                    ? Mathf.Clamp(
                        Mathf.RoundToInt(legacyMeters / 0.2f),
                        10,
                        100)
                    : Mathf.Clamp(_movementScore, 10, 100);
            }
        }
        public float PresentationMetersPerSecond => _presentationMetersPerSecond;
        public float PresentationDurationSeconds =>
            Mathf.Max(0.01f, _presentationDurationSeconds);
        public float PresentationHopHeight =>
            Mathf.Max(0f, _presentationHopHeight);
        public float PresentationRotationDegrees =>
            Mathf.Clamp(_presentationRotationDegrees, 0f, 30f);
        public float SelectedScale =>
            Mathf.Clamp(_selectedScale, 1f, 2f);

        private float ResolveLegacyMoveMeters()
        {
            if (_legacyMoveMeters > 0f)
            {
                return _legacyMoveMeters;
            }

            return _legacyMoveMetersPerTurn > 0
                ? _legacyMoveMetersPerTurn
                : 0f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var legacyMeters = ResolveLegacyMoveMeters();
            if (legacyMeters > 0f)
            {
                _movementScore = Mathf.Clamp(
                    Mathf.RoundToInt(legacyMeters / 0.2f),
                    10,
                    100);
                _legacyMoveMeters = 0f;
                _legacyMoveMetersPerTurn = 0;
            }

            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError($"[{name}] Definition Id가 비어 있습니다.", this);
            }
        }
#endif
    }
}
