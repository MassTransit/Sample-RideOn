using System;

namespace RideOn.Contracts
{
    public interface PatronVisited
    {
        Guid PatronId { get; }
        DateTime Entered { get; }
        DateTime Left { get; }
    }
}