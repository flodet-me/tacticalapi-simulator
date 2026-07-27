using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Logging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="FileLoggingExtensions" />
///     (src/simulator/TacticalApi.Simulator.Core/Logging/FileLoggingExtensions.cs).
/// </summary>
public sealed class FileLoggingExtensionsTests
{
    [Fact]
    public void AddFileLogging_Disabled_WritesNoFile()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("filelog-disabled-");
        try
        {
            var config = InMemoryConfig(false, Path.Combine(dir.FullName, "test-.jsonl"));

            // Act
            using (var loggerFactory = LoggerFactory.Create(b => b.AddFileLogging(config)))
            {
                loggerFactory.CreateLogger("Test").LogInformation("Should not be written");
            }

            // Assert
            Assert.Empty(dir.GetFiles());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void AddFileLogging_Enabled_WritesOneStructuredJsonLinePerLogCall()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("filelog-enabled-");
        try
        {
            var config = InMemoryConfig(true, Path.Combine(dir.FullName, "test-.jsonl"));

            // Act
            using (var loggerFactory = LoggerFactory.Create(b => b.AddFileLogging(config)))
            {
                var logger = loggerFactory.CreateLogger("Test");
                logger.LogInformation("Hello {Name}, count {Count}", "World", 3);
                logger.LogWarning("Second line");
            }

            // Assert: exactly one rolled file, one JSON object per line, properties preserved distinctly
            // (not flattened into the message text) - that's what makes this "structured" logging.
            var file = Assert.Single(dir.GetFiles("test-*.jsonl"));
            var lines = File.ReadAllLines(file.FullName).Where(l => l.Length > 0).ToList();
            Assert.Equal(2, lines.Count);

            using var firstEvent = JsonDocument.Parse(lines[0]);
            var properties = firstEvent.RootElement.GetProperty("Properties");
            Assert.Equal("World", properties.GetProperty("Name").GetString());
            Assert.Equal(3, properties.GetProperty("Count").GetInt32());
            Assert.Contains("World", firstEvent.RootElement.GetProperty("RenderedMessage").GetString());

            using var secondEvent = JsonDocument.Parse(lines[1]);
            Assert.Equal("Warning", secondEvent.RootElement.GetProperty("Level").GetString());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    private static IConfiguration InMemoryConfig(bool enabled, string path)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{FileLoggingOptions.SectionName}:{nameof(FileLoggingOptions.Enabled)}"] = enabled.ToString(),
                [$"{FileLoggingOptions.SectionName}:{nameof(FileLoggingOptions.Path)}"] = path
            })
            .Build();
    }
}
