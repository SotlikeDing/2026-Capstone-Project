using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class UserDocumentsControllerTests
{
    private readonly Mock<UserDocuments_db> _mockDb;
    private readonly UserDocumentsController _controller;

    private const string ValidPdfDataUrl = "data:application/pdf;base64,JVBERi0xLjQKJcOkw7zDtsO8";

    public UserDocumentsControllerTests()
    {
        _mockDb = new Mock<UserDocuments_db>("DummyConnectionString");
        _controller = new UserDocumentsController(_mockDb.Object);
        SetCaller(1);
    }

    private void SetCaller(int userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetRequirements_ReturnsListFromDb()
    {
        _mockDb.Setup(db => db.GetRequiredDocumentTypes("PH")).ReturnsAsync(new List<DocumentRequirementDto>
        {
            new() { DocumentTypeId = 1, DocumentTypeName = "Eerstehulp Sertifikaat", IsRequired = true }
        });

        var result = await _controller.GetRequirements("PH");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<DocumentRequirementDto>>(okResult.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetDocuments_WhenRoleNotFound_ReturnsNotFound()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(99)).ReturnsAsync(0);

        var result = await _controller.GetDocuments(99);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetDocuments_WhenCallerIsNotOwnerOrAdmin_ReturnsForbidden()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(2); // owned by user 2, caller is user 1

        var result = await _controller.GetDocuments(5);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_WhenCallerIsOwner_ReturnsOk()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(1);
        _mockDb.Setup(db => db.GetDocumentsForUserRole(5)).ReturnsAsync(new List<UserDocumentDto>());

        var result = await _controller.GetDocuments(5);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetDocuments_WhenCallerIsAdmin_ReturnsOkEvenIfNotOwner()
    {
        SetCaller(999, isAdmin: true);
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(1);
        _mockDb.Setup(db => db.GetDocumentsForUserRole(5)).ReturnsAsync(new List<UserDocumentDto>());

        var result = await _controller.GetDocuments(5);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetDocumentSummary_WithoutRoleCode_ReturnsBadRequest()
    {
        var result = await _controller.GetDocumentSummary(5, "");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetDocumentSummary_NeverExposesFileData_ReturnsSummaryDtoOnly()
    {
        _mockDb.Setup(db => db.GetRoleCodeForUserRole(5)).ReturnsAsync("PH");
        _mockDb.Setup(db => db.GetDocumentSummaryForUserRole(5, "PH")).ReturnsAsync(new List<UserDocumentSummaryDto>
        {
            new() { DocumentTypeId = 1, DocumentTypeName = "Eerstehulp Sertifikaat", IsRequired = true, IsUploaded = true, UploadedAt = DateTime.UtcNow }
        });

        var result = await _controller.GetDocumentSummary(5, "PH");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserDocumentSummaryDto>>(okResult.Value);
        Assert.Single(list);
        // UserDocumentSummaryDto has no FileDataUrl property at all - compile-time guarantee it can't leak.
    }

    [Fact]
    public async Task GetDocumentSummary_WhenRoleCodeDoesNotMatchActualRole_ReturnsBadRequest()
    {
        _mockDb.Setup(db => db.GetRoleCodeForUserRole(5)).ReturnsAsync("PH");

        var result = await _controller.GetDocumentSummary(5, "VG");

        Assert.IsType<BadRequestObjectResult>(result);
        _mockDb.Verify(db => db.GetDocumentSummaryForUserRole(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetDocumentSummary_WhenUserRoleNotFound_ReturnsNotFound()
    {
        _mockDb.Setup(db => db.GetRoleCodeForUserRole(99)).ReturnsAsync((string?)null);

        var result = await _controller.GetDocumentSummary(99, "PH");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [InlineData("", ValidPdfDataUrl)] // missing filename
    [InlineData("cert.exe", "data:application/x-msdownload;base64,AAAA")] // disallowed mime type
    public async Task Upload_WithInvalidFile_ReturnsBadRequest(string fileName, string dataUrl)
    {
        var request = new UploadUserDocumentRequest { DocumentTypeId = 1, FileName = fileName, FileDataUrl = dataUrl };

        var result = await _controller.Upload(5, request);

        Assert.IsType<BadRequestObjectResult>(result);
        _mockDb.Verify(db => db.UploadDocument(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Upload_WithOversizedFile_ReturnsBadRequest()
    {
        string oversized = "data:application/pdf;base64," + new string('A', 7_000_001);
        var request = new UploadUserDocumentRequest { DocumentTypeId = 1, FileName = "cert.pdf", FileDataUrl = oversized };

        var result = await _controller.Upload(5, request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Upload_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(2);
        var request = new UploadUserDocumentRequest { DocumentTypeId = 1, FileName = "cert.pdf", FileDataUrl = ValidPdfDataUrl };

        var result = await _controller.Upload(5, request);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task Upload_WithValidRequestFromOwner_ReturnsOk()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(1);
        _mockDb.Setup(db => db.IsValidDocumentTypeForRole(5, 1)).ReturnsAsync(true);
        _mockDb.Setup(db => db.UploadDocument(5, 1, "cert.pdf", ValidPdfDataUrl, 1)).ReturnsAsync(42);
        var request = new UploadUserDocumentRequest { DocumentTypeId = 1, FileName = "cert.pdf", FileDataUrl = ValidPdfDataUrl };

        var result = await _controller.Upload(5, request);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Upload_WhenDocumentTypeNotValidForRole_ReturnsBadRequestAndNeverUploads()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(1);
        _mockDb.Setup(db => db.IsValidDocumentTypeForRole(5, 999)).ReturnsAsync(false);
        var request = new UploadUserDocumentRequest { DocumentTypeId = 999, FileName = "cert.pdf", FileDataUrl = ValidPdfDataUrl };

        var result = await _controller.Upload(5, request);

        Assert.IsType<BadRequestObjectResult>(result);
        _mockDb.Verify(db => db.UploadDocument(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Replace_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        _mockDb.Setup(db => db.GetDocumentOwner(10)).ReturnsAsync((5, 2));
        var request = new ReplaceUserDocumentRequest { FileName = "cert.pdf", FileDataUrl = ValidPdfDataUrl };

        var result = await _controller.Replace(10, request);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task Replace_WhenDocumentNotFound_ReturnsNotFound()
    {
        _mockDb.Setup(db => db.GetDocumentOwner(10)).ReturnsAsync((0, 0));
        var request = new ReplaceUserDocumentRequest { FileName = "cert.pdf", FileDataUrl = ValidPdfDataUrl };

        var result = await _controller.Replace(10, request);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Replace_WithValidRequestFromOwner_ReturnsOk()
    {
        _mockDb.Setup(db => db.GetDocumentOwner(10)).ReturnsAsync((5, 1));
        _mockDb.Setup(db => db.ReplaceDocument(10, "new.pdf", ValidPdfDataUrl, 1)).ReturnsAsync(true);
        var request = new ReplaceUserDocumentRequest { FileName = "new.pdf", FileDataUrl = ValidPdfDataUrl };

        var result = await _controller.Replace(10, request);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Delete_WhenDocumentIsRequiredType_ReturnsBadRequestAndNeverDeletes()
    {
        _mockDb.Setup(db => db.GetDocumentOwner(10)).ReturnsAsync((5, 1));
        _mockDb.Setup(db => db.GetDeletionContext(10)).ReturnsAsync(new DocumentDeletionContext
        {
            UserRoleId = 5,
            DocumentTypeId = 2,
            DocumentTypeName = "Eerstehulp Sertifikaat",
            IsRequired = true
        });

        var result = await _controller.Delete(10);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("verpligte", badRequest.Value!.ToString());
        _mockDb.Verify(db => db.DeleteDocument(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Delete_WhenDocumentIsOptional_DeletesSuccessfully()
    {
        _mockDb.Setup(db => db.GetDocumentOwner(10)).ReturnsAsync((5, 1));
        _mockDb.Setup(db => db.GetDeletionContext(10)).ReturnsAsync(new DocumentDeletionContext
        {
            UserRoleId = 5,
            DocumentTypeId = 9,
            DocumentTypeName = "Bykomende Dokument",
            IsRequired = false
        });
        _mockDb.Setup(db => db.DeleteDocument(10, 1)).ReturnsAsync(true);

        var result = await _controller.Delete(10);

        Assert.IsType<OkObjectResult>(result);
        _mockDb.Verify(db => db.DeleteDocument(10, 1), Times.Once);
    }

    [Fact]
    public async Task Delete_WhenCallerIsNotOwner_ReturnsForbiddenAndNeverChecksRequirement()
    {
        _mockDb.Setup(db => db.GetDocumentOwner(10)).ReturnsAsync((5, 2));

        var result = await _controller.Delete(10);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
        _mockDb.Verify(db => db.GetDeletionContext(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetAuditTrail_WhenCallerIsNotOwnerOrAdmin_ReturnsForbidden()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(2);

        var result = await _controller.GetAuditTrail(5);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetAuditTrail_WhenCallerIsOwner_ReturnsOk()
    {
        _mockDb.Setup(db => db.GetUserRoleOwnerUserId(5)).ReturnsAsync(1);
        _mockDb.Setup(db => db.GetAuditTrail(5)).ReturnsAsync(new List<DocumentAuditEntryDto>());

        var result = await _controller.GetAuditTrail(5);

        Assert.IsType<OkObjectResult>(result);
    }
}
