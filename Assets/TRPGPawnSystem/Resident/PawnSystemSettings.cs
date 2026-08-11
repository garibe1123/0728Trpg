using UnityEngine;
using UnityEngine.Serialization;

namespace Trpg.Pawns
{
    public enum CampaignRuleSet
    {
        CallOfCthulhu7E = 0,
        Generic = 1
    }

    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Pawn System Settings",
        fileName = "PawnSystemSettings")]
    public sealed class PawnSystemSettings : ScriptableObject
    {
        [SerializeField, Tooltip("세이브 및 네트워크에서 사용할 설정 ID")]
        private string _id = "pawn_system_default";

        [Header("Rule Set")]
        [SerializeField, Tooltip(
            "캠페인에서 사용할 규칙 보조 계층. Generic은 범용 기능만 사용합니다.")]
        private CampaignRuleSet _campaignRuleSet =
            CampaignRuleSet.CallOfCthulhu7E;

        [SerializeField, Min(1), Tooltip(
            "CoC에서 한 번에 이 수치 이상 SAN을 잃으면 광기 조건으로 알립니다.")]
        private int _cocSingleSanityLossThreshold = 5;

        [SerializeField, Range(0.01f, 1f), Tooltip(
            "CoC에서 기간 시작 SAN 대비 이 비율 이상 잃으면 장기 광기 조건으로 알립니다.")]
        private float _cocPeriodSanityLossRatio = 0.2f;

        [Header("Movement")]
        [FormerlySerializedAs("_defaultMoveMeters")]
        [SerializeField, HideInInspector]
        private float _legacyDefaultMoveMeters;

        [FormerlySerializedAs("_defaultMoveMetersPerTurn")]
        [SerializeField, HideInInspector]
        private int _legacyDefaultMoveMetersPerTurn;

        [SerializeField, Range(10, 100), Tooltip(
            "Definition에 이동 능력치가 없을 때 사용할 기본값")]
        private int _defaultMovementScore = 40;

        [SerializeField, Min(0.01f), Tooltip(
            "이동 능력치 1점이 한 턴에 이동 가능한 미터")]
        private float _metersPerMovementPoint = 0.2f;

        [SerializeField, Min(0.01f), Tooltip(
            "이동 목적지와 사용 이동 거리를 맞출 최소 단위(m)")]
        private float _movementStepMeters = 0.1f;

        [SerializeField, Min(0.01f), Tooltip("5ft 격자 한 칸의 실제 미터 값")]
        private float _gridCellMeters = 1.524f;

        [FormerlySerializedAs("_snapDestinationToGrid")]
        [SerializeField, Tooltip(
            "이동 목적지를 Movement Step Meters 단위에 맞출지 여부")]
        private bool _snapDestinationToMovementStep = true;

        [SerializeField, Min(0f), Tooltip("NavMesh 표면 보정 최대 거리")]
        private float _maxProjectionMeters = 0.3f;

        [SerializeField, Min(0f), Tooltip("Door 도착 직후 역이동을 막는 시간")]
        private float _doorGuardSeconds = 0.2f;

        [Header("Info Bar")]
        [SerializeField, Tooltip("런타임 생성 정보 바 배경색")]
        private Color _infoBarColor = new Color(0.055f, 0.07f, 0.09f, 0.96f);

        [SerializeField, Tooltip("Canvas Scaler 기준 해상도")]
        private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [SerializeField, Min(80f), Tooltip("정보 바 높이")]
        private float _infoBarHeight = 150f;

        [SerializeField, Min(0f), Tooltip("화면 좌우 여백")]
        private float _infoBarHorizontalMargin = 32f;

        [SerializeField, Min(0f), Tooltip("정보 바의 화면 하단 여백")]
        private float _infoBarBottomMargin = 24f;

        [SerializeField, Min(32f), Tooltip("Portrait 정사각형 크기")]
        private float _portraitSize = 110f;

        [SerializeField, Range(10, 64), Tooltip("이름 텍스트 크기")]
        private int _nameFontSize = 28;

        [SerializeField, Range(10, 64), Tooltip("설명 텍스트 크기")]
        private int _descriptionFontSize = 20;

        [SerializeField, Min(0.01f), Tooltip("정보 바가 올라오는 시간")]
        private float _showDuration = 0.22f;

        [SerializeField, Min(0.01f), Tooltip("정보 바가 내려가는 시간")]
        private float _hideDuration = 0.16f;

        public string Id => _id;
        public CampaignRuleSet RuleSet => _campaignRuleSet;
        public bool UsesCallOfCthulhuRules =>
            _campaignRuleSet == CampaignRuleSet.CallOfCthulhu7E;
        public int CocSingleSanityLossThreshold =>
            Mathf.Max(1, _cocSingleSanityLossThreshold);
        public float CocPeriodSanityLossRatio =>
            Mathf.Clamp(_cocPeriodSanityLossRatio, 0.01f, 1f);
        public int DefaultMovementScore
        {
            get
            {
                var legacyMeters = ResolveLegacyDefaultMeters();
                return legacyMeters > 0f
                    ? Mathf.Clamp(
                        Mathf.RoundToInt(
                            legacyMeters / MetersPerMovementPoint),
                        10,
                        100)
                    : Mathf.Clamp(_defaultMovementScore, 10, 100);
            }
        }
        public float MetersPerMovementPoint =>
            Mathf.Max(0.01f, _metersPerMovementPoint);
        public float MovementStepMeters =>
            Mathf.Max(0.01f, _movementStepMeters);
        public float GridCellMeters => _gridCellMeters;
        public bool SnapDestinationToMovementStep =>
            _snapDestinationToMovementStep;
        public float MaxProjectionMeters => _maxProjectionMeters;
        public float DoorGuardSeconds => _doorGuardSeconds;
        public Color InfoBarColor => _infoBarColor;
        public Vector2 ReferenceResolution => _referenceResolution;
        public float InfoBarHeight => _infoBarHeight;
        public float InfoBarHorizontalMargin => _infoBarHorizontalMargin;
        public float InfoBarBottomMargin => _infoBarBottomMargin;
        public float PortraitSize => _portraitSize;
        public int NameFontSize => _nameFontSize;
        public int DescriptionFontSize => _descriptionFontSize;
        public float ShowDuration => _showDuration;
        public float HideDuration => _hideDuration;

        public float GetTurnMoveMeters(int movementScore)
        {
            return Mathf.Clamp(movementScore, 10, 100) *
                   MetersPerMovementPoint;
        }

        private float ResolveLegacyDefaultMeters()
        {
            if (_legacyDefaultMoveMeters > 0f)
            {
                return _legacyDefaultMoveMeters;
            }

            return _legacyDefaultMoveMetersPerTurn > 0
                ? _legacyDefaultMoveMetersPerTurn
                : 0f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var legacyMeters = ResolveLegacyDefaultMeters();
            if (legacyMeters > 0f)
            {
                _defaultMovementScore = Mathf.Clamp(
                    Mathf.RoundToInt(
                        legacyMeters / MetersPerMovementPoint),
                    10,
                    100);
                _legacyDefaultMoveMeters = 0f;
                _legacyDefaultMoveMetersPerTurn = 0;
            }

            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError($"[{name}] Settings Id가 비어 있습니다.", this);
            }
        }
#endif
    }
}
