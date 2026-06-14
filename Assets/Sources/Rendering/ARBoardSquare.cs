using UnityEngine;

public class ARBoardSquare : MonoBehaviour
{
    public int Row;
    public int Col;

    public Vector2Int Square => new Vector2Int(Row, Col);
}
