using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

public class ChessARRenderer : ChessBoardRendererBase
{
    private const string ResourceRoot = "ARModels";
    private const float HighlightHeightOffset = 0.005f;
    private const float PieceHeightOffset = 0.0f;
    private const float DefaultBoardSizeMeters = 0.25f;
    private const float MinBoardSizeMeters = 0.1f;
    private const float MaxBoardSizeMeters = 1.0f;

    private readonly Dictionary<Piece, string> _pieceResourceNames = new Dictionary<Piece, string>
    {
        { Piece.WhitePawn, "pion_alb" },
        { Piece.WhiteKnight, "cal_alb" },
        { Piece.WhiteBishop, "nebun_alb" },
        { Piece.WhiteRook, "tura_alb" },
        { Piece.WhiteQueen, "regina_alb" },
        { Piece.WhiteKing, "rege_alb" },
        { Piece.BlackPawn, "pion_negru" },
        { Piece.BlackKnight, "cal_negru" },
        { Piece.BlackBishop, "nebun_negru" },
        { Piece.BlackRook, "tura_negru" },
        { Piece.BlackQueen, "regina_negru" },
        { Piece.BlackKing, "rege_negru" }
    };

    private readonly Transform[,] _squareAnchors = new Transform[8, 8];
    private readonly Renderer[,] _highlightRenderers = new Renderer[8, 8];
    private readonly GameObject[,] _pieceInstances = new GameObject[8, 8];

    private GameObject _boardRigRoot;
    private Transform _boardVisualRoot;
    private Transform _squaresRoot;
    private Transform _piecesRoot;
    private Transform _highlightsRoot;
    private bool _localPlayerIsWhite = true;
    private bool _isActive;
    private bool _isPlaced;
    private bool _isInitialized;
    private bool _eventsSubscribed;
    private float _currentBoardSizeMeters = DefaultBoardSizeMeters;
    private Bounds _boardLocalBounds;
    private ARAnchor _placementAnchor;

    public bool IsPlaced => _isPlaced;
    public Vector2 BoardFootprint
    {
        get
        {
            EnsureRigBuilt();
            float boardScale = CurrentBoardScale;
            return new Vector2(
                Mathf.Max(_boardLocalBounds.size.x * boardScale, MinBoardSizeMeters),
                Mathf.Max(_boardLocalBounds.size.z * boardScale, MinBoardSizeMeters));
        }
    }

    private float CurrentBoardScale
    {
        get
        {
            float importedFootprint = Mathf.Max(_boardLocalBounds.size.x, _boardLocalBounds.size.z);
            if (importedFootprint <= Mathf.Epsilon)
            {
                return _currentBoardSizeMeters;
            }

            return _currentBoardSizeMeters / importedFootprint;
        }
    }

    private void Awake()
    {
        SubscribeToEvents();
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
        _isActive = true;
        if (_boardRigRoot != null)
        {
            _boardRigRoot.SetActive(_isPlaced);
        }

        RedrawPieces();
    }

    public override void Deactivate()
    {
        _isActive = false;
        if (_boardRigRoot != null)
        {
            _boardRigRoot.SetActive(false);
        }
    }

    public override void SetPerspective(bool isWhite)
    {
        if (_localPlayerIsWhite == isWhite)
        {
            return;
        }

        _localPlayerIsWhite = isWhite;
        if (_isPlaced && _boardRigRoot != null)
        {
            _boardRigRoot.transform.Rotate(Vector3.up, 180f, Space.World);
        }
    }

    public override void RedrawPieces()
    {
        if (!_isPlaced || !EnsureRigBuilt() || GameStateManager.Instance == null)
        {
            return;
        }

        Piece[,] board = GameStateManager.Instance.Board;
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (_pieceInstances[row, col] != null)
                {
                    Destroy(_pieceInstances[row, col]);
                    _pieceInstances[row, col] = null;
                }

                Piece piece = board[row, col];
                if (piece == Piece.None)
                {
                    continue;
                }

                GameObject pieceInstance = CreatePieceVisual(piece);
                pieceInstance.name = $"Piece_{row}_{col}_{piece}";
                pieceInstance.transform.SetParent(_piecesRoot, false);
                pieceInstance.transform.position = _squareAnchors[row, col].position + Vector3.up * PieceHeightOffset;
                pieceInstance.transform.rotation = _boardRigRoot.transform.rotation;
                FitPieceToSquare(pieceInstance, _squareAnchors[row, col]);
                _pieceInstances[row, col] = pieceInstance;
            }
        }
    }

    public override void ClearAllHighlights()
    {
        if (!_isInitialized)
        {
            return;
        }

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (_highlightRenderers[row, col] != null)
                {
                    ApplyColorToRenderer(_highlightRenderers[row, col], Color.clear);
                }
            }
        }
    }

    public override void SetHighlight(Vector2Int square, Color color)
    {
        if (!_isInitialized || square.x < 0 || square.x > 7 || square.y < 0 || square.y > 7)
        {
            return;
        }

        Renderer highlightRenderer = _highlightRenderers[square.x, square.y];
        if (highlightRenderer != null)
        {
            ApplyColorToRenderer(highlightRenderer, color);
        }
    }

    public bool PlaceBoard(Pose pose, Transform cameraTransform, ARAnchor anchor = null)
    {
        if (!EnsureRigBuilt())
        {
            return false;
        }

        ClearPlacementAnchor();

        Quaternion placementRotation = CalculatePlacementRotation(pose.position, cameraTransform);
        if (anchor != null)
        {
            _placementAnchor = anchor;
            _boardRigRoot.transform.SetParent(anchor.transform, false);
            _boardRigRoot.transform.localPosition = Vector3.zero;
            _boardRigRoot.transform.localRotation = Quaternion.Inverse(anchor.transform.rotation) * placementRotation;
        }
        else
        {
            _boardRigRoot.transform.SetParent(transform, true);
            _boardRigRoot.transform.SetPositionAndRotation(pose.position, placementRotation);
        }

        ApplyBoardScale();
        _boardRigRoot.SetActive(_isActive);
        _isPlaced = true;
        ClearAllHighlights();
        RedrawPieces();
        return true;
    }

    public void ClearPlacement()
    {
        _isPlaced = false;
        if (_boardRigRoot != null)
        {
            _boardRigRoot.transform.SetParent(transform, true);
            _boardRigRoot.SetActive(false);
        }

        ClearPlacementAnchor();
    }

    public void RotateBoard(float degrees)
    {
        if (_boardRigRoot == null || !_isPlaced)
        {
            return;
        }

        _boardRigRoot.transform.Rotate(Vector3.up, degrees, Space.World);
    }

    public void AdjustScale(float delta)
    {
        _currentBoardSizeMeters = Mathf.Clamp(
            _currentBoardSizeMeters + delta * DefaultBoardSizeMeters,
            MinBoardSizeMeters,
            MaxBoardSizeMeters);

        ApplyBoardScale();
    }

    private bool EnsureRigBuilt()
    {
        if (_isInitialized)
        {
            return true;
        }

        _boardRigRoot = new GameObject("ARChessBoardRig");
        _boardRigRoot.transform.SetParent(transform, false);
        _boardRigRoot.SetActive(false);

        _boardVisualRoot = new GameObject("BoardModel").transform;
        _boardVisualRoot.SetParent(_boardRigRoot.transform, false);

        _squaresRoot = new GameObject("SquaresRoot").transform;
        _squaresRoot.SetParent(_boardRigRoot.transform, false);

        _piecesRoot = new GameObject("PiecesRoot").transform;
        _piecesRoot.SetParent(_boardRigRoot.transform, false);

        _highlightsRoot = new GameObject("HighlightsRoot").transform;
        _highlightsRoot.SetParent(_boardRigRoot.transform, false);

        BuildBoardVisual();
        BuildSquaresAndHighlights();

        _isInitialized = true;
        return true;
    }

    private void BuildBoardVisual()
    {
        GameObject boardPrefab = Resources.Load<GameObject>($"{ResourceRoot}/chessboard");
        if (boardPrefab != null)
        {
            GameObject boardModel = Instantiate(boardPrefab, _boardVisualRoot, false);
            boardModel.name = "BoardVisual";
        }
        else
        {
            GameObject fallbackBoard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallbackBoard.name = "BoardVisual";
            fallbackBoard.transform.SetParent(_boardVisualRoot, false);
            fallbackBoard.transform.localScale = new Vector3(1f, 0.05f, 1f);
            ApplyColorToRenderer(fallbackBoard.GetComponent<Renderer>(), new Color(0.25f, 0.22f, 0.18f, 1f));
        }

        _boardLocalBounds = CalculateLocalBounds(_boardVisualRoot);
    }

    private void ApplyBoardScale()
    {
        if (!EnsureRigBuilt() || _boardRigRoot == null)
        {
            return;
        }

        _boardRigRoot.transform.localScale = Vector3.one * CurrentBoardScale;
    }

    private void ClearPlacementAnchor()
    {
        if (_placementAnchor == null)
        {
            return;
        }

        if (_boardRigRoot != null && _boardRigRoot.transform.parent == _placementAnchor.transform)
        {
            _boardRigRoot.transform.SetParent(transform, true);
        }

        Destroy(_placementAnchor.gameObject);
        _placementAnchor = null;
    }

    private void BuildSquaresAndHighlights()
    {
        float width = _boardLocalBounds.size.x;
        float depth = _boardLocalBounds.size.z;
        float cellWidth = width / 8f;
        float cellDepth = depth / 8f;
        float surfaceY = _boardLocalBounds.max.y;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                float centerX = _boardLocalBounds.min.x + cellWidth * (col + 0.5f);
                float centerZ = _boardLocalBounds.min.z + cellDepth * (row + 0.5f);

                var squareObject = new GameObject($"Square_{row}_{col}");
                squareObject.transform.SetParent(_squaresRoot, false);
                squareObject.transform.localPosition = new Vector3(centerX, surfaceY + HighlightHeightOffset, centerZ);
                squareObject.transform.localRotation = Quaternion.identity;

                var boxCollider = squareObject.AddComponent<BoxCollider>();
                boxCollider.size = new Vector3(cellWidth, 0.03f, cellDepth);
                boxCollider.center = Vector3.zero;

                var squareMarker = squareObject.AddComponent<ARBoardSquare>();
                squareMarker.Row = row;
                squareMarker.Col = col;

                _squareAnchors[row, col] = squareObject.transform;

                GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
                highlight.name = $"Highlight_{row}_{col}";
                highlight.transform.SetParent(_highlightsRoot, false);
                highlight.transform.localPosition = new Vector3(centerX, surfaceY + HighlightHeightOffset, centerZ);
                highlight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                highlight.transform.localScale = new Vector3(cellWidth * 0.95f, cellDepth * 0.95f, 1f);
                Destroy(highlight.GetComponent<Collider>());

                Renderer highlightRenderer = highlight.GetComponent<Renderer>();
                ApplyColorToRenderer(highlightRenderer, Color.clear);
                _highlightRenderers[row, col] = highlightRenderer;
            }
        }
    }

    private GameObject CreatePieceVisual(Piece piece)
    {
        if (_pieceResourceNames.TryGetValue(piece, out string resourceName))
        {
            GameObject prefab = Resources.Load<GameObject>($"{ResourceRoot}/{resourceName}");
            if (prefab != null)
            {
                return Instantiate(prefab);
            }
        }

        GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fallback.name = $"Fallback_{piece}";
        ApplyColorToRenderer(fallback.GetComponent<Renderer>(), IsWhitePiece(piece) ? Color.white : Color.black);
        return fallback;
    }

    private void FitPieceToSquare(GameObject pieceInstance, Transform squareAnchor)
    {
        Bounds bounds = CalculateWorldBounds(pieceInstance.transform);
        float currentFootprint = Mathf.Max(bounds.size.x, bounds.size.z);
        if (currentFootprint <= Mathf.Epsilon)
        {
            return;
        }

        float targetFootprint = Mathf.Min(
            _squareAnchors[0, 0].GetComponent<BoxCollider>().size.x,
            _squareAnchors[0, 0].GetComponent<BoxCollider>().size.z) * 0.7f * CurrentBoardScale;

        float scaleMultiplier = targetFootprint / currentFootprint;
        pieceInstance.transform.localScale *= scaleMultiplier;

        Bounds scaledBounds = CalculateWorldBounds(pieceInstance.transform);
        Vector3 position = squareAnchor.position + Vector3.up * (scaledBounds.extents.y + PieceHeightOffset);
        pieceInstance.transform.position = position;
    }

    private Quaternion CalculatePlacementRotation(Vector3 placementPosition, Transform cameraTransform)
    {
        Vector3 toCamera = cameraTransform != null
            ? Vector3.ProjectOnPlane(cameraTransform.position - placementPosition, Vector3.up)
            : Vector3.back;

        if (toCamera.sqrMagnitude < 0.001f)
        {
            toCamera = Vector3.back;
        }

        Quaternion rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        if (!_localPlayerIsWhite)
        {
            rotation *= Quaternion.Euler(0f, 180f, 0f);
        }

        return rotation;
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

    private static Bounds CalculateLocalBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one);
        }

        bool initialized = false;
        Bounds combined = default;
        foreach (Renderer renderer in renderers)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3[] corners =
            {
                new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.min.z),
                new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.max.z),
                new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.min.z),
                new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.max.z),
                new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.min.z),
                new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.max.z),
                new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.min.z),
                new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.max.z)
            };

            foreach (Vector3 corner in corners)
            {
                Vector3 localCorner = root.InverseTransformPoint(corner);
                if (!initialized)
                {
                    combined = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    combined.Encapsulate(localCorner);
                }
            }
        }

        return combined;
    }

    private static Bounds CalculateWorldBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(root.position, Vector3.one);
        }

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        return combined;
    }

    private static void ApplyColorToRenderer(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        if (renderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return;
            }

            renderer.sharedMaterial = new Material(shader);
        }

        Material material = renderer.material;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.color = color;
        }

        if (color.a < 0.999f)
        {
            ConfigureTransparentMaterial(material);
        }
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static bool IsWhitePiece(Piece piece) => (int)piece > 0;
}
