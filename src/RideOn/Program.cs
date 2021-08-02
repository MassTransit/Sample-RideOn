using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using GreenPipes;
using MassTransit;
using MassTransit.KafkaIntegration;
using MassTransit.KafkaIntegration.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RideOn.Components;
using RideOn.Contracts;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace RideOn
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();
            var services = new ServiceCollection();

            services.TryAddSingleton<ILoggerFactory>(new SerilogLoggerFactory());
            services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));

            services.AddMassTransit(x =>
            {
                x.AddConsumer<PatronVisitedConsumer>();

                x.UsingRabbitMq((context, cfg) => cfg.ConfigureEndpoints(context));

                x.AddRider(rider =>
                {
                    rider.AddSagaStateMachine<PatronStateMachine, PatronState, PatronStateDefinition>()
                        .InMemoryRepository();

                    rider.AddProducer<PatronEntered>(nameof(PatronEntered));
                    rider.AddProducer<PatronLeft>(nameof(PatronLeft));

                    // duplicative, since it's already published to RabbitMQ, but showing how to also
                    // produce an event on Kafka from a state machine
                    rider.AddProducer<PatronVisited>(nameof(PatronVisited));

                    rider.UsingKafka((context, k) =>
                    {
                        k.Host("localhost:9092");

                        k.TopicEndpoint<Null, PatronEntered>(nameof(PatronEntered), nameof(RideOn), c =>
                        {
                            c.AutoOffsetReset = AutoOffsetReset.Earliest;
                            c.CreateIfMissing(t => t.NumPartitions = 1);
                            c.ConfigureSaga<PatronState>(context);
                        });

                        k.TopicEndpoint<Null, PatronLeft>(nameof(PatronLeft), nameof(RideOn), c =>
                        {
                            c.AutoOffsetReset = AutoOffsetReset.Earliest;
                            c.CreateIfMissing(t => t.NumPartitions = 1);
                            c.ConfigureSaga<PatronState>(context);
                        });
                    });
                });
            });

            await using var provider = services.BuildServiceProvider(true);

            var logger = provider.GetRequiredService<ILogger<Program>>();

            var busControl = provider.GetRequiredService<IBusControl>();

            var startTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

            await busControl.StartAsync(startTokenSource);
            try
            {
                logger.LogInformation("Started");

                await Task.Run(() => Client(provider), CancellationToken.None);
            }
            finally
            {
                await busControl.StopAsync(TimeSpan.FromSeconds(30));
            }
        }

        static async Task Client(IServiceProvider provider)
        {
            var logger = provider.GetRequiredService<ILogger<Program>>();

            while (true)
            {
                Console.Write("Enter # of patrons to visit, or empty to quit: ");
                var line = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(line))
                    break;

                int limit;
                int loops = 1;
                var segments = line.Split(',');
                if (segments.Length == 2)
                {
                    loops = int.TryParse(segments[1], out int result) ? result : 1;
                    limit = int.TryParse(segments[0], out result) ? result : 1;
                }
                else if (!int.TryParse(line, out limit))
                    limit = 1;

                logger.LogInformation("Running {LoopCount} loops of {Limit} patrons each", loops, limit);

                using var serviceScope = provider.CreateScope();

                var enteredProducer = serviceScope.ServiceProvider.GetRequiredService<ITopicProducer<PatronEntered>>();
                var leftProducer = serviceScope.ServiceProvider.GetRequiredService<ITopicProducer<PatronLeft>>();

                var random = new Random();

                for (var pass = 0; pass < loops; pass++)
                {
                    try
                    {
                        var tasks = new List<Task>();

                        var patronIds = NewId.Next(limit);

                        for (var i = 0; i < limit; i++)
                        {
                            var enteredTask = enteredProducer.Produce(new
                            {
                                PatronId = patronIds[i],
                                Timestamp = DateTime.UtcNow
                            });

                            var leftTask = leftProducer.Produce(new
                            {
                                PatronId = patronIds[i],
                                Timestamp = DateTime.UtcNow + TimeSpan.FromMinutes(random.Next(60))
                            });

                            tasks.Add(enteredTask);
                            tasks.Add(leftTask);
                        }

                        await Task.WhenAll(tasks.ToArray());
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Loop Faulted");
                    }
                }
            }
        }
    }
}