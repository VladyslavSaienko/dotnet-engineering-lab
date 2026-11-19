using System.ComponentModel.DataAnnotations;

namespace LastCopy.Shared.Telemetry;

/// <summary>Settings bound from the "LastCopy" configuration section.</summary>
public sealed class LastCopyOptions
{
    public const string SectionName = "LastCopy";

    /// <summary>
    /// Identifies the variant in metrics and traces (for example <c>v0-naive</c>), so runs of different
    /// variants can be told apart in the same dashboard.
    /// </summary>
    [Required]
    public string Variant { get; set; } = string.Empty;
}
