using System;
using System.Text;
using Trpg.Data.Coc;
using Trpg.Domain.Dice;
using UnityEngine;

namespace Trpg.Pawns
{
    public enum CoCStat
    {
        Strength,
        Constitution,
        Size,
        Dexterity,
        Appearance,
        Intelligence,
        Power,
        Education,
        Luck
    }

    public enum CoCCheckGrade
    {
        Invalid,
        Fumble,
        Failure,
        Success,
        HardSuccess,
        ExtremeSuccess,
        CriticalSuccess
    }

    [Serializable]
    public sealed class CoCCharacterSaveData
    {
        public string characterDefinitionId;
        public int currentHp;
        public int currentMp;
        public int currentSan;
        public int currentLuck;
    }

    [DisallowMultipleComponent]
    public sealed class CoCCharacterSheet : MonoBehaviour
    {
        private const int MovementScoreMultiplier = 5;

        [SerializeField, Tooltip("이 Pawn이 사용할 CoC 캐릭터 스탯 에셋")]
        private CoCCharacterDefinition _definition;

        [Header("Runtime - 게임 중 자동 관리")]
        [SerializeField] private int _currentHp;
        [SerializeField] private int _currentMp;
        [SerializeField] private int _currentSan;
        [SerializeField] private int _currentLuck;
        [SerializeField, HideInInspector] private bool _initialized;

        public event Action Changed;

        public CoCCharacterDefinition Definition => _definition;
        public bool IsInitialized => _initialized && _definition != null;

        public int MaxHp =>
            _definition == null
                ? 0
                : Mathf.FloorToInt(
                    (_definition.Constitution + _definition.Size) / 10f);

        public int MaxMp =>
            _definition == null
                ? 0
                : Mathf.FloorToInt(_definition.Power / 5f);

        public int StartingSan => _definition == null ? 0 : _definition.Power;
        public int StartingLuck => _definition == null ? 0 : _definition.Luck;

        public int CurrentHp => _currentHp;
        public int CurrentMp => _currentMp;
        public int CurrentSan => _currentSan;
        public int CurrentLuck => _currentLuck;

        public int Move => CalculateMove();
        public int MovementScore => Move * MovementScoreMultiplier;
        public int Build => CalculateBuild();
        public string DamageBonus => CalculateDamageBonus();
        public int MajorWoundThreshold =>
            Mathf.Max(1, Mathf.CeilToInt(MaxHp / 2f));

        private void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (_initialized)
            {
                ClampRuntimeValues();
                return;
            }

            if (_definition == null)
            {
                Debug.LogError(
                    $"[{name}] CoC Character Definition이 지정되지 않았습니다.",
                    this);
                return;
            }

            _currentHp = MaxHp;
            _currentMp = MaxMp;
            _currentSan = StartingSan;
            _currentLuck = StartingLuck;
            _initialized = true;
            Changed?.Invoke();
        }

        public bool Configure(CoCCharacterDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            _definition = definition;
            _initialized = false;
            Initialize();
            return true;
        }

        public int GetStat(CoCStat stat)
        {
            if (_definition == null)
            {
                return 0;
            }

            switch (stat)
            {
                case CoCStat.Strength:
                    return _definition.Strength;
                case CoCStat.Constitution:
                    return _definition.Constitution;
                case CoCStat.Size:
                    return _definition.Size;
                case CoCStat.Dexterity:
                    return _definition.Dexterity;
                case CoCStat.Appearance:
                    return _definition.Appearance;
                case CoCStat.Intelligence:
                    return _definition.Intelligence;
                case CoCStat.Power:
                    return _definition.Power;
                case CoCStat.Education:
                    return _definition.Education;
                case CoCStat.Luck:
                    return _initialized
                        ? _currentLuck
                        : _definition.Luck;
                default:
                    return 0;
            }
        }

        public int GetHardValue(CoCStat stat)
        {
            return GetStat(stat) / 2;
        }

        public int GetExtremeValue(CoCStat stat)
        {
            return GetStat(stat) / 5;
        }

        public CoCCheckGrade EvaluateCheck(CoCStat stat, int roll)
        {
            return ConvertOutcome(
                CoCCheckRules.Evaluate(GetStat(stat), roll));
        }

        public void AdjustHp(int amount)
        {
            SetHp(_currentHp + amount);
        }

        public void AdjustMp(int amount)
        {
            SetMp(_currentMp + amount);
        }

        public void AdjustSan(int amount)
        {
            SetSan(_currentSan + amount);
        }

        public void AdjustLuck(int amount)
        {
            SetLuck(_currentLuck + amount);
        }

        public void SetHp(int value)
        {
            SetRuntimeValue(ref _currentHp, Mathf.Clamp(value, 0, MaxHp));
        }

        public void SetMp(int value)
        {
            SetRuntimeValue(ref _currentMp, Mathf.Clamp(value, 0, MaxMp));
        }

        public void SetSan(int value)
        {
            SetRuntimeValue(ref _currentSan, Mathf.Clamp(value, 0, 99));
        }

        public void SetLuck(int value)
        {
            SetRuntimeValue(ref _currentLuck, Mathf.Clamp(value, 0, 99));
        }

        public CoCCharacterSaveData CreateSaveData()
        {
            return new CoCCharacterSaveData
            {
                characterDefinitionId =
                    _definition == null ? string.Empty : _definition.Id,
                currentHp = _currentHp,
                currentMp = _currentMp,
                currentSan = _currentSan,
                currentLuck = _currentLuck
            };
        }

        public bool Restore(CoCCharacterSaveData saveData)
        {
            if (saveData == null || _definition == null)
            {
                return false;
            }

            if (!string.Equals(
                    saveData.characterDefinitionId,
                    _definition.Id,
                    StringComparison.Ordinal))
            {
                Debug.LogError(
                    $"[{name}] 저장 데이터의 캐릭터 ID가 일치하지 않습니다.",
                    this);
                return false;
            }

            _currentHp = saveData.currentHp;
            _currentMp = saveData.currentMp;
            _currentSan = saveData.currentSan;
            _currentLuck = saveData.currentLuck;
            _initialized = true;
            ClampRuntimeValues();
            Changed?.Invoke();
            return true;
        }

        public string GetStatTooltip(CoCStat stat)
        {
            var value = GetStat(stat);
            var builder = new StringBuilder();

            builder.Append(GetStatLabel(stat))
                .Append(": ")
                .Append(value)
                .Append("\n어려운 성공: ")
                .Append(value / 2)
                .Append("\n극단적 성공: ")
                .Append(value / 5);

            switch (stat)
            {
                case CoCStat.Strength:
                    builder.Append("\n영향: MOV, Build, DB");
                    break;
                case CoCStat.Constitution:
                    builder.Append("\n영향: 최대 HP");
                    break;
                case CoCStat.Size:
                    builder.Append("\n영향: 최대 HP, MOV, Build, DB");
                    break;
                case CoCStat.Dexterity:
                    builder.Append("\n영향: MOV");
                    break;
                case CoCStat.Power:
                    builder.Append("\n영향: 최대 MP, 시작 SAN");
                    break;
                case CoCStat.Luck:
                    builder.Append("\n현재값: ").Append(_currentLuck);
                    break;
            }

            return builder.ToString();
        }

        private int CalculateMove()
        {
            if (_definition == null)
            {
                return 0;
            }

            int move;
            if (_definition.Strength < _definition.Size &&
                _definition.Dexterity < _definition.Size)
            {
                move = 7;
            }
            else if (_definition.Strength > _definition.Size &&
                     _definition.Dexterity > _definition.Size)
            {
                move = 9;
            }
            else
            {
                move = 8;
            }

            return Mathf.Max(1, move - GetAgeMovePenalty(_definition.Age));
        }

        private int CalculateBuild()
        {
            var sum = GetStrengthAndSizeSum();

            if (sum <= 64) return -2;
            if (sum <= 84) return -1;
            if (sum <= 124) return 0;
            if (sum <= 164) return 1;
            if (sum <= 204) return 2;

            return 3 + ((sum - 205) / 80);
        }

        private string CalculateDamageBonus()
        {
            var sum = GetStrengthAndSizeSum();

            if (sum <= 64) return "-2";
            if (sum <= 84) return "-1";
            if (sum <= 124) return "0";
            if (sum <= 164) return "+1D4";
            if (sum <= 204) return "+1D6";

            var diceCount = 2 + ((sum - 205) / 80);
            return $"+{diceCount}D6";
        }

        private int GetStrengthAndSizeSum()
        {
            return _definition == null
                ? 0
                : _definition.Strength + _definition.Size;
        }

        private static int GetAgeMovePenalty(int age)
        {
            if (age < 40) return 0;
            if (age < 50) return 1;
            if (age < 60) return 2;
            if (age < 70) return 3;
            if (age < 80) return 4;
            return 5;
        }

        private static string GetStatLabel(CoCStat stat)
        {
            switch (stat)
            {
                case CoCStat.Strength:
                    return "근력 STR";
                case CoCStat.Constitution:
                    return "건강 CON";
                case CoCStat.Size:
                    return "크기 SIZ";
                case CoCStat.Dexterity:
                    return "민첩 DEX";
                case CoCStat.Appearance:
                    return "외모 APP";
                case CoCStat.Intelligence:
                    return "지능 INT";
                case CoCStat.Power:
                    return "정신력 POW";
                case CoCStat.Education:
                    return "교육 EDU";
                case CoCStat.Luck:
                    return "운 LUCK";
                default:
                    return stat.ToString();
            }
        }

        private static CoCCheckGrade ConvertOutcome(
            CoCCheckOutcome outcome)
        {
            switch (outcome)
            {
                case CoCCheckOutcome.CriticalSuccess:
                    return CoCCheckGrade.CriticalSuccess;
                case CoCCheckOutcome.ExtremeSuccess:
                    return CoCCheckGrade.ExtremeSuccess;
                case CoCCheckOutcome.HardSuccess:
                    return CoCCheckGrade.HardSuccess;
                case CoCCheckOutcome.Success:
                    return CoCCheckGrade.Success;
                case CoCCheckOutcome.Failure:
                    return CoCCheckGrade.Failure;
                case CoCCheckOutcome.Fumble:
                    return CoCCheckGrade.Fumble;
                default:
                    return CoCCheckGrade.Invalid;
            }
        }

        private void ClampRuntimeValues()
        {
            _currentHp = Mathf.Clamp(_currentHp, 0, MaxHp);
            _currentMp = Mathf.Clamp(_currentMp, 0, MaxMp);
            _currentSan = Mathf.Clamp(_currentSan, 0, 99);
            _currentLuck = Mathf.Clamp(_currentLuck, 0, 99);
        }

        private void SetRuntimeValue(ref int target, int value)
        {
            if (target == value)
            {
                return;
            }

            target = value;
            Changed?.Invoke();
        }
    }
}
