using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Trpg.Pawns
{
    /// <summary>
    /// Photon Fusion Host/Client 세션을 생성하고,
    /// Player Build에서는 같은 세션 코드를 자동으로 재검색합니다.
    ///
    /// Editor: GM Host
    /// Player Build: Client 자동 참가 및 재접속
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TRPGNetworkBootstrap : MonoBehaviour,
        INetworkRunnerCallbacks
    {
        private const int MaximumPeerCount = 5;
        private const string DefaultSessionCode = "TRPG_TEST_01";

        private static TRPGNetworkBootstrap _instance;

        [Header("Session")]
        [SerializeField] private string _sessionCode = DefaultSessionCode;

        [Header("Player Client")]
        [SerializeField]
        private bool _autoJoinClientInPlayerBuild = true;
        [SerializeField]
        private bool _autoReconnectClient = true;
        [SerializeField, Min(0.5f)]
        private float _reconnectDelaySeconds = 3f;

        [Header("Debug Panel")]
        [SerializeField] private bool _showDebugPanel = true;
        [SerializeField]
        private bool _showDebugPanelInPlayerBuild;

        [Header("Debug Inputs")]
        [SerializeField]
        private string _characterDefinitionId = string.Empty;
        [SerializeField]
        private string _activeActorDefinitionId = string.Empty;

        private NetworkRunner _runner;
        private NetworkSceneManagerDefault _sceneManager;
        private TRPGSessionAuthority _authority;
        private Coroutine _reconnectRoutine;
        private bool _isStarting;
        private bool _manualDisconnect;
        private bool _clientModeRequested;
        private bool _hasEverConnected;
        private bool _isQuitting;
        private string _status = "대기 중";
        private Rect _windowRect = new Rect(20f, 20f, 430f, 320f);

        public static TRPGNetworkBootstrap Instance => _instance;
        public NetworkRunner Runner => _runner;
        public TRPGSessionAuthority Authority => _authority;
        public bool IsRunning => _runner != null && _runner.IsRunning;
        public bool IsHost => IsRunning && _runner.IsServer;
        public bool IsClient =>
            IsRunning && _runner != null && !_runner.IsServer;
        public bool IsStarting => _isStarting;
        public bool IsSearching => _reconnectRoutine != null;
        public bool HasEverConnected => _hasEverConnected;
        public bool IsClientModeRequested => _clientModeRequested;
        public string SessionCode => _sessionCode;
        public string Status => _status;

        public event Action<string> StatusChanged;
        public event Action<TRPGSessionAuthority> AuthorityChanged;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            NormalizeSessionCode();
        }

        private void Start()
        {
            if (!Application.isEditor &&
                _autoJoinClientInPlayerBuild)
            {
                BeginClientSearch();
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void OnDestroy()
        {
            _isQuitting = true;
            CancelReconnect();

            if (_instance == this)
                _instance = null;

            StatusChanged = null;
            AuthorityChanged = null;
        }

        private void OnGUI()
        {
            if (!_showDebugPanel)
                return;

            if (!Application.isEditor &&
                !_showDebugPanelInPlayerBuild)
            {
                return;
            }

            _windowRect = GUILayout.Window(
                GetInstanceID(),
                _windowRect,
                DrawDebugWindow,
                "TRPG Photon Fusion 테스트");
        }

        public static void RegisterAuthority(
            TRPGSessionAuthority authority)
        {
            if (_instance == null || authority == null)
                return;

            _instance._authority = authority;
            _instance.AuthorityChanged?.Invoke(authority);

            authority.LocalMessageChanged -=
                _instance.HandleAuthorityMessage;
            authority.LocalMessageChanged +=
                _instance.HandleAuthorityMessage;
        }

        public static void UnregisterAuthority(
            TRPGSessionAuthority authority)
        {
            if (_instance == null ||
                _instance._authority != authority)
            {
                return;
            }

            authority.LocalMessageChanged -=
                _instance.HandleAuthorityMessage;

            _instance._authority = null;
            _instance.AuthorityChanged?.Invoke(null);
        }

        public void BeginClientSearch()
        {
            _clientModeRequested = true;
            _manualDisconnect = false;

            if (IsRunning || _isStarting)
                return;

            JoinClient();
        }

        public async void StartHost()
        {
            _clientModeRequested = false;
            _manualDisconnect = false;
            CancelReconnect();

            await StartSessionAsync(GameMode.Host);
        }

        public async void JoinClient()
        {
            _clientModeRequested = true;
            _manualDisconnect = false;
            CancelReconnect();

            await StartSessionAsync(GameMode.Client);

            if (!IsRunning)
                ScheduleClientReconnect();
        }

        public async void Disconnect()
        {
            _manualDisconnect = true;
            CancelReconnect();

            if (_runner == null)
            {
                SetStatus("연결 종료");
                return;
            }

            SetStatus("연결 종료 중...");

            var runner = _runner;
            _runner = null;
            _sceneManager = null;

            try
            {
                await runner.Shutdown(
                    destroyGameObject: false,
                    shutdownReason: ShutdownReason.Ok);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            if (runner != null)
                Destroy(runner.gameObject);

            SetStatus("연결 종료");
        }

        private async Task StartSessionAsync(GameMode mode)
        {
            CleanupStoppedRunner();

            if (_isStarting || IsRunning)
            {
                SetStatus("이미 연결 중이거나 실행 중입니다.");
                return;
            }

            NormalizeSessionCode();
            if (string.IsNullOrWhiteSpace(_sessionCode))
            {
                SetStatus("세션 코드가 비어 있습니다.");
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.buildIndex < 0)
            {
                SetStatus(
                    "현재 씬이 Build Profiles의 Scene List에 없습니다.");
                return;
            }

            _isStarting = true;
            SetStatus(mode == GameMode.Host
                ? "GM Host 생성 중..."
                : "서버를 검색하고 있습니다...");

            var runnerObject = new GameObject(
                mode == GameMode.Host
                    ? "FusionRunner_Host"
                    : "FusionRunner_Client");
            DontDestroyOnLoad(runnerObject);

            _runner = runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            _runner.AddCallbacks(this);

            _sceneManager = runnerObject.AddComponent<
                NetworkSceneManagerDefault>();

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(
                SceneRef.FromIndex(activeScene.buildIndex),
                LoadSceneMode.Single);

            try
            {
                var result = await _runner.StartGame(
                    new StartGameArgs
                    {
                        GameMode = mode,
                        SessionName = _sessionCode,
                        PlayerCount = MaximumPeerCount,
                        Scene = sceneInfo,
                        SceneManager = _sceneManager
                    });

                if (!result.Ok)
                {
                    SetStatus(
                        $"접속 실패: {result.ShutdownReason}");
                    CleanupFailedRunner();
                    return;
                }

                _hasEverConnected = true;
                SetStatus(mode == GameMode.Host
                    ? $"GM Host 접속 완료 · {_sessionCode}"
                    : $"Player Client 접속 완료 · {_sessionCode}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                SetStatus($"접속 예외: {exception.Message}");
                CleanupFailedRunner();
            }
            finally
            {
                _isStarting = false;
            }
        }

        private void ScheduleClientReconnect()
        {
            if (_isQuitting ||
                _manualDisconnect ||
                !_clientModeRequested ||
                !_autoReconnectClient ||
                IsRunning ||
                _reconnectRoutine != null)
            {
                return;
            }

            _reconnectRoutine = StartCoroutine(
                ReconnectClientRoutine());
        }

        private IEnumerator ReconnectClientRoutine()
        {
            while (!_isQuitting &&
                   !_manualDisconnect &&
                   _clientModeRequested &&
                   !IsRunning)
            {
                var remaining = Mathf.Max(
                    0.5f,
                    _reconnectDelaySeconds);
                var lastShownSecond = -1;

                while (remaining > 0f)
                {
                    if (_isQuitting ||
                        _manualDisconnect ||
                        !_clientModeRequested ||
                        IsRunning)
                    {
                        _reconnectRoutine = null;
                        yield break;
                    }

                    var shownSecond =
                        Mathf.CeilToInt(remaining);

                    if (shownSecond != lastShownSecond)
                    {
                        lastShownSecond = shownSecond;
                        SetStatus(
                            $"서버 검색 재시도까지 {shownSecond}초");
                    }

                    remaining -= Time.unscaledDeltaTime;
                    yield return null;
                }

                CleanupStoppedRunner();

                if (_isStarting)
                {
                    yield return null;
                    continue;
                }

                SetStatus("서버를 다시 검색하고 있습니다...");

                var attempt = StartSessionAsync(GameMode.Client);
                while (!attempt.IsCompleted)
                    yield return null;

                if (IsRunning)
                    break;
            }

            _reconnectRoutine = null;
        }

        private void CancelReconnect()
        {
            if (_reconnectRoutine == null)
                return;

            StopCoroutine(_reconnectRoutine);
            _reconnectRoutine = null;
        }

        private void CleanupStoppedRunner()
        {
            if (_runner == null || _runner.IsRunning)
                return;

            var runnerObject = _runner.gameObject;
            _runner = null;
            _sceneManager = null;

            if (runnerObject != null)
                Destroy(runnerObject);
        }

        private void CleanupFailedRunner()
        {
            if (_runner == null)
                return;

            var runnerObject = _runner.gameObject;
            _runner = null;
            _sceneManager = null;

            if (runnerObject != null)
                Destroy(runnerObject);
        }

        private void DrawDebugWindow(int windowId)
        {
            GUILayout.Label(
                "Editor는 Host, Player Build는 Client");
            GUILayout.Space(4f);

            GUILayout.Label("Session Code");
            _sessionCode = GUILayout.TextField(
                _sessionCode ?? string.Empty);

            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = !_isStarting && !IsRunning;

                if (GUILayout.Button("GM Host"))
                    StartHost();

                if (GUILayout.Button("Client Join"))
                    JoinClient();

                GUI.enabled = true;
            }

            GUI.enabled = IsRunning;
            if (GUILayout.Button("Disconnect"))
                Disconnect();
            GUI.enabled = true;

            GUILayout.Space(8f);
            GUILayout.Label("Character PawnDefinition.Id");
            _characterDefinitionId = GUILayout.TextField(
                _characterDefinitionId ?? string.Empty);

            GUI.enabled =
                IsRunning &&
                _authority != null &&
                _authority.IsSpawned;

            if (GUILayout.Button("캐릭터 점유 요청"))
            {
                _authority.RequestLocalCharacterClaim(
                    _characterDefinitionId);
            }

            GUI.enabled = true;

            GUILayout.Space(8f);
            GUILayout.Label(
                "GM Active Actor PawnDefinition.Id");

            _activeActorDefinitionId = GUILayout.TextField(
                _activeActorDefinitionId ?? string.Empty);

            GUI.enabled =
                IsHost &&
                _authority != null &&
                _authority.IsSpawned;

            if (GUILayout.Button("활성 캐릭터 선언"))
            {
                _authority.HostDeclareActiveByDefinitionId(
                    _activeActorDefinitionId);
            }

            GUI.enabled = true;

            GUILayout.Space(8f);
            GUILayout.Label($"상태: {_status}");

            if (_authority != null && _authority.IsSpawned)
            {
                GUILayout.Label(
                    _authority.GetLocalStateLabel());
                GUILayout.Label(
                    _authority.GetSlotSummary());
            }

            GUI.DragWindow();
        }

        private void NormalizeSessionCode()
        {
            _sessionCode =
                (_sessionCode ?? string.Empty).Trim();

            if (_sessionCode.Length > 64)
            {
                _sessionCode =
                    _sessionCode.Substring(0, 64);
            }
        }

        private void HandleAuthorityMessage(string message)
        {
            SetStatus(message);
        }

        private void SetStatus(string message)
        {
            _status = string.IsNullOrWhiteSpace(message)
                ? "상태 없음"
                : message.Trim();

            StatusChanged?.Invoke(_status);
        }

        public void OnPlayerJoined(
            NetworkRunner runner,
            PlayerRef player)
        {
            SetStatus(
                $"Player {player.PlayerId} 접속 · " +
                $"현재 {runner.ActivePlayers.Count()}명");
        }

        public void OnPlayerLeft(
            NetworkRunner runner,
            PlayerRef player)
        {
            if (runner.IsServer && _authority != null)
                _authority.HostReleasePlayer(player);

            SetStatus($"Player {player.PlayerId} 퇴장");
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
            _hasEverConnected = true;
            SetStatus("Photon 서버 연결 완료");
        }

        public void OnDisconnectedFromServer(
            NetworkRunner runner,
            NetDisconnectReason reason)
        {
            SetStatus($"서버 연결 종료: {reason}");
            ScheduleClientReconnect();
        }

        public void OnConnectFailed(
            NetworkRunner runner,
            NetAddress remoteAddress,
            NetConnectFailedReason reason)
        {
            SetStatus($"연결 실패: {reason}");
        }

        public void OnShutdown(
            NetworkRunner runner,
            ShutdownReason shutdownReason)
        {
            if (_runner == runner)
            {
                _runner = null;
                _sceneManager = null;
            }

            if (runner != null)
                Destroy(runner.gameObject);

            SetStatus($"Fusion 종료: {shutdownReason}");
            ScheduleClientReconnect();
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            SetStatus("GameScene01 네트워크 로드 완료");
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
            SetStatus("GameScene01 네트워크 로드 중...");
        }

        public void OnInput(
            NetworkRunner runner,
            NetworkInput input)
        {
        }

        public void OnInputMissing(
            NetworkRunner runner,
            PlayerRef player,
            NetworkInput input)
        {
        }

        public void OnConnectRequest(
            NetworkRunner runner,
            NetworkRunnerCallbackArgs.ConnectRequest request,
            byte[] token)
        {
            request.Accept();
        }

#pragma warning disable CS0618
        public void OnUserSimulationMessage(
            NetworkRunner runner,
            SimulationMessagePtr message)
        {
        }
#pragma warning restore CS0618

        public void OnSessionListUpdated(
            NetworkRunner runner,
            List<SessionInfo> sessionList)
        {
        }

        public void OnCustomAuthenticationResponse(
            NetworkRunner runner,
            Dictionary<string, object> data)
        {
        }

        public void OnHostMigration(
            NetworkRunner runner,
            HostMigrationToken hostMigrationToken)
        {
            SetStatus("Host Migration은 현재 지원하지 않습니다.");
        }

        public void OnReliableDataReceived(
            NetworkRunner runner,
            PlayerRef player,
            ReliableKey key,
            ReadOnlySpan<byte> data)
        {
        }

        public void OnReliableDataProgress(
            NetworkRunner runner,
            PlayerRef player,
            ReliableKey key,
            float progress)
        {
        }

        public void OnObjectEnterAOI(
            NetworkRunner runner,
            NetworkObject obj,
            PlayerRef player)
        {
        }

        public void OnObjectExitAOI(
            NetworkRunner runner,
            NetworkObject obj,
            PlayerRef player)
        {
        }
    }

    internal static class NetworkRunnerEnumerableUtility
    {
        public static int Count(
            this IEnumerable<PlayerRef> players)
        {
            var count = 0;

            foreach (var _ in players)
                count++;

            return count;
        }
    }
}
