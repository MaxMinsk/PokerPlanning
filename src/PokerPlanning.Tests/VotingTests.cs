using PokerPlanning.Models;
using PokerPlanning.Services;

namespace PokerPlanning.Tests;

public class VotingTests
{
    private readonly RoomService _svc = new();
    private const string Cards = "Task 1\nTask 2";

    private Room SetupRoom(bool coffeeBreak = false)
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, Cards, "owner-conn", coffeeBreak: coffeeBreak);
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        _svc.JoinRoom(room.Code, "Bob", "bob-conn");
        return room;
    }

    [Fact]
    public void Vote_StoresValueOnCurrentCard()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");

        Assert.Equal("5", room.CurrentCard!.Votes["alice-conn"]);
    }

    [Fact]
    public void Vote_RejectsInvalidScaleValue()
    {
        var room = SetupRoom();
        Assert.Throws<ArgumentException>(() =>
            _svc.Vote(room.Code, "alice-conn", "999"));
    }

    [Fact]
    public void Vote_RejectsSpectator()
    {
        var room = _svc.CreateRoom(null, ScaleType.Fibonacci, Cards, "owner-conn"); // spectator owner
        Assert.Throws<InvalidOperationException>(() =>
            _svc.Vote(room.Code, "owner-conn", "5"));
    }

    [Fact]
    public void Vote_RejectsPlayerNotInRoom()
    {
        var room = SetupRoom();
        Assert.Throws<InvalidOperationException>(() =>
            _svc.Vote(room.Code, "unknown-conn", "5"));
    }

    [Fact]
    public void Vote_AllowsQuestionMark()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "?");
        Assert.Equal("?", room.CurrentCard!.Votes["alice-conn"]);
    }

    [Fact]
    public void Vote_OverwritesPreviousVote()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.Vote(room.Code, "alice-conn", "8");
        Assert.Equal("8", room.CurrentCard!.Votes["alice-conn"]);
    }

    [Fact]
    public void CoffeeVote_AcceptedWhenEnabled()
    {
        var room = SetupRoom(coffeeBreak: true);
        _svc.Vote(room.Code, "alice-conn", "☕");
        Assert.Equal("☕", room.CurrentCard!.Votes["alice-conn"]);
    }

    [Fact]
    public void CoffeeVote_RejectedWhenNotEnabled()
    {
        var room = SetupRoom(coffeeBreak: false);
        Assert.Throws<ArgumentException>(() =>
            _svc.Vote(room.Code, "alice-conn", "☕"));
    }

    [Fact]
    public void RevealCards_ChangesStateAndReturnsNamedVotes()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.Vote(room.Code, "bob-conn", "8");

        var namedVotes = _svc.RevealCards(room.Code, "owner-conn");

        Assert.Equal(RoomState.Revealed, room.State);
        Assert.Equal("5", namedVotes["Alice"]);
        Assert.Equal("8", namedVotes["Bob"]);
    }

    [Fact]
    public void RevealCards_RejectsNonOwner()
    {
        var room = SetupRoom();
        Assert.Throws<InvalidOperationException>(() =>
            _svc.RevealCards(room.Code, "alice-conn"));
    }

    [Fact]
    public void RevealCards_RejectsDoubleReveal()
    {
        var room = SetupRoom();
        _svc.RevealCards(room.Code, "owner-conn");
        Assert.Throws<InvalidOperationException>(() =>
            _svc.RevealCards(room.Code, "owner-conn"));
    }

    [Fact]
    public void Revote_ClearsVotesAndResetsState()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.RevealCards(room.Code, "owner-conn");

        _svc.Revote(room.Code, "owner-conn");

        Assert.Equal(RoomState.Voting, room.State);
        Assert.Empty(room.CurrentCard!.Votes);
        Assert.Null(room.CurrentCard.AcceptedEstimate);
    }

    [Fact]
    public void Revote_RejectsNonOwner()
    {
        var room = SetupRoom();
        _svc.RevealCards(room.Code, "owner-conn");
        Assert.Throws<InvalidOperationException>(() =>
            _svc.Revote(room.Code, "alice-conn"));
    }

    [Fact]
    public void AcceptEstimate_StoresValue()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.RevealCards(room.Code, "owner-conn");
        _svc.AcceptEstimate(room.Code, "owner-conn", "5");

        Assert.Equal("5", room.CurrentCard!.AcceptedEstimate);
    }

    [Fact]
    public void AcceptEstimate_RejectsNonOwner()
    {
        var room = SetupRoom();
        _svc.RevealCards(room.Code, "owner-conn");
        Assert.Throws<InvalidOperationException>(() =>
            _svc.AcceptEstimate(room.Code, "alice-conn", "5"));
    }

    [Fact]
    public void Vote_RoomNotFound_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _svc.Vote("ZZZZZZ", "conn", "5"));
    }
}
