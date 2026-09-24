using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Documents.Domain;
using VMS.Modules.Documents.Models;
using VMS.Modules.Documents.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Documents.Controllers;

/// <summary>A vehicle's or a partner's documents (FSD §23A): one engine, reached from two routes so each owner's screen calls a route of its own.</summary>
[ApiController]
public class OwnerDocumentsController(IDocumentService documents) : ControllerBase
{
    private DocumentCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet("api/vehicles/{id:int}/documents")]
    [RequirePermission(PermissionCodes.DOC_VIEW)]
    public async Task<IActionResult> VehicleList(int id, [FromQuery] bool includeHistory = false) =>
        Ok(ApiResponse<List<DocumentSlotModel>>.Ok(await documents.ListAsync(DocumentOwnerTypes.Vehicle, id, includeHistory)));

    [HttpPost("api/vehicles/{id:int}/documents")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequirePermission(PermissionCodes.DOC_UPLOAD)]
    public async Task<IActionResult> VehicleUpload(int id, [FromForm] int documentTypeId, [FromForm] string? documentNumber, [FromForm] string? provider,
        [FromForm] string? issueDate, [FromForm] string? expiryDate, IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        var request = new UploadDocumentRequest { DocumentTypeId = documentTypeId, DocumentNumber = documentNumber, Provider = provider, IssueDate = issueDate, ExpiryDate = expiryDate };
        return Ok(ApiResponse<DocumentModel>.Ok(await documents.UploadAsync(DocumentOwnerTypes.Vehicle, id, request, stream, file.FileName, Caller), "Document uploaded."));
    }

    [HttpPost("api/vehicles/{id:int}/documents/{documentTypeId:int}/renew")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequirePermission(PermissionCodes.DOC_RENEW)]
    public async Task<IActionResult> VehicleRenew(int id, int documentTypeId, [FromForm] string? documentNumber, [FromForm] string? provider,
        [FromForm] string? issueDate, [FromForm] string? expiryDate, [FromForm] int? linkedTransactionId, IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        var request = new RenewDocumentRequest { DocumentNumber = documentNumber, Provider = provider, IssueDate = issueDate, ExpiryDate = expiryDate, LinkedTransactionId = linkedTransactionId };
        return Ok(ApiResponse<DocumentModel>.Ok(await documents.RenewAsync(DocumentOwnerTypes.Vehicle, id, documentTypeId, request, stream, file.FileName, Caller), "Document renewed."));
    }

    [HttpGet("api/partners/{id:int}/documents")]
    [RequirePermission(PermissionCodes.DOC_VIEW)]
    public async Task<IActionResult> PartnerList(int id, [FromQuery] bool includeHistory = false) =>
        Ok(ApiResponse<List<DocumentSlotModel>>.Ok(await documents.ListAsync(DocumentOwnerTypes.BusinessPartner, id, includeHistory)));

    [HttpPost("api/partners/{id:int}/documents")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequirePermission(PermissionCodes.DOC_UPLOAD)]
    public async Task<IActionResult> PartnerUpload(int id, [FromForm] int documentTypeId, [FromForm] string? documentNumber, [FromForm] string? provider,
        [FromForm] string? issueDate, [FromForm] string? expiryDate, IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        var request = new UploadDocumentRequest { DocumentTypeId = documentTypeId, DocumentNumber = documentNumber, Provider = provider, IssueDate = issueDate, ExpiryDate = expiryDate };
        return Ok(ApiResponse<DocumentModel>.Ok(await documents.UploadAsync(DocumentOwnerTypes.BusinessPartner, id, request, stream, file.FileName, Caller), "Document uploaded."));
    }

    [HttpPost("api/partners/{id:int}/documents/{documentTypeId:int}/renew")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequirePermission(PermissionCodes.DOC_RENEW)]
    public async Task<IActionResult> PartnerRenew(int id, int documentTypeId, [FromForm] string? documentNumber, [FromForm] string? provider,
        [FromForm] string? issueDate, [FromForm] string? expiryDate, IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        var request = new RenewDocumentRequest { DocumentNumber = documentNumber, Provider = provider, IssueDate = issueDate, ExpiryDate = expiryDate };
        return Ok(ApiResponse<DocumentModel>.Ok(await documents.RenewAsync(DocumentOwnerTypes.BusinessPartner, id, documentTypeId, request, stream, file.FileName, Caller), "Document renewed."));
    }
}

/// <summary>Actions on one document version that do not belong to an owner's route: reject, download (BR-DOC-007, BR-DOC-008).</summary>
[ApiController]
[Route("api/documents")]
public class DocumentActionsController(IDocumentService documents, IDocumentRegisterService register, IDocumentTypeService types) : ControllerBase
{
    private DocumentCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpPost("{id:int}/reject")]
    [RequirePermission(PermissionCodes.DOC_REJECT)]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectDocumentRequest request) =>
        Ok(ApiResponse<DocumentModel>.Ok(await documents.RejectAsync(id, request, Caller), "Document rejected."));

    /// <summary>
    /// A short-lived, authorised link to the file (FR-DOC-001): the storage path is never returned. Every call is audited (BR-DOC-007).
    /// No <c>[RequirePermission]</c> here: which permission is needed (DOC_DOWNLOAD, or DOC_DOWNLOAD_SENSITIVE for a CNIC/licence, §23A.5)
    /// depends on the document being downloaded, so the check is made inside <see cref="IDocumentService.DownloadLinkAsync"/> once it knows the type.
    /// The controller-level <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute"/> the API applies to every endpoint still requires a signed-in caller.
    /// </summary>
    [HttpPost("{id:int}/download-link")]
    [AuthenticatedOnly]
    public async Task<IActionResult> DownloadLink(int id) =>
        Ok(ApiResponse<DownloadLinkModel>.Ok(await documents.DownloadLinkAsync(id, Caller)));

    /// <summary>Fleet-wide and partner-wide: every current document (§23A.4's Document Register). The compliance officer's screen.</summary>
    [HttpGet("register")]
    [RequirePermission(PermissionCodes.DOC_REGISTER_VIEW)]
    public async Task<IActionResult> Register([FromQuery] RegisterQuery query) => Ok(ApiResponse<List<RegisterRow>>.Ok(await register.RegisterAsync(query)));

    [HttpGet("missing")]
    [RequirePermission(PermissionCodes.DOC_REGISTER_VIEW)]
    public async Task<IActionResult> Missing([FromQuery] string? ownerType) => Ok(ApiResponse<List<MissingDocumentRow>>.Ok(await register.MissingAsync(ownerType)));

    [HttpGet("expiry-calendar")]
    [RequirePermission(PermissionCodes.DOC_REGISTER_VIEW)]
    public async Task<IActionResult> ExpiryCalendar([FromQuery] int year, [FromQuery] int month) => Ok(ApiResponse<List<CalendarEntry>>.Ok(await register.CalendarAsync(year, month)));

    [HttpGet("types")]
    [RequirePermission(PermissionCodes.DOC_VIEW)]
    public async Task<IActionResult> Types([FromQuery] string? appliesTo) => Ok(ApiResponse<List<DocumentTypeModel>>.Ok(await types.ListAsync(appliesTo)));
}

/// <summary>The document type master (FSD §23A.1), configurable under Administration.</summary>
[ApiController]
[Route("api/admin/document-types")]
public class DocumentTypesController(IDocumentTypeService types) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.ADM_MASTER_MANAGE)]
    public async Task<IActionResult> List() => Ok(ApiResponse<List<DocumentTypeModel>>.Ok(await types.ListAsync()));

    [HttpPost]
    [RequirePermission(PermissionCodes.ADM_MASTER_MANAGE)]
    public async Task<IActionResult> Create([FromBody] SaveDocumentTypeRequest request) =>
        Ok(ApiResponse<DocumentTypeModel>.Ok(await types.CreateAsync(request), "Document type created."));

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionCodes.ADM_MASTER_MANAGE)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveDocumentTypeRequest request) =>
        Ok(ApiResponse<DocumentTypeModel>.Ok(await types.UpdateAsync(id, request), "Document type saved."));
}

/// <summary>A manual trigger for the nightly documents job (BR-DOC-002, BR-DOC-003), the same pattern S4-REC-05 set for a job with no scheduler.</summary>
[ApiController]
[Route("api/admin/jobs/documents")]
public class DocumentJobController(IDocumentJobsRunner runner) : ControllerBase
{
    [HttpPost("run")]
    [RequirePermission(PermissionCodes.ADM_CONFIG_MANAGE)]
    public async Task<IActionResult> Run() => Ok(ApiResponse<DocumentJobsResult>.Ok(await runner.RunAsync()));
}
