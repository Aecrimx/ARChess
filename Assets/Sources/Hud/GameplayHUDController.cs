using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Sources.Hud
{
    // —————————————————————————————————————————————————————————————————————————————
    //  GameplayHUDController
    //
    //  RESPONSIBILITY:
    //  - Update the timer display and turn indicator during gameplay.
    //  - Build a lightweight in-game menu button.
    //  - Show a confirmation dialog before leaving the current match.
    //
    //  PHONE BACK BUTTON:
    //  Unity maps Android's hardware back button to KeyCode.Escape, so the
    //  same flow is used for the on-screen Menu button and the phone back key.
    // —————————————————————————————————————————————————————————————————————————————
    public class GameplayHUDController : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TMP_Text playerTimerText;
        [SerializeField] private TMP_Text opponentTimerText;
        [SerializeField] private TMP_Text turnIndicatorText;

        [Header("Settings")]
        [Tooltip("True if the local player is White, False if Black.")]
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
        private Chess2DInputHandler _inputHandler;
        private bool _exitDialogOpen;

        /// <summary>
        /// Called at game-start by ChessNetworkProxy.RpcGameStarted (LAN)
        /// or by GameModeManager (local modes) to set the HUD perspective.
        /// </summary>
        public void SetLocalPlayerIsWhite(bool value)
        {
            isPlayerWhite = value;
            if (GameStateManager.Instance != null)
                UpdateTurnIndicator(GameStateManager.Instance.IsWhiteTurn);
        }

        private void Start()
        {
            ApplyDefaultLocalColorFromMode();
            GameEvents.OnTurnChanged += HandleTurnChanged;
            GameEvents.OnBoardReset += HandleBoardReset;
            GameEvents.OnGameOver += HandleGameOver;

            BuildInGameMenu();

            bool isWhiteTurn = GameStateManager.Instance == null || GameStateManager.Instance.IsWhiteTurn;
            UpdateTurnIndicator(isWhiteTurn);
        }

        private void OnDestroy()
        {
            GameEvents.OnTurnChanged -= HandleTurnChanged;
            GameEvents.OnBoardReset -= HandleBoardReset;
            GameEvents.OnGameOver -= HandleGameOver;
        }

        private void Update()
        {
            if (WasExitPressedThisFrame())
                HandleExitIntent();

            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Result != GameResult.Ongoing) return;

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

            if (_menuButtonRoot != null)
                _menuButtonRoot.SetActive(true);

            if (_exitDialogOpen)
                SetExitDialogVisible(false);
        }

        private void HandleGameOver(GameResult _)
        {
            if (_menuButtonRoot != null)
                _menuButtonRoot.SetActive(false);

            if (_exitDialogOpen)
                SetExitDialogVisible(false, restoreInput: false);
        }

        private void HandleExitIntent()
        {
            SetExitDialogVisible(!_exitDialogOpen);
        }

        private void UpdateTurnIndicator(bool isWhiteTurn)
        {
            if (turnIndicatorText == null) return;

            if (isWhiteTurn == isPlayerWhite)
            {
                turnIndicatorText.text = "Your turn";
                turnIndicatorText.color = playerTurnColor;
            }
            else
            {
                turnIndicatorText.text = "Opponent's turn";
                turnIndicatorText.color = opponentTurnColor;
            }
        }

        private void UpdateTimers(GameStateManager gsm)
        {
            float playerTime = isPlayerWhite ? gsm.WhiteTimeRemaining : gsm.BlackTimeRemaining;
            float opponentTime = isPlayerWhite ? gsm.BlackTimeRemaining : gsm.WhiteTimeRemaining;

            if (playerTimerText != null)
                playerTimerText.text = FormatTime(playerTime);

            if (opponentTimerText != null)
                opponentTimerText.text = FormatTime(opponentTime);
        }

        private void ApplyDefaultLocalColorFromMode()
        {
            if (GameModeManager.Instance != null && GameModeManager.Instance.IsLanClient)
                isPlayerWhite = false;
        }

        private void BuildInGameMenu()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[GameplayHUDController] No Canvas found for in-game menu.");
                return;
            }

            _menuButtonRoot = MakeButton(
                "InGameMenuButton",
                canvas.transform,
                new Vector2(0.03f, 0.93f),
                new Vector2(0.20f, 0.985f),
                "Menu",
                menuPrimaryButtonColor,
                menuTextColor,
                34,
                menuButtonSprite).gameObject;
            _menuButtonRoot.GetComponent<Button>().onClick.AddListener(HandleExitIntent);

            _exitDialogOverlay = MakeImage(
                "ExitDialogOverlay",
                canvas.transform,
                menuOverlayColor,
                Vector2.zero,
                Vector2.one);
            _exitDialogOverlay.GetComponent<Image>().raycastTarget = true;

            var panel = MakeImage(
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
                "",
                34,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);

            var stayButton = MakeButton(
                "StayButton",
                panel.transform,
                new Vector2(0.08f, 0.10f),
                new Vector2(0.46f, 0.28f),
                "Stay",
                menuSecondaryButtonColor,
                menuTextColor,
                34,
                menuButtonSprite);
            stayButton.onClick.AddListener(() => SetExitDialogVisible(false));

            var leaveButton = MakeButton(
                "LeaveButton",
                panel.transform,
                new Vector2(0.54f, 0.10f),
                new Vector2(0.92f, 0.28f),
                "Exit to Menu",
                menuPrimaryButtonColor,
                menuTextColor,
                34,
                menuButtonSprite);
            leaveButton.onClick.AddListener(ConfirmExitToMenu);

            _exitDialogOverlay.SetActive(false);
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
                _exitDialogOverlay.SetActive(visible);

            if (_exitDialogBodyText != null)
                _exitDialogBodyText.text = BuildExitPrompt();

            if (visible)
            {
                SetBoardInputActive(false);
            }
            else if (restoreInput && (GameStateManager.Instance == null || GameStateManager.Instance.Result == GameResult.Ongoing))
            {
                SetBoardInputActive(true);
            }
        }

        private string BuildExitPrompt()
        {
            var gmm = GameModeManager.Instance;
            if (gmm != null && gmm.IsLan)
                return "Are you sure you want to leave this LAN match?\nYou will disconnect and return to the main menu.";

            return "Are you sure you want to leave this game and return to the main menu?";
        }

        private void SetBoardInputActive(bool active)
        {
            _inputHandler ??= GetComponent<Chess2DInputHandler>();
            _inputHandler ??= FindAnyObjectByType<Chess2DInputHandler>();

            if (_inputHandler == null) return;

            if (active) _inputHandler.Activate();
            else        _inputHandler.Deactivate();
        }

        private string FormatTime(float timeInSeconds)
        {
            if (timeInSeconds >= float.MaxValue) return "\u221e";
            int minutes = Mathf.FloorToInt(timeInSeconds / 60F);
            int seconds = Mathf.FloorToInt(timeInSeconds - minutes * 60);
            return string.Format("{0:00}:{1:00}", minutes, seconds);
        }

        private TMP_Text MakeText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                  string content, int fontSize, FontStyle fontStyle, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.color = menuTextColor;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = ConvertAlignment(alignment);
            text.font = GetMenuFontAsset();
            text.raycastTarget = false;

            return text;
        }

        private GameObject MakeImage(string name, Transform parent, Color color,
                                     Vector2 anchorMin, Vector2 anchorMax, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            return go;
        }

        private Button MakeButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                  string label, Color backgroundColor, Color textColor,
                                  int fontSize, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = backgroundColor;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;

            var labelText = MakeText(
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

        private TMP_FontAsset GetMenuFontAsset()
        {
            if (turnIndicatorText != null && turnIndicatorText.font != null)
                return turnIndicatorText.font;

            if (playerTimerText != null && playerTimerText.font != null)
                return playerTimerText.font;

            if (opponentTimerText != null && opponentTimerText.font != null)
                return opponentTimerText.font;

            return TMP_Settings.defaultFontAsset;
        }

        private static TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
        {
            return alignment switch
            {
                TextAnchor.MiddleLeft => TextAlignmentOptions.MidlineLeft,
                TextAnchor.MiddleRight => TextAlignmentOptions.MidlineRight,
                _ => TextAlignmentOptions.Midline,
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
