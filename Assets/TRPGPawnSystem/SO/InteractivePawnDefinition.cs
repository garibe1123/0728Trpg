using System;
using System.Collections.Generic;
using Trpg.Data.Skills;
using Trpg.Data.Stats;
using Trpg.Domain.Stats;
using UnityEngine;
using UnityEngine.Serialization;

namespace Trpg.Pawns
{
    [Serializable]
    public sealed class PawnBaseStatRecord
    {
        [SerializeField, Tooltip("룰 템플릿에서 사용하는 스탯 ID")]
        private string _statId;

        [SerializeField] private float _value;

        public string StatId => _statId;
        public float Value => _value;

        public PawnBaseStatRecord(string statId, float value)
        {
            _statId = statId;
            _value = value;
        }
    }

    [Serializable]
    public sealed class PawnSkillRecord
    {
        [SerializeField, Tooltip("기술 이름과 미훈련 기본치를 정의하는 Skill SO")]
        private SkillDefinition _definition;

        [SerializeField, Tooltip(
            "끄면 Skill SO의 미훈련 기본치를 사용하고, 켜면 아래 보통 성공값을 사용")]
        private bool _overrideBaseValue = true;

        [SerializeField, Min(0), Tooltip(
            "이 캐릭터의 보통 성공 기준값. 어려움과 극단적 성공값은 자동 계산")]
        private int _regularValue;

        public SkillDefinition Definition => _definition;
        public bool UsesBaseValue => !_overrideBaseValue;
        public int RegularValue =>
            _overrideBaseValue
                ? Mathf.Max(0, _regularValue)
                : _definition != null
                    ? _definition.BaseValue
                    : 0;
    }

    [CreateAssetMenu(
        menuName = "Trpg/Pawn/Interactive Pawn Definition",
        fileName = "InteractivePawnDefinition")]
    public sealed class InteractivePawnDefinition :
        ScriptableObject,
        ICharacterStatDefinition
    {
        [SerializeField, Tooltip("콘텐츠 식별 ID")]
        private string _id = "interactive_new";

        [SerializeField, Tooltip("이 Pawn의 상호작용 종류")]
        private InteractivePawnKind _kind = InteractivePawnKind.Npc;

        [SerializeField, HideInInspector, Tooltip(
            "이전 비플레이어 Moveable SO 직렬화 호환용 내부 값")]
        private MoveablePawnKind _moveableKind = MoveablePawnKind.Player;

        [SerializeField, Tooltip("NPC 이동 가능 여부")]
        private NpcMovementMode _npcMovementMode =
            NpcMovementMode.Fixed;

        [Header("Info Bar")]
        [SerializeField, Tooltip("하단 정보 바에 표시할 이름")]
        private string _displayName;

        [SerializeField, TextArea(2, 5), Tooltip("하단 정보 바와 공개 인물 설명에 표시할 설명")]
        private string _description;

        [SerializeField, TextArea(4, 12), Tooltip(
            "GM에게만 표시하는 운용 지침. 예: 이 캐릭터는 ~를 갖고 있으며, " +
            "특정 조건에서 어떤 결과를 제공합니다.")]
        private string _gmInstructions;

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
            "캐릭터 스탯을 사용할 수 없을 때의 이동 능력치")]
        private int _movementScore = 40;

        [Header("Stats")]
        [SerializeField, Tooltip(
            "비워두면 내장 CoC 7판 규칙을 사용합니다. 다른 룰북을 쓸 때만 연결하십시오.")]
        private StatRuleTemplate _statRuleTemplate;

        [SerializeField, Tooltip(
            "이 Pawn의 기본 스탯 값입니다. 기본 구성은 CoC 7판입니다.")]
        private List<PawnBaseStatRecord> _baseStats =
            CreateDefaultCocStats();

        [SerializeField, Min(0.01f), Tooltip(
            "룰 템플릿의 Movement 값을 기존 MovementScore로 바꾸는 배율. " +
            "CoC MOV 8을 MovementScore 40으로 쓰려면 5")]
        private float _movementStatToScoreMultiplier = 5f;

        [Header("Skills")]
        [SerializeField, Tooltip(
            "런타임 스킬 추가 UI에서 사용할 전체 스킬 카탈로그")]
        private SkillCatalogDefinition _skillCatalog;

        [SerializeField, Tooltip(
            "이 캐릭터의 스킬 목록. 기술별 보통 성공값만 저장하고 어려움/극단은 자동 계산합니다.")]
        private List<PawnSkillRecord> _skills =
            new List<PawnSkillRecord>();

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

        [Header("Visual")]
        [SerializeField, Tooltip(
            "Legacy는 기존 SpriteRenderer, Modular Character는 파츠 조립, " +
            "Simple Sprite는 단일 Sprite와 Portrait만 사용합니다.")]
        private PawnVisualMode _visualMode = PawnVisualMode.Legacy;

        [FormerlySerializedAs("_useModularSpriteMotion")]
        [FormerlySerializedAs("_usePawnSpriteAnimator")]
        [SerializeField, HideInInspector]
        private bool _legacyUseModularSpriteMotion;

        [SerializeField, HideInInspector]
        private bool _visualModeMigrated;

        [SerializeField, Tooltip(
            "Simple Sprite 모드에서 사용할 단일 월드 Sprite와 Portrait SO")]
        private SimplePawnVisualDefinition _simpleVisual;

        [SerializeField, Tooltip(
            "Modular Character 모드에서 사용할 기본 파츠 및 팔레트 구성")]
        private PawnAppearance _defaultAppearance = PawnAppearance.Default;

        private readonly List<StatBaseValue> _baseStatCache =
            new List<StatBaseValue>();

        public string Id => _id;
        public InteractivePawnKind Kind => _kind;
        public MoveablePawnKind MoveableKind => _moveableKind;
        public NpcMovementMode NpcMovement => ResolveNpcMovementMode(
            _kind,
            _moveableKind,
            _npcMovementMode);
        public InteractivePawnRole Role => ResolveRole(
            _kind,
            _moveableKind);
        public bool IsPlayer => Role == InteractivePawnRole.Player;
        public bool IsNpc => Role == InteractivePawnRole.Npc;
        public bool IsDoor => Role == InteractivePawnRole.Door;
        public bool SupportsFullCharacterSheet => IsPlayer;
        public bool SupportsStats => IsPlayer || IsNpc;
        public bool SupportsSkills => IsPlayer;
        public bool SupportsInventory => IsPlayer;
        public bool SupportsProfile => IsPlayer;
        public bool SupportsRolls => SupportsStats;
        public bool SupportsCocStatReroll => SupportsStats;
        public bool ShowsInformationOnly => IsNpc;
        public bool HasIdentityDetail => !IsDoor;
        public bool CanMove =>
            IsPlayer ||
            (IsNpc && NpcMovement == NpcMovementMode.Walkable);
        public string DisplayName => _displayName;
        public string Description => _description;
        public string GmInstructions => _gmInstructions;
        public PawnVisualMode VisualMode => ResolveVisualMode();
        public SimplePawnVisualDefinition SimpleVisual => _simpleVisual;
        public Sprite SimpleWorldSprite =>
            VisualMode == PawnVisualMode.SimpleSprite && _simpleVisual != null
                ? _simpleVisual.WorldSprite
                : null;
        public Sprite Portrait =>
            VisualMode == PawnVisualMode.SimpleSprite &&
            _simpleVisual != null &&
            _simpleVisual.Portrait != null
                ? _simpleVisual.Portrait
                : _portrait;
        public StatRuleTemplate StatRuleTemplateAsset => _statRuleTemplate;
        public IStatRuleTemplate EffectiveStatRuleTemplate =>
            _statRuleTemplate != null
                ? _statRuleTemplate
                : StatRuleTemplateDefaults.Coc7;
        public bool UsesBuiltInCocRules => _statRuleTemplate == null;
        public SkillCatalogDefinition SkillCatalog => _skillCatalog;
        public IReadOnlyList<PawnSkillRecord> Skills => _skills;
        IStatRuleTemplate ICharacterStatDefinition.RuleTemplate =>
            EffectiveStatRuleTemplate;
        public IReadOnlyList<StatBaseValue> BaseValues
        {
            get
            {
                _baseStatCache.Clear();
                if (!SupportsStats || _baseStats == null)
                    return _baseStatCache;

                for (var index = 0; index < _baseStats.Count; index++)
                {
                    var value = _baseStats[index];
                    if (value != null)
                    {
                        _baseStatCache.Add(
                            new StatBaseValue(
                                value.StatId,
                                value.Value));
                    }
                }

                return _baseStatCache;
            }
        }
        public float MovementStatToScoreMultiplier =>
            Mathf.Max(0.01f, _movementStatToScoreMultiplier);
        public int MovementScore
        {
            get
            {
                var fallback = ResolveFallbackMovementScore();
                if (!SupportsStats)
                    return fallback;

                try
                {
                    var runtime = new StatRuntimeState(this);
                    return ResolveMovementScore(runtime, fallback);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    return fallback;
                }
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
        public bool UseModularSpriteMotion =>
            VisualMode == PawnVisualMode.ModularCharacter;
        public bool UseSimpleSpriteVisual =>
            VisualMode == PawnVisualMode.SimpleSprite;
        public PawnAppearance DefaultAppearance =>
            _defaultAppearance.WithVisibleColorDefaults();

        private PawnVisualMode ResolveVisualMode()
        {
            if (IsDoor)
                return PawnVisualMode.Legacy;

            if (!_visualModeMigrated && _legacyUseModularSpriteMotion)
                return PawnVisualMode.ModularCharacter;

            return _visualMode;
        }

        public bool TryGetDefaultStatValue(
            StatRole role,
            out double value)
        {
            value = 0d;
            if (!SupportsStats)
                return false;

            try
            {
                var runtime = new StatRuntimeState(this);
                var statId = runtime.Template.GetStatId(role);
                if (string.IsNullOrWhiteSpace(statId) ||
                    !runtime.TryGetDefinition(statId, out _))
                {
                    return false;
                }

                value = runtime.GetNumber(statId);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public static InteractivePawnRole ResolveRole(
            InteractivePawnKind kind,
            MoveablePawnKind moveableKind)
        {
            if (kind == InteractivePawnKind.Door)
                return InteractivePawnRole.Door;

            if (kind == InteractivePawnKind.Moveable &&
                moveableKind == MoveablePawnKind.Player)
            {
                return InteractivePawnRole.Player;
            }

            return InteractivePawnRole.Npc;
        }

        public static NpcMovementMode ResolveNpcMovementMode(
            InteractivePawnKind kind,
            MoveablePawnKind moveableKind,
            NpcMovementMode npcMovementMode)
        {
            if (kind == InteractivePawnKind.Moveable &&
                moveableKind == MoveablePawnKind.LegacyWalkableNpc)
            {
                return NpcMovementMode.Walkable;
            }

            return kind == InteractivePawnKind.Npc
                ? npcMovementMode
                : NpcMovementMode.Fixed;
        }

        public int ResolveDexterity(int fallback = 50)
        {
            return TryGetDefaultStatValue(
                    StatRole.Dexterity,
                    out var dexterity)
                ? Mathf.Clamp(
                    Mathf.RoundToInt((float)dexterity),
                    0,
                    9999)
                : Mathf.Max(0, fallback);
        }

        public int ResolveInitiative(int fallback = 50)
        {
            return TryGetDefaultStatValue(
                    StatRole.Initiative,
                    out var initiative)
                ? Mathf.Clamp(
                    Mathf.RoundToInt((float)initiative),
                    0,
                    9999)
                : Mathf.Max(0, fallback);
        }

        public int ResolveMovementScore(
            IStatValueProvider statProvider,
            int fallback = -1)
        {
            var resolvedFallback = fallback >= 0
                ? Mathf.Clamp(fallback, 10, 100)
                : ResolveFallbackMovementScore();

            if (!SupportsStats ||
                statProvider == null ||
                !statProvider.TryGetRoleNumber(
                    StatRole.Movement,
                    out var movement))
            {
                return resolvedFallback;
            }

            return Mathf.Clamp(
                Mathf.RoundToInt(
                    (float)movement *
                    MovementStatToScoreMultiplier),
                10,
                100);
        }

        private int ResolveMovementScore(
            StatRuntimeState runtime,
            int fallback)
        {
            var movementStatId =
                runtime.Template.GetStatId(StatRole.Movement);
            if (string.IsNullOrWhiteSpace(movementStatId) ||
                !runtime.TryGetDefinition(
                    movementStatId,
                    out _))
            {
                return fallback;
            }

            return Mathf.Clamp(
                Mathf.RoundToInt(
                    (float)runtime.GetNumber(movementStatId) *
                    MovementStatToScoreMultiplier),
                10,
                100);
        }

        private int ResolveFallbackMovementScore()
        {
            var legacyMeters = ResolveLegacyMoveMeters();
            return legacyMeters > 0f
                ? Mathf.Clamp(
                    Mathf.RoundToInt(legacyMeters / 0.2f),
                    10,
                    100)
                : Mathf.Clamp(_movementScore, 10, 100);
        }

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

        [ContextMenu("Reset Stats To Built-In CoC 7th")]
        public void ResetStatsToBuiltInCoc()
        {
            _statRuleTemplate = null;
            _baseStats = CreateDefaultCocStats();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public static List<PawnBaseStatRecord> CreateDefaultCocStats()
        {
            return new List<PawnBaseStatRecord>
            {
                new PawnBaseStatRecord("coc.str", 50),
                new PawnBaseStatRecord("coc.con", 50),
                new PawnBaseStatRecord("coc.siz", 50),
                new PawnBaseStatRecord("coc.dex", 50),
                new PawnBaseStatRecord("coc.app", 50),
                new PawnBaseStatRecord("coc.int", 50),
                new PawnBaseStatRecord("coc.pow", 50),
                new PawnBaseStatRecord("coc.edu", 50),
                new PawnBaseStatRecord("coc.luck", 50),
                new PawnBaseStatRecord("coc.cthulhu_mythos", 0)
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!_visualModeMigrated)
            {
                if (_legacyUseModularSpriteMotion)
                    _visualMode = PawnVisualMode.ModularCharacter;
                _legacyUseModularSpriteMotion = false;
                _visualModeMigrated = true;
            }

            if (_kind == InteractivePawnKind.Door)
                _visualMode = PawnVisualMode.Legacy;

            _defaultAppearance =
                _defaultAppearance.WithVisibleColorDefaults();

            if (_kind == InteractivePawnKind.Moveable &&
                _moveableKind == MoveablePawnKind.LegacyWalkableNpc)
            {
                _kind = InteractivePawnKind.Npc;
                _npcMovementMode = NpcMovementMode.Walkable;
            }

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

            if (!SupportsStats)
                return;

            if (_baseStats == null)
            {
                _baseStats = CreateDefaultCocStats();
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < _baseStats.Count; index++)
            {
                var value = _baseStats[index];
                if (value == null ||
                    string.IsNullOrWhiteSpace(value.StatId))
                {
                    Debug.LogError(
                        $"[{name}] {index}번 기본 스탯 ID가 비어 있습니다.",
                        this);
                    continue;
                }

                if (!ids.Add(value.StatId))
                {
                    Debug.LogError(
                        $"[{name}] 중복 기본 스탯 ID: {value.StatId}",
                        this);
                }
            }

            if (!SupportsSkills)
                return;

            if (_skills == null)
            {
                _skills = new List<PawnSkillRecord>();
                return;
            }

            var skillIds =
                new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < _skills.Count; index++)
            {
                var record = _skills[index];
                if (record == null || record.Definition == null)
                {
                    Debug.LogError(
                        $"[{name}] {index}번 Skill Definition이 비어 있습니다.",
                        this);
                    continue;
                }

                var skillId = record.Definition.Id;
                if (string.IsNullOrWhiteSpace(skillId))
                    continue;

                if (!skillIds.Add(skillId))
                {
                    Debug.LogError(
                        $"[{name}] 중복 Skill ID: {skillId}",
                        this);
                }
            }
        }
#endif
    }
}
