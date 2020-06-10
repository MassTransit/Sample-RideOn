using System;

namespace RideOn.Contracts
{
    public interface PatronLeft
    {
        Guid PatronId { get; }
        DateTime Timestamp { get; }
    }
}