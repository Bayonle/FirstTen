namespace First10.Modules.Recognition;

public enum RecognitionConsentChoice
{
    OptIn = 1,
    Decline = 2,
    Withdraw = 3
}

public enum RecognitionAdjustmentKind
{
    Invalidate = 1,
    Reinstate = 2
}

public sealed class ContributionRecognitionAward
{
    public const string CitizenFirstResponderBadge = "citizen-first-responder";
    public const string CurrentPolicyVersion = "first10-recognition/1";

    private ContributionRecognitionAward()
    {
    }

    public Guid Id { get; private set; }
    public Guid ContributionId { get; private set; }
    public string ReporterKey { get; private set; } = string.Empty;
    public string ReviewedIncidentLga { get; private set; } = string.Empty;
    public string BadgeKey { get; private set; } = string.Empty;
    public string PolicyVersion { get; private set; } = string.Empty;
    public DateTimeOffset AwardedAtUtc { get; private set; }

    public static ContributionRecognitionAward Create(
        Guid contributionId,
        string reporterKey,
        string? reviewedIncidentLga,
        DateTimeOffset awardedAtUtc)
    {
        if (contributionId == Guid.Empty)
        {
            throw new ArgumentException("Contribution ID is required.", nameof(contributionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reporterKey);
        return new ContributionRecognitionAward
        {
            Id = Guid.NewGuid(),
            ContributionId = contributionId,
            ReporterKey = reporterKey.Trim(),
            ReviewedIncidentLga = NormalizeLga(reviewedIncidentLga),
            BadgeKey = CitizenFirstResponderBadge,
            PolicyVersion = CurrentPolicyVersion,
            AwardedAtUtc = awardedAtUtc
        };
    }

    public static string NormalizeLga(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}

public sealed class RecognitionConsentDecision
{
    private RecognitionConsentDecision()
    {
    }

    public Guid Id { get; private set; }
    public string SemanticKey { get; private set; } = string.Empty;
    public string ReporterKey { get; private set; } = string.Empty;
    public RecognitionConsentChoice Choice { get; private set; }
    public DateTimeOffset DecidedAtUtc { get; private set; }

    public static RecognitionConsentDecision Create(
        Guid id,
        string reporterKey,
        RecognitionConsentChoice choice,
        DateTimeOffset decidedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Consent decision ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reporterKey);
        return new RecognitionConsentDecision
        {
            Id = id,
            SemanticKey = $"recognition-consent:{id:N}",
            ReporterKey = reporterKey.Trim(),
            Choice = choice,
            DecidedAtUtc = decidedAtUtc
        };
    }
}

public sealed class RecognitionAwardAdjustment
{
    private RecognitionAwardAdjustment()
    {
    }

    public Guid Id { get; private set; }
    public Guid AwardId { get; private set; }
    public RecognitionAdjustmentKind Kind { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static RecognitionAwardAdjustment Create(
        Guid id,
        Guid awardId,
        RecognitionAdjustmentKind kind,
        string reasonCode,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty || awardId == Guid.Empty)
        {
            throw new ArgumentException("Adjustment and award IDs are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return new RecognitionAwardAdjustment
        {
            Id = id,
            AwardId = awardId,
            Kind = kind,
            ReasonCode = reasonCode.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }
}

public sealed class RecognitionNotificationIntent
{
    private RecognitionNotificationIntent()
    {
    }

    public Guid Id { get; private set; }
    public Guid AwardId { get; private set; }
    public Guid ContactReference { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string Language { get; private set; } = string.Empty;
    public string ExactText { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static RecognitionNotificationIntent Create(
        Guid awardId,
        Guid contactReference,
        string channel,
        string language,
        DateTimeOffset createdAtUtc)
    {
        if (awardId == Guid.Empty || contactReference == Guid.Empty)
        {
            throw new ArgumentException("Award and contact references are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var normalizedLanguage = language.Trim().ToLowerInvariant();
        return new RecognitionNotificationIntent
        {
            Id = Guid.NewGuid(),
            AwardId = awardId,
            ContactReference = contactReference,
            Channel = channel.Trim(),
            Language = normalizedLanguage,
            ExactText = LocalizedText(normalizedLanguage),
            CreatedAtUtc = createdAtUtc
        };
    }

    private static string LocalizedText(string language) => language switch
    {
        "yoruba" => "A ti fun ọ ni baaji Olùdáhùn Àkọ́kọ́ Araalu. Ìwọ nìkan ló lè yan bóyá a ó ka a mọ́ àpapọ̀ LGA; a kò ní fi orúkọ rẹ hàn.",
        "nigerianpidgin" => "You don earn Citizen First Responder badge. Na only you fit choose make e count for LGA total; we no go show your name.",
        _ => "You earned a Citizen First Responder badge. Only you can opt in to an anonymous LGA total; your name will not be shown."
    };
}

public sealed record SetRecognitionConsent(
    Guid DecisionId,
    string ReporterKey,
    RecognitionConsentChoice Choice);

public sealed record AdjustRecognitionAward(
    Guid AdjustmentId,
    Guid AwardId,
    RecognitionAdjustmentKind Kind,
    string ReasonCode);

public static class RecognitionPolicy
{
    // Service-hour and monetary awards require a future partner-backed code change.
    public static bool SupportsServiceHours => false;
    public static bool SupportsMonetaryValue => false;
}
