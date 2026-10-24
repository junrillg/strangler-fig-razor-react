using Microsoft.Extensions.Configuration;

namespace Strangler.Common;

public static class Secrets
{
    public static string Read(IConfiguration configuration, string key, string defaultPath)
    {
        var path = configuration[key] ?? defaultPath;
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 24 or > 4096)
        {
            throw new InvalidOperationException($"A valid mounted secret is required for {key}.");
        }

        var value = File.ReadAllText(path).TrimEnd('\r', '\n');
        if (value.Length is < 24 or > 512 || value.Any(char.IsControl))
        {
            throw new InvalidOperationException($"The mounted secret for {key} is invalid.");
        }

        return value;
    }
}
