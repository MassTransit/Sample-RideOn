using System;

namespace RideOn.Contracts
{
    public interface PatronEntered
    {
        Guid PatronId { get; }
        DateTime Timestamp { get; }
    }
}