using Microsoft.Extensions.Configuration;

namespace First10.Infrastructure.Modules.Intake.OpenAI;

public enum OpenAiWorkload
{
    Transcription,
    Triage,
    CrewBriefing
}

public sealed class ApprovedOpenAiProfile(IConfiguration configuration)
{
    public string RequireModel(OpenAiWorkload workload)
    {
        var suffix = workload switch
        {
            OpenAiWorkload.Transcription => "TranscriptionModel",
            OpenAiWorkload.Triage => "TriageModel",
            OpenAiWorkload.CrewBriefing => "CrewBriefingModel",
            _ => throw new ArgumentOutOfRangeException(nameof(workload))
        };
        var effectiveModel = configuration[$"OpenAI:{suffix}"];
        var approvedModel = configuration[$"OpenAI:ApprovedProfile:{suffix}"];
        RequireEqual("project", configuration["OpenAI:ProjectId"], configuration["OpenAI:ApprovedProfile:ProjectId"]);
        RequireEqual("region", configuration["OpenAI:Region"], configuration["OpenAI:ApprovedProfile:Region"]);
        RequireEqual("retention", configuration["OpenAI:RetentionMode"], configuration["OpenAI:ApprovedProfile:RetentionMode"]);
        RequireEqual("model", effectiveModel, approvedModel);
        if (!string.Equals(configuration["OpenAI:RetentionMode"], "disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(configuration["OpenAI:RetentionMode"], "zero_data_retention", StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenAiProviderException("openai_approved_profile_retention_invalid");
        }

        return effectiveModel!;
    }

    public void ApplyProjectHeader(HttpRequestMessage request)
    {
        var projectId = configuration["OpenAI:ProjectId"]
            ?? throw new OpenAiProviderException("openai_approved_profile_project_missing");
        request.Headers.TryAddWithoutValidation("OpenAI-Project", projectId);
    }

    private static void RequireEqual(string field, string? effective, string? approved)
    {
        if (string.IsNullOrWhiteSpace(effective)
            || string.IsNullOrWhiteSpace(approved)
            || !string.Equals(effective, approved, StringComparison.Ordinal))
        {
            throw new OpenAiProviderException($"openai_approved_profile_{field}_mismatch");
        }
    }
}
