using System.Security.Cryptography;
using System.Text;

namespace OmniCore.Cli;

/// <summary>Personal presentation labels; model identifiers never change.</summary>
internal sealed class TuiModelDisplayNames(string directory)
{
    private readonly Dictionary<string, string?> _loaded = new(StringComparer.Ordinal);

    public string? Read(string modelId)
    {
        if (_loaded.TryGetValue(modelId, out var name)) return name;
        try
        {
            var file = new FileInfo(FileFor(modelId));
            if (file.Exists && file.Length <= 256 && TryNormalize(File.ReadAllText(file.FullName), out var stored))
                name = stored;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return _loaded[modelId] = name;
    }

    internal static bool TryNormalize(string text, out string? name)
    {
        name = null;
        if (text.Length > 48 || text.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029')) return false;
        var trimmed = text.Trim();
        if (trimmed.Length > 0) name = trimmed;
        return true;
    }

    public void Save(string modelId, string? name)
    {
        var path = FileFor(modelId);
        if (name is null) { if (Directory.Exists(directory)) File.Delete(path); }
        else
        {
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, name); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        _loaded[modelId] = name;
    }

    private string FileFor(string modelId) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modelId))) + ".txt");
}
