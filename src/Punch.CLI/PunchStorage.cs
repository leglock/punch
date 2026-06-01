using System.Text.Json;

namespace Punch.CLI;

internal static class PunchStorage
{
    internal static string? DataDirectoryOverride { get; set; }

    public static string GetDataDirectory()
    {
        if (!string.IsNullOrEmpty(DataDirectoryOverride))
            return DataDirectoryOverride;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".punch", "data");
    }

    public static string GetFilePath(DateOnly date)
    {
        return Path.Combine(GetDataDirectory(), $"{date:yyyy-MM-dd}.json");
    }

    public static string GetDisplayPath(DateOnly date)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var full = GetFilePath(date);
        return full.StartsWith(home) ? "~" + full[home.Length..] : full;
    }

    public static List<TimeBlock> Load(DateOnly date)
    {
        var path = GetFilePath(date);
        if (!File.Exists(path))
            return new List<TimeBlock>();

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<PunchData>(json);
            if (data?.Blocks == null)
                return new List<TimeBlock>();

            var result = new List<TimeBlock>();
            var occupied = new bool[96];
            foreach (var dto in data.Blocks)
            {
                if (dto.StartSlot < 0 || dto.StartSlot >= 96 || dto.Length < 1 || dto.StartSlot + dto.Length > 96)
                    continue;

                var overlaps = false;
                for (var s = dto.StartSlot; s < dto.StartSlot + dto.Length; s++)
                {
                    if (occupied[s]) { overlaps = true; break; }
                }
                if (overlaps) continue;

                for (var s = dto.StartSlot; s < dto.StartSlot + dto.Length; s++)
                    occupied[s] = true;

                result.Add(new TimeBlock(dto.StartSlot, dto.Length, dto.Label, dto.Ticket));
            }
            return result;
        }
        catch (JsonException ex)
        {
            var backupPath = path + ".bak";
            try
            {
                File.Copy(path, backupPath, overwrite: true);
            }
            catch (Exception backupEx)
            {
                throw new InvalidOperationException($"The daily log file is corrupted and could not be backed up ({backupEx.Message}). Original parse error: {ex.Message}", ex);
            }
            throw new InvalidOperationException($"The daily log file is corrupted. A backup has been created at '{backupPath}'. Parse error: {ex.Message}", ex);
        }
    }

    public static void Save(DateOnly date, IReadOnlyList<TimeBlock> blocks)
    {
        var dir = GetDataDirectory();
        Directory.CreateDirectory(dir);

        var data = new PunchData
        {
            Blocks = blocks.Select(b => new TimeBlockDto
            {
                StartSlot = b.StartSlot,
                Length = b.Length,
                Label = b.Label,
                Ticket = b.Ticket
            }).ToList()
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var path = GetFilePath(date);
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }
}
