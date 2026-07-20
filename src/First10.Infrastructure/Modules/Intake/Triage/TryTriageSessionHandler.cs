using System.Security.Cryptography;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Modules.Intake.Location;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Infrastructure.Modules.Intake.OpenAI;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace First10.Infrastructure.Modules.Intake.Triage;

public sealed class TriageSessionProcessor(
    First10DbContext database,
    ISafeMediaReader safeMediaReader,
    OpenAiAudioPreparer audioPreparer,
    IReporterAudioTranscriber transcriber,
    IStructuredTriageProvider triageProvider,
    OpenAiSafetyIdentifier safetyIdentifier,
    CorridorGazetteer gazetteer,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public async Task ProcessAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var triageCase = await database.TriageCases.SingleOrDefaultAsync(
            x => x.SessionId == sessionId,
            cancellationToken);
        if (triageCase is null)
        {
            return;
        }

        if (triageCase.Status == TriageStatus.Completed)
        {
            await TryApplyReporterPinAsync(triageCase, cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (now >= triageCase.DeadlineAtUtc || triageCase.Status != TriageStatus.AwaitingEvidence)
        {
            return;
        }

        var session = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .SingleAsync(x => x.Id == sessionId, cancellationToken);
        var assets = await database.IntakeMediaAssets
            .Where(x => x.SessionId == sessionId && x.Status == MediaProcessingStatus.Stored)
            .ToArrayAsync(cancellationToken);
        var audioAsset = assets.SingleOrDefault(x => x.Kind == IntakeMediaKind.Audio);
        if (audioAsset?.SafeObjectKey is null
            || audioAsset.SafeContentType is null
            || audioAsset.SafeLength is null)
        {
            return;
        }

        var expectedVersion = triageCase.AuthoritativeVersion;
        if (!triageCase.TryStartProcessing(now))
        {
            return;
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            return;
        }

        var remaining = triageCase.DeadlineAtUtc - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCancellation.CancelAfter(remaining);
        var deadlineToken = deadlineCancellation.Token;
        var audioInput = session.Inputs.Single(x => x.Id == audioAsset.InputId);
        var transcriptReference = $"transcript:{audioAsset.Id:N}";
        byte[]? imageBytes = null;
        try
        {
            using var safeAudio = await safeMediaReader.ReadAsync(
                audioAsset.Id,
                audioAsset.SafeObjectKey,
                audioAsset.SafeContentType,
                audioAsset.SafeLength.Value,
                deadlineToken);
            using var preparedAudio = audioPreparer.Prepare(
                safeAudio.Bytes,
                safeAudio.ContentType);
            var transcript = await transcriber.TranscribeAsync(
                preparedAudio.Bytes,
                preparedAudio.ContentType,
                transcriptReference,
                audioInput.OccurredAtUtc,
                deadlineToken);

            var imageAsset = assets.SingleOrDefault(x => x.Kind == IntakeMediaKind.Image);
            string? imageReference = null;
            if (imageAsset?.SafeObjectKey is not null
                && imageAsset.SafeContentType == "image/jpeg"
                && imageAsset.SafeLength is not null)
            {
                using var safeImage = await safeMediaReader.ReadAsync(
                    imageAsset.Id,
                    imageAsset.SafeObjectKey,
                    imageAsset.SafeContentType,
                    imageAsset.SafeLength.Value,
                    deadlineToken);
                imageBytes = safeImage.Bytes.ToArray();
                imageReference = $"image:{imageAsset.Id:N}";
            }

            var result = await triageProvider.TriageAsync(
                new TriageProviderRequest(
                    transcript,
                    imageBytes,
                    imageReference,
                    safetyIdentifier.Create(session.ReporterKey),
                    ReadEnabledGuidanceCategories(),
                    expectedVersion),
                deadlineToken);
            await ApplyResultAsync(
                triageCase.Id,
                result,
                expectedVersion,
                timeProvider.GetUtcNow(),
                session.Id,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(session, audioInput, "triage_deadline_elapsed", cancellationToken);
        }
        catch (OpenAiProviderException exception)
        {
            await RecordFailureAsync(session, audioInput, exception.Code, cancellationToken);
        }
        catch (CryptographicException)
        {
            await RecordFailureAsync(session, audioInput, "safe_media_authentication_failed", cancellationToken);
        }
        finally
        {
            if (imageBytes is not null)
            {
                CryptographicOperations.ZeroMemory(imageBytes);
            }
        }
    }

    private async Task ApplyResultAsync(
        Guid triageCaseId,
        TriageProviderResult result,
        int expectedVersion,
        DateTimeOffset receivedAtUtc,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await PersistAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            await PersistAsync();
        }

        async Task PersistAsync()
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({triageCaseId.ToString()}))",
                cancellationToken);
            var currentCase = await database.TriageCases
                .Include(x => x.Assessments)
                .SingleAsync(x => x.Id == triageCaseId, cancellationToken);
            var existingAssessmentIds = currentCase.Assessments.Select(x => x.Id).ToHashSet();
            var authoritative = currentCase.TryApplyAssessment(
                result.Triage,
                result.ModelConfiguration,
                receivedAtUtc,
                expectedVersion);
            var assessment = currentCase.Assessments.Single(x => !existingAssessmentIds.Contains(x.Id));
            database.TriageAssessments.Add(assessment);

            var currentSession = await database.GuidedIntakeSessions
                .Include(x => x.Inputs)
                .SingleAsync(x => x.Id == sessionId, cancellationToken);
            var pin = currentSession.Inputs
                .Where(x => x.ContentKind == IntakeContentKind.Location
                            && x.Latitude.HasValue
                            && x.Longitude.HasValue)
                .OrderByDescending(x => x.OccurredAtUtc)
                .FirstOrDefault();
            var location = gazetteer.Resolve(
                result.Triage.LocationPhrase,
                pin?.Latitude,
                pin?.Longitude,
                pin is null ? result.Triage.EvidenceReferences[0].Reference : $"pin:{pin.Id:N}");
            if (location is not null)
            {
                assessment.TryApplyResolvedLocation(location);
            }

            await AuditWriter.AppendAsync(
                database,
                authoritative ? "intake.triage.completed" : "intake.triage.late_evidence",
                "worker:triage",
                AuditPayload.Create(new Dictionary<string, object?>
                {
                    ["triageCaseId"] = currentCase.Id,
                    ["assessmentId"] = assessment.Id,
                    ["authoritative"] = authoritative,
                    ["modelConfiguration"] = assessment.ModelConfiguration,
                    ["locationResolved"] = assessment.ResolvedLatitude.HasValue,
                    ["locationFromPin"] = assessment.LocationFromPin
                }),
                cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task RecordFailureAsync(
        GuidedIntakeSession session,
        GuidedSessionInput input,
        string code,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        var safeCode = code.Length <= 48 ? code : code[..48];
        database.IntakeRecoveryItems.Add(IntakeRecoveryItem.Create(
            session.Channel,
            input.ProviderMessageId,
            $"triage_{safeCode}",
            timeProvider.GetUtcNow(),
            session.ContactReference,
            session.ReporterKey));
        await AuditWriter.AppendAsync(
            database,
            "intake.triage.provider_failure",
            "worker:triage",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["triageCaseId"] = session.Id,
                ["reason"] = safeCode
            }),
            cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
        }
    }

    private HashSet<GuidanceCategory> ReadEnabledGuidanceCategories()
    {
        var values = configuration.GetSection("Guidance:EnabledCategories").Get<string[]>() ?? [];
        return values
            .Select(value => Enum.TryParse<GuidanceCategory>(value, true, out var category)
                ? category
                : GuidanceCategory.None)
            .Where(category => category != GuidanceCategory.None)
            .ToHashSet();
    }

    private async Task TryApplyReporterPinAsync(
        TriageCase triageCase,
        CancellationToken cancellationToken)
    {
        var session = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .SingleAsync(x => x.Id == triageCase.SessionId, cancellationToken);
        var pin = session.Inputs
            .Where(x => x.ContentKind == IntakeContentKind.Location
                        && x.Latitude.HasValue
                        && x.Longitude.HasValue)
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstOrDefault();
        if (pin is null || triageCase.AuthoritativeAssessmentId is null)
        {
            return;
        }

        var assessment = await database.TriageAssessments.SingleAsync(
            x => x.Id == triageCase.AuthoritativeAssessmentId,
            cancellationToken);
        var resolved = gazetteer.Resolve(
            null,
            pin.Latitude,
            pin.Longitude,
            $"pin:{pin.Id:N}");
        if (resolved is null || !assessment.TryApplyResolvedLocation(resolved))
        {
            return;
        }

        await AuditWriter.AppendAsync(
            database,
            "intake.triage.location_pin_applied",
            "worker:triage",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["triageCaseId"] = triageCase.Id,
                ["assessmentId"] = assessment.Id,
                ["locationEvidenceReference"] = resolved.EvidenceReference
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }
}

public static class TryTriageSessionHandler
{
    public static Task Handle(
        TryTriageSession command,
        TriageSessionProcessor processor,
        CancellationToken cancellationToken) =>
        processor.ProcessAsync(command.SessionId, cancellationToken);
}
