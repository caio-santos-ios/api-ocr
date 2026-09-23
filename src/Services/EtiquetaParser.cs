using System.Globalization;
using System.Text.RegularExpressions;
using api_ocr.src.Responses;

namespace api_ocr.src.Services;

public static partial class EtiquetaParser
{
    [GeneratedRegex(@"^\s*(DRA?\.?\s+.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex RxCirurgiao();

    [GeneratedRegex(@"Paciente:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex RxPaciente();

    [GeneratedRegex(@"Data:\s*(\d{2})[/.\-](\d{2})[/.\-](\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex RxData();

    [GeneratedRegex(@"Regi[aã]o:\s*(\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex RxRegiao();

    [GeneratedRegex(@"Trav\w*:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex RxTrav();

    [GeneratedRegex(@"Aux\w*:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex RxAux();

    [GeneratedRegex(@"Aux\w*:[^\n]*\n(?<obs>[\s\S]*?)(?=Liberar|$)", RegexOptions.IgnoreCase)]
    private static partial Regex RxObs();

    [GeneratedRegex(@"Liberar\s+(\d+)\s*dias", RegexOptions.IgnoreCase)]
    private static partial Regex RxLiberar();

    private static readonly HashSet<int> Fdi = new(
        Enumerable.Range(1, 4).SelectMany(q => Enumerable.Range(1, 8).Select(n => q * 10 + n)));

    private static string? Pega(Regex rx, string texto)
    {
        var m = rx.Match(texto);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    public static EtiquetaCirurgia Parse(string texto)
    {
        DateOnly? data = null;
        var md = RxData().Match(texto);
        if (md.Success &&
            DateOnly.TryParseExact(
                $"{md.Groups[1].Value}/{md.Groups[2].Value}/{md.Groups[3].Value}",
                "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            data = dt;
        }

        var regiao = Pega(RxRegiao(), texto);
        var regiaoValida = int.TryParse(regiao, out var dente) && Fdi.Contains(dente);

        string? observacao = null;
        var mo = RxObs().Match(texto);
        if (mo.Success)
        {
            var v = mo.Groups["obs"].Value.Trim();
            if (v.Length > 0) observacao = v;
        }

        int? liberarDias = null;
        var ml = RxLiberar().Match(texto);
        if (ml.Success && int.TryParse(ml.Groups[1].Value, out var dias))
            liberarDias = dias;

        return new EtiquetaCirurgia(
            Cirurgiao: Pega(RxCirurgiao(), texto),
            Paciente: Pega(RxPaciente(), texto),
            Data: data,
            Regiao: regiao,
            RegiaoValida: regiaoValida,
            Travamento: Pega(RxTrav(), texto),
            Auxiliar: Pega(RxAux(), texto),
            Observacao: observacao,
            LiberarDias: liberarDias);
    }
}