using System;
using Trpg.Domain.Stats;
using Trpg.Pawns;
using UnityEngine;

namespace Trpg.UI.Stats
{
    public sealed class PlayerStatState : MonoBehaviour
    {
        [SerializeField, Tooltip(
            "이 Pawn의 스탯·스킬·표시 데이터를 보유한 Interactive Pawn Definition입니다.")]
        private InteractivePawnDefinition _definition;

        [SerializeField]
        private bool _initializeOnAwake = true;

        private StatRuntimeState _runtime;

        public static event Action<PlayerStatState> ActiveStateChanged;
        public event Action Changed;

        public static PlayerStatState ActiveState { get; private set; }
        public InteractivePawnDefinition Definition => _definition;
        public StatRuntimeState Runtime => _runtime;
        public bool IsInitialized => _runtime != null;

        private void Awake()
        {
            if (_initializeOnAwake && _definition != null)
                Initialize();
        }

        private void OnDestroy()
        {
            if (_runtime != null)
                _runtime.Changed -= OnRuntimeChanged;

            if (ReferenceEquals(ActiveState, this))
                SetActive(null);
        }

        private void OnDisable()
        {
            if (ReferenceEquals(ActiveState, this))
                SetActive(null);
        }

        public void Initialize()
        {
            if (_runtime != null)
                return;

            if (!TryGetCharacterDefinition(
                    _definition,
                    out var characterDefinition,
                    out var error))
            {
                Debug.LogError($"[{name}] {error}", this);
                return;
            }

            try
            {
                _runtime = new StatRuntimeState(characterDefinition);
                _runtime.Changed += OnRuntimeChanged;
                Changed?.Invoke();
            }
            catch (Exception exception)
            {
                _runtime = null;
                Debug.LogException(exception, this);
            }
        }

        public bool Configure(InteractivePawnDefinition definition)
        {
            if (definition == null)
                return false;

            if (ReferenceEquals(_definition, definition))
                return true;

            if (_runtime != null)
                return false;

            _definition = definition;
            return true;
        }

        public bool TryAdjust(string statId, double delta)
        {
            return _runtime != null &&
                   _runtime.TryAdjust(statId, delta);
        }

        public bool TrySetRuntimeValue(string statId, double value)
        {
            return _runtime != null &&
                   _runtime.TrySetRuntimeValue(statId, value);
        }

        public bool TrySetDisplayedValue(string statId, double value)
        {
            return _runtime != null &&
                   _runtime.TrySetDisplayedValue(statId, value);
        }

        public bool TrySetAuthoritativeDisplayedValue(
            string statId,
            double value)
        {
            return _runtime != null &&
                   _runtime.TrySetAuthoritativeDisplayedValue(
                       statId,
                       value);
        }

        public void Activate()
        {
            if (!IsInitialized)
                Initialize();

            if (IsInitialized)
                SetActive(this);
        }

        public static bool SetActiveFrom(GameObject selectedObject)
        {
            if (selectedObject == null)
            {
                SetActive(null);
                return false;
            }

            var pawn = ResolveInteractivePawn(selectedObject);
            var state = pawn != null &&
                        pawn.Definition != null &&
                        pawn.HasStats
                ? ResolveOrCreate(pawn.gameObject, pawn.Definition)
                : pawn == null
                    ? ResolveForPawn(selectedObject)
                    : null;

            if (state == null)
            {
                SetActive(null);
                return false;
            }

            state.Activate();
            return state.IsInitialized;
        }

        public static void SetActive(PlayerStatState state)
        {
            if (ReferenceEquals(ActiveState, state))
                return;

            ActiveState = state;
            ActiveStateChanged?.Invoke(ActiveState);
        }

        /// <summary>
        /// Pawn 하나에 귀속된 PlayerStatState를 찾습니다.
        /// InteractivePawn을 찾은 경우 그 Pawn 루트와 자식만 검색하며,
        /// 여러 Pawn이 공유하는 상위 오브젝트의 상태를 가져오지 않습니다.
        /// </summary>
        public static PlayerStatState ResolveForPawn(
            GameObject pawnObject)
        {
            if (pawnObject == null)
                return null;

            var pawn = ResolveInteractivePawn(pawnObject);
            var root = pawn != null
                ? pawn.gameObject
                : pawnObject;

            var state = root.GetComponent<PlayerStatState>();
            if (state == null)
            {
                state = root.GetComponentInChildren<
                    PlayerStatState>(true);
            }

            if (state == null && pawn == null)
            {
                state = pawnObject.GetComponentInParent<
                    PlayerStatState>(true);
            }

            return state;
        }

        /// <summary>
        /// 현재 Pawn의 InteractivePawnDefinition을 기준으로 상태를 찾거나
        /// Pawn 루트에 생성하고 초기화합니다.
        /// </summary>
        public static PlayerStatState ResolveOrCreate(
            GameObject selectedObject,
            InteractivePawnDefinition definition)
        {
            if (selectedObject == null ||
                definition == null ||
                !definition.SupportsStats)
            {
                return null;
            }

            var pawn = ResolveInteractivePawn(selectedObject);
            var root = pawn != null
                ? pawn.gameObject
                : selectedObject;

            var state = ResolveForPawn(root);
            if (state == null)
                state = root.AddComponent<PlayerStatState>();

            if (!state.Configure(definition))
                return null;

            if (!state.IsInitialized)
                state.Initialize();

            return state.IsInitialized ? state : null;
        }

        /// <summary>
        /// 저장 스냅숏을 적용할 상태를 현재 Pawn Definition으로 준비합니다.
        /// 구버전 저장의 빈 CharacterDefinitionId와 룰 메타데이터는
        /// 현재 Pawn Definition을 기준으로 보완합니다.
        /// </summary>
        public static bool TryResolveOrCreateForSnapshot(
            GameObject pawnObject,
            InteractivePawnDefinition definition,
            StatRuntimeSnapshot snapshot,
            out PlayerStatState state,
            out string error)
        {
            state = null;
            error = string.Empty;

            if (pawnObject == null)
            {
                error = "대상 Pawn 오브젝트가 없습니다.";
                return false;
            }

            if (definition == null)
            {
                error = "InteractivePawnDefinition이 연결되지 않았습니다.";
                return false;
            }

            if (!definition.SupportsStats)
            {
                error = "Player 또는 NPC Pawn만 스탯 Snapshot을 사용할 수 있습니다.";
                return false;
            }

            if (snapshot == null)
            {
                error = "스탯 스냅숏이 없습니다.";
                return false;
            }

            if (!TryNormalizeSnapshotMetadata(
                    snapshot,
                    definition,
                    out _,
                    out error))
            {
                return false;
            }

            var pawn = ResolveInteractivePawn(pawnObject);
            var root = pawn != null
                ? pawn.gameObject
                : pawnObject;
            state = ResolveForPawn(root);

            var created = false;
            if (state == null)
            {
                state = root.AddComponent<PlayerStatState>();
                created = true;
            }

            if (!state.Configure(definition))
            {
                error =
                    "현재 PlayerStatState가 다른 InteractivePawnDefinition으로 " +
                    "이미 초기화되어 있습니다.";
                if (created)
                    UnityEngine.Object.Destroy(state);
                state = null;
                return false;
            }

            if (!state.IsInitialized)
                state.Initialize();

            if (!state.IsInitialized)
            {
                error = "스탯 런타임을 초기화하지 못했습니다.";
                if (created)
                    UnityEngine.Object.Destroy(state);
                state = null;
                return false;
            }

            if (!state.CanApplySnapshot(snapshot, out error))
            {
                if (created)
                    UnityEngine.Object.Destroy(state);
                state = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 이전 v11.3/v11.4 호출 형태와의 컴파일 호환용입니다.
        /// Pawn에 연결된 InteractivePawnDefinition을 직접 사용합니다.
        /// </summary>
        public static bool TryResolveOrCreateForSnapshot(
            GameObject pawnObject,
            StatRuntimeSnapshot snapshot,
            out PlayerStatState state,
            out string error)
        {
            var pawn = ResolveInteractivePawn(pawnObject);
            return TryResolveOrCreateForSnapshot(
                pawnObject,
                pawn != null ? pawn.Definition : null,
                snapshot,
                out state,
                out error);
        }

        public bool CanApplySnapshot(
            StatRuntimeSnapshot snapshot,
            out string error)
        {
            return TryNormalizeSnapshotMetadata(
                snapshot,
                _definition,
                out _,
                out error);
        }

        public bool TryPrepareSnapshotMetadata(
            StatRuntimeSnapshot snapshot,
            out bool upgraded,
            out string error)
        {
            return TryNormalizeSnapshotMetadata(
                snapshot,
                _definition,
                out upgraded,
                out error);
        }

        public bool AddModifier(
            string statId,
            string sourceId,
            double amount)
        {
            return _runtime != null &&
                   _runtime.AddModifier(statId, sourceId, amount);
        }

        public bool RemoveModifier(string statId, string sourceId)
        {
            return _runtime != null &&
                   _runtime.RemoveModifier(statId, sourceId);
        }

        public StatRuntimeSnapshot CreateSnapshot()
        {
            var snapshot = _runtime?.CreateSnapshot();
            if (snapshot == null)
                return null;

            if (!TryNormalizeSnapshotMetadata(
                    snapshot,
                    _definition,
                    out _,
                    out var error))
            {
                Debug.LogError(
                    $"[{name}] 스탯 스냅숏 메타데이터 생성 실패: {error}",
                    this);
            }

            return snapshot;
        }

        public bool TryApplySnapshot(
            StatRuntimeSnapshot snapshot,
            out string error)
        {
            if (_runtime == null)
            {
                error = "스탯 런타임이 초기화되지 않았습니다.";
                return false;
            }

            if (!CanApplySnapshot(snapshot, out error))
                return false;

            return _runtime.TryApplySnapshot(snapshot, out error);
        }

        private static bool TryNormalizeSnapshotMetadata(
            StatRuntimeSnapshot snapshot,
            InteractivePawnDefinition definition,
            out bool upgraded,
            out string error)
        {
            upgraded = false;
            error = string.Empty;

            if (snapshot == null)
            {
                error = "스탯 스냅숏이 없습니다.";
                return false;
            }

            if (!TryGetCharacterDefinition(
                    definition,
                    out var characterDefinition,
                    out error))
            {
                return false;
            }

            var template = characterDefinition.RuleTemplate;
            if (template == null)
            {
                error = "InteractivePawnDefinition에서 스탯 룰 템플릿을 찾지 못했습니다.";
                return false;
            }

            var currentCharacterId = NormalizeId(
                characterDefinition.Id);
            if (string.IsNullOrWhiteSpace(currentCharacterId))
            {
                error = "InteractivePawnDefinition의 Id가 비어 있습니다.";
                return false;
            }

            var storedCharacterId = NormalizeId(
                snapshot.CharacterDefinitionId);
            if (string.IsNullOrWhiteSpace(storedCharacterId))
            {
                snapshot.CharacterDefinitionId = currentCharacterId;
                upgraded = true;
            }
            else if (!string.Equals(
                         storedCharacterId,
                         currentCharacterId,
                         StringComparison.Ordinal))
            {
                error =
                    $"다른 Pawn Definition의 스탯 스냅숏입니다. " +
                    $"저장={storedCharacterId}, 현재={currentCharacterId}";
                return false;
            }
            else
            {
                snapshot.CharacterDefinitionId = storedCharacterId;
            }

            var currentTemplateId = NormalizeId(template.Id);
            if (string.IsNullOrWhiteSpace(currentTemplateId))
            {
                error = "현재 스탯 룰 템플릿의 Id가 비어 있습니다.";
                return false;
            }

            var storedTemplateId = NormalizeId(snapshot.RuleTemplateId);
            if (string.IsNullOrWhiteSpace(storedTemplateId))
            {
                snapshot.RuleTemplateId = currentTemplateId;
                upgraded = true;
            }
            else if (!string.Equals(
                         storedTemplateId,
                         currentTemplateId,
                         StringComparison.Ordinal))
            {
                error =
                    $"다른 룰 템플릿의 스탯 스냅숏입니다. " +
                    $"저장={storedTemplateId}, 현재={currentTemplateId}";
                return false;
            }
            else
            {
                snapshot.RuleTemplateId = storedTemplateId;
            }

            if (snapshot.RuleTemplateVersion <= 0)
            {
                snapshot.RuleTemplateVersion = template.Version;
                upgraded = true;
            }
            else if (snapshot.RuleTemplateVersion > template.Version)
            {
                error =
                    $"저장 데이터의 룰 템플릿 버전이 더 높습니다. " +
                    $"저장={snapshot.RuleTemplateVersion}, " +
                    $"현재={template.Version}";
                return false;
            }

            return true;
        }

        private static bool TryGetCharacterDefinition(
            InteractivePawnDefinition definition,
            out ICharacterStatDefinition characterDefinition,
            out string error)
        {
            characterDefinition = definition as ICharacterStatDefinition;
            error = string.Empty;

            if (definition == null)
            {
                error = "InteractivePawnDefinition이 연결되지 않았습니다.";
                return false;
            }

            if (characterDefinition == null)
            {
                error =
                    "현재 InteractivePawnDefinition이 " +
                    "ICharacterStatDefinition을 구현하지 않습니다.";
                return false;
            }

            return true;
        }

        private static InteractivePawn ResolveInteractivePawn(
            GameObject selectedObject)
        {
            if (selectedObject == null)
                return null;

            var pawn = selectedObject.GetComponent<InteractivePawn>();
            if (pawn == null)
            {
                pawn = selectedObject.GetComponentInParent<
                    InteractivePawn>(true);
            }
            if (pawn == null)
            {
                pawn = selectedObject.GetComponentInChildren<
                    InteractivePawn>(true);
            }

            return pawn;
        }

        private static string NormalizeId(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        private void OnRuntimeChanged()
        {
            Changed?.Invoke();
        }
    }
}
