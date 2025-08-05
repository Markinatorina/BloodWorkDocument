using BloodWorkDocument_API.Models;
using BloodWorkDocument_API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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
            var json = await _bloodWorkDocumentService.GetRawDocument(dto.File.OpenReadStream());
            return Ok(json);
        }

        /* Just commenting this so it makes it into the commit, but we don't need it right now.
        [HttpPost("upload/raw")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> GetRawDocumentAsDictionary([FromForm][Required] BloodWorkUploadDTO dto)
        {
            var tupleList = await _bloodWorkDocumentService.GetRawDocumentAsDictionary(dto.File.OpenReadStream());
            var lines = tupleList.Select(t =>
                $"    {{ \"{EscapeForCSharp(t.Left)}\", \"{EscapeForCSharp(t.Right)}\" }},");
            var result = string.Join("\n", lines);
            return Ok(result);
        }

        private static string EscapeForCSharp(string input)
        {
            if (input == null) return string.Empty;
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }*/
    }
}
