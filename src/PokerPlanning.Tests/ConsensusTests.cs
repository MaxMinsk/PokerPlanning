using PokerPlanning.Services;

namespace PokerPlanning.Tests;

public class ConsensusTests
{
    private readonly RoomService _svc = new();

    [Fact]
    public void Consensus_MajorityAgrees()
    {
        // 3 out of 5 = 60% > 50%
        var votes = new[] { "5", "5", "5", "8", "3" };
        Assert.Equal("5", _svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Consensus_ExactHalf_NoConsensus()
    {
        // 2 out of 4 = 50%, not > 50%
        var votes = new[] { "5", "5", "8", "8" };
        Assert.Null(_svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Consensus_AllSame()
    {
        var votes = new[] { "13", "13", "13" };
        Assert.Equal("13", _svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Consensus_AllDifferent_NoConsensus()
    {
        var votes = new[] { "1", "2", "3", "5" };
        Assert.Null(_svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Consensus_SingleVote()
    {
        var votes = new[] { "8" };
        Assert.Equal("8", _svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Consensus_Empty_ReturnsNull()
    {
        Assert.Null(_svc.CalculateConsensus(Array.Empty<string>()));
    }

    [Fact]
    public void Consensus_IgnoresQuestionMark()
    {
        var votes = new[] { "5", "5", "?" };
        Assert.Equal("5", _svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Consensus_IgnoresCoffeeVotes()
    {
        var votes = new[] { "5", "5", "☕" };
        Assert.Equal("5", _svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Consensus_AllQuestionMarks_ReturnsNull()
    {
        var votes = new[] { "?", "?", "?" };
        Assert.Null(_svc.CalculateConsensus(votes));
    }

    [Fact]
    public void Average_NumericVotes()
    {
        var votes = new[] { "2", "4", "6" };
        Assert.Equal(4.0, _svc.CalculateAverage(votes));
    }

    [Fact]
    public void Average_MixedNumericAndNonNumeric()
    {
        var votes = new[] { "5", "XL", "?", "10" };
        Assert.Equal(7.5, _svc.CalculateAverage(votes));
    }

    [Fact]
    public void Average_AllNonNumeric_ReturnsNull()
    {
        var votes = new[] { "XS", "M", "XL", "?" };
        Assert.Null(_svc.CalculateAverage(votes));
    }

    [Fact]
    public void Average_IgnoresCoffeeVotes()
    {
        var votes = new[] { "5", "☕", "10" };
        Assert.Equal(7.5, _svc.CalculateAverage(votes));
    }

    [Fact]
    public void Average_Empty_ReturnsNull()
    {
        Assert.Null(_svc.CalculateAverage(Array.Empty<string>()));
    }

    [Fact]
    public void CountCoffeeVotes_CountsCorrectly()
    {
        var votes = new[] { "5", "☕", "8", "☕", "☕" };
        Assert.Equal(3, _svc.CountCoffeeVotes(votes));
    }

    [Fact]
    public void CountCoffeeVotes_NoCoffee()
    {
        var votes = new[] { "5", "8", "13" };
        Assert.Equal(0, _svc.CountCoffeeVotes(votes));
    }

    [Fact]
    public void CountCoffeeVotes_Empty()
    {
        Assert.Equal(0, _svc.CountCoffeeVotes(Array.Empty<string>()));
    }
}
