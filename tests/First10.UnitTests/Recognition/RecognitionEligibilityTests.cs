using First10.Modules.Recognition;

namespace First10.UnitTests.Recognition;

public sealed class RecognitionEligibilityTests
{
    [Fact]
    public void AwardContainsOnlyPseudonymousRecognitionFields()
    {
        var contributionId = Guid.NewGuid();
        var award = ContributionRecognitionAward.Create(
            contributionId,
            "reporter-pseudonym",
            "Obafemi Owode",
            DateTimeOffset.UtcNow);

        Assert.Equal(contributionId, award.ContributionId);
        Assert.Equal("reporter-pseudonym", award.ReporterKey);
        Assert.Equal("Obafemi Owode", award.ReviewedIncidentLga);
        Assert.Equal(ContributionRecognitionAward.CitizenFirstResponderBadge, award.BadgeKey);
        Assert.DoesNotContain(
            award.GetType().GetProperties(),
            property => property.Name.Contains("Incident", StringComparison.Ordinal)
                        && property.Name != nameof(ContributionRecognitionAward.ReviewedIncidentLga));
    }

    [Fact]
    public void MissingReviewedLgaIsExplicitlyUnknown()
    {
        var award = ContributionRecognitionAward.Create(
            Guid.NewGuid(),
            "reporter-pseudonym",
            null,
            DateTimeOffset.UtcNow);

        Assert.Equal("Unknown", award.ReviewedIncidentLga);
    }

    [Fact]
    public void PartnerBackedValuePathsCannotBeEnabledByConfiguration()
    {
        Assert.False(RecognitionPolicy.SupportsServiceHours);
        Assert.False(RecognitionPolicy.SupportsMonetaryValue);
    }

    [Theory]
    [InlineData("English", "Citizen First Responder")]
    [InlineData("NigerianPidgin", "don earn")]
    [InlineData("Yoruba", "baaji")]
    public void PrivateNotificationIsLocalizedAndContainsNoIncidentReference(
        string language,
        string expected)
    {
        var notification = RecognitionNotificationIntent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Telegram",
            language,
            DateTimeOffset.UtcNow);

        Assert.Contains(expected, notification.ExactText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("incident", notification.ExactText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("victim", notification.ExactText, StringComparison.OrdinalIgnoreCase);
    }
}
