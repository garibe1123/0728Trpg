using System;
using System.Collections.Generic;
using Trpg.Data.Inventory;
using Trpg.Pawns;
using Trpg.UI.Stats;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Data.Inventory
{
    public enum InventoryItemType
    {
        Food,
        Drink,
        Medical,
        Handgun,
        LongGun,
        Ammunition,
        MeleeWeapon,
        Explosive,
        Key,
        Document,
        Light,
        Tool,
        Electronic,
        Valuable,
        ProtectiveGear,
        Clothing,
        Bag,
        Relic,
        Aberration
    }

    public static class InventoryItemTypeUtility
    {
        public static string GetDisplayName(InventoryItemType type)
        {
            switch (type)
            {
                case InventoryItemType.Food: return "음식";
                case InventoryItemType.Drink: return "음료";
                case InventoryItemType.Medical: return "의료품";
                case InventoryItemType.Handgun: return "권총";
                case InventoryItemType.LongGun: return "장총";
                case InventoryItemType.Ammunition: return "탄약";
                case InventoryItemType.MeleeWeapon: return "근접무기";
                case InventoryItemType.Explosive: return "폭발물";
                case InventoryItemType.Key: return "열쇠";
                case InventoryItemType.Document: return "문서";
                case InventoryItemType.Light: return "조명";
                case InventoryItemType.Tool: return "공구";
                case InventoryItemType.Electronic: return "전자기기";
                case InventoryItemType.Valuable: return "귀중품";
                case InventoryItemType.ProtectiveGear: return "보호구";
                case InventoryItemType.Clothing: return "의류";
                case InventoryItemType.Bag: return "가방";
                case InventoryItemType.Relic: return "성물";
                case InventoryItemType.Aberration: return "괴이물";
                default: return type.ToString();
            }
        }

        public static string GetCompactLabel(InventoryItemType type)
        {
            switch (type)
            {
                case InventoryItemType.Food: return "음식";
                case InventoryItemType.Drink: return "음료";
                case InventoryItemType.Medical: return "의료";
                case InventoryItemType.Handgun: return "권총";
                case InventoryItemType.LongGun: return "장총";
                case InventoryItemType.Ammunition: return "탄약";
                case InventoryItemType.MeleeWeapon: return "근접";
                case InventoryItemType.Explosive: return "폭발";
                case InventoryItemType.Key: return "열쇠";
                case InventoryItemType.Document: return "문서";
                case InventoryItemType.Light: return "조명";
                case InventoryItemType.Tool: return "공구";
                case InventoryItemType.Electronic: return "전자";
                case InventoryItemType.Valuable: return "귀중";
                case InventoryItemType.ProtectiveGear: return "보호";
                case InventoryItemType.Clothing: return "의류";
                case InventoryItemType.Bag: return "가방";
                case InventoryItemType.Relic: return "성물";
                case InventoryItemType.Aberration: return "괴이";
                default: return "아이템";
            }
        }
    }

    [CreateAssetMenu(
        menuName = "Trpg/Inventory/Item Definition",
        fileName = "ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("세이브와 네트워크에서 사용하는 고유 아이템 ID")]
        private string _id = "item.new";

        [SerializeField, Tooltip("슬롯 아이콘 종류")]
        private InventoryItemType _type = InventoryItemType.Document;

        [SerializeField, Tooltip("우클릭 상세창에 표시할 아이템 이름")]
        private string _displayName = "새 아이템";

        [SerializeField, Min(1), Tooltip("SO를 추가할 때 사용할 기본 개수")]
        private int _defaultQuantity = 1;

        [SerializeField, Min(0f), Tooltip("아이템 한 개의 무게")]
        private float _unitWeight;

        public string Id => _id;
        public InventoryItemType Type => _type;
        public string DisplayName => _displayName;
        public int DefaultQuantity => Mathf.Max(1, _defaultQuantity);
        public float UnitWeight => Mathf.Max(0f, _unitWeight);

#if UNITY_EDITOR
        private void OnValidate()
        {
            _defaultQuantity = Mathf.Max(1, _defaultQuantity);
            _unitWeight = Mathf.Max(0f, _unitWeight);

            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError(
                    $"[{name}] Item Definition Id가 비어 있습니다.",
                    this);
            }

            if (string.IsNullOrWhiteSpace(_displayName))
            {
                Debug.LogError(
                    $"[{name}] 아이템 표시 이름이 비어 있습니다.",
                    this);
            }
        }
#endif
    }

    [CreateAssetMenu(
        menuName = "Trpg/Inventory/Item Catalog Definition",
        fileName = "ItemCatalogDefinition")]
    public sealed class ItemCatalogDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("카탈로그 고유 ID")]
        private string _id = "item_catalog.default";

        [SerializeField]
        private List<ItemDefinition> _items = new List<ItemDefinition>();

        public string Id => _id;
        public IReadOnlyList<ItemDefinition> Items => _items;

        public bool TryGetById(string itemId, out ItemDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                if (item != null && string.Equals(
                        item.Id,
                        itemId,
                        StringComparison.Ordinal))
                {
                    definition = item;
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
                    $"[{name}] Item Catalog Id가 비어 있습니다.",
                    this);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                if (item == null)
                    continue;

                if (!ids.Add(item.Id))
                {
                    Debug.LogError(
                        $"[{name}] 중복 Item Id: {item.Id}",
                        this);
                }
            }
        }
#endif
    }

    [Serializable]
    public sealed class InventoryTypeIconRecord
    {
        [SerializeField] private InventoryItemType _type;
        [SerializeField] private Sprite _icon;

        public InventoryItemType Type => _type;
        public Sprite Icon => _icon;
    }

    [CreateAssetMenu(
        menuName = "Trpg/Inventory/Inventory Icon Set Definition",
        fileName = "InventoryIconSetDefinition")]
    public sealed class InventoryIconSetDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("아이콘 세트 고유 ID")]
        private string _id = "inventory_icons.default";

        [SerializeField]
        private List<InventoryTypeIconRecord> _icons =
            new List<InventoryTypeIconRecord>();

        public string Id => _id;

        public Sprite GetIcon(InventoryItemType type)
        {
            for (var index = 0; index < _icons.Count; index++)
            {
                var record = _icons[index];
                if (record != null && record.Type == type)
                    return record.Icon;
            }

            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogError(
                    $"[{name}] Inventory Icon Set Id가 비어 있습니다.",
                    this);
            }

            var types = new HashSet<InventoryItemType>();
            for (var index = 0; index < _icons.Count; index++)
            {
                var record = _icons[index];
                if (record == null)
                    continue;

                if (!types.Add(record.Type))
                {
                    Debug.LogError(
                        $"[{name}] 중복 아이콘 종류: {record.Type}",
                        this);
                }
            }
        }
#endif
    }
}

namespace Trpg.UI.Inventory
{
    public readonly struct InventoryRuntimeValue
    {
        public InventoryRuntimeValue(
            ItemDefinition definition,
            string runtimeId,
            string definitionId,
            InventoryItemType type,
            string displayName,
            int quantity,
            float unitWeight,
            bool isCustom)
        {
            Definition = definition;
            RuntimeId = runtimeId ?? string.Empty;
            DefinitionId = definitionId ?? string.Empty;
            Type = type;
            DisplayName = NormalizeName(displayName);
            Quantity = Mathf.Max(1, quantity);
            UnitWeight = Mathf.Max(0f, unitWeight);
            IsCustom = isCustom;
        }

        public ItemDefinition Definition { get; }
        public string RuntimeId { get; }
        public string DefinitionId { get; }
        public InventoryItemType Type { get; }
        public string DisplayName { get; }
        public int Quantity { get; }
        public float UnitWeight { get; }
        public bool IsCustom { get; }
        public float TotalWeight => UnitWeight * Quantity;

        public InventoryRuntimeValue WithQuantity(int quantity)
        {
            return new InventoryRuntimeValue(
                Definition,
                RuntimeId,
                DefinitionId,
                Type,
                DisplayName,
                quantity,
                UnitWeight,
                IsCustom);
        }

        private static string NormalizeName(string value)
        {
            var normalized = value != null ? value.Trim() : string.Empty;
            return string.IsNullOrWhiteSpace(normalized)
                ? "이름 없는 아이템"
                : normalized;
        }
    }

    [Serializable]
    public sealed class InventoryItemSnapshot
    {
        public string RuntimeId;
        public string DefinitionId;
        public InventoryItemType Type;
        public string DisplayName;
        public int Quantity;
        public float UnitWeight;
        public bool IsCustom;
    }

    [Serializable]
    public sealed class InventoryRuntimeSnapshot
    {
        public string CharacterDefinitionId;
        public List<InventoryItemSnapshot> Items =
            new List<InventoryItemSnapshot>();
    }

    [DisallowMultipleComponent]
    public sealed class PlayerInventoryState : MonoBehaviour
    {
        private readonly List<InventoryRuntimeValue> _items =
            new List<InventoryRuntimeValue>();

        private InteractivePawnDefinition _definition;
        private ItemCatalogDefinition _catalog;
        private bool _isInitialized;

        public event Action Changed;

        public InteractivePawnDefinition Definition => _definition;
        public ItemCatalogDefinition Catalog => _catalog;
        public IReadOnlyList<InventoryRuntimeValue> Items => _items;
        public bool IsInitialized => _isInitialized;

        public float CurrentWeight
        {
            get
            {
                var total = 0f;
                for (var index = 0; index < _items.Count; index++)
                    total += _items[index].TotalWeight;
                return Mathf.Max(0f, total);
            }
        }

        public bool Configure(
            InteractivePawnDefinition definition,
            ItemCatalogDefinition catalog = null)
        {
            if (definition == null)
                return false;

            if (_definition != null &&
                !ReferenceEquals(_definition, definition) &&
                _isInitialized)
            {
                return false;
            }

            _definition = definition;
            if (catalog != null)
                _catalog = catalog;
            return true;
        }

        public void Initialize()
        {
            if (_isInitialized || _definition == null)
                return;

            _items.Clear();
            _isInitialized = true;
            Changed?.Invoke();
        }

        public bool TryAdd(
            ItemDefinition definition,
            int quantity,
            out string runtimeId,
            out string error)
        {
            runtimeId = string.Empty;
            error = string.Empty;
            if (!EnsureInitialized())
            {
                error = "인벤토리 상태가 초기화되지 않았습니다.";
                return false;
            }

            if (definition == null ||
                string.IsNullOrWhiteSpace(definition.Id))
            {
                error = "추가할 Item Definition이 유효하지 않습니다.";
                return false;
            }

            var addedQuantity = Mathf.Max(1, quantity);
            for (var index = 0; index < _items.Count; index++)
            {
                var current = _items[index];
                if (!current.IsCustom && string.Equals(
                        current.DefinitionId,
                        definition.Id,
                        StringComparison.Ordinal))
                {
                    _items[index] = current.WithQuantity(
                        current.Quantity + addedQuantity);
                    runtimeId = current.RuntimeId;
                    Changed?.Invoke();
                    return true;
                }
            }

            runtimeId = CreateRuntimeId();
            _items.Add(
                new InventoryRuntimeValue(
                    definition,
                    runtimeId,
                    definition.Id,
                    definition.Type,
                    definition.DisplayName,
                    addedQuantity,
                    definition.UnitWeight,
                    false));
            Changed?.Invoke();
            return true;
        }

        public bool TryAddCustom(
            InventoryItemType type,
            string displayName,
            int quantity,
            float unitWeight,
            out string runtimeId,
            out string error)
        {
            runtimeId = string.Empty;
            error = string.Empty;
            if (!EnsureInitialized())
            {
                error = "인벤토리 상태가 초기화되지 않았습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                error = "아이템 이름이 비어 있습니다.";
                return false;
            }

            if (unitWeight < 0f || float.IsNaN(unitWeight) ||
                float.IsInfinity(unitWeight))
            {
                error = "아이템 무게가 유효하지 않습니다.";
                return false;
            }

            runtimeId = CreateRuntimeId();
            _items.Add(
                new InventoryRuntimeValue(
                    null,
                    runtimeId,
                    string.Empty,
                    type,
                    displayName,
                    Mathf.Max(1, quantity),
                    unitWeight,
                    true));
            Changed?.Invoke();
            return true;
        }

        public bool TrySetQuantity(string runtimeId, int quantity)
        {
            if (!EnsureInitialized() ||
                string.IsNullOrWhiteSpace(runtimeId))
            {
                return false;
            }

            var clamped = Mathf.Max(1, quantity);
            for (var index = 0; index < _items.Count; index++)
            {
                var current = _items[index];
                if (!string.Equals(
                        current.RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (current.Quantity == clamped)
                    return true;

                _items[index] = current.WithQuantity(clamped);
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        public bool TryRemove(string runtimeId)
        {
            if (!EnsureInitialized() ||
                string.IsNullOrWhiteSpace(runtimeId))
            {
                return false;
            }

            for (var index = 0; index < _items.Count; index++)
            {
                if (!string.Equals(
                        _items[index].RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                _items.RemoveAt(index);
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>
        /// targetIndex 위치에 source 아이템을 삽입합니다.
        /// 예: [1,2,3,4]에서 4를 1 위치로 옮기면 [4,1,2,3].
        /// </summary>
        public bool TryMove(string runtimeId, int targetIndex)
        {
            if (!EnsureInitialized() ||
                string.IsNullOrWhiteSpace(runtimeId) ||
                _items.Count <= 1)
            {
                return false;
            }

            var sourceIndex = -1;
            for (var index = 0; index < _items.Count; index++)
            {
                if (string.Equals(
                        _items[index].RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    sourceIndex = index;
                    break;
                }
            }

            if (sourceIndex < 0)
                return false;

            targetIndex = Mathf.Clamp(targetIndex, 0, _items.Count - 1);
            if (sourceIndex == targetIndex)
                return true;

            var moving = _items[sourceIndex];
            _items.RemoveAt(sourceIndex);
            targetIndex = Mathf.Clamp(targetIndex, 0, _items.Count);
            _items.Insert(targetIndex, moving);
            Changed?.Invoke();
            return true;
        }

        public float CalculateCapacity(
            PlayerStatState statState,
            string statId,
            float multiplier,
            float fallbackCapacity)
        {
            var fallback = Mathf.Max(0f, fallbackCapacity);
            var normalizedId = statId != null ? statId.Trim() : string.Empty;
            if (statState == null ||
                !statState.IsInitialized ||
                statState.Runtime == null ||
                string.IsNullOrWhiteSpace(normalizedId) ||
                !statState.Runtime.TryGetDefinition(normalizedId, out _))
            {
                return fallback;
            }

            var statValue = statState.Runtime.GetNumber(normalizedId);
            if (double.IsNaN(statValue) || double.IsInfinity(statValue))
                return fallback;

            return Mathf.Max(
                0f,
                (float)statValue * Mathf.Max(0f, multiplier));
        }

        public InventoryRuntimeSnapshot CreateSnapshot()
        {
            EnsureInitialized();
            var snapshot = new InventoryRuntimeSnapshot
            {
                CharacterDefinitionId =
                    _definition != null ? _definition.Id : string.Empty
            };

            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                snapshot.Items.Add(
                    new InventoryItemSnapshot
                    {
                        RuntimeId = item.RuntimeId,
                        DefinitionId = item.DefinitionId,
                        Type = item.Type,
                        DisplayName = item.DisplayName,
                        Quantity = item.Quantity,
                        UnitWeight = item.UnitWeight,
                        IsCustom = item.IsCustom
                    });
            }

            return snapshot;
        }

        public bool TryApplySnapshot(
            InventoryRuntimeSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (_definition == null)
            {
                error = "캐릭터 정의가 연결되지 않았습니다.";
                return false;
            }

            if (snapshot == null || snapshot.Items == null)
            {
                error = "인벤토리 Snapshot이 비어 있습니다.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    snapshot.CharacterDefinitionId) &&
                !string.Equals(
                    snapshot.CharacterDefinitionId,
                    _definition.Id,
                    StringComparison.Ordinal))
            {
                error = "다른 캐릭터 정의의 인벤토리 Snapshot입니다.";
                return false;
            }

            var restored = new List<InventoryRuntimeValue>(
                snapshot.Items.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < snapshot.Items.Count; index++)
            {
                var stored = snapshot.Items[index];
                if (stored == null ||
                    string.IsNullOrWhiteSpace(stored.RuntimeId) ||
                    !ids.Add(stored.RuntimeId))
                {
                    error = "인벤토리 Snapshot에 비어 있거나 중복된 RuntimeId가 있습니다.";
                    return false;
                }

                if (stored.Quantity <= 0 ||
                    stored.UnitWeight < 0f ||
                    float.IsNaN(stored.UnitWeight) ||
                    float.IsInfinity(stored.UnitWeight))
                {
                    error = $"유효하지 않은 아이템 수량 또는 무게입니다: {stored.RuntimeId}";
                    return false;
                }

                // 저장 데이터가 인벤토리의 실제 원본입니다.
                // 현재 씬이나 Item Catalog에 같은 SO가 없어도,
                // 저장된 종류·이름·개수·무게로 런타임 아이템을 생성합니다.
                restored.Add(
                    new InventoryRuntimeValue(
                        null,
                        stored.RuntimeId,
                        stored.DefinitionId,
                        stored.Type,
                        stored.DisplayName,
                        stored.Quantity,
                        stored.UnitWeight,
                        stored.IsCustom));
            }

            _items.Clear();
            _items.AddRange(restored);
            _isInitialized = true;
            Changed?.Invoke();
            return true;
        }

        public static PlayerInventoryState ResolveOrCreate(
            GameObject selectedObject,
            InteractivePawnDefinition definition,
            ItemCatalogDefinition catalog = null)
        {
            if (selectedObject == null || definition == null)
                return null;

            var pawn = ResolveInteractivePawn(selectedObject);
            var root = pawn != null ? pawn.gameObject : selectedObject;

            var state = root.GetComponent<PlayerInventoryState>();
            if (state == null)
            {
                state = root.GetComponentInChildren<
                    PlayerInventoryState>(true);
            }
            if (state == null)
                state = root.AddComponent<PlayerInventoryState>();

            if (!state.Configure(definition, catalog))
                return null;

            state.Initialize();
            return state.IsInitialized ? state : null;
        }

        private bool EnsureInitialized()
        {
            if (!_isInitialized)
                Initialize();
            return _isInitialized;
        }

        private string CreateRuntimeId()
        {
            string id;
            do
            {
                id = $"item.runtime.{Guid.NewGuid():N}";
            }
            while (ContainsRuntimeId(id));

            return id;
        }

        private bool ContainsRuntimeId(string runtimeId)
        {
            for (var index = 0; index < _items.Count; index++)
            {
                if (string.Equals(
                        _items[index].RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static InteractivePawn ResolveInteractivePawn(
            GameObject selectedObject)
        {
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

        private void OnDestroy()
        {
            Changed = null;
        }
    }
}

namespace Trpg.UI.Inventory
{
    public readonly struct InventoryItemDraft
    {
        public InventoryItemDraft(
            ItemDefinition definition,
            InventoryItemType type,
            string displayName,
            int quantity,
            float unitWeight)
        {
            Definition = definition;
            Type = type;
            DisplayName = displayName ?? string.Empty;
            Quantity = Mathf.Max(1, quantity);
            UnitWeight = Mathf.Max(0f, unitWeight);
        }

        public ItemDefinition Definition { get; }
        public InventoryItemType Type { get; }
        public string DisplayName { get; }
        public int Quantity { get; }
        public float UnitWeight { get; }
    }

    [DisallowMultipleComponent]
    public sealed class PawnInventoryWidget : MonoBehaviour
    {
        private const int ColumnCount = 3;
        private const float PanelEdgePadding = 10f;
        private static readonly Vector2 SlotSize = new Vector2(104f, 104f);

        private RectTransform _rootRect;
        private Canvas _rootCanvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _panelRect;
        private RectTransform _contentRect;
        private RectTransform _windowDragHandle;
        private RectTransform _viewportRect;
        private Text _weightText;
        private Text _titleText;
        private Text _emptyText;
        private Button _addButton;
        private Button _closeButton;
        private Font _font;
        private bool _isEmbedded;
        private RectTransform _legacyRootParent;
        private Vector2 _legacyRootAnchorMin;
        private Vector2 _legacyRootAnchorMax;
        private Vector2 _legacyRootPivot;
        private Vector2 _legacyRootAnchoredPosition;
        private Vector2 _legacyRootSizeDelta;
        private Vector2 _legacyPanelAnchorMin;
        private Vector2 _legacyPanelAnchorMax;
        private Vector2 _legacyPanelPivot;
        private Vector2 _legacyPanelAnchoredPosition;
        private Vector2 _legacyPanelSizeDelta;

        private GameObject _addPanel;
        private Button _catalogModeButton;
        private Button _customModeButton;
        private Button _catalogSelectButton;
        private Text _catalogSelectLabel;
        private Button _typeSelectButton;
        private Text _typeSelectLabel;
        private InputField _nameInput;
        private InputField _quantityInput;
        private InputField _weightInput;
        private Text _addErrorText;
        private GameObject _catalogPopup;
        private RectTransform _catalogPopupContent;
        private GameObject _typePopup;

        private GameObject _detailPanel;
        private RectTransform _detailPanelRect;
        private Text _detailNameText;
        private Text _detailTypeText;
        private Text _detailWeightText;
        private InputField _detailQuantityInput;
        private string _detailRuntimeId;

        private readonly List<InventoryRuntimeValue> _items =
            new List<InventoryRuntimeValue>();
        private readonly List<ItemDefinition> _catalogItems =
            new List<ItemDefinition>();

        private ItemCatalogDefinition _catalog;
        private InventoryIconSetDefinition _iconSet;
        private int _catalogSelectionIndex = -1;
        private InventoryItemType _customType = InventoryItemType.Food;
        private bool _usesCatalogMode;
        private string _dragRuntimeId;
        private int _dragTargetIndex = -1;
        private CanvasGroup _dragCanvasGroup;
        private bool _isVisible;
        private bool _isWindowDragging;
        private bool _hasUserMovedWindow;

        public event Action<InventoryItemDraft> AddRequested;
        public event Action<string> RemoveRequested;
        public event Action<string, int> QuantityChangedRequested;
        public event Action<string, int> MoveRequested;
        public event Action CloseRequested;

        public bool IsVisible => _isVisible;
        public bool IsEmbedded => _isEmbedded;
        public RectTransform RootRect => _rootRect;
        public RectTransform PanelRect => _panelRect;

        public static PawnInventoryWidget CreateRuntime(
            RectTransform parentRect,
            Font font)
        {
            if (parentRect == null)
                throw new ArgumentNullException(nameof(parentRect));

            var root = new GameObject(
                "PawnInventoryWidget",
                typeof(RectTransform),
                typeof(CanvasGroup));
            var widget = root.AddComponent<PawnInventoryWidget>();
            widget.BuildRuntime(parentRect, font);
            return widget;
        }

        public void SetEmbeddedMode(
            RectTransform host,
            bool enabled)
        {
            if (_rootRect == null || _panelRect == null)
                return;

            if (enabled)
            {
                if (host == null)
                    throw new ArgumentNullException(nameof(host));

                if (!_isEmbedded)
                {
                    _legacyRootParent = _rootRect.parent as RectTransform;
                    _legacyRootAnchorMin = _rootRect.anchorMin;
                    _legacyRootAnchorMax = _rootRect.anchorMax;
                    _legacyRootPivot = _rootRect.pivot;
                    _legacyRootAnchoredPosition = _rootRect.anchoredPosition;
                    _legacyRootSizeDelta = _rootRect.sizeDelta;
                    _legacyPanelAnchorMin = _panelRect.anchorMin;
                    _legacyPanelAnchorMax = _panelRect.anchorMax;
                    _legacyPanelPivot = _panelRect.pivot;
                    _legacyPanelAnchoredPosition = _panelRect.anchoredPosition;
                    _legacyPanelSizeDelta = _panelRect.sizeDelta;
                }

                _isEmbedded = true;
                _rootRect.SetParent(host, false);
                StretchRect(_rootRect);
                StretchRect(_panelRect);
                ApplyEmbeddedLayout();
                return;
            }

            if (!_isEmbedded)
                return;

            _isEmbedded = false;
            if (_legacyRootParent != null)
                _rootRect.SetParent(_legacyRootParent, false);
            _rootRect.anchorMin = _legacyRootAnchorMin;
            _rootRect.anchorMax = _legacyRootAnchorMax;
            _rootRect.pivot = _legacyRootPivot;
            _rootRect.anchoredPosition = _legacyRootAnchoredPosition;
            _rootRect.sizeDelta = _legacyRootSizeDelta;
            _panelRect.anchorMin = _legacyPanelAnchorMin;
            _panelRect.anchorMax = _legacyPanelAnchorMax;
            _panelRect.pivot = _legacyPanelPivot;
            _panelRect.anchoredPosition = _legacyPanelAnchoredPosition;
            _panelRect.sizeDelta = _legacyPanelSizeDelta;
            ApplyFloatingLayout();
        }

        public void Bind(
            IReadOnlyList<InventoryRuntimeValue> items,
            float currentWeight,
            float capacity,
            ItemCatalogDefinition catalog,
            InventoryIconSetDefinition iconSet)
        {
            _catalog = catalog;
            _iconSet = iconSet;
            _items.Clear();
            if (items != null)
            {
                for (var index = 0; index < items.Count; index++)
                    _items.Add(items[index]);
            }

            RebuildCatalogItems();
            RefreshWeight(currentWeight, capacity);
            RebuildSlots();

            if (!string.IsNullOrWhiteSpace(_detailRuntimeId))
            {
                if (TryGetItem(_detailRuntimeId, out var detailItem))
                    BindDetail(detailItem);
                else
                    HideDetailPanel();
            }
        }

        public void Show()
        {
            Show(null);
        }

        public void Show(RectTransform anchorRect)
        {
            _isVisible = true;
            if (_rootRect != null)
                _rootRect.gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }

            Canvas.ForceUpdateCanvases();
            if (!_isEmbedded)
            {
                if (!_hasUserMovedWindow && anchorRect != null)
                    PositionAboveAnchor(anchorRect);
                ClampPanelToRoot();
            }
        }

        public void Hide()
        {
            _isVisible = false;
            HideAddPanel();
            HideDetailPanel();
            HideSelectionPopups();
            ResetItemDragVisual();
            _isWindowDragging = false;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            if (_rootRect != null)
                _rootRect.gameObject.SetActive(false);
        }

        private void BuildRuntime(RectTransform parentRect, Font font)
        {
            _rootCanvas = parentRect.GetComponentInParent<Canvas>();
            _font = font != null
                ? font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _rootRect = GetComponent<RectTransform>();
            _rootRect.SetParent(parentRect, false);
            _rootRect.anchorMin = Vector2.zero;
            _rootRect.anchorMax = Vector2.one;
            _rootRect.offsetMin = Vector2.zero;
            _rootRect.offsetMax = Vector2.zero;

            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            var panel = CreateImageObject(
                "InventoryPanel",
                _rootRect,
                new Color(0.035f, 0.05f, 0.065f, 0.98f));
            _panelRect = panel.rectTransform;
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(390f, 480f);
            _panelRect.anchoredPosition = Vector2.zero;

            BuildWindowHeader();
            BuildScrollArea();
            BuildAddPanel();
            BuildDetailPanel();
            Hide();
        }

        private void BuildWindowHeader()
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
            _windowDragHandle.sizeDelta = new Vector2(0f, 62f);

            var dragTrigger = header.gameObject.AddComponent<EventTrigger>();
            AddTrigger(
                dragTrigger,
                EventTriggerType.BeginDrag,
                BeginWindowDrag);
            AddTrigger(
                dragTrigger,
                EventTriggerType.Drag,
                DragWindow);
            AddTrigger(
                dragTrigger,
                EventTriggerType.EndDrag,
                EndWindowDrag);

            _weightText = CreateText(
                "WeightText",
                _panelRect,
                "[0.0 / 0.0]",
                18,
                TextAnchor.MiddleLeft);
            SetRect(
                _weightText.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(18f, -16f),
                new Vector2(180f, 34f));
            _weightText.raycastTarget = false;

            _titleText = CreateText(
                "Title",
                _panelRect,
                "INVENTORY",
                20,
                TextAnchor.MiddleCenter);
            SetRect(
                _titleText.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -16f),
                new Vector2(140f, 34f));
            _titleText.raycastTarget = false;

            _addButton = CreateButton(
                "AddButton",
                _panelRect,
                "+",
                24,
                new Color(0.10f, 0.28f, 0.34f, 1f));
            SetRect(
                _addButton.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-62f, -16f),
                new Vector2(38f, 34f));
            _addButton.onClick.AddListener(ShowAddPanel);

            _closeButton = CreateButton(
                "CloseButton",
                _panelRect,
                "×",
                24,
                new Color(0.18f, 0.08f, 0.08f, 1f));
            SetRect(
                _closeButton.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-18f, -16f),
                new Vector2(38f, 34f));
            _closeButton.onClick.AddListener(
                () => CloseRequested?.Invoke());
        }

        private void BuildScrollArea()
        {
            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D));
            _viewportRect = viewportObject.GetComponent<RectTransform>();
            _viewportRect.SetParent(_panelRect, false);
            _viewportRect.anchorMin = Vector2.zero;
            _viewportRect.anchorMax = Vector2.one;
            _viewportRect.offsetMin = new Vector2(18f, 18f);
            _viewportRect.offsetMax = new Vector2(-18f, -70f);
            viewportObject.GetComponent<Image>().color =
                new Color(0.02f, 0.03f, 0.04f, 0.94f);

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(GridLayoutGroup),
                typeof(ContentSizeFitter));
            _contentRect = contentObject.GetComponent<RectTransform>();
            _contentRect.SetParent(_viewportRect, false);
            _contentRect.anchorMin = new Vector2(0f, 1f);
            _contentRect.anchorMax = new Vector2(1f, 1f);
            _contentRect.pivot = new Vector2(0.5f, 1f);
            _contentRect.anchoredPosition = Vector2.zero;
            _contentRect.sizeDelta = Vector2.zero;

            var grid = contentObject.GetComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(12, 12, 12, 12);
            grid.cellSize = SlotSize;
            grid.spacing = new Vector2(10f, 10f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = ColumnCount;

            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewportObject.AddComponent<ScrollRect>();
            scroll.viewport = _viewportRect;
            scroll.content = _contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            _emptyText = CreateText(
                "EmptyText",
                _viewportRect,
                "보유한 아이템이 없습니다.\n우측 상단 + 버튼으로 추가하십시오.",
                17,
                TextAnchor.MiddleCenter);
            _emptyText.color = new Color(0.66f, 0.72f, 0.76f, 1f);
            _emptyText.rectTransform.anchorMin = Vector2.zero;
            _emptyText.rectTransform.anchorMax = Vector2.one;
            _emptyText.rectTransform.offsetMin = Vector2.zero;
            _emptyText.rectTransform.offsetMax = Vector2.zero;
            _emptyText.raycastTarget = false;
        }

        private void BuildAddPanel()
        {
            var panelImage = CreateImageObject(
                "AddItemPanel",
                _panelRect,
                new Color(0.045f, 0.065f, 0.078f, 1f));
            var rect = panelImage.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(354f, 408f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            _addPanel = panelImage.gameObject;

            var title = CreateText(
                "Title",
                rect,
                "아이템 추가",
                20,
                TextAnchor.MiddleCenter);
            SetRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -20f),
                new Vector2(260f, 34f));

            _catalogModeButton = CreateButton(
                "CatalogMode",
                rect,
                "목록에서 선택",
                15,
                new Color(0.08f, 0.25f, 0.30f, 1f));
            SetRect(
                _catalogModeButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(-78f, -62f),
                new Vector2(148f, 38f));
            _catalogModeButton.onClick.AddListener(
                () => SetAddMode(true));

            _customModeButton = CreateButton(
                "CustomMode",
                rect,
                "직접 작성",
                15,
                new Color(0.08f, 0.18f, 0.21f, 1f));
            SetRect(
                _customModeButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(78f, -62f),
                new Vector2(148f, 38f));
            _customModeButton.onClick.AddListener(
                () => SetAddMode(false));

            _catalogSelectButton = CreateButton(
                "CatalogSelect",
                rect,
                "아이템 목록 ▼",
                15,
                new Color(0.09f, 0.16f, 0.19f, 1f));
            SetRect(
                _catalogSelectButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -108f),
                new Vector2(312f, 38f));
            _catalogSelectLabel =
                _catalogSelectButton.GetComponentInChildren<Text>();
            _catalogSelectButton.onClick.AddListener(ToggleCatalogPopup);

            _typeSelectButton = CreateButton(
                "TypeSelect",
                rect,
                "종류: 음식 ▼",
                15,
                new Color(0.09f, 0.16f, 0.19f, 1f));
            SetRect(
                _typeSelectButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -108f),
                new Vector2(312f, 38f));
            _typeSelectLabel =
                _typeSelectButton.GetComponentInChildren<Text>();
            _typeSelectButton.onClick.AddListener(ToggleTypePopup);

            CreateInputRow(
                rect,
                "NameRow",
                "아이템 이름",
                "아이템 이름",
                -158f,
                out _nameInput);
            CreateInputRow(
                rect,
                "QuantityRow",
                "개수",
                "1",
                -210f,
                out _quantityInput);
            _quantityInput.contentType = InputField.ContentType.IntegerNumber;
            CreateInputRow(
                rect,
                "WeightRow",
                "개당 무게",
                "0",
                -262f,
                out _weightInput);
            _weightInput.contentType = InputField.ContentType.DecimalNumber;

            _addErrorText = CreateText(
                "ErrorText",
                rect,
                string.Empty,
                13,
                TextAnchor.MiddleCenter);
            _addErrorText.color = new Color(1f, 0.42f, 0.32f, 1f);
            SetRect(
                _addErrorText.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 74f),
                new Vector2(312f, 30f));

            var accept = CreateButton(
                "Accept",
                rect,
                "추가",
                16,
                new Color(0.08f, 0.32f, 0.28f, 1f));
            SetRect(
                accept.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-80f, 30f),
                new Vector2(148f, 40f));
            accept.onClick.AddListener(SubmitAdd);

            var cancel = CreateButton(
                "Cancel",
                rect,
                "취소",
                16,
                new Color(0.22f, 0.10f, 0.10f, 1f));
            SetRect(
                cancel.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(80f, 30f),
                new Vector2(148f, 40f));
            cancel.onClick.AddListener(HideAddPanel);

            BuildCatalogPopup(rect);
            BuildTypePopup(rect);
            _addPanel.SetActive(false);
        }

        private void CreateInputRow(
            Transform parent,
            string objectName,
            string label,
            string placeholder,
            float verticalPosition,
            out InputField input)
        {
            var labelText = CreateText(
                objectName + "Label",
                parent,
                label,
                14,
                TextAnchor.MiddleLeft);
            SetRect(
                labelText.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(-114f, verticalPosition),
                new Vector2(84f, 38f));

            input = CreateInputField(
                objectName + "Input",
                parent,
                placeholder);
            SetRect(
                input.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(46f, verticalPosition),
                new Vector2(226f, 38f));
        }

        private void BuildCatalogPopup(RectTransform parent)
        {
            var popupImage = CreateImageObject(
                "CatalogPopup",
                parent,
                new Color(0.025f, 0.04f, 0.05f, 1f));
            var popupRect = popupImage.rectTransform;
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.sizeDelta = new Vector2(326f, 306f);
            popupRect.anchoredPosition = Vector2.zero;
            _catalogPopup = popupImage.gameObject;

            var title = CreateText(
                "Title",
                popupRect,
                "아이템 목록",
                18,
                TextAnchor.MiddleCenter);
            SetRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -20f),
                new Vector2(240f, 34f));

            var close = CreateButton(
                "Close",
                popupRect,
                "×",
                21,
                new Color(0.16f, 0.07f, 0.07f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-18f, -18f),
                new Vector2(34f, 32f));
            close.onClick.AddListener(() => _catalogPopup.SetActive(false));

            var viewport = CreateImageObject(
                "Viewport",
                popupRect,
                new Color(0.02f, 0.03f, 0.04f, 1f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportRect = viewport.rectTransform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(14f, 14f);
            viewportRect.offsetMax = new Vector2(-14f, -58f);

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            _catalogPopupContent = contentObject.GetComponent<RectTransform>();
            _catalogPopupContent.SetParent(viewportRect, false);
            _catalogPopupContent.anchorMin = new Vector2(0f, 1f);
            _catalogPopupContent.anchorMax = new Vector2(1f, 1f);
            _catalogPopupContent.pivot = new Vector2(0.5f, 1f);
            _catalogPopupContent.anchoredPosition = Vector2.zero;
            _catalogPopupContent.sizeDelta = Vector2.zero;

            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = _catalogPopupContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            _catalogPopup.SetActive(false);
        }

        private void BuildTypePopup(RectTransform parent)
        {
            var popupImage = CreateImageObject(
                "TypePopup",
                parent,
                new Color(0.025f, 0.04f, 0.05f, 1f));
            var popupRect = popupImage.rectTransform;
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.sizeDelta = new Vector2(330f, 330f);
            popupRect.anchoredPosition = Vector2.zero;
            _typePopup = popupImage.gameObject;

            var title = CreateText(
                "Title",
                popupRect,
                "아이템 종류 선택",
                18,
                TextAnchor.MiddleCenter);
            SetRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -19f),
                new Vector2(240f, 34f));

            var close = CreateButton(
                "Close",
                popupRect,
                "×",
                21,
                new Color(0.16f, 0.07f, 0.07f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-18f, -18f),
                new Vector2(34f, 32f));
            close.onClick.AddListener(() => _typePopup.SetActive(false));

            var gridObject = new GameObject(
                "TypeGrid",
                typeof(RectTransform),
                typeof(GridLayoutGroup));
            var gridRect = gridObject.GetComponent<RectTransform>();
            gridRect.SetParent(popupRect, false);
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(12f, 12f);
            gridRect.offsetMax = new Vector2(-12f, -58f);

            var grid = gridObject.GetComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(4, 4, 4, 4);
            grid.cellSize = new Vector2(94f, 34f);
            grid.spacing = new Vector2(8f, 7f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            var values = (InventoryItemType[])Enum.GetValues(
                typeof(InventoryItemType));
            for (var index = 0; index < values.Length; index++)
            {
                var type = values[index];
                var button = CreateButton(
                    "Type_" + type,
                    gridRect,
                    InventoryItemTypeUtility.GetDisplayName(type),
                    13,
                    new Color(0.08f, 0.18f, 0.21f, 1f));
                button.onClick.AddListener(() => SelectCustomType(type));
            }

            _typePopup.SetActive(false);
        }

        private void BuildDetailPanel()
        {
            var panelImage = CreateImageObject(
                "ItemContextPanel",
                _panelRect,
                new Color(0.055f, 0.075f, 0.09f, 0.99f));
            _detailPanelRect = panelImage.rectTransform;
            _detailPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _detailPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _detailPanelRect.pivot = new Vector2(0.5f, 0.5f);
            _detailPanelRect.sizeDelta = new Vector2(286f, 270f);
            _detailPanelRect.anchoredPosition = Vector2.zero;
            _detailPanel = panelImage.gameObject;

            _detailNameText = CreateText(
                "Name",
                _detailPanelRect,
                string.Empty,
                20,
                TextAnchor.MiddleCenter);
            SetRect(
                _detailNameText.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                new Vector2(250f, 40f));

            _detailTypeText = CreateText(
                "Type",
                _detailPanelRect,
                string.Empty,
                15,
                TextAnchor.MiddleCenter);
            SetRect(
                _detailTypeText.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -70f),
                new Vector2(250f, 28f));

            _detailWeightText = CreateText(
                "Weight",
                _detailPanelRect,
                string.Empty,
                15,
                TextAnchor.MiddleCenter);
            SetRect(
                _detailWeightText.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -102f),
                new Vector2(250f, 30f));

            var quantityLabel = CreateText(
                "QuantityLabel",
                _detailPanelRect,
                "개수",
                14,
                TextAnchor.MiddleLeft);
            SetRect(
                quantityLabel.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-94f, 4f),
                new Vector2(56f, 38f));

            _detailQuantityInput = CreateInputField(
                "Quantity",
                _detailPanelRect,
                "1");
            _detailQuantityInput.contentType =
                InputField.ContentType.IntegerNumber;
            SetRect(
                _detailQuantityInput.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(30f, 4f),
                new Vector2(170f, 38f));

            var apply = CreateButton(
                "ApplyQuantity",
                _detailPanelRect,
                "개수 적용",
                15,
                new Color(0.08f, 0.30f, 0.30f, 1f));
            SetRect(
                apply.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 92f),
                new Vector2(244f, 38f));
            apply.onClick.AddListener(SubmitDetailQuantity);

            var remove = CreateButton(
                "Remove",
                _detailPanelRect,
                "삭제",
                15,
                new Color(0.34f, 0.08f, 0.08f, 1f));
            SetRect(
                remove.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-66f, 38f),
                new Vector2(114f, 40f));
            remove.onClick.AddListener(SubmitRemove);

            var close = CreateButton(
                "Close",
                _detailPanelRect,
                "닫기",
                15,
                new Color(0.12f, 0.15f, 0.17f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(66f, 38f),
                new Vector2(114f, 40f));
            close.onClick.AddListener(HideDetailPanel);
            _detailPanel.SetActive(false);
        }

        private void RebuildSlots()
        {
            if (_contentRect == null)
                return;

            for (var index = _contentRect.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(_contentRect.GetChild(index).gameObject);
            }

            _emptyText.gameObject.SetActive(_items.Count == 0);
            for (var index = 0; index < _items.Count; index++)
                CreateSlot(_items[index], index);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);
        }

        private void CreateSlot(InventoryRuntimeValue item, int index)
        {
            var slot = new GameObject(
                $"ItemSlot_{index}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(EventTrigger));
            var rect = slot.GetComponent<RectTransform>();
            rect.SetParent(_contentRect, false);
            rect.sizeDelta = SlotSize;

            var background = slot.GetComponent<Image>();
            background.color = new Color(0.08f, 0.12f, 0.15f, 1f);

            var iconSprite = _iconSet != null
                ? _iconSet.GetIcon(item.Type)
                : null;
            if (iconSprite != null)
            {
                var icon = CreateImageObject("Icon", rect, Color.white);
                icon.sprite = iconSprite;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.rectTransform.anchorMin = new Vector2(0.16f, 0.16f);
                icon.rectTransform.anchorMax = new Vector2(0.84f, 0.84f);
                icon.rectTransform.offsetMin = Vector2.zero;
                icon.rectTransform.offsetMax = Vector2.zero;
            }
            else
            {
                var fallback = CreateText(
                    "IconFallback",
                    rect,
                    InventoryItemTypeUtility.GetCompactLabel(item.Type),
                    18,
                    TextAnchor.MiddleCenter);
                fallback.raycastTarget = false;
                fallback.rectTransform.anchorMin = new Vector2(0.08f, 0.12f);
                fallback.rectTransform.anchorMax = new Vector2(0.92f, 0.88f);
                fallback.rectTransform.offsetMin = Vector2.zero;
                fallback.rectTransform.offsetMax = Vector2.zero;
            }

            var quantity = CreateText(
                "Quantity",
                rect,
                item.Quantity > 1 ? $"×{item.Quantity}" : string.Empty,
                16,
                TextAnchor.LowerRight);
            quantity.fontStyle = FontStyle.Bold;
            quantity.raycastTarget = false;
            quantity.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            quantity.rectTransform.anchorMax = Vector2.one;
            quantity.rectTransform.offsetMin = new Vector2(0f, 4f);
            quantity.rectTransform.offsetMax = new Vector2(-7f, -3f);

            var trigger = slot.GetComponent<EventTrigger>();
            AddTrigger(
                trigger,
                EventTriggerType.PointerClick,
                data =>
                {
                    var pointer = data as PointerEventData;
                    if (pointer == null)
                        return;

                    if (pointer.button == PointerEventData.InputButton.Right)
                        ShowDetail(item.RuntimeId, pointer.position);
                });
            AddTrigger(
                trigger,
                EventTriggerType.BeginDrag,
                data => BeginItemDrag(
                    data,
                    item.RuntimeId,
                    index,
                    slot));
            AddTrigger(
                trigger,
                EventTriggerType.PointerEnter,
                data =>
                {
                    if (!string.IsNullOrWhiteSpace(_dragRuntimeId))
                        _dragTargetIndex = index;
                });
            AddTrigger(
                trigger,
                EventTriggerType.EndDrag,
                data => EndItemDrag());
        }

        private void BeginItemDrag(
            BaseEventData eventData,
            string runtimeId,
            int sourceIndex,
            GameObject slot)
        {
            var pointer = eventData as PointerEventData;
            if (pointer != null &&
                pointer.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            HideDetailPanel();
            _dragRuntimeId = runtimeId;
            _dragTargetIndex = sourceIndex;
            _dragCanvasGroup = slot != null
                ? slot.GetComponent<CanvasGroup>()
                : null;
            if (_dragCanvasGroup != null)
            {
                _dragCanvasGroup.alpha = 0.55f;
                _dragCanvasGroup.blocksRaycasts = false;
            }
        }

        private void EndItemDrag()
        {
            var runtimeId = _dragRuntimeId;
            var targetIndex = _dragTargetIndex;
            ResetItemDragVisual();
            if (!string.IsNullOrWhiteSpace(runtimeId) && targetIndex >= 0)
                MoveRequested?.Invoke(runtimeId, targetIndex);
        }

        private void ResetItemDragVisual()
        {
            if (_dragCanvasGroup != null)
            {
                _dragCanvasGroup.alpha = 1f;
                _dragCanvasGroup.blocksRaycasts = true;
            }

            _dragCanvasGroup = null;
            _dragRuntimeId = string.Empty;
            _dragTargetIndex = -1;
        }

        private void BeginWindowDrag(BaseEventData eventData)
        {
            if (_isEmbedded)
                return;

            var pointer = eventData as PointerEventData;
            if (pointer == null ||
                pointer.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            HideSelectionPopups();
            HideDetailPanel();
            _isWindowDragging = true;
        }

        private void DragWindow(BaseEventData eventData)
        {
            if (_isEmbedded || !_isWindowDragging)
                return;

            var pointer = eventData as PointerEventData;
            if (pointer == null)
                return;

            var camera = GetCanvasCamera();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rootRect,
                    pointer.position,
                    camera,
                    out var current) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rootRect,
                    pointer.position - pointer.delta,
                    camera,
                    out var previous))
            {
                return;
            }

            _panelRect.anchoredPosition += current - previous;
            _hasUserMovedWindow = true;
            ClampPanelToRoot();
        }

        private void EndWindowDrag(BaseEventData eventData)
        {
            _isWindowDragging = false;
        }

        private void ApplyEmbeddedLayout()
        {
            if (_windowDragHandle != null)
                _windowDragHandle.gameObject.SetActive(false);
            if (_titleText != null)
                _titleText.gameObject.SetActive(false);
            if (_closeButton != null)
                _closeButton.gameObject.SetActive(false);
            if (_weightText != null)
            {
                SetRect(
                    _weightText.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(16f, -10f),
                    new Vector2(220f, 36f));
            }
            if (_addButton != null)
            {
                SetRect(
                    _addButton.transform as RectTransform,
                    Vector2.one,
                    Vector2.one,
                    Vector2.one,
                    new Vector2(-16f, -10f),
                    new Vector2(42f, 36f));
            }
            if (_viewportRect != null)
            {
                _viewportRect.offsetMin = new Vector2(16f, 16f);
                _viewportRect.offsetMax = new Vector2(-16f, -54f);
            }
        }

        private void ApplyFloatingLayout()
        {
            if (_windowDragHandle != null)
                _windowDragHandle.gameObject.SetActive(true);
            if (_titleText != null)
                _titleText.gameObject.SetActive(true);
            if (_closeButton != null)
                _closeButton.gameObject.SetActive(true);
            if (_weightText != null)
            {
                SetRect(
                    _weightText.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(18f, -16f),
                    new Vector2(180f, 34f));
            }
            if (_addButton != null)
            {
                SetRect(
                    _addButton.transform as RectTransform,
                    Vector2.one,
                    Vector2.one,
                    Vector2.one,
                    new Vector2(-62f, -16f),
                    new Vector2(38f, 34f));
            }
            if (_viewportRect != null)
            {
                _viewportRect.offsetMin = new Vector2(18f, 18f);
                _viewportRect.offsetMax = new Vector2(-18f, -70f);
            }
        }

        private static void StretchRect(RectTransform rect)
        {
            if (rect == null)
                return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void PositionAboveAnchor(RectTransform anchorRect)
        {
            if (anchorRect == null || _rootRect == null || _panelRect == null)
                return;

            var corners = new Vector3[4];
            anchorRect.GetWorldCorners(corners);
            var worldTopCenter = (corners[1] + corners[2]) * 0.5f;
            var screenPoint = RectTransformUtility.WorldToScreenPoint(
                GetCanvasCamera(),
                worldTopCenter);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rootRect,
                    screenPoint,
                    GetCanvasCamera(),
                    out var localPoint))
            {
                return;
            }

            _panelRect.anchoredPosition = localPoint +
                Vector2.up * (_panelRect.rect.height * 0.5f + 12f);
            ClampPanelToRoot();
        }

        private void ClampPanelToRoot()
        {
            if (_rootRect == null || _panelRect == null)
                return;

            var rootBounds = _rootRect.rect;
            var halfWidth = _panelRect.rect.width * 0.5f;
            var halfHeight = _panelRect.rect.height * 0.5f;
            var minimumX = rootBounds.xMin + halfWidth + PanelEdgePadding;
            var maximumX = rootBounds.xMax - halfWidth - PanelEdgePadding;
            var minimumY = rootBounds.yMin + halfHeight + PanelEdgePadding;
            var maximumY = rootBounds.yMax - halfHeight - PanelEdgePadding;

            var position = _panelRect.anchoredPosition;
            position.x = minimumX <= maximumX
                ? Mathf.Clamp(position.x, minimumX, maximumX)
                : 0f;
            position.y = minimumY <= maximumY
                ? Mathf.Clamp(position.y, minimumY, maximumY)
                : 0f;
            _panelRect.anchoredPosition = position;
        }

        private Camera GetCanvasCamera()
        {
            if (_rootCanvas == null ||
                _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return _rootCanvas.worldCamera;
        }

        private void RefreshWeight(float currentWeight, float capacity)
        {
            if (_weightText == null)
                return;

            var current = Mathf.Max(0f, currentWeight);
            var maximum = Mathf.Max(0f, capacity);
            _weightText.text = $"[{current:0.##} / {maximum:0.##}]";
            _weightText.color = maximum > 0f && current > maximum + 0.001f
                ? new Color(1f, 0.35f, 0.28f, 1f)
                : new Color(0.82f, 0.91f, 0.94f, 1f);
        }

        private void RebuildCatalogItems()
        {
            _catalogItems.Clear();
            if (_catalog != null && _catalog.Items != null)
            {
                for (var index = 0; index < _catalog.Items.Count; index++)
                {
                    var item = _catalog.Items[index];
                    if (item != null)
                        _catalogItems.Add(item);
                }
            }

            if (_catalogItems.Count == 0)
                _catalogSelectionIndex = -1;
            else
                _catalogSelectionIndex = Mathf.Clamp(
                    _catalogSelectionIndex,
                    0,
                    _catalogItems.Count - 1);

            RebuildCatalogPopupButtons();
            RefreshAddSelection();
        }

        private void RebuildCatalogPopupButtons()
        {
            if (_catalogPopupContent == null)
                return;

            for (var index = _catalogPopupContent.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(_catalogPopupContent.GetChild(index).gameObject);
            }

            if (_catalogItems.Count == 0)
            {
                var empty = CreateText(
                    "Empty",
                    _catalogPopupContent,
                    "등록된 ItemDefinition이 없습니다.",
                    14,
                    TextAnchor.MiddleCenter);
                var layout = empty.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = 42f;
                return;
            }

            for (var index = 0; index < _catalogItems.Count; index++)
            {
                var capturedIndex = index;
                var definition = _catalogItems[index];
                var button = CreateButton(
                    "CatalogItem_" + index,
                    _catalogPopupContent,
                    definition.DisplayName,
                    14,
                    new Color(0.08f, 0.18f, 0.21f, 1f));
                var layout = button.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = 36f;
                button.onClick.AddListener(
                    () => SelectCatalogItem(capturedIndex));
            }
        }

        private void ShowAddPanel()
        {
            HideDetailPanel();
            HideSelectionPopups();
            _usesCatalogMode = _catalogItems.Count > 0;
            _catalogSelectionIndex = _catalogItems.Count > 0 ? 0 : -1;
            _customType = InventoryItemType.Food;
            _nameInput.text = string.Empty;
            _quantityInput.text = "1";
            _weightInput.text = "0";
            _addErrorText.text = string.Empty;
            SetAddMode(_usesCatalogMode);
            _addPanel.SetActive(true);
        }

        private void HideAddPanel()
        {
            HideSelectionPopups();
            if (_addPanel != null)
                _addPanel.SetActive(false);
        }

        private void SetAddMode(bool usesCatalog)
        {
            var nextUsesCatalog = usesCatalog && _catalogItems.Count > 0;
            var changed = _usesCatalogMode != nextUsesCatalog;
            _usesCatalogMode = nextUsesCatalog;
            HideSelectionPopups();

            if (changed && !_usesCatalogMode)
            {
                _nameInput.text = string.Empty;
                _quantityInput.text = "1";
                _weightInput.text = "0";
            }

            RefreshAddSelection();
        }

        private void RefreshAddSelection()
        {
            if (_catalogModeButton == null || _customModeButton == null)
                return;

            var catalogImage = _catalogModeButton.targetGraphic as Image;
            var customImage = _customModeButton.targetGraphic as Image;
            if (catalogImage != null)
            {
                catalogImage.color = _usesCatalogMode
                    ? new Color(0.08f, 0.32f, 0.36f, 1f)
                    : new Color(0.08f, 0.18f, 0.21f, 1f);
            }
            if (customImage != null)
            {
                customImage.color = !_usesCatalogMode
                    ? new Color(0.08f, 0.32f, 0.36f, 1f)
                    : new Color(0.08f, 0.18f, 0.21f, 1f);
            }

            _catalogModeButton.interactable = _catalogItems.Count > 0;
            _catalogSelectButton.gameObject.SetActive(_usesCatalogMode);
            _typeSelectButton.gameObject.SetActive(!_usesCatalogMode);

            if (_usesCatalogMode &&
                _catalogSelectionIndex >= 0 &&
                _catalogSelectionIndex < _catalogItems.Count)
            {
                var definition = _catalogItems[_catalogSelectionIndex];
                _catalogSelectLabel.text = definition.DisplayName + " ▼";
                _nameInput.text = definition.DisplayName;
                _quantityInput.text = definition.DefaultQuantity.ToString();
                _weightInput.text = definition.UnitWeight.ToString("0.###");
                _nameInput.interactable = false;
                _weightInput.interactable = false;
            }
            else
            {
                _catalogSelectLabel.text = _catalogItems.Count > 0
                    ? "아이템 목록 ▼"
                    : "등록된 SO 없음";
                _typeSelectLabel.text =
                    $"종류: {InventoryItemTypeUtility.GetDisplayName(_customType)} ▼";
                _nameInput.interactable = true;
                _weightInput.interactable = true;
            }
        }

        private void ToggleCatalogPopup()
        {
            if (!_usesCatalogMode || _catalogItems.Count == 0)
                return;

            var show = !_catalogPopup.activeSelf;
            _typePopup.SetActive(false);
            _catalogPopup.SetActive(show);
        }

        private void ToggleTypePopup()
        {
            if (_usesCatalogMode)
                return;

            var show = !_typePopup.activeSelf;
            _catalogPopup.SetActive(false);
            _typePopup.SetActive(show);
        }

        private void HideSelectionPopups()
        {
            if (_catalogPopup != null)
                _catalogPopup.SetActive(false);
            if (_typePopup != null)
                _typePopup.SetActive(false);
        }

        private void SelectCatalogItem(int index)
        {
            if (index < 0 || index >= _catalogItems.Count)
                return;

            _catalogSelectionIndex = index;
            _catalogPopup.SetActive(false);
            RefreshAddSelection();
        }

        private void SelectCustomType(InventoryItemType type)
        {
            _customType = type;
            _typePopup.SetActive(false);
            RefreshAddSelection();
        }

        private void SubmitAdd()
        {
            if (!int.TryParse(_quantityInput.text, out var quantity))
            {
                _addErrorText.text = "개수는 정수로 입력하십시오.";
                return;
            }
            quantity = Mathf.Max(1, quantity);

            ItemDefinition definition = null;
            if (_usesCatalogMode &&
                _catalogSelectionIndex >= 0 &&
                _catalogSelectionIndex < _catalogItems.Count)
            {
                definition = _catalogItems[_catalogSelectionIndex];
            }

            var type = definition != null ? definition.Type : _customType;
            var displayName = definition != null
                ? definition.DisplayName
                : _nameInput.text;
            var unitWeight = definition != null
                ? definition.UnitWeight
                : 0f;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                _addErrorText.text = "아이템 이름을 입력하십시오.";
                return;
            }

            if (definition == null &&
                !float.TryParse(_weightInput.text, out unitWeight))
            {
                _addErrorText.text = "무게는 숫자로 입력하십시오.";
                return;
            }

            if (unitWeight < 0f || float.IsNaN(unitWeight) ||
                float.IsInfinity(unitWeight))
            {
                _addErrorText.text = "무게가 유효하지 않습니다.";
                return;
            }

            AddRequested?.Invoke(
                new InventoryItemDraft(
                    definition,
                    type,
                    displayName,
                    quantity,
                    unitWeight));
            HideAddPanel();
        }

        private void ShowDetail(string runtimeId, Vector2 screenPosition)
        {
            if (!TryGetItem(runtimeId, out var item))
                return;

            HideAddPanel();
            _detailRuntimeId = runtimeId;
            BindDetail(item);
            PositionDetailPanel(screenPosition);
            _detailPanel.SetActive(true);
        }

        private void PositionDetailPanel(Vector2 screenPosition)
        {
            if (_detailPanelRect == null || _panelRect == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _panelRect,
                    screenPosition,
                    GetCanvasCamera(),
                    out var localPoint))
            {
                localPoint = Vector2.zero;
            }

            _detailPanelRect.anchoredPosition = localPoint +
                new Vector2(36f, -36f);
            ClampChildToParent(_detailPanelRect, _panelRect, 8f);
        }

        private void BindDetail(InventoryRuntimeValue item)
        {
            _detailNameText.text = item.DisplayName;
            _detailTypeText.text =
                InventoryItemTypeUtility.GetDisplayName(item.Type);
            _detailWeightText.text =
                $"개당 {item.UnitWeight:0.###} / 총 {item.TotalWeight:0.###}";
            _detailQuantityInput.text = item.Quantity.ToString();
        }

        private void HideDetailPanel()
        {
            _detailRuntimeId = string.Empty;
            if (_detailPanel != null)
                _detailPanel.SetActive(false);
        }

        private void SubmitDetailQuantity()
        {
            if (string.IsNullOrWhiteSpace(_detailRuntimeId) ||
                !int.TryParse(_detailQuantityInput.text, out var quantity))
            {
                return;
            }

            QuantityChangedRequested?.Invoke(
                _detailRuntimeId,
                Mathf.Max(1, quantity));
        }

        private void SubmitRemove()
        {
            var runtimeId = _detailRuntimeId;
            HideDetailPanel();
            if (!string.IsNullOrWhiteSpace(runtimeId))
                RemoveRequested?.Invoke(runtimeId);
        }

        private bool TryGetItem(
            string runtimeId,
            out InventoryRuntimeValue item)
        {
            for (var index = 0; index < _items.Count; index++)
            {
                if (string.Equals(
                        _items[index].RuntimeId,
                        runtimeId,
                        StringComparison.Ordinal))
                {
                    item = _items[index];
                    return true;
                }
            }

            item = default(InventoryRuntimeValue);
            return false;
        }

        private static void ClampChildToParent(
            RectTransform child,
            RectTransform parent,
            float padding)
        {
            if (child == null || parent == null)
                return;

            var parentRect = parent.rect;
            var childRect = child.rect;
            var halfWidth = childRect.width * 0.5f;
            var halfHeight = childRect.height * 0.5f;
            var position = child.anchoredPosition;
            position.x = Mathf.Clamp(
                position.x,
                parentRect.xMin + halfWidth + padding,
                parentRect.xMax - halfWidth - padding);
            position.y = Mathf.Clamp(
                position.y,
                parentRect.yMin + halfHeight + padding,
                parentRect.yMax - halfHeight - padding);
            child.anchoredPosition = position;
        }

        private static void AddTrigger(
            EventTrigger trigger,
            EventTriggerType type,
            Action<BaseEventData> callback)
        {
            if (trigger.triggers == null)
                trigger.triggers = new List<EventTrigger.Entry>();

            var entry = new EventTrigger.Entry
            {
                eventID = type,
                callback = new EventTrigger.TriggerEvent()
            };
            entry.callback.AddListener(data => callback?.Invoke(data));
            trigger.triggers.Add(entry);
        }

        private Image CreateImageObject(
            string objectName,
            Transform parent,
            Color color)
        {
            var gameObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var image = gameObject.GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.color = color;
            return image;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            string value,
            int fontSize,
            TextAnchor alignment)
        {
            var gameObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            var text = gameObject.GetComponent<Text>();
            text.rectTransform.SetParent(parent, false);
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.88f, 0.93f, 0.95f, 1f);
            text.text = value ?? string.Empty;
            text.supportRichText = false;
            return text;
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            string label,
            int fontSize,
            Color color)
        {
            var image = CreateImageObject(objectName, parent, color);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var text = CreateText(
                "Label",
                image.rectTransform,
                label,
                fontSize,
                TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(5f, 2f);
            text.rectTransform.offsetMax = new Vector2(-5f, -2f);
            return button;
        }

        private InputField CreateInputField(
            string objectName,
            Transform parent,
            string placeholder)
        {
            var image = CreateImageObject(
                objectName,
                parent,
                new Color(0.025f, 0.04f, 0.05f, 1f));
            var input = image.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;

            var text = CreateText(
                "Text",
                image.rectTransform,
                string.Empty,
                16,
                TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(10f, 4f);
            text.rectTransform.offsetMax = new Vector2(-10f, -4f);

            var placeholderText = CreateText(
                "Placeholder",
                image.rectTransform,
                placeholder,
                16,
                TextAnchor.MiddleLeft);
            placeholderText.color = new Color(0.45f, 0.52f, 0.56f, 1f);
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.rectTransform.anchorMin = Vector2.zero;
            placeholderText.rectTransform.anchorMax = Vector2.one;
            placeholderText.rectTransform.offsetMin = new Vector2(10f, 4f);
            placeholderText.rectTransform.offsetMax =
                new Vector2(-10f, -4f);

            input.textComponent = text;
            input.placeholder = placeholderText;
            return input;
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

        private void OnRectTransformDimensionsChange()
        {
            if (_isVisible)
                ClampPanelToRoot();
        }

        private void OnDestroy()
        {
            AddRequested = null;
            RemoveRequested = null;
            QuantityChangedRequested = null;
            MoveRequested = null;
            CloseRequested = null;
        }
    }
}
