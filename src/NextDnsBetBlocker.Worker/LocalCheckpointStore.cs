namespace NextDnsBetBlocker.Worker;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class LocalCheckpointStore : ICheckpointStore
{
    private readonly string _checkpointPath;
    private readonly ILogger<LocalCheckpointStore> _logger;

    public LocalCheckpointStore(ILogger<LocalCheckpointStore> logger)
    {
        _checkpointPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "checkpoints.txt");
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_checkpointPath)!);
    }

    public Task<DateTime?> GetLastTimestampAsync(string profileId)
    {
        try
        {
            if (!File.Exists(_checkpointPath))
                return Task.FromResult<DateTime?>(null);

            var lines = File.ReadAllLines(_checkpointPath);
            var checkpoint = lines.FirstOrDefault(line => line.StartsWith($"{profileId}|"));

            if (checkpoint != null)
            {
                var parts = checkpoint.Split('|');
                if (parts.Length >= 2 && DateTime.TryParse(parts[1], out var timestamp))
                {
                    return Task.FromResult<DateTime?>(timestamp);
                }
            }

            return Task.FromResult<DateTime?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading checkpoint");
            return Task.FromResult<DateTime?>(null);
        }
    }

    public Task UpdateLastTimestampAsync(string profileId, DateTime timestamp)
    {
        try
        {
            var lines = File.Exists(_checkpointPath) 
                ? File.ReadAllLines(_checkpointPath).ToList() 
                : new List<string>();

            var index = lines.FindIndex(line => line.StartsWith($"{profileId}|"));
            var entry = $"{profileId}|{timestamp:O}";

            if (index >= 0)
            {
                lines[index] = entry;
            }
            else
            {
                lines.Add(entry);
            }

            File.WriteAllLines(_checkpointPath, lines);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating checkpoint");
            return Task.CompletedTask;
        }
    }
}
