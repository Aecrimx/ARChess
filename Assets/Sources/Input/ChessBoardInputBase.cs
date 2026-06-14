using UnityEngine;

public abstract class ChessBoardInputBase : MonoBehaviour
{
    public abstract void Activate();
    public abstract void Deactivate();
    public abstract void SetLocalPlayerIsWhite(bool isWhite);
    public abstract void SetLocalProxy(ChessNetworkProxy proxy);
}
