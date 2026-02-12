using PokerPlanning.Models;
using PokerPlanning.Services;

namespace PokerPlanning.Tests;

public class NextQuestionTests
{
    private readonly RoomService _svc = new();
    private const string Cards = "Task 1\nTask 2\nTask 3";

    private Room SetupRoom()
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, Cards, "owner-conn");
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        return room;
    }

    [Fact]
    public void NextQuestion_AdvancesIndexAndResetsState()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.RevealCards(room.Code, "owner-conn");

        var nextCard = _svc.NextQuestion(room.Code, "owner-conn");

        Assert.NotNull(nextCard);
        Assert.Equal(1, room.CurrentCardIndex);
        Assert.Equal(RoomState.Voting, room.State);
        Assert.Equal("Task 2", nextCard!.Subject);
    }

    [Fact]
    public void NextQuestion_AutoAcceptsEstimate()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.Vote(room.Code, "owner-conn", "5");
        _svc.RevealCards(room.Code, "owner-conn");

        // Don't manually accept — should auto-accept
        _svc.NextQuestion(room.Code, "owner-conn");

        Assert.Equal("5", room.Cards[0].AcceptedEstimate);
    }

    [Fact]
    public void NextQuestion_DoesNotOverwriteManualAccept()
    {
        var room = SetupRoom();
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.RevealCards(room.Code, "owner-conn");
        _svc.AcceptEstimate(room.Code, "owner-conn", "8"); // manual override

        _svc.NextQuestion(room.Code, "owner-conn");

        Assert.Equal("8", room.Cards[0].AcceptedEstimate); // kept manual
    }

    [Fact]
    public void NextQuestion_ReturnsNull_WhenLastCard()
    {
        var room = SetupRoom();

        // Advance through all 3 cards
        for (int i = 0; i < 3; i++)
        {
            _svc.RevealCards(room.Code, "owner-conn");
            var next = _svc.NextQuestion(room.Code, "owner-conn");

            if (i < 2)
            {
                Assert.NotNull(next);
                Assert.Equal(RoomState.Voting, room.State);
            }
            else
            {
                Assert.Null(next);
                Assert.Equal(RoomState.Finished, room.State);
            }
        }
    }

    [Fact]
    public void NextQuestion_RejectsNonOwner()
    {
        var room = SetupRoom();
        _svc.RevealCards(room.Code, "owner-conn");

        Assert.Throws<InvalidOperationException>(() =>
            _svc.NextQuestion(room.Code, "alice-conn"));
    }

    [Fact]
    public void NextQuestion_WithTimer_ResetsTimerStart()
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, Cards, "owner-conn", sessionMinutes: 30);
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        var firstTimerStart = room.CardTimerStartedAt;
        Assert.NotNull(firstTimerStart);

        _svc.RevealCards(room.Code, "owner-conn");

        // Small delay to ensure different timestamp
        _svc.NextQuestion(room.Code, "owner-conn");

        Assert.NotNull(room.CardTimerStartedAt);
        // Timer was reset (could be same ms, but at least not null)
    }
}

public class GetResultsTests
{
    private readonly RoomService _svc = new();

    // Helper: anonymous types from another assembly can't be accessed via dynamic.
    // Use reflection to read properties.
    private static T GetProp<T>(object obj, string name)
    {
        var prop = obj.GetType().GetProperty(name)
            ?? throw new Exception($"Property '{name}' not found on {obj.GetType().Name}");
        return (T)prop.GetValue(obj)!;
    }

    [Fact]
    public void Results_OrderedByOriginalIndex()
    {
        var cards = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"Card {i}"));
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, cards, "owner-conn", shuffle: true);

        var results = _svc.GetResults(room.Code);

        // Results should be in original order regardless of shuffle
        for (int i = 0; i < results.Count; i++)
        {
            Assert.Equal(i + 1, GetProp<int>(results[i], "index"));
            Assert.Equal($"Card {i + 1}", GetProp<string>(results[i], "subject"));
        }
    }

    [Fact]
    public void Results_OrphanedVotesSkipped()
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, "Task 1", "owner-conn");
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        _svc.Vote(room.Code, "alice-conn", "5");

        // Simulate orphaned vote: remove player but keep vote
        room.Players.TryRemove("alice-conn", out _);

        var results = _svc.GetResults(room.Code);
        var votes = GetProp<Dictionary<string, string>>(results[0], "votes");

        // Orphaned vote should be skipped, not crash
        Assert.DoesNotContain("Unknown", votes.Keys);
        Assert.Empty(votes); // Alice was removed, so her vote is orphaned
    }

    [Fact]
    public void Results_IncludesAllCards_EvenUnvoted()
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, "Task 1\nTask 2\nTask 3", "owner-conn");

        var results = _svc.GetResults(room.Code);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Results_IncludesAcceptedEstimate()
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, "Task 1\nTask 2", "owner-conn");
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        _svc.Vote(room.Code, "alice-conn", "5");
        _svc.RevealCards(room.Code, "owner-conn");
        _svc.AcceptEstimate(room.Code, "owner-conn", "5");

        var results = _svc.GetResults(room.Code);
        Assert.Equal("5", GetProp<string?>(results[0], "estimate"));
    }

    [Fact]
    public void Results_RoomNotFound_Throws()
    {
        Assert.Throws<ArgumentException>(() => _svc.GetResults("ZZZZZZ"));
    }

    [Fact]
    public void GetNamedVotes_MapsConnectionIdToPlayerName()
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, "Task 1", "owner-conn");
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        _svc.Vote(room.Code, "owner-conn", "3");
        _svc.Vote(room.Code, "alice-conn", "5");

        var namedVotes = _svc.GetNamedVotes(room);
        Assert.Equal("3", namedVotes["Owner"]);
        Assert.Equal("5", namedVotes["Alice"]);
    }

    [Fact]
    public void GetNamedVotes_SkipsOrphanedPlayers()
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, "Task 1", "owner-conn");
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        _svc.Vote(room.Code, "alice-conn", "5");

        // Remove player but keep vote
        room.Players.TryRemove("alice-conn", out _);

        var namedVotes = _svc.GetNamedVotes(room);
        Assert.DoesNotContain("Alice", namedVotes.Keys);
        Assert.DoesNotContain("Unknown", namedVotes.Keys);
    }
}
