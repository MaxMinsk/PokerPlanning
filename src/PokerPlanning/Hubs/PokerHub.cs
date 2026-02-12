using Microsoft.AspNetCore.SignalR;
using PokerPlanning.Models;
using PokerPlanning.Services;

namespace PokerPlanning.Hubs;

public class PokerHub : Hub
{
    private readonly RoomService _roomService;
    private readonly ILogger<PokerHub> _logger;

    public PokerHub(RoomService roomService, ILogger<PokerHub> logger)
    {
        _roomService = roomService;
        _logger = logger;
    }

    public async Task CreateRoom(string? ownerName, int scaleType, string cardsText, int? sessionMinutes = null, bool coffeeBreak = false, bool shuffle = false)
    {
        try
        {
            var scale = (ScaleType)scaleType;
            var room = _roomService.CreateRoom(ownerName, scale, cardsText, Context.ConnectionId, sessionMinutes, coffeeBreak, shuffle);

            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
            var creatorPlayer = room.Players[Context.ConnectionId];

            _logger.LogInformation("Room {RoomCode} created by \"{OwnerName}\" ({ScaleName}, {CardCount} cards, shuffle={Shuffle})",
                room.Code, creatorPlayer.Name, ScaleDefinitions.GetDisplayName(room.Scale), room.Cards.Count, shuffle);

            await Clients.Caller.SendAsync("RoomCreated", new
            {
                roomCode = room.Code,
                playerId = creatorPlayer.PlayerId,
                myName = creatorPlayer.Name,
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
            _logger.LogError(ex, "CreateRoom failed for {ConnectionId}", Context.ConnectionId);
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
                _logger.LogWarning("JoinRoom: room {RoomCode} not found for {ConnectionId}", roomCode, Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Room not found.");
                return;
            }

            var player = _roomService.JoinRoom(roomCode, playerName, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            var activeCount = _roomService.GetActivePlayers(room).Count();
            _logger.LogInformation("Player \"{PlayerName}\" joined room {RoomCode} ({PlayerCount} players)",
                player.Name, room.Code, activeCount);

            // Notify others
            await Clients.OthersInGroup(room.Code).SendAsync("PlayerJoined", new
            {
                name = player.Name,
                isOwner = player.IsOwner,
                isSpectator = player.IsSpectator,
                hasVoted = false,
                playerCount = activeCount
            });

            await SendFullState(room, player);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JoinRoom failed for {ConnectionId} in room {RoomCode}", Context.ConnectionId, roomCode);
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
                _logger.LogInformation("Rejoin failed: session expired or room {RoomCode} not found (playerId={PlayerId})",
                    roomCode, playerId);
                await Clients.Caller.SendAsync("RejoinFailed", "Session expired or room not found.");
                return;
            }

            var room = _roomService.GetRoom(roomCode)!;
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            _logger.LogInformation("Player \"{PlayerName}\" rejoined room {RoomCode} (isOwner={IsOwner})",
                player.Name, room.Code, player.IsOwner);

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
            _logger.LogError(ex, "RejoinRoom failed for {ConnectionId} in room {RoomCode}", Context.ConnectionId, roomCode);
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
            myName = player.Name,
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
            _logger.LogError(ex, "Vote failed for {ConnectionId} in room {RoomCode}, value={Value}",
                Context.ConnectionId, roomCode, value);
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
            var consensus = _roomService.CalculateConsensus(cardVotes);
            _logger.LogInformation("Cards revealed in {RoomCode} card #{CardIndex}, consensus={Consensus}, votes={VoteCount}",
                room.Code, room.CurrentCardIndex + 1, consensus ?? "none", namedVotes.Count);

            await Clients.Group(room.Code).SendAsync("CardsRevealed", new
            {
                votes = namedVotes,
                consensus,
                average = _roomService.CalculateAverage(cardVotes),
                coffeeVotes = _roomService.CountCoffeeVotes(cardVotes)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RevealCards failed for {ConnectionId} in room {RoomCode}", Context.ConnectionId, roomCode);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task AcceptEstimate(string roomCode, string value)
    {
        try
        {
            _roomService.AcceptEstimate(roomCode, Context.ConnectionId, value);

            _logger.LogInformation("Estimate accepted in {RoomCode}: {Value}", roomCode, value);

            await Clients.Group(roomCode.ToUpperInvariant()).SendAsync("EstimateAccepted", new
            {
                value
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AcceptEstimate failed for {ConnectionId} in room {RoomCode}", Context.ConnectionId, roomCode);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    public async Task Revote(string roomCode)
    {
        try
        {
            _roomService.Revote(roomCode, Context.ConnectionId);
            var room = _roomService.GetRoom(roomCode)!;

            _logger.LogInformation("Revote triggered in {RoomCode} card #{CardIndex}", room.Code, room.CurrentCardIndex + 1);

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
            _logger.LogError(ex, "Revote failed for {ConnectionId} in room {RoomCode}", Context.ConnectionId, roomCode);
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
                _logger.LogInformation("Game finished in {RoomCode} ({CardCount} cards)", room.Code, room.Cards.Count);
                var results = _roomService.GetResults(roomCode);
                await Clients.Group(room.Code).SendAsync("GameFinished", new
                {
                    results
                });
            }
            else
            {
                _logger.LogInformation("Next question in {RoomCode}: card #{CardIndex}/{TotalCards}",
                    room.Code, room.CurrentCardIndex + 1, room.Cards.Count);
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
            _logger.LogError(ex, "NextQuestion failed for {ConnectionId} in room {RoomCode}", Context.ConnectionId, roomCode);
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
            _logger.LogError(ex, "GetResults failed for {ConnectionId} in room {RoomCode}", Context.ConnectionId, roomCode);
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
            var wasOwner = player?.IsOwner == true;

            // Mark as disconnected (grace period) instead of removing immediately
            _roomService.DisconnectPlayer(Context.ConnectionId);

            var activePlayers = _roomService.GetActivePlayers(room);
            if (activePlayers.Any())
            {
                // Only send newOwnerName if ownership actually changed
                string? newOwnerName = null;
                if (wasOwner)
                {
                    newOwnerName = room.GetOwner()?.Name;
                    _logger.LogWarning("Owner \"{PlayerName}\" disconnected from {RoomCode}, transferred to \"{NewOwner}\"",
                        playerName, room.Code, newOwnerName ?? "nobody");
                }
                else
                {
                    _logger.LogInformation("Player \"{PlayerName}\" disconnected from {RoomCode} ({PlayerCount} remaining)",
                        playerName, room.Code, activePlayers.Count());
                }

                await Clients.Group(room.Code).SendAsync("PlayerLeft", new
                {
                    playerName,
                    playerCount = activePlayers.Count(),
                    newOwnerName
                });
            }
        }

        if (exception != null)
        {
            _logger.LogError(exception, "Connection {ConnectionId} disconnected with error", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
