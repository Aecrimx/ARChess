using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class ChessNotationExporterTests
{
    private GameObject _host;
    private GameStateManager _gsm;

    [SetUp]
    public void SetUp()
    {
        _host = new GameObject("GameStateManagerTests");
        _gsm = _host.AddComponent<GameStateManager>();
        _gsm.InitBoard();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_host);
    }

    [Test]
    public void ToFen_StartingPosition_UsesStandardFen()
    {
        Assert.AreEqual(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            _gsm.ToFen());
    }

    [Test]
    public void ToFen_AfterE2E4_TracksTurnEnPassantAndCounters()
    {
        Assert.IsTrue(_gsm.TryApplyMove(new Vector2Int(1, 4), new Vector2Int(3, 4)));

        Assert.AreEqual(
            "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
            _gsm.ToFen());
    }

    [Test]
    public void ToFen_AfterBlackMove_IncrementsFullMoveNumber()
    {
        Assert.IsTrue(_gsm.TryApplyMove(new Vector2Int(1, 4), new Vector2Int(3, 4)));
        Assert.IsTrue(_gsm.TryApplyMove(new Vector2Int(6, 4), new Vector2Int(4, 4)));

        Assert.AreEqual(
            "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2",
            _gsm.ToFen());
    }

    [Test]
    public void ToUci_NormalizesPromotionNotation()
    {
        var record = new MoveRecord { Notation = "e7e8=Q" };

        Assert.AreEqual("e7e8q", record.ToUci());
    }

    [Test]
    public void BuildReviewRequest_IncludesMoveHistoryAndFinalFen()
    {
        Assert.IsTrue(_gsm.TryApplyMove(new Vector2Int(1, 4), new Vector2Int(3, 4)));

        AiGameReviewRequest request = _gsm.BuildReviewRequest("white");

        Assert.AreEqual("white", request.PlayerColor);
        CollectionAssert.AreEqual(new List<string> { "e2e4" }, request.MovesUci);
        Assert.AreEqual(_gsm.ToFen(), request.FinalFen);
    }
}
