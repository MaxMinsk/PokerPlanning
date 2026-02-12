namespace PokerPlanning.Models;

public class Player
{
    public string PlayerId { get; set; } = Guid.NewGuid().ToString("N");
    public required string ConnectionId { get; set; }
    public required string Name { get; set; }
    public bool IsOwner { get; set; }
    public bool WasOriginalOwner { get; set; }
    public bool IsSpectator { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public bool IsConnected => DisconnectedAt == null;
}
