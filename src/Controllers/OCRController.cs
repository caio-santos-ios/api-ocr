using api_ocr.src.Requests;
using api_ocr.src.Responses;
using api_ocr.src.Services;
using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;
using Tesseract;

namespace api_ocr.src.Controllers;

[ApiController]
[Route("api/ocr")] // mantenha o [Route] que você já usava, se for outro
public class OCRController(OcrService ocr, ImplanteReader implantes, IWebHostEnvironment env) : ControllerBase
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

        // ImDecode já aplica a orientação EXIF (foto de celular em pé)
        using var bgr = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
        if (bgr.Empty())
            return BadRequest("Não foi possível ler a imagem.");

        // 1) etiqueta creme (cirurgião, paciente, data, região...)
        using var recorte = OcrService.RecortarEtiqueta(bgr) ?? bgr.Clone(); // fallback: foto inteira
        using var img = OcrService.Preparar(recorte);

        // Só para ajustar o limiar do recorte. A imagem contém dado de paciente: apague depois.
        // if (env.IsDevelopment()) Cv2.ImWrite("debug-etiqueta.png", recorte);

        var (texto, confianca) = ocr.Ler(img, PageSegMode.SingleBlock);
        var etiqueta = EtiquetaParser.Parse(texto);

        // 2) etiqueta do fabricante do implante (REF, LOT, dimensão)
        var (implante, diagnostico) = implantes.Ler(bgr);

        // texto bruto e diagnóstico só em desenvolvimento: podem conter dado de paciente
        return Ok(new OcrResponse(
            etiqueta,
            implante,
            confianca,
            env.IsDevelopment() ? texto : null,
            env.IsDevelopment() ? diagnostico : null));
    }
}