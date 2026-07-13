namespace AsusDisplayControl;

internal enum ScheduleMode { Fixed, Sun }

/// <summary>One "at HH:mm switch to preset N" rule for the fixed-time schedule.</summary>
internal sealed class TimeRule
{
    public string Time { get; set; } = "09:00"; // 24h HH:mm, local wall-clock
    public int Preset { get; set; } = 4;
}

/// <summary>Automatic preset-switching configuration (persisted to dwc_schedule.json).</summary>
internal sealed class ScheduleConfig
{
    public bool Enabled { get; set; }
    public ScheduleMode Mode { get; set; } = ScheduleMode.Fixed;

    // Fixed-time schedule
    public List<TimeRule> Rules { get; set; } = new();

    // Daylight (sunrise/sunset) schedule
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int DayPreset { get; set; } = 4;   // Standard
    public int NightPreset { get; set; } = 8; // Darkroom

    /// <summary>Which preset should be active right now, or null if the schedule can't decide.</summary>
    public int? DesiredPreset(DateTime localNow)
    {
        if (Mode == ScheduleMode.Sun)
        {
            if (Latitude == 0 && Longitude == 0) return null; // location not set
            return Solar.IsDaytime(localNow.ToUniversalTime(), Latitude, Longitude) ? DayPreset : NightPreset;
        }

        if (Rules == null || Rules.Count == 0) return null;

        var now = localNow.TimeOfDay;
        TimeRule? current = null; TimeSpan currentAt = TimeSpan.MinValue;
        TimeRule? lastOfDay = null; TimeSpan lastAt = TimeSpan.MinValue;
        foreach (var r in Rules)
        {
            if (!TimeSpan.TryParse(r.Time, out var at)) continue;
            if (at > lastAt) { lastAt = at; lastOfDay = r; }       // latest rule overall (for wrap-around)
            if (at <= now && at > currentAt) { currentAt = at; current = r; } // latest rule already reached today
        }
        // Before the first rule of the day -> the previous day's last rule still applies.
        return (current ?? lastOfDay)?.Preset;
    }
}
