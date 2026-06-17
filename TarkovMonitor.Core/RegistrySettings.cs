using Microsoft.Win32;

namespace TarkovMonitor;

/// <summary>
/// Provides shared settings stored under <c>HKLM\SOFTWARE\TarkovMonitor</c> so that both the
/// Windows Service and the UI process can read the same values without a gRPC round-trip.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read access</b> (both Service and UI): All reads use <see cref="RegistryKey.OpenSubKey(string)"/>
/// which does not require elevation on any modern Windows version.
/// </para>
/// <para>
/// <b>Write access</b> (Service only): <see cref="RegistryKey.CreateSubKey(string, bool)"/> with
/// <c>writable: true</c> against HKLM requires administrator privileges.  The Windows Service runs as
/// LocalSystem and may therefore write these values.  UI processes must call the gRPC
/// <c>UpdateConfig</c> RPC instead, which instructs the Service to persist the change.
/// </para>
/// </remarks>
public static class RegistrySettings
{
    private const string BaseKey = @"SOFTWARE\TarkovMonitor";

    /// <summary>
    /// Gets or sets a custom path to the EFT Logs folder.
    /// Returns <see langword="null"/> when no custom path has been stored.
    /// </summary>
    /// <remarks>Setting to <see langword="null"/> or empty string clears the stored value.</remarks>
    public static string? CustomLogsPath
    {
        get => Registry.LocalMachine.OpenSubKey(BaseKey)?.GetValue("CustomLogsPath") as string;
        set => Set("CustomLogsPath", value ?? "");
    }

    /// <summary>
    /// Writes a named string value to the TarkovMonitor registry key, creating it if absent.
    /// Requires administrator/LocalSystem rights because the target hive is HKLM.
    /// </summary>
    private static void Set(string name, string value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(BaseKey, writable: true);
        key.SetValue(name, value);
    }
}
