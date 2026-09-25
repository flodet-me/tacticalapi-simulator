namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>One node of the baseline front line, with the sector it belongs to.</summary>
internal sealed record FrontNode(double Lat, double Lon, string Sector);

/// <summary>A leg of a strategic movement route, named so it can be shown as a waypoint.</summary>
internal sealed record RouteNode(double Lat, double Lon, string Name, string Comment);

/// <summary>Which side an object belongs to, and therefore how it is drawn.</summary>
internal enum Allegiance
{
    /// <summary>Western / NATO side, drawn as friendly.</summary>
    Western,

    /// <summary>Eastern side, drawn as hostile.</summary>
    Eastern,

    /// <summary>Civilian or non-aligned, drawn as neutral.</summary>
    Neutral
}

/// <summary>A fixed installation - a base, a depot, a factory, a port.</summary>
internal sealed record Installation(double Lat, double Lon, string Name, string Kind, Allegiance Side, string Function);

/// <summary>A satellite's orbit, as the few numbers a ground track needs.</summary>
internal sealed record SatelliteOrbit(string Name, double InclinationDeg, double StartLongitude, double PhaseDeg,
    Allegiance Side, string Task);

/// <summary>
///     One push against the front: which node it is centered on, when in the cycle it runs, how deep
///     it goes relative to the configured maximum, and who is doing the pushing. Several of these
///     overlapping is what keeps the line from moving as one smooth arc.
/// </summary>
internal sealed record BulgeProfile(int CenterIndex, double StartPhase, double EndPhase, double Depth,
    double Spread, Allegiance Side, string Sector);

/// <summary>
///     One vessel on a naval station: the battle dimension it is drawn in ('S' surface, 'U' subsurface),
///     its MIL-STD-2525C function identifier, what it is, and the name it is reported under.
/// </summary>
internal sealed record NavalVessel(char Dimension, string Function, string Role, string Name);

/// <summary>
///     A naval station: a patch of ocean one or two vessels work, far from the land fighting. Most
///     stations hold a single ship - a warship on station is normally alone on the plot, and a map
///     full of four-icon clusters reads as a fleet review rather than as a sea picture.
/// </summary>
internal sealed record NavalStation(string Name, double Lat, double Lon, double RadiusKm, Allegiance Side,
    string Waters, NavalVessel[] Vessels);

/// <summary>An incident worth a picture: what happened, where, and who reported it.</summary>
internal sealed record Incident(string Id, string Name, string Kind, double Lat, double Lon, string Description,
    Allegiance Side);

/// <summary>
///     The fixed geography of the eastern flank picture: the baseline front line from the Gulf of
///     Finland to the Black Sea, the strategic routes feeding it, the naval stations out in the
///     oceans, and the installations spread across the whole theater and well beyond it.
///     Coordinates are real places, deliberately coarse - this is a schematic for a demo, not a
///     survey. Everything that moves is computed from these points in <see cref="EasternFlankSource" />.
/// </summary>
internal static class EasternFlankGeometry
{
    /// <summary>
    ///     The baseline front, north to south: the Estonian border at Narva, down the Baltic
    ///     states, across the Suwałki gap, along the Polish border and through Ukraine to the Black
    ///     Sea coast. Index order matters - bulges are centered on an index.
    /// </summary>
    public static readonly FrontNode[] Front =
    [
        new(59.38, 28.19, "Estonia"),
        new(58.35, 27.50, "Estonia"),
        new(57.15, 27.85, "Latvia"),
        new(56.10, 28.05, "Latvia"),
        new(55.20, 26.60, "Lithuania"),
        new(54.35, 23.90, "Suwalki gap"),
        new(53.30, 23.80, "Poland north"),
        new(52.30, 23.60, "Poland east"),
        new(51.30, 23.90, "Poland south"),
        new(50.40, 25.60, "Ukraine north"),
        new(49.20, 27.80, "Ukraine center"),
        new(47.90, 29.80, "Ukraine south"),
        new(46.45, 30.75, "Black Sea coast")
    ];

    /// <summary>Front index the main eastern breakthrough is centered on - the Polish border sector.</summary>
    public const int EasternBreakthroughIndex = 7;

    /// <summary>Front index the main western counter-offensive is centered on - the Latvian border sector.</summary>
    public const int WesternCounterIndex = 3;

    /// <summary>
    ///     The pushes that shape the line over one cycle. The two main efforts run for half a cycle
    ///     each; the supporting attacks are shallower, start later and end earlier, so sectors move
    ///     at different times and the trace keeps changing shape rather than just growing and
    ///     shrinking.
    /// </summary>
    public static readonly BulgeProfile[] Bulges =
    [
        new(EasternBreakthroughIndex, 0.00, 0.50, 1.00, 1.9, Allegiance.Eastern, "Poland east"),
        new(11, 0.08, 0.38, 0.55, 1.3, Allegiance.Eastern, "Ukraine south"),
        new(4, 0.18, 0.46, 0.35, 1.1, Allegiance.Eastern, "Lithuania"),
        new(WesternCounterIndex, 0.50, 1.00, 1.00, 1.9, Allegiance.Western, "Latvia"),
        new(9, 0.58, 0.88, 0.60, 1.3, Allegiance.Western, "Ukraine north"),
        new(6, 0.66, 0.96, 0.35, 1.1, Allegiance.Western, "Poland north")
    ];

    /// <summary>
    ///     Transatlantic reinforcement: US east coast to a European port, then overland to the
    ///     staging area behind the front.
    /// </summary>
    public static readonly RouteNode[] TransatlanticRoute =
    [
        new(36.85, -76.30, "Norfolk", "Sea port of embarkation"),
        new(42.00, -60.00, "Atlantic", "Convoy en route"),
        new(48.50, -25.00, "Mid-Atlantic", "Escorted transit"),
        new(50.50, -5.00, "Western approaches", "Entering European waters"),
        new(53.55, 8.58, "Bremerhaven", "Sea port of debarkation"),
        new(52.40, 16.90, "Poznan", "Rail head, onward movement"),
        new(52.30, 21.00, "Warsaw", "Staging area behind the front")
    ];

    /// <summary>Eastern reserve movement: far-eastern rail head across Siberia to the western theater.</summary>
    public static readonly RouteNode[] EasternReserveRoute =
    [
        new(39.90, 116.40, "Beijing", "Rail head, reserve formations"),
        new(50.30, 106.70, "Ulan-Ude", "Trans-Siberian junction"),
        new(55.03, 82.92, "Novosibirsk", "Rail transit"),
        new(56.83, 60.60, "Yekaterinburg", "Rail transit"),
        new(55.75, 37.62, "Moscow", "Central staging"),
        new(53.90, 27.57, "Minsk", "Forward staging area")
    ];

    /// <summary>
    ///     Southern supply line: overland from Iran to the Caspian, across it by ship, then up the
    ///     Volga corridor and west to the eastern side's forward staging area.
    /// </summary>
    public static readonly RouteNode[] SouthernSupplyRoute =
    [
        new(35.69, 51.39, "Tehran", "Production and marshalling"),
        new(37.47, 49.46, "Bandar Anzali", "Caspian port of embarkation"),
        new(41.50, 49.90, "Caspian Sea", "Sea crossing"),
        new(46.35, 48.04, "Astrakhan", "Port of debarkation"),
        new(48.71, 44.51, "Volgograd", "Rail transfer"),
        new(51.66, 39.20, "Voronezh", "Forward depot"),
        new(52.10, 30.65, "Gomel", "Forward staging area")
    ];

    /// <summary>Legs of <see cref="SouthernSupplyRoute" /> before this index are the Caspian crossing.</summary>
    public const int SouthernSupplyLastSeaLeg = 3;

    /// <summary>Legs of <see cref="TransatlanticRoute" /> before this index are at sea.</summary>
    public const int TransatlanticLastSeaLeg = 4;

    /// <summary>Air defense umbrellas on the eastern side, as radius in kilometers around a point.</summary>
    public static readonly (double Lat, double Lon, double RadiusKm, string Name)[] EasternAirDefense =
    [
        (54.71, 20.51, 250, "A2/AD umbrella KALININGRAD"),
        (44.62, 33.53, 300, "A2/AD umbrella CRIMEA"),
        (55.75, 37.62, 200, "Air defense zone MOSCOW")
    ];

    /// <summary>Air defense umbrellas on the western side.</summary>
    public static readonly (double Lat, double Lon, double RadiusKm, string Name)[] WesternAirDefense =
    [
        (54.35, 18.65, 180, "Integrated air defense GDANSK"),
        (52.23, 21.01, 180, "Integrated air defense WARSAW"),
        (44.43, 26.10, 200, "Integrated air defense BUCHAREST")
    ];

    /// <summary>Rear staging areas behind the western front, where reinforcements assemble.</summary>
    public static readonly (double Lat, double Lon, double RadiusKm, string Name)[] WesternStagingAreas =
    [
        (52.30, 21.00, 60, "Staging area WARSAW"),
        (50.05, 19.95, 60, "Staging area KRAKOW"),
        (56.95, 24.11, 50, "Staging area RIGA")
    ];

    /// <summary>Rear staging areas behind the eastern front.</summary>
    public static readonly (double Lat, double Lon, double RadiusKm, string Name)[] EasternStagingAreas =
    [
        (53.90, 27.57, 60, "Staging area MINSK"),
        (52.10, 30.65, 55, "Staging area GOMEL"),
        (57.81, 28.33, 50, "Staging area PSKOV")
    ];

    /// <summary>
    ///     Where the fleets are: spread right across the world's oceans rather than clustered on the
    ///     flank. Two carrier groups and one other pair are the only places two hulls are on the same
    ///     station; everything else - a frigate in the South Atlantic, a destroyer in the Indian
    ///     Ocean, a submarine nobody has a good fix on - is a single vessel a long way from the
    ///     fighting, which is what a global sea picture actually looks like.
    ///     Coordinates are open water, checked against the coastlines around them.
    /// </summary>
    public static readonly NavalStation[] NavalStations =
    [
        // Western, the two-hull groups first.
        new("CARRIER STRIKE GROUP 12", 47.50, -32.00, 260, Allegiance.Western, "North Atlantic",
        [
            new('S', "CLCV", "Aircraft carrier", "CVN AURORA"),
            new('S', "CLDD", "Destroyer", "DDG HALBERD")
        ]),
        new("TASK UNIT MEDITERRANEAN", 35.60, 17.50, 180, Allegiance.Western, "Central Mediterranean",
            [new('S', "CLDD", "Destroyer", "DDG SENTINEL")]),
        new("TASK UNIT NORWEGIAN SEA", 67.50, 3.50, 200, Allegiance.Western, "Norwegian Sea",
            [new('S', "CLFF", "Frigate", "FFG NORDKAPP")]),
        new("TASK UNIT BALTIC", 56.50, 19.00, 120, Allegiance.Western, "Baltic Sea",
            [new('S', "CMMA", "Mine countermeasures vessel", "MCM AMBER")]),
        new("TASK UNIT SOUTH ATLANTIC", -28.00, -18.00, 300, Allegiance.Western, "South Atlantic",
            [new('S', "CLFF", "Frigate", "FFG SOUTHERN CROSS")]),
        new("TASK UNIT INDIAN OCEAN", -12.00, 74.00, 300, Allegiance.Western, "Central Indian Ocean",
            [new('S', "CLDD", "Destroyer", "DDG MONSOON")]),
        new("TASK UNIT CENTRAL PACIFIC", 18.00, -158.00, 300, Allegiance.Western, "Central Pacific",
            [new('S', "CLFF", "Frigate", "FFG TRADEWIND")]),
        new("TASK UNIT ARABIAN SEA", 14.00, 63.00, 200, Allegiance.Western, "Arabian Sea",
            [new('S', "CLDD", "Destroyer", "DDG KHAMSIN")]),
        new("SUBMARINE PATROL ATLANTIC", 42.00, -45.00, 260, Allegiance.Western, "Mid-Atlantic, deep water",
            [new('U', "SF", "Attack submarine", "SSN BARRACUDA")]),

        // Eastern.
        new("NORTHERN FLEET GROUP", 72.50, 36.00, 240, Allegiance.Eastern, "Barents Sea",
        [
            new('S', "CLCV", "Aircraft carrier", "CV POLYARNY"),
            new('S', "CLDD", "Destroyer", "DD SEVER")
        ]),
        new("PACIFIC FLEET GROUP", 42.00, 150.00, 260, Allegiance.Eastern, "North Pacific",
        [
            new('S', "CLCV", "Aircraft carrier", "CV VOSTOK"),
            new('S', "CLDD", "Destroyer", "DD AMUR")
        ]),
        new("BLACK SEA SQUADRON", 44.20, 31.50, 120, Allegiance.Eastern, "Black Sea",
            [new('S', "CLFF", "Frigate", "FF AZOV")]),
        new("MEDITERRANEAN SQUADRON", 34.20, 27.50, 180, Allegiance.Eastern, "Eastern Mediterranean",
            [new('S', "CLDD", "Destroyer", "DD LEVANT")]),
        new("SOUTH PACIFIC PATROL", -20.00, -150.00, 300, Allegiance.Eastern, "South Pacific",
            [new('S', "CLFF", "Frigate", "FF TAIFUN")]),
        new("INDIAN OCEAN PATROL", -6.00, 84.00, 280, Allegiance.Eastern, "Eastern Indian Ocean",
            [new('S', "CLDD", "Destroyer", "DD MUSSON")]),
        new("GULF OF ADEN DETACHMENT", 13.00, 50.00, 160, Allegiance.Eastern, "Gulf of Aden",
            [new('S', "CLFF", "Frigate", "FF ADEN")]),
        new("SUBMARINE PATROL SOUTH ATLANTIC", -22.00, -8.00, 280, Allegiance.Eastern,
            "South Atlantic, deep water",
            [new('U', "SF", "Attack submarine", "SSN KASATKA")])
    ];

    /// <summary>Formation names used along the front, west side, cycled per sector.</summary>
    public static readonly string[] WesternFormations =
    [
        "EE 1 Inf Bde", "US 3 ABCT", "LV Mech Bde", "DE 41 PzGren Bde", "LT Iron Wolf Bde",
        "PL 15 Mech Bde", "UK 4 Inf Bde", "PL 18 Mech Div", "FR 7 Armd Bde", "PL 21 Rifle Bde",
        "UA 92 Mech Bde", "UA 28 Mech Bde", "RO 282 Mech Bde", "US 2 CR"
    ];

    /// <summary>Formation names used along the front, east side, cycled per sector.</summary>
    public static readonly string[] EasternFormations =
    [
        "RU 6 CAA", "RU 76 Air Aslt", "RU 1 GTA", "RU 20 CAA", "BY 11 Mech Bde",
        "RU 11 Army Corps", "BY 120 Mech Bde", "RU 8 CAA", "RU 41 CAA", "RU 58 CAA",
        "RU 35 CAA", "RU 49 CAA", "RU 22 Army Corps", "RU 2 GMRD"
    ];

    /// <summary>
    ///     Special forces infiltration routes: each runs from a rear area across the line and deep
    ///     into the other side's hinterland, where the team works an objective and comes back.
    ///     Deep means several hundred kilometres past the line, onto the bases, ports and production
    ///     the far side of the theater runs on, not just over it. That depth is the point of having
    ///     them on the map at all: a team a hundred kilometres out reads as reconnaissance, one
    ///     sitting on the port of debarkation or the strategic air base reads as special operations.
    ///     Ground teams, maritime teams and aviation elements all carry the plain special operations
    ///     battle dimension rather than an ordinary infantry symbol; the branch names the team in its
    ///     description instead of narrowing the symbol, for the reason given on
    ///     <see cref="EasternFlankSource" />'s special forces.
    /// </summary>
    public static readonly (string Name, Allegiance Side, string Branch, string Objective,
        (double Lat, double Lon)[] Path)[] SpecialForcesRoutes =
    [
        // Westward teams work the eastern rear: the air defense belt at Voronezh, the rail junction
        // at Tula, the Northern Fleet in the Kola inlet, the production complex on the Volga.
        ("TF ODIN", Allegiance.Western, "G", "Reconnaissance of the Voronezh air base air defenses",
            [(50.30, 25.20), (50.70, 29.40), (51.10, 33.60), (51.45, 36.80), (51.66, 39.15)]),
        ("TF RAVEN", Allegiance.Western, "A", "Aviation insert, rail junction at Tula",
            [(52.30, 22.80), (52.80, 27.00), (53.30, 31.00), (53.80, 34.60), (54.20, 37.55)]),
        ("TF TRIDENT", Allegiance.Western, "N", "Maritime reconnaissance, Severomorsk anchorage",
            [(65.00, 6.00), (68.00, 13.00), (70.20, 20.00), (70.60, 28.00), (69.80, 32.60), (69.10, 33.42)]),
        ("TF LYNX", Allegiance.Western, "G", "Target acquisition, Nizhny Novgorod production complex",
            [(56.10, 27.40), (56.40, 32.60), (56.70, 37.60), (56.55, 41.20), (56.31, 43.95)]),

        // Eastward teams work the western rear, which is where the reinforcement flow lands and
        // stages: the port of debarkation, the theater air hub, Rotterdam, the air base in England.
        ("SPN 1", Allegiance.Eastern, "G", "Reconnaissance of the Bremerhaven port of debarkation",
            [(53.30, 24.40), (53.00, 19.40), (52.80, 14.60), (53.20, 10.80), (53.53, 8.62)]),
        ("SPN 2", Allegiance.Eastern, "A", "Aviation insert near the Ramstein theater air hub",
            [(51.20, 24.60), (50.60, 19.80), (50.10, 15.20), (49.70, 11.20), (49.45, 7.65)]),
        ("SPN 3", Allegiance.Eastern, "N", "Maritime reconnaissance, approaches to Rotterdam",
            [(55.60, 20.60), (55.30, 15.60), (55.90, 11.40), (57.70, 8.60), (55.50, 5.00), (52.15, 4.05)]),
        ("SPN 4", Allegiance.Eastern, "G", "Sabotage team, Lakenheath air base perimeter",
            [(54.70, 19.60), (54.40, 14.20), (53.90, 9.00), (53.10, 4.20), (52.43, 0.60)])
    ];

    /// <summary>
    ///     Reconnaissance satellites: three western, two eastern, each on its own inclined orbit with
    ///     its own phase so the ground tracks sweep different parts of the globe instead of trailing
    ///     each other. Both sides watch the other from orbit, which is why each has more than one.
    /// </summary>
    public static readonly SatelliteOrbit[] Satellites =
    [
        new("RECON SAT ALPHA", 63.4, -40.0, 0, Allegiance.Western, "Imagery pass, eastern theater"),
        new("RECON SAT BRAVO", 51.6, 120.0, 140, Allegiance.Western, "Signals collection, wide area"),
        new("RECON SAT CHARLIE", 97.8, -95.0, 60, Allegiance.Western,
            "Sun-synchronous imagery pass, polar coverage"),
        new("KOSMOS 2599", 82.5, 30.0, 220, Allegiance.Eastern, "Imagery pass, western theater"),
        new("KOSMOS 2617", 67.1, 155.0, 300, Allegiance.Eastern,
            "Maritime surveillance, ocean reconnaissance")
    ];

    /// <summary>
    ///     Installations, deliberately spread from the American east coast to the Pacific rather
    ///     than clustered on the flank. The fighting is the focus of the picture; the bases,
    ///     depots, ports and plants that sustain it are not all within sight of it.
    ///     The function code decides the icon: "IB" a military base, "IU" industry, "IT" a civilian
    ///     transport node.
    /// </summary>
    public static readonly Installation[] Installations =
    [
        // Western military, front and rear.
        new(59.26, 24.21, "Amari Air Base", "Air base", Allegiance.Western, "IB"),
        new(52.38, 17.85, "Powidz Air Base", "Air base, forward logistics", Allegiance.Western, "IB"),
        new(50.11, 22.04, "Rzeszow Logistics Hub", "Theater logistics hub", Allegiance.Western, "IB"),
        new(54.52, 18.54, "Gdynia Naval Base", "Naval base", Allegiance.Western, "IB"),
        new(46.47, 30.73, "Odesa Port Facility", "Sea port of debarkation", Allegiance.Western, "IB"),
        new(49.44, 7.60, "Ramstein Air Base", "Theater air hub", Allegiance.Western, "IB"),
        new(52.41, 0.56, "Lakenheath Air Base", "Air base", Allegiance.Western, "IB"),
        new(36.62, -6.35, "Rota Naval Station", "Naval station", Allegiance.Western, "IB"),
        new(63.99, -22.61, "Keflavik Air Station", "Maritime patrol base", Allegiance.Western, "IB"),
        new(36.95, -76.33, "Norfolk Naval Station", "Fleet base, port of embarkation", Allegiance.Western, "IB"),
        new(35.14, -79.01, "Fort Bragg", "Rapid deployment garrison", Allegiance.Western, "IB"),
        new(37.00, 35.42, "Incirlik Air Base", "Air base", Allegiance.Western, "IB"),

        // Eastern military, front and deep rear.
        new(54.65, 19.91, "Baltiysk Naval Base", "Naval base", Allegiance.Eastern, "IB"),
        new(44.62, 33.53, "Sevastopol Naval Base", "Fleet base", Allegiance.Eastern, "IB"),
        new(51.66, 39.20, "Voronezh Air Base", "Air base", Allegiance.Eastern, "IB"),
        new(53.90, 27.55, "Minsk Depot", "Ammunition depot", Allegiance.Eastern, "IB"),
        new(69.07, 33.42, "Severomorsk Naval Base", "Northern fleet base", Allegiance.Eastern, "IB"),
        new(51.48, 46.21, "Engels Air Base", "Strategic aviation base", Allegiance.Eastern, "IB"),
        new(43.11, 131.89, "Vladivostok Naval Base", "Pacific fleet base", Allegiance.Eastern, "IB"),
        new(34.90, 35.88, "Tartus Naval Facility", "Mediterranean support point", Allegiance.Eastern, "IB"),
        new(35.72, 51.35, "Tehran Production Complex", "Materiel production", Allegiance.Eastern, "IU"),
        new(37.47, 49.46, "Bandar Anzali Port", "Caspian port", Allegiance.Eastern, "IB"),

        // Civilian infrastructure, drawn neutral, well spread out.
        new(50.05, 19.95, "Krakow Steelworks", "Heavy industry", Allegiance.Neutral, "IU"),
        new(51.10, 17.03, "Wroclaw Vehicle Plant", "Vehicle production", Allegiance.Neutral, "IU"),
        new(56.51, 21.01, "Liepaja Power Plant", "Power generation", Allegiance.Neutral, "IU"),
        new(56.31, 44.00, "Nizhny Novgorod Plant", "Heavy industry", Allegiance.Neutral, "IU"),
        new(44.94, 26.03, "Ploiesti Refinery", "Refinery", Allegiance.Neutral, "IU"),
        new(52.23, 21.01, "Warsaw Rail Hub", "Civilian rail junction", Allegiance.Neutral, "IT"),
        new(53.55, 8.58, "Bremerhaven Port", "Civilian sea port", Allegiance.Neutral, "IT"),
        new(53.55, 9.99, "Hamburg Port", "Civilian sea port", Allegiance.Neutral, "IT"),
        new(51.92, 4.48, "Rotterdam Port", "Civilian sea port", Allegiance.Neutral, "IT"),
        new(41.02, 29.00, "Bosphorus Transit", "Strait traffic control", Allegiance.Neutral, "IT"),
        new(30.00, 32.55, "Suez Canal Transit", "Canal traffic control", Allegiance.Neutral, "IT")
    ];

    /// <summary>Civilian shipping and relief traffic, drawn neutral, on lanes away from the fighting.</summary>
    public static readonly (string Name, string Kind, bool Sea, (double Lat, double Lon)[] Lane)[] NeutralTraffic =
    [
        ("MV NORDLAND", "Civilian cargo vessel", true, [(55.20, 14.20), (55.90, 17.50), (56.60, 20.80)]),
        ("MV BLACK PEARL", "Civilian tanker", true, [(41.20, 29.10), (43.00, 30.60), (44.40, 32.40)]),
        ("RELIEF CONVOY 1", "Humanitarian convoy", false, [(50.45, 30.52), (50.00, 27.80), (49.84, 24.03)]),
        ("RELIEF CONVOY 2", "Humanitarian convoy", false, [(47.84, 35.14), (47.20, 33.00), (46.65, 32.62)])
    ];

    /// <summary>
    ///     Unidentified tracks: contacts a sensor has produced but nobody has classified yet, which
    ///     is what the "unknown" affiliation is for. They drift across the line, so they show up on
    ///     both sides of it.
    /// </summary>
    public static readonly (string Name, bool Air, (double Lat, double Lon)[] Lane)[] UnknownContacts =
    [
        ("UNK AIR 01", true, [(56.80, 24.20), (56.40, 27.60), (56.10, 30.90)]),
        ("UNK AIR 02", true, [(50.20, 22.10), (50.60, 25.90), (50.90, 29.40)]),
        ("UNK TRACK 03", false, [(53.60, 23.10), (53.40, 24.60), (53.20, 26.10)]),
        ("UNK TRACK 04", false, [(47.60, 30.50), (47.90, 32.20), (48.20, 33.80)])
    ];

    /// <summary>
    ///     Incidents that come with imagery. Each is shown for a while as a picture document and
    ///     then expires, so a viewer sees reports arriving rather than a static wall of them.
    /// </summary>
    public static readonly Incident[] Incidents =
    [
        new("sabotage-brest", "Sabotage, rail bridge at Brest", "Sabotage", 52.10, 23.70,
            "Rail bridge dropped overnight, engineer reconnaissance photo attached", Allegiance.Western),
        new("uav-gomel", "UAV imagery, depot at Gomel", "UAV imagery", 52.42, 30.98,
            "Unmanned reconnaissance pass over the forward depot, fuel storage visible", Allegiance.Western),
        new("abduction-narva", "Abduction, Narva sector", "Abduction", 59.30, 28.05,
            "Local official abducted at a checkpoint, imagery from the traffic camera", Allegiance.Western),
        new("sabotage-poznan", "Sabotage, rail head at Poznan", "Sabotage", 52.40, 16.90,
            "Charges found on the rail head approach, disposal team on site", Allegiance.Eastern),
        new("uav-kaliningrad", "UAV imagery, air defense site", "UAV imagery", 54.71, 20.51,
            "Unmanned pass over the air defense site, launchers in the open", Allegiance.Eastern),
        new("abduction-lviv", "Abduction, western Ukraine", "Abduction", 49.84, 24.03,
            "Convoy driver taken at a halt, photo from the escort vehicle", Allegiance.Eastern)
    ];

    /// <summary>Point at <paramref name="fraction" /> of the way between two points.</summary>
    public static (double Lat, double Lon) Between((double Lat, double Lon) from, (double Lat, double Lon) to,
        double fraction)
    {
        return (from.Lat + (to.Lat - from.Lat) * fraction, from.Lon + (to.Lon - from.Lon) * fraction);
    }

    /// <summary>
    ///     Bearing pointing west of the front line at <paramref name="index" /> - the direction an
    ///     eastern breakthrough pushes, and the opposite of where a western counter-offensive goes.
    /// </summary>
    public static double WestwardBearing(int index)
    {
        var previous = Front[Math.Max(0, index - 1)];
        var next = Front[Math.Min(Front.Length - 1, index + 1)];

        // The nodes run north to south, so "along the line" points south; rotating it by +90
        // turns it to the west, which is the direction an eastern breakthrough pushes.
        var along = GeoMath.Bearing((previous.Lat, previous.Lon), (next.Lat, next.Lon));
        return (along + 90) % 360;
    }
}
