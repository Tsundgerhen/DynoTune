namespace DynoTune.Models;

public class OptimizationResult
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string CandidateName { get; set; } = string.Empty;
    public bool CandidateApplied { get; set; }
    public bool Accepted { get; set; }
    public string AcceptanceReason { get; set; } = string.Empty;
    public string RollbackReason { get; set; } = string.Empty;
}
