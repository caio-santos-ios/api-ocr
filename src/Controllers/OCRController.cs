using api_ocr.src.Requests;
using api_ocr.src.Responses;
using api_ocr.src.Services;
using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;
using Tesseract;

namespace api_ocr.src.Controllers;

[ApiController]
[Route("api/ocr")]
public class OCRController(OcrService ocr, IWebHostEnvironment env) : ControllerBase
{
    private const long MaxBytes = 10 * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxBytes)]
    public async Task<IActionResult> Create([FromForm] CreateOCRRequest request)
    {
        var foto = request.Photo;
        if (foto.Length == 0 || foto.Length > MaxBytes)
            return BadRequest("Foto vazia ou maior que 10 MB.");
        if (!foto.ContentType.StartsWith("image/"))
            return BadRequest("Envie uma imagem.");

        using var ms = new MemoryStream();
        await foto.CopyToAsync(ms);

        using var bgr = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
        if (bgr.Empty())
            return BadRequest("Não foi possível ler a imagem.");

        using var recorte = OcrService.RecortarEtiqueta(bgr) ?? bgr.Clone();
        using var img = OcrService.Preparar(recorte);

        var (texto, confianca) = ocr.Ler(img, PageSegMode.SingleBlock);
        var etiqueta = EtiquetaParser.Parse(texto);

        var (implante, textoImplante) = ocr.LerImplante(bgr);

        return Ok(new OcrResponse(
            etiqueta,
            implante,
            confianca,
            env.IsDevelopment() ? texto : null,
            env.IsDevelopment() ? textoImplante : null));
    }
}