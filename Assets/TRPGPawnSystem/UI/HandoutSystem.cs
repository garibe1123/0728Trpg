using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Data.Handouts
{
    [CreateAssetMenu(
        menuName = "Trpg/Handouts/Handout Definition",
        fileName = "HandoutDefinition")]
    public sealed class HandoutDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("저장과 네트워크에서 사용하는 고유 ID")]
        private string _id = "handout.new";

        [SerializeField, Tooltip("화면에 표시할 핸드아웃 번호")]
        private string _handoutNumber = "01";

        [SerializeField, TextArea(3, 12), Tooltip("이미지 아래에 표시할 설명")]
        private string _description = "핸드아웃 설명";

        [SerializeField, Tooltip("핸드아웃 원본 이미지")]
        private Sprite _image;

        public string Id => _id;
        public string HandoutNumber => _handoutNumber;
        public string Description => _description;
        public Sprite Image => _image;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError(
                    $"[{name}] Handout Definition Id가 비어 있습니다.",
                    this);
            }

            if (string.IsNullOrWhiteSpace(_handoutNumber))
            {
                Debug.LogError(
                    $"[{name}] 핸드아웃 번호가 비어 있습니다.",
                    this);
            }

            if (_image == null)
            {
                Debug.LogWarning(
                    $"[{name}] 핸드아웃 이미지가 비어 있습니다.",
                    this);
            }
        }
#endif
    }

    [CreateAssetMenu(
        menuName = "Trpg/Handouts/Handout Catalog Definition",
        fileName = "HandoutCatalogDefinition")]
    public sealed class HandoutCatalogDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("카탈로그 고유 ID")]
        private string _id = "handout_catalog.default";

        [SerializeField, Tooltip("시나리오에 미리 준비된 전체 핸드아웃")]
        private List<HandoutDefinition> _handouts =
            new List<HandoutDefinition>();

        public string Id => _id;
        public IReadOnlyList<HandoutDefinition> Handouts => _handouts;

        public bool TryGetById(
            string handoutId,
            out HandoutDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(handoutId))
                return false;

            for (var index = 0; index < _handouts.Count; index++)
            {
                var current = _handouts[index];
                if (current != null && string.Equals(
                        current.Id,
                        handoutId,
                        StringComparison.Ordinal))
                {
                    definition = current;
                    return true;
                }
            }

            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError(
                    $"[{name}] Handout Catalog Id가 비어 있습니다.",
                    this);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < _handouts.Count; index++)
            {
                var handout = _handouts[index];
                if (handout == null)
                    continue;

                if (!ids.Add(handout.Id))
                {
                    Debug.LogError(
                        $"[{name}] 중복 Handout Id: {handout.Id}",
                        this);
                }
            }
        }
#endif
    }
}

namespace Trpg.UI.Handouts
{
    using Trpg.Data.Handouts;
    using Trpg.Pawns;

    public readonly struct PublicHandoutRuntimeValue
    {
        public PublicHandoutRuntimeValue(
            HandoutDefinition definition,
            string definitionId,
            string handoutNumber,
            string description)
        {
            Definition = definition;
            DefinitionId = definitionId ?? string.Empty;
            HandoutNumber = NormalizeNumber(handoutNumber);
            Description = description ?? string.Empty;
        }

        public HandoutDefinition Definition { get; }
        public string DefinitionId { get; }
        public string HandoutNumber { get; }
        public string Description { get; }
        public Sprite Image => Definition != null
            ? Definition.Image
            : null;
        public bool IsDefinitionMissing => Definition == null;

        public PublicHandoutRuntimeValue Resolve(
            HandoutDefinition definition)
        {
            if (definition == null)
                return this;

            return new PublicHandoutRuntimeValue(
                definition,
                definition.Id,
                definition.HandoutNumber,
                definition.Description);
        }

        private static string NormalizeNumber(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "?"
                : value.Trim();
        }
    }

    [Serializable]
    public sealed class PublicHandoutEntrySnapshot
    {
        public string DefinitionId;
        public string HandoutNumber;
        public string Description;
    }

    [Serializable]
    public sealed class PawnHandoutRecordSnapshot
    {
        public string PawnInstanceId;
        public string DefinitionId;
        public bool IsAvailable;
        public bool HasOpened;
        public string FirstOpenedUtc;
        public string LastOpenedUtc;
    }

    [Serializable]
    public sealed class PublicHandoutSnapshot
    {
        public List<PublicHandoutEntrySnapshot> Handouts =
            new List<PublicHandoutEntrySnapshot>();
        public List<PawnHandoutRecordSnapshot> PawnRecords =
            new List<PawnHandoutRecordSnapshot>();
    }

    [DisallowMultipleComponent]
    public sealed class PublicHandoutState : MonoBehaviour
    {
        private readonly List<PublicHandoutRuntimeValue> _handouts =
            new List<PublicHandoutRuntimeValue>();
        private readonly List<PawnHandoutRecordSnapshot> _pawnRecords =
            new List<PawnHandoutRecordSnapshot>();

        private HandoutCatalogDefinition _catalog;
        private bool _isInitialized;

        public event Action Changed;

        public HandoutCatalogDefinition Catalog => _catalog;
        public IReadOnlyList<PublicHandoutRuntimeValue> Handouts =>
            _handouts;
        public bool IsInitialized => _isInitialized;

        public static PublicHandoutState ResolveOrCreate(
            GameObject preferredHost,
            HandoutCatalogDefinition catalog = null)
        {
            PublicHandoutState state = null;
            if (preferredHost != null)
                state = preferredHost.GetComponent<PublicHandoutState>();

            if (state == null)
            {
                state = UnityEngine.Object.FindFirstObjectByType<
                    PublicHandoutState>(
                    FindObjectsInactive.Include);
            }

            if (state == null && preferredHost != null)
                state = preferredHost.AddComponent<PublicHandoutState>();

            if (state == null)
                return null;

            state.Configure(catalog);
            state.Initialize();
            return state;
        }

        public void Configure(HandoutCatalogDefinition catalog)
        {
            if (catalog != null)
                _catalog = catalog;

            if (!_isInitialized || _catalog == null)
                return;

            var changed = false;
            for (var index = 0; index < _handouts.Count; index++)
            {
                var current = _handouts[index];
                if (!_catalog.TryGetById(
                        current.DefinitionId,
                        out var definition) ||
                    ReferenceEquals(current.Definition, definition))
                {
                    continue;
                }

                _handouts[index] = current.Resolve(definition);
                changed = true;
            }

            if (changed)
                Changed?.Invoke();
        }

        public void Initialize()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            Changed?.Invoke();
        }

        public bool Contains(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                return false;

            for (var index = 0; index < _handouts.Count; index++)
            {
                if (string.Equals(
                        _handouts[index].DefinitionId,
                        definitionId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryAdd(
            HandoutDefinition definition,
            out string error)
        {
            error = string.Empty;
            if (!_isInitialized)
                Initialize();

            if (definition == null ||
                string.IsNullOrWhiteSpace(definition.Id))
            {
                error = "추가할 Handout Definition이 유효하지 않습니다.";
                return false;
            }

            if (Contains(definition.Id))
            {
                error = "이미 공개된 핸드아웃입니다.";
                return false;
            }

            _handouts.Add(
                new PublicHandoutRuntimeValue(
                    definition,
                    definition.Id,
                    definition.HandoutNumber,
                    definition.Description));
            Changed?.Invoke();
            return true;
        }

        public bool TryRemove(string definitionId)
        {
            if (!_isInitialized ||
                string.IsNullOrWhiteSpace(definitionId))
            {
                return false;
            }

            for (var index = 0; index < _handouts.Count; index++)
            {
                if (!string.Equals(
                        _handouts[index].DefinitionId,
                        definitionId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                _handouts.RemoveAt(index);
                _pawnRecords.RemoveAll(record =>
                    record != null &&
                    string.Equals(
                        record.DefinitionId,
                        definitionId,
                        StringComparison.Ordinal));
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>
        /// targetIndex 위치에 핸드아웃을 삽입합니다.
        /// [1,2,3,4]에서 4를 1 위치로 옮기면 [4,1,2,3]입니다.
        /// </summary>
        public bool TryMove(string definitionId, int targetIndex)
        {
            if (!_isInitialized ||
                string.IsNullOrWhiteSpace(definitionId) ||
                _handouts.Count <= 1)
            {
                return false;
            }

            var sourceIndex = -1;
            for (var index = 0; index < _handouts.Count; index++)
            {
                if (string.Equals(
                        _handouts[index].DefinitionId,
                        definitionId,
                        StringComparison.Ordinal))
                {
                    sourceIndex = index;
                    break;
                }
            }

            if (sourceIndex < 0)
                return false;

            var clampedTarget = Mathf.Clamp(
                targetIndex,
                0,
                _handouts.Count - 1);
            if (sourceIndex == clampedTarget)
                return true;

            var moving = _handouts[sourceIndex];
            _handouts.RemoveAt(sourceIndex);
            clampedTarget = Mathf.Clamp(
                clampedTarget,
                0,
                _handouts.Count);
            _handouts.Insert(clampedTarget, moving);
            Changed?.Invoke();
            return true;
        }

        public IReadOnlyList<PublicHandoutRuntimeValue>
            GetAvailableForPawn(InteractivePawn pawn)
        {
            var result = new List<PublicHandoutRuntimeValue>();
            var pawnId = ResolvePawnId(pawn);
            if (string.IsNullOrWhiteSpace(pawnId))
                return result;

            for (var index = 0; index < _handouts.Count; index++)
            {
                var handout = _handouts[index];
                var record = FindRecord(pawnId, handout.DefinitionId);
                if (record != null && record.IsAvailable)
                    result.Add(handout);
            }

            return result;
        }

        public bool TryGrantToPawn(
            InteractivePawn pawn,
            HandoutDefinition definition,
            out string error)
        {
            error = string.Empty;
            var pawnId = ResolvePawnId(pawn);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                error = "핸드아웃을 부여할 Pawn Instance Id가 없습니다.";
                return false;
            }

            if (definition == null ||
                string.IsNullOrWhiteSpace(definition.Id))
            {
                error = "부여할 Handout Definition이 유효하지 않습니다.";
                return false;
            }

            if (!Contains(definition.Id))
            {
                if (!TryAdd(definition, out error))
                    return false;
            }

            var record = FindRecord(pawnId, definition.Id);
            if (record == null)
            {
                _pawnRecords.Add(new PawnHandoutRecordSnapshot
                {
                    PawnInstanceId = pawnId,
                    DefinitionId = definition.Id,
                    IsAvailable = true,
                    HasOpened = false
                });
                Changed?.Invoke();
                return true;
            }

            if (record.IsAvailable)
            {
                error = "선택한 Pawn에게 이미 공개된 핸드아웃입니다.";
                return false;
            }

            record.IsAvailable = true;
            Changed?.Invoke();
            return true;
        }

        public bool TryGrantExistingToPawn(
            InteractivePawn pawn,
            string definitionId,
            out string error)
        {
            error = string.Empty;
            var pawnId = ResolvePawnId(pawn);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                error = "핸드아웃을 부여할 Pawn Instance Id가 없습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definitionId) ||
                !Contains(definitionId))
            {
                error = "중앙 핸드아웃 목록에서 해당 항목을 찾지 못했습니다.";
                return false;
            }

            var record = FindRecord(pawnId, definitionId);
            if (record == null)
            {
                _pawnRecords.Add(new PawnHandoutRecordSnapshot
                {
                    PawnInstanceId = pawnId,
                    DefinitionId = definitionId,
                    IsAvailable = true,
                    HasOpened = false
                });
                Changed?.Invoke();
                return true;
            }

            if (record.IsAvailable)
                return true;

            record.IsAvailable = true;
            Changed?.Invoke();
            return true;
        }

        public bool TryRevokeFromPawn(
            InteractivePawn pawn,
            string definitionId)
        {
            var pawnId = ResolvePawnId(pawn);
            var record = FindRecord(pawnId, definitionId);
            if (record == null || !record.IsAvailable)
                return false;

            record.IsAvailable = false;
            Changed?.Invoke();
            return true;
        }

        public bool MarkOpened(
            InteractivePawn pawn,
            string definitionId)
        {
            var pawnId = ResolvePawnId(pawn);
            var record = FindRecord(pawnId, definitionId);
            if (record == null || !record.IsAvailable)
                return false;

            var now = DateTime.UtcNow.ToString("O");
            if (!record.HasOpened)
            {
                record.HasOpened = true;
                record.FirstOpenedUtc = now;
            }
            record.LastOpenedUtc = now;
            Changed?.Invoke();
            return true;
        }

        public bool TryGetRecordSnapshot(
            InteractivePawn pawn,
            string definitionId,
            out PawnHandoutRecordSnapshot snapshot)
        {
            snapshot = null;
            var record = FindRecord(
                ResolvePawnId(pawn),
                definitionId);
            if (record == null)
                return false;

            snapshot = CloneRecord(record);
            return true;
        }

        public IReadOnlyList<PawnHandoutRecordSnapshot>
            GetRecordSnapshotsForPawn(InteractivePawn pawn)
        {
            var result = new List<PawnHandoutRecordSnapshot>();
            var pawnId = ResolvePawnId(pawn);
            if (string.IsNullOrWhiteSpace(pawnId))
                return result;

            for (var index = 0; index < _pawnRecords.Count; index++)
            {
                var record = _pawnRecords[index];
                if (record != null &&
                    string.Equals(
                        record.PawnInstanceId,
                        pawnId,
                        StringComparison.Ordinal))
                {
                    result.Add(CloneRecord(record));
                }
            }

            return result;
        }

        public bool ApplyNetworkRecord(
            InteractivePawn pawn,
            PawnHandoutRecordSnapshot incoming)
        {
            if (pawn == null || incoming == null ||
                string.IsNullOrWhiteSpace(incoming.DefinitionId))
            {
                return false;
            }

            var pawnId = ResolvePawnId(pawn);
            if (string.IsNullOrWhiteSpace(pawnId))
                return false;

            if (!Contains(incoming.DefinitionId))
            {
                HandoutDefinition definition = null;
                _catalog?.TryGetById(
                    incoming.DefinitionId,
                    out definition);
                _handouts.Add(
                    definition != null
                        ? new PublicHandoutRuntimeValue(
                            definition,
                            definition.Id,
                            definition.HandoutNumber,
                            definition.Description)
                        : new PublicHandoutRuntimeValue(
                            null,
                            incoming.DefinitionId,
                            "?",
                            string.Empty));
            }

            var record = FindRecord(pawnId, incoming.DefinitionId);
            if (record == null)
            {
                record = new PawnHandoutRecordSnapshot
                {
                    PawnInstanceId = pawnId,
                    DefinitionId = incoming.DefinitionId
                };
                _pawnRecords.Add(record);
            }

            record.IsAvailable = incoming.IsAvailable;
            record.HasOpened = incoming.HasOpened;
            record.FirstOpenedUtc = incoming.FirstOpenedUtc;
            record.LastOpenedUtc = incoming.LastOpenedUtc;
            Changed?.Invoke();
            return true;
        }

        public bool HasOpened(
            InteractivePawn pawn,
            string definitionId)
        {
            var record = FindRecord(
                ResolvePawnId(pawn),
                definitionId);
            return record != null &&
                   record.IsAvailable &&
                   record.HasOpened;
        }

        private PawnHandoutRecordSnapshot FindRecord(
            string pawnId,
            string definitionId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) ||
                string.IsNullOrWhiteSpace(definitionId))
            {
                return null;
            }

            for (var index = 0; index < _pawnRecords.Count; index++)
            {
                var record = _pawnRecords[index];
                if (record != null &&
                    string.Equals(
                        record.PawnInstanceId,
                        pawnId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        record.DefinitionId,
                        definitionId,
                        StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        private static PawnHandoutRecordSnapshot CloneRecord(
            PawnHandoutRecordSnapshot record)
        {
            return record == null
                ? null
                : new PawnHandoutRecordSnapshot
                {
                    PawnInstanceId = record.PawnInstanceId,
                    DefinitionId = record.DefinitionId,
                    IsAvailable = record.IsAvailable,
                    HasOpened = record.HasOpened,
                    FirstOpenedUtc = record.FirstOpenedUtc,
                    LastOpenedUtc = record.LastOpenedUtc
                };
        }

        private static string ResolvePawnId(InteractivePawn pawn)
        {
            if (pawn == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(pawn.InstanceId))
                return pawn.InstanceId.Trim();

            var definitionId = pawn.Definition != null
                ? pawn.Definition.Id
                : string.Empty;
            return definitionId + "|" + pawn.name;
        }

        public PublicHandoutSnapshot CreateSnapshot()
        {
            var snapshot = new PublicHandoutSnapshot();
            for (var index = 0; index < _handouts.Count; index++)
            {
                var value = _handouts[index];
                snapshot.Handouts.Add(
                    new PublicHandoutEntrySnapshot
                    {
                        DefinitionId = value.DefinitionId,
                        HandoutNumber = value.HandoutNumber,
                        Description = value.Description
                    });
            }

            for (var index = 0; index < _pawnRecords.Count; index++)
            {
                var record = _pawnRecords[index];
                if (record == null)
                    continue;

                snapshot.PawnRecords.Add(
                    new PawnHandoutRecordSnapshot
                    {
                        PawnInstanceId = record.PawnInstanceId,
                        DefinitionId = record.DefinitionId,
                        IsAvailable = record.IsAvailable,
                        HasOpened = record.HasOpened,
                        FirstOpenedUtc = record.FirstOpenedUtc,
                        LastOpenedUtc = record.LastOpenedUtc
                    });
            }

            return snapshot;
        }

        public bool TryApplySnapshot(
            PublicHandoutSnapshot snapshot,
            out List<string> missingDefinitionIds,
            out string error)
        {
            missingDefinitionIds = new List<string>();
            error = string.Empty;
            if (snapshot == null)
            {
                error = "핸드아웃 Snapshot이 없습니다.";
                return false;
            }

            var source = snapshot.Handouts ??
                         new List<PublicHandoutEntrySnapshot>();
            var restored = new List<PublicHandoutRuntimeValue>(
                source.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < source.Count; index++)
            {
                var stored = source[index];
                if (stored == null ||
                    string.IsNullOrWhiteSpace(stored.DefinitionId))
                {
                    error = $"{index + 1}번째 핸드아웃 ID가 비어 있습니다.";
                    return false;
                }

                if (!ids.Add(stored.DefinitionId))
                {
                    error = "중복 핸드아웃 ID가 있습니다: " +
                            stored.DefinitionId;
                    return false;
                }

                HandoutDefinition definition = null;
                if (_catalog != null)
                {
                    _catalog.TryGetById(
                        stored.DefinitionId,
                        out definition);
                }

                if (definition == null)
                    missingDefinitionIds.Add(stored.DefinitionId);

                restored.Add(
                    definition != null
                        ? new PublicHandoutRuntimeValue(
                            definition,
                            definition.Id,
                            definition.HandoutNumber,
                            definition.Description)
                        : new PublicHandoutRuntimeValue(
                            null,
                            stored.DefinitionId,
                            stored.HandoutNumber,
                            stored.Description));
            }

            var restoredRecords =
                new List<PawnHandoutRecordSnapshot>();
            var recordKeys = new HashSet<string>(StringComparer.Ordinal);
            var recordSource = snapshot.PawnRecords ??
                new List<PawnHandoutRecordSnapshot>();
            for (var index = 0; index < recordSource.Count; index++)
            {
                var storedRecord = recordSource[index];
                if (storedRecord == null ||
                    string.IsNullOrWhiteSpace(
                        storedRecord.PawnInstanceId) ||
                    string.IsNullOrWhiteSpace(
                        storedRecord.DefinitionId))
                {
                    continue;
                }

                var key = storedRecord.PawnInstanceId + "\n" +
                          storedRecord.DefinitionId;
                if (!recordKeys.Add(key))
                    continue;

                restoredRecords.Add(
                    new PawnHandoutRecordSnapshot
                    {
                        PawnInstanceId = storedRecord.PawnInstanceId,
                        DefinitionId = storedRecord.DefinitionId,
                        IsAvailable = storedRecord.IsAvailable,
                        HasOpened = storedRecord.HasOpened,
                        FirstOpenedUtc = storedRecord.FirstOpenedUtc,
                        LastOpenedUtc = storedRecord.LastOpenedUtc
                    });
            }

            _handouts.Clear();
            _handouts.AddRange(restored);
            _pawnRecords.Clear();
            _pawnRecords.AddRange(restoredRecords);
            _isInitialized = true;
            Changed?.Invoke();
            return true;
        }
    }

    public sealed class PublicHandoutWidget : MonoBehaviour
    {
        private const float ListImageHeight = 150f;
        private const float DetailImageHeight = 410f;
        private const float MinimumCardWidth = 120f;
        private const float MaximumCardWidth = 300f;

        private Canvas _rootCanvas;
        private Font _font;
        private RectTransform _rootRect;
        private CanvasGroup _canvasGroup;
        private RectTransform _panelRect;
        private RectTransform _windowDragHandle;
        private RectTransform _contentRect;
        private Text _emptyText;

        private GameObject _catalogPanel;
        private RectTransform _catalogContent;
        private Text _catalogEmptyText;

        private GameObject _detailPanel;
        private RectTransform _detailPanelRect;
        private Text _detailTitle;
        private Image _detailImage;
        private Text _detailMissingText;
        private Text _detailDescription;

        private GameObject _contextPanel;
        private RectTransform _contextPanelRect;
        private Text _contextTitle;
        private string _contextDefinitionId;

        private readonly List<PublicHandoutRuntimeValue> _handouts =
            new List<PublicHandoutRuntimeValue>();
        private readonly List<HandoutDefinition> _availableCatalog =
            new List<HandoutDefinition>();

        private HandoutCatalogDefinition _catalog;
        private string _boundContextId = string.Empty;
        private string _dragDefinitionId;
        private int _dragTargetIndex = -1;
        private CanvasGroup _dragCanvasGroup;
        private bool _isVisible;
        private bool _isWindowDragging;
        private bool _hasUserMovedWindow;

        public event Action<HandoutDefinition> AddRequested;
        public event Action<string> RemoveRequested;
        public event Action<string, int> MoveRequested;
        public event Action<string> Opened;
        public event Action CloseRequested;

        public bool IsVisible => _isVisible;

        public static PublicHandoutWidget CreateRuntime(
            Canvas rootCanvas,
            Font font)
        {
            if (rootCanvas == null)
                throw new ArgumentNullException(nameof(rootCanvas));

            var root = new GameObject(
                "PublicHandoutWidget",
                typeof(RectTransform),
                typeof(CanvasGroup));
            var widget = root.AddComponent<PublicHandoutWidget>();
            widget.BuildRuntime(rootCanvas, font);
            return widget;
        }

        public void Bind(
            IReadOnlyList<PublicHandoutRuntimeValue> handouts,
            HandoutCatalogDefinition catalog,
            string contextId = null)
        {
            var normalizedContext = contextId ?? string.Empty;
            if (!string.Equals(
                    _boundContextId,
                    normalizedContext,
                    StringComparison.Ordinal))
            {
                _boundContextId = normalizedContext;
                HideCatalogPanel();
                HideDetailPanel();
                HideContextPanel();
                ResetCardDragVisual();
            }

            _catalog = catalog;
            _handouts.Clear();
            if (handouts != null)
            {
                for (var index = 0; index < handouts.Count; index++)
                    _handouts.Add(handouts[index]);
            }

            RebuildAvailableCatalog();
            RebuildCards();
            RebuildCatalogButtons();
        }

        public void Show(RectTransform anchorRect)
        {
            _isVisible = true;
            _rootRect.gameObject.SetActive(true);
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            Canvas.ForceUpdateCanvases();
            if (!_hasUserMovedWindow && anchorRect != null)
                PositionAboveAnchor(anchorRect);
            ClampPanelToRoot();
        }

        public void Hide()
        {
            _isVisible = false;
            HideCatalogPanel();
            HideDetailPanel();
            HideContextPanel();
            ResetCardDragVisual();
            _isWindowDragging = false;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _rootRect.gameObject.SetActive(false);
        }

        private void BuildRuntime(Canvas rootCanvas, Font font)
        {
            _rootCanvas = rootCanvas.rootCanvas != null
                ? rootCanvas.rootCanvas
                : rootCanvas;
            _font = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");

            _rootRect = GetComponent<RectTransform>();
            _rootRect.SetParent(_rootCanvas.transform, false);
            _rootRect.anchorMin = Vector2.zero;
            _rootRect.anchorMax = Vector2.one;
            _rootRect.offsetMin = Vector2.zero;
            _rootRect.offsetMax = Vector2.zero;

            _canvasGroup = GetComponent<CanvasGroup>();

            var panel = CreateImageObject(
                "HandoutPanel",
                _rootRect,
                new Color(0.035f, 0.05f, 0.065f, 0.98f));
            _panelRect = panel.rectTransform;
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(820f, 300f);
            _panelRect.anchoredPosition = Vector2.zero;

            BuildHeader();
            BuildList();
            BuildCatalogPanel();
            BuildDetailPanel();
            BuildContextPanel();
            Hide();
        }

        private void BuildHeader()
        {
            var header = CreateImageObject(
                "WindowDragHandle",
                _panelRect,
                new Color(0.055f, 0.075f, 0.09f, 1f));
            _windowDragHandle = header.rectTransform;
            _windowDragHandle.anchorMin = new Vector2(0f, 1f);
            _windowDragHandle.anchorMax = new Vector2(1f, 1f);
            _windowDragHandle.pivot = new Vector2(0.5f, 1f);
            _windowDragHandle.anchoredPosition = Vector2.zero;
            _windowDragHandle.sizeDelta = new Vector2(0f, 56f);

            var trigger = header.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.BeginDrag, BeginWindowDrag);
            AddTrigger(trigger, EventTriggerType.Drag, DragWindow);
            AddTrigger(trigger, EventTriggerType.EndDrag, EndWindowDrag);

            var title = CreateText(
                "Title",
                _windowDragHandle,
                "HANDOUTS",
                20,
                TextAnchor.MiddleCenter);
            title.raycastTarget = false;
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(80f, 0f);
            title.rectTransform.offsetMax = new Vector2(-120f, 0f);

            var add = CreateButton(
                "AddButton",
                _windowDragHandle,
                "+",
                24,
                new Color(0.10f, 0.28f, 0.34f, 1f));
            SetRect(
                add.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-62f, -11f),
                new Vector2(38f, 34f));
            add.onClick.AddListener(ShowCatalogPanel);

            var close = CreateButton(
                "CloseButton",
                _windowDragHandle,
                "×",
                24,
                new Color(0.18f, 0.08f, 0.08f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-18f, -11f),
                new Vector2(38f, 34f));
            close.onClick.AddListener(() => CloseRequested?.Invoke());
        }

        private void BuildList()
        {
            var viewport = CreateImageObject(
                "Viewport",
                _panelRect,
                new Color(0.02f, 0.03f, 0.04f, 0.94f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportRect = viewport.rectTransform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(16f, 16f);
            viewportRect.offsetMax = new Vector2(-16f, -66f);

            var content = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            _contentRect = content.GetComponent<RectTransform>();
            _contentRect.SetParent(viewportRect, false);
            _contentRect.anchorMin = new Vector2(0f, 0.5f);
            _contentRect.anchorMax = new Vector2(0f, 0.5f);
            _contentRect.pivot = new Vector2(0f, 0.5f);
            _contentRect.anchoredPosition = Vector2.zero;
            _contentRect.sizeDelta = Vector2.zero;

            var layout = content.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = _contentRect;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;

            _emptyText = CreateText(
                "EmptyText",
                viewportRect,
                "공개된 핸드아웃이 없습니다.\n+ 버튼으로 카탈로그에서 공개하십시오.",
                18,
                TextAnchor.MiddleCenter);
            _emptyText.color = new Color(0.72f, 0.78f, 0.82f, 1f);
            _emptyText.rectTransform.anchorMin = Vector2.zero;
            _emptyText.rectTransform.anchorMax = Vector2.one;
            _emptyText.rectTransform.offsetMin = Vector2.zero;
            _emptyText.rectTransform.offsetMax = Vector2.zero;
        }

        private void BuildCatalogPanel()
        {
            var panel = CreateImageObject(
                "CatalogPanel",
                _rootRect,
                new Color(0.035f, 0.05f, 0.065f, 0.995f));
            var rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(520f, 460f);
            rect.anchoredPosition = Vector2.zero;
            _catalogPanel = panel.gameObject;

            var title = CreateText(
                "Title",
                rect,
                "공개할 핸드아웃 선택",
                20,
                TextAnchor.MiddleCenter);
            SetRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -24f),
                new Vector2(380f, 42f));

            var close = CreateButton(
                "Close",
                rect,
                "×",
                23,
                new Color(0.18f, 0.08f, 0.08f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-20f, -20f),
                new Vector2(38f, 36f));
            close.onClick.AddListener(HideCatalogPanel);

            var viewport = CreateImageObject(
                "Viewport",
                rect,
                new Color(0.02f, 0.03f, 0.04f, 1f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportRect = viewport.rectTransform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(16f, 16f);
            viewportRect.offsetMax = new Vector2(-16f, -70f);

            var content = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            _catalogContent = content.GetComponent<RectTransform>();
            _catalogContent.SetParent(viewportRect, false);
            _catalogContent.anchorMin = new Vector2(0f, 1f);
            _catalogContent.anchorMax = new Vector2(1f, 1f);
            _catalogContent.pivot = new Vector2(0.5f, 1f);
            _catalogContent.anchoredPosition = Vector2.zero;
            _catalogContent.sizeDelta = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = _catalogContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            _catalogEmptyText = CreateText(
                "EmptyText",
                viewportRect,
                string.Empty,
                17,
                TextAnchor.MiddleCenter);
            _catalogEmptyText.rectTransform.anchorMin = Vector2.zero;
            _catalogEmptyText.rectTransform.anchorMax = Vector2.one;
            _catalogEmptyText.rectTransform.offsetMin = Vector2.zero;
            _catalogEmptyText.rectTransform.offsetMax = Vector2.zero;
            _catalogPanel.SetActive(false);
        }

        private void BuildDetailPanel()
        {
            var panel = CreateImageObject(
                "DetailPanel",
                _rootRect,
                new Color(0.025f, 0.035f, 0.045f, 0.998f));
            _detailPanelRect = panel.rectTransform;
            _detailPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _detailPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _detailPanelRect.pivot = new Vector2(0.5f, 0.5f);
            _detailPanelRect.sizeDelta = new Vector2(620f, 650f);
            _detailPanelRect.anchoredPosition = Vector2.zero;
            _detailPanel = panel.gameObject;

            _detailTitle = CreateText(
                "Title",
                _detailPanelRect,
                string.Empty,
                22,
                TextAnchor.MiddleCenter);
            SetRect(
                _detailTitle.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -25f),
                new Vector2(500f, 42f));

            var close = CreateButton(
                "Close",
                _detailPanelRect,
                "×",
                24,
                new Color(0.18f, 0.08f, 0.08f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-20f, -20f),
                new Vector2(38f, 36f));
            close.onClick.AddListener(HideDetailPanel);

            var imageBackground = CreateImageObject(
                "ImageBackground",
                _detailPanelRect,
                Color.black);
            var imageBackgroundRect = imageBackground.rectTransform;
            imageBackgroundRect.anchorMin = new Vector2(0.5f, 1f);
            imageBackgroundRect.anchorMax = new Vector2(0.5f, 1f);
            imageBackgroundRect.pivot = new Vector2(0.5f, 1f);
            imageBackgroundRect.anchoredPosition = new Vector2(0f, -62f);
            imageBackgroundRect.sizeDelta = new Vector2(
                580f,
                DetailImageHeight);

            var imageObject = CreateImageObject(
                "HandoutImage",
                imageBackgroundRect,
                Color.white);
            _detailImage = imageObject;
            _detailImage.preserveAspect = true;
            _detailImage.rectTransform.anchorMin = Vector2.zero;
            _detailImage.rectTransform.anchorMax = Vector2.one;
            _detailImage.rectTransform.offsetMin = new Vector2(8f, 8f);
            _detailImage.rectTransform.offsetMax = new Vector2(-8f, -8f);

            _detailMissingText = CreateText(
                "MissingImage",
                imageBackgroundRect,
                "이미지 누락",
                22,
                TextAnchor.MiddleCenter);
            _detailMissingText.color = new Color(0.9f, 0.35f, 0.35f, 1f);
            _detailMissingText.rectTransform.anchorMin = Vector2.zero;
            _detailMissingText.rectTransform.anchorMax = Vector2.one;
            _detailMissingText.rectTransform.offsetMin = Vector2.zero;
            _detailMissingText.rectTransform.offsetMax = Vector2.zero;

            var descriptionViewport = CreateImageObject(
                "DescriptionViewport",
                _detailPanelRect,
                new Color(0.045f, 0.06f, 0.075f, 1f));
            descriptionViewport.gameObject.AddComponent<RectMask2D>();
            var descriptionViewportRect = descriptionViewport.rectTransform;
            descriptionViewportRect.anchorMin = new Vector2(0f, 0f);
            descriptionViewportRect.anchorMax = new Vector2(1f, 0f);
            descriptionViewportRect.pivot = new Vector2(0.5f, 0f);
            descriptionViewportRect.anchoredPosition = new Vector2(0f, 18f);
            descriptionViewportRect.sizeDelta = new Vector2(-32f, 150f);

            _detailDescription = CreateText(
                "Description",
                descriptionViewportRect,
                string.Empty,
                18,
                TextAnchor.UpperLeft);
            _detailDescription.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _detailDescription.verticalOverflow =
                VerticalWrapMode.Overflow;
            _detailDescription.rectTransform.anchorMin =
                new Vector2(0f, 1f);
            _detailDescription.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            _detailDescription.rectTransform.pivot =
                new Vector2(0.5f, 1f);
            _detailDescription.rectTransform.anchoredPosition = Vector2.zero;
            _detailDescription.rectTransform.sizeDelta =
                new Vector2(-22f, 0f);

            var fitter = _detailDescription.gameObject.AddComponent<
                ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = descriptionViewport.gameObject.AddComponent<
                ScrollRect>();
            scroll.viewport = descriptionViewportRect;
            scroll.content = _detailDescription.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            _detailPanel.SetActive(false);
        }

        private void BuildContextPanel()
        {
            var panel = CreateImageObject(
                "ContextPanel",
                _rootRect,
                new Color(0.055f, 0.075f, 0.09f, 0.995f));
            _contextPanelRect = panel.rectTransform;
            _contextPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _contextPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _contextPanelRect.pivot = new Vector2(0.5f, 0.5f);
            _contextPanelRect.sizeDelta = new Vector2(260f, 150f);
            _contextPanel = panel.gameObject;

            _contextTitle = CreateText(
                "Title",
                _contextPanelRect,
                string.Empty,
                17,
                TextAnchor.MiddleCenter);
            SetRect(
                _contextTitle.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                new Vector2(230f, 46f));

            var remove = CreateButton(
                "Remove",
                _contextPanelRect,
                "공용 목록에서 제거",
                15,
                new Color(0.34f, 0.08f, 0.08f, 1f));
            SetRect(
                remove.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 68f),
                new Vector2(220f, 38f));
            remove.onClick.AddListener(SubmitRemove);

            var close = CreateButton(
                "Close",
                _contextPanelRect,
                "닫기",
                15,
                new Color(0.12f, 0.15f, 0.17f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 24f),
                new Vector2(220f, 34f));
            close.onClick.AddListener(HideContextPanel);
            _contextPanel.SetActive(false);
        }

        private void RebuildCards()
        {
            if (_contentRect == null)
                return;

            for (var index = _contentRect.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(_contentRect.GetChild(index).gameObject);
            }

            _emptyText.gameObject.SetActive(_handouts.Count == 0);
            for (var index = 0; index < _handouts.Count; index++)
                CreateCard(_handouts[index], index);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);
        }

        private void CreateCard(
            PublicHandoutRuntimeValue handout,
            int index)
        {
            var width = CalculateWidth(
                handout.Image,
                ListImageHeight,
                MinimumCardWidth,
                MaximumCardWidth);
            var card = new GameObject(
                "HandoutCard_" + index,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(EventTrigger));
            var rect = card.GetComponent<RectTransform>();
            rect.SetParent(_contentRect, false);
            rect.sizeDelta = new Vector2(width, 210f);
            card.GetComponent<Image>().color =
                new Color(0.08f, 0.12f, 0.15f, 1f);

            var number = CreateText(
                "Number",
                rect,
                "HANDOUT " + handout.HandoutNumber,
                16,
                TextAnchor.MiddleCenter);
            number.fontStyle = FontStyle.Bold;
            number.raycastTarget = false;
            number.rectTransform.anchorMin = new Vector2(0f, 1f);
            number.rectTransform.anchorMax = new Vector2(1f, 1f);
            number.rectTransform.pivot = new Vector2(0.5f, 1f);
            number.rectTransform.anchoredPosition = Vector2.zero;
            number.rectTransform.sizeDelta = new Vector2(0f, 38f);

            var imageBackground = CreateImageObject(
                "ImageBackground",
                rect,
                Color.black);
            imageBackground.raycastTarget = false;
            imageBackground.rectTransform.anchorMin = new Vector2(0f, 0f);
            imageBackground.rectTransform.anchorMax = new Vector2(1f, 0f);
            imageBackground.rectTransform.pivot = new Vector2(0.5f, 0f);
            imageBackground.rectTransform.anchoredPosition =
                new Vector2(0f, 10f);
            imageBackground.rectTransform.sizeDelta =
                new Vector2(-16f, ListImageHeight);

            if (handout.Image != null)
            {
                var image = CreateImageObject(
                    "Image",
                    imageBackground.rectTransform,
                    Color.white);
                image.sprite = handout.Image;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.rectTransform.anchorMin = Vector2.zero;
                image.rectTransform.anchorMax = Vector2.one;
                image.rectTransform.offsetMin = new Vector2(5f, 5f);
                image.rectTransform.offsetMax = new Vector2(-5f, -5f);
            }
            else
            {
                var missing = CreateText(
                    "Missing",
                    imageBackground.rectTransform,
                    "이미지 누락",
                    17,
                    TextAnchor.MiddleCenter);
                missing.color = new Color(0.9f, 0.35f, 0.35f, 1f);
                missing.raycastTarget = false;
                missing.rectTransform.anchorMin = Vector2.zero;
                missing.rectTransform.anchorMax = Vector2.one;
                missing.rectTransform.offsetMin = Vector2.zero;
                missing.rectTransform.offsetMax = Vector2.zero;
            }

            var trigger = card.GetComponent<EventTrigger>();
            AddTrigger(
                trigger,
                EventTriggerType.PointerClick,
                data => HandleCardClick(
                    handout,
                    data as PointerEventData));
            AddTrigger(
                trigger,
                EventTriggerType.BeginDrag,
                data => BeginCardDrag(
                    handout.DefinitionId,
                    index,
                    card));
            AddTrigger(
                trigger,
                EventTriggerType.PointerEnter,
                data =>
                {
                    if (!string.IsNullOrWhiteSpace(_dragDefinitionId))
                        _dragTargetIndex = index;
                });
            AddTrigger(
                trigger,
                EventTriggerType.EndDrag,
                data => EndCardDrag());
        }

        private void HandleCardClick(
            PublicHandoutRuntimeValue handout,
            PointerEventData pointer)
        {
            if (pointer == null)
                return;

            if (pointer.button == PointerEventData.InputButton.Right)
            {
                ShowContextPanel(handout, pointer.position);
                return;
            }

            if (pointer.button == PointerEventData.InputButton.Left)
                ShowDetailPanel(handout);
        }

        private void RebuildAvailableCatalog()
        {
            _availableCatalog.Clear();
            if (_catalog == null || _catalog.Handouts == null)
                return;

            for (var index = 0; index < _catalog.Handouts.Count; index++)
            {
                var definition = _catalog.Handouts[index];
                if (definition == null || IsRevealed(definition.Id))
                    continue;
                _availableCatalog.Add(definition);
            }
        }

        private bool IsRevealed(string definitionId)
        {
            for (var index = 0; index < _handouts.Count; index++)
            {
                if (string.Equals(
                        _handouts[index].DefinitionId,
                        definitionId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildCatalogButtons()
        {
            if (_catalogContent == null)
                return;

            for (var index = _catalogContent.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(_catalogContent.GetChild(index).gameObject);
            }

            _catalogEmptyText.gameObject.SetActive(
                _availableCatalog.Count == 0);
            _catalogEmptyText.text = _catalog == null
                ? "Handout Catalog Definition이 연결되지 않았습니다."
                : "추가로 공개할 핸드아웃이 없습니다.";

            for (var index = 0;
                 index < _availableCatalog.Count;
                 index++)
            {
                var definition = _availableCatalog[index];
                var button = CreateCatalogButton(definition);
                button.onClick.AddListener(
                    () =>
                    {
                        AddRequested?.Invoke(definition);
                        HideCatalogPanel();
                    });
            }
        }

        private Button CreateCatalogButton(HandoutDefinition definition)
        {
            var buttonObject = new GameObject(
                "Catalog_" + definition.Id,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(_catalogContent, false);
            rect.sizeDelta = new Vector2(0f, 76f);
            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 76f;
            layout.minHeight = 76f;

            var background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.07f, 0.14f, 0.17f, 1f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;

            var thumb = CreateImageObject(
                "Thumbnail",
                rect,
                Color.black);
            thumb.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            thumb.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            thumb.rectTransform.pivot = new Vector2(0f, 0.5f);
            thumb.rectTransform.anchoredPosition = new Vector2(8f, 0f);
            thumb.rectTransform.sizeDelta = new Vector2(92f, 62f);
            thumb.raycastTarget = false;

            if (definition.Image != null)
            {
                var image = CreateImageObject(
                    "Image",
                    thumb.rectTransform,
                    Color.white);
                image.sprite = definition.Image;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.rectTransform.anchorMin = Vector2.zero;
                image.rectTransform.anchorMax = Vector2.one;
                image.rectTransform.offsetMin = new Vector2(3f, 3f);
                image.rectTransform.offsetMax = new Vector2(-3f, -3f);
            }

            var label = CreateText(
                "Label",
                rect,
                "HANDOUT " + definition.HandoutNumber,
                18,
                TextAnchor.MiddleLeft);
            label.raycastTarget = false;
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(116f, 0f);
            label.rectTransform.offsetMax = new Vector2(-12f, 0f);
            return button;
        }

        private void ShowCatalogPanel()
        {
            RebuildAvailableCatalog();
            RebuildCatalogButtons();
            HideDetailPanel();
            HideContextPanel();
            _catalogPanel.SetActive(true);
            _catalogPanel.transform.SetAsLastSibling();
        }

        private void HideCatalogPanel()
        {
            if (_catalogPanel != null)
                _catalogPanel.SetActive(false);
        }

        private void ShowDetailPanel(PublicHandoutRuntimeValue handout)
        {
            Opened?.Invoke(handout.DefinitionId);
            HideCatalogPanel();
            HideContextPanel();

            _detailTitle.text = "HANDOUT " + handout.HandoutNumber;
            _detailDescription.text = string.IsNullOrWhiteSpace(
                    handout.Description)
                ? "설명이 없습니다."
                : handout.Description;
            _detailImage.sprite = handout.Image;
            _detailImage.gameObject.SetActive(handout.Image != null);
            _detailMissingText.gameObject.SetActive(handout.Image == null);

            var imageWidth = CalculateWidth(
                handout.Image,
                DetailImageHeight,
                320f,
                820f);
            _detailPanelRect.sizeDelta = new Vector2(
                Mathf.Clamp(imageWidth + 40f, 380f, 860f),
                650f);

            var imageBackground = _detailImage.transform.parent
                as RectTransform;
            if (imageBackground != null)
            {
                imageBackground.sizeDelta = new Vector2(
                    Mathf.Clamp(imageWidth, 340f, 820f),
                    DetailImageHeight);
            }

            _detailPanel.SetActive(true);
            _detailPanel.transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                _detailDescription.rectTransform);
        }

        private void HideDetailPanel()
        {
            if (_detailPanel != null)
                _detailPanel.SetActive(false);
        }

        private void ShowContextPanel(
            PublicHandoutRuntimeValue handout,
            Vector2 screenPosition)
        {
            HideCatalogPanel();
            _contextDefinitionId = handout.DefinitionId;
            _contextTitle.text = "HANDOUT " + handout.HandoutNumber;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rootRect,
                screenPosition,
                _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : _rootCanvas.worldCamera,
                out var localPoint);
            _contextPanelRect.anchoredPosition = localPoint;
            _contextPanel.SetActive(true);
            _contextPanel.transform.SetAsLastSibling();
            ClampRectToRoot(_contextPanelRect);
        }

        private void HideContextPanel()
        {
            _contextDefinitionId = string.Empty;
            if (_contextPanel != null)
                _contextPanel.SetActive(false);
        }

        private void SubmitRemove()
        {
            if (string.IsNullOrWhiteSpace(_contextDefinitionId))
                return;

            var id = _contextDefinitionId;
            HideContextPanel();
            RemoveRequested?.Invoke(id);
        }

        private void BeginCardDrag(
            string definitionId,
            int sourceIndex,
            GameObject card)
        {
            _dragDefinitionId = definitionId;
            _dragTargetIndex = sourceIndex;
            _dragCanvasGroup = card != null
                ? card.GetComponent<CanvasGroup>()
                : null;
            if (_dragCanvasGroup != null)
            {
                _dragCanvasGroup.alpha = 0.72f;
                _dragCanvasGroup.blocksRaycasts = false;
            }
        }

        private void EndCardDrag()
        {
            if (!string.IsNullOrWhiteSpace(_dragDefinitionId) &&
                _dragTargetIndex >= 0)
            {
                MoveRequested?.Invoke(
                    _dragDefinitionId,
                    _dragTargetIndex);
            }

            ResetCardDragVisual();
        }

        private void ResetCardDragVisual()
        {
            if (_dragCanvasGroup != null)
            {
                _dragCanvasGroup.alpha = 1f;
                _dragCanvasGroup.blocksRaycasts = true;
            }

            _dragDefinitionId = string.Empty;
            _dragTargetIndex = -1;
            _dragCanvasGroup = null;
        }

        private void BeginWindowDrag(BaseEventData data)
        {
            _isWindowDragging = data is PointerEventData;
            HideContextPanel();
        }

        private void DragWindow(BaseEventData data)
        {
            if (!_isWindowDragging || !(data is PointerEventData pointer))
                return;

            var scale = _rootCanvas != null
                ? Mathf.Max(0.001f, _rootCanvas.scaleFactor)
                : 1f;
            _panelRect.anchoredPosition += pointer.delta / scale;
            _hasUserMovedWindow = true;
            ClampPanelToRoot();
        }

        private void EndWindowDrag(BaseEventData data)
        {
            _isWindowDragging = false;
            ClampPanelToRoot();
        }

        private void PositionAboveAnchor(RectTransform anchorRect)
        {
            if (anchorRect == null || _panelRect == null)
                return;

            var screenPoint = RectTransformUtility.WorldToScreenPoint(
                _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : _rootCanvas.worldCamera,
                anchorRect.TransformPoint(anchorRect.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rootRect,
                screenPoint,
                _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : _rootCanvas.worldCamera,
                out var localPoint);

            _panelRect.anchoredPosition = localPoint +
                new Vector2(
                    -_panelRect.rect.width * 0.5f +
                    anchorRect.rect.width * 0.5f,
                    anchorRect.rect.height * 0.5f +
                    _panelRect.rect.height * 0.5f + 14f);
        }

        private void ClampPanelToRoot()
        {
            ClampRectToRoot(_panelRect);
        }

        private void ClampRectToRoot(RectTransform target)
        {
            if (target == null || _rootRect == null)
                return;

            var rootSize = _rootRect.rect.size;
            var targetSize = target.rect.size;
            var halfRoot = rootSize * 0.5f;
            var halfTarget = targetSize * 0.5f;
            var position = target.anchoredPosition;
            position.x = Mathf.Clamp(
                position.x,
                -halfRoot.x + halfTarget.x,
                halfRoot.x - halfTarget.x);
            position.y = Mathf.Clamp(
                position.y,
                -halfRoot.y + halfTarget.y,
                halfRoot.y - halfTarget.y);
            target.anchoredPosition = position;
        }

        private static float CalculateWidth(
            Sprite sprite,
            float height,
            float minimum,
            float maximum)
        {
            if (sprite == null || sprite.rect.height <= 0.001f)
                return minimum;

            var aspect = sprite.rect.width / sprite.rect.height;
            return Mathf.Clamp(height * aspect, minimum, maximum);
        }

        private Image CreateImageObject(
            string objectName,
            RectTransform parent,
            Color color)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private Text CreateText(
            string objectName,
            RectTransform parent,
            string value,
            int fontSize,
            TextAnchor alignment)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = value;
            text.supportRichText = false;
            return text;
        }

        private Button CreateButton(
            string objectName,
            RectTransform parent,
            string label,
            int fontSize,
            Color color)
        {
            var root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = color;
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;

            var text = CreateText(
                "Label",
                rect,
                label,
                fontSize,
                TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4f, 2f);
            text.rectTransform.offsetMax = new Vector2(-4f, -2f);
            return button;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void AddTrigger(
            EventTrigger trigger,
            EventTriggerType type,
            Action<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry
            {
                eventID = type
            };
            entry.callback.AddListener(data => callback?.Invoke(data));
            trigger.triggers.Add(entry);
        }
    }
}
