# ── 1. Frontend React
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ── 2. API ASP.NET Core
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY backend/DocuCentral.Api/DocuCentral.Api.csproj DocuCentral.Api/
RUN dotnet restore DocuCentral.Api/DocuCentral.Api.csproj
COPY backend/DocuCentral.Api/ DocuCentral.Api/
COPY --from=web /web/dist DocuCentral.Api/wwwroot/
RUN dotnet publish DocuCentral.Api/DocuCentral.Api.csproj -c Release -o /app --no-restore

# ── 3. Imagen final
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api /app ./
ENV ASPNETCORE_URLS=http://+:8080 \
    Almacenamiento__RutaRepositorio=/data/repositorio \
    Almacenamiento__RutaEntrada=/data/entrada \
    Almacenamiento__RutaErrores=/data/errores
USER root
RUN mkdir -p /data/repositorio /data/entrada /data/errores && chown -R app:app /data
USER app
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocuCentral.Api.dll"]
