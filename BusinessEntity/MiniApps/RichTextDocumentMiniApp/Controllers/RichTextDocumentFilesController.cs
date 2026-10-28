using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.Services;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Controllers
{
    // Отдает embedded-файлы rich-text документа из технического storage.
    [ApiController]
    [Route("rich-document-files")]
    public class RichTextDocumentFilesController : ControllerBase
    {
        private readonly RichTextFileService RichTextFileService;
        private readonly IUserConnector _users;

        public RichTextDocumentFilesController(RichTextFileService fileService, IUserConnector users)
        {
            RichTextFileService = fileService; _users = users;
        }

        // Возвращает embedded-изображение rich-text документа по imageId и variant.
        [HttpGet("{documentId:guid}/images/{imageId}/{variant}")]
        public async Task<IActionResult> GetImage(Guid documentId, string imageId, string variant, CancellationToken cancellationToken)
        {
            var file = await RichTextFileService.GetRichTextEmbeddedFileAsync(documentId, imageId, variant, cancellationToken);
            if (file == null)
            {
                return NotFound();
            }

            return File(file.Content, file.ContentType, file.FileName);
        }

        // Загружает одно embedded-изображение rich-text документа.
        [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("{documentId:guid}/images")]
        [RequestSizeLimit(20L * 1024L * 1024L)]
        public async Task<IActionResult> UploadImage(Guid documentId, IFormFile file, CancellationToken cancellationToken)
        {
            if (!(await _users.GetEntityContentAccessAsync(documentId, cancellationToken)).CanEditDraft) return Forbid();
            if (file == null || file.Length <= 0)
            {
                return BadRequest("Файл изображения не передан.");
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await RichTextFileService.SaveRichTextEmbeddedImageAsync(
                    documentId,
                    stream,
                    file.FileName,
                    file.ContentType,
                    cancellationToken);

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
