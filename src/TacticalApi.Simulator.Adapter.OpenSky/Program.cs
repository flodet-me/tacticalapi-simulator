using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Sources.OpenSky;

AdapterHost.Run(args, (services, configuration) => services.AddOpenSkySources(configuration));
