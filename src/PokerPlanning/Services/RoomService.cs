using System.Collections.Concurrent;
using PokerPlanning.Models;

namespace PokerPlanning.Services;

public class RoomService
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private static readonly Random _random = new();

    public Room CreateRoom(string? ownerName, ScaleType scale, string cardsText, string ownerConnectionId)
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
            State = RoomState.Voting
        };

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

    public Room? GetRoom(string code) =>
        _rooms.TryGetValue(code.ToUpperInvariant(), out var room) ? room : null;

    public Player JoinRoom(string code, string playerName, string connectionId)
    {
        var room = GetRoom(code) ?? throw new ArgumentException("Room not found.");

        if (room.Players.Count >= 18)
            throw new InvalidOperationException("Room is full (max 18 players).");

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

    public void RemovePlayer(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.Players.Remove(connectionId, out _))
            {
                // If owner left, transfer ownership to first remaining player
                if (room.IsOwner(connectionId) && room.Players.Count > 0)
                {
                    var newOwner = room.Players.Values.First();
                    newOwner.IsOwner = true;
                    room.OwnerConnectionId = newOwner.ConnectionId;
                }

                // Clean up empty rooms
                if (room.Players.Count == 0)
                {
                    _rooms.TryRemove(room.Code, out _);
                }
            }
        }
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
        if (!scale.Contains(value))
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

    public string? CalculateConsensus(IEnumerable<string> votes)
    {
        var voteList = votes.Where(v => v != "?").ToList();
        if (voteList.Count == 0) return null;

        return voteList
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    public double? CalculateAverage(IEnumerable<string> votes)
    {
        var numericVotes = votes
            .Where(v => double.TryParse(v, out _))
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
