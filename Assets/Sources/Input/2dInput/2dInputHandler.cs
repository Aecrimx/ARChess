using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Chess2DInputHandler : ChessBoardInputBase
{
    [Header("Dependencies")]
    public Chess2DRenderer renderer2D;
    public PawnPromotionPicker promotionPicker;

    [Header("Highlight Colors")]
    public Color selectedColor = new Color(0.20f, 0.85f, 0.20f, 0.60f);
    public Color legalMoveColor = new Color(0.20f, 0.60f, 1.00f, 0.50f);
    public Color lastMoveColor = new Color(1.00f, 0.85f, 0.00f, 0.40f);
    public Color checkColor = new Color(1.00f, 0.10f, 0.10f, 0.55f);

    public bool LocalPlayerIsWhite { get; private set; } = true;

    private bool _isActive = true;
    private bool _callbacksRegistered;
    private ChessNetworkProxy _localProxy;
    private ChessMoveInteractionController _interactionController;

    private void Start()
    {
        ApplyDefaultLocalColorFromMode();
        EnsureInitialized();
        _interactionController?.Activate();
    }

    private void OnDestroy()
    {
        _interactionController?.Dispose();
    }

    public override void Activate()
    {
        EnsureInitialized();
        _isActive = true;
        renderer2D?.SetPerspective(LocalPlayerIsWhite);
        _interactionController?.Activate();
    }

    public override void Deactivate()
    {
        _isActive = false;
        _interactionController?.Deactivate();
    }

    public override void SetLocalPlayerIsWhite(bool isWhite)
    {
        LocalPlayerIsWhite = isWhite;
        renderer2D?.SetPerspective(isWhite);
        _interactionController?.SetContext(LocalPlayerIsWhite, _localProxy);
    }

    public override void SetLocalProxy(ChessNetworkProxy proxy)
    {
        _localProxy = proxy;
        _interactionController?.SetContext(LocalPlayerIsWhite, _localProxy);
    }

    public void OnSquarePointerDown(int row, int col)
    {
        if (!_isActive)
        {
            return;
        }

        _interactionController?.OnSquarePointerDown(new Vector2Int(row, col));
    }

    public void OnSquarePointerEnter(int row, int col)
    {
        if (!_isActive)
        {
            return;
        }

        _interactionController?.OnSquarePointerEnter(new Vector2Int(row, col));
    }

    public void OnSquarePointerUp(int row, int col)
    {
        if (!_isActive)
        {
            return;
        }

        _interactionController?.OnSquarePointerUp(new Vector2Int(row, col));
    }

    private void EnsureInitialized()
    {
        if (_callbacksRegistered)
        {
            return;
        }

        renderer2D ??= FindAnyObjectByType<Chess2DRenderer>();
        promotionPicker ??= FindAnyObjectByType<PawnPromotionPicker>();

        RegisterButtonCallbacks();
        if (renderer2D == null)
        {
            return;
        }

        _interactionController = new ChessMoveInteractionController(
            renderer2D,
            promotionPicker,
            new ChessMoveInteractionController.HighlightPalette(
                selectedColor,
                legalMoveColor,
                lastMoveColor,
                checkColor));
        _interactionController.SetContext(LocalPlayerIsWhite, _localProxy);
    }

    private void ApplyDefaultLocalColorFromMode()
    {
        if (GameModeManager.Instance != null && GameModeManager.Instance.IsLanClient)
        {
            LocalPlayerIsWhite = false;
        }
    }

    private void RegisterButtonCallbacks()
    {
        if (_callbacksRegistered)
        {
            return;
        }

        if (renderer2D == null)
        {
            Debug.LogError("[Chess2DInputHandler] renderer2D is not assigned.");
            return;
        }

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                Image hitArea = renderer2D.GetHitArea(row, col);
                if (hitArea == null)
                {
                    continue;
                }

                Button button = hitArea.GetComponent<Button>();
                if (button != null)
                {
                    Destroy(button);
                }

                SquareInputTrigger trigger = hitArea.GetComponent<SquareInputTrigger>();
                if (trigger == null)
                {
                    trigger = hitArea.gameObject.AddComponent<SquareInputTrigger>();
                }

                trigger.row = row;
                trigger.col = col;
                trigger.handler = this;
            }
        }

        _callbacksRegistered = true;
    }
}

public class SquareInputTrigger : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler
{
    [HideInInspector] public int row;
    [HideInInspector] public int col;
    [HideInInspector] public Chess2DInputHandler handler;

    public void OnPointerDown(PointerEventData eventData) => handler.OnSquarePointerDown(row, col);
    public void OnPointerEnter(PointerEventData eventData) => handler.OnSquarePointerEnter(row, col);
    public void OnPointerUp(PointerEventData eventData) => handler.OnSquarePointerUp(row, col);
}
