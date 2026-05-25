namespace DynoTune.Models;

/// <summary>
/// Windows "Processor performance boost mode" power setting values.
/// DWORD values match setting GUID be337238-0d82-4146-a960-4f3749d470c7.
/// </summary>
public enum ProcessorBoostMode
{
    Disabled            = 0,
    Enabled             = 1,
    Aggressive          = 2,
    EfficientEnabled    = 3,
    EfficientAggressive = 4
}
