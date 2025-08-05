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
        [HttpPost("upload/raw")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> GetRawDocument([FromForm][Required] BloodWorkUploadDTO dto)
        {
            var json = await _bloodWorkDocumentService.GetRawDocument(dto.File.OpenReadStream(), dto.FileName);
            return Ok(json);
        }
        [HttpPost("upload/raw2")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> GetRawDocument2([FromForm][Required] BloodWorkUploadDTO dto)
        {
            var json = await _bloodWorkDocumentService.GetRawDocument2(dto.File.OpenReadStream(), dto.FileName);
            return Ok(json);
        }
        [HttpPost("upload/raw3")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> GetRawDocument3([FromForm][Required] BloodWorkUploadDTO dto, double xThreshold)
        {
            var json = await _bloodWorkDocumentService.GetRawDocument3(dto.File.OpenReadStream(), dto.FileName, xThreshold);
            return Ok(json);
        }
        [HttpPost("upload/raw4")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> GetRawDocument4([FromForm][Required] BloodWorkUploadDTO dto)
        {
            var json = await _bloodWorkDocumentService.GetRawDocument4(dto.File.OpenReadStream(), dto.FileName);
            return Ok(json);
        }
        [HttpPost("upload/raw6")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> GetRawDocument6([FromForm][Required] BloodWorkUploadDTO dto)
        {
            var json = await _bloodWorkDocumentService.GetRawDocument6(dto.File.OpenReadStream());
            return Ok(json);
        }
    }
}
