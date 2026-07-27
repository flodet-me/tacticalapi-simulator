using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Sources.Nws;

AdapterHost.Run(args, (services, configuration) => services.AddNwsSources(configuration));
