using System;
using MassTransit;

namespace RideOn.Components
{
    public class PatronState :
        SagaStateMachineInstance
    {
        public Guid CorrelationId { get; set; }
        public int CurrentState { get; set; }
        public int VisitedStatus { get; set; }

        public DateTime Entered { get; set; }
        public DateTime Left { get; set; }
    }
}