using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Trpg.Domain.Stats
{
    public enum StatValueSource
    {
        Base,
        Runtime,
        Formula,
        LookupTable
    }

    public enum StatDisplayKind
    {
        Number,
        CurrentAndMax,
        Dice,
        Distance
    }

    public enum StatVisibility
    {
        Self,
        Party,
        Everyone,
        GameMasterOnly
    }

    public enum StatRole
    {
        None,
        HealthCurrent,
        HealthMax,
        MagicCurrent,
        MagicMax,
        SanityCurrent,
        SanityMax,
        Movement,
        MeleeAttack,
        Defense,
        Initiative,
        LuckCurrent,
        LuckMax,
        Dexterity
    }

    public interface IStatLookupBand
    {
        string Condition { get; }
        double NumericValue { get; }
        string DisplayText { get; }
    }

    public interface IStatDefinition
    {
        string Id { get; }
        string DisplayName { get; }
        string Category { get; }
        StatValueSource Source { get; }
        StatDisplayKind DisplayKind { get; }
        StatVisibility Visibility { get; }
        int SortOrder { get; }
        bool ShowInSummary { get; }
        bool IsAdjustable { get; }
        double DefaultValue { get; }
        double MinValue { get; }
        double MaxValue { get; }
        double AdjustStep { get; }
        string Formula { get; }
        string InitialValueFormula { get; }
        string MaxStatId { get; }
        IReadOnlyList<IStatLookupBand> LookupBands { get; }
    }

    public interface IStatRuleTemplate
    {
        string Id { get; }
        string DisplayName { get; }
        int Version { get; }
        IReadOnlyList<IStatDefinition> Stats { get; }
        string GetStatId(StatRole role);
    }

    public interface ICharacterStatDefinition
    {
        string Id { get; }
        IStatRuleTemplate RuleTemplate { get; }
        IReadOnlyList<StatBaseValue> BaseValues { get; }
    }

    public interface IStatValueProvider
    {
        bool TryGetNumber(string statId, out double value);
        bool TryGetRoleNumber(StatRole role, out double value);
    }

    [Serializable]
    public readonly struct StatBaseValue
    {
        public readonly string StatId;
        public readonly double Value;

        public StatBaseValue(string statId, double value)
        {
            StatId = statId;
            Value = value;
        }
    }

    [Serializable]
    public sealed class StatRuntimeSnapshot
    {
        public string CharacterDefinitionId;
        public string RuleTemplateId;
        public int RuleTemplateVersion;
        public List<StatStoredValue> RuntimeValues = new List<StatStoredValue>();
        public List<StatStoredModifier> Modifiers = new List<StatStoredModifier>();
    }

    [Serializable]
    public sealed class StatStoredValue
    {
        public string StatId;
        public double Value;

        public StatStoredValue()
        {
        }

        public StatStoredValue(string statId, double value)
        {
            StatId = statId;
            Value = value;
        }
    }

    [Serializable]
    public sealed class StatStoredModifier
    {
        public string StatId;
        public string SourceId;
        public double Amount;

        public StatStoredModifier()
        {
        }

        public StatStoredModifier(string statId, string sourceId, double amount)
        {
            StatId = statId;
            SourceId = sourceId;
            Amount = amount;
        }
    }

    public readonly struct StatValue
    {
        public readonly double Number;
        public readonly string DisplayOverride;

        public StatValue(double number, string displayOverride = null)
        {
            Number = number;
            DisplayOverride = displayOverride;
        }
    }
}
namespace Trpg.Domain.Stats
{
    public sealed class StatFormulaCalculator
    {
        public double Evaluate(string expression, Func<string, double> resolveIdentifier)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("계산식이 비어 있습니다.", nameof(expression));
            if (resolveIdentifier == null)
                throw new ArgumentNullException(nameof(resolveIdentifier));

            var parser = new Parser(expression, resolveIdentifier);
            var result = parser.ParseExpression();
            parser.RequireEnd();
            return result;
        }

        public IReadOnlyList<string> ExtractIdentifiers(string expression)
        {
            var identifiers = new List<string>();
            if (string.IsNullOrWhiteSpace(expression))
                return identifiers;

            var lexer = new Lexer(expression);
            Token current;
            Token next = lexer.Next();

            do
            {
                current = next;
                next = lexer.Next();

                if (current.Kind != TokenKind.Identifier || next.Kind == TokenKind.LeftParenthesis)
                    continue;
                if (!identifiers.Contains(current.Text))
                    identifiers.Add(current.Text);
            }
            while (current.Kind != TokenKind.End);

            return identifiers;
        }

        private enum TokenKind
        {
            Number,
            Identifier,
            Plus,
            Minus,
            Multiply,
            Divide,
            LeftParenthesis,
            RightParenthesis,
            Comma,
            Less,
            LessOrEqual,
            Greater,
            GreaterOrEqual,
            Equal,
            NotEqual,
            And,
            Or,
            Not,
            End
        }

        private readonly struct Token
        {
            public readonly TokenKind Kind;
            public readonly string Text;
            public readonly double Number;

            public Token(TokenKind kind, string text, double number = 0d)
            {
                Kind = kind;
                Text = text;
                Number = number;
            }
        }

        private sealed class Lexer
        {
            private readonly string _source;
            private int _index;

            public Lexer(string source)
            {
                _source = source;
            }

            public Token Next()
            {
                SkipWhiteSpace();
                if (_index >= _source.Length)
                    return new Token(TokenKind.End, string.Empty);

                var c = _source[_index];
                if (char.IsDigit(c) || c == '.')
                    return ReadNumber();
                if (char.IsLetter(c) || c == '_')
                    return ReadIdentifier();

                _index++;
                switch (c)
                {
                    case '+': return new Token(TokenKind.Plus, "+");
                    case '-': return new Token(TokenKind.Minus, "-");
                    case '*': return new Token(TokenKind.Multiply, "*");
                    case '/': return new Token(TokenKind.Divide, "/");
                    case '(': return new Token(TokenKind.LeftParenthesis, "(");
                    case ')': return new Token(TokenKind.RightParenthesis, ")");
                    case ',': return new Token(TokenKind.Comma, ",");
                    case '<':
                        if (Match('=')) return new Token(TokenKind.LessOrEqual, "<=");
                        return new Token(TokenKind.Less, "<");
                    case '>':
                        if (Match('=')) return new Token(TokenKind.GreaterOrEqual, ">=");
                        return new Token(TokenKind.Greater, ">");
                    case '=':
                        if (Match('=')) return new Token(TokenKind.Equal, "==");
                        break;
                    case '!':
                        if (Match('=')) return new Token(TokenKind.NotEqual, "!=");
                        return new Token(TokenKind.Not, "!");
                    case '&':
                        if (Match('&')) return new Token(TokenKind.And, "&&");
                        break;
                    case '|':
                        if (Match('|')) return new Token(TokenKind.Or, "||");
                        break;
                }

                throw new FormatException($"지원하지 않는 문자입니다: '{c}' ({_index - 1})");
            }

            private Token ReadNumber()
            {
                var start = _index;
                var hasDot = false;
                while (_index < _source.Length)
                {
                    var c = _source[_index];
                    if (c == '.')
                    {
                        if (hasDot)
                            break;
                        hasDot = true;
                        _index++;
                        continue;
                    }
                    if (!char.IsDigit(c))
                        break;
                    _index++;
                }

                var text = _source.Substring(start, _index - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new FormatException($"올바르지 않은 숫자입니다: {text}");
                return new Token(TokenKind.Number, text, value);
            }

            private Token ReadIdentifier()
            {
                var start = _index;
                while (_index < _source.Length)
                {
                    var c = _source[_index];
                    if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                        break;
                    _index++;
                }
                return new Token(TokenKind.Identifier, _source.Substring(start, _index - start));
            }

            private bool Match(char expected)
            {
                if (_index >= _source.Length || _source[_index] != expected)
                    return false;
                _index++;
                return true;
            }

            private void SkipWhiteSpace()
            {
                while (_index < _source.Length && char.IsWhiteSpace(_source[_index]))
                    _index++;
            }
        }

        private sealed class Parser
        {
            private const double BooleanEpsilon = 1e-9d;

            private readonly Lexer _lexer;
            private readonly Func<string, double> _resolveIdentifier;
            private Token _current;

            public Parser(string expression, Func<string, double> resolveIdentifier)
            {
                _lexer = new Lexer(expression);
                _resolveIdentifier = resolveIdentifier;
                _current = _lexer.Next();
            }

            public double ParseExpression()
            {
                return ParseOr();
            }

            public void RequireEnd()
            {
                if (_current.Kind != TokenKind.End)
                    throw new FormatException($"계산식 뒤에 해석할 수 없는 값이 있습니다: {_current.Text}");
            }

            private double ParseOr()
            {
                var value = ParseAnd();
                while (_current.Kind == TokenKind.Or)
                {
                    Read(TokenKind.Or);
                    var right = ParseAnd();
                    value = IsTrue(value) || IsTrue(right) ? 1d : 0d;
                }
                return value;
            }

            private double ParseAnd()
            {
                var value = ParseEquality();
                while (_current.Kind == TokenKind.And)
                {
                    Read(TokenKind.And);
                    var right = ParseEquality();
                    value = IsTrue(value) && IsTrue(right) ? 1d : 0d;
                }
                return value;
            }

            private double ParseEquality()
            {
                var value = ParseComparison();
                while (_current.Kind == TokenKind.Equal || _current.Kind == TokenKind.NotEqual)
                {
                    var kind = _current.Kind;
                    Read(kind);
                    var right = ParseComparison();
                    var equal = Math.Abs(value - right) <= BooleanEpsilon;
                    value = kind == TokenKind.Equal ? (equal ? 1d : 0d) : (equal ? 0d : 1d);
                }
                return value;
            }

            private double ParseComparison()
            {
                var value = ParseAdditive();
                while (_current.Kind == TokenKind.Less ||
                       _current.Kind == TokenKind.LessOrEqual ||
                       _current.Kind == TokenKind.Greater ||
                       _current.Kind == TokenKind.GreaterOrEqual)
                {
                    var kind = _current.Kind;
                    Read(kind);
                    var right = ParseAdditive();
                    switch (kind)
                    {
                        case TokenKind.Less: value = value < right ? 1d : 0d; break;
                        case TokenKind.LessOrEqual: value = value <= right ? 1d : 0d; break;
                        case TokenKind.Greater: value = value > right ? 1d : 0d; break;
                        default: value = value >= right ? 1d : 0d; break;
                    }
                }
                return value;
            }

            private double ParseAdditive()
            {
                var value = ParseMultiplicative();
                while (_current.Kind == TokenKind.Plus || _current.Kind == TokenKind.Minus)
                {
                    var kind = _current.Kind;
                    Read(kind);
                    var right = ParseMultiplicative();
                    value = kind == TokenKind.Plus ? value + right : value - right;
                }
                return value;
            }

            private double ParseMultiplicative()
            {
                var value = ParseUnary();
                while (_current.Kind == TokenKind.Multiply || _current.Kind == TokenKind.Divide)
                {
                    var kind = _current.Kind;
                    Read(kind);
                    var right = ParseUnary();
                    if (kind == TokenKind.Divide && Math.Abs(right) <= BooleanEpsilon)
                        throw new DivideByZeroException("스탯 계산식에서 0으로 나눌 수 없습니다.");
                    value = kind == TokenKind.Multiply ? value * right : value / right;
                }
                return value;
            }

            private double ParseUnary()
            {
                if (_current.Kind == TokenKind.Minus)
                {
                    Read(TokenKind.Minus);
                    return -ParseUnary();
                }
                if (_current.Kind == TokenKind.Plus)
                {
                    Read(TokenKind.Plus);
                    return ParseUnary();
                }
                if (_current.Kind == TokenKind.Not)
                {
                    Read(TokenKind.Not);
                    return IsTrue(ParseUnary()) ? 0d : 1d;
                }
                return ParsePrimary();
            }

            private double ParsePrimary()
            {
                if (_current.Kind == TokenKind.Number)
                {
                    var number = _current.Number;
                    Read(TokenKind.Number);
                    return number;
                }

                if (_current.Kind == TokenKind.Identifier)
                {
                    var identifier = _current.Text;
                    Read(TokenKind.Identifier);
                    if (_current.Kind == TokenKind.LeftParenthesis)
                        return ParseFunction(identifier);
                    return _resolveIdentifier(identifier);
                }

                if (_current.Kind == TokenKind.LeftParenthesis)
                {
                    Read(TokenKind.LeftParenthesis);
                    var value = ParseExpression();
                    Read(TokenKind.RightParenthesis);
                    return value;
                }

                throw new FormatException($"값이 필요한 위치에 '{_current.Text}'가 있습니다.");
            }

            private double ParseFunction(string name)
            {
                Read(TokenKind.LeftParenthesis);
                var arguments = new List<double>();
                if (_current.Kind != TokenKind.RightParenthesis)
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                        if (_current.Kind != TokenKind.Comma)
                            break;
                        Read(TokenKind.Comma);
                    }
                    while (true);
                }
                Read(TokenKind.RightParenthesis);

                switch (name.ToLowerInvariant())
                {
                    case "floor": RequireCount(name, arguments, 1); return Math.Floor(arguments[0]);
                    case "ceil": RequireCount(name, arguments, 1); return Math.Ceiling(arguments[0]);
                    case "round": RequireCount(name, arguments, 1); return Math.Round(arguments[0], MidpointRounding.AwayFromZero);
                    case "abs": RequireCount(name, arguments, 1); return Math.Abs(arguments[0]);
                    case "min": RequireCount(name, arguments, 2); return Math.Min(arguments[0], arguments[1]);
                    case "max": RequireCount(name, arguments, 2); return Math.Max(arguments[0], arguments[1]);
                    case "clamp":
                        RequireCount(name, arguments, 3);
                        return Math.Max(arguments[1], Math.Min(arguments[2], arguments[0]));
                    case "if":
                        RequireCount(name, arguments, 3);
                        return IsTrue(arguments[0]) ? arguments[1] : arguments[2];
                    default:
                        throw new FormatException($"지원하지 않는 함수입니다: {name}");
                }
            }

            private void Read(TokenKind expected)
            {
                if (_current.Kind != expected)
                    throw new FormatException($"'{expected}'가 필요하지만 '{_current.Text}'가 있습니다.");
                _current = _lexer.Next();
            }

            private static bool IsTrue(double value)
            {
                return Math.Abs(value) > BooleanEpsilon;
            }

            private static void RequireCount(string name, List<double> arguments, int expected)
            {
                if (arguments.Count != expected)
                    throw new FormatException($"{name} 함수는 인자 {expected}개가 필요합니다.");
            }
        }
    }
}
namespace Trpg.Domain.Stats
{
    public sealed class StatPresentationService
    {
        private readonly StatRuntimeState _runtime;
        private readonly StatFormulaCalculator _calculator;
        private readonly Dictionary<string, IStatDefinition> _definitions;

        public StatPresentationService(StatRuntimeState runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _calculator = new StatFormulaCalculator();
            _definitions = new Dictionary<string, IStatDefinition>(StringComparer.Ordinal);

            var stats = runtime.Template.Stats;
            for (var i = 0; i < stats.Count; i++)
                _definitions[stats[i].Id] = stats[i];
        }

        public string FormatValue(string statId)
        {
            var definition = GetRequiredDefinition(statId);
            var value = _runtime.GetValue(statId);
            if (!string.IsNullOrWhiteSpace(value.DisplayOverride))
                return value.DisplayOverride;

            var current = FormatNumber(value.Number);
            if (definition.DisplayKind == StatDisplayKind.CurrentAndMax &&
                !string.IsNullOrWhiteSpace(definition.MaxStatId) &&
                _definitions.ContainsKey(definition.MaxStatId))
            {
                return $"{current} / {FormatNumber(_runtime.GetNumber(definition.MaxStatId))}";
            }

            return current;
        }

        public string BuildTooltip(string statId)
        {
            var definition = GetRequiredDefinition(statId);
            var builder = new StringBuilder(256);
            builder.Append(definition.DisplayName).Append("  ").Append(FormatValue(statId));

            AppendOwnCalculation(builder, definition);

            var affected = FindAffectedStats(statId);
            if (affected.Count > 0)
            {
                builder.AppendLine().AppendLine().Append("영향받는 수치");
                for (var i = 0; i < affected.Count; i++)
                {
                    builder.AppendLine();
                    AppendCalculationLine(builder, affected[i]);
                }
            }

            return builder.ToString();
        }

        private List<IStatDefinition> FindAffectedStats(string sourceStatId)
        {
            var result = new List<IStatDefinition>();
            foreach (var pair in _definitions)
            {
                var target = pair.Value;
                if (target.Id != sourceStatId && UsesIdentifier(target, sourceStatId))
                    result.Add(target);
            }

            result.Sort((left, right) => left.SortOrder.CompareTo(right.SortOrder));
            return result;
        }

        private bool UsesIdentifier(IStatDefinition definition, string statId)
        {
            if (ContainsIdentifier(definition.Formula, statId) ||
                ContainsIdentifier(definition.InitialValueFormula, statId))
                return true;

            var bands = definition.LookupBands;
            for (var i = 0; i < bands.Count; i++)
            {
                if (ContainsIdentifier(bands[i].Condition, statId))
                    return true;
            }
            return false;
        }

        private bool ContainsIdentifier(string expression, string statId)
        {
            var identifiers = _calculator.ExtractIdentifiers(expression);
            for (var i = 0; i < identifiers.Count; i++)
            {
                if (string.Equals(identifiers[i], statId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void AppendOwnCalculation(StringBuilder builder, IStatDefinition definition)
        {
            if (definition.Source == StatValueSource.Base)
            {
                var baseValue = _runtime.GetBaseValue(definition.Id);
                var modifier = _runtime.GetModifierTotal(definition.Id);
                builder.AppendLine().AppendLine()
                    .Append("기본값 ").Append(FormatNumber(baseValue));
                if (Math.Abs(modifier) > 1e-9d)
                {
                    builder.Append("  보정 ")
                        .Append(modifier >= 0d ? "+" : string.Empty)
                        .Append(FormatNumber(modifier));
                }
                return;
            }

            if (definition.Source == StatValueSource.Formula ||
                definition.Source == StatValueSource.LookupTable)
            {
                builder.AppendLine().AppendLine("계산 근거");
                AppendCalculationLine(builder, definition);
            }
        }

        private void AppendCalculationLine(StringBuilder builder, IStatDefinition definition)
        {
            if (definition.Source == StatValueSource.Formula)
            {
                builder.Append(definition.DisplayName).Append(": ")
                    .Append(ReplaceIdentifiers(definition.Formula))
                    .Append(" = ").Append(FormatValue(definition.Id));
                return;
            }

            if (definition.Source == StatValueSource.LookupTable)
            {
                var band = FindMatchingBand(definition);
                builder.Append(definition.DisplayName).Append(": ")
                    .Append(ReplaceIdentifiers(band.Condition))
                    .Append(" -> ").Append(FormatValue(definition.Id));
                return;
            }

            builder.Append(definition.DisplayName).Append(": ").Append(FormatValue(definition.Id));
        }

        private IStatLookupBand FindMatchingBand(IStatDefinition definition)
        {
            var bands = definition.LookupBands;
            for (var i = 0; i < bands.Count; i++)
            {
                if (_calculator.Evaluate(bands[i].Condition, _runtime.GetNumber) != 0d)
                    return bands[i];
            }
            throw new InvalidOperationException($"[{definition.Id}] 조건표에서 일치하는 구간이 없습니다.");
        }

        private string ReplaceIdentifiers(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return string.Empty;

            var result = expression;
            var identifiers = _calculator.ExtractIdentifiers(expression);
            var ordered = new List<string>(identifiers);
            ordered.Sort((left, right) => right.Length.CompareTo(left.Length));
            for (var i = 0; i < ordered.Count; i++)
            {
                var id = ordered[i];
                if (!_definitions.TryGetValue(id, out var definition))
                    continue;
                result = result.Replace(
                    id,
                    $"{definition.DisplayName} {FormatNumber(_runtime.GetNumber(id))}");
            }
            return result;
        }

        private IStatDefinition GetRequiredDefinition(string statId)
        {
            if (!_definitions.TryGetValue(statId, out var definition))
                throw new KeyNotFoundException($"등록되지 않은 스탯 ID입니다: {statId}");
            return definition;
        }

        private static string FormatNumber(double number)
        {
            var rounded = Math.Round(number);
            return Math.Abs(number - rounded) <= 1e-9d
                ? rounded.ToString(CultureInfo.InvariantCulture)
                : number.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
namespace Trpg.Domain.Stats
{
    public sealed class StatRuntimeState
    {
        public const string DirectEditModifierSourceId =
            "__gm_direct_edit__";

        private readonly ICharacterStatDefinition _character;
        private readonly IStatRuleTemplate _template;
        private readonly StatFormulaCalculator _calculator;
        private readonly Dictionary<string, IStatDefinition> _definitions;
        private readonly Dictionary<string, double> _baseValues;
        private readonly Dictionary<string, double> _runtimeValues;
        private readonly Dictionary<string, Dictionary<string, double>> _modifiers;
        private readonly HashSet<string> _evaluationStack;

        public event Action Changed;

        public ICharacterStatDefinition Character => _character;
        public IStatRuleTemplate Template => _template;

        public StatRuntimeState(ICharacterStatDefinition character)
        {
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _template = character.RuleTemplate ?? throw new ArgumentException("캐릭터에 룰 템플릿이 없습니다.", nameof(character));
            _calculator = new StatFormulaCalculator();
            _definitions = new Dictionary<string, IStatDefinition>(StringComparer.Ordinal);
            _baseValues = new Dictionary<string, double>(StringComparer.Ordinal);
            _runtimeValues = new Dictionary<string, double>(StringComparer.Ordinal);
            _modifiers = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
            _evaluationStack = new HashSet<string>(StringComparer.Ordinal);

            IndexDefinitions();
            IndexBaseValues();
            InitializeRuntimeValues();
            ValidateAll();
        }

        public bool TryGetDefinition(string statId, out IStatDefinition definition)
        {
            return _definitions.TryGetValue(statId, out definition);
        }

        public StatValue GetValue(string statId)
        {
            if (!_definitions.TryGetValue(statId, out var definition))
                throw new KeyNotFoundException($"등록되지 않은 스탯 ID입니다: {statId}");

            if (!_evaluationStack.Add(statId))
                throw new InvalidOperationException($"스탯 공식에 순환 참조가 있습니다: {statId}");

            try
            {
                return EvaluateDefinition(definition);
            }
            finally
            {
                _evaluationStack.Remove(statId);
            }
        }

        public double GetNumber(string statId)
        {
            return GetValue(statId).Number;
        }

        public bool TryAdjust(string statId, double delta)
        {
            if (!_definitions.TryGetValue(statId, out var definition) ||
                definition.Source != StatValueSource.Runtime ||
                !definition.IsAdjustable)
                return false;

            var current = _runtimeValues[statId];
            _runtimeValues[statId] = Clamp(definition, current + delta);
            Changed?.Invoke();
            return true;
        }

        public bool TrySetRuntimeValue(string statId, double value)
        {
            if (!_definitions.TryGetValue(statId, out var definition) ||
                definition.Source != StatValueSource.Runtime)
                return false;

            _runtimeValues[statId] = Clamp(definition, value);
            Changed?.Invoke();
            return true;
        }

        public bool TrySetDisplayedValue(string statId, double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                !_definitions.TryGetValue(statId, out var definition))
                return false;

            switch (definition.Source)
            {
                case StatValueSource.Base:
                    SetBaseDisplayedValue(definition, value);
                    NormalizeRuntimeValues();
                    Changed?.Invoke();
                    return true;

                case StatValueSource.Runtime when definition.IsAdjustable:
                    var modifierTotal = GetModifierTotal(statId);
                    _runtimeValues[statId] = Clamp(definition, value - modifierTotal);
                    Changed?.Invoke();
                    return true;

                default:
                    return false;
            }
        }

        public bool AddModifier(string statId, string sourceId, double amount)
        {
            if (!_definitions.ContainsKey(statId) || string.IsNullOrWhiteSpace(sourceId))
                return false;

            if (!_modifiers.TryGetValue(statId, out var statModifiers))
            {
                statModifiers = new Dictionary<string, double>(StringComparer.Ordinal);
                _modifiers.Add(statId, statModifiers);
            }

            statModifiers[sourceId] = amount;
            Changed?.Invoke();
            return true;
        }

        public bool RemoveModifier(string statId, string sourceId)
        {
            if (!_modifiers.TryGetValue(statId, out var statModifiers) ||
                !statModifiers.Remove(sourceId))
                return false;

            if (statModifiers.Count == 0)
                _modifiers.Remove(statId);
            Changed?.Invoke();
            return true;
        }

        public double GetBaseValue(string statId)
        {
            var definition = GetRequiredDefinition(statId);
            if (definition.Source != StatValueSource.Base)
                throw new InvalidOperationException($"기본 스탯이 아닙니다: {statId}");
            return _baseValues.TryGetValue(statId, out var value)
                ? value
                : definition.DefaultValue;
        }

        public double GetModifierTotal(string statId)
        {
            if (!_modifiers.TryGetValue(statId, out var statModifiers))
                return 0d;

            var total = 0d;
            foreach (var pair in statModifiers)
                total += pair.Value;
            return total;
        }

        public double GetModifierAmount(
            string statId,
            string sourceId)
        {
            if (string.IsNullOrWhiteSpace(statId) ||
                string.IsNullOrWhiteSpace(sourceId) ||
                !_modifiers.TryGetValue(
                    statId,
                    out var statModifiers) ||
                !statModifiers.TryGetValue(
                    sourceId,
                    out var amount))
            {
                return 0d;
            }

            return amount;
        }

        public StatRuntimeSnapshot CreateSnapshot()
        {
            var snapshot = new StatRuntimeSnapshot
            {
                CharacterDefinitionId = _character.Id,
                RuleTemplateId = _template.Id,
                RuleTemplateVersion = _template.Version
            };

            foreach (var pair in _runtimeValues)
                snapshot.RuntimeValues.Add(new StatStoredValue(pair.Key, pair.Value));

            foreach (var statPair in _modifiers)
            {
                foreach (var modifierPair in statPair.Value)
                {
                    snapshot.Modifiers.Add(
                        new StatStoredModifier(statPair.Key, modifierPair.Key, modifierPair.Value));
                }
            }

            return snapshot;
        }

        public bool TryApplySnapshot(StatRuntimeSnapshot snapshot, out string error)
        {
            error = string.Empty;
            if (snapshot == null)
            {
                error = "스냅샷이 없습니다.";
                return false;
            }
            if (!string.Equals(snapshot.CharacterDefinitionId, _character.Id, StringComparison.Ordinal))
            {
                error = "다른 캐릭터의 스냅샷입니다.";
                return false;
            }
            if (!string.Equals(snapshot.RuleTemplateId, _template.Id, StringComparison.Ordinal))
            {
                error = "다른 룰 템플릿의 스냅샷입니다.";
                return false;
            }
            if (snapshot.RuleTemplateVersion > _template.Version)
            {
                error = "현재 코드보다 새로운 룰 템플릿 버전의 스냅샷입니다.";
                return false;
            }

            InitializeRuntimeValues();
            _modifiers.Clear();

            if (snapshot.RuntimeValues != null)
            {
                for (var i = 0; i < snapshot.RuntimeValues.Count; i++)
                {
                    var stored = snapshot.RuntimeValues[i];
                    if (stored != null)
                        TrySetRuntimeValueSilently(stored.StatId, stored.Value);
                }
            }

            if (snapshot.Modifiers != null)
            {
                for (var i = 0; i < snapshot.Modifiers.Count; i++)
                {
                    var stored = snapshot.Modifiers[i];
                    if (stored != null)
                        AddModifierSilently(stored.StatId, stored.SourceId, stored.Amount);
                }
            }

            NormalizeRuntimeValues();
            Changed?.Invoke();
            return true;
        }

        private void IndexDefinitions()
        {
            var stats = _template.Stats;
            for (var i = 0; i < stats.Count; i++)
            {
                var definition = stats[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    throw new InvalidOperationException($"[{_template.Id}] 비어 있는 스탯 정의가 있습니다.");
                if (_definitions.ContainsKey(definition.Id))
                    throw new InvalidOperationException($"중복 스탯 ID입니다: {definition.Id}");
                _definitions.Add(definition.Id, definition);
            }
        }

        private void IndexBaseValues()
        {
            var values = _character.BaseValues;
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (_definitions.TryGetValue(value.StatId, out var definition) &&
                    definition.Source == StatValueSource.Base)
                    _baseValues[value.StatId] = value.Value;
            }
        }

        private void InitializeRuntimeValues()
        {
            _runtimeValues.Clear();
            foreach (var pair in _definitions)
            {
                var definition = pair.Value;
                if (definition.Source != StatValueSource.Runtime)
                    continue;
                _runtimeValues.Add(definition.Id, definition.DefaultValue);
            }

            foreach (var pair in _definitions)
            {
                var definition = pair.Value;
                if (definition.Source != StatValueSource.Runtime)
                    continue;

                var value = definition.DefaultValue;
                if (!string.IsNullOrWhiteSpace(definition.InitialValueFormula))
                    value = _calculator.Evaluate(definition.InitialValueFormula, ResolveIdentifier);
                _runtimeValues[definition.Id] = Clamp(definition, value);
            }
        }

        private void SetBaseDisplayedValue(IStatDefinition definition, double requestedValue)
        {
            var baseValue = GetBaseValue(definition.Id);
            var otherModifiers = GetModifierTotal(definition.Id);
            if (_modifiers.TryGetValue(definition.Id, out var statModifiers) &&
                statModifiers.TryGetValue(
                    DirectEditModifierSourceId,
                    out var previousDirectEdit))
                otherModifiers -= previousDirectEdit;

            var clampedValue = Math.Max(
                definition.MinValue,
                Math.Min(definition.MaxValue, requestedValue));
            var directEditAmount = clampedValue - baseValue - otherModifiers;

            if (Math.Abs(directEditAmount) <= 1e-9d)
            {
                if (statModifiers != null)
                {
                    statModifiers.Remove(DirectEditModifierSourceId);
                    if (statModifiers.Count == 0)
                        _modifiers.Remove(definition.Id);
                }
                return;
            }

            AddModifierSilently(
                definition.Id,
                DirectEditModifierSourceId,
                directEditAmount);
        }

        private void NormalizeRuntimeValues()
        {
            var statIds = new List<string>(_runtimeValues.Keys);
            for (var i = 0; i < statIds.Count; i++)
            {
                var statId = statIds[i];
                var definition = _definitions[statId];
                _runtimeValues[statId] = Clamp(definition, _runtimeValues[statId]);
            }
        }

        private void ValidateAll()
        {
            foreach (var pair in _definitions)
                GetValue(pair.Key);
        }

        private StatValue EvaluateDefinition(IStatDefinition definition)
        {
            double number;
            string displayOverride = null;

            switch (definition.Source)
            {
                case StatValueSource.Base:
                    number = _baseValues.TryGetValue(definition.Id, out var baseValue)
                        ? baseValue
                        : definition.DefaultValue;
                    break;
                case StatValueSource.Runtime:
                    number = _runtimeValues[definition.Id];
                    break;
                case StatValueSource.Formula:
                    number = _calculator.Evaluate(definition.Formula, ResolveIdentifier);
                    break;
                case StatValueSource.LookupTable:
                    var band = FindMatchingBand(definition);
                    number = band.NumericValue;
                    displayOverride = band.DisplayText;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            number += GetModifierTotal(definition.Id);
            return new StatValue(Clamp(definition, number), displayOverride);
        }

        private IStatLookupBand FindMatchingBand(IStatDefinition definition)
        {
            var bands = definition.LookupBands;
            for (var i = 0; i < bands.Count; i++)
            {
                var band = bands[i];
                if (_calculator.Evaluate(band.Condition, ResolveIdentifier) != 0d)
                    return band;
            }
            throw new InvalidOperationException($"[{definition.Id}] 조건표에서 일치하는 구간이 없습니다.");
        }

        private double ResolveIdentifier(string statId)
        {
            return GetNumber(statId);
        }

        private double Clamp(IStatDefinition definition, double value)
        {
            var max = definition.MaxValue;
            if (!string.IsNullOrWhiteSpace(definition.MaxStatId) &&
                _definitions.ContainsKey(definition.MaxStatId) &&
                !_evaluationStack.Contains(definition.MaxStatId))
                max = Math.Min(max, GetNumber(definition.MaxStatId));
            return Math.Max(definition.MinValue, Math.Min(max, value));
        }

        private IStatDefinition GetRequiredDefinition(string statId)
        {
            if (!_definitions.TryGetValue(statId, out var definition))
                throw new KeyNotFoundException($"등록되지 않은 스탯 ID입니다: {statId}");
            return definition;
        }

        private bool TrySetRuntimeValueSilently(string statId, double value)
        {
            if (!_definitions.TryGetValue(statId, out var definition) ||
                definition.Source != StatValueSource.Runtime)
                return false;
            _runtimeValues[statId] = Clamp(definition, value);
            return true;
        }

        private bool AddModifierSilently(string statId, string sourceId, double amount)
        {
            if (!_definitions.ContainsKey(statId) || string.IsNullOrWhiteSpace(sourceId))
                return false;
            if (!_modifiers.TryGetValue(statId, out var statModifiers))
            {
                statModifiers = new Dictionary<string, double>(StringComparer.Ordinal);
                _modifiers.Add(statId, statModifiers);
            }
            statModifiers[sourceId] = amount;
            return true;
        }
    }
}
