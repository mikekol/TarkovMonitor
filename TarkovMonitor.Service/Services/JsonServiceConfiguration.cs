using System.Text.Json.Nodes;

namespace TarkovMonitor.Service.Services;

/// <summary>
/// <see cref="IServiceConfiguration"/> implementation that persists settings to
/// <c>appsettings.json</c> in the service's working directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>SaveAsync</b> builds the JSON document manually using <see cref="JsonObject"/> nodes rather
/// than serializing an anonymous/custom type.  Anonymous-type serialization requires reflection
/// metadata that the Native AOT trimmer removes; building the tree explicitly avoids that problem.
/// </para>
/// <para>
/// <b>LoadAsync</b> reads values through <see cref="IConfiguration"/>, which is already wired to
/// appsettings.json by the ASP.NET Core host and does not use reflection for primitive reads.
/// </para>
/// </remarks>
public class JsonServiceConfiguration : IServiceConfiguration
{
    private readonly IConfiguration _config;

    /// <summary>Path to the JSON file that is written by <see cref="SaveAsync"/>.</summary>
    private readonly string _configPath = "appsettings.json";

    /// <inheritdoc />
    public string? CustomLogsPath { get; set; }

    /// <inheritdoc />
    public string? CustomMap { get; set; }

    /// <inheritdoc />
    public Dictionary<string, string> TarkovTrackerDomains { get; set; } = new();

    /// <inheritdoc />
    public Dictionary<string, string> TarkovTrackerTokens { get; set; } = new();

    /// <inheritdoc />
    public bool TarkovTrackerEnabled => TarkovTrackerTokens.Count > 0;

    /// <summary>
    /// Initialises the configuration service.  Values are not loaded until <see cref="LoadAsync"/> is called.
    /// </summary>
    /// <param name="config">
    /// The <see cref="IConfiguration"/> instance supplied by the ASP.NET Core host, pre-wired to appsettings.json.
    /// </param>
    public JsonServiceConfiguration(IConfiguration config)
    {
        _config = config;
    }

    /// <inheritdoc />
    public string GetDomainForProfile(string profileId) =>
        TarkovTrackerDomains.TryGetValue(profileId, out var domain) && !string.IsNullOrEmpty(domain)
            ? domain
            : "tarkovtracker.io";

    /// <inheritdoc />
    public string? GetTokenForProfile(string profileId) =>
        TarkovTrackerTokens.TryGetValue(profileId, out var token) ? token : null;

    /// <inheritdoc />
    public bool HasTokenForProfile(string profileId) =>
        TarkovTrackerTokens.ContainsKey(profileId) && !string.IsNullOrEmpty(TarkovTrackerTokens[profileId]);

    /// <inheritdoc />
    public async Task LoadAsync()
    {
        try
        {
            var section = _config.GetSection("TarkovMonitor");
            CustomLogsPath = section["CustomLogsPath"];
            CustomMap      = section["CustomMap"];

            // IConfiguration natively enumerates child keys of a section, which is AOT-safe.
            TarkovTrackerDomains = new();
            foreach (var child in section.GetSection("TarkovTrackerDomains").GetChildren())
                if (!string.IsNullOrEmpty(child.Value))
                    TarkovTrackerDomains[child.Key] = child.Value;

            TarkovTrackerTokens = new();
            foreach (var child in section.GetSection("TarkovTrackerTokens").GetChildren())
                if (!string.IsNullOrEmpty(child.Value))
                    TarkovTrackerTokens[child.Key] = child.Value;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading config: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        try
        {
            // Build sub-objects explicitly — avoids serializing Dictionary<string,string>
            // via reflection (which the AOT trimmer would remove).
            var domainsNode = new JsonObject();
            foreach (var kvp in TarkovTrackerDomains)
                domainsNode[kvp.Key] = kvp.Value;

            var tokensNode = new JsonObject();
            foreach (var kvp in TarkovTrackerTokens)
                tokensNode[kvp.Key] = kvp.Value;

            var root = new JsonObject
            {
                ["TarkovMonitor"] = new JsonObject
                {
                    ["CustomLogsPath"]        = CustomLogsPath ?? "",
                    ["CustomMap"]             = CustomMap ?? "",
                    ["TarkovTrackerDomains"]  = domainsNode,
                    ["TarkovTrackerTokens"]   = tokensNode
                }
            };

            // JsonNode.ToJsonString does not use reflection — it serializes the already-parsed
            // node tree, so it is safe under Native AOT.
            await File.WriteAllTextAsync(_configPath,
                root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Mirror the custom logs path into the Registry so GameWatcher can read it without
            // a gRPC round-trip.  Writes require LocalSystem/admin; the Service runs with sufficient rights.
            if (!string.IsNullOrEmpty(CustomLogsPath))
                RegistrySettings.CustomLogsPath = CustomLogsPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}
