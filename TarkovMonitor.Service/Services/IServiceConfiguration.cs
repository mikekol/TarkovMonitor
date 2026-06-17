namespace TarkovMonitor.Service.Services;

/// <summary>
/// Provides read/write access to the service's runtime configuration.
/// Implementations persist settings to a backing store (e.g. appsettings.json) and expose the
/// current values to other hosted services without requiring them to parse the file directly.
/// </summary>
public interface IServiceConfiguration
{
    /// <summary>
    /// Gets or sets an optional override for the EFT Logs folder path.
    /// When <see langword="null"/> or empty the service auto-detects the path from the EFT registry key.
    /// </summary>
    string? CustomLogsPath { get; set; }

    /// <summary>
    /// Gets or sets an optional map name used as a fallback when a screenshot is taken but no map
    /// is recorded in the current raid.  Mirrors <c>Properties.Settings.Default.customMap</c> from
    /// the UI, pushed here via the <c>UpdateConfig</c> gRPC call.
    /// </summary>
    string? CustomMap { get; set; }

    /// <summary>
    /// Gets or sets the per-profile TarkovTracker API domains keyed by EFT profile ID.
    /// Defaults to <c>tarkovtracker.io</c> for any profile not explicitly configured, so that
    /// profiles belonging to different Windows users can target different TarkovTracker instances.
    /// </summary>
    Dictionary<string, string> TarkovTrackerDomains { get; set; }

    /// <summary>
    /// Gets or sets the per-profile TarkovTracker bearer tokens keyed by EFT profile ID.
    /// PVP and PVE profiles can have independent tokens without restarting the service.
    /// </summary>
    Dictionary<string, string> TarkovTrackerTokens { get; set; }

    /// <summary>
    /// Gets a value indicating whether at least one profile has a token configured.
    /// Convenience guard used by services that should do nothing when TarkovTracker is not set up.
    /// </summary>
    bool TarkovTrackerEnabled { get; }

    /// <summary>
    /// Returns the TarkovTracker domain for <paramref name="profileId"/>, falling back to
    /// <c>tarkovtracker.io</c> when no domain has been stored for that profile.
    /// </summary>
    string GetDomainForProfile(string profileId);

    /// <summary>Returns the token for the given <paramref name="profileId"/>, or <see langword="null"/> if none is set.</summary>
    string? GetTokenForProfile(string profileId);

    /// <summary>Returns <see langword="true"/> when <paramref name="profileId"/> has a token stored.</summary>
    bool HasTokenForProfile(string profileId);

    /// <summary>Persists the current in-memory configuration values to the backing store.</summary>
    Task SaveAsync();

    /// <summary>Loads (or reloads) configuration values from the backing store into memory.</summary>
    Task LoadAsync();
}
