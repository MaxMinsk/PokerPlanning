using PokerPlanning.Models;
using PokerPlanning.Services;

namespace PokerPlanning.Tests;

public class RoomLifecycleTests
{
    private readonly RoomService _svc = new();
    private const string Cards = "Task 1;Desc 1\nTask 2;Desc 2\nTask 3";

    [Fact]
    public void CreateRoom_ReturnsValidRoom()
    {
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, Cards, "conn1");

        Assert.NotNull(room);
        Assert.Equal(6, room.Code.Length);
        Assert.Equal(3, room.Cards.Count);
        Assert.Equal(RoomState.Voting, room.State);
        Assert.Equal(0, room.CurrentCardIndex);
        Assert.Equal(ScaleType.Fibonacci, room.Scale);
        Assert.Single(room.Players);

        var owner = room.Players["conn1"];
        Assert.True(owner.IsOwner);
        Assert.True(owner.WasOriginalOwner);
        Assert.False(owner.IsSpectator);
        Assert.Equal("Max", owner.Name);
    }

    [Fact]
    public void CreateRoom_EmptyCards_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _svc.CreateRoom("Max", ScaleType.Fibonacci, "", "conn1"));
    }

    [Fact]
    public void CreateRoom_BlankOwnerName_CreatesSpectator()
    {
        var room = _svc.CreateRoom("", ScaleType.Fibonacci, Cards, "conn1");

        var owner = room.Players["conn1"];
        Assert.True(owner.IsSpectator);
        Assert.True(owner.IsOwner);
        Assert.Equal("Spectator", owner.Name);
    }

    [Fact]
    public void CreateRoom_NullOwnerName_CreatesSpectator()
    {
        var room = _svc.CreateRoom(null, ScaleType.Fibonacci, Cards, "conn1");

        var owner = room.Players["conn1"];
        Assert.True(owner.IsSpectator);
    }

    [Fact]
    public void CreateRoom_Shuffle_PreservesOriginalIndex()
    {
        // Create with many cards to make shuffle statistically detectable
        var manyCards = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"Card {i}"));
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, manyCards, "conn1", shuffle: true);

        // OriginalIndex should cover 0..19
        var origIndices = room.Cards.Select(c => c.OriginalIndex).OrderBy(i => i).ToList();
        Assert.Equal(Enumerable.Range(0, 20).ToList(), origIndices);

        // Check that subjects still correspond to their OriginalIndex
        foreach (var card in room.Cards)
        {
            Assert.Equal($"Card {card.OriginalIndex + 1}", card.Subject);
        }
    }

    [Fact]
    public void CreateRoom_NoShuffle_OriginalOrder()
    {
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, Cards, "conn1", shuffle: false);

        for (int i = 0; i < room.Cards.Count; i++)
        {
            Assert.Equal(i, room.Cards[i].OriginalIndex);
        }
        Assert.Equal("Task 1", room.Cards[0].Subject);
        Assert.Equal("Task 3", room.Cards[2].Subject);
    }

    [Fact]
    public void CreateRoom_WithSessionTimer_CalculatesSecondsPerCard()
    {
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, Cards, "conn1", sessionMinutes: 30);

        Assert.Equal(30, room.SessionMinutes);
        Assert.Equal(600, room.SecondsPerCard); // 30*60 / 3 cards
        Assert.NotNull(room.CardTimerStartedAt);
    }

    [Fact]
    public void CreateRoom_NoTimer_NullTimerFields()
    {
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, Cards, "conn1");

        Assert.Null(room.SessionMinutes);
        Assert.Null(room.SecondsPerCard);
        Assert.Null(room.CardTimerStartedAt);
    }

    [Fact]
    public void CreateRoom_CoffeeBreak_SetsFlag()
    {
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, Cards, "conn1", coffeeBreak: true);
        Assert.True(room.CoffeeBreakEnabled);
    }

    [Fact]
    public void GetRoom_CaseInsensitive()
    {
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, Cards, "conn1");
        var code = room.Code;

        Assert.NotNull(_svc.GetRoom(code.ToLower()));
        Assert.NotNull(_svc.GetRoom(code.ToUpper()));
    }

    [Fact]
    public void GetRoom_NotFound_ReturnsNull()
    {
        Assert.Null(_svc.GetRoom("ZZZZZZ"));
    }

    [Fact]
    public void CreateRoom_ParsesSubjectAndDescription()
    {
        var room = _svc.CreateRoom("Max", ScaleType.Fibonacci, Cards, "conn1");

        Assert.Equal("Task 1", room.Cards[0].Subject);
        Assert.Equal("Desc 1", room.Cards[0].Description);
        Assert.Equal("Task 3", room.Cards[2].Subject);
        Assert.Null(room.Cards[2].Description); // no semicolon
    }

    [Theory]
    [InlineData(ScaleType.Fibonacci)]
    [InlineData(ScaleType.TShirt)]
    [InlineData(ScaleType.PowersOf2)]
    [InlineData(ScaleType.Sequential)]
    [InlineData(ScaleType.Risk)]
    public void CreateRoom_AllScaleTypes(ScaleType scale)
    {
        var room = _svc.CreateRoom("Max", scale, Cards, "conn1");
        Assert.Equal(scale, room.Scale);
    }
}
