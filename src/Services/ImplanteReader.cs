using System.Text.RegularExpressions;
using api_ocr.src.Responses;
using OpenCvSharp;
using Tesseract;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace api_ocr.src.Services;

/// <summary>
/// Lê REF, LOT e dimensão da etiqueta do fabricante do implante.
///
/// Como funciona (validado em Python nas 5 fotos de exemplo):
/// 1. As etiquetas costumam estar giradas 90° no sentido horário, então a foto é girada 90° anti-horário.
/// 2. Acha as caixinhas com borda ("REF", "LOT", "SIZE"...) pela geometria, e não pelo OCR da página.
/// 3. Lê o rótulo dentro de cada caixa (poucas letras, com votação entre recortes).
/// 4. Recorta só a linha do valor ao lado da caixa, normaliza a altura e lê com A-Z/0-9 (votação).
/// 5. Etiquetas sem caixa (ex.: Dérig): acha as palavras "REF"/"LOT" no texto e usa a posição delas.
/// </summary>
public sealed partial class ImplanteReader(OcrService ocr)
{
    private static readonly RotateFlags?[] Rotacoes =
    [
        RotateFlags.Rotate90Counterclockwise,
        null,
        RotateFlags.Rotate90Clockwise
    ];

    private static readonly string[] Chaves = ["REF", "LOT", "SIZE", "UDI", "MD"];

    [GeneratedRegex(@"[^A-Z0-9./\-]")]
    private static partial Regex ForaDoCodigo();

    // ------------------------------------------------------------------ entrada

    public (EtiquetaImplante? Dados, string Diagnostico) Ler(Mat bgr)
    {
        using var cinza0 = new Mat();
        Cv2.CvtColor(bgr, cinza0, ColorConversionCodes.BGR2GRAY);

        var melhor = new Achados();
        var diagnostico = "nada encontrado";

        foreach (var rot in Rotacoes)
        {
            using var cinza = Girar(cinza0, rot);
            var achados = LerRotacao(cinza, out var log);

            if (achados.Pontos > melhor.Pontos)
            {
                melhor = achados;
                diagnostico = $"rotação={NomeRotacao(rot)}; {log}";
            }

            if (achados.Ref is not null && achados.Lot is not null) break;
        }

        if (melhor.Pontos == 0) return (null, diagnostico);
        return (new EtiquetaImplante(melhor.Ref, melhor.Lot, melhor.Dimensao), diagnostico);
    }

    private Achados LerRotacao(Mat cinza, out string log)
    {
        var achados = new Achados();
        var partes = new List<string>();

        using var realcada = new Mat();
        using (var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8)))
            clahe.Apply(cinza, realcada);

        // 1) caixas com borda em volta de REF / LOT / SIZE
        foreach (var caixa in AcharCaixas(cinza).OrderBy(c => c.Y))
        {
            var chave = ClassificarRotulo(realcada, caixa);
            if (chave is null) continue;
            partes.Add(chave);

            if (chave == "REF" && achados.Ref is null) achados.Ref = LerCampo(cinza, caixa);
            else if (chave == "LOT" && achados.Lot is null) achados.Lot = LerCampo(cinza, caixa);
            else if (chave == "SIZE" && achados.Dimensao is null)
                achados.Dimensao = ImplanteParser.NormalizarDimensao(LerCampo(cinza, caixa));
        }

        // 2) sem caixa: procura as palavras REF / LOT no texto e usa a posição delas
        if (achados.Ref is null || achados.Lot is null)
        {
            var palavras = ocr.Palavras(realcada, PageSegMode.SparseText);

            foreach (var (chave, caixa) in AcharRefLot(palavras))
            {
                partes.Add(chave + "(texto)");
                if (chave == "REF" && achados.Ref is null) achados.Ref = LerCampo(cinza, caixa);
                else if (chave == "LOT" && achados.Lot is null) achados.Lot = LerCampo(cinza, caixa);
            }

            // último recurso: regex no texto corrido
            var regex = ImplanteParser.Parse(string.Join(' ', palavras.Select(p => p.Texto)));
            achados.Ref ??= regex.Ref;
            achados.Lot ??= regex.Lot;
            achados.Dimensao ??= regex.Dimensao;
        }

        log = "encontrado: " + (partes.Count == 0 ? "-" : string.Join(", ", partes));
        return achados;
    }

    // ------------------------------------------------------------------ caixas com borda

    /// <summary>Retângulos pequenos com borda contínua e interior "oco" (onde fica o texto).</summary>
    private static List<Rect> AcharCaixas(Mat cinza)
    {
        var caixas = new List<Rect>();
        var maior = Math.Max(cinza.Width, cinza.Height);

        using var bw = new Mat();
        Cv2.AdaptiveThreshold(cinza, bw, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 51, 15);
        Cv2.FindContours(bw, out var contornos, out var hierarquia,
            RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        for (var i = 0; i < contornos.Length; i++)
        {
            if (hierarquia[i].Parent >= 0) continue; // é um buraco, não um contorno externo

            var r = Cv2.BoundingRect(contornos[i]);
            if (r.Width <= 0.010 * maior || r.Width >= 0.06 * maior) continue;
            if (r.Height <= 0.008 * maior || r.Height >= 0.04 * maior) continue;

            var proporcao = (double)r.Width / r.Height;
            if (proporcao <= 1.1 || proporcao >= 4.0) continue;

            // o maior buraco (interior da caixa) precisa ocupar boa parte do retângulo
            var areaBuraco = 0;
            for (var filho = hierarquia[i].Child; filho >= 0; filho = hierarquia[filho].Next)
            {
                var rf = Cv2.BoundingRect(contornos[filho]);
                areaBuraco = Math.Max(areaBuraco, rf.Width * rf.Height);
            }
            if (areaBuraco < 0.45 * r.Width * r.Height) continue;

            // borda contínua: o contorno externo preenche o retângulo
            if (Cv2.ContourArea(contornos[i]) / ((double)r.Width * r.Height) < 0.80) continue;

            caixas.Add(r);
        }
        return caixas;
    }

    /// <summary>Lê o rótulo da caixa em 4 recortes (com recuos diferentes da borda) e vota.</summary>
    private string? ClassificarRotulo(Mat realcada, Rect caixa)
    {
        var votos = new Dictionary<string, int>();
        var limites = new Rect(0, 0, realcada.Width, realcada.Height);

        foreach (var recuo in new[] { 0.0, 0.06, 0.10, 0.14 })
        {
            var m = (int)(recuo * caixa.Height);
            var area = Rect.Intersect(
                new Rect(caixa.X + m, caixa.Y + m, caixa.Width - 2 * m, caixa.Height - 2 * m), limites);
            if (area.Width < 8 || area.Height < 8) continue;

            using var recorte = new Mat(realcada, area);
            using var ampliado = new Mat();
            Cv2.Resize(recorte, ampliado, new Size(), 2, 2, InterpolationFlags.Cubic);
            using var moldurado = Moldura(ampliado, 20);

            var texto = new string(ocr.LerRotulo(moldurado).Where(char.IsLetter).ToArray()).ToUpperInvariant();
            if (texto.Length == 0) continue;

            // a primeira letra basta para separar REF / LOT / SIZE / UDI / MD; tolera até 2 erros
            var candidatos = Chaves.Where(k => k[0] == texto[0] && Lev(texto, k) <= 2).ToList();
            if (candidatos.Count == 0) continue;

            var escolhido = candidatos.MinBy(k => Lev(texto, k))!;
            votos[escolhido] = votos.GetValueOrDefault(escolhido) + 1;
        }

        return votos.Count == 0 ? null : votos.MaxBy(v => v.Value).Key;
    }

    // ------------------------------------------------------------------ etiquetas sem caixa

    private static Dictionary<string, Rect> AcharRefLot(List<Palavra> palavras)
    {
        var achados = new Dictionary<string, (float Conf, Rect Caixa)>();

        foreach (var p in palavras)
        {
            var t = new string(p.Texto.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            if (t.Length is < 3 or > 4 || p.Confianca <= 0) continue;

            foreach (var chave in new[] { "REF", "LOT" })
            {
                var casa = (t[0] == chave[0] && Lev(t, chave) <= 1) || (chave == "LOT" && t == "LOTE");
                if (casa && (!achados.TryGetValue(chave, out var atual) || p.Confianca > atual.Conf))
                    achados[chave] = (p.Confianca, p.Caixa);
            }
        }
        return achados.ToDictionary(kv => kv.Key, kv => kv.Value.Caixa);
    }

    // ------------------------------------------------------------------ valor ao lado do rótulo

    private string? LerCampo(Mat cinza, Rect caixa)
    {
        using var valor = RecortarValor(cinza, caixa);
        return valor is null ? null : LerCodigo(valor);
    }

    /// <summary>
    /// Recorta só a linha do valor à direita do rótulo. Usa componentes conexos: fica com o que
    /// está na altura da caixa e para na primeira lacuna grande (evita pegar o QR/UDI ao lado).
    /// </summary>
    private static Mat? RecortarValor(Mat cinza, Rect b)
    {
        var x0 = b.X + b.Width + (int)(0.10 * b.Width);
        var x1 = Math.Min(cinza.Width, x0 + 8 * b.Width);
        var y0 = Math.Max(0, b.Y - (int)(0.6 * b.Height));
        var y1 = Math.Min(cinza.Height, b.Y + b.Height + (int)(0.6 * b.Height));
        if (x1 - x0 < 10 || y1 - y0 < 10) return null;

        using var faixa = new Mat(cinza, new Rect(x0, y0, x1 - x0, y1 - y0));
        using var bw = new Mat();
        Cv2.AdaptiveThreshold(faixa, bw, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 41, 12);

        var cc = Cv2.ConnectedComponentsEx(bw, PixelConnectivity.Connectivity8);
        var mantidos = new List<Rect>();
        foreach (var blob in cc.Blobs.Skip(1)) // 0 = fundo
        {
            var centroY = y0 + blob.Top + blob.Height / 2.0;
            if (blob.Area < 15 || blob.Height < 0.18 * b.Height || blob.Height > 1.2 * b.Height) continue;
            if (centroY < b.Y || centroY > b.Y + b.Height) continue; // só o que está na altura da caixa
            mantidos.Add(blob.Rect);
        }
        if (mantidos.Count == 0) return null;

        mantidos.Sort((a, c) => a.X.CompareTo(c.X));

        var lacunaMax = 0.9 * b.Height;
        var grupo = new List<Rect> { mantidos[0] };
        foreach (var c in mantidos.Skip(1))
        {
            var anterior = grupo[^1];
            if (c.X - (anterior.X + anterior.Width) > lacunaMax) break;
            grupo.Add(c);
        }

        var xs = grupo.Min(c => c.X);
        var xe = grupo.Max(c => c.X + c.Width);
        var ys = grupo.Min(c => c.Y);
        var ye = grupo.Max(c => c.Y + c.Height);
        var p = (int)(0.12 * b.Height);

        var area = Rect.Intersect(
            new Rect(xs - p, ys - p, xe - xs + 2 * p, ye - ys + 2 * p),
            new Rect(0, 0, faixa.Width, faixa.Height));

        using var sub = new Mat(faixa, area);
        return sub.Clone();
    }

    /// <summary>
    /// Lê o valor normalizando a altura do recorte (o Tesseract erra menos com o texto em
    /// tamanho "de treino") e vota entre 4 alturas.
    /// </summary>
    private string? LerCodigo(Mat valor)
    {
        var votos = new Dictionary<string, int>();

        foreach (var altura in new[] { 56, 64, 72, 80 })
        {
            var s = (double)altura / valor.Height;
            using var redim = new Mat();
            Cv2.Resize(valor, redim, new Size(), s, s, s > 1 ? InterpolationFlags.Cubic : InterpolationFlags.Area);
            using var moldurado = Moldura(redim, 16);

            var t = ForaDoCodigo().Replace(ocr.LerCodigo(moldurado).ToUpperInvariant(), "");
            if (t.Length > 0) votos[t] = votos.GetValueOrDefault(t) + 1;
        }

        return votos.Count == 0 ? null : votos.MaxBy(v => v.Value).Key;
    }

    // ------------------------------------------------------------------ utilitários

    private static Mat Girar(Mat src, RotateFlags? rot)
    {
        var dst = new Mat();
        if (rot is { } r) Cv2.Rotate(src, dst, r);
        else src.CopyTo(dst);
        return dst;
    }

    private static string NomeRotacao(RotateFlags? rot) => rot switch
    {
        RotateFlags.Rotate90Counterclockwise => "90° anti-horário",
        RotateFlags.Rotate90Clockwise => "90° horário",
        _ => "nenhuma"
    };

    private static Mat Moldura(Mat src, int p)
    {
        var dst = new Mat();
        Cv2.CopyMakeBorder(src, dst, p, p, p, p, BorderTypes.Constant, Scalar.All(255));
        return dst;
    }

    private static int Lev(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var anterior = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var temp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), anterior + (a[i - 1] == b[j - 1] ? 0 : 1));
                anterior = temp;
            }
        }
        return d[b.Length];
    }

    private sealed class Achados
    {
        public string? Ref { get; set; }
        public string? Lot { get; set; }
        public string? Dimensao { get; set; }
        public int Pontos => (Ref is null ? 0 : 1) + (Lot is null ? 0 : 1);
    }
}