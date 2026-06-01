using System.Text.Json;

namespace TarkovMonitor.Service.Services;

public class JsonServiceConfiguration : IServiceConfiguration
{
    private readonly IConfiguration _config;
    private readonly string _configPath = "appsettings.json";

    public string? CustomLogsPath { get; set; }
    public string? TarkovTrackerToken { get; set; }
    public bool TarkovTrackerEnabled => !string.IsNullOrEmpty(TarkovTrackerToken);

    public JsonServiceConfiguration(IConfiguration config)
    {
        _config = config;
    }

    public async Task LoadAsync()
    {
        try
        {
            var section = _config.GetSection("TarkovMonitor");
            CustomLogsPath = section["CustomLogsPath"];
            TarkovTrackerToken = section["TarkovTrackerToken"];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading config: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                TarkovMonitor = new
                {
                    CustomLogsPath = CustomLogsPath ?? "",
                    TarkovTrackerToken = TarkovTrackerToken ?? ""
                }
            }, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}
