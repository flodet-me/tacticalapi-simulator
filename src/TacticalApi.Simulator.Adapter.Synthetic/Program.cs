using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Sources.Synthetic;

AdapterHost.Run(args, (services, configuration) => services.AddSyntheticSources(configuration));
