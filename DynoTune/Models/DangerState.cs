namespace DynoTune.Models;

public class DangerState
{
    public DangerLevel Level { get; set; } = DangerLevel.Safe;
    public DangerReason Reason { get; set; } = DangerReason.None;
    public string ReasonDetail { get; set; } = string.Empty;
    public DateTime? FirstTriggeredUtc { get; set; }
    public DateTime? LastTriggeredUtc { get; set; }
    public bool AutoRollbackApplied { get; set; }
}
