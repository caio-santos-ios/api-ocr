namespace api_ocr.src.Responses;

public record EtiquetaCirurgia(
    string? Cirurgiao,
    string? Paciente,
    DateOnly? Data,
    string? Regiao,
    bool RegiaoValida,
    string? Travamento,
    string? Auxiliar,
    string? Observacao,
    int? LiberarDias);

public record EtiquetaImplante(
    string? Ref,
    string? Lot,
    string? Dimensao);

public record OcrResponse(
    EtiquetaCirurgia Etiqueta,
    EtiquetaImplante? Implante,
    float Confianca,
    string? TextoBruto,
    string? TextoBrutoImplante);