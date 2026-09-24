FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["api-ocr.csproj", "./"]
RUN dotnet restore "api-ocr.csproj"

COPY . .
RUN dotnet publish "api-ocr.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Ubuntu 24.04 (noble): leptonica 1.82.0 e tesseract 5.3.4, que casam com o NuGet Tesseract 5.2.0
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        liblept5 \
        libtesseract5 \
        tesseract-ocr-por \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# O wrapper "Tesseract" (NuGet) só procura as bibliotecas nativas em /app/x64
# e com estes nomes exatos. Os symlinks apontam para as libs instaladas pelo apt.
RUN mkdir -p /app/x64 \
    && ln -s /usr/lib/x86_64-linux-gnu/liblept.so.5      /app/x64/libleptonica-1.82.0.so \
    && ln -s /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /app/x64/libtesseract50.so

# Diagnóstico no build: falha se o pacote nativo do OpenCvSharp não foi publicado e
# lista (no log do Render) qualquer biblioteca do sistema que ele precise e não exista.
RUN f="$(find /app -name 'libOpenCvSharpExtern.so' | head -n 1)"; \
    echo "OpenCvSharp nativo: ${f:-NAO ENCONTRADO}"; \
    test -n "$f"; \
    ldd "$f" | grep 'not found' || echo "OpenCvSharp: nenhuma dependencia do sistema faltando"

# Modelo "por" instalado pelo apt (tesseract-ocr-por). Lido em OcrService: cfg["Tesseract:DataPath"]
ENV Tesseract__DataPath=/usr/share/tesseract-ocr/5/tessdata

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "api-ocr.dll"]