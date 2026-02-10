using Microsoft.AspNetCore.SignalR;
using PokerPlanning.Models;
using PokerPlanning.Services;

namespace PokerPlanning.Hubs;

public class PokerHub : Hub
{
    private readonly RoomService _roomService;

    public PokerHub(RoomService roomService)
    {
        _roomService = roomService;
    }

    public async Task CreateRoom(string? ownerName, int scaleType, string cardsText)
    {
        try
        {
            var scale = (ScaleType)scaleType;
            var room = _roomService.CreateRoom(ownerName, scale, cardsText, Context.ConnectionId);

            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
            await Clients.Caller.SendAsync("RoomCreated", new
            {
                roomCode = room.Code,
                scale = ScaleDefinitions.GetScale(room.Scale),
                scaleName = ScaleDefinitions.GetDisplayName(room.Scale),
                currentCard = new { room.CurrentCard!.Subject, room.CurrentCard.Description },
                currentCardIndex = room.CurrentCardIndex,
                totalCards = room.Cards.Count,
                isOwner = true,
                isSpectator = room.Players[Context.ConnectionId].IsSpectator,
                players = room.Players.Values.Select(p => new
                {
                    name = p.Name,
                    isOwner = p.IsOwner,
                    isSpectator = p.IsSpectator,
                    hasVoted = false
                })
            });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task JoinRoom(string roomCode, string playerName)
    {
        try
        {
            var room = _roomService.GetRoom(roomCode);
            if (room == null)
            {
                await Clients.Caller.SendAsync("Error", "Room not found.");
                return;
            }

            var player = _roomService.JoinRoom(roomCode, playerName, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            // Notify others
            await Clients.OthersInGroup(room.Code).SendAsync("PlayerJoined", new
            {
                name = player.Name,
                isOwner = player.IsOwner,
                isSpectator = player.IsSpectator,
                hasVoted = false,
                playerCount = room.Players.Count
            });

            // Send full state to joining player
            var currentCard = room.CurrentCard;
            var namedVotes = room.State == RoomState.Revealed
                ? _roomService.GetNamedVotes(room)
                : new Dictionary<string, string>();

            await Clients.Caller.SendAsync("RoomState", new
            {
                roomCode = room.Code,
                scale = ScaleDefinitions.GetScale(room.Scale),
                scaleName = ScaleDefinitions.GetDisplayName(room.Scale),
                currentCard = currentCard != null ? new { currentCard.Subject, currentCard.Description } : null,
                currentCardIndex = room.CurrentCardIndex,
                totalCards = room.Cards.Count,
                state = room.State.ToString(),
                isOwner = false,
                isSpectator = false,
                players = room.Players.Values.Select(p => new
                {
                    name = p.Name,
                    isOwner = p.IsOwner,
                    isSpectator = p.IsSpectator,
                    hasVoted = currentCard != null && currentCard.Votes.ContainsKey(p.ConnectionId)
                }),
                votes = room.State == RoomState.Revealed ? namedVotes : null,
                consensus = room.State == RoomState.Revealed
                    ? _roomService.CalculateConsensus(currentCard?.Votes.Values ?? Enumerable.Empty<string>())
                    : null,
                average = room.State == RoomState.Revealed
                    ? _roomService.CalculateAverage(currentCard?.Votes.Values ?? Enumerable.Empty<string>())
                    : null
            });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task Vote(string roomCode, string value)
    {
        try
        {
            _roomService.Vote(roomCode, Context.ConnectionId, value);

            var room = _roomService.GetRoom(roomCode)!;
            var player = room.Players[Context.ConnectionId];

            // Notify everyone that this player has voted (without revealing the value)
            if (room.State == RoomState.Voting)
            {
                await Clients.Group(room.Code).SendAsync("VoteReceived", new
                {
                    playerName = player.Name
                });
            }
            else if (room.State == RoomState.Revealed)
            {
                // After reveal, votes are visible — send the updated vote
                await Clients.Group(room.Code).SendAsync("VoteUpdated", new
                {
                    playerName = player.Name,
                    value,
                    consensus = _roomService.CalculateConsensus(room.CurrentCard?.Votes.Values ?? Enumerable.Empty<string>()),
                    average = _roomService.CalculateAverage(room.CurrentCard?.Votes.Values ?? Enumerable.Empty<string>())
                });
            }
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task RevealCards(string roomCode)
    {
        try
        {
            var namedVotes = _roomService.RevealCards(roomCode, Context.ConnectionId);
            var room = _roomService.GetRoom(roomCode)!;
            var card = room.CurrentCard;

            await Clients.Group(room.Code).SendAsync("CardsRevealed", new
            {
                votes = namedVotes,
                consensus = _roomService.CalculateConsensus(card?.Votes.Values ?? Enumerable.Empty<string>()),
                average = _roomService.CalculateAverage(card?.Votes.Values ?? Enumerable.Empty<string>())
            });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task AcceptEstimate(string roomCode, string value)
    {
        try
        {
            _roomService.AcceptEstimate(roomCode, Context.ConnectionId, value);

            await Clients.Group(roomCode.ToUpperInvariant()).SendAsync("EstimateAccepted", new
            {
                value
            });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task Revote(string roomCode)
    {
        try
        {
            _roomService.Revote(roomCode, Context.ConnectionId);
            var room = _roomService.GetRoom(roomCode)!;

            await Clients.Group(room.Code).SendAsync("NewRound", new
            {
                cardIndex = room.CurrentCardIndex,
                card = new { room.CurrentCard!.Subject, room.CurrentCard.Description },
                totalCards = room.Cards.Count
            });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task NextQuestion(string roomCode)
    {
        try
        {
            var nextCard = _roomService.NextQuestion(roomCode, Context.ConnectionId);
            var room = _roomService.GetRoom(roomCode)!;

            if (nextCard == null)
            {
                // Game finished
                var results = _roomService.GetResults(roomCode);
                await Clients.Group(room.Code).SendAsync("GameFinished", new
                {
                    results
                });
            }
            else
            {
                await Clients.Group(room.Code).SendAsync("NewRound", new
                {
                    cardIndex = room.CurrentCardIndex,
                    card = new { nextCard.Subject, nextCard.Description },
                    totalCards = room.Cards.Count
                });
            }
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task GetResults(string roomCode)
    {
        try
        {
            var results = _roomService.GetResults(roomCode);
            await Clients.Caller.SendAsync("Results", new { results });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = _roomService.GetRoomByPlayer(Context.ConnectionId);
        if (room != null)
        {
            var player = room.Players.GetValueOrDefault(Context.ConnectionId);
            var playerName = player?.Name ?? "Unknown";

            _roomService.RemovePlayer(Context.ConnectionId);

            if (room.Players.Count > 0)
            {
                var newOwner = room.GetOwner();
                await Clients.Group(room.Code).SendAsync("PlayerLeft", new
                {
                    playerName,
                    playerCount = room.Players.Count,
                    newOwnerName = newOwner?.Name
                });
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
