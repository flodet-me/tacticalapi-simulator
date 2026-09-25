using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for <see cref="EasternFlankSource" />, the theater-scale demo picture: a front line
///     from the Baltic to the Black Sea that bulges one way, gets pushed back, then bulges the
///     other way, with strategic reinforcement flows feeding both sides.
///     This is a fictional wargame picture for showing a client at map scale - the geography is
///     real, the forces, formations and movements are invented.
///     Bound from "Adapter:EasternFlank" via IOptionsMonitor (hot-reloadable), so the tempo and the
///     force sizes can be changed live during a demo.
/// </summary>
public sealed class EasternFlankOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":EasternFlank";

    /// <summary>Diagnostic name, kept in step with the section this binds to.</summary>
    public static string Name => SimulationSourceName.FromSectionName(SectionName);

    /// <summary>Whether the theater picture runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Delay between cycles.</summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "01:00:00")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     One full swing of the front: eastern breakthrough, pushed back, western counter-offensive,
    ///     pushed back - then it starts over. Three minutes by default, so a viewer sees the whole
    ///     story without standing in front of the screen: roughly 45 seconds per phase.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:30", "24:00:00")]
    public TimeSpan CycleDuration { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How deep a breakthrough pushes the front line at its peak.</summary>
    [Range(10, 600)]
    public double MaxBulgeKm { get; set; } = 160;

    /// <summary>
    ///     Formations shown along the front line per side. Deliberately sparse: the line is meant to
    ///     read as a front, not as a wall of icons, and the rest of the picture - fleets, strategic
    ///     movements, installations - needs room next to it.
    /// </summary>
    [Range(0, 200)]
    public int FrontUnitsPerSide { get; set; } = 8;

    /// <summary>Transports on the transatlantic reinforcement route.</summary>
    [Range(0, 100)]
    public int UsReinforcementCount { get; set; } = 6;

    /// <summary>Transports on the eastern reserve route.</summary>
    [Range(0, 100)]
    public int EasternReserveCount { get; set; } = 5;

    /// <summary>Transports on the southern supply line, from Iran across the Caspian.</summary>
    [Range(0, 100)]
    public int SouthernSupplyCount { get; set; } = 5;

    /// <summary>Air patrols per side along the front.</summary>
    [Range(0, 50)]
    public int AirPatrolsPerSide { get; set; } = 3;

    /// <summary>
    ///     Upper bound on the vessels shown per naval station. Most stations only hold one ship
    ///     anyway - the naval picture is deliberately spread across the Atlantic, the Pacific, the
    ///     South Atlantic, the Indian Ocean and the Arabian Sea rather than clustered into groups -
    ///     so this only bites on the three two-hull groups, and setting it to 1 puts every vessel on
    ///     its own.
    /// </summary>
    [Range(0, 2)]
    public int MaxVesselsPerNavalGroup { get; set; } = 2;

    /// <summary>
    ///     Special forces teams per side, working objectives deep in the other side's hinterland.
    ///     Capped by the number of infiltration routes the geography defines.
    /// </summary>
    [Range(0, 4)]
    public int SpecialForcesTeamsPerSide { get; set; } = 3;

    /// <summary>
    ///     Reconnaissance satellites. Their ground tracks sweep the globe continuously, which is what
    ///     puts the theater into a global context on the map.
    /// </summary>
    public bool ShowSatellites { get; set; } = true;

    /// <summary>One satellite orbit in this time - short enough that a viewer sees a pass during a demo.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "02:00:00")]
    public TimeSpan SatelliteOrbitDuration { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Unidentified tracks drifting across the line, drawn with the unknown affiliation.</summary>
    [Range(0, 4)]
    public int UnknownContactCount { get; set; } = 4;

    /// <summary>Civilian shipping and relief movements, drawn with the neutral affiliation.</summary>
    [Range(0, 4)]
    public int NeutralTrafficCount { get; set; } = 4;

    /// <summary>Whether the fixed installations (bases, depots, plants, ports) are shown.</summary>
    public bool ShowInstallations { get; set; } = true;

    /// <summary>
    ///     Incident reports with imagery attached - sabotage, unmanned reconnaissance, an abduction.
    ///     They arrive one at a time and expire, so the picture keeps producing new information.
    /// </summary>
    public bool ShowIncidents { get; set; } = true;

    /// <summary>
    ///     Whether formations are lost in combat. A loss is reported once with an expiry in the past,
    ///     so the object is deleted from the situation, and a replacement appears in the next window
    ///     - which is what makes attrition visible rather than implied.
    /// </summary>
    public bool ShowLosses { get; set; } = true;

    /// <summary>
    ///     Expiry stamped on every object this source reports - the moving symbols and the static
    ///     frame alike - and pushed forward again on every cycle. Comfortably longer than a cycle,
    ///     so a brief client outage doesn't empty the map mid-demo, while a client that ages objects
    ///     out by their expiry (a TAK client's stale time) never drops the picture as long as the
    ///     adapter is running. Objects that manage their own lifetime - a destroyed formation, a
    ///     loss report, an incident picture - keep theirs.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan TrackTimeToLive { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Reporter identity attached to every object this source emits.</summary>
    [Required]
    public string ReporterId { get; set; } = "SIM-FLANK";
}
