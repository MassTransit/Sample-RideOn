using System;
using MassTransit;
using RideOn.Contracts;

namespace RideOn.Components
{
    public sealed class PatronStateMachine :
        MassTransitStateMachine<PatronState>
    {
        public PatronStateMachine()
        {
            Event(() => Entered, x => x.CorrelateById(m => m.Message.PatronId));
            Event(() => Left, x => x.CorrelateById(m => m.Message.PatronId));

            InstanceState(x => x.CurrentState, Tracking);

            Initially(
                When(Entered)
                    .Then(context => context.Saga.Entered = context.Message.Timestamp)
                    .TransitionTo(Tracking),
                When(Left)
                    .Then(context => context.Saga.Left = context.Message.Timestamp)
                    .TransitionTo(Tracking)
            );

            During(Tracking,
                When(Entered)
                    .Then(context => context.Saga.Entered = context.Message.Timestamp),
                When(Left)
                    .Then(context => context.Saga.Left = context.Message.Timestamp)
            );

            CompositeEvent(() => Visited, x => x.VisitedStatus, CompositeEventOptions.IncludeInitial, Entered, Left);

            DuringAny(
                When(Visited)
                    .Then(context => Console.WriteLine("Visited: {0}", context.Saga.CorrelationId))

                    // Publish will go to RabbitMQ, via the bus
                    .PublishAsync(context => context.Init<PatronVisited>(new
                    {
                        PatronId = context.Saga.CorrelationId,
                        context.Saga.Entered,
                        context.Saga.Left
                    }))

                    // Produce will go to Kafka
                    .Produce(context => context.Init<PatronVisited>(new
                    {
                        PatronId = context.Saga.CorrelationId,
                        context.Saga.Entered,
                        context.Saga.Left
                    }))
                    .Finalize()
            );

            SetCompletedWhenFinalized();
        }

        public State Tracking { get; private set; }
        public Event<PatronEntered> Entered { get; private set; }
        public Event<PatronLeft> Left { get; private set; }
        public Event Visited { get; private set; }
    }
}