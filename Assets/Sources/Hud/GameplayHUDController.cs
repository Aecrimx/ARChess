using UnityEngine;
using TMPro;

namespace Sources.Hud
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  GameplayHUDController
    //
    //  Updates the Timer and Turn Indicator during gameplay.
    // ─────────────────────────────────────────────────────────────────────────────
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

        private void Start()
        {
            GameEvents.OnTurnChanged += HandleTurnChanged;
            GameEvents.OnBoardReset += HandleBoardReset;
            UpdateTurnIndicator(GameStateManager.Instance.IsWhiteTurn);
        }

        private void OnDestroy()
        {
            GameEvents.OnTurnChanged -= HandleTurnChanged;
            GameEvents.OnBoardReset -= HandleBoardReset;
        }

        private void Update()
        {
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
            float playerTime   = isPlayerWhite ? gsm.WhiteTimeRemaining : gsm.BlackTimeRemaining;
            float opponentTime = isPlayerWhite ? gsm.BlackTimeRemaining : gsm.WhiteTimeRemaining;

            if (playerTimerText != null)
                playerTimerText.text = FormatTime(playerTime);
            
            if (opponentTimerText != null)
                opponentTimerText.text = FormatTime(opponentTime);
        }

        private string FormatTime(float timeInSeconds)
        {
            int minutes = Mathf.FloorToInt(timeInSeconds / 60F);
            int seconds = Mathf.FloorToInt(timeInSeconds - minutes * 60);
            return string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }
}
