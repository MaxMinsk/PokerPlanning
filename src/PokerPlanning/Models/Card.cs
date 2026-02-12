using System.Collections.Concurrent;

namespace PokerPlanning.Models;

public class Card
{
    public required string Subject { get; set; }
    public string? Description { get; set; }
    public string? AcceptedEstimate { get; set; }
    public int OriginalIndex { get; set; }
    public ConcurrentDictionary<string, string> Votes { get; set; } = new();
}
