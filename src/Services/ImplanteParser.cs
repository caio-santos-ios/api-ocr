using System.Text.RegularExpressions;
using api_ocr.src.Responses;

namespace api_ocr.src.Services;

/// <summary>
/// Regex sobre texto corrido. Serve de último recurso para REF/LOT e para a dimensão
/// quando a leitura por posição (ImplanteReader) não encontra.
/// </summary>
public static partial class ImplanteParser
{
    // (?![A-Za-z]) em vez de \b: aceita "REF:" e também "REF02.23" colado.
    // [ \t]* (e não \s*) evita pular de linha e pegar o texto da linha seguinte.
    [GeneratedRegex(@"\bREF(?![A-Za-z])\.?[ \t]*[:.\-]?[ \t]*([A-Z0-9][A-Z0-9.\-/]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex RxRef();

    [GeneratedRegex(@"\bLOTE?(?![A-Za-z])\.?[ \t]*[:.\-]?[ \t]*([A-Z0-9][A-Z0-9.\-/]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex RxLot();

    // dimensão dentro de texto corrido: exige "mm" para não confundir com outros números
    [GeneratedRegex(@"(\d{1,2}[.,]\d{1,2})\s*[x×]\s*(\d{1,2}(?:[.,]\d{1,2})?)\s*mm", RegexOptions.IgnoreCase)]
    private static partial Regex RxDim();

    // valor lido da caixa SIZE (já em maiúsculas e sem espaços): "3.75X11.5", "4.3X11.5MM"
    [GeneratedRegex(@"(\d{1,2}[.,]\d{1,2})[X×](\d{1,2}(?:[.,]\d{1,2})?)")]
    private static partial Regex RxDimCaixa();

    private static string? Pega(Regex rx, string texto)
    {
        var m = rx.Match(texto);
        return m.Success ? m.Groups[1].Value.Trim('.', '-', '/') : null;
    }

    private static string Formatar(Match m) =>
        $"{m.Groups[1].Value.Replace('.', ',')} × {m.Groups[2].Value.Replace('.', ',')} mm";

    public static EtiquetaImplante Parse(string texto)
    {
        var d = RxDim().Match(texto);
        return new EtiquetaImplante(
            Ref: Pega(RxRef(), texto),
            Lot: Pega(RxLot(), texto),
            Dimensao: d.Success ? Formatar(d) : null);
    }

    public static string? NormalizarDimensao(string? valorDaCaixa)
    {
        if (string.IsNullOrWhiteSpace(valorDaCaixa)) return null;
        var m = RxDimCaixa().Match(valorDaCaixa.ToUpperInvariant());
        return m.Success ? Formatar(m) : null;
    }
}