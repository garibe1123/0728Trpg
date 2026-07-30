using System;
using System.Collections.Generic;
using Trpg.Domain.Skills;
using Trpg.Domain.Stats;
using Trpg.UI.Skills;
using Trpg.UI.Stats;
using UnityEngine;

namespace Trpg.Pawns
{
    public sealed class PawnUIManager : MonoBehaviour
    {
        private const int DefaultCheckTarget =
            PawnRollStats.FallbackCheckTarget;
        private const int MinimumCheckTarget = 1;
        private const int MaximumCheckTarget = 100;

        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private PawnSystemSettings _settings;
        [SerializeField, Tooltip("비어 있으면 런타임에 하단 정보 바를 자동 생성")]
        private PawnInfoBarWidget _infoBar;

        [Header("Roll")]
        [SerializeField, Tooltip(
            "판정할 스탯 ID. default이면 룰 템플릿의 Dexterity 역할을 사용합니다.")]
        private string _checkStatId = PawnRollStats.DefaultStatId;

        [SerializeField, Min(1), Tooltip("효과 굴림의 주사위 개수")]
        private int _effectDiceCount = 2;

        [SerializeField, Min(2), Tooltip("효과 굴림의 주사위 면수")]
        private int _effectDiceSides = 6;

        [SerializeField, Tooltip("효과 굴림 최종 합계에 더할 보정치")]
        private int _effectDiceModifier;

        [Header("Stats")]
        [SerializeField, Tooltip(
            "테스트 단계의 GM 편집 권한입니다. 기본값은 true입니다.")]
        private bool _isGmMode = true;

        [Header("System Menu")]
        [SerializeField, Tooltip(
            "비어 있으면 같은 GameObject에 자동으로 추가합니다.")]
        private CampaignSaveManager _campaignSaveManager;

        [Header("Roll Result Colors")]
        [SerializeField] private Color _criticalColor =
            new Color(0.20f, 0.95f, 0.38f);
        [SerializeField] private Color _successColor =
            new Color(0.24f, 0.84f, 1f);
        [SerializeField] private Color _failureColor =
            new Color(1f, 0.36f, 0.28f);
        [SerializeField] private Color _fumbleColor = Color.black;
        [SerializeField] private Color _effectColor =
            new Color(1f, 0.78f, 0.22f);

        private PawnRollService _rollService;
        private PlayerStatState _boundStatState;
        private InteractivePawn _playerHudPawn;
        private PlayerStatState _playerHudStatState;
        private PlayerSkillState _playerHudSkillState;
        private bool _isRollInProgress;

        private void Awake()
        {
            if (_pawnManager == null || _settings == null)
            {
                Debug.LogError(
                    $"[{name}] PawnUIManager 필수 참조가 비어 있습니다.",
                    this);
                enabled = false;
                return;
            }

            if (_infoBar == null)
            {
                _infoBar = PawnInfoBarWidget.CreateRuntime(_settings);
            }

            EnsureCampaignSaveManager();

            var seed = unchecked(
                Environment.TickCount * 397 ^ GetInstanceID());
            _rollService = new PawnRollService(seed);
        }

        private void OnEnable()
        {
            if (_pawnManager == null || _infoBar == null)
            {
                return;
            }

            _pawnManager.InteractiveSelectionChanged +=
                HandleInteractiveSelectionChanged;
            _pawnManager.MovementManager.MovementBudgetChanged +=
                HandleMovementBudgetChanged;
            _pawnManager.MovementManager.PathPreviewChanged +=
                HandlePathPreviewChanged;
            _pawnManager.MovementManager.MovementRangeChanged +=
                HandleMovementRangeChanged;
            _infoBar.CloseRequested += HandleCloseRequested;
            _infoBar.MoveRequested += HandleMoveRequested;
            _infoBar.CheckRollRequested += HandleCheckRollRequested;
            _infoBar.EffectRollRequested += HandleEffectRollRequested;
            _infoBar.RollPresentationCompleted +=
                HandleRollPresentationCompleted;
            _infoBar.ResourceStatValueEditRequested +=
                HandleResourceStatValueEditRequested;
            _infoBar.PlayerStatValueEditRequested +=
                HandlePlayerStatValueEditRequested;
            _infoBar.PlayerSkillAddRequested +=
                HandlePlayerSkillAddRequested;
            _infoBar.PlayerSkillNameEditRequested +=
                HandlePlayerSkillNameEditRequested;
            _infoBar.PlayerSkillRegularEditRequested +=
                HandlePlayerSkillRegularEditRequested;
            _infoBar.PlayerSkillRemoveRequested +=
                HandlePlayerSkillRemoveRequested;
            _infoBar.PlayerHudRequested +=
                HandlePlayerHudRequested;
            EnsurePlayerHudState(
                _pawnManager.SelectedInteractive);
            HandleInteractiveSelectionChanged(
                _pawnManager.SelectedInteractive);
        }

        private void OnDisable()
        {
            if (_pawnManager != null)
            {
                _pawnManager.InteractiveSelectionChanged -=
                    HandleInteractiveSelectionChanged;
                _pawnManager.MovementManager.MovementBudgetChanged -=
                    HandleMovementBudgetChanged;
                _pawnManager.MovementManager.PathPreviewChanged -=
                    HandlePathPreviewChanged;
                _pawnManager.MovementManager.MovementRangeChanged -=
                    HandleMovementRangeChanged;
            }

            if (_infoBar != null)
            {
                _infoBar.CloseRequested -= HandleCloseRequested;
                _infoBar.MoveRequested -= HandleMoveRequested;
                _infoBar.CheckRollRequested -= HandleCheckRollRequested;
                _infoBar.EffectRollRequested -= HandleEffectRollRequested;
                _infoBar.RollPresentationCompleted -=
                    HandleRollPresentationCompleted;
                _infoBar.ResourceStatValueEditRequested -=
                    HandleResourceStatValueEditRequested;
                _infoBar.PlayerStatValueEditRequested -=
                    HandlePlayerStatValueEditRequested;
                _infoBar.PlayerSkillAddRequested -=
                    HandlePlayerSkillAddRequested;
                _infoBar.PlayerSkillNameEditRequested -=
                    HandlePlayerSkillNameEditRequested;
                _infoBar.PlayerSkillRegularEditRequested -=
                    HandlePlayerSkillRegularEditRequested;
                _infoBar.PlayerSkillRemoveRequested -=
                    HandlePlayerSkillRemoveRequested;
                _infoBar.PlayerHudRequested -=
                    HandlePlayerHudRequested;
                _infoBar.Hide();
                _infoBar.ClearStats();
            }

            _isRollInProgress = false;
            BindStatState(null);
            BindPlayerHudState(null, null);
            BindPlayerSkillState(null);
            PlayerStatState.SetActive(null);
        }

        private void HandleCloseRequested()
        {
            _pawnManager.ClearSelection();
        }

        private void EnsureCampaignSaveManager()
        {
            if (_campaignSaveManager == null)
            {
                _campaignSaveManager =
                    GetComponent<CampaignSaveManager>();
            }

            if (_campaignSaveManager == null)
            {
                _campaignSaveManager =
                    gameObject.AddComponent<CampaignSaveManager>();
            }

            _campaignSaveManager.Configure(_pawnManager);
        }

        private void HandlePlayerHudRequested()
        {
            if (_pawnManager == null ||
                !IsPlayerPawn(_playerHudPawn))
            {
                return;
            }

            _pawnManager.SelectAndFocusInteractive(_playerHudPawn);
        }

        private void HandleInteractiveSelectionChanged(
            InteractivePawn pawn)
        {
            if (pawn == null || pawn.Definition == null)
            {
                BindStatState(null);
                EnsurePlayerHudState(null);
                PlayerStatState.SetActive(_playerHudStatState);
                _infoBar.Unbind();
                RefreshPlayerStatPanel();
                RefreshRollButtons(null);
                return;
            }

            var definition = pawn.Definition;
            PlayerStatState.SetActiveFrom(
                pawn.gameObject,
                definition,
                definition.Id);
            BindStatState(PlayerStatState.ActiveState);
            if (IsPlayerPawn(pawn))
            {
                BindPlayerHudState(
                    pawn,
                    PlayerStatState.ActiveState);
            }
            else
            {
                EnsurePlayerHudState(null);
            }
            _pawnManager.MovementManager
                .RefreshMovementBudgetFromStats(
                    pawn,
                    true);

            var displayName =
                string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? pawn.name
                    : definition.DisplayName;
            var movementScore = _boundStatState != null
                ? definition.ResolveMovementScore(
                    _boundStatState,
                    definition.MovementScore)
                : definition.MovementScore;
            var data = new PawnInfoBarData(
                displayName,
                definition.Description,
                definition.Portrait,
                movementScore);
            _infoBar.Bind(data);
            RefreshSelectedResourceBar();
            RefreshPlayerStatPanel();
            RefreshMovementBudget(pawn);
            RefreshMovementModeState(pawn);
            RefreshRollButtons(pawn);
        }

        private void HandleMoveRequested()
        {
            if (_pawnManager == null)
            {
                return;
            }

            var requestedActive =
                !_pawnManager.IsMovementModeActive;
            _pawnManager.SetMovementMode(requestedActive);
            RefreshMovementModeState(
                _pawnManager.SelectedInteractive);
        }

        public void SetCheckStatId(string statId)
        {
            _checkStatId = string.IsNullOrWhiteSpace(statId)
                ? PawnRollStats.DefaultStatId
                : statId.Trim();
            RefreshRollButtons(_pawnManager.SelectedInteractive);
        }

        public void SetGmMode(bool isGmMode)
        {
            if (_isGmMode == isGmMode)
                return;

            _isGmMode = isGmMode;
            RefreshSelectedResourceBar();
            RefreshPlayerStatPanel();
        }

        public void SetEffectDiceExpression(
            int diceCount,
            int sides,
            int modifier = 0)
        {
            _effectDiceCount = Mathf.Clamp(
                diceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _effectDiceSides = Mathf.Clamp(
                sides,
                2,
                PawnRollService.MaximumDiceSides);
            _effectDiceModifier = modifier;
            RefreshRollButtons(_pawnManager.SelectedInteractive);
        }

        private void HandleMovementBudgetChanged(
            InteractivePawn pawn,
            float remainingMeters,
            float maximumMeters)
        {
            if (pawn == _pawnManager.SelectedInteractive)
            {
                _infoBar.SetMovementBudget(
                    remainingMeters,
                    maximumMeters);
            }
        }

        private void HandlePathPreviewChanged(PawnPathPreviewData data)
        {
            _infoBar.SetPathPreview(data, _pawnManager.BoardCamera);
        }

        private void HandleMovementRangeChanged(PawnMovementRangeData data)
        {
            _infoBar.SetMovementRange(data, _pawnManager.BoardCamera);
        }

        private void RefreshMovementBudget(InteractivePawn pawn)
        {
            if (_pawnManager.MovementManager.TryGetMovementBudget(
                    pawn,
                    out var remaining,
                    out var maximum))
            {
                _infoBar.SetMovementBudget(remaining, maximum);
            }
        }

        private void RefreshMovementModeState(InteractivePawn pawn)
        {
            var canMove = pawn != null && pawn.IsMoveable;
            _infoBar.SetMovementModeState(
                canMove,
                canMove && _pawnManager.IsMovementModeActive);
        }

        private void HandleCheckRollRequested(PawnCheckRollRequest request)
        {
            if (!TryBeginRoll(out var pawn))
            {
                return;
            }

            var target = ResolveCheckTarget(pawn);
            var result = _rollService.RollD100(target);
            var presentation = new PawnRollPresentationData(
                "판정 굴림",
                $"d100 / 목표 {target}",
                result.Roll,
                1,
                100,
                GetCheckResultLabel(result.Grade),
                $"굴림 {result.Roll} / 목표 {target}",
                GetCheckResultColor(result.Grade),
                1.55f);
            _infoBar.PlayRoll(presentation);
        }

        private void HandleEffectRollRequested(PawnEffectRollRequest request)
        {
            if (!TryBeginRoll(out _))
            {
                return;
            }

            var result = _rollService.RollEffect(
                _effectDiceCount,
                _effectDiceSides,
                _effectDiceModifier);
            var presentation = new PawnRollPresentationData(
                "효과 굴림",
                result.Expression,
                result.Total,
                result.MinimumTotal,
                result.MaximumTotal,
                $"합계 {result.Total}",
                result.GetBreakdownLabel(),
                _effectColor,
                1.35f);
            _infoBar.PlayRoll(presentation);
        }

        private void HandleRollPresentationCompleted()
        {
            _isRollInProgress = false;
            _infoBar.SetRollButtonsEnabled(
                _pawnManager.SelectedInteractive != null &&
                _pawnManager.SelectedInteractive.Definition != null);
        }

        private bool TryBeginRoll(out InteractivePawn pawn)
        {
            pawn = _pawnManager.SelectedInteractive;
            if (_isRollInProgress ||
                pawn == null ||
                pawn.Definition == null)
            {
                return false;
            }

            _isRollInProgress = true;
            _infoBar.SetRollButtonsEnabled(false);
            return true;
        }

        private int ResolveCheckTarget(InteractivePawn pawn)
        {
            var statState = ResolvePawnStatState(pawn);
            if (statState != null &&
                TryResolveRuleCheckTarget(
                    statState,
                    out var ruleTarget))
            {
                return ruleTarget;
            }

            if (pawn != null &&
                pawn.TryGetComponent<PawnRollStats>(out var stats))
            {
                return stats.GetCheckTarget(_checkStatId);
            }

            return DefaultCheckTarget;
        }

        private bool TryResolveRuleCheckTarget(
            PlayerStatState statState,
            out int target)
        {
            target = DefaultCheckTarget;
            if (statState == null ||
                !statState.IsInitialized)
            {
                return false;
            }

            var statId = _checkStatId;
            if (string.IsNullOrWhiteSpace(statId) ||
                string.Equals(
                    statId,
                    PawnRollStats.DefaultStatId,
                    StringComparison.Ordinal))
            {
                statId = statState.Runtime.Template.GetStatId(
                    StatRole.Dexterity);
            }

            if (string.IsNullOrWhiteSpace(statId))
            {
                statId = FindFirstBaseStatId(
                    statState.Runtime.Template);
            }

            if (string.IsNullOrWhiteSpace(statId) ||
                !statState.TryGetNumber(statId, out var value))
            {
                return false;
            }

            target = Mathf.Clamp(
                Mathf.RoundToInt((float)value),
                MinimumCheckTarget,
                MaximumCheckTarget);
            return true;
        }

        private static string FindFirstBaseStatId(
            IStatRuleTemplate template)
        {
            if (template == null)
            {
                return string.Empty;
            }

            var stats = template.Stats;
            for (var index = 0;
                 index < stats.Count;
                 index++)
            {
                if (stats[index] != null &&
                    stats[index].Source == StatValueSource.Base)
                {
                    return stats[index].Id;
                }
            }

            return string.Empty;
        }

        private static PlayerStatState ResolvePawnStatState(
            InteractivePawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            var state = pawn.GetComponent<PlayerStatState>();
            if (state == null)
                state = pawn.GetComponentInChildren<PlayerStatState>();
            if (state == null)
                state = pawn.GetComponentInParent<PlayerStatState>();
            return state;
        }

        private void BindStatState(PlayerStatState state)
        {
            if (ReferenceEquals(_boundStatState, state))
            {
                return;
            }

            if (_boundStatState != null)
            {
                _boundStatState.Changed -= HandleBoundStatChanged;
            }

            _boundStatState = state;
            if (_boundStatState != null)
            {
                _boundStatState.Changed += HandleBoundStatChanged;
            }
        }

        private void HandleBoundStatChanged()
        {
            var pawn = _pawnManager != null
                ? _pawnManager.SelectedInteractive
                : null;
            if (pawn == null)
            {
                return;
            }

            _pawnManager.MovementManager
                .RefreshMovementBudgetFromStats(
                    pawn,
                    true);
            RefreshMovementBudget(pawn);
            RefreshSelectedResourceBar();
            RefreshRollButtons(pawn);
        }

        private void BindPlayerHudState(
            InteractivePawn pawn,
            PlayerStatState state)
        {
            var skillState = IsPlayerPawn(pawn)
                ? PlayerSkillState.ResolveOrCreate(
                    pawn.gameObject,
                    pawn.Definition)
                : null;
            if (ReferenceEquals(_playerHudPawn, pawn) &&
                ReferenceEquals(_playerHudStatState, state) &&
                ReferenceEquals(_playerHudSkillState, skillState))
            {
                return;
            }

            if (_playerHudStatState != null)
            {
                _playerHudStatState.Changed -=
                    HandlePlayerHudStatChanged;
            }

            _playerHudPawn = pawn;
            _playerHudStatState = state;
            if (_playerHudStatState != null)
            {
                _playerHudStatState.Changed +=
                    HandlePlayerHudStatChanged;
            }
            BindPlayerSkillState(skillState);
        }

        private void BindPlayerSkillState(PlayerSkillState state)
        {
            if (ReferenceEquals(_playerHudSkillState, state))
                return;

            if (_playerHudSkillState != null)
            {
                _playerHudSkillState.Changed -=
                    HandlePlayerHudSkillChanged;
            }

            _playerHudSkillState = state;
            if (_playerHudSkillState != null)
            {
                _playerHudSkillState.Changed +=
                    HandlePlayerHudSkillChanged;
            }
        }

        private void EnsurePlayerHudState(
            InteractivePawn preferredPawn)
        {
            if (IsPlayerPawn(preferredPawn))
            {
                PlayerStatState.SetActiveFrom(
                    preferredPawn.gameObject,
                    preferredPawn.Definition,
                    preferredPawn.Definition.Id);
                BindPlayerHudState(
                    preferredPawn,
                    PlayerStatState.ActiveState);
                return;
            }

            if (_playerHudPawn != null &&
                _playerHudStatState != null &&
                _playerHudStatState.IsInitialized)
            {
                return;
            }

            var playerPawns = _pawnManager != null
                ? _pawnManager.PlayerPawns
                : null;
            var playerPawn =
                playerPawns != null && playerPawns.Count > 0
                    ? playerPawns[0]
                    : null;
            if (!IsPlayerPawn(playerPawn))
            {
                BindPlayerHudState(null, null);
                return;
            }

            PlayerStatState.SetActiveFrom(
                playerPawn.gameObject,
                playerPawn.Definition,
                playerPawn.Definition.Id);
            BindPlayerHudState(
                playerPawn,
                PlayerStatState.ActiveState);
        }

        private static bool IsPlayerPawn(InteractivePawn pawn)
        {
            return pawn != null &&
                   pawn.Definition != null &&
                   pawn.Definition.Kind ==
                       InteractivePawnKind.Moveable &&
                   pawn.Definition.MoveableKind ==
                       MoveablePawnKind.Player;
        }

        private void HandlePlayerHudStatChanged()
        {
            RefreshPlayerStatPanel();
        }

        private void HandlePlayerHudSkillChanged()
        {
            RefreshPlayerStatPanel();
        }

        private void RefreshSelectedResourceBar()
        {
            if (_infoBar == null ||
                _boundStatState == null ||
                !_boundStatState.IsInitialized)
            {
                _infoBar?.ClearResourceStats();
                return;
            }

            _infoBar.SetResourceStats(
                CreateResourceValues(_boundStatState));
        }

        private void RefreshPlayerStatPanel()
        {
            if (_infoBar == null ||
                _playerHudStatState == null ||
                !_playerHudStatState.IsInitialized)
            {
                _infoBar?.ClearPlayerStats();
                return;
            }

            var panelData =
                CreateStatPanelData(_playerHudStatState);
            _infoBar.SetPlayerStats(panelData);
        }

        private PawnStatPanelData CreateStatPanelData(
            PlayerStatState state)
        {
            var runtime = state.Runtime;
            var presentation = new StatPresentationService(runtime);
            var definitions =
                new List<IStatDefinition>(runtime.Template.Stats);
            definitions.Sort(
                (left, right) =>
                    left.SortOrder.CompareTo(right.SortOrder));

            var resourceStatIds =
                CollectResourceStatIds(runtime.Template);
            var resources = CreateResourceValues(state);

            var axes = new List<PawnStatAxisData>(8);
            var entries =
                new List<PawnStatEntryData>(definitions.Count);
            for (var index = 0;
                 index < definitions.Count;
                 index++)
            {
                var definition = definitions[index];
                if (definition == null)
                    continue;

                if (definition.Source == StatValueSource.Base &&
                    !resourceStatIds.Contains(definition.Id) &&
                    axes.Count < 8)
                {
                    axes.Add(
                        new PawnStatAxisData(
                            definition.Id,
                            definition.DisplayName,
                            runtime.GetNumber(definition.Id),
                            definition.MinValue,
                            definition.MaxValue));
                }

                if (resourceStatIds.Contains(definition.Id))
                    continue;

                var numericValue = runtime.GetNumber(definition.Id);
                var canEdit =
                    _isGmMode &&
                    definition.Source == StatValueSource.Base;
                var showsDifficulty =
                    definition.Source == StatValueSource.Base;
                var baseValue = showsDifficulty
                    ? runtime.GetBaseValue(definition.Id)
                    : numericValue;
                var manualModifier = showsDifficulty
                    ? runtime.GetModifierAmount(
                        definition.Id,
                        StatRuntimeState.DirectEditModifierSourceId)
                    : 0d;
                var otherModifier = showsDifficulty
                    ? runtime.GetModifierTotal(definition.Id) -
                      manualModifier
                    : 0d;
                var thresholds =
                    SkillDifficultyCalculator.Calculate(
                        Mathf.RoundToInt((float)numericValue));
                entries.Add(
                    new PawnStatEntryData(
                        definition.Id,
                        definition.DisplayName,
                        presentation.FormatValue(definition.Id),
                        numericValue,
                        canEdit,
                        showsDifficulty,
                        thresholds.Regular,
                        thresholds.Hard,
                        thresholds.Extreme,
                        baseValue,
                        otherModifier,
                        manualModifier,
                        definition.MinValue,
                        definition.MaxValue));
            }

            var playerDefinition = _playerHudPawn != null
                ? _playerHudPawn.Definition
                : null;
            var displayName = playerDefinition != null &&
                              !string.IsNullOrWhiteSpace(
                                  playerDefinition.DisplayName)
                ? playerDefinition.DisplayName
                : _playerHudPawn != null
                    ? _playerHudPawn.name
                    : string.Empty;
            var portrait = playerDefinition != null
                ? playerDefinition.Portrait
                : null;
            var skills = CreateSkillValues(_playerHudSkillState);
            var availableSkillOptions =
                CreateAvailableSkillOptions(
                    playerDefinition,
                    _playerHudSkillState);
            var panelData = new PawnStatPanelData(
                displayName,
                portrait,
                axes,
                entries,
                resources,
                skills,
                availableSkillOptions,
                _playerHudSkillState != null);
            return panelData;
        }

        private static List<PawnSkillValueData> CreateSkillValues(
            PlayerSkillState state)
        {
            var values = new List<PawnSkillValueData>();
            var records = state != null && state.IsInitialized
                ? state.Skills
                : null;
            var count = records != null ? records.Count : 0;
            for (var index = 0; index < count; index++)
            {
                var record = records[index];
                if (string.IsNullOrWhiteSpace(record.SkillId))
                    continue;

                var thresholds =
                    SkillDifficultyCalculator.Calculate(
                        record.RegularValue);
                values.Add(
                    new PawnSkillValueData(
                        record.SkillId,
                        record.DisplayName,
                        record.Category,
                        thresholds.Regular,
                        thresholds.Hard,
                        thresholds.Extreme,
                        record.UsesBaseValue,
                        record.RequiresTraining,
                        record.SortOrder));
            }

            values.Sort(CompareSkills);
            return values;
        }

        private static List<PawnSkillOptionData>
            CreateAvailableSkillOptions(
                InteractivePawnDefinition definition,
                PlayerSkillState state)
        {
            var options = new List<PawnSkillOptionData>();
            var catalog = definition != null
                ? definition.SkillCatalog
                : null;
            var skills = catalog != null ? catalog.Skills : null;
            var ownedIds =
                new HashSet<string>(StringComparer.Ordinal);
            var owned = state != null && state.IsInitialized
                ? state.Skills
                : null;
            var ownedCount = owned != null ? owned.Count : 0;
            for (var index = 0; index < ownedCount; index++)
            {
                var skillId = owned[index].SkillId;
                if (!string.IsNullOrWhiteSpace(skillId))
                    ownedIds.Add(skillId);
            }

            var count = skills != null ? skills.Count : 0;
            for (var index = 0; index < count; index++)
            {
                var skill = skills[index];
                if (skill == null ||
                    string.IsNullOrWhiteSpace(skill.Id) ||
                    ownedIds.Contains(skill.Id))
                {
                    continue;
                }

                options.Add(
                    new PawnSkillOptionData(
                        skill.Id,
                        skill.DisplayName,
                        skill.BaseValue));
            }

            options.Sort(
                (left, right) => string.Compare(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.CurrentCulture));
            return options;
        }

        private static int CompareSkills(
            PawnSkillValueData left,
            PawnSkillValueData right)
        {
            var categoryOrder = string.Compare(
                left.Category,
                right.Category,
                StringComparison.CurrentCulture);
            if (categoryOrder != 0)
                return categoryOrder;

            var sortOrder = left.SortOrder.CompareTo(right.SortOrder);
            if (sortOrder != 0)
                return sortOrder;

            return string.Compare(
                left.DisplayName,
                right.DisplayName,
                StringComparison.CurrentCulture);
        }

        private List<PawnResourceValueData> CreateResourceValues(
            PlayerStatState state)
        {
            return new List<PawnResourceValueData>(4)
            {
                CreateResourceValue(
                    state,
                    StatRole.HealthCurrent,
                    StatRole.HealthMax,
                    "현재 체력",
                    false,
                    _isGmMode),
                CreateResourceValue(
                    state,
                    StatRole.SanityCurrent,
                    StatRole.SanityMax,
                    "현재 이성",
                    false,
                    _isGmMode),
                CreateResourceValue(
                    state,
                    StatRole.LuckCurrent,
                    StatRole.LuckMax,
                    "현재 운",
                    false,
                    _isGmMode),
                CreateResourceValue(
                    state,
                    StatRole.MagicCurrent,
                    StatRole.MagicMax,
                    "현재 마력",
                    true,
                    _isGmMode)
            };
        }

        private static HashSet<string> CollectResourceStatIds(
            IStatRuleTemplate template)
        {
            var result =
                new HashSet<string>(StringComparer.Ordinal);
            if (template == null)
                return result;

            AddRoleStatId(result, template, StatRole.HealthCurrent);
            AddRoleStatId(result, template, StatRole.HealthMax);
            AddRoleStatId(result, template, StatRole.SanityCurrent);
            AddRoleStatId(result, template, StatRole.SanityMax);
            AddRoleStatId(result, template, StatRole.LuckCurrent);
            AddRoleStatId(result, template, StatRole.LuckMax);
            AddRoleStatId(result, template, StatRole.MagicCurrent);
            AddRoleStatId(result, template, StatRole.MagicMax);
            return result;
        }

        private static void AddRoleStatId(
            ISet<string> target,
            IStatRuleTemplate template,
            StatRole role)
        {
            var statId = template.GetStatId(role);
            if (!string.IsNullOrWhiteSpace(statId))
                target.Add(statId);
        }

        private static PawnResourceValueData CreateResourceValue(
            PlayerStatState state,
            StatRole currentRole,
            StatRole maximumRole,
            string label,
            bool hideWhenMaximumIsZero,
            bool isGmMode)
        {
            var currentStatId = state != null
                ? state.Runtime.Template.GetStatId(currentRole)
                : string.Empty;
            var maximumStatId = state != null
                ? state.Runtime.Template.GetStatId(maximumRole)
                : string.Empty;
            if (state == null ||
                !state.TryGetRoleNumber(
                    currentRole,
                    out var current) ||
                !state.TryGetRoleNumber(
                    maximumRole,
                    out var maximum))
            {
                return new PawnResourceValueData(
                    label,
                    currentStatId,
                    0d,
                    maximumStatId,
                    0d,
                    false,
                    false,
                    false);
            }

            var isVisible =
                !hideWhenMaximumIsZero ||
                maximum > 0.0001d;
            return new PawnResourceValueData(
                label,
                currentStatId,
                current,
                maximumStatId,
                maximum,
                isVisible,
                CanEditStat(
                    state.Runtime,
                    currentStatId,
                    isGmMode),
                CanEditStat(
                    state.Runtime,
                    maximumStatId,
                    isGmMode));
        }

        private static bool CanEditStat(
            StatRuntimeState runtime,
            string statId,
            bool isGmMode)
        {
            if (!isGmMode ||
                runtime == null ||
                string.IsNullOrWhiteSpace(statId) ||
                !runtime.TryGetDefinition(
                    statId,
                    out var definition))
            {
                return false;
            }

            return definition.Source == StatValueSource.Base ||
                   (definition.Source == StatValueSource.Runtime &&
                    definition.IsAdjustable);
        }

        private void HandleResourceStatValueEditRequested(
            string statId,
            double value)
        {
            if (!_isGmMode ||
                _boundStatState == null)
            {
                return;
            }

            _boundStatState.TrySetDisplayedValue(statId, value);
        }

        private void HandlePlayerStatValueEditRequested(
            string statId,
            double modifierAmount)
        {
            if (!_isGmMode ||
                _playerHudStatState == null)
            {
                return;
            }

            _playerHudStatState.SetGmManualModifier(
                statId,
                modifierAmount);
        }

        private void HandlePlayerSkillAddRequested(
            PawnSkillAddRequest request)
        {
            if (_playerHudSkillState == null)
                return;

            string error;
            bool added;
            if (string.IsNullOrWhiteSpace(request.SkillId))
            {
                added = _playerHudSkillState.TryAddCustom(
                    "새 스킬",
                    request.RegularValue,
                    out _,
                    out error);
            }
            else
            {
                added = _playerHudSkillState.TryAdd(
                    request.SkillId,
                    request.RegularValue,
                    out error);
            }

            if (!added)
            {
                Debug.LogWarning(error, _playerHudPawn);
            }
        }

        private void HandlePlayerSkillNameEditRequested(
            PawnSkillNameEditRequest request)
        {
            if (_playerHudSkillState == null)
                return;

            if (!_playerHudSkillState.TrySetDisplayName(
                    request.SkillId,
                    request.DisplayName))
            {
                Debug.LogWarning(
                    $"스킬 이름을 변경하지 못했습니다: {request.SkillId}",
                    _playerHudPawn);
            }
        }

        private void HandlePlayerSkillRegularEditRequested(
            PawnSkillRegularEditRequest request)
        {
            if (_playerHudSkillState == null)
                return;

            if (!_playerHudSkillState.TrySetRegularValue(
                    request.SkillId,
                    request.RegularValue))
            {
                Debug.LogWarning(
                    $"스킬 보통값을 변경하지 못했습니다: {request.SkillId}",
                    _playerHudPawn);
            }
        }

        private void HandlePlayerSkillRemoveRequested(
            PawnSkillRemoveRequest request)
        {
            if (_playerHudSkillState == null)
                return;

            if (!_playerHudSkillState.TryRemove(request.SkillId))
            {
                Debug.LogWarning(
                    $"스킬을 삭제하지 못했습니다: {request.SkillId}",
                    _playerHudPawn);
            }
        }

        private void RefreshRollButtons(InteractivePawn pawn)
        {
            if (_infoBar == null)
            {
                return;
            }

            _isRollInProgress = false;
            _infoBar.CancelRollPresentation();

            var hasPawn = pawn != null && pawn.Definition != null;
            _infoBar.SetRollButtonsEnabled(hasPawn);
            if (!hasPawn)
            {
                return;
            }

            var target = Mathf.Clamp(
                ResolveCheckTarget(pawn),
                MinimumCheckTarget,
                MaximumCheckTarget);
            var effectExpression = PawnRollService.FormatExpression(
                _effectDiceCount,
                _effectDiceSides,
                _effectDiceModifier);
            _infoBar.SetRollButtonLabels(
                $"판정 굴림\nD100 ≤ {target}",
                $"효과 굴림\n{effectExpression}");
        }

        private Color GetCheckResultColor(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return _criticalColor;
                case CheckRollGrade.Fumble:
                    return _fumbleColor;
                case CheckRollGrade.ExtremeSuccess:
                case CheckRollGrade.HardSuccess:
                case CheckRollGrade.Success:
                    return _successColor;
                default:
                    return _failureColor;
            }
        }

        private static string GetCheckResultLabel(CheckRollGrade grade)
        {
            switch (grade)
            {
                case CheckRollGrade.Critical:
                    return "대성공";
                case CheckRollGrade.ExtremeSuccess:
                    return "극단적 성공";
                case CheckRollGrade.HardSuccess:
                    return "어려운 성공";
                case CheckRollGrade.Success:
                    return "성공";
                case CheckRollGrade.Fumble:
                    return "대실패";
                default:
                    return "실패";
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _effectDiceCount = Mathf.Clamp(
                _effectDiceCount,
                1,
                PawnRollService.MaximumDiceCount);
            _effectDiceSides = Mathf.Clamp(
                _effectDiceSides,
                2,
                PawnRollService.MaximumDiceSides);
            if (string.IsNullOrWhiteSpace(_checkStatId))
            {
                _checkStatId = PawnRollStats.DefaultStatId;
            }
        }
#endif
    }
}
