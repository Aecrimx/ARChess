using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Sources.Hud
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  CapturedPiecesController
    //
    //  Listens for game events and updates the display of captured pieces.
    //  Identical pieces are stacked horizontally.
    // ─────────────────────────────────────────────────────────────────────────────
    public class CapturedPiecesController : MonoBehaviour
    {
        [Header("UI Containers")]
        [Tooltip("The RectTransform where Black pieces captured by White will be parented.")]
        [SerializeField] private RectTransform whiteCaptureContainer;

        [Tooltip("The RectTransform where White pieces captured by Black will be parented.")]
        [SerializeField] private RectTransform blackCaptureContainer;

        [Header("Sprites")]
        [Tooltip("Order: WPawn, WKnight, WBishop, WRook, WQueen, WKing, BPawn, BKnight, BBishop, BRook, BQueen, BKing")]
        public Sprite[] pieceSprites = new Sprite[12];

        [Header("Layout Settings")]
        [SerializeField] private float pieceSize = 35f;
        [SerializeField] private float stackOffset = 12f;   // How much identical pieces overlap
        [SerializeField] private float groupSpacing = 40f;  // Gap between different piece types

        private void Start()
        {
            GameEvents.OnMoveMade += HandleMoveMade;
            GameEvents.OnBoardReset += RedrawAll;
            RedrawAll();
        }

        private void OnDestroy()
        {
            GameEvents.OnMoveMade -= HandleMoveMade;
            GameEvents.OnBoardReset -= RedrawAll;
        }

        private void HandleMoveMade(MoveRecord move)
        {
            // If a piece was captured in this move, refresh the display
            if (move.PieceCaptured != Piece.None)
            {
                RedrawAll();
            }
        }

        private void RedrawAll()
        {
            if (GameStateManager.Instance == null) return;

            RedrawContainer(whiteCaptureContainer, GameStateManager.Instance.CapturedByWhite);
            RedrawContainer(blackCaptureContainer, GameStateManager.Instance.CapturedByBlack);
        }

        private void RedrawContainer(RectTransform container, List<Piece> capturedPieces)
        {
            if (container == null) return;

            // Clear all existing child icons
            foreach (Transform child in container)
            {
                Destroy(child.gameObject);
            }

            if (capturedPieces == null || capturedPieces.Count == 0) return;

            // Group identically captured pieces (e.g. 3 pawns together)
            // Sort by importance (Queen = 5, Rook = 4, Bishop = 3, Knight = 2, Pawn = 1)
            var groups = capturedPieces
                .GroupBy(p => p)
                .OrderByDescending(g => Mathf.Abs((int)g.Key)) 
                .ToList();

            float currentX = 0f;

            foreach (var group in groups)
            {
                Piece pieceType = group.Key;
                int count = group.Count();
                Sprite sprite = GetSpriteForPiece(pieceType);

                for (int i = 0; i < count; i++)
                {
                    GameObject go = new GameObject($"Captured_{pieceType}_{i}");
                    
                    // We parent it to our container
                    go.transform.SetParent(container, false);

                    Image img = go.AddComponent<Image>();
                    img.sprite = sprite;
                    img.preserveAspect = true;

                    RectTransform rt = go.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(pieceSize, pieceSize);
                    

                    rt.anchorMin = new Vector2(0, 0.5f);
                    rt.anchorMax = new Vector2(0, 0.5f);
                    rt.pivot = new Vector2(0, 0.5f);

                    // Position the piece. Stack identical ones using stackOffset.
                    rt.anchoredPosition = new Vector2(currentX + (i * stackOffset), 0f);

                    // By letting earlier indices (i=0) render first and (i=1) render after, 
                    // i=1 sits ON TOP in Unity UI logic.
                    // Visual logic is basically to "stack behind" the first appearence
                    if (i > 0)
                    {
                        go.transform.SetAsFirstSibling();
                    }
                }

                // Advance X position for the next piece type group
                // The space taken by this group is the size of one full spacing unit PLUS the overlaps.
                currentX += groupSpacing + ((count - 1) * stackOffset);
            }
        }

        private Sprite GetSpriteForPiece(Piece p)
        {
            if (pieceSprites == null || pieceSprites.Length == 0) return null;

            int index = -1;
            switch (p)
            {
                case Piece.WhitePawn:   index = 0; break;
                case Piece.WhiteKnight: index = 1; break;
                case Piece.WhiteBishop: index = 2; break;
                case Piece.WhiteRook:   index = 3; break;
                case Piece.WhiteQueen:  index = 4; break;
                case Piece.WhiteKing:   index = 5; break;

                case Piece.BlackPawn:   index = 6; break;
                case Piece.BlackKnight: index = 7; break;
                case Piece.BlackBishop: index = 8; break;
                case Piece.BlackRook:   index = 9; break;
                case Piece.BlackQueen:  index = 10; break;
                case Piece.BlackKing:   index = 11; break;
            }

            if (index >= 0 && index < pieceSprites.Length)
            {
                return pieceSprites[index];
            }
            
            return null;
        }
    }
}
