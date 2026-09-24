using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Identities;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests for the map GUI's data endpoint (<c>/api/objects</c>), driven exactly as
///     the frontend drives it. Focused on the symbol identifier, since that is what decides
///     whether an object renders as its tactical symbol or as a plain marker: the frontend hands
///     whatever <c>sidc</c> it gets straight to milsymbol, which takes a 15-character letter code
///     (2525B/C, APP-6B) or a 20-digit numeric one (2525D/E, APP-6D/E) - so the numeric form's two
///     sets of ten digits have to arrive already joined.
/// </summary>
public sealed class MapApiE2ETests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow;

    [Fact]
    public async Task StringIdentifier_IsSurfacedWithItsCatalog()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var id = Unique("string-sidc");

        await AddAsync(client, id, new SymbolIdentifier
        {
            StringIdentifier = "SFGPUCI----D---",
            SymbolCatalog = SymbolCatalog.Mil2525C
        });

        // Act
        var symbolIdentifier = await GetSymbolIdentifierAsync(factory, id);

        // Assert
        Assert.Equal("SFGPUCI----D---", symbolIdentifier.GetProperty("sidc").GetString());
        Assert.Equal(nameof(SymbolCatalog.Mil2525C), symbolIdentifier.GetProperty("catalog").GetString());
    }

    [Fact]
    public async Task NumericIdentifier_IsSurfacedAsTheJoinedTwentyDigitCode()
    {
        // Arrange - the 2525D land unit "infantry": set A 1003100014, set B 1211000000.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var id = Unique("numeric-sidc");

        await AddAsync(client, id, new SymbolIdentifier
        {
            NumericIdentifier = new NumericIdentifier { FirstTenDigits = 1003100014, SecondTenDigits = 1211000000 },
            SymbolCatalog = SymbolCatalog.Mil2525D
        });

        // Act
        var symbolIdentifier = await GetSymbolIdentifierAsync(factory, id);

        // Assert
        Assert.Equal("10031000141211000000", symbolIdentifier.GetProperty("sidc").GetString());
        Assert.Equal(nameof(SymbolCatalog.Mil2525D), symbolIdentifier.GetProperty("catalog").GetString());
    }

    [Fact]
    public async Task NumericIdentifier_KeepsTheLeadingZeroesOfEitherSet()
    {
        // Arrange - a set starting with zeroes arrives as a shorter number over the wire
        // (int64), and losing those zeroes would shift every field of the code.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var id = Unique("numeric-sidc-zeroes");

        await AddAsync(client, id, new SymbolIdentifier
        {
            NumericIdentifier = new NumericIdentifier { FirstTenDigits = 31000014, SecondTenDigits = 1000000 },
            SymbolCatalog = SymbolCatalog.App6E
        });

        // Act
        var symbolIdentifier = await GetSymbolIdentifierAsync(factory, id);

        // Assert
        Assert.Equal("00310000140001000000", symbolIdentifier.GetProperty("sidc").GetString());
    }

    [Theory]
    [InlineData(10_000_000_000L, 1211000000L)] // set A is eleven digits
    [InlineData(1003100014L, -1L)] // set B is negative
    public async Task NumericIdentifier_ThatCannotBeTenDigits_IsLeftToThePlainMarkerFallback(
        long firstTenDigits, long secondTenDigits)
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var id = Unique("numeric-sidc-invalid");

        await AddAsync(client, id, new SymbolIdentifier
        {
            NumericIdentifier = new NumericIdentifier
            {
                FirstTenDigits = firstTenDigits,
                SecondTenDigits = secondTenDigits
            },
            SymbolCatalog = SymbolCatalog.Mil2525D
        });

        // Act
        var mapObject = await GetObjectAsync(factory, id);

        // Assert - no symbol identifier at all, rather than a code that would draw a wrong symbol.
        Assert.Equal(JsonValueKind.Null, mapObject.GetProperty("symbolIdentifier").ValueKind);
    }

    [Fact]
    public async Task ObjectWithoutASymbolIdentifier_HasNone()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var id = Unique("no-sidc");

        await AddAsync(client, id, symbolIdentifier: null);

        // Act
        var mapObject = await GetObjectAsync(factory, id);

        // Assert
        Assert.Equal(JsonValueKind.Null, mapObject.GetProperty("symbolIdentifier").ValueKind);
    }

    [Fact]
    public async Task Endpoint_IsNotServedWhenTheMapUiIsDisabled()
    {
        // Arrange
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:MapUi:Enabled"] = "false"
        });
        var http = factory.CreateClient();

        // Act
        var response = await http.GetAsync(new Uri("/api/objects", UriKind.Relative));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task AddAsync(
        Situation.SituationClient client, string id, SymbolIdentifier? symbolIdentifier)
    {
        var response = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects =
            {
                E2E.Symbol(id, T0, "ALPHA", 53.0, 8.8, symbolIdentifier: symbolIdentifier)
            }
        });
        Assert.True(response.Header.Success, response.Header.ErrorMessage);
    }

    private static async Task<JsonElement> GetSymbolIdentifierAsync(SimulatorFactory factory, string id)
    {
        var mapObject = await GetObjectAsync(factory, id);
        var symbolIdentifier = mapObject.GetProperty("symbolIdentifier");
        Assert.Equal(JsonValueKind.Object, symbolIdentifier.ValueKind);
        return symbolIdentifier;
    }

    private static async Task<JsonElement> GetObjectAsync(SimulatorFactory factory, string id)
    {
        var key = IdentityKey.TryCreate(new Identity { StringIdentity = id });
        var objects = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/objects");
        return Assert.Single(objects.EnumerateArray(), o => o.GetProperty("id").GetString() == key);
    }

    private static string Unique(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}
