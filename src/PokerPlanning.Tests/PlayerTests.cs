using PokerPlanning.Models;
using PokerPlanning.Services;

namespace PokerPlanning.Tests;

public class PlayerTests
{
    private readonly RoomService _svc = new();
    private const string Cards = "Task 1\nTask 2\nTask 3";

    private Room CreateTestRoom(string ownerName = "Owner")
    {
        return _svc.CreateRoom(ownerName, ScaleType.Fibonacci, Cards, "owner-conn");
    }

    [Fact]
    public void JoinRoom_AddsPlayerWithCorrectProperties()
    {
        var room = CreateTestRoom();
        var player = _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        Assert.Equal("Alice", player.Name);
        Assert.False(player.IsOwner);
        Assert.False(player.IsSpectator);
        Assert.True(player.IsConnected);
        Assert.Equal(2, room.Players.Count);
    }

    [Fact]
    public void JoinRoom_ReconnectsDisconnectedPlayerBySameName()
    {
        var room = CreateTestRoom();
        var alice = _svc.JoinRoom(room.Code, "Alice", "alice-conn1");
        var alicePlayerId = alice.PlayerId;

        // Disconnect Alice
        _svc.DisconnectPlayer("alice-conn1");
        Assert.False(alice.IsConnected);

        // Join with same name, different connection
        var reconnected = _svc.JoinRoom(room.Code, "Alice", "alice-conn2");

        Assert.Equal(alicePlayerId, reconnected.PlayerId);
        Assert.Equal("alice-conn2", reconnected.ConnectionId);
        Assert.True(reconnected.IsConnected);
    }

    [Fact]
    public void JoinRoom_RejectWhenFull()
    {
        var room = CreateTestRoom();

        // Fill up to 50
        for (int i = 0; i < 49; i++)
            _svc.JoinRoom(room.Code, $"Player{i}", $"conn-{i}");

        Assert.Equal(50, room.Players.Count);
        Assert.Throws<InvalidOperationException>(() =>
            _svc.JoinRoom(room.Code, "OneMore", "conn-overflow"));
    }

    [Fact]
    public void JoinRoom_DuplicateConnectionId_ReturnsExisting()
    {
        var room = CreateTestRoom();
        var first = _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        var second = _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        Assert.Same(first, second);
        Assert.Equal(2, room.Players.Count); // not 3
    }

    [Fact]
    public void JoinRoom_RoomNotFound_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _svc.JoinRoom("ZZZZZZ", "Alice", "alice-conn"));
    }

    [Fact]
    public void DisconnectPlayer_SetsDisconnectedAt()
    {
        var room = CreateTestRoom();
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        _svc.DisconnectPlayer("alice-conn");

        var alice = room.Players["alice-conn"];
        Assert.NotNull(alice.DisconnectedAt);
        Assert.False(alice.IsConnected);
    }

    [Fact]
    public void DisconnectPlayer_TransfersOwnership_WhenOwnerLeaves()
    {
        var room = CreateTestRoom();
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        _svc.DisconnectPlayer("owner-conn");

        var owner = room.Players["owner-conn"];
        Assert.False(owner.IsOwner);

        var alice = room.Players["alice-conn"];
        Assert.True(alice.IsOwner);
        Assert.Equal("alice-conn", room.OwnerConnectionId);
    }

    [Fact]
    public void DisconnectPlayer_DoesNotTransferOwnership_WhenNonOwnerLeaves()
    {
        var room = CreateTestRoom();
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        _svc.DisconnectPlayer("alice-conn");

        var owner = room.Players["owner-conn"];
        Assert.True(owner.IsOwner);
        Assert.Equal("owner-conn", room.OwnerConnectionId);
    }

    [Fact]
    public void CleanupDisconnected_RemovesExpiredPlayersAndVotes()
    {
        var room = CreateTestRoom();
        var alice = _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        // Alice votes then disconnects
        _svc.Vote(room.Code, "alice-conn", "5");
        Assert.True(room.CurrentCard!.Votes.ContainsKey("alice-conn"));

        // Simulate expired disconnect (>5 min ago)
        alice.DisconnectedAt = DateTime.UtcNow.AddMinutes(-10);

        _svc.CleanupDisconnected();

        Assert.False(room.Players.ContainsKey("alice-conn"));
        Assert.False(room.CurrentCard!.Votes.ContainsKey("alice-conn"));
    }

    [Fact]
    public void CleanupDisconnected_RemovesEmptyRooms()
    {
        var room = CreateTestRoom();
        var code = room.Code;

        // Disconnect owner with expired time
        var owner = room.Players["owner-conn"];
        owner.DisconnectedAt = DateTime.UtcNow.AddMinutes(-10);

        _svc.CleanupDisconnected();

        Assert.Null(_svc.GetRoom(code));
    }

    [Fact]
    public void CleanupDisconnected_KeepsRecentlyDisconnected()
    {
        var room = CreateTestRoom();
        var alice = _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        // Disconnect just now (within grace period)
        _svc.DisconnectPlayer("alice-conn");

        _svc.CleanupDisconnected();

        Assert.True(room.Players.ContainsKey("alice-conn")); // still there
    }

    [Fact]
    public void GetActivePlayers_ExcludesDisconnected()
    {
        var room = CreateTestRoom();
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");
        _svc.JoinRoom(room.Code, "Bob", "bob-conn");

        _svc.DisconnectPlayer("alice-conn");

        var active = _svc.GetActivePlayers(room).ToList();
        Assert.Equal(2, active.Count); // Owner + Bob
        Assert.DoesNotContain(active, p => p.Name == "Alice");
    }

    [Fact]
    public void GetRoomByPlayer_FindsCorrectRoom()
    {
        var room = CreateTestRoom();
        _svc.JoinRoom(room.Code, "Alice", "alice-conn");

        var found = _svc.GetRoomByPlayer("alice-conn");
        Assert.NotNull(found);
        Assert.Equal(room.Code, found.Code);
    }

    [Fact]
    public void GetRoomByPlayer_NotFound_ReturnsNull()
    {
        Assert.Null(_svc.GetRoomByPlayer("unknown-conn"));
    }
}
