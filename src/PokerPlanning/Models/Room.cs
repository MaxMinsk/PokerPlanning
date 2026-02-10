namespace PokerPlanning.Models;

public class Room
{
    public required string Code { get; set; }
    public string? OwnerConnectionId { get; set; }
    public ScaleType Scale { get; set; } = ScaleType.Fibonacci;
    public List<Card> Cards { get; set; } = [];
    public int CurrentCardIndex { get; set; }
    public Dictionary<string, Player> Players { get; set; } = new();
    public RoomState State { get; set; } = RoomState.Voting;

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
