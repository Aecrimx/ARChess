using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Sources.Hud
{
    public class GameplayHUDController : MonoBehaviour
    {
        private const float ArBottomBarWithControlsMinY = 0.165f;
        private const float ArBottomBarWithControlsMaxY = 0.295f;
        private const float ArBottomBarCollapsedMinY = 0f;
        private const float ArBottomBarCollapsedMaxY = 0.14f;

        [Header("UI Elements")]
        [SerializeField] private TMP_Text playerTimerText;
        [SerializeField] private TMP_Text opponentTimerText;
        [SerializeField] private TMP_Text turnIndicatorText;

        [Header("Settings")]
        public bool isPlayerWhite = true;

        [Header("Colors")]
        [SerializeField] private Color playerTurnColor = Color.green;
        [SerializeField] private Color opponentTurnColor = Color.red;

        [Header("In-Game Menu")]
        [SerializeField] private Sprite menuButtonSprite;
        [SerializeField] private Sprite menuPanelSprite;
        [SerializeField] private Color menuOverlayColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color menuPanelColor = new Color(0.18f, 0.14f, 0.24f, 1f);
        [SerializeField] private Color menuPrimaryButtonColor = new Color(0.59f, 0.48f, 0.71f, 1f);
        [SerializeField] private Color menuSecondaryButtonColor = new Color(0.32f, 0.28f, 0.40f, 1f);
        [SerializeField] private Color menuTextColor = new Color(0.98f, 0.94f, 0.90f, 1f);

        private GameObject _menuButtonRoot;
        private GameObject _exitDialogOverlay;
        private TMP_Text _exitDialogBodyText;
        private bool _exitDialogOpen;

        private Button _arToggleButton;
        private TMP_Text _arToggleButtonLabel;
        private TMP_Text _arAvailabilityText;

        private GameObject _arHudRoot;
        private GameObject _arTopBar;
        private GameObject _arBottomBar;
        private RectTransform _arBottomBarRect;
        private TMP_Text _arOpponentTimerText;
        private TMP_Text _arPlayerTimerText;
        private TMP_Text _arTurnIndicatorText;
        private RectTransform _arWhiteCaptureContainer;
        private RectTransform _arBlackCaptureContainer;
        private GameObject _arControlsPanel;
        private GameObject _arControlsToggleRoot;
        private bool _arControlsVisible = true;

        private ChessViewModeController _viewModeController;
        private CapturedPiecesController _capturedPiecesController;

        public void SetLocalPlayerIsWhite(bool value)
        {
            isPlayerWhite = value;
            ApplyARCapturePlacement();

            if (GameStateManager.Instance != null)
            {
                UpdateTurnIndicator(GameStateManager.Instance.IsWhiteTurn);
                UpdateTimers(GameStateManager.Instance);
            }
        }

        private void Start()
        {
            ApplyDefaultLocalColorFromMode();
            GameEvents.OnTurnChanged += HandleTurnChanged;
            GameEvents.OnBoardReset += HandleBoardReset;
            GameEvents.OnGameOver += HandleGameOver;

            _viewModeController = ChessViewModeController.EnsureInScene();
            if (_viewModeController != null)
            {
                _viewModeController.StateChanged += HandleViewModeStateChanged;
            }

            _capturedPiecesController = GetComponent<CapturedPiecesController>();
            if (_capturedPiecesController == null)
            {
                _capturedPiecesController = FindAnyObjectByType<CapturedPiecesController>();
            }

            BuildInGameMenu();

            bool isWhiteTurn = GameStateManager.Instance == null || GameStateManager.Instance.IsWhiteTurn;
            UpdateTurnIndicator(isWhiteTurn);
            if (GameStateManager.Instance != null)
            {
                UpdateTimers(GameStateManager.Instance);
            }

            RefreshARUiState();
        }

        private void OnDestroy()
        {
            GameEvents.OnTurnChanged -= HandleTurnChanged;
            GameEvents.OnBoardReset -= HandleBoardReset;
            GameEvents.OnGameOver -= HandleGameOver;

            if (_viewModeController != null)
            {
                _viewModeController.StateChanged -= HandleViewModeStateChanged;
            }
        }

        private void Update()
        {
            if (WasExitPressedThisFrame())
            {
                HandleExitIntent();
            }

            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Result != GameResult.Ongoing)
            {
                return;
            }

            UpdateTimers(gsm);
        }

        private void HandleTurnChanged(bool isWhiteTurn)
        {
            UpdateTurnIndicator(isWhiteTurn);
        }

        private void HandleBoardReset()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                UpdateTurnIndicator(gsm.IsWhiteTurn);
                UpdateTimers(gsm);
            }

            if (_exitDialogOpen)
            {
                SetExitDialogVisible(false);
            }

            RefreshARUiState();
        }

        private void HandleGameOver(GameResult _)
        {
            SetTwoDMatchHudVisible(false);
            SetMainControlsVisible(false);
            SetARHudVisible(false);

            if (_exitDialogOpen)
            {
                SetExitDialogVisible(false, restoreInput: false);
            }
        }

        private void HandleViewModeStateChanged()
        {
            RefreshARUiState();
        }

        private void HandleExitIntent()
        {
            SetExitDialogVisible(!_exitDialogOpen);
        }

        private void UpdateTurnIndicator(bool isWhiteTurn)
        {
            string label;
            Color color;
            if (isWhiteTurn == isPlayerWhite)
            {
                label = "Your turn";
                color = playerTurnColor;
            }
            else
            {
                label = "Opponent's turn";
                color = opponentTurnColor;
            }

            if (turnIndicatorText != null)
            {
                turnIndicatorText.text = label;
                turnIndicatorText.color = color;
            }

            if (_arTurnIndicatorText != null)
            {
                _arTurnIndicatorText.text = label;
                _arTurnIndicatorText.color = color;
            }
        }

        private void UpdateTimers(GameStateManager gsm)
        {
            float playerTime = isPlayerWhite ? gsm.WhiteTimeRemaining : gsm.BlackTimeRemaining;
            float opponentTime = isPlayerWhite ? gsm.BlackTimeRemaining : gsm.WhiteTimeRemaining;
            string playerTimeText = FormatTime(playerTime);
            string opponentTimeText = FormatTime(opponentTime);

            if (playerTimerText != null)
            {
                playerTimerText.text = playerTimeText;
            }

            if (opponentTimerText != null)
            {
                opponentTimerText.text = opponentTimeText;
            }

            if (_arPlayerTimerText != null)
            {
                _arPlayerTimerText.text = playerTimeText;
            }

            if (_arOpponentTimerText != null)
            {
                _arOpponentTimerText.text = opponentTimeText;
            }
        }

        private void ApplyDefaultLocalColorFromMode()
        {
            if (GameModeManager.Instance != null && GameModeManager.Instance.IsLanClient)
            {
                isPlayerWhite = false;
            }
        }

        private void BuildInGameMenu()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = FindAnyObjectByType<Canvas>();
            }

            if (canvas == null)
            {
                Debug.LogWarning("[GameplayHUDController] No Canvas found for in-game UI.");
                return;
            }

            _arToggleButton = MakeButton(
                "ARToggleButton",
                canvas.transform,
                new Vector2(0.58f, 0.93f),
                new Vector2(0.78f, 0.985f),
                "Enter AR",
                menuSecondaryButtonColor,
                menuTextColor,
                30,
                menuButtonSprite);
            _arToggleButton.onClick.AddListener(HandleARTogglePressed);
            _arToggleButtonLabel = _arToggleButton.GetComponentInChildren<TextMeshProUGUI>();

            _menuButtonRoot = MakeButton(
                "InGameMenuButton",
                canvas.transform,
                new Vector2(0.80f, 0.93f),
                new Vector2(0.97f, 0.985f),
                "Menu",
                menuPrimaryButtonColor,
                menuTextColor,
                34,
                menuButtonSprite).gameObject;
            _menuButtonRoot.GetComponent<Button>().onClick.AddListener(HandleExitIntent);

            _arAvailabilityText = MakeText(
                "ARAvailabilityText",
                canvas.transform,
                new Vector2(0.03f, 0.90f),
                new Vector2(0.55f, 0.965f),
                string.Empty,
                22,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);

            BuildARHud(canvas.transform);
            BuildExitDialog(canvas.transform);
        }

        private void BuildARHud(Transform parent)
        {
            _arHudRoot = new GameObject("ARMatchHud", typeof(RectTransform));
            _arHudRoot.transform.SetParent(parent, false);
            StretchFull(_arHudRoot.GetComponent<RectTransform>());

            Color arBarColor = new Color(menuPanelColor.r, menuPanelColor.g, menuPanelColor.b, 0.82f);
            _arTopBar = MakeImage(
                "ARTopBar",
                _arHudRoot.transform,
                arBarColor,
                new Vector2(0f, 0.875f),
                Vector2.one,
                menuPanelSprite);

            _arBottomBar = MakeImage(
                "ARBottomBar",
                _arHudRoot.transform,
                arBarColor,
                new Vector2(0f, ArBottomBarWithControlsMinY),
                new Vector2(1f, ArBottomBarWithControlsMaxY),
                menuPanelSprite);
            _arBottomBarRect = _arBottomBar.GetComponent<RectTransform>();

            _arOpponentTimerText = MakeText(
                "AROpponentTimer",
                _arTopBar.transform,
                new Vector2(0.43f, 0.12f),
                new Vector2(0.56f, 0.88f),
                "00:00",
                34,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            MakeButton(
                "ARMenuTop",
                _arTopBar.transform,
                new Vector2(0.80f, 0.18f),
                new Vector2(0.97f, 0.86f),
                "Menu",
                menuPrimaryButtonColor,
                menuTextColor,
                30,
                menuButtonSprite).onClick.AddListener(HandleExitIntent);

            _arTurnIndicatorText = MakeText(
                "ARTurnIndicator",
                _arBottomBar.transform,
                new Vector2(0.04f, 0.12f),
                new Vector2(0.24f, 0.88f),
                "Your turn",
                34,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);

            _arPlayerTimerText = MakeText(
                "ARPlayerTimer",
                _arBottomBar.transform,
                new Vector2(0.58f, 0.12f),
                new Vector2(0.76f, 0.88f),
                "00:00",
                34,
                FontStyle.Bold,
                TextAnchor.MiddleRight);

            _arWhiteCaptureContainer = MakeRectTransform("ARWhiteCaptureContainer", _arBottomBar.transform);
            _arBlackCaptureContainer = MakeRectTransform("ARBlackCaptureContainer", _arTopBar.transform);
            _capturedPiecesController?.RegisterAdditionalContainers(_arWhiteCaptureContainer, _arBlackCaptureContainer);
            ApplyARCapturePlacement();

            BuildARControlsPanel(_arHudRoot.transform);
            SetARControlsVisible(true);
            _arHudRoot.SetActive(false);
        }

        private void BuildARControlsPanel(Transform parent)
        {
            _arControlsPanel = MakeImage(
                "ARControlsPanel",
                parent,
                new Color(menuPanelColor.r, menuPanelColor.g, menuPanelColor.b, 0.88f),
                new Vector2(0.04f, 0f),
                new Vector2(0.96f, 0.15f),
                menuPanelSprite);

            MakeButton(
                "ARReposition",
                _arControlsPanel.transform,
                new Vector2(0.02f, 0.12f),
                new Vector2(0.145f, 0.88f),
                "Reposition",
                menuPrimaryButtonColor,
                menuTextColor,
                18,
                menuButtonSprite).onClick.AddListener(() => _viewModeController?.GetARInput()?.RepositionBoard());

            MakeButton(
                "ARRotateLeft",
                _arControlsPanel.transform,
                new Vector2(0.155f, 0.12f),
                new Vector2(0.28f, 0.88f),
                "Rotate -",
                menuSecondaryButtonColor,
                menuTextColor,
                18,
                menuButtonSprite).onClick.AddListener(() => _viewModeController?.GetARInput()?.RotateBoard(-15f));

            MakeButton(
                "ARRotateRight",
                _arControlsPanel.transform,
                new Vector2(0.29f, 0.12f),
                new Vector2(0.415f, 0.88f),
                "Rotate +",
                menuSecondaryButtonColor,
                menuTextColor,
                18,
                menuButtonSprite).onClick.AddListener(() => _viewModeController?.GetARInput()?.RotateBoard(15f));

            MakeButton(
                "ARScaleDown",
                _arControlsPanel.transform,
                new Vector2(0.425f, 0.12f),
                new Vector2(0.55f, 0.88f),
                "Scale -",
                menuSecondaryButtonColor,
                menuTextColor,
                18,
                menuButtonSprite).onClick.AddListener(() => _viewModeController?.GetARInput()?.AdjustBoardScale(-0.1f));

            MakeButton(
                "ARScaleUp",
                _arControlsPanel.transform,
                new Vector2(0.56f, 0.12f),
                new Vector2(0.685f, 0.88f),
                "Scale +",
                menuSecondaryButtonColor,
                menuTextColor,
                18,
                menuButtonSprite).onClick.AddListener(() => _viewModeController?.GetARInput()?.AdjustBoardScale(0.1f));

            MakeButton(
                "ARReturn2DControls",
                _arControlsPanel.transform,
                new Vector2(0.695f, 0.12f),
                new Vector2(0.84f, 0.88f),
                "Return to 2D",
                menuPrimaryButtonColor,
                menuTextColor,
                16,
                menuButtonSprite).onClick.AddListener(() => _viewModeController?.ExitARMode());

            MakeButton(
                "ARHideControls",
                _arControlsPanel.transform,
                new Vector2(0.85f, 0.12f),
                new Vector2(0.98f, 0.88f),
                "Hide",
                menuPrimaryButtonColor,
                menuTextColor,
                22,
                menuButtonSprite).onClick.AddListener(() => SetARControlsVisible(false));

            _arControlsToggleRoot = MakeButton(
                "ARShowControlsButton",
                _arBottomBar.transform,
                new Vector2(0.78f, 0.16f),
                new Vector2(0.97f, 0.84f),
                "Controls",
                menuPrimaryButtonColor,
                menuTextColor,
                22,
                menuButtonSprite).gameObject;
            _arControlsToggleRoot.GetComponent<Button>().onClick.AddListener(() => SetARControlsVisible(true));
        }

        private void BuildExitDialog(Transform parent)
        {
            _exitDialogOverlay = MakeImage(
                "ExitDialogOverlay",
                parent,
                menuOverlayColor,
                Vector2.zero,
                Vector2.one);
            _exitDialogOverlay.GetComponent<Image>().raycastTarget = true;

            GameObject panel = MakeImage(
                "ExitDialogPanel",
                _exitDialogOverlay.transform,
                menuPanelColor,
                new Vector2(0.16f, 0.30f),
                new Vector2(0.84f, 0.70f),
                menuPanelSprite);

            MakeText(
                "ExitDialogTitle",
                panel.transform,
                new Vector2(0.08f, 0.66f),
                new Vector2(0.92f, 0.90f),
                "Leave match?",
                54,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            _exitDialogBodyText = MakeText(
                "ExitDialogBody",
                panel.transform,
                new Vector2(0.08f, 0.38f),
                new Vector2(0.92f, 0.64f),
                string.Empty,
                34,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);

            MakeButton(
                "StayButton",
                panel.transform,
                new Vector2(0.08f, 0.10f),
                new Vector2(0.46f, 0.28f),
                "Stay",
                menuSecondaryButtonColor,
                menuTextColor,
                34,
                menuButtonSprite).onClick.AddListener(() => SetExitDialogVisible(false));

            MakeButton(
                "LeaveButton",
                panel.transform,
                new Vector2(0.54f, 0.10f),
                new Vector2(0.92f, 0.28f),
                "Exit to Menu",
                menuPrimaryButtonColor,
                menuTextColor,
                34,
                menuButtonSprite).onClick.AddListener(ConfirmExitToMenu);

            _exitDialogOverlay.SetActive(false);
        }

        private void HandleARTogglePressed()
        {
            _viewModeController ??= ChessViewModeController.EnsureInScene();
            _viewModeController?.ToggleARMode();
        }

        private void ConfirmExitToMenu()
        {
            SetExitDialogVisible(false, restoreInput: false);
            GameModeManager.Instance?.ExitCurrentGameToMainMenu();
        }

        private void SetExitDialogVisible(bool visible, bool restoreInput = true)
        {
            _exitDialogOpen = visible;

            if (_exitDialogOverlay != null)
            {
                _exitDialogOverlay.SetActive(visible);
            }

            if (_exitDialogBodyText != null)
            {
                _exitDialogBodyText.text = BuildExitPrompt();
            }

            if (visible)
            {
                SetBoardInputActive(false);
            }
            else if (restoreInput && IsGameOngoing())
            {
                SetBoardInputActive(true);
            }
        }

        private string BuildExitPrompt()
        {
            var gmm = GameModeManager.Instance;
            if (gmm != null && gmm.IsLan)
            {
                return "Are you sure you want to leave this LAN match?\nYou will disconnect and return to the main menu.";
            }

            return "Are you sure you want to leave this game and return to the main menu?";
        }

        private void SetBoardInputActive(bool active)
        {
            _viewModeController ??= ChessViewModeController.EnsureInScene();
            _viewModeController?.SetActiveInputEnabled(active);
        }

        private void RefreshARUiState()
        {
            _viewModeController ??= ChessViewModeController.EnsureInScene();
            if (_viewModeController == null)
            {
                return;
            }

            bool isARModeActive = _viewModeController.IsARModeActive;
            bool showGameplayHud = IsGameOngoing();
            bool showARHud = isARModeActive && showGameplayHud;

            SetTwoDMatchHudVisible(!isARModeActive && showGameplayHud);
            SetMainControlsVisible(!isARModeActive && showGameplayHud);

            if (_arToggleButton != null)
            {
                _arToggleButton.interactable = !_viewModeController.IsCheckingAvailability && _viewModeController.CanToggleAR;
            }

            if (_arToggleButtonLabel != null)
            {
                _arToggleButtonLabel.text = "Enter AR";
            }

            if (_arHudRoot != null && showARHud && !_arHudRoot.activeSelf)
            {
                _arControlsVisible = true;
            }

            SetARHudVisible(showARHud);

            if (_arAvailabilityText != null)
            {
                if (_viewModeController.IsCheckingAvailability)
                {
                    _arAvailabilityText.text = "Checking AR support...";
                }
                else if (!isARModeActive && !_viewModeController.IsARSupported)
                {
                    _arAvailabilityText.text = _viewModeController.AvailabilityMessage;
                }
                else
                {
                    _arAvailabilityText.text = string.Empty;
                }
            }
        }

        private void SetTwoDMatchHudVisible(bool visible)
        {
            SetTextVisible(playerTimerText, visible);
            SetTextVisible(opponentTimerText, visible);
            SetTextVisible(turnIndicatorText, visible);
            _capturedPiecesController?.SetPrimaryContainersVisible(visible);
        }

        private void SetMainControlsVisible(bool visible)
        {
            if (_arToggleButton != null)
            {
                _arToggleButton.gameObject.SetActive(visible);
            }

            if (_menuButtonRoot != null)
            {
                _menuButtonRoot.SetActive(visible);
            }
        }

        private void SetARHudVisible(bool visible)
        {
            if (_arHudRoot != null)
            {
                _arHudRoot.SetActive(visible);
            }

            if (visible)
            {
                ApplyARBottomBarLayout();
                ApplyARCapturePlacement();
            }

            SetARControlsObjectsVisible(visible);
        }

        private void SetARControlsVisible(bool visible)
        {
            _arControlsVisible = visible;
            ApplyARBottomBarLayout();
            SetARControlsObjectsVisible(_arHudRoot != null && _arHudRoot.activeSelf);
        }

        private void SetARControlsObjectsVisible(bool hudVisible)
        {
            if (_arControlsPanel != null)
            {
                _arControlsPanel.SetActive(hudVisible && _arControlsVisible);
            }

            if (_arControlsToggleRoot != null)
            {
                _arControlsToggleRoot.SetActive(hudVisible && !_arControlsVisible);
            }
        }

        private void ApplyARBottomBarLayout()
        {
            if (_arBottomBarRect == null)
            {
                return;
            }

            _arBottomBarRect.anchorMin = new Vector2(
                0f,
                _arControlsVisible ? ArBottomBarWithControlsMinY : ArBottomBarCollapsedMinY);
            _arBottomBarRect.anchorMax = new Vector2(
                1f,
                _arControlsVisible ? ArBottomBarWithControlsMaxY : ArBottomBarCollapsedMaxY);
            _arBottomBarRect.offsetMin = Vector2.zero;
            _arBottomBarRect.offsetMax = Vector2.zero;
        }

        private void ApplyARCapturePlacement()
        {
            if (_arWhiteCaptureContainer == null || _arBlackCaptureContainer == null ||
                _arTopBar == null || _arBottomBar == null)
            {
                return;
            }

            RectTransform playerCaptureContainer = isPlayerWhite ? _arWhiteCaptureContainer : _arBlackCaptureContainer;
            RectTransform opponentCaptureContainer = isPlayerWhite ? _arBlackCaptureContainer : _arWhiteCaptureContainer;

            PlaceCaptureContainer(
                playerCaptureContainer,
                _arBottomBar.transform,
                new Vector2(0.26f, 0.18f),
                new Vector2(0.58f, 0.82f));
            PlaceCaptureContainer(
                opponentCaptureContainer,
                _arTopBar.transform,
                new Vector2(0.04f, 0.18f),
                new Vector2(0.42f, 0.82f));
        }

        private string FormatTime(float timeInSeconds)
        {
            if (timeInSeconds >= float.MaxValue)
            {
                return "\u221e";
            }

            int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
            int seconds = Mathf.FloorToInt(timeInSeconds - minutes * 60);
            return $"{minutes:00}:{seconds:00}";
        }

        private TMP_Text MakeText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            string content, int fontSize, FontStyle fontStyle, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TMP_Text text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.color = menuTextColor;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = ConvertAlignment(alignment);
            text.font = GetMenuFontAsset();
            text.raycastTarget = false;
            return text;
        }

        private GameObject MakeImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = go.GetComponent<Image>();
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            return go;
        }

        private Button MakeButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            string label, Color backgroundColor, Color textColor, int fontSize, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = go.GetComponent<Image>();
            image.color = backgroundColor;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            Button button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;

            TMP_Text labelText = MakeText(
                "Label",
                go.transform,
                Vector2.zero,
                Vector2.one,
                label,
                fontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            labelText.color = textColor;

            return button;
        }

        private RectTransform MakeRectTransform(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private TMP_FontAsset GetMenuFontAsset()
        {
            if (turnIndicatorText != null && turnIndicatorText.font != null)
            {
                return turnIndicatorText.font;
            }

            if (playerTimerText != null && playerTimerText.font != null)
            {
                return playerTimerText.font;
            }

            if (opponentTimerText != null && opponentTimerText.font != null)
            {
                return opponentTimerText.font;
            }

            return TMP_Settings.defaultFontAsset;
        }

        private bool IsGameOngoing()
        {
            return GameStateManager.Instance == null || GameStateManager.Instance.Result == GameResult.Ongoing;
        }

        private static void SetTextVisible(TMP_Text text, bool visible)
        {
            if (text != null)
            {
                text.gameObject.SetActive(visible);
            }
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void PlaceCaptureContainer(RectTransform rect, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (rect.parent != parent)
            {
                rect.SetParent(parent, false);
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
        {
            return alignment switch
            {
                TextAnchor.MiddleLeft => TextAlignmentOptions.MidlineLeft,
                TextAnchor.MiddleRight => TextAlignmentOptions.MidlineRight,
                _ => TextAlignmentOptions.Midline
            };
        }

        private static bool WasExitPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
