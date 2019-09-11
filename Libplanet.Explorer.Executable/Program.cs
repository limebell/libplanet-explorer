using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Crypto;
using Libplanet.Explorer.Interfaces;
using Libplanet.Net;
using Libplanet.Store;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Events;

namespace Libplanet.Explorer.Executable
{
    /// <summary>
    /// The program entry point to run a web server.
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            Options options = Options.Parse(args, Console.Error);

            var loggerConfig = new LoggerConfiguration();
            loggerConfig = options.Debug
                ? loggerConfig.MinimumLevel.Debug()
                : loggerConfig.MinimumLevel.Information();
            loggerConfig = loggerConfig
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console();
            Log.Logger = loggerConfig.CreateLogger();

            IStore store = new LiteDBStore(options.StorePath, readOnly: options.Seed is null);
            IBlockPolicy<AppAgnosticAction> policy = new BlockPolicy<AppAgnosticAction>(
                null,
                blockIntervalMilliseconds: options.BlockIntervalMilliseconds,
                minimumDifficulty: options.MinimumDifficulty,
                difficultyBoundDivisor: options.DifficultyBoundDivisor);
            var blockChain = new BlockChain<AppAgnosticAction>(policy, store);
            Startup.BlockChainSingleton = blockChain;

            Swarm<AppAgnosticAction> swarm = null;
            if (options.Seed is Peer)
            {
                // TODO: Take privateKey as a CLI option
                // TODO: Take appProtocolVersion as a CLI option
                // TODO: Take host as a CLI option
                // TODO: Take listenPort as a CLI option
                if (options.IceServer is null)
                {
                    Console.Error.WriteLine(
                        "error: -s/--seed option requires -I/--ice-server as well."
                    );
                    Environment.Exit(1);
                    return;
                }

                swarm = new Swarm<AppAgnosticAction>(
                    blockChain,
                    new PrivateKey(),
                    1,
                    millisecondsDialTimeout: 1000 * 15,
                    millisecondsLinger: 1000 * 1,
                    iceServers: new[] { options.IceServer }
                );
            }

            IWebHost webHost = WebHost.CreateDefaultBuilder()
                .UseStartup<ExplorerStartup<AppAgnosticAction, Startup>>()
                .UseSerilog()
                .UseUrls($"http://{options.Host}:{options.Port}/")
                .Build();

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            Task swarmTask = Task.Run(
                async () =>
                {
                    if (swarm is null)
                    {
                        return;
                    }

                    var peers = new HashSet<Peer>();
                    if (options.Seed is Peer peer)
                    {
                        peers.Add(peer);
                    }

                    await swarm.AddPeersAsync(
                        peers,
                        cancellationToken: cts.Token
                    );

                    ImmutableHashSet<Address> trustedPeers =
                        peers.Select(p => p.Address).ToImmutableHashSet();
                    await swarm.PreloadAsync(
                        trustedStateValidators: trustedPeers,
                        cancellationToken: cts.Token
                    );

                    await swarm.StartAsync(cancellationToken: cts.Token);
                },
                cts.Token
            );

            try
            {
                Task.WaitAll(webHost.RunAsync(cts.Token), swarmTask);
            }
            catch (OperationCanceledException)
            {
                if (swarm is Swarm<AppAgnosticAction>)
                {
                    Task.WaitAll(swarm.StopAsync());
                }
            }
        }

        internal class AppAgnosticAction : IAction
        {
            public IImmutableDictionary<string, object> PlainValue
            {
                get;
                private set;
            }

            public void LoadPlainValue(
                IImmutableDictionary<string, object> plainValue)
            {
                PlainValue = plainValue;
            }

            public IAccountStateDelta Execute(IActionContext context)
            {
                return context.PreviousStates;
            }

            public void Render(
                IActionContext context,
                IAccountStateDelta nextStates)
            {
            }

            public void Unrender(
                IActionContext context,
                IAccountStateDelta nextStates)
            {
            }
        }

        internal class Startup : IBlockChainContext<AppAgnosticAction>
        {
            public BlockChain<AppAgnosticAction> BlockChain => BlockChainSingleton;

            internal static BlockChain<AppAgnosticAction> BlockChainSingleton { get; set; }
        }
    }
}
