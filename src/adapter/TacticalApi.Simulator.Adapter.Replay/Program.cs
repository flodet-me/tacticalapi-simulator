using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Sources.Replay;

AdapterHost.Run(args, (services, configuration) => services.AddReplaySources(configuration));
