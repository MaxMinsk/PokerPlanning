namespace PokerPlanning.Models;

public class Player
{
    public required string ConnectionId { get; set; }
    public required string Name { get; set; }
    public bool IsOwner { get; set; }
    public bool IsSpectator { get; set; }
}
