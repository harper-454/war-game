using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using EpochWar.Unity.Net;

namespace EpochWar.Unity.Bootstrap
{
    /// <summary>
    /// The composition root for the lobby scene <c>Boot.unity</c> (task 17.1): a minimal UI Toolkit
    /// menu that lets the Player pick a Match mode and either Host or Join, then transitions into the
    /// playable <c>Match.unity</c> scene.
    ///
    /// <para><b>Mode.</b> The Player picks a <see cref="NetworkMatchMode"/> (2-human competitive or
    /// human(s)+AI co-op, Req 8.1); the choice is recorded in <see cref="LobbyConfig"/> so the
    /// <see cref="MatchSceneController"/> seeds the matching Nations when the match scene assembles.</para>
    ///
    /// <para><b>Host / Join.</b> "Host" calls <see cref="MatchNetworkManager.StartHost"/> and then
    /// tells Netcode to load the match scene, which the transport replicates to every client that
    /// joins (so clients follow the Host into the Match automatically). "Join" calls
    /// <see cref="MatchNetworkManager.StartClient"/>; the connecting client is moved into the Host's
    /// match scene by Netcode's networked scene management, so it does not load the scene itself.</para>
    ///
    /// <para>The manager is expected to live on a <c>NetworkObject</c> that survives the scene load
    /// (marked <c>DontDestroyOnLoad</c> by the NGO <c>NetworkManager</c>), so the same authoritative
    /// manager assembles the Match in <c>Match.unity</c>. When no <see cref="MatchNetworkManager"/> is
    /// present the controller falls back to loading the match scene locally for offline iteration.
    /// This component holds no gameplay rules — it only wires the menu to the lifecycle entry points.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class BootController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The UIDocument whose rootVisualElement hosts the lobby menu. Defaults to the sibling UIDocument.")]
        private UIDocument _document;

        [SerializeField]
        [Tooltip("The networked match manager (on a NetworkObject). Optional: absent means offline load.")]
        private MatchNetworkManager _networkManager;

        [SerializeField]
        [Tooltip("Build-settings name of the playable match scene to load.")]
        private string _matchSceneName = "Match";

        [SerializeField]
        [Tooltip("The mode selected by default when the lobby opens.")]
        private NetworkMatchMode _selectedMode = NetworkMatchMode.CompetitiveTwoHuman;

        private VisualElement _root;
        private Button _competitiveButton;
        private Button _coopButton;
        private Label _statusLabel;
        private bool _built;

        private void OnEnable()
        {
            EnsureBuilt();
            // Seed the shared selection so a direct Host/Join reflects the current toggle.
            LobbyConfig.Select(_selectedMode);
            RefreshModeButtons();
        }

        // ------------------------------------------------------------------
        // Lobby actions
        // ------------------------------------------------------------------

        /// <summary>Records <paramref name="mode"/> as the Player's selection (Req 8.1).</summary>
        public void SelectMode(NetworkMatchMode mode)
        {
            _selectedMode = mode;
            LobbyConfig.Select(mode);
            RefreshModeButtons();
            SetStatus($"Mode: {mode}");
        }

        /// <summary>Starts this peer as the authoritative Host and loads the match scene (Req 8.3).</summary>
        public void Host()
        {
            LobbyConfig.Select(_selectedMode);

            if (HasTransport())
            {
                if (!StartHost())
                {
                    SetStatus("Failed to start host.");
                    return;
                }

                SetStatus("Hosting - loading match...");
                LoadMatchSceneNetworked();
                return;
            }

            // No networking available: load the match scene locally for offline iteration.
            SetStatus("Loading match (offline)...");
            SceneManager.LoadScene(_matchSceneName, LoadSceneMode.Single);
        }

        /// <summary>Starts this peer as a client; Netcode moves it into the Host's match scene.</summary>
        public void Join()
        {
            LobbyConfig.Select(_selectedMode);

            if (!HasTransport())
            {
                SetStatus("No network manager to join with.");
                return;
            }

            if (!StartClient())
            {
                SetStatus("Failed to start client.");
                return;
            }

            // The Host's networked scene management transitions this client into the match scene, so
            // the client does not load the scene itself.
            SetStatus("Joining - waiting for host's match...");
        }

        // Prefer the wired MatchNetworkManager (task-specified entry points), falling back to the NGO
        // singleton so Boot.unity works even when the match manager is a scene object in Match.unity.
        private bool HasTransport() => _networkManager != null || NetworkManager.Singleton != null;

        private bool StartHost()
            => _networkManager != null
                ? _networkManager.StartHost()
                : NetworkManager.Singleton != null && NetworkManager.Singleton.StartHost();

        private bool StartClient()
            => _networkManager != null
                ? _networkManager.StartClient()
                : NetworkManager.Singleton != null && NetworkManager.Singleton.StartClient();

        /// <summary>
        /// Loads the match scene through Netcode's networked scene management on the Host so every
        /// connected (and future) client is synchronized into it. Falls back to a local load if the
        /// networked scene manager is not yet available.
        /// </summary>
        private void LoadMatchSceneNetworked()
        {
            NetworkManager ngo = NetworkManager.Singleton;
            if (ngo != null && ngo.IsServer && ngo.SceneManager != null)
            {
                ngo.SceneManager.LoadScene(_matchSceneName, LoadSceneMode.Single);
                return;
            }

            SceneManager.LoadScene(_matchSceneName, LoadSceneMode.Single);
        }

        // ------------------------------------------------------------------
        // UI construction (code-built; no UXML required)
        // ------------------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            var uiRoot = _document != null ? _document.rootVisualElement : null;
            if (uiRoot == null)
            {
                return;
            }

            _root = new VisualElement { name = "boot-root" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.bottom = 0;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            var panel = new VisualElement { name = "boot-panel" };
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.minWidth = 280;

            var title = new Label("Epoch War") { name = "boot-title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 24;
            title.style.marginBottom = 16;
            panel.Add(title);

            var modeRow = new VisualElement { name = "boot-mode-row" };
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom = 12;

            _competitiveButton = new Button(() => SelectMode(NetworkMatchMode.CompetitiveTwoHuman))
            {
                name = "boot-mode-competitive",
                text = "Competitive (2 Human)",
            };
            _coopButton = new Button(() => SelectMode(NetworkMatchMode.CooperativeVsAi))
            {
                name = "boot-mode-coop",
                text = "Co-op vs AI",
            };
            modeRow.Add(_competitiveButton);
            modeRow.Add(_coopButton);
            panel.Add(modeRow);

            var actionRow = new VisualElement { name = "boot-action-row" };
            actionRow.style.flexDirection = FlexDirection.Row;

            var hostButton = new Button(Host) { name = "boot-host", text = "Host" };
            var joinButton = new Button(Join) { name = "boot-join", text = "Join" };
            actionRow.Add(hostButton);
            actionRow.Add(joinButton);
            panel.Add(actionRow);

            _statusLabel = new Label($"Mode: {_selectedMode}") { name = "boot-status" };
            _statusLabel.style.marginTop = 12;
            panel.Add(_statusLabel);

            _root.Add(panel);
            uiRoot.Add(_root);
            _built = true;
            RefreshModeButtons();
        }

        private void RefreshModeButtons()
        {
            if (!_built)
            {
                return;
            }

            // The selected mode's button is disabled to read as "currently chosen".
            _competitiveButton?.SetEnabled(_selectedMode != NetworkMatchMode.CompetitiveTwoHuman);
            _coopButton?.SetEnabled(_selectedMode != NetworkMatchMode.CooperativeVsAi);
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
            }
        }
    }
}
