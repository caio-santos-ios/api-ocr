using api_ocr.src.Responses;
using OpenCvSharp;
using Tesseract;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace api_ocr.src.Services;

public sealed class OcrService : IDisposable
{
    private readonly TesseractEngine _engine;
    private readonly object _lock = new();

    public OcrService(IConfiguration cfg)
    {
        var dataPath = cfg["Tesseract:DataPath"] ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        var arquivo = Path.Combine(dataPath, "por.traineddata");

        if (!File.Exists(arquivo))
            throw new FileNotFoundException($"Modelo do Tesseract não encontrado: {arquivo}");

        _engine = new TesseractEngine(dataPath, "por", EngineMode.LstmOnly);
    }

    public static Mat Preparar(Mat bgr)
    {
        var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        var maior = Math.Max(gray.Width, gray.Height);
        if (maior > 2500)
            Cv2.Resize(gray, gray, new Size(), 2500.0 / maior, 2500.0 / maior, InterpolationFlags.Area);

        using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
        clahe.Apply(gray, gray);
        return gray;
    }
    public static Mat? RecortarEtiqueta(Mat bgr, int limiarB = 132)
    {
        using var lab = new Mat();
        Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
        var canais = Cv2.Split(lab);
        try
        {
            using var suave = new Mat();
            Cv2.GaussianBlur(canais[2], suave, new Size(15, 15), 0);

            using var mask = new Mat();
            Cv2.Threshold(suave, mask, limiarB, 255, ThresholdTypes.Binary);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(31, 31));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);

            Cv2.FindContours(mask, out var contornos, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var areaMin = bgr.Width * bgr.Height * 0.03;
            var candidatos = contornos
                .Select(c => Cv2.BoundingRect(c))
                .Where(rc => rc.Width * rc.Height > areaMin)
                .OrderByDescending(rc => rc.Width * rc.Height)
                .ToList();

            if (candidatos.Count == 0) return null;

            var area = Rect.Inflate(candidatos[0], 10, 10);
            area = Rect.Intersect(area, new Rect(0, 0, bgr.Width, bgr.Height));
            return new Mat(bgr, area).Clone();
        }
        finally
        {
            foreach (var c in canais) c.Dispose();
        }
    }

    public (string Texto, float Confianca) Ler(Mat img, PageSegMode modo)
    {
        Cv2.ImEncode(".bmp", img, out var bytes);
        using var pix = Pix.LoadFromMemory(bytes);
        lock (_lock)
        {
            using var page = _engine.Process(pix, modo);
            return (page.GetText(), page.GetMeanConfidence());
        }
    }

    public (EtiquetaImplante? Dados, string Texto) LerImplante(Mat bgr)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        using (var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8))) clahe.Apply(gray, gray);

        EtiquetaImplante? melhor = null;
        var textoMelhor = "";
        var pontosMelhor = -1;

        var rotacoes = new RotateFlags?[]
        {
            null,
            RotateFlags.Rotate90Counterclockwise,
            RotateFlags.Rotate90Clockwise
        };

        foreach (var rot in rotacoes)
        {
            using var girada = new Mat();
            if (rot is { } r) Cv2.Rotate(gray, girada, r);
            else gray.CopyTo(girada);

            var (texto, _) = Ler(girada, PageSegMode.SparseText);
            var dados = ImplanteParser.Parse(texto);
            var pontos = (dados.Ref is null ? 0 : 1) + (dados.Lot is null ? 0 : 1) + (dados.Dimensao is null ? 0 : 1);

            if (pontos > pontosMelhor)
            {
                melhor = dados;
                textoMelhor = texto;
                pontosMelhor = pontos;
            }

            if (dados.Ref is not null && dados.Lot is not null) break;
        }

        return (pontosMelhor > 0 ? melhor : null, textoMelhor);
    }

    public void Dispose() => _engine.Dispose();
}