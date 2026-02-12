using System.Collections.Concurrent;

namespace PokerPlanning.Models;

public class Room
{
    public required string Code { get; set; }
    public string? OwnerConnectionId { get; set; }
    public ScaleType Scale { get; set; } = ScaleType.Fibonacci;
    public List<Card> Cards { get; set; } = [];
    public int CurrentCardIndex { get; set; }
    public ConcurrentDictionary<string, Player> Players { get; set; } = new();
    public RoomState State { get; set; } = RoomState.Voting;

    // Coffee break voting
    public bool CoffeeBreakEnabled { get; set; }

    // Session timer (optional)
    public int? SessionMinutes { get; set; }          // Total session time in minutes
    public int? SecondsPerCard { get; set; }           // Calculated: SessionMinutes * 60 / Cards.Count
    public DateTime? CardTimerStartedAt { get; set; }  // When current card timer started

    public Card? CurrentCard =>
        CurrentCardIndex >= 0 && CurrentCardIndex < Cards.Count
            ? Cards[CurrentCardIndex]
            : null;

    public bool IsOwner(string connectionId) =>
        OwnerConnectionId == connectionId;

    public Player? GetOwner() =>
        OwnerConnectionId != null && Players.TryGetValue(OwnerConnectionId, out var owner)
            ? owner
            : null;
}
