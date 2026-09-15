using Watchout.Core.Models;
using Watchout.Core.Persistence;

namespace Watchout.Core.Media;

public static class AccessControl
{
    public static bool IsLocked(AppSettings settings) =>
        settings.AccessEnabled && !string.IsNullOrWhiteSpace(settings.AccessPin);

    public static bool Unlock(AppSettings settings, string? pin) =>
        !IsLocked(settings) || string.Equals(settings.AccessPin, pin ?? "", StringComparison.Ordinal);

    public static bool CanEditShow(AccessRole role) => role != AccessRole.Viewer;

    public static bool CanOutput(AccessRole role) => role != AccessRole.Viewer;
}

public static class NodeControl
{
    public static string HealthUrl(ShowNode node)
    {
        var host = string.IsNullOrWhiteSpace(node.Address) ? "127.0.0.1" : node.Address;
        var port = node.ControlPort > 0 ? node.ControlPort : 8090;
        return $"http://{host}:{port}/watchme/health";
    }

    public static string ShowUrl(ShowNode node)
    {
        var host = string.IsNullOrWhiteSpace(node.Address) ? "127.0.0.1" : node.Address;
        var port = node.ControlPort > 0 ? node.ControlPort : 8090;
        return $"http://{host}:{port}/watchme/show";
    }

    public static bool ParseHealth(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        return json.Contains("ok", StringComparison.OrdinalIgnoreCase)
               || json.Contains("online", StringComparison.OrdinalIgnoreCase)
               || json.Contains("\"status\"", StringComparison.OrdinalIgnoreCase);
    }
}
