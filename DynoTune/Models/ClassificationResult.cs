namespace DynoTune.Models;

/// <summary>
/// Output of WorkloadClassifier for one telemetry sample.
/// </summary>
public class ClassificationResult
{
    public WorkloadType WorkloadType { get; set; } = WorkloadType.Unknown;

    /// <summary>
    /// Maps to thesis / experiment <see cref="WorkloadClass"/> buckets.
    /// </summary>
    public WorkloadClass CoarseWorkloadClass { get; set; } = WorkloadClass.Mixed;

    public string Reason { get; set; } = string.Empty;
}
