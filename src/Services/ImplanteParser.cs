using System.Text.RegularExpressions;
using api_ocr.src.Responses;

namespace api_ocr.src.Services;

public static partial class ImplanteParser
{
    [GeneratedRegex(@"\bREF(?![A-Za-z])\.?[ \t]*[:.\-]?[ \t]*([A-Z0-9][A-Z0-9.\-/]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex RxRef();

    [GeneratedRegex(@"\bLOTE?(?![A-Za-z])\.?[ \t]*[:.\-]?[ \t]*([A-Z0-9][A-Z0-9.\-/]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex RxLot();

    [GeneratedRegex(@"(\d{1,2}[.,]\d)\s*[x×]\s*(\d{1,2}(?:[.,]\d)?)\s*mm", RegexOptions.IgnoreCase)]
    private static partial Regex RxDim();

    private static string? Pega(Regex rx, string texto)
    {
        var m = rx.Match(texto);
        return m.Success ? m.Groups[1].Value.Trim('.', '-', '/') : null;
    }

    public static EtiquetaImplante Parse(string texto)
    {
        var d = RxDim().Match(texto);
        var dimensao = d.Success
            ? $"{d.Groups[1].Value.Replace('.', ',')} × {d.Groups[2].Value.Replace('.', ',')} mm"
            : null;

        return new EtiquetaImplante(
            Ref: Pega(RxRef(), texto),
            Lot: Pega(RxLot(), texto),
            Dimensao: dimensao);
    }
}