using Trpg.UI.Inventory;
using UnityEngine;

namespace Trpg.Pawns
{
    /// <summary>
    /// 기존 PawnManager와 PawnUIManager가 사용하는 로컬 어댑터입니다.
    /// 네트워크 RPC는 이미 정상 Spawn되는 TRPGSessionAuthority가 담당합니다.
    /// 이 컴포넌트 자체는 NetworkBehaviour가 아니므로 Fusion Bake 대상이 아닙니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TRPGNetworkGameManager : MonoBehaviour
    {
        [Header("Auto References")]
        [SerializeField, HideInInspector]
        private TRPGSessionAuthority _sessionAuthority;
        [SerializeField, HideInInspector]
        private PawnManager _pawnManager;
        [SerializeField, HideInInspector]
        private PawnUIManager _pawnUiManager;

        public bool IsOnline =>
            ResolveAuthority() != null &&
            _sessionAuthority.IsOnline;

        public bool IsHost =>
            ResolveAuthority() != null &&
            _sessionAuthority.IsLocalGameMaster;

        public bool ShouldRouteClientMove
        {
            get
            {
                var authority = ResolveAuthority();
                if (authority != null && authority.IsOnline)
                    return authority.ShouldRouteClientMove;

                var bootstrap = TRPGNetworkBootstrap.Instance;
                return bootstrap != null && bootstrap.IsClient;
            }
        }

        public bool ShouldRouteClientStatChange
        {
            get
            {
                var authority = ResolveAuthority();
                if (authority != null && authority.IsOnline)
                    return authority.ShouldRouteClientStatChange;

                var bootstrap = TRPGNetworkBootstrap.Instance;
                return bootstrap != null && bootstrap.IsClient;
            }
        }

        public bool ShouldRouteClientInventoryChange
        {
            get
            {
                var authority = ResolveAuthority();
                if (authority != null && authority.IsOnline)
                    return authority.ShouldRouteClientInventoryChange;

                var bootstrap = TRPGNetworkBootstrap.Instance;
                return bootstrap != null && bootstrap.IsClient;
            }
        }

        private void Awake()
        {
            ResolveAndConfigure();
        }

        private void OnEnable()
        {
            ResolveAndConfigure();
        }

        private void Start()
        {
            ResolveAndConfigure();
        }

        public void Configure(
            TRPGSessionAuthority authority,
            PawnManager pawnManager,
            PawnUIManager pawnUiManager)
        {
            if (authority != null)
                _sessionAuthority = authority;
            if (pawnManager != null)
                _pawnManager = pawnManager;
            if (pawnUiManager != null)
                _pawnUiManager = pawnUiManager;

            ResolveAndConfigure();
        }

        public bool RequestMove(
            InteractivePawn pawn,
            Vector2 requestedDestination)
        {
            var authority = ResolveAuthority();
            return authority != null &&
                   authority.RequestMove(
                       pawn,
                       requestedDestination);
        }

        public bool RequestStatChange(
            InteractivePawn pawn,
            string statId,
            double requestedValue)
        {
            var authority = ResolveAuthority();
            return authority != null &&
                   authority.RequestStatChange(
                       pawn,
                       statId,
                       requestedValue);
        }

        public void PublishHostStatChange(
            InteractivePawn pawn,
            string statId,
            double previousValue,
            double currentValue)
        {
            ResolveAuthority()?.PublishHostStatChange(
                pawn,
                statId,
                previousValue,
                currentValue);
        }

        public bool RequestInventoryAdd(
            InteractivePawn pawn,
            InventoryItemDraft draft)
        {
            var authority = ResolveAuthority();
            return authority != null &&
                   authority.RequestInventoryAdd(pawn, draft);
        }

        public bool RequestInventoryRemove(
            InteractivePawn pawn,
            string runtimeId)
        {
            var authority = ResolveAuthority();
            return authority != null &&
                   authority.RequestInventoryRemove(
                       pawn,
                       runtimeId);
        }

        public bool RequestInventoryQuantity(
            InteractivePawn pawn,
            string runtimeId,
            int quantity)
        {
            var authority = ResolveAuthority();
            return authority != null &&
                   authority.RequestInventoryQuantity(
                       pawn,
                       runtimeId,
                       quantity);
        }

        public bool RequestInventoryMove(
            InteractivePawn pawn,
            string runtimeId,
            int targetIndex)
        {
            var authority = ResolveAuthority();
            return authority != null &&
                   authority.RequestInventoryMove(
                       pawn,
                       runtimeId,
                       targetIndex);
        }

        public void PublishHostInventorySnapshot(
            InteractivePawn pawn,
            string title,
            string detail)
        {
            ResolveAuthority()?.PublishHostInventorySnapshot(
                pawn,
                title,
                detail);
        }

        private void ResolveAndConfigure()
        {
            var authority = ResolveAuthority();

            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();
            if (_pawnUiManager == null)
                _pawnUiManager = FindFirst<PawnUIManager>();

            _pawnManager?.ConfigureNetworkManager(this);
            _pawnUiManager?.ConfigureNetworkManager(this);

            authority?.ConfigureGameplayReferences(
                _pawnManager,
                _pawnUiManager);
        }

        private TRPGSessionAuthority ResolveAuthority()
        {
            if (_sessionAuthority != null)
                return _sessionAuthority;

            _sessionAuthority =
                GetComponent<TRPGSessionAuthority>();

            if (_sessionAuthority == null)
            {
                _sessionAuthority =
                    TRPGSessionAuthority.Instance;
            }

            if (_sessionAuthority == null)
                _sessionAuthority = FindFirst<TRPGSessionAuthority>();

            return _sessionAuthority;
        }

        private static T FindFirst<T>()
            where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }
    }
}
