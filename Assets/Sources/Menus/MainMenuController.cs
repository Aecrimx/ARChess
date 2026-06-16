using System;
using System.Collections.Generic;
using System.Net;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
//  MainMenuController
//
//  RESPONSIBILITY: Main menu UI and navigation.
//  Builds the entire menu hierarchy in code — no prefabs needed.
//
//  PANEL STACK:
//  MainPanel       → Play vs AI | PvP | Settings
//  PvpPanel        → Local PvP | LAN
//  TimerSelectPanel → Timer picker + Start Game for local modes
//  LanPanel        → Host Game | Join Game
//  HostLobbyPanel  → Timer picker + Start Hosting + status label
//  JoinLobbyPanel  → Discovered server list (ScrollRect) + Refresh
//  SettingsPanel   → Player name InputField + Back
//
//  SCENE SETUP:
//  1. Create a scene called MainMenu (build index 0).
//  2. Add a Canvas (Screen Space - Overlay, Scale With Screen Size).
//  3. Attach this script to the Canvas.
//  4. Attach LanNetworkManager + LanDiscovery to a separate persistent GO.
// ─────────────────────────────────────────────────────────────────────────────
public class MainMenuController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Visuals")]
    public Font   uiFont;
    public Sprite buttonSprite;
    public Sprite logoSprite;
    public Color  backgroundColor  = new Color(0.08f, 0.08f, 0.12f, 1f);
    public Color  buttonColor      = new Color(0.20f, 0.20f, 0.30f, 1f);
    public Color  buttonHoverColor = new Color(0.30f, 0.30f, 0.45f, 1f);
    public Color  titleColor       = Color.white;
    public Color  buttonTextColor  = Color.white;

    [Header("Scene Names")]
    public string gameSceneName = "ChessScene";

    // ── Panel references ──────────────────────────────────────────────────────
    private GameObject _mainPanel;
    private GameObject _pvpPanel;
    private GameObject _timerSelectPanel;
    private GameObject _lanPanel;
    private GameObject _hostLobbyPanel;
    private GameObject _joinLobbyPanel;
    private GameObject _settingsPanel;

    // ── Host lobby state ──────────────────────────────────────────────────────
    private Text   _hostStatusLabel;
    private Text   _selectedTimerLabel;
    private Text   _modeTimerTitleLabel;
    private Text   _modeTimerSelectedLabel;
    private string _selectedTimerPreset = "unlimited";   // PlayerPrefs value
    private string _pendingLocalMode = "local2P";
    private readonly List<Button> _timerButtons = new List<Button>();

    // ── Join lobby state ──────────────────────────────────────────────────────
    private Transform      _serverListContent;
    private readonly List<DiscoveryResponse> _foundServers = new List<DiscoveryResponse>();
    private Coroutine _discoveryBurstRoutine;
    private Coroutine _subnetSweepRoutine;

    // ── Settings ──────────────────────────────────────────────────────────────
    private InputField _nameField;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        _selectedTimerPreset = PlayerPrefs.GetString("TimerPreset", "unlimited");
        SetBackground();
        BuildUI();
        ShowPanel(_mainPanel);

        LanDiscovery.OnServerDiscovered += OnServerFound;
    }

    void OnDestroy()
    {
        LanDiscovery.OnServerDiscovered -= OnServerFound;
        StopDiscoveryRoutines();
    }

    // ── Background ────────────────────────────────────────────────────────────
    private void SetBackground()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI CONSTRUCTION
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) { Debug.LogError("[MainMenuController] No Canvas found."); return; }
        Transform root = canvas.transform;

        BuildMainPanel(root);
        BuildPvpPanel(root);
        BuildTimerSelectPanel(root);
        BuildLanPanel(root);
        BuildHostLobbyPanel(root);
        BuildJoinLobbyPanel(root);
        BuildSettingsPanel(root);

        // Version label (always visible)
        MakeText("Version", root,
                 new Vector2(0f, 0f), new Vector2(1f, 0.06f),
                 "v0.3", 30, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);
    }

    // ── Main panel ────────────────────────────────────────────────────────────
    private void BuildMainPanel(Transform root)
    {
        _mainPanel = MakePanel("MainPanel", root);

        if (logoSprite != null)
        {
            var logoGO = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            logoGO.transform.SetParent(_mainPanel.transform, false);
            var rt = logoGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.65f);
            rt.anchorMax = new Vector2(0.9f, 0.90f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = logoGO.GetComponent<Image>();
            img.sprite = logoSprite; img.preserveAspect = true; img.raycastTarget = false;
        }
        else
        {
            MakeText("Title", _mainPanel.transform,
                     new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.88f),
                     "AR Chess", 90, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);
        }

        MakeText("Subtitle", _mainPanel.transform,
                 new Vector2(0.1f, 0.60f), new Vector2(0.9f, 0.68f),
                 "Choose a mode to play", 50, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);

        MakeButton("BtnVsAI",    _mainPanel.transform,
                   new Vector2(0.1f, 0.46f), new Vector2(0.9f, 0.58f),
                   "Play vs AI").onClick.AddListener(OnVsAIClicked);

        MakeButton("BtnPvP",     _mainPanel.transform,
                   new Vector2(0.1f, 0.32f), new Vector2(0.9f, 0.44f),
                   "PvP").onClick.AddListener(() => ShowPanel(_pvpPanel));

        MakeButton("BtnSettings", _mainPanel.transform,
                   new Vector2(0.1f, 0.18f), new Vector2(0.9f, 0.30f),
                   "Settings").onClick.AddListener(() => ShowPanel(_settingsPanel));
    }

    // ── PvP panel ─────────────────────────────────────────────────────────────
    private void BuildPvpPanel(Transform root)
    {
        _pvpPanel = MakePanel("PvpPanel", root);

        MakeText("Title", _pvpPanel.transform,
                 new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.90f),
                 "PvP Mode", 80, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);

        MakeButton("BtnLocal", _pvpPanel.transform,
                   new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.68f),
                   "Local PvP").onClick.AddListener(OnLocal2PClicked);

        MakeButton("BtnLan", _pvpPanel.transform,
                   new Vector2(0.1f, 0.40f), new Vector2(0.9f, 0.53f),
                   "LAN").onClick.AddListener(() => ShowPanel(_lanPanel));

        MakeButton("BtnBack", _pvpPanel.transform,
                   new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.27f),
                   "Back").onClick.AddListener(() => ShowPanel(_mainPanel));
    }

    // ── Local mode timer panel ───────────────────────────────────────────────
    private void BuildTimerSelectPanel(Transform root)
    {
        _timerSelectPanel = MakePanel("TimerSelectPanel", root);
        var t = _timerSelectPanel.transform;

        _modeTimerTitleLabel = MakeText("Title", t,
                 new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.94f),
                 "Choose Time Control", 75, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);

        MakeText("TimerLabel", t,
                 new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.78f),
                 "Time Control:", 50, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);

        BuildTimerPresetButtons(t, 0.56f, 0.67f);

        _modeTimerSelectedLabel = MakeText("SelectedTimerLabel", t,
                 new Vector2(0.05f, 0.46f), new Vector2(0.95f, 0.54f),
                 "", 40, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);

        MakeButton("BtnStartLocalMode", t,
                   new Vector2(0.1f, 0.32f), new Vector2(0.9f, 0.45f),
                   "Start Game").onClick.AddListener(OnStartSelectedLocalModeClicked);

        MakeButton("BtnBack", t,
                   new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.27f),
                   "Back").onClick.AddListener(() =>
        {
            ShowPanel(_pendingLocalMode == "vsAI" ? _mainPanel : _pvpPanel);
        });
    }

    // ── LAN panel ─────────────────────────────────────────────────────────────
    private void BuildLanPanel(Transform root)
    {
        _lanPanel = MakePanel("LanPanel", root);

        MakeText("Title", _lanPanel.transform,
                 new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.90f),
                 "LAN", 80, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);

        MakeButton("BtnHost", _lanPanel.transform,
                   new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.68f),
                   "Host Game").onClick.AddListener(() => ShowPanel(_hostLobbyPanel));

        MakeButton("BtnJoin", _lanPanel.transform,
                   new Vector2(0.1f, 0.40f), new Vector2(0.9f, 0.53f),
                   "Join Game").onClick.AddListener(OnJoinGameClicked);

        MakeButton("BtnBack", _lanPanel.transform,
                   new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.27f),
                   "Back").onClick.AddListener(() => ShowPanel(_pvpPanel));
    }

    // ── Host lobby panel ──────────────────────────────────────────────────────
    private void BuildHostLobbyPanel(Transform root)
    {
        _hostLobbyPanel = MakePanel("HostLobbyPanel", root);
        var t = _hostLobbyPanel.transform;

        MakeText("Title", t,
                 new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.95f),
                 "Host a Game", 75, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);

        MakeText("TimerLabel", t,
                 new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.82f),
                 "Time Control:", 50, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);

        _selectedTimerLabel = MakeText("SelectedTimerLabel", t,
                 new Vector2(0.05f, 0.52f), new Vector2(0.95f, 0.60f),
                 "", 40, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);

        BuildTimerPresetButtons(t, 0.60f, 0.71f);

        RefreshTimerSelectionUi();

        MakeButton("BtnStartHost", t,
                   new Vector2(0.1f, 0.44f), new Vector2(0.9f, 0.57f),
                   "Start Hosting").onClick.AddListener(OnStartHostingClicked);

        _hostStatusLabel = MakeText("StatusLabel", t,
                                    new Vector2(0.05f, 0.32f), new Vector2(0.95f, 0.43f),
                                    "", 45, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);

        MakeButton("BtnBack", t,
                   new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.27f),
                   "Back").onClick.AddListener(() =>
        {
            LanNetworkManager.Instance?.StopHost();
            ResetHostLobbyState();
            ShowPanel(_lanPanel);
        });
    }

    // ── Join lobby panel ──────────────────────────────────────────────────────
    private void BuildJoinLobbyPanel(Transform root)
    {
        _joinLobbyPanel = MakePanel("JoinLobbyPanel", root);
        var t = _joinLobbyPanel.transform;

        MakeText("Title", t,
                 new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.97f),
                 "Available Games", 70, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);

        // ScrollRect for the server list
        var scrollGO = new GameObject("ServerScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGO.transform.SetParent(t, false);
        var scrollRT        = scrollGO.GetComponent<RectTransform>();
        scrollRT.anchorMin  = new Vector2(0.05f, 0.30f);
        scrollRT.anchorMax  = new Vector2(0.95f, 0.83f);
        scrollRT.offsetMin  = scrollRT.offsetMax = Vector2.zero;
        scrollGO.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f, 1f);

        var contentGO      = new GameObject("Content", typeof(RectTransform));
        contentGO.transform.SetParent(scrollGO.transform, false);
        var contentRT      = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot     = new Vector2(0.5f, 1f);
        contentRT.sizeDelta = new Vector2(0f, 0f);
        _serverListContent  = contentGO.transform;

        var layout = contentGO.AddComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        layout.childControlHeight     = true;   // respect each row's LayoutElement height
        layout.childControlWidth      = true;
        layout.spacing                = 5f;
        layout.padding                = new RectOffset(4, 4, 4, 4);

        // ContentSizeFitter makes the content rect grow as rows are added.
        // Without this the container stays at zero height and rows are invisible.
        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var scroll       = scrollGO.GetComponent<ScrollRect>();
        scroll.content   = contentRT;
        scroll.vertical  = true;
        scroll.horizontal = false;

        MakeButton("BtnRefresh", t,
                   new Vector2(0.1f, 0.28f), new Vector2(0.55f, 0.30f),  // thin row below list
                   "Refresh").onClick.AddListener(OnRefreshClicked);

        MakeButton("BtnBack", t,
                   new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.27f),
                   "Back").onClick.AddListener(() =>
        {
            StopDiscoveryRoutines();
            LanNetworkManager.Instance?.GetComponent<LanDiscovery>()?.StopDiscovery();
            ShowPanel(_lanPanel);
        });
    }

    // ── Settings panel ────────────────────────────────────────────────────────
    private void BuildSettingsPanel(Transform root)
    {
        _settingsPanel = MakePanel("SettingsPanel", root);
        var t = _settingsPanel.transform;

        MakeText("Title", t,
                 new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.95f),
                 "Settings", 80, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);

        MakeText("NameLabel", t,
                 new Vector2(0.1f, 0.65f), new Vector2(0.9f, 0.75f),
                 "Player Name", 55, FontStyle.Normal, titleColor, TextAnchor.MiddleCenter);

        // InputField for player name
        var ifGO = new GameObject("NameField", typeof(RectTransform), typeof(Image), typeof(InputField));
        ifGO.transform.SetParent(t, false);
        var ifRT        = ifGO.GetComponent<RectTransform>();
        ifRT.anchorMin  = new Vector2(0.1f, 0.53f);
        ifRT.anchorMax  = new Vector2(0.9f, 0.65f);
        ifRT.offsetMin  = ifRT.offsetMax = Vector2.zero;
        ifGO.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.22f, 1f);
        _nameField       = ifGO.GetComponent<InputField>();

        // Placeholder text
        var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(ifGO.transform, false);
        StretchFull(phGO.GetComponent<RectTransform>());
        var phTxt = phGO.GetComponent<Text>();
        phTxt.text      = "Enter name...";
        phTxt.color     = new Color(0.5f, 0.5f, 0.5f, 1f);
        phTxt.fontSize  = 50;
        phTxt.font      = GetFont();
        phTxt.alignment = TextAnchor.MiddleCenter;

        // Input text
        var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        txtGO.transform.SetParent(ifGO.transform, false);
        StretchFull(txtGO.GetComponent<RectTransform>());
        var txt = txtGO.GetComponent<Text>();
        txt.color     = Color.white;
        txt.fontSize  = 50;
        txt.font      = GetFont();
        txt.alignment = TextAnchor.MiddleCenter;

        _nameField.textComponent   = txt;
        _nameField.placeholder     = phTxt;
        _nameField.characterLimit  = 20;
        _nameField.text            = PlayerPrefs.GetString("PlayerName", "");

        MakeButton("BtnBack", t,
                   new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.27f),
                   "Back").onClick.AddListener(OnSettingsBack);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  BUTTON CALLBACKS
    // ═════════════════════════════════════════════════════════════════════════

    private void OnVsAIClicked()
    {
        ShowLocalTimerSelection("vsAI", "Play vs AI");
    }

    private void OnLocal2PClicked()
    {
        ShowLocalTimerSelection("local2P", "Local PvP");
    }

    private void ShowLocalTimerSelection(string gameMode, string title)
    {
        _pendingLocalMode = gameMode;
        _selectedTimerPreset = PlayerPrefs.GetString("TimerPreset", _selectedTimerPreset);

        if (_modeTimerTitleLabel != null)
            _modeTimerTitleLabel.text = $"{title} Time Control";

        RefreshTimerSelectionUi();
        ShowPanel(_timerSelectPanel);
    }

    private void OnStartSelectedLocalModeClicked()
    {
        PlayerPrefs.SetString("GameMode", _pendingLocalMode);
        PlayerPrefs.SetString("TimerPreset", _selectedTimerPreset);
        PlayerPrefs.Save();
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnStartHostingClicked()
    {
        PlayerPrefs.SetString("GameMode",     "lanHost");
        PlayerPrefs.SetString("TimerPreset",  _selectedTimerPreset);
        PlayerPrefs.SetString("PlayerName",   PlayerPrefs.GetString("PlayerName", "Host"));
        PlayerPrefs.Save();

        var mgr = LanNetworkManager.Instance;
        if (mgr == null) { Debug.LogError("[MainMenu] LanNetworkManager not found!"); return; }

        mgr.HostPlayerName = PlayerPrefs.GetString("PlayerName", "Host");
        mgr.TimerSeconds   = GameModeManager.SecondsFromPreset(_selectedTimerPreset);
        mgr.StartHost();
        mgr.GetComponent<LanDiscovery>()?.AdvertiseServer();

        if (_hostStatusLabel != null)
            _hostStatusLabel.text = "Waiting for opponent…";

        Debug.Log($"[MainMenu] Hosting started. Timer: {_selectedTimerPreset}");
    }

    private void OnJoinGameClicked()
    {
        ShowPanel(_joinLobbyPanel);
        ClearServerList();
        StartDiscovery();
    }

    private void OnRefreshClicked()
    {
        ClearServerList();
        StartDiscovery();
    }

    private void OnSettingsBack()
    {
        string name = _nameField != null ? _nameField.text.Trim() : "";
        if (string.IsNullOrEmpty(name)) name = "Player";
        PlayerPrefs.SetString("PlayerName", name);
        PlayerPrefs.Save();
        ShowPanel(_mainPanel);
    }

    // ── Discovery ─────────────────────────────────────────────────────────────
    private void StartDiscovery()
    {
        var discovery = LanNetworkManager.Instance?.GetComponent<LanDiscovery>();
        if (discovery == null)
        {
            Debug.LogError("[MainMenu] LanDiscovery component not found! " +
                           "Make sure LanDiscovery is on the same GO as LanNetworkManager.");
            return;
        }
        Debug.Log("[MainMenu] Starting LAN discovery (UDP broadcast)...");
        discovery.StartDiscovery();
        discovery.BroadcastDiscoveryRequest();

        if (_discoveryBurstRoutine != null)
            StopCoroutine(_discoveryBurstRoutine);
        _discoveryBurstRoutine = StartCoroutine(BurstDiscoveryRequests(discovery));

        if (_subnetSweepRoutine != null)
            StopCoroutine(_subnetSweepRoutine);
        _subnetSweepRoutine = StartCoroutine(SweepLocalSubnet(discovery));
    }

    private void OnServerFound(DiscoveryResponse response)
    {
        Debug.Log($"[MainMenu] Server discovered: {response.HostName} @ {response.uri} ({response.TimerLabel})");

        // Avoid duplicates
        foreach (var s in _foundServers)
            if (s.uri?.ToString() == response.uri?.ToString()) return;

        _foundServers.Add(response);
        AddServerRow(response);
    }

    private void ClearServerList()
    {
        _foundServers.Clear();
        if (_serverListContent == null) return;
        foreach (Transform child in _serverListContent)
            Destroy(child.gameObject);
    }

    private void AddServerRow(DiscoveryResponse response)
    {
        if (_serverListContent == null)
        {
            Debug.LogError("[MainMenu] _serverListContent is null — cannot add server row.");
            return;
        }

        var rowGO = new GameObject("ServerRow", typeof(RectTransform), typeof(Image));
        rowGO.transform.SetParent(_serverListContent, false);

        // LayoutElement tells VerticalLayoutGroup how tall each row is
        var le          = rowGO.AddComponent<LayoutElement>();
        le.preferredHeight = 90f;
        le.flexibleWidth   = 1f;

        rowGO.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.25f, 1f);

        MakeText("Label", rowGO.transform,
                 new Vector2(0.02f, 0f), new Vector2(0.70f, 1f),
                 $"{response.HostName}  •  {response.TimerLabel}",
                 42, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);

        var joinBtn = MakeButton("BtnJoin", rowGO.transform,
                                 new Vector2(0.72f, 0.1f), new Vector2(0.98f, 0.9f),
                                 "Join");
        Uri uri = response.uri;
        joinBtn.onClick.AddListener(() => OnJoinServer(uri));

        // Force the scroll content to resize immediately
        LayoutRebuilder.ForceRebuildLayoutImmediate(
            _serverListContent.GetComponent<RectTransform>());
    }

    private void OnJoinServer(Uri uri)
    {
        PlayerPrefs.SetString("GameMode", "lanClient");
        PlayerPrefs.Save();

        var mgr = LanNetworkManager.Instance;
        if (mgr == null) return;

        StopDiscoveryRoutines();
        mgr.GetComponent<LanDiscovery>()?.StopDiscovery();
        mgr.StartClient(uri);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PANEL MANAGEMENT
    // ═════════════════════════════════════════════════════════════════════════

    private void ShowPanel(GameObject panel)
    {
        _mainPanel?.SetActive(false);
        _pvpPanel?.SetActive(false);
        _timerSelectPanel?.SetActive(false);
        _lanPanel?.SetActive(false);
        _hostLobbyPanel?.SetActive(false);
        _joinLobbyPanel?.SetActive(false);
        _settingsPanel?.SetActive(false);

        if (panel == _hostLobbyPanel)
        {
            _selectedTimerPreset = PlayerPrefs.GetString("TimerPreset", _selectedTimerPreset);
            RefreshTimerSelectionUi();
            ResetHostLobbyState();
        }

        panel?.SetActive(true);
    }

    private void ResetHostLobbyState()
    {
        if (_hostStatusLabel != null)
            _hostStatusLabel.text = "";
    }

    private void RefreshTimerSelectionUi()
    {
        if (_selectedTimerLabel != null)
            _selectedTimerLabel.text = $"Selected: {FormatTimerPreset(_selectedTimerPreset)}";

        if (_modeTimerSelectedLabel != null)
            _modeTimerSelectedLabel.text = $"Selected: {FormatTimerPreset(_selectedTimerPreset)}";

        string[] presets = { "unlimited", "1", "3", "5", "10", "30" };
        for (int i = 0; i < _timerButtons.Count; i++)
        {
            var button = _timerButtons[i];
            if (button == null) continue;

            var image = button.GetComponent<Image>();
            if (image == null) continue;

            image.color = presets[i % presets.Length] == _selectedTimerPreset
                ? buttonHoverColor
                : buttonColor;
        }
    }

    private void BuildTimerPresetButtons(Transform parent, float yMin, float yMax)
    {
        string[] labels  = { "Unlimited", "1 min", "3 min", "5 min", "10 min", "30 min" };
        string[] presets = { "unlimited", "1", "3", "5", "10", "30" };
        float btnW = 1f / labels.Length;

        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var timerButton = MakeButton($"BtnTimer{presets[i]}", parent,
                       new Vector2(i * btnW + 0.01f, yMin),
                       new Vector2((i + 1) * btnW - 0.01f, yMax),
                       labels[i]);
            _timerButtons.Add(timerButton);
            timerButton.onClick.AddListener(() =>
            {
                _selectedTimerPreset = presets[idx];
                RefreshTimerSelectionUi();
                Debug.Log($"[MainMenu] Timer preset: {_selectedTimerPreset}");
            });
        }
    }

    private static string FormatTimerPreset(string preset) => preset switch
    {
        "1" => "1 min",
        "3" => "3 min",
        "5" => "5 min",
        "10" => "10 min",
        "30" => "30 min",
        _ => "Unlimited"
    };

    private void StopDiscoveryRoutines()
    {
        if (_discoveryBurstRoutine != null)
        {
            StopCoroutine(_discoveryBurstRoutine);
            _discoveryBurstRoutine = null;
        }

        if (_subnetSweepRoutine != null)
        {
            StopCoroutine(_subnetSweepRoutine);
            _subnetSweepRoutine = null;
        }

    }

    private System.Collections.IEnumerator BurstDiscoveryRequests(LanDiscovery discovery)
    {
        for (int i = 0; i < 8; i++)
        {
            if (discovery == null)
                yield break;

            discovery.BroadcastDiscoveryRequest();
            yield return new WaitForSecondsRealtime(0.25f);
        }

        _discoveryBurstRoutine = null;
    }

    private System.Collections.IEnumerator SweepLocalSubnet(LanDiscovery discovery)
    {
        foreach (IPAddress address in discovery.GetLikelyLanAddresses())
        {
            if (discovery == null)
                yield break;

            discovery.SendDiscoveryRequestTo(address);
            yield return null;
        }

        _subnetSweepRoutine = null;
    }

    private static GameObject MakePanel(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt       = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private Button MakeButton(string name, Transform parent,
                              Vector2 anchorMin, Vector2 anchorMax, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        if (buttonSprite != null) { img.sprite = buttonSprite; img.type = Image.Type.Sliced; }
        img.color = buttonColor;

        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        var colors              = btn.colors;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor     = new Color(buttonColor.r * 0.7f, buttonColor.g * 0.7f,
                                            buttonColor.b * 0.7f, 1f);
        btn.colors = colors;

        MakeText($"{name}Label", go.transform,
                 Vector2.zero, Vector2.one,
                 label, 55, FontStyle.Bold, buttonTextColor, TextAnchor.MiddleCenter);
        return btn;
    }

    private Text MakeText(string name, Transform parent,
                          Vector2 anchorMin, Vector2 anchorMax,
                          string content, int fontSize, FontStyle style,
                          Color color, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var txt       = go.GetComponent<Text>();
        txt.text      = content; txt.fontSize = fontSize; txt.fontStyle = style;
        txt.color     = color;   txt.alignment = alignment;
        txt.font      = GetFont(); txt.raycastTarget = false;
        return txt;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private Font GetFont()
    {
        if (uiFont != null) return uiFont;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
