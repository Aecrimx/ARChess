using UnityEngine;

public abstract class ChessBoardRendererBase : MonoBehaviour
{
    public abstract void Activate();
    public abstract void Deactivate();
    public abstract void SetPerspective(bool isWhite);
    public abstract void RedrawPieces();
    public abstract void ClearAllHighlights();
    public abstract void SetHighlight(Vector2Int square, Color color);
}
