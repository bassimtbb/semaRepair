# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore dependencies first (cached layer — only re-runs if .csproj changes)
COPY SemaRepair.Api.csproj .
RUN dotnet restore

# Copy source and publish
COPY . .
RUN dotnet publish SemaRepair.Api.csproj -c Release -o /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SemaRepair.Api.dll"]
