using System.Text;
using System.Text.Json;
using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Parties;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

/// <summary>A4-04: identification documents, document images and the customer photo.</summary>
internal static class PartyDocumentEndpoints
{
    private const string Documents = "DOCUMENTS";
    private const string PartySubModule = "PARTY";
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "application/pdf"];

    // Declared so the routing content-type check lets every upload through: the service checks the bytes and
    // returns the upload.* problem codes. Without it, ASP.NET answers 415 before the checks run.
    private const string AnyType = "*/*";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapPartyDocumentEndpoints(this RouteGroupBuilder api)
    {
        var parties = api.MapGroup("/parties/{partyId:long}").WithTags("Party documents");

        parties.MapGet("/documents", ListAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.View)
            .SensitiveRead()
            .Produces<IReadOnlyList<DocumentSummary>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapGet("/documents/{documentId:long}", GetAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.View)
            .SensitiveRead()
            .Produces<DocumentDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPost("/documents", AddAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapPut("/documents/{documentId:long}", UpdateAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Removing a document is an edit of the customer's document set (no seeded group holds PTY delete).
        parties.MapDelete("/documents/{documentId:long}", DeleteAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapPut("/documents/{documentId:long}/image", SetDocumentImageAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.Edit)
            .Accepts(typeof(byte[]), ImageTypes[0], [.. ImageTypes[1..], AnyType])
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapGet("/documents/{documentId:long}/image", GetDocumentImageAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.View)
            .SensitiveRead()
            .Produces(StatusCodes.Status200OK, contentType: ImageTypes[0], additionalContentTypes: ImageTypes[1..])
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapGet("/photo", GetPhotoAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .Produces<PhotoView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPut("/photo", SetPhotoAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.Edit)
            .Accepts(typeof(byte[]), ImageTypes[0], ImageTypes[1], AnyType)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapGet("/photo/image", GetPhotoImageAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .SensitiveRead()
            .Produces(StatusCodes.Status200OK, contentType: ImageTypes[0], additionalContentTypes: [ImageTypes[1]])
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPost("/photo/supervise", SupervisePhotoAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapPost("/photo/reject", RejectPhotoAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Addendum 3: specimen signatures (A4-09, A4-11).
        parties.MapGet("/signature", GetSignatureAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .WithTags("Signatures")
            .Produces<SignatureView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPut("/signature", CaptureSignatureAsync)
            .RequirePermission(ModuleCatalog.Party, Documents, PermissionAction.Edit)
            .WithTags("Signatures")
            .Accepts(typeof(byte[]), ImageTypes[1], AnyType)
            .Produces<CreatedId>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapGet("/signature/image", GetSignatureImageAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .WithTags("Signatures")
            .SensitiveRead()
            .Produces(StatusCodes.Status200OK, contentType: ImageTypes[1])
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPost("/signature/supervise", SuperviseSignatureAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Supervise)
            .WithTags("Signatures")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapPost("/signature/reject", RejectSignatureAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Supervise)
            .WithTags("Signatures")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> ListAsync(long partyId, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var list = await documents.ListAsync(partyId, cancellationToken).ConfigureAwait(false);
        return list is null ? NotFound() : TypedResults.Ok(list);
    }

    private static async Task<IResult> GetAsync(long partyId, long documentId, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(partyId, documentId, cancellationToken).ConfigureAwait(false);
        return document is null ? NotFound() : TypedResults.Ok(document);
    }

    private static Task<IResult> AddAsync(
        long partyId,
        DocumentRequest request,
        HttpRequest http,
        PartyDocuments documents,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        IdempotentRequest.RunAsync(
            http,
            idempotency,
            JsonSerializer.SerializeToUtf8Bytes(request, Json),
            async () =>
            {
                var result = await documents.AddAsync(
                        partyId,
                        new DocumentDraft(request.DocumentTypeId, request.DocumentNumber, request.IssuedOn, request.ExpiresOn),
                        cancellationToken)
                    .ConfigureAwait(false);
                return result.Succeeded
                    ? IdempotentOutcome.Store(StatusCodes.Status201Created, new CreatedId(result.Value))
                    : IdempotentOutcome.DoNotStore(ResultProblems.From(result));
            },
            cancellationToken);

    private static async Task<IResult> UpdateAsync(
        long partyId,
        long documentId,
        DocumentRequest request,
        PartyDocuments documents,
        CancellationToken cancellationToken)
    {
        var result = await documents.UpdateAsync(
                partyId,
                documentId,
                new DocumentDraft(request.DocumentTypeId, request.DocumentNumber, request.IssuedOn, request.ExpiresOn),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> DeleteAsync(long partyId, long documentId, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var result = await documents.DeleteAsync(partyId, documentId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> SetDocumentImageAsync(
        long partyId,
        long documentId,
        HttpRequest http,
        PartyDocuments documents,
        CancellationToken cancellationToken)
    {
        var content = await RequestBody.ReadAsync(http, documents.MaxImageBytes, cancellationToken).ConfigureAwait(false);
        var result = await documents.SetImageAsync(partyId, documentId, content, http.ContentType, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : UploadProblem(result, documents.MaxImageBytes);
    }

    private static async Task<IResult> GetDocumentImageAsync(long partyId, long documentId, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var image = await documents.GetImageAsync(partyId, documentId, cancellationToken).ConfigureAwait(false);
        return image is null ? NotFound() : TypedResults.File(image.Content, image.ContentType);
    }

    private static async Task<IResult> GetPhotoAsync(long partyId, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var photo = await documents.GetPhotoAsync(partyId, cancellationToken).ConfigureAwait(false);
        return photo is null ? NotFound() : TypedResults.Ok(photo);
    }

    private static async Task<IResult> SetPhotoAsync(
        long partyId,
        HttpRequest http,
        PartyDocuments documents,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken)
    {
        var content = await RequestBody.ReadAsync(http, documents.MaxImageBytes, cancellationToken).ConfigureAwait(false);
        return await IdempotentRequest.RunAsync(
                http,
                idempotency,
                content ?? [],
                async () =>
                {
                    var result = await documents.SetPhotoAsync(partyId, content, http.ContentType, cancellationToken).ConfigureAwait(false);
                    return result.Succeeded
                        ? IdempotentOutcome.Store(StatusCodes.Status204NoContent, null)
                        : IdempotentOutcome.DoNotStore(UploadProblem(result, documents.MaxImageBytes));
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetPhotoImageAsync(long partyId, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var image = await documents.GetPhotoImageAsync(partyId, cancellationToken).ConfigureAwait(false);
        return image is null ? NotFound() : TypedResults.File(image.Content, image.ContentType);
    }

    private static async Task<IResult> SupervisePhotoAsync(long partyId, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var result = await documents.SupervisePhotoAsync(partyId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> RejectPhotoAsync(long partyId, RejectRequest? request, PartyDocuments documents, CancellationToken cancellationToken)
    {
        var result = await documents.RejectPhotoAsync(partyId, request?.Reason, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> GetSignatureAsync(long partyId, PartySignatures signatures, CancellationToken cancellationToken)
    {
        var view = await signatures.GetAsync(partyId, cancellationToken).ConfigureAwait(false);
        return view is null ? NotFound() : TypedResults.Ok(view);
    }

    private static async Task<IResult> CaptureSignatureAsync(
        long partyId,
        HttpRequest http,
        PartySignatures signatures,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken)
    {
        var content = await RequestBody.ReadAsync(http, PartySignatures.MaxBytes, cancellationToken).ConfigureAwait(false);
        return await IdempotentRequest.RunAsync(
                http,
                idempotency,
                content ?? [],
                async () =>
                {
                    var result = await signatures.CaptureAsync(partyId, content, http.ContentType, cancellationToken).ConfigureAwait(false);
                    return result.Succeeded
                        ? IdempotentOutcome.Store(StatusCodes.Status200OK, new CreatedId(result.Value))
                        : IdempotentOutcome.DoNotStore(UploadProblem(result, PartySignatures.MaxBytes));
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetSignatureImageAsync(
        long partyId,
        PartySignatures signatures,
        bool pending = false,
        CancellationToken cancellationToken = default)
    {
        var image = await signatures.GetImageAsync(partyId, pending, cancellationToken).ConfigureAwait(false);
        return image is null ? NotFound() : TypedResults.File(image.Content, image.ContentType);
    }

    private static async Task<IResult> SuperviseSignatureAsync(long partyId, PartySignatures signatures, CancellationToken cancellationToken)
    {
        var result = await signatures.SuperviseAsync(partyId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> RejectSignatureAsync(long partyId, RejectRequest? request, PartySignatures signatures, CancellationToken cancellationToken)
    {
        var result = await signatures.RejectAsync(partyId, request?.Reason, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static IResult UploadProblem(OperationResult<long> result, int maxBytes) => result.ProblemCode switch
    {
        ProblemCodes.UploadDimensions => ApiProblems.Result(
            StatusCodes.Status400BadRequest,
            ProblemCodes.UploadDimensions,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["maxWidth"] = PartySignatures.MaxWidth, ["maxHeight"] = PartySignatures.MaxHeight }),
        ProblemCodes.UploadTooLarge => ApiProblems.Result(
            StatusCodes.Status400BadRequest,
            ProblemCodes.UploadTooLarge,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["maxBytes"] = maxBytes }),
        ProblemCodes.UploadEmpty or ProblemCodes.UploadTypeNotAllowed or ProblemCodes.UploadTypeMismatch =>
            ApiProblems.Result(StatusCodes.Status400BadRequest, result.ProblemCode),
        _ => ResultProblems.From(result)
    };

    private static IResult NotFound() => ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound);
}

/// <summary>Reads a raw request body up to a limit.</summary>
internal static class RequestBody
{
    /// <summary>The body, or null when it is longer than <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]?> ReadAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }
}

/// <summary>The result of an idempotent action: the HTTP result, and what to store for a replay.</summary>
internal sealed record IdempotentOutcome(IResult Result, int? StoreStatus, object? StoreBody)
{
    public static IdempotentOutcome Store(int status, object? body) =>
        new(body is null ? (IResult)TypedResults.StatusCode(status) : TypedResults.Json(body, statusCode: status), status, body);

    public static IdempotentOutcome DoNotStore(IResult result) => new(result, null, null);
}

/// <summary>Addendum 4: runs an action once per <c>Idempotency-Key</c>; a retry gets the first result back.</summary>
internal static class IdempotentRequest
{
    public const string Header = "Idempotency-Key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> RunAsync(
        HttpRequest request,
        IdempotencyStore store,
        ReadOnlyMemory<byte> fingerprint,
        Func<Task<IdempotentOutcome>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(action);

        if (!request.Headers.TryGetValue(Header, out var values))
        {
            return (await action().ConfigureAwait(false)).Result;
        }

        var key = values.ToString();
        if (!IdempotencyStore.IsValidKey(key))
        {
            return ApiProblems.Result(StatusCodes.Status400BadRequest, ProblemCodes.IdempotencyKeyInvalid);
        }

        var route = $"{request.Method} {request.Path}";
        route = route.Length <= 200 ? route : route[..200];
        var begin = await store.BeginAsync(key, route, IdempotencyStore.HashOf(route, fingerprint.Span), cancellationToken).ConfigureAwait(false);

        switch (begin.Decision)
        {
            case IdempotencyDecision.Replay:
                return begin.Body is null
                    ? TypedResults.StatusCode(begin.Status)
                    : TypedResults.Content(begin.Body, "application/json", Encoding.UTF8, begin.Status);
            case IdempotencyDecision.Refuse:
                return ApiProblems.Result(StatusCodes.Status409Conflict, begin.ProblemCode!);
        }

        IdempotentOutcome outcome;
        try
        {
            outcome = await action().ConfigureAwait(false);
        }
        catch
        {
            await store.ReleaseAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (outcome.StoreStatus is { } status)
        {
            var body = outcome.StoreBody is null ? null : JsonSerializer.Serialize(outcome.StoreBody, Json);
            await store.CompleteAsync(key, status, body, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await store.ReleaseAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return outcome.Result;
    }
}
