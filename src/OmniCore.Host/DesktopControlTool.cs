namespace OmniCore.Host;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmniCore.Abstractions;
using OmniCore.Domain;
using OmniCore.Extensions;

/// <summary>Session-owned selection gate. A skill selects code; it never grants authority.</summary>
internal sealed class DesktopSkillGate(SessionId session, SessionSkillActivationStore store, string skillDirectory)
{
    private LoadedSkill? _selected;
    internal bool Selected => _selected is not null;
    internal void Select(IReadOnlyList<LoadedSkill> skills)
    {
        _selected = skills.SingleOrDefault(s => s.Skill.Scope == ScopeLevel.BuiltIn
            && s.Skill.Trust == TrustLevel.Core && s.Skill.Manifest.Id == "desktop-control"
            && s.Skill.Manifest.Sealed && s.Skill.Manifest.Activation?.Explicit == true
            && s.Skill.Manifest.Tools?.Contains("desktop.control", StringComparer.Ordinal) == true
            && Path.GetFullPath(s.Skill.Directory).Equals(Path.GetFullPath(skillDirectory), StringComparison.OrdinalIgnoreCase));
    }
    internal bool IsCurrent()
    {
        var selected = _selected;
        if (selected is null) return false;
        try
        {
            var activation = store.ReadDetailed(session).GetValueOrDefault(new(ScopeLevel.BuiltIn, "desktop-control"));
            if (activation is null || activation.ManifestSha256 != selected.Skill.ManifestSha256
                || activation.PackageContentSha256 != selected.PackageContentSha256) return false;
            var loader = new SkillManifestLoader();
            var fresh = loader.DiscoverSingle(skillDirectory, ScopeLevel.BuiltIn, TrustLevel.Core);
            return fresh is not null && fresh.ManifestSha256 == selected.Skill.ManifestSha256
                && loader.Activate(fresh).PackageContentSha256 == selected.PackageContentSha256;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        { return false; }
    }
}

/// <summary>Fixed native implementation of one Core tool; never an arbitrary process launcher.</summary>
internal sealed class DesktopControlTool(DesktopSkillGate gate, Action? cancelDesktop = null, string locale = "es",
    bool supportsVision = false) : ITool, IDisposable
{
    internal const string Id = "desktop.control";
    private const string Schema = """
        {"type":"object","properties":{"operation":{"type":"string","enum":["capabilities","list_windows","get_window","activate_window","get_window_state","click","scroll","drag","type_text","press_key","invoke","set_value"]},"arguments":{"type":"object"}},"required":["operation","arguments"],"additionalProperties":false}
        """;
    private static readonly HashSet<string> Observations = ["capabilities", "list_windows", "get_window", "get_window_state"];
    private static readonly HashSet<string> Operations = [.. Observations, "activate_window", "click", "scroll", "drag", "type_text", "press_key", "invoke", "set_value"];
    private readonly SemaphoreSlim _serial = new(1, 1);
    private Process? _process;
    private bool _faulted;
    private long _request;
    private volatile bool _disposed;
    internal bool HasStarted => _process is not null;
    public ToolDescriptor Descriptor { get; } = new(new ToolId(Id),
        "Use OmniDesktop Control through the active desktop-control skill. Read the skill first. " +
        (supportsVision ? "This route can receive window screenshots." : "This route can use accessibility and menus, but cannot receive screenshots."),
        new InputSchema(Schema), ["desktop", "native", "skill"], false, false, ToolRisk.High,
        ComponentSource.Core(), ToolProtection.None, EffectClass.NonIdempotent);

    public ToolPreparation Prepare(ValidatedToolCall call, ToolPreparationContext context)
    {
        // Prepare remains pure. Re-read durable selection and package bytes only after authorization.
        if (!gate.Selected) return new PreparationRejected("desktop-control skill is inactive.", null);
        try
        {
            using var doc = JsonDocument.Parse(call.NormalizedArgumentsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty("operation", out var op) || op.ValueKind != JsonValueKind.String
                || !Operations.Contains(op.GetString()!) || !root.TryGetProperty("arguments", out var args)
                || args.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(args.GetRawText()) > 60_000)
                return new PreparationRejected("Use operation and a bounded arguments object from the skill.", null);
            var operation = op.GetString()!;
            if (operation == "get_window_state")
            {
                var screenshotFields = args.EnumerateObject().Where(property => property.NameEquals("include_screenshot")).ToArray();
                if (screenshotFields.Length > 1 || screenshotFields.Any(property =>
                    property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
                    return new PreparationRejected("Set include_screenshot once, using a boolean.", null);
                if (!supportsVision && RequestsScreenshot(operation, args))
                    return new PreparationRejected("The current model route cannot receive screenshots. Use include_text: true or menu.", null);
            }
            return new Prepared(new ToolIntent(call.ToolCallId, call.ToolId, root.GetRawText(),
                Observations.Contains(operation) ? EffectClass.None : EffectClass.NonIdempotent,
                ResourceClaims.Empty(), ToolRisk.High, null));
        }
        catch (JsonException) { return new PreparationRejected("Invalid desktop request JSON.", null); }
    }

    public async Task<ToolResult> ExecuteAsync(AuthorizedToolIntent authorized, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!gate.IsCurrent()) throw new InvalidOperationException("desktop-control skill was released or changed; activate it again.");
            if (_faulted) throw new InvalidOperationException("Native desktop channel failed; start a new turn and observe again. No action was retried.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var args = JsonDocument.Parse(authorized.Intent.NormalizedArgumentsJson);
                var operation = args.RootElement.GetProperty("operation").GetString()!;
                var parameters = args.RootElement.GetProperty("arguments");
                var screenshotRequested = RequestsScreenshot(operation, parameters);
                if (screenshotRequested && !supportsVision)
                    throw new InvalidOperationException("The current model route cannot receive screenshots.");
                EnsureStarted();
                var id = ++_request;
                using var requestBytes = new MemoryStream();
                using (var writer = new Utf8JsonWriter(requestBytes))
                {
                    writer.WriteStartObject(); writer.WriteNumber("id", id); writer.WriteString("method", operation);
                    writer.WritePropertyName("params"); parameters.WriteTo(writer);
                    writer.WriteEndObject();
                }
                var request = Encoding.UTF8.GetString(requestBytes.ToArray());
                await _process!.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
                var line = await ReadBoundedAsync(_process.StandardOutput, timeout.Token).ConfigureAwait(false);
                var node = JsonNode.Parse(line)!.AsObject();
                if (node["id"]?.GetValue<long>() != id) throw new InvalidDataException("Native desktop response correlation failed.");
                var success = node["ok"]?.GetValue<bool>() == true;
                var artifact = success && node["result"] is JsonObject result
                    ? StoreScreenshot(result, context.Artifacts, supportsVision && screenshotRequested) : null;
                var annotated = "{\"provenance\":{\"kind\":\"WindowsDesktop\",\"trust\":\"untrusted-data\"},\"externalData\":" + node.ToJsonString() + "}";
                var summary = success ? "Desktop " + operation : "Desktop rejected: "
                    + node["error"]?["code"]?.GetValue<string>() + ": " + node["error"]?["message"]?.GetValue<string>();
                return new ToolResult(summary, annotated, artifact,
                    line.Length, false, Observations.Contains(operation) ? EffectOutcome.None : success ? EffectOutcome.Applied : EffectOutcome.Unknown,
                    isError: !success);
            }
            catch
            {
                _faulted = true; Stop(); throw; // An uncertain input operation is never replayed.
            }
        }
        finally { _serial.Release(); }
    }

    private static bool RequestsScreenshot(string operation, JsonElement parameters) =>
        operation == "get_window_state" && parameters.TryGetProperty("include_screenshot", out var screenshot)
        && screenshot.ValueKind == JsonValueKind.True;

    internal static ArtifactRef? StoreScreenshot(JsonObject result, IArtifactStore? artifacts, bool expected)
    {
        const string dataPrefix = "data:image/png;base64,";
        if (result["screenshots"] is not JsonArray { Count: > 0 } screenshots)
        {
            if (expected) throw new InvalidDataException("The requested native screenshot is missing.");
            return null;
        }
        if (!expected || screenshots.Count != 1 || screenshots[0] is not JsonObject screenshot)
            throw new InvalidDataException("Unexpected native screenshot.");
        var url = screenshot["url"]?.GetValue<string>() ?? "";
        if (!url.StartsWith(dataPrefix, StringComparison.Ordinal) || artifacts is not IBinaryArtifactStore binary)
            throw new InvalidDataException("Native screenshot storage is unavailable.");
        var bytes = Convert.FromBase64String(url[dataPrefix.Length..]);
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Native screenshot exceeds 8 MiB.");
        var decoded = GeneratedImageValidator.Decode("image/png", bytes);
        var artifact = binary.PutBytes(decoded.Bytes, decoded.MediaType, ArtifactKind.Other, Sensitivity.Sensitive)
            with { FileName = "desktop.png" };
        screenshot.Remove("url");
        screenshot.Remove("path");
        screenshot["artifactHash"] = artifact.Hash.Value;
        return artifact;
    }

    private void EnsureStarted()
    {
        if (_process is not null) return;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native desktop control requires Windows.");
        var executable = Path.Combine(AppContext.BaseDirectory, "native", "omnicore-desktop.exe");
        using var expectedStream = typeof(DesktopControlTool).Assembly.GetManifestResourceStream("OmniCore.Host.desktop.sha256")
            ?? throw new InvalidDataException("This build does not include a verified native desktop component.");
        using var reader = new StreamReader(expectedStream);
        var expected = reader.ReadToEnd().Trim();
        using var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexStringLower(SHA256.HashData(file));
        if (expected.Length != 64 || actual != expected) throw new InvalidDataException("Native desktop component integrity check failed.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--allow-input");
        if (VerifiedSelectorOcr(Path.Combine(AppContext.BaseDirectory, "native", "ocr")))
            start.ArgumentList.Add("--allow-ocr");
        start.ArgumentList.Add("--locale"); start.ArgumentList.Add(locale);
        start.ArgumentList.Add("--output-dir"); start.ArgumentList.Add(Path.Combine(Path.GetTempPath(), "OmniCore-Desktop", Guid.NewGuid().ToString("N")));
        var inherited = new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" }.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        start.Environment.Clear(); foreach (var item in inherited) if (item.Value is not null) start.Environment[item.Key] = item.Value;
        _process = Process.Start(start) ?? throw new IOException("Cannot start native desktop component.");
        _ = MonitorCancellationAsync(_process.StandardError);
    }

    internal static bool VerifiedSelectorOcr(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        try
        {
            using var pin = typeof(DesktopControlTool).Assembly.GetManifestResourceStream("OmniCore.Host.desktop.ocr.sha256.json");
            if (pin is null) return false;
            using var manifest = JsonDocument.Parse(pin);
            if (manifest.RootElement.ValueKind != JsonValueKind.Object) return false;
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String || !files.Add(entry.Name)) return false;
                using var stream = File.OpenRead(Path.Combine(directory, entry.Name));
                if (!Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(entry.Value.GetString(), StringComparison.Ordinal))
                    return false;
            }
            return files.Count > 0 && !Directory.EnumerateFiles(directory).Any(path =>
                (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                && !files.Contains(Path.GetFileName(path)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return false; }
    }

    internal async System.Threading.Tasks.Task MonitorCancellationAsync(StreamReader reader)
    {
        try
        {
            var buffer = new char[256]; var line = new StringBuilder();
            while (!_disposed)
            {
                var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (count == 0) return;
                for (var i = 0; i < count; i++)
                {
                    var character = buffer[i];
                    if (character == '\n')
                    {
                        if (line.ToString().TrimEnd('\r') == "OMNICORE_DESKTOP_CANCELLED" && !_disposed)
                        {
                            cancelDesktop?.Invoke();
                            return;
                        }
                        line.Clear();
                    }
                    else if (line.Length < 256) line.Append(character);
                    else return;
                }
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096]; var text = new StringBuilder();
        while (text.Length <= 16 * 1024 * 1024)
        {
            var n = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Native desktop channel closed; outcome may be unknown.");
            var newline = Array.IndexOf(buffer, '\n', 0, n);
            text.Append(buffer, 0, newline < 0 ? n : newline);
            if (newline >= 0)
            {
                if (newline != n - 1) throw new InvalidDataException("Unexpected extra native desktop output.");
                return text.ToString();
            }
        }
        throw new InvalidDataException("Native desktop output exceeds 16 MiB.");
    }
    private void Stop()
    {
        var process = _process; _process = null; if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
    public void Dispose() { _disposed = true; Stop(); _serial.Dispose(); }
}
