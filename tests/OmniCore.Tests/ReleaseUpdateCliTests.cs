using OmniCore.Cli;
using OmniCore.Protocol;
using OmniCore.Host;

namespace OmniCore.Tests;

[Collection(nameof(ProcessEnvironmentCollection))]
public sealed class ReleaseUpdateCliTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly string? _previousData = Environment.GetEnvironmentVariable("OMNICORE_DATA_DIR");
    private readonly string? _previousConfig = Environment.GetEnvironmentVariable("OMNICORE_CONFIG_DIR");
    public ReleaseUpdateCliTests()
    {
        Environment.SetEnvironmentVariable("OMNICORE_DATA_DIR", Path.Combine(_directory.Path, "data"));
        Environment.SetEnvironmentVariable("OMNICORE_CONFIG_DIR", Path.Combine(_directory.Path, "config"));
    }
    public void Dispose()
    {
        CliApp.ReleaseUpdateApplyOverride = null;
        Environment.SetEnvironmentVariable("OMNICORE_DATA_DIR", _previousData);
        Environment.SetEnvironmentVariable("OMNICORE_CONFIG_DIR", _previousConfig);
        _directory.Dispose();
    }

    [Theory]
    [InlineData("0.1.1-rc.4", ReleaseUpdateChannel.Preview)]
    [InlineData("0.1.1-rc.9+source", ReleaseUpdateChannel.Preview)]
    [InlineData("1.0.0", ReleaseUpdateChannel.Stable)]
    [InlineData("1.0.0+development-with-hyphen", ReleaseUpdateChannel.Stable)]
    public void Simple_channel_tracks_prerelease_identity_instead_of_build_metadata(string version, ReleaseUpdateChannel expected) =>
        Assert.Equal(expected, CliApp.AutomaticUpdateChannel(version));

    [Theory]
    [InlineData("update")]
    [InlineData("-update")]
    [InlineData("--update")]
    public async Task Simple_update_selects_preview_when_no_stable_exists_and_applies_without_extra_arguments(string command)
    {
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        var channels = new List<ReleaseUpdateChannel>(); var applies = 0;
        CliApp.ReleaseUpdateCheckOverride = (request, _) =>
        {
            channels.Add(request.Channel);
            return Task.FromResult(request.Channel == ReleaseUpdateChannel.Stable
                ? new ReleaseUpdateCheckResult(request.Channel, ReleaseUpdateStatus.NoPublishedRelease,
                    ReleaseCompatibility.Unknown, request.InstalledVersion)
                : new ReleaseUpdateCheckResult(request.Channel, ReleaseUpdateStatus.UpdateAvailable,
                    ReleaseCompatibility.Compatible, request.InstalledVersion, "0.2.0-rc.1", "v0.2.0-rc.1",
                    Notes: "untrusted notes must not appear", Asset: new(42, "fixture.tgz", 100, new string('a', 64), "win-x64")));
        };
        CliApp.ReleaseUpdateApplyOverride = check =>
        {
            applies++; Assert.Equal(ReleaseUpdateChannel.Preview, check.Channel);
            return Task.FromResult(new ReleaseUpdateApplyResult(true));
        };
        try
        {
            Assert.Equal(0, await CliApp.RunAsync([command]));
            Assert.Equal(1, applies);
            Assert.Equal(ReleaseUpdateChannel.Preview, channels[^1]);
            Assert.DoesNotContain("intención asumida", output.ToString());
            Assert.DoesNotContain("untrusted notes", output.ToString());
            Assert.DoesNotContain("Confirmación requerida", output.ToString());
        }
        finally { CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut); }
    }

    [Theory]
    [InlineData(ReleaseUpdateStatus.NetworkError)]
    [InlineData(ReleaseUpdateStatus.RateLimited)]
    [InlineData(ReleaseUpdateStatus.UpToDate)]
    [InlineData(ReleaseUpdateStatus.ManifestInvalid)]
    public async Task Simple_update_never_changes_channels_or_applies_after_network_integrity_or_current_results(ReleaseUpdateStatus status)
    {
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        var calls = 0;
        CliApp.ReleaseUpdateCheckOverride = (request, _) =>
        {
            calls++;
            return Task.FromResult(new ReleaseUpdateCheckResult(request.Channel, status,
                ReleaseCompatibility.Unknown, request.InstalledVersion));
        };
        CliApp.ReleaseUpdateApplyOverride = _ => throw new InvalidOperationException("must not apply");
        try
        {
            Assert.Equal(status == ReleaseUpdateStatus.UpToDate ? 0 : 1, await CliApp.RunAsync(["update"]));
            Assert.Equal(1, calls);
        }
        finally { CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut); }
    }

    [Fact]
    public async Task Download_http_failure_is_reported_as_network_failure_instead_of_unsupported_installation()
    {
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        CliApp.ReleaseUpdateCheckOverride = (request, _) => Task.FromResult(new ReleaseUpdateCheckResult(
            request.Channel, ReleaseUpdateStatus.UpdateAvailable, ReleaseCompatibility.Compatible,
            request.InstalledVersion, "0.2.0", "v0.2.0", Asset: new(42, "fixture.tgz", 100, new string('a', 64), "win-x64")));
        CliApp.ReleaseUpdateApplyOverride = _ => Task.FromResult(new ReleaseUpdateApplyResult(false, "release_asset_http_618"));
        try
        {
            Assert.Equal(1, await CliApp.RunAsync(["update"]));
            Assert.Contains("red rechazó", output.ToString());
            Assert.Contains("release_asset_http_618", output.ToString());
            Assert.DoesNotContain("no soportada", output.ToString());
        }
        finally { CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut); }
    }

    [Fact]
    public async Task Simple_update_hands_the_exact_prepared_operation_to_the_waiting_launcher()
    {
        var previousNonce = Environment.GetEnvironmentVariable("OMNICORE_UPDATE_RECEIPT_NONCE");
        var nonce = new string('c', 32); var operationId = new string('b', 32);
        Environment.SetEnvironmentVariable("OMNICORE_UPDATE_RECEIPT_NONCE", nonce);
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        CliApp.ReleaseUpdateCheckOverride = (request, _) => Task.FromResult(new ReleaseUpdateCheckResult(
            request.Channel, ReleaseUpdateStatus.UpdateAvailable, ReleaseCompatibility.Compatible,
            request.InstalledVersion, "0.2.0", "v0.2.0", Asset: new(42, "fixture.tgz", 100, new string('a', 64), "win-x64")));
        CliApp.ReleaseUpdateApplyOverride = _ => Task.FromResult(new ReleaseUpdateApplyResult(true, State: new(
            1, ReleaseUpdateOperation.Apply, ReleaseUpdateOperationStatus.Pending, "0.1.0", "0.2.0",
            null, null, Environment.ProcessId, null, DateTimeOffset.UtcNow, null, OperationId: operationId)));
        try
        {
            Assert.Equal(0, await CliApp.RunAsync(["update"]));
            var line = output.ToString().Split('\n').Single(value => value.StartsWith("OMNICORE_UPDATE_", StringComparison.Ordinal));
            var marker = "OMNICORE_UPDATE_" + nonce + ":";
            Assert.StartsWith(marker, line);
            using var receipt = System.Text.Json.JsonDocument.Parse(line[marker.Length..]);
            Assert.Equal(operationId, receipt.RootElement.GetProperty("operationId").GetString());
            Assert.Equal(ReleaseUpdateStateStore.CreateDefault().StatePath, receipt.RootElement.GetProperty("statePath").GetString());
            Assert.Contains("0.2.0", receipt.RootElement.GetProperty("succeeded").GetString()!);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OMNICORE_UPDATE_RECEIPT_NONCE", previousNonce);
            CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut);
        }
    }

    [Theory]
    [InlineData("check --channel live")]
    [InlineData("check --channel stable --force")]
    [InlineData("apply --channel live")]
    [InlineData("rollback --bogus")]
    [InlineData("status extra")]
    [InlineData("unknown")]
    public async Task Malformed_update_commands_stop_before_any_checker_or_model_route(string tail)
    {
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        var calls = 0;
        CliApp.ReleaseUpdateCheckOverride = (_, _) => { calls++; throw new InvalidOperationException("must not check"); };
        try
        {
            var args = new[] { "update" }.Concat(tail.Split(' ')).ToArray();
            var exit = await CliApp.RunAsync(args);
            Assert.Equal(2, exit); Assert.Equal(0, calls);
            Assert.Contains("omni update check|status", output.ToString());
        }
        finally { CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut); }
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("rollback")]
    public async Task Destructive_commands_require_confirmation_before_checking_or_inspecting(string command)
    {
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        var calls = 0;
        CliApp.ReleaseUpdateCheckOverride = (_, _) => { calls++; throw new InvalidOperationException("must not check"); };
        try
        {
            var exit = await CliApp.RunAsync(["update", command]);
            Assert.Equal(1, exit); Assert.Equal(0, calls);
            Assert.Contains("update_confirmation_required", output.ToString());
        }
        finally { CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut); }
    }

    [Fact]
    public async Task Apply_with_confirmation_reports_a_release_without_a_verifiable_asset()
    {
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        var calls = 0;
        CliApp.ReleaseUpdateCheckOverride = (request, _) =>
        {
            calls++;
            return Task.FromResult(new ReleaseUpdateCheckResult(request.Channel, ReleaseUpdateStatus.UpdateAvailable,
                ReleaseCompatibility.Compatible, request.InstalledVersion, "0.2.0", "v0.2.0"));
        };
        try
        {
            var exit = await CliApp.RunAsync(["update", "apply", "--yes"]);
            Assert.Equal(1, exit); Assert.Equal(1, calls);
            Assert.Contains("update_asset_unavailable", output.ToString());
        }
        finally { CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut); }
    }

    [Fact]
    public async Task Valid_preview_check_uses_only_the_injected_checker_and_sanitizes_terminal_content()
    {
        var previousOut = Console.Out; using var output = new StringWriter(); Console.SetOut(output);
        ReleaseUpdateCheckRequest? received = null;
        CliApp.ReleaseUpdateCheckOverride = (request, _) =>
        {
            received = request;
            return Task.FromResult(new ReleaseUpdateCheckResult(request.Channel, ReleaseUpdateStatus.CompatibilityUnknown,
                ReleaseCompatibility.Unknown, request.InstalledVersion, "2.0.0-rc.1", "v2.0.0-rc.1",
                "1111111111111111111111111111111111111111",
                "https://github.com/juancgomezm-s/OmniCore/releases/tag/v2.0.0-rc.1", "note\u001b[31mtext"));
        };
        try
        {
            var exit = await CliApp.RunAsync(["--locale", "en", "update", "check", "--channel", "preview"]);
            Assert.Equal(0, exit); Assert.Equal(ReleaseUpdateChannel.Preview, received!.Channel);
            Assert.Contains("Compatibility unknown", output.ToString());
            Assert.Contains("Metadata only", output.ToString());
            Assert.DoesNotContain('\u001b', output.ToString());
        }
        finally { CliApp.ReleaseUpdateCheckOverride = null; Console.SetOut(previousOut); }
    }
}
