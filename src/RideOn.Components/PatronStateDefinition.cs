using GreenPipes;
using GreenPipes.Partitioning;
using MassTransit;
using MassTransit.Definition;
using RideOn.Contracts;

namespace RideOn.Components
{
    public class PatronStateDefinition :
        SagaDefinition<PatronState>
    {
        readonly IPartitioner _partition;

        public PatronStateDefinition()
        {
            _partition = new Partitioner(64, new Murmur3UnsafeHashGenerator());
        }

        protected override void ConfigureSaga(IReceiveEndpointConfigurator endpointConfigurator, ISagaConfigurator<PatronState> sagaConfigurator)
        {
            sagaConfigurator.Message<PatronEntered>(x => x.UsePartitioner(_partition, m => m.Message.PatronId));
            sagaConfigurator.Message<PatronLeft>(x => x.UsePartitioner(_partition, m => m.Message.PatronId));

            endpointConfigurator.UseMessageRetry(r => r.Intervals(20, 50, 100, 1000, 5000));
        }
    }
}