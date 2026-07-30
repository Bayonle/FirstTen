namespace First10.Modules.Intake.Triage;

public sealed class TriageValidationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class TriagePolicy
{
    private const int MaximumPlausibleCasualties = 100;

    public static void Validate(
        StructuredTriage result,
        IReadOnlySet<GuidanceCategory> enabledGuidanceCategories,
        IReadOnlySet<string> availableEvidenceReferences)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(enabledGuidanceCategories);
        ArgumentNullException.ThrowIfNull(availableEvidenceReferences);

        if (result.CasualtyMinimum is < 0 or > MaximumPlausibleCasualties
            || result.CasualtyMaximum is < 0 or > MaximumPlausibleCasualties
            || result.CasualtyMinimum > result.CasualtyMaximum)
        {
            throw new TriageValidationException("implausible_casualty_range");
        }

        if (result.EvidenceReferences.Count == 0
            || result.EvidenceReferences.Any(x =>
                string.IsNullOrWhiteSpace(x.Reference)
                || !availableEvidenceReferences.Contains(x.Reference)))
        {
            throw new TriageValidationException("invalid_evidence_reference");
        }

        if (result.GuidanceCategory != GuidanceCategory.None
            && !enabledGuidanceCategories.Contains(result.GuidanceCategory))
        {
            throw new TriageValidationException("disabled_guidance_category");
        }

        if (result.IncidentType == IncidentType.Unknown
            && result.Uncertainty != TriageUncertainty.High)
        {
            throw new TriageValidationException("unknown_requires_high_uncertainty");
        }
    }
}
