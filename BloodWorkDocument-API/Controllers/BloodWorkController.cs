using BloodWorkDocument_API.Models;
using BloodWorkDocument_API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Threading;

namespace BloodWorkDocument_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BloodWorkController : ControllerBase
    {
        private readonly BloodWorkDocumentService _bloodWorkDocumentService;
        public BloodWorkController(BloodWorkDocumentService bloodWorkDocumentService)
        {
            _bloodWorkDocumentService = bloodWorkDocumentService;
        }

        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadBloodWorkDocument([FromForm][Required] BloodWorkUploadDTO dto)
        {
            var json = await _bloodWorkDocumentService.ExtractToJsonAsync(dto.File.OpenReadStream(), dto.FileName);
            return Ok(json);
        }
    }
}
