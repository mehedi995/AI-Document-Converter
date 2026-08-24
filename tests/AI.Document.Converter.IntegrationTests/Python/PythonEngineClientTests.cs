using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.Python;

// Exercises the real bundled executable (ADR-001), not a mock - the whole point
// of this test tier per docs/16-TEST-STRATEGY.md. Requires
// scripts/build-python-engine.ps1 to have been run first.
public class PythonEngineClientTests
{
    [Fact]
    public async Task CheckHealthAsync_RealBundledEngine_ReturnsTrue()
    {
        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(
            File.Exists(enginePath),
            $"Bundled Python engine not found at '{enginePath}'. Run scripts/build-python-engine.ps1 first.");

        var settings = new AppSettings { PythonExecutablePath = enginePath };
        var client = new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(settings),
            NullLogger<PythonEngineClient>.Instance);

        var isHealthy = await client.CheckHealthAsync(CancellationToken.None);

        Assert.True(isHealthy);
    }
}
