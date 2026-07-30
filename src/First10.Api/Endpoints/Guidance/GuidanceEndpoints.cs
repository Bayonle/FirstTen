using System.Security.Claims;
using First10.Infrastructure.Modules.Guidance;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Modules.Intake.Delivery;
using First10.Infrastructure.Persistence;
using First10.Modules.Guidance;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Endpoints.Guidance;

public static class GuidanceEndpoints
{
    public static IEndpointRouteBuilder MapGuidanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var clinical = endpoints.MapGroup("/api/guidance/templates")
            .RequireAuthorization(IdentityConfiguration.ClinicalApproverPolicy)
            .WithTags("Guidance");
        clinical.MapGet("/", ListTemplatesAsync);
        clinical.MapPost("/", CreateDraftAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        clinical.MapPost("/{templateId:guid}/approve", ApproveAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        endpoints.MapPost("/api/guidance/intents/{intentId:guid}/retry", RetryAsync)
            .RequireAuthorization(IdentityConfiguration.DispatcherPolicy)
            .WithTags("Guidance")
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        return endpoints;
    }

    private static async Task<IResult> ListTemplatesAsync(
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var templates = await database.GuidanceTemplateSets.AsNoTracking()
            .Include(x => x.Assets)
            .OrderByDescending(x => x.EnabledAtUtc)
            .Select(x => new
            {
                id = x.Id,
                x.TemplateKey,
                purpose = x.Purpose.ToString(),
                category = x.Category.ToString(),
                severityBand = x.SeverityBand.ToString(),
                x.PolicyVersion,
                x.Trigger,
                x.EligibilityContext,
                x.IsConservativeDefault,
                x.ApprovedBy,
                x.ApprovedAtUtc,
                x.EnabledAtUtc,
                x.SupersededAtUtc,
                assets = x.Assets.Select(asset => new
                {
                    language = asset.Language.ToString(),
                    asset.ExactText,
                    asset.TextSha256,
                    asset.VoiceAssetKey,
                    asset.VoiceSha256,
                    asset.SpeechModel,
                    asset.SpeechVoice,
                    asset.SpeechSettings
                })
            })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(templates);
    }

    private static async Task<IResult> CreateDraftAsync(
        CreateGuidanceTemplateRequest request,
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GuidancePurpose>(request.Purpose, true, out var purpose)
            || !Enum.TryParse<GuidanceCategory>(request.Category, true, out var category)
            || !Enum.TryParse<GuidanceSeverityBand>(request.SeverityBand, true, out var severity))
        {
            return Endpoints.Incidents.Problems.Validation("template", "Purpose, category, or severity is invalid.");
        }

        try
        {
            var set = GuidanceTemplateSet.CreateDraft(
                request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
                request.TemplateKey,
                purpose,
                category,
                severity,
                request.PolicyVersion,
                request.Trigger,
                request.EligibilityContext,
                request.IsConservativeDefault,
                request.Assets.Select(asset => new GuidanceLocaleDraft(
                    Enum.Parse<GuidanceLanguage>(asset.Language, true),
                    asset.ExactText,
                    asset.TextSha256,
                    asset.VoiceAssetKey,
                    asset.VoiceSha256,
                    asset.SpeechModel,
                    asset.SpeechVoice,
                    asset.SpeechSettings)));
            database.GuidanceTemplateSets.Add(set);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/guidance/templates/{set.Id}", new { set.Id });
        }
        catch (ArgumentException exception)
        {
            return Endpoints.Incidents.Problems.Validation("template", exception.Message);
        }
    }

    private static async Task<IResult> ApproveAsync(
        Guid templateId,
        First10DbContext database,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var set = await database.GuidanceTemplateSets.Include(x => x.Assets)
            .SingleOrDefaultAsync(x => x.Id == templateId, cancellationToken);
        if (set is null)
        {
            return Results.NotFound();
        }

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        if (!set.TryApproveAndEnable(true, actor, DateTimeOffset.UtcNow))
        {
            return Results.Conflict(new { error = "Template is already approved or its assets are incomplete." });
        }

        await database.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { set.Id, set.PolicyVersion, set.EnabledAtUtc });
    }

    private static async Task<IResult> RetryAsync(
        Guid intentId,
        OutboundChannelSender sender,
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var exists = await database.GuidanceIntents.AnyAsync(x => x.Id == intentId, cancellationToken);
        if (!exists)
        {
            return Results.NotFound();
        }

        var retry = await sender.TryDeliverAsync(intentId, cancellationToken);
        var status = await database.GuidanceIntents.AsNoTracking()
            .Where(x => x.Id == intentId)
            .Select(x => new { status = x.Status.ToString(), x.AttemptCount, x.FailureCode })
            .SingleAsync(cancellationToken);
        return Results.Ok(new { status.status, status.AttemptCount, status.FailureCode, retryAfter = retry });
    }
}

public sealed record CreateGuidanceTemplateRequest(
    Guid Id,
    string TemplateKey,
    string Purpose,
    string Category,
    string SeverityBand,
    string PolicyVersion,
    string Trigger,
    string EligibilityContext,
    bool IsConservativeDefault,
    IReadOnlyList<GuidanceAssetRequest> Assets);

public sealed record GuidanceAssetRequest(
    string Language,
    string ExactText,
    string TextSha256,
    string VoiceAssetKey,
    string VoiceSha256,
    string SpeechModel,
    string SpeechVoice,
    string SpeechSettings);
