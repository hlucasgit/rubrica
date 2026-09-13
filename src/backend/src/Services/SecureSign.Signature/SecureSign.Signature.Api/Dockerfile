FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Services/SecureSign.Signature/SecureSign.Signature.Api/SecureSign.Signature.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# La imagen base no trae NINGUNA fuente instalada — PdfSharpCore
# (IEstampadorVisualDocumento) falla con "No Fonts installed on this
# device!" al intentar dibujar el sello visual de firma sin esto.
# fonts-liberation es compatible en métricas con Arial/Times/Courier y de
# licencia libre; el nombre de familia real que expone es "Liberation Sans"
# (ver EstampadorVisualDocumentoPdf), no "Arial".
RUN apt-get update && apt-get install -y --no-install-recommends fontconfig fonts-liberation \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SecureSign.Signature.Api.dll"]
