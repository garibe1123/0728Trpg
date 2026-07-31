using System;
using System.Collections.Generic;
using Trpg.Domain.Stats;
using Trpg.UI.Skills;
using Trpg.UI.Stats;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 선택 Pawn의 하단 정보 바, 이동, 굴림, 스탯 패널과
    /// HP/MP/SAN 리소스 UI를 연결하는 씬 단위 UI Manager입니다.
    /// </summary>
    public sealed class PawnUIManager : MonoBehaviour
    {
        private const int DefaultCheckTarget =
            PawnRollStats.FallbackCheckTarget;
        private const int MinimumCheckTarget = 1;
        private const int MaximumCheckTarget = 100;

        private static readonly string[] PreferredCocAxisIds =
        {
            "coc.str",
            "coc.con",
            "coc.siz",
            "coc.dex",
            "coc.app",
            "coc.int",
            "coc.pow",
            "coc.edu"
        };

        [SerializeField] private PawnManager _pawnManager;
        [SerializeField] private PawnSystemSettings _settings;
        [SerializeField, Tooltip(
            "비어 있으면 런타임에 하단 정보 바를 자동 생성")]
        private PawnInfoBarWidget _infoBar;

        [Header("Movement")]
        [FormerlySerializedAs("_moveButton")]
        [FormerlySerializedAs("_movementButton")]
        [FormerlySerializedAs("walkButton")]
        [SerializeField, Tooltip(
            "선택한 Pawn의 이동 모드를 시작하는 걷기 버튼")]
        private Button _walkButton;

        [Header("Roll")]
        [SerializeField, Tooltip(
            "기존 판정 굴림에서 사용할 기본 능력치 ID")]
        private string _checkStatId = PawnRollStats.DefaultStatId;

        [SerializeField, Min(1), Tooltip("효과 굴림의 주사위 개수")]
        private int _effectDiceCount = 2;

        [SerializeField, Min(2), Tooltip("효과 굴림의 주사위 면수")]
        private int _effectDiceSides = 6;

        [SerializeField, Tooltip("효과 굴림 최종 합계에 더할 보정치")]
        private int _effectDiceModifier;

        [Header("Roll Result Colors")]
        [SerializeField]
        private Color _criticalColor =
            new Color(0.20f, 0.95f, 0.38f);
        [SerializeField]
        private Color _successColor =
            new Color(0.24f, 0.84f, 1f);
        [SerializeField]
        private Color _failureColor =
            new Color(1f, 0.36f, 0.28f);
        [SerializeField] private Color _fumbleColor = Color.black;
        [SerializeField]
        private Color _effectColor =
            new Color(1f, 0.78f, 0.22f);

        private PawnRollService _rollService;
        private PlayerStatState _boundStatState;
        private PlayerSkillState _boundSkillState;
        private string _boundDisplayName;
        private Sprite _boundPortrait;
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
            _infoBar.MoveRequested += StartWalking;
            _infoBar.CheckRollRequested += HandleCheckRollRequested;
            _infoBar.EffectRollRequested += HandleEffectRollRequested;
            _infoBar.RollPresentationCompleted +=
                HandleRollPresentationCompleted;
            _infoBar.StatValueEditRequested +=
                HandleStatValueEditRequested;
            _infoBar.PlayerSkillAddRequested +=
                HandleSkillAddRequested;
            _infoBar.PlayerSkillNameEditRequested +=
                HandleSkillNameEditRequested;
            _infoBar.PlayerSkillRegularEditRequested +=
                HandleSkillRegularEditRequested;
            _infoBar.PlayerSkillRemoveRequested +=
                HandleSkillRemoveRequested;

            BindWalkButton();
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
                _infoBar.MoveRequested -= StartWalking;
                _infoBar.CheckRollRequested -= HandleCheckRollRequested;
                _infoBar.EffectRollRequested -= HandleEffectRollRequested;
                _infoBar.RollPresentationCompleted -=
                    HandleRollPresentationCompleted;
                _infoBar.StatValueEditRequested -=
                    HandleStatValueEditRequested;
                _infoBar.PlayerSkillAddRequested -=
                    HandleSkillAddRequested;
                _infoBar.PlayerSkillNameEditRequested -=
                    HandleSkillNameEditRequested;
                _infoBar.PlayerSkillRegularEditRequested -=
                    HandleSkillRegularEditRequested;
                _infoBar.PlayerSkillRemoveRequested -=
                    HandleSkillRemoveRequested;
                _infoBar.Hide();
            }

            UnbindStatState();
            UnbindSkillState();
            UnbindWalkButton();
            _isRollInProgress = false;
            PlayerStatState.SetActive(null);
        }

        private void HandleCloseRequested()
        {
            _pawnManager.ClearSelection();
        }

        private void HandleInteractiveSelectionChanged(
            InteractivePawn pawn)
        {
            UnbindStatState();
            UnbindSkillState();

            if (pawn == null || pawn.Definition == null)
            {
                PlayerStatState.SetActive(null);
                _infoBar.Unbind();
                RefreshWalkButton(null);
                RefreshRollButtons(null);
                return;
            }

            PlayerStatState.SetActiveFrom(pawn.gameObject);

            var definition = pawn.Definition;
            var displayName =
                string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? pawn.name
                    : definition.DisplayName;
            var infoData = new PawnInfoBarData(
                displayName,
                definition.Description,
                definition.Portrait,
                definition.MovementScore);

            _infoBar.Bind(infoData);

            var statState = ResolveStatState(pawn);
            if (statState != null)
            {
                BindStatState(
                    statState,
                    displayName,
                    definition.Portrait);
                BindSkillState(
                    PlayerSkillState.ResolveOrCreate(
                        pawn.gameObject,
                        definition));
                RefreshStatUi();
            }
            else
            {
                _infoBar.ClearStats();
            }

            RefreshMovementBudget(pawn);
            RefreshWalkButton(pawn);
            RefreshRollButtons(pawn);
        }

        public void StartWalking()
        {
            if (_pawnManager == null)
            {
                return;
            }

            _pawnManager.SetMovementMode(true);
            RefreshWalkButton(_pawnManager.SelectedInteractive);
        }

        public void SetCheckStatId(string statId)
        {
            _checkStatId = string.IsNullOrWhiteSpace(statId)
                ? PawnRollStats.DefaultStatId
                : statId.Trim();
            RefreshRollButtons(_pawnManager.SelectedInteractive);
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

        private void BindStatState(
            PlayerStatState statState,
            string displayName,
            Sprite portrait)
        {
            if (statState == null)
            {
                _infoBar.ClearStats();
                return;
            }

            if (!statState.IsInitialized)
            {
                statState.Initialize();
            }

            if (!statState.IsInitialized)
            {
                _infoBar.ClearStats();
                return;
            }

            _boundStatState = statState;
            _boundDisplayName = displayName;
            _boundPortrait = portrait;
            _boundStatState.Changed -= HandleBoundStatStateChanged;
            _boundStatState.Changed += HandleBoundStatStateChanged;
        }

        private void UnbindStatState()
        {
            if (_boundStatState != null)
            {
                _boundStatState.Changed -= HandleBoundStatStateChanged;
            }

            _boundStatState = null;
            _boundDisplayName = string.Empty;
            _boundPortrait = null;
        }

        private void BindSkillState(PlayerSkillState skillState)
        {
            if (skillState == null)
                return;

            if (!skillState.IsInitialized)
                skillState.Initialize();
            if (!skillState.IsInitialized)
                return;

            _boundSkillState = skillState;
            _boundSkillState.Changed -= HandleBoundSkillStateChanged;
            _boundSkillState.Changed += HandleBoundSkillStateChanged;
        }

        private void UnbindSkillState()
        {
            if (_boundSkillState != null)
            {
                _boundSkillState.Changed -= HandleBoundSkillStateChanged;
            }

            _boundSkillState = null;
        }

        private void HandleBoundStatStateChanged()
        {
            RefreshStatUi();
        }

        private void HandleBoundSkillStateChanged()
        {
            RefreshStatUi();
        }

        private void HandleStatValueEditRequested(
            string statId,
            double value)
        {
            if (_boundStatState == null ||
                string.IsNullOrWhiteSpace(statId))
            {
                return;
            }

            if (!_boundStatState.TrySetDisplayedValue(statId, value))
            {
                Debug.LogWarning(
                    $"[{name}] 표시 스탯 값을 변경하지 못했습니다. " +
                    $"StatId={statId}, Value={value}",
                    _boundStatState);
                RefreshStatUi();
            }
        }

        private void HandleSkillAddRequested(
            PawnSkillAddRequest request)
        {
            if (_boundSkillState == null)
                return;

            string error;
            var succeeded = string.IsNullOrWhiteSpace(request.SkillId)
                ? _boundSkillState.TryAddCustom(
                    "새 스킬",
                    request.RegularValue,
                    out _,
                    out error)
                : _boundSkillState.TryAdd(
                    request.SkillId,
                    request.RegularValue,
                    out error);

            if (!succeeded)
            {
                Debug.LogWarning(
                    $"[{name}] 스킬을 추가하지 못했습니다. {error}",
                    _boundSkillState);
                RefreshStatUi();
            }
        }

        private void HandleSkillNameEditRequested(
            PawnSkillNameEditRequest request)
        {
            if (_boundSkillState == null ||
                !_boundSkillState.TrySetDisplayName(
                    request.SkillId,
                    request.DisplayName))
            {
                RefreshStatUi();
            }
        }

        private void HandleSkillRegularEditRequested(
            PawnSkillRegularEditRequest request)
        {
            if (_boundSkillState == null ||
                !_boundSkillState.TrySetRegularValue(
                    request.SkillId,
                    request.RegularValue))
            {
                RefreshStatUi();
            }
        }

        private void HandleSkillRemoveRequested(
            PawnSkillRemoveRequest request)
        {
            if (_boundSkillState == null ||
                !_boundSkillState.TryRemove(request.SkillId))
            {
                RefreshStatUi();
            }
        }

        private void RefreshStatUi()
        {
            if (_boundStatState == null ||
                !_boundStatState.IsInitialized ||
                _boundStatState.Runtime == null)
            {
                _infoBar.ClearStats();
                return;
            }

            try
            {
                var data = BuildStatPanelData(
                    _boundStatState.Runtime,
                    _boundSkillState,
                    _boundDisplayName,
                    _boundPortrait);
                _infoBar.SetStats(data);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _infoBar.ClearStats();
            }
        }

        private static PawnStatPanelData BuildStatPanelData(
            StatRuntimeState runtime,
            PlayerSkillState skillState,
            string displayName,
            Sprite portrait)
        {
            var definitions = new List<IStatDefinition>(
                runtime.Template.Stats);
            definitions.Sort(
                (left, right) =>
                    left.SortOrder.CompareTo(right.SortOrder));

            var resourceIds = new HashSet<string>(
                StringComparer.Ordinal);
            var resources = new List<PawnResourceValueData>(3);

            TryAddResource(
                runtime,
                StatRole.HealthCurrent,
                StatRole.HealthMax,
                "체력",
                resources,
                resourceIds);
            TryAddResource(
                runtime,
                StatRole.SanityCurrent,
                StatRole.SanityMax,
                "이성",
                resources,
                resourceIds);
            TryAddResource(
                runtime,
                StatRole.MagicCurrent,
                StatRole.MagicMax,
                "마력",
                resources,
                resourceIds);

            var axes = BuildAxes(
                runtime,
                definitions,
                resourceIds);
            var entries = BuildEntries(
                runtime,
                definitions,
                resourceIds);

            var skills = BuildSkillValues(skillState);

            return new PawnStatPanelData(
                displayName,
                portrait,
                axes,
                entries,
                resources,
                skills,
                Array.Empty<PawnSkillOptionData>(),
                skillState != null && skillState.IsInitialized);
        }

        private static List<PawnSkillValueData> BuildSkillValues(
            PlayerSkillState skillState)
        {
            var result = new List<PawnSkillValueData>();
            if (skillState == null || !skillState.IsInitialized)
                return result;

            var source = skillState.Skills;
            for (var index = 0; index < source.Count; index++)
            {
                var value = source[index];
                var regular = Mathf.Clamp(
                    value.RegularValue,
                    0,
                    999);
                result.Add(
                    new PawnSkillValueData(
                        value.SkillId,
                        value.DisplayName,
                        value.Category,
                        regular,
                        regular / 2,
                        regular / 5,
                        value.UsesBaseValue,
                        value.RequiresTraining,
                        value.SortOrder));
            }

            result.Sort((left, right) =>
            {
                var order = left.SortOrder.CompareTo(right.SortOrder);
                if (order != 0)
                    return order;

                return string.Compare(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.CurrentCulture);
            });
            return result;
        }

        private static List<PawnStatAxisData> BuildAxes(
            StatRuntimeState runtime,
            IReadOnlyList<IStatDefinition> definitions,
            ISet<string> excludedIds)
        {
            var axes = new List<PawnStatAxisData>(8);
            var usedIds = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0;
                 index < PreferredCocAxisIds.Length;
                 index++)
            {
                TryAddAxis(
                    runtime,
                    PreferredCocAxisIds[index],
                    axes,
                    usedIds,
                    excludedIds);
            }

            for (var index = 0;
                 index < definitions.Count && axes.Count < 8;
                 index++)
            {
                var definition = definitions[index];
                if (definition.Source != StatValueSource.Base)
                {
                    continue;
                }

                TryAddAxis(
                    runtime,
                    definition.Id,
                    axes,
                    usedIds,
                    excludedIds);
            }

            return axes;
        }

        private static void TryAddAxis(
            StatRuntimeState runtime,
            string statId,
            ICollection<PawnStatAxisData> destination,
            ISet<string> usedIds,
            ISet<string> excludedIds)
        {
            if (string.IsNullOrWhiteSpace(statId) ||
                usedIds.Contains(statId) ||
                excludedIds.Contains(statId) ||
                !runtime.TryGetDefinition(statId, out var definition))
            {
                return;
            }

            var value = runtime.GetNumber(statId);
            var minimum = definition.MinValue;
            var maximum = definition.MaxValue;
            if (maximum <= minimum)
            {
                maximum = Math.Max(minimum + 1d, value);
            }

            destination.Add(
                new PawnStatAxisData(
                    statId,
                    definition.DisplayName,
                    value,
                    minimum,
                    maximum));
            usedIds.Add(statId);
        }

        private static List<PawnStatEntryData> BuildEntries(
            StatRuntimeState runtime,
            IReadOnlyList<IStatDefinition> definitions,
            ISet<string> resourceIds)
        {
            var entries = new List<PawnStatEntryData>(
                definitions.Count);
            var presentation = new StatPresentationService(runtime);
            var isCocTemplate = IsCocTemplate(runtime.Template);

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (resourceIds.Contains(definition.Id))
                {
                    continue;
                }

                var value = runtime.GetNumber(definition.Id);
                var showsDifficulty =
                    isCocTemplate &&
                    IsD100Checkable(definition, value);
                var regular = showsDifficulty
                    ? Mathf.Clamp(
                        (int)Math.Floor(value),
                        0,
                        100)
                    : 0;
                var hard = showsDifficulty ? regular / 2 : 0;
                var extreme = showsDifficulty ? regular / 5 : 0;

                entries.Add(
                    new PawnStatEntryData(
                        definition.Id,
                        definition.DisplayName,
                        presentation.FormatValue(definition.Id),
                        value,
                        IsEditable(definition),
                        showsDifficulty,
                        regular,
                        hard,
                        extreme,
                        value,
                        0d,
                        0d,
                        definition.MinValue,
                        definition.MaxValue));
            }

            return entries;
        }

        private static bool IsCocTemplate(IStatRuleTemplate template)
        {
            if (template == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(template.Id) &&
                template.Id.IndexOf(
                    "coc",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var stats = template.Stats;
            for (var index = 0; index < stats.Count; index++)
            {
                if (stats[index].Id.StartsWith(
                        "coc.",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsD100Checkable(
            IStatDefinition definition,
            double value)
        {
            if (definition == null ||
                value < 0d ||
                value > 100d)
            {
                return false;
            }

            if (definition.Source == StatValueSource.Base)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(definition.Category) &&
                   definition.Category.IndexOf(
                       "기술",
                       StringComparison.Ordinal) >= 0;
        }

        private static void TryAddResource(
            StatRuntimeState runtime,
            StatRole currentRole,
            StatRole maximumRole,
            string label,
            ICollection<PawnResourceValueData> destination,
            ISet<string> resourceIds)
        {
            var currentId = runtime.Template.GetStatId(currentRole);
            var maximumId = runtime.Template.GetStatId(maximumRole);

            if (string.IsNullOrWhiteSpace(currentId) ||
                !runtime.TryGetDefinition(
                    currentId,
                    out var currentDefinition))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(maximumId))
            {
                maximumId = currentDefinition.MaxStatId;
            }

            var current = runtime.GetNumber(currentId);
            var maximum = current;
            var canEditMaximum = false;

            if (!string.IsNullOrWhiteSpace(maximumId) &&
                runtime.TryGetDefinition(
                    maximumId,
                    out var maximumDefinition))
            {
                maximum = runtime.GetNumber(maximumId);
                canEditMaximum = IsEditable(maximumDefinition);
                resourceIds.Add(maximumId);
            }

            destination.Add(
                new PawnResourceValueData(
                    label,
                    currentId,
                    current,
                    maximumId,
                    maximum,
                    true,
                    IsEditable(currentDefinition),
                    canEditMaximum));
            resourceIds.Add(currentId);
        }

        private static bool IsEditable(IStatDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return definition.Source == StatValueSource.Base ||
                   definition.Source == StatValueSource.Runtime &&
                   definition.IsAdjustable;
        }

        private static PlayerStatState ResolveStatState(
            InteractivePawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            var state = pawn.GetComponentInParent<PlayerStatState>();
            if (state == null)
            {
                state = pawn.GetComponentInChildren<PlayerStatState>();
            }

            if (state == null)
            {
                state = PlayerStatState.ActiveState;
            }

            return state;
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

        private void HandleMovementRangeChanged(
            PawnMovementRangeData data)
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

        private void BindWalkButton()
        {
            if (_walkButton == null)
            {
                return;
            }

            _walkButton.onClick.RemoveListener(StartWalking);
            _walkButton.onClick.AddListener(StartWalking);
        }

        private void UnbindWalkButton()
        {
            if (_walkButton != null)
            {
                _walkButton.onClick.RemoveListener(StartWalking);
            }
        }

        private void RefreshWalkButton(InteractivePawn pawn)
        {
            var canMove = pawn != null && pawn.IsMoveable;
            var isActive =
                canMove &&
                _pawnManager != null &&
                _pawnManager.IsMovementModeActive &&
                _pawnManager.SelectedInteractive == pawn;

            _infoBar?.SetMovementModeState(canMove, isActive);

            if (_walkButton != null)
            {
                _walkButton.interactable = canMove;
            }
        }

        private void HandleCheckRollRequested(
            PawnCheckRollRequest request)
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

        private void HandleEffectRollRequested(
            PawnEffectRollRequest request)
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
            if (pawn != null &&
                pawn.TryGetComponent<PawnRollStats>(out var stats))
            {
                return stats.GetCheckTarget(_checkStatId);
            }

            return DefaultCheckTarget;
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

            // 판정 입력창의 목표값을 강제로 다시 밀어 넣지 않는다.
            // 잘못 추가된 별도 판정 Overlay도 이 Manager에서 생성하지 않는다.
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
