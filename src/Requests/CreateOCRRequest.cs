using System.ComponentModel.DataAnnotations;

namespace api_ocr.src.Requests;

public class CreateOCRRequest
{
    [Required]
    public IFormFile Photo { get; set; } = default!;
}