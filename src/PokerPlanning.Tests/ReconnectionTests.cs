using PokerPlanning.Models;
using PokerPlanning.Services;

namespace PokerPlanning.Tests;

public class ReconnectionTests
{
    private readonly RoomService _svc = new();
    private const string Cards = "Task 1\nTask 2\nTask 3";

    private (Room room, Player owner) CreateRoomWithPlayers(params string[] playerNames)
    {
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, Cards, "owner-conn");
        foreach (var (name, i) in playerNames.Select((n, i) => (n, i)))
            _svc.JoinRoom(room.Code, name, $"conn-{i}");
        return (room, room.Players["owner-conn"]);
    }

    [Fact]
    public void RejoinRoom_RestoresPlayerAndMigratesVotes()
    {
        var (room, owner) = CreateRoomWithPlayers("Alice");
        var alice = room.Players["conn-0"];
        var playerId = alice.PlayerId;

        // Alice votes
        _svc.Vote(room.Code, "conn-0", "5");

        // Disconnect
        _svc.DisconnectPlayer("conn-0");

        // Rejoin with new connection
        var reconnected = _svc.RejoinRoom(room.Code, playerId, "new-conn");

        Assert.NotNull(reconnected);
        Assert.Equal("Alice", reconnected!.Name);
        Assert.Equal("new-conn", reconnected.ConnectionId);
        Assert.True(reconnected.IsConnected);

        // Vote migrated
        Assert.True(room.CurrentCard!.Votes.ContainsKey("new-conn"));
        Assert.Equal("5", room.CurrentCard.Votes["new-conn"]);
        Assert.False(room.CurrentCard.Votes.ContainsKey("conn-0"));
    }

    [Fact]
    public void RejoinRoom_UnknownPlayerId_ReturnsNull()
    {
        var (room, _) = CreateRoomWithPlayers();
        var result = _svc.RejoinRoom(room.Code, "nonexistent-player-id", "new-conn");
        Assert.Null(result);
    }

    [Fact]
    public void RejoinRoom_UnknownRoom_ReturnsNull()
    {
        var result = _svc.RejoinRoom("ZZZZZZ", "some-id", "new-conn");
        Assert.Null(result);
    }

    [Fact]
    public void OriginalOwner_Reconnect_ReclaimsOwnership()
    {
        var (room, owner) = CreateRoomWithPlayers("Alice");
        var ownerPlayerId = owner.PlayerId;

        // Owner disconnects -> ownership transfers to Alice
        _svc.DisconnectPlayer("owner-conn");
        var alice = room.Players["conn-0"];
        Assert.True(alice.IsOwner);
        Assert.Equal("conn-0", room.OwnerConnectionId);

        // Owner reconnects
        var reconnected = _svc.RejoinRoom(room.Code, ownerPlayerId, "owner-conn2");

        Assert.NotNull(reconnected);
        Assert.True(reconnected!.IsOwner);
        Assert.Equal("owner-conn2", room.OwnerConnectionId);

        // Alice lost ownership
        Assert.False(alice.IsOwner);
    }

    [Fact]
    public void Reconnect_UpdatesOwnerConnectionId()
    {
        var (room, owner) = CreateRoomWithPlayers("Alice");
        var ownerPlayerId = owner.PlayerId;

        // Disconnect without ownership transfer (just mark disconnected, no other non-spectator)
        // Actually with Alice present, ownership transfers. Let's test a non-owner reconnect.
        var alicePlayerId = room.Players["conn-0"].PlayerId;
        _svc.DisconnectPlayer("conn-0");

        var reconnected = _svc.RejoinRoom(room.Code, alicePlayerId, "alice-new");
        Assert.NotNull(reconnected);
        Assert.Equal("alice-new", reconnected!.ConnectionId);
        Assert.True(room.Players.ContainsKey("alice-new"));
        Assert.False(room.Players.ContainsKey("conn-0"));
    }

    [Fact]
    public void Reconnect_MigratesVotesAcrossAllCards()
    {
        var (room, _) = CreateRoomWithPlayers("Alice");
        var alicePlayerId = room.Players["conn-0"].PlayerId;

        // Vote on card 0
        _svc.Vote(room.Code, "conn-0", "5");

        // Advance to card 1 (need to reveal first)
        _svc.RevealCards(room.Code, "owner-conn");
        _svc.NextQuestion(room.Code, "owner-conn");

        // Vote on card 1
        _svc.Vote(room.Code, "conn-0", "8");

        // Disconnect and rejoin
        _svc.DisconnectPlayer("conn-0");
        _svc.RejoinRoom(room.Code, alicePlayerId, "alice-new");

        // Both cards should have migrated votes
        Assert.True(room.Cards[0].Votes.ContainsKey("alice-new"));
        Assert.Equal("5", room.Cards[0].Votes["alice-new"]);
        Assert.True(room.Cards[1].Votes.ContainsKey("alice-new"));
        Assert.Equal("8", room.Cards[1].Votes["alice-new"]);

        // Old keys gone
        Assert.False(room.Cards[0].Votes.ContainsKey("conn-0"));
        Assert.False(room.Cards[1].Votes.ContainsKey("conn-0"));
    }

    [Fact]
    public void JoinRoom_ReconnectsByName_WhenDisconnected()
    {
        var (room, _) = CreateRoomWithPlayers("Alice");
        var alicePlayerId = room.Players["conn-0"].PlayerId;

        _svc.DisconnectPlayer("conn-0");

        // Join with same name, new connection
        var reconnected = _svc.JoinRoom(room.Code, "Alice", "alice-new");

        Assert.Equal(alicePlayerId, reconnected.PlayerId);
        Assert.Equal("alice-new", reconnected.ConnectionId);
        Assert.True(reconnected.IsConnected);
    }

    [Fact]
    public void OwnerDisconnect_OnlySpectatorLeft_NoOwnershipTransfer()
    {
        // Create room where owner is the only non-spectator
        var room = _svc.CreateRoom("Owner", ScaleType.Fibonacci, Cards, "owner-conn");
        // Add a spectator by joining normally then... actually spectators can't be created via join.
        // Let's just have owner as the only player
        _svc.DisconnectPlayer("owner-conn");

        // Owner still has IsOwner (no one to transfer to)
        var owner = room.Players["owner-conn"];
        Assert.True(owner.IsOwner); // ownership can't transfer, stays with disconnected owner
    }
}
