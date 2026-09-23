using OpenCvSharp;
using Tesseract;
using Rect = OpenCvSharp.Rect;   // Tesseract também tem Rect/Size: evita ambiguidade
using Size = OpenCvSharp.Size;

namespace api_ocr.src.Services;

/// <summary>Palavra lida pelo OCR, com a posição na imagem.</summary>
public readonly record struct Palavra(string Texto, float Confianca, Rect Caixa);

public sealed class OcrService : IDisposable
{
    // Motores separados porque a lista de caracteres permitidos (whitelist) é definida na criação.
    private const string ListaRotulo = "REFLOTSIZEUDMN";
    private const string ListaCodigo = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789./-";

    private readonly Motor _geral;   // texto livre (etiqueta creme, varredura de palavras)
    private readonly Motor _rotulo;  // rótulos das caixas: REF, LOT, SIZE...
    private readonly Motor _codigo;  // valores: REF, LOT, dimensão

    public OcrService(IConfiguration cfg)
    {
        var dataPath = cfg["Tesseract:DataPath"] ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        var arquivo = Path.Combine(dataPath, "por.traineddata");

        if (!File.Exists(arquivo))
            throw new FileNotFoundException($"Modelo do Tesseract não encontrado: {arquivo}");

        _geral = new Motor(dataPath, null);
        _rotulo = new Motor(dataPath, ListaRotulo);
        _codigo = new Motor(dataPath, ListaCodigo);
    }

    // ---------------------------------------------------------------- pré-processamento

    /// <summary>Cinza + limita tamanho + CLAHE. Usado na etiqueta creme.</summary>
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

    /// <summary>
    /// Localiza a etiqueta creme (levemente amarelada) pelo canal b do LAB.
    /// Retorna null se não achar. Ajuste limiarB olhando o recorte gerado.
    /// </summary>
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

    // ---------------------------------------------------------------- leitura

    /// <summary>OCR de texto livre (etiqueta creme).</summary>
    public (string Texto, float Confianca) Ler(Mat img, PageSegMode modo) => _geral.Ler(img, modo);

    /// <summary>Lê o rótulo de uma caixa (uma palavra: REF, LOT, SIZE...).</summary>
    public string LerRotulo(Mat img) => _rotulo.Ler(img, PageSegMode.SingleWord).Texto;

    /// <summary>Lê um valor de uma linha só (REF, LOT, dimensão), só com A-Z, 0-9 e . / -</summary>
    public string LerCodigo(Mat img) => _codigo.Ler(img, PageSegMode.SingleLine).Texto;

    /// <summary>Palavras da imagem com a posição de cada uma.</summary>
    public List<Palavra> Palavras(Mat img, PageSegMode modo) => _geral.Palavras(img, modo);

    public void Dispose()
    {
        _geral.Dispose();
        _rotulo.Dispose();
        _codigo.Dispose();
    }

    // ---------------------------------------------------------------- motor

    private sealed class Motor : IDisposable
    {
        private readonly TesseractEngine _engine;
        private readonly object _lock = new(); // TesseractEngine não é thread-safe

        public Motor(string dataPath, string? whitelist)
        {
            var opcoes = new Dictionary<string, object>();
            if (whitelist is not null)
                opcoes["tessedit_char_whitelist"] = whitelist;

            _engine = new TesseractEngine(dataPath, "por", EngineMode.LstmOnly,
                Array.Empty<string>(), opcoes, false);
        }

        public (string Texto, float Confianca) Ler(Mat img, PageSegMode modo)
        {
            Cv2.ImEncode(".bmp", img, out var bytes); // BMP: não gasta tempo comprimindo imagem grande
            using var pix = Pix.LoadFromMemory(bytes);
            lock (_lock)
            {
                using var page = _engine.Process(pix, modo);
                return (page.GetText(), page.GetMeanConfidence());
            }
        }

        public List<Palavra> Palavras(Mat img, PageSegMode modo)
        {
            Cv2.ImEncode(".bmp", img, out var bytes);
            using var pix = Pix.LoadFromMemory(bytes);
            var lista = new List<Palavra>();

            lock (_lock)
            {
                using var page = _engine.Process(pix, modo);
                using var it = page.GetIterator();
                it.Begin();
                do
                {
                    var texto = it.GetText(PageIteratorLevel.Word);
                    if (string.IsNullOrWhiteSpace(texto)) continue;
                    if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var r)) continue;

                    lista.Add(new Palavra(
                        texto.Trim(),
                        it.GetConfidence(PageIteratorLevel.Word),
                        new Rect(r.X1, r.Y1, r.Width, r.Height)));
                } while (it.Next(PageIteratorLevel.Word));
            }
            return lista;
        }

        public void Dispose() => _engine.Dispose();
    }
}