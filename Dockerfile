FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["api-ocr.csproj", "./"]

RUN dotnet restore "api-ocr.csproj"

COPY . .

RUN dotnet publish "api-ocr.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

RUN apt-get update \
    && apt-get install -y \
        libleptonica-dev \
        libtesseract-dev \
        tesseract-ocr \
        tesseract-ocr-por \
        libglib2.0-0 \
        libsm6 \
        libxext6 \
        libxrender1 \
    && rm -rf /var/lib/apt/lists/*

RUN ln -s /usr/lib/x86_64-linux-gnu/liblept.so.5 \
    /usr/local/lib/libleptonica-1.82.0.so \
    && ldconfig

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "api-ocr.dll"]