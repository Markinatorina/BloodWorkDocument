namespace BloodWorkDocument_API.Models
{
    public class BloodWorkUploadDTO
    {
        public required string FileName { get; set; }
        public required IFormFile File { get; set; }
    }
}
