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

    public async Task CreateRoom(string? ownerName, int scaleType, string cardsText, int? sessionMinutes = null, bool coffeeBreak = false)
    {
        try
        {
            var scale = (ScaleType)scaleType;
            var room = _roomService.CreateRoom(ownerName, scale, cardsText, Context.ConnectionId, sessionMinutes, coffeeBreak);

            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
            var creatorPlayer = room.Players[Context.ConnectionId];
            await Clients.Caller.SendAsync("RoomCreated", new
            {
                roomCode = room.Code,
                playerId = creatorPlayer.PlayerId,
                scale = ScaleDefinitions.GetScale(room.Scale),
                scaleName = ScaleDefinitions.GetDisplayName(room.Scale),
                currentCard = new { room.CurrentCard!.Subject, room.CurrentCard.Description },
                currentCardIndex = room.CurrentCardIndex,
                totalCards = room.Cards.Count,
                isOwner = true,
                isSpectator = creatorPlayer.IsSpectator,
                secondsPerCard = room.SecondsPerCard,
                coffeeBreakEnabled = room.CoffeeBreakEnabled,
                players = _roomService.GetActivePlayers(room).Select(p => new
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
                playerCount = _roomService.GetActivePlayers(room).Count()
            });

            await SendFullState(room, player);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task RejoinRoom(string roomCode, string playerId)
    {
        try
        {
            var player = _roomService.RejoinRoom(roomCode, playerId, Context.ConnectionId);
            if (player == null)
            {
                await Clients.Caller.SendAsync("RejoinFailed", "Session expired or room not found.");
                return;
            }

            var room = _roomService.GetRoom(roomCode)!;
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            // Notify others that player is back
            await Clients.OthersInGroup(room.Code).SendAsync("PlayerJoined", new
            {
                name = player.Name,
                isOwner = player.IsOwner,
                isSpectator = player.IsSpectator,
                hasVoted = room.CurrentCard?.Votes.ContainsKey(Context.ConnectionId) ?? false,
                playerCount = _roomService.GetActivePlayers(room).Count()
            });

            await SendFullState(room, player);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("RejoinFailed", ex.Message);
        }
    }

    private async Task SendFullState(Room room, Player player)
    {
        var currentCard = room.CurrentCard;
        var namedVotes = room.State == RoomState.Revealed
            ? _roomService.GetNamedVotes(room)
            : new Dictionary<string, string>();

        // Check if this player has voted on the current card
        var myVote = currentCard?.Votes.TryGetValue(player.ConnectionId, out var v) == true ? v : null;

        await Clients.Caller.SendAsync("RoomState", new
        {
            roomCode = room.Code,
            playerId = player.PlayerId,
            scale = ScaleDefinitions.GetScale(room.Scale),
            scaleName = ScaleDefinitions.GetDisplayName(room.Scale),
            currentCard = currentCard != null ? new { currentCard.Subject, currentCard.Description } : null,
            currentCardIndex = room.CurrentCardIndex,
            totalCards = room.Cards.Count,
            state = room.State.ToString(),
            isOwner = player.IsOwner,
            isSpectator = player.IsSpectator,
            myVote,
            secondsPerCard = room.SecondsPerCard,
            cardTimerStartedAt = room.CardTimerStartedAt?.ToString("o"),
            coffeeBreakEnabled = room.CoffeeBreakEnabled,
            players = _roomService.GetActivePlayers(room).Select(p => new
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
                : null,
            coffeeVotes = room.State == RoomState.Revealed
                ? _roomService.CountCoffeeVotes(currentCard?.Votes.Values ?? Enumerable.Empty<string>())
                : 0
        });
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
                var updatedVotes = room.CurrentCard?.Votes.Values ?? Enumerable.Empty<string>();
                await Clients.Group(room.Code).SendAsync("VoteUpdated", new
                {
                    playerName = player.Name,
                    value,
                    consensus = _roomService.CalculateConsensus(updatedVotes),
                    average = _roomService.CalculateAverage(updatedVotes),
                    coffeeVotes = _roomService.CountCoffeeVotes(updatedVotes)
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

            var cardVotes = card?.Votes.Values ?? Enumerable.Empty<string>();
            await Clients.Group(room.Code).SendAsync("CardsRevealed", new
            {
                votes = namedVotes,
                consensus = _roomService.CalculateConsensus(cardVotes),
                average = _roomService.CalculateAverage(cardVotes),
                coffeeVotes = _roomService.CountCoffeeVotes(cardVotes)
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
                totalCards = room.Cards.Count,
                secondsPerCard = room.SecondsPerCard
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
                    totalCards = room.Cards.Count,
                    secondsPerCard = room.SecondsPerCard
                });
            }
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task PlayerThinking(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;

        var player = room.Players.GetValueOrDefault(Context.ConnectionId);
        if (player == null || player.IsSpectator) return;

        await Clients.OthersInGroup(room.Code).SendAsync("PlayerThinking", new
        {
            playerName = player.Name
        });
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

            // Mark as disconnected (grace period) instead of removing immediately
            _roomService.DisconnectPlayer(Context.ConnectionId);

            var activePlayers = _roomService.GetActivePlayers(room);
            if (activePlayers.Any())
            {
                var newOwner = room.GetOwner();
                await Clients.Group(room.Code).SendAsync("PlayerLeft", new
                {
                    playerName,
                    playerCount = activePlayers.Count(),
                    newOwnerName = newOwner?.Name
                });
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
