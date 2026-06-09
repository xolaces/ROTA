using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Serves the server-authoritative subject catalog (T52) so clients can populate the Bug / Player-report
/// subject pickers. The same catalog is enforced server-side on submission — clients cannot send off-list
/// subjects. Feedback stays open-text but is always filed under <c>FeedbackCategory</c>.
/// </summary>
[ApiController]
[Route("api/subjects")]
[Authorize]
public sealed class SubjectsController : ControllerBase
{
    private readonly ISubjectCatalogProvider _catalog;

    public SubjectsController(ISubjectCatalogProvider catalog) => _catalog = catalog;

    /// <summary>The full subject catalog (bug + report subject lists and the feedback category label).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(SubjectCatalogResponse), StatusCodes.Status200OK)]
    public ActionResult<SubjectCatalogResponse> Get()
        => Ok(new SubjectCatalogResponse
        {
            BugSubjects = _catalog.BugSubjects.Select(s => new SubjectOption { Key = s.Key, Label = s.Label }).ToList(),
            ReportSubjects = _catalog.ReportSubjects.Select(s => new SubjectOption { Key = s.Key, Label = s.Label }).ToList(),
            FeedbackCategory = _catalog.FeedbackCategory,
        });
}
