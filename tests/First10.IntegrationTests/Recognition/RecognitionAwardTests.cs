using First10.Infrastructure.Modules.Recognition;
using First10.Infrastructure.Persistence;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Recognition;
using Microsoft.EntityFrameworkCore;

namespace First10.IntegrationTests.Recognition;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class RecognitionAwardTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task DispatcherVerifiedContributionReplayCreatesOnePrivateAward()
    {
        var now = new DateTimeOffset(2041, 11, 25, 13, 0, 0, TimeSpan.Zero);
        var contribution = new ContributionDispatcherVerified(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "reporter-recognition",
            Guid.NewGuid(),
            "Telegram",
            "Yoruba",
            "Obafemi Owode",
            now);
        await using (var database = Database())
        {
            await database.Database.MigrateAsync();
            var processor = new RecognitionAwardProcessor(database, new FixedTimeProvider(now));
            var first = await processor.ApplyAsync(contribution);
            var replay = await processor.ApplyAsync(contribution);
            Assert.NotNull(first);
            Assert.Equal(first, replay);
        }

        await using var verification = Database();
        var award = await verification.ContributionRecognitionAwards.SingleAsync(
            x => x.ContributionId == contribution.ContributionId);
        Assert.Equal("reporter-recognition", award.ReporterKey);
        Assert.Equal("Obafemi Owode", award.ReviewedIncidentLga);
        Assert.Single(await verification.RecognitionNotificationIntents
            .Where(x => x.AwardId == award.Id)
            .ToArrayAsync());
        Assert.Empty(await verification.RecognitionConsentDecisions
            .Where(x => x.ReporterKey == award.ReporterKey)
            .ToArrayAsync());
    }

    [Fact]
    public async Task OnlyLatestExplicitOptInContributesToAnonymousLgaAggregate()
    {
        var now = new DateTimeOffset(2042, 12, 26, 14, 0, 0, TimeSpan.Zero);
        var contribution = new ContributionDispatcherVerified(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "reporter-consent", Guid.NewGuid(),
            "WhatsApp", "English", "Ikeja", now);
        await using var database = Database();
        await database.Database.MigrateAsync();
        var awards = new RecognitionAwardProcessor(database, new FixedTimeProvider(now));
        await awards.ApplyAsync(contribution);
        var consent = new RecognitionConsentProcessor(database, new FixedTimeProvider(now.AddMinutes(1)));
        Assert.True(await consent.ApplyAsync(new SetRecognitionConsent(
            Guid.NewGuid(), contribution.ReporterKey, RecognitionConsentChoice.OptIn)));
        var publicCount = await CountPublicAwardsAsync(database, "Ikeja");
        Assert.Equal(1, publicCount);

        consent = new RecognitionConsentProcessor(database, new FixedTimeProvider(now.AddMinutes(2)));
        Assert.True(await consent.ApplyAsync(new SetRecognitionConsent(
            Guid.NewGuid(), contribution.ReporterKey, RecognitionConsentChoice.Withdraw)));

        Assert.Equal(0, await CountPublicAwardsAsync(database, "Ikeja"));
    }

    private static async Task<int> CountPublicAwardsAsync(First10DbContext database, string lga)
    {
        var decisions = await database.RecognitionConsentDecisions.AsNoTracking()
            .OrderBy(x => x.DecidedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => new { x.ReporterKey, x.Choice })
            .ToArrayAsync();
        var optedIn = decisions.GroupBy(x => x.ReporterKey, StringComparer.Ordinal)
            .Where(group => group.Last().Choice == RecognitionConsentChoice.OptIn)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var awards = await database.ContributionRecognitionAwards.AsNoTracking()
            .Where(award => award.ReviewedIncidentLga == lga)
            .Select(award => award.ReporterKey)
            .ToArrayAsync();
        return awards.Count(optedIn.Contains);
    }

    private First10DbContext Database() => new(
        First10.Infrastructure.Persistence.PersistenceConfiguration.CreateOptions(postgres.ConnectionString));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
