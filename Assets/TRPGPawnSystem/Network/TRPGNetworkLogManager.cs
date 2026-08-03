using UnityEngine;

namespace Trpg.Pawns
{
    /// <summary>
    /// 원격 룰렛 표시 설정만 TRPGSessionAuthority에 전달하는 로컬 어댑터입니다.
    /// 로그 RPC는 TRPGSessionAuthority가 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TRPGNetworkLogManager : MonoBehaviour
    {
        [Header("Auto References")]
        [SerializeField, HideInInspector]
        private TRPGSessionAuthority _sessionAuthority;
        [SerializeField, HideInInspector]
        private PawnManager _pawnManager;

        [Header("Remote Roulette")]
        [SerializeField] private Font _uiFont;
        [SerializeField] private int _sortingOrder = 2400;
        [SerializeField, Min(0f)]
        private float _resultHoldSeconds = 2.4f;

        public bool IsOnline =>
            ResolveAuthority() != null &&
            _sessionAuthority.IsOnline;

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
            PawnManager pawnManager)
        {
            if (authority != null)
                _sessionAuthority = authority;
            if (pawnManager != null)
                _pawnManager = pawnManager;

            ResolveAndConfigure();
        }

        private void ResolveAndConfigure()
        {
            var authority = ResolveAuthority();
            if (_pawnManager == null)
                _pawnManager = FindFirst<PawnManager>();

            authority?.ConfigureLogPresentation(
                _pawnManager,
                _uiFont,
                _sortingOrder,
                _resultHoldSeconds);
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
