using UnityEngine;
using UnityEngine.UI;

public class Chess2DRenderer : ChessBoardRendererBase
{
    [Header("Board")]
    public RectTransform boardContainer;
    public Image boardImage;

    [Range(0f, 0.2f)]
    public float boardBorderFraction = 0f;

    [Header("Piece Sprites")]
    public Sprite[] pieceSprites = new Sprite[12];

    private readonly Image[,] _hitAreas = new Image[8, 8];
    private readonly Image[,] _highlights = new Image[8, 8];
    private readonly Image[,] _pieces = new Image[8, 8];

    private float _cellSize;
    private float _boardOffset;
    private bool _isInitialized;
    private bool _eventsSubscribed;
    private bool _localPlayerIsWhite = true;

    private void Start()
    {
        EnsureInitialized();
        ApplyDefaultPerspectiveFromMode();
        RedrawPieces();
    }

    private void OnDestroy()
    {
        if (_eventsSubscribed)
        {
            UnsubscribeFromEvents();
        }
    }

    public override void Activate()
    {
        if (!EnsureInitialized())
        {
            return;
        }

        ApplyPerspective();
        boardContainer.gameObject.SetActive(true);
        ClearAllHighlights();
        RedrawPieces();
    }

    public override void Deactivate()
    {
        if (boardContainer == null)
        {
            return;
        }

        boardContainer.gameObject.SetActive(false);
    }

    public override void SetPerspective(bool isWhite)
    {
        _localPlayerIsWhite = isWhite;
        ApplyPerspective();
    }

    public override void RedrawPieces()
    {
        if (!EnsureInitialized() || GameStateManager.Instance == null)
        {
            return;
        }

        Piece[,] board = GameStateManager.Instance.Board;
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                Piece piece = board[row, col];
                if (piece == Piece.None)
                {
                    _pieces[row, col].sprite = null;
                    _pieces[row, col].color = Color.clear;
                }
                else
                {
                    _pieces[row, col].sprite = GetSprite(piece);
                    _pieces[row, col].color = Color.white;
                }

                _pieces[row, col].rectTransform.localRotation = _localPlayerIsWhite
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, 0f, 180f);
            }
        }
    }

    public override void ClearAllHighlights()
    {
        if (!EnsureInitialized())
        {
            return;
        }

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                _highlights[row, col].color = Color.clear;
            }
        }
    }

    public override void SetHighlight(Vector2Int square, Color color)
    {
        SetHighlight(square.x, square.y, color);
    }

    public void SetHighlight(int row, int col, Color color)
    {
        if (row < 0 || row > 7 || col < 0 || col > 7)
        {
            return;
        }

        _highlights[row, col].color = color;
    }

    public Image GetHitArea(int row, int col)
    {
        if (!EnsureInitialized())
        {
            return null;
        }

        return _hitAreas[row, col];
    }

    private bool EnsureInitialized()
    {
        if (_isInitialized)
        {
            return true;
        }

        if (boardContainer == null)
        {
            Debug.LogError("[Chess2DRenderer] boardContainer is not assigned.");
            return false;
        }

        ComputeLayout();
        BuildOverlayGrid();
        SubscribeToEvents();
        _isInitialized = true;
        ApplyPerspective();
        return true;
    }

    private void ComputeLayout()
    {
        float boardSize = boardContainer.rect.width;
        _boardOffset = boardSize * boardBorderFraction;
        float playArea = boardSize - _boardOffset * 2f;
        _cellSize = playArea / 8f;
    }

    private void BuildOverlayGrid()
    {
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                _hitAreas[row, col] = MakeImage($"Hit_{row}{col}", boardContainer);
                PlaceCell(_hitAreas[row, col].rectTransform, row, col);
                _hitAreas[row, col].color = Color.clear;
                _hitAreas[row, col].raycastTarget = true;

                _highlights[row, col] = MakeImage($"Hi_{row}{col}", boardContainer);
                PlaceCell(_highlights[row, col].rectTransform, row, col);
                _highlights[row, col].color = Color.clear;
                _highlights[row, col].raycastTarget = false;

                _pieces[row, col] = MakeImage($"Pc_{row}{col}", boardContainer);
                PlacePieceCell(_pieces[row, col].rectTransform, row, col);
                _pieces[row, col].color = Color.clear;
                _pieces[row, col].raycastTarget = false;
            }
        }
    }

    private void SubscribeToEvents()
    {
        if (_eventsSubscribed)
        {
            return;
        }

        GameEvents.OnMoveMade += HandleMoveMade;
        GameEvents.OnBoardReset += HandleBoardReset;
        _eventsSubscribed = true;
    }

    private void UnsubscribeFromEvents()
    {
        GameEvents.OnMoveMade -= HandleMoveMade;
        GameEvents.OnBoardReset -= HandleBoardReset;
        _eventsSubscribed = false;
    }

    private void HandleMoveMade(MoveRecord _)
    {
        RedrawPieces();
    }

    private void HandleBoardReset()
    {
        ClearAllHighlights();
        RedrawPieces();
    }

    private void PlaceCell(RectTransform rectTransform, int row, int col)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(_cellSize, _cellSize);
        rectTransform.anchoredPosition = new Vector2(
            _boardOffset + col * _cellSize,
            _boardOffset + row * _cellSize);
    }

    private void PlacePieceCell(RectTransform rectTransform, int row, int col)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(_cellSize, _cellSize);
        rectTransform.anchoredPosition = new Vector2(
            _boardOffset + col * _cellSize + _cellSize * 0.5f,
            _boardOffset + row * _cellSize + _cellSize * 0.5f);
    }

    private void ApplyDefaultPerspectiveFromMode()
    {
        if (GameModeManager.Instance != null && GameModeManager.Instance.IsLanClient)
        {
            _localPlayerIsWhite = false;
        }

        ApplyPerspective();
    }

    private void ApplyPerspective()
    {
        if (boardContainer == null)
        {
            return;
        }

        boardContainer.localRotation = _localPlayerIsWhite
            ? Quaternion.identity
            : Quaternion.Euler(0f, 0f, 180f);

        if (boardImage != null && boardImage.rectTransform != boardContainer)
        {
            boardImage.rectTransform.localRotation = _localPlayerIsWhite
                ? Quaternion.identity
                : Quaternion.Euler(0f, 0f, 180f);
        }

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (_pieces[row, col] == null)
                {
                    continue;
                }

                _pieces[row, col].rectTransform.localRotation = _localPlayerIsWhite
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, 0f, 180f);
            }
        }
    }

    private static Image MakeImage(string name, RectTransform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<Image>();
    }

    private Sprite GetSprite(Piece piece)
    {
        int index = piece switch
        {
            Piece.WhitePawn => 0,
            Piece.WhiteKnight => 1,
            Piece.WhiteBishop => 2,
            Piece.WhiteRook => 3,
            Piece.WhiteQueen => 4,
            Piece.WhiteKing => 5,
            Piece.BlackPawn => 6,
            Piece.BlackKnight => 7,
            Piece.BlackBishop => 8,
            Piece.BlackRook => 9,
            Piece.BlackQueen => 10,
            Piece.BlackKing => 11,
            _ => -1
        };

        return index >= 0 && index < pieceSprites.Length ? pieceSprites[index] : null;
    }
}
