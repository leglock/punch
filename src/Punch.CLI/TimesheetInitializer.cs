namespace Punch.CLI;

internal static class TimesheetInitializer
{
    public static TimesheetInitialization Initialize(DateOnly date, PunchSettings settings)
    {
        // Existence, never block count, is the one-time initialization guard.
        if (File.Exists(PunchStorage.GetFilePath(date)))
        {
            var blocks = PunchStorage.Load(date, out var note);
            return new(new DaySchedule(blocks), note, false, []);
        }

        var (schedule, warnings) = DayTemplateResolver.Resolve(settings.DayTemplates, date.DayOfWeek);
        if (schedule.Count > 0)
        {
            try
            {
                // Explicit empty note avoids the block-only overload's preservation load.
                PunchStorage.Save(date, schedule.Blocks, "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    "Failed to save initial dayTemplates entries. Check that the data directory is writable and has free space, then retry.", ex);
            }
        }
        return new(schedule, "", schedule.Count > 0, warnings);
    }
}
