using System.Collections.Concurrent;
using PokerPlanning.Models;

namespace PokerPlanning.Services;

public class RoomService
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private static readonly Random _random = new();

    public Room CreateRoom(string? ownerName, ScaleType scale, string cardsText, string ownerConnectionId, int? sessionMinutes = null, bool coffeeBreak = false)
    {
        var code = GenerateCode();
        var cards = ParseCards(cardsText);

        if (cards.Count == 0)
            throw new ArgumentException("At least one card/question is required.");

        var room = new Room
        {
            Code = code,
            OwnerConnectionId = ownerConnectionId,
            Scale = scale,
            Cards = cards,
            CurrentCardIndex = 0,
            State = RoomState.Voting,
            CoffeeBreakEnabled = coffeeBreak
        };

        // Session timer
        if (sessionMinutes.HasValue && sessionMinutes.Value > 0)
        {
            room.SessionMinutes = sessionMinutes.Value;
            room.SecondsPerCard = (sessionMinutes.Value * 60) / cards.Count;
            room.CardTimerStartedAt = DateTime.UtcNow;
        }

        var isSpectator = string.IsNullOrWhiteSpace(ownerName);
        var player = new Player
        {
            ConnectionId = ownerConnectionId,
            Name = isSpectator ? "Spectator" : ownerName!.Trim(),
            IsOwner = true,
            IsSpectator = isSpectator
        };

        room.Players[ownerConnectionId] = player;
        _rooms[code] = room;

        return room;
    }

    private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromMinutes(5);

    public Room? GetRoom(string code) =>
        _rooms.TryGetValue(code.ToUpperInvariant(), out var room) ? room : null;

    public Player JoinRoom(string code, string playerName, string connectionId)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");

        // Check if there's a disconnected player with the same name — reconnect them
        var existingByName = room.Players.Values
            .FirstOrDefault(p => p.Name.Equals(playerName.Trim(), StringComparison.OrdinalIgnoreCase) && !p.IsConnected);
        if (existingByName != null)
        {
            return ReconnectPlayer(room, existingByName, connectionId);
        }

        var activeCount = room.Players.Values.Count(p => p.IsConnected);
        if (activeCount >= 50)
            throw new InvalidOperationException("Room is full (max 50 players).");

        if (room.Players.ContainsKey(connectionId))
            return room.Players[connectionId];

        var player = new Player
        {
            ConnectionId = connectionId,
            Name = playerName.Trim(),
            IsOwner = false,
            IsSpectator = false
        };

        room.Players[connectionId] = player;
        return player;
    }

    /// <summary>
    /// Rejoin by playerId (from localStorage). Returns player or null if not found.
    /// </summary>
    public Player? RejoinRoom(string code, string playerId, string newConnectionId)
    {
        var room = GetRoom(code);
        if (room == null) return null;

        var player = room.Players.Values.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return null;

        return ReconnectPlayer(room, player, newConnectionId);
    }

    private Player ReconnectPlayer(Room room, Player player, string newConnectionId)
    {
        var oldConnectionId = player.ConnectionId;

        // Remove old key, update connection, re-add with new key
        room.Players.Remove(oldConnectionId);
        player.ConnectionId = newConnectionId;
        player.DisconnectedAt = null;
        room.Players[newConnectionId] = player;

        // Migrate votes from old connectionId to new
        foreach (var card in room.Cards)
        {
            if (card.Votes.Remove(oldConnectionId, out var vote))
            {
                card.Votes[newConnectionId] = vote;
            }
        }

        // Restore ownership if this was the owner
        if (room.OwnerConnectionId == oldConnectionId)
        {
            room.OwnerConnectionId = newConnectionId;
        }

        return player;
    }

    public void DisconnectPlayer(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.Players.TryGetValue(connectionId, out var player))
            {
                player.DisconnectedAt = DateTime.UtcNow;

                // If owner disconnected, transfer ownership to first connected player
                if (room.IsOwner(connectionId))
                {
                    var newOwner = room.Players.Values.FirstOrDefault(p => p.IsConnected && !p.IsSpectator && p.ConnectionId != connectionId);
                    if (newOwner != null)
                    {
                        player.IsOwner = false;
                        newOwner.IsOwner = true;
                        room.OwnerConnectionId = newOwner.ConnectionId;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Clean up players who have been disconnected longer than the grace period.
    /// Called periodically by a background timer.
    /// </summary>
    public (string roomCode, string playerName, string? newOwnerName)? CleanupDisconnected()
    {
        var now = DateTime.UtcNow;
        foreach (var room in _rooms.Values)
        {
            var expired = room.Players.Values
                .Where(p => p.DisconnectedAt.HasValue && (now - p.DisconnectedAt.Value) > DisconnectGracePeriod)
                .ToList();

            foreach (var player in expired)
            {
                room.Players.Remove(player.ConnectionId);
            }

            if (room.Players.Count == 0)
            {
                _rooms.TryRemove(room.Code, out _);
            }
        }
        return null;
    }

    public Room? GetRoomByPlayer(string connectionId)
    {
        return _rooms.Values.FirstOrDefault(r => r.Players.ContainsKey(connectionId));
    }

    public void Vote(string code, string connectionId, string value)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");
        var card = room.CurrentCard ?? throw new InvalidOperationException("No active card.");

        if (!room.Players.TryGetValue(connectionId, out var player))
            throw new InvalidOperationException("Player not in room.");

        if (player.IsSpectator)
            throw new InvalidOperationException("Spectators cannot vote.");

        var scale = ScaleDefinitions.GetScale(room.Scale);
        if (!scale.Contains(value) && !(room.CoffeeBreakEnabled && value == CoffeeVote))
            throw new ArgumentException($"Invalid vote value: {value}");

        card.Votes[connectionId] = value;
    }

    public Dictionary<string, string> RevealCards(string code, string connectionId)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");

        if (!room.IsOwner(connectionId))
            throw new InvalidOperationException("Only the room owner can reveal cards.");

        if (room.State != RoomState.Voting)
            throw new InvalidOperationException("Cards are already revealed.");

        room.State = RoomState.Revealed;

        // Return votes with player names instead of connection IDs
        return GetNamedVotes(room);
    }

    public void AcceptEstimate(string code, string connectionId, string value)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");

        if (!room.IsOwner(connectionId))
            throw new InvalidOperationException("Only the room owner can accept estimates.");

        var card = room.CurrentCard ?? throw new InvalidOperationException("No active card.");
        card.AcceptedEstimate = value;
    }

    public void Revote(string code, string connectionId)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");

        if (!room.IsOwner(connectionId))
            throw new InvalidOperationException("Only the room owner can trigger revote.");

        var card = room.CurrentCard ?? throw new InvalidOperationException("No active card.");
        card.Votes.Clear();
        card.AcceptedEstimate = null;
        room.State = RoomState.Voting;
        if (room.SecondsPerCard.HasValue)
            room.CardTimerStartedAt = DateTime.UtcNow;
    }

    public Card? NextQuestion(string code, string connectionId)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");

        if (!room.IsOwner(connectionId))
            throw new InvalidOperationException("Only the room owner can advance questions.");

        // Auto-accept current card's estimate if not set
        var currentCard = room.CurrentCard;
        if (currentCard != null && currentCard.AcceptedEstimate == null && currentCard.Votes.Count > 0)
        {
            currentCard.AcceptedEstimate = CalculateConsensus(currentCard.Votes.Values);
        }

        room.CurrentCardIndex++;

        if (room.CurrentCardIndex >= room.Cards.Count)
        {
            room.State = RoomState.Finished;
            return null;
        }

        // Reset timer for next card
        if (room.SecondsPerCard.HasValue)
            room.CardTimerStartedAt = DateTime.UtcNow;

        room.State = RoomState.Voting;
        return room.CurrentCard;
    }

    public Dictionary<string, string> GetNamedVotes(Room room)
    {
        var card = room.CurrentCard;
        if (card == null) return new();

        var result = new Dictionary<string, string>();
        foreach (var (connId, vote) in card.Votes)
        {
            if (room.Players.TryGetValue(connId, out var player))
                result[player.Name] = vote;
        }
        return result;
    }

    /// <summary>
    /// Get connected players for display (filter out disconnected from player list shown to others).
    /// </summary>
    public IEnumerable<Player> GetActivePlayers(Room room)
    {
        return room.Players.Values.Where(p => p.IsConnected);
    }

    public const string CoffeeVote = "☕";

    public string? CalculateConsensus(IEnumerable<string> votes)
    {
        var voteList = votes.Where(v => v != "?" && v != CoffeeVote).ToList();
        if (voteList.Count == 0) return null;

        var topGroup = voteList
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .First();

        // Consensus only when majority (>50%) agrees on the same value
        if (topGroup.Count() > voteList.Count / 2.0)
            return topGroup.Key;

        // No majority — no consensus. Owner decides via Accept.
        return null;
    }

    public int CountCoffeeVotes(IEnumerable<string> votes)
    {
        return votes.Count(v => v == CoffeeVote);
    }

    public double? CalculateAverage(IEnumerable<string> votes)
    {
        var numericVotes = votes
            .Where(v => v != CoffeeVote && double.TryParse(v, out _))
            .Select(v => double.Parse(v))
            .ToList();

        return numericVotes.Count > 0 ? Math.Round(numericVotes.Average(), 2) : null;
    }

    public List<object> GetResults(string code)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");

        return room.Cards.Select((card, index) => (object)new
        {
            index = index + 1,
            subject = card.Subject,
            description = card.Description,
            estimate = card.AcceptedEstimate,
            votes = card.Votes.ToDictionary(
                v => room.Players.TryGetValue(v.Key, out var p) ? p.Name : "Unknown",
                v => v.Value
            )
        }).ToList();
    }

    private static List<Card> ParseCards(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Select(line =>
            {
                var parts = line.Split(';', 2);
                return new Card
                {
                    Subject = parts[0].Trim(),
                    Description = parts.Length > 1 ? parts[1].Trim() : null
                };
            })
            .ToList();
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => chars[_random.Next(chars.Length)]).ToArray());
    }
}
