# ===== STAGE 1: build/publish =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiando somente os .csproj primeiro
COPY BankMore.Transferencia.Api/BankMore.Transferencia.Api.csproj BankMore.Transferencia.Api/
COPY BankMore.Transferencia.Application/BankMore.Transferencia.Application.csproj BankMore.Transferencia.Application/
COPY BankMore.Transferencia.Domain/BankMore.Transferencia.Domain.csproj BankMore.Transferencia.Domain/
COPY BankMore.Transferencia.Infrastructure/BankMore.Transferencia.Infrastructure.csproj BankMore.Transferencia.Infrastructure/

# Restaurando
RUN dotnet restore BankMore.Transferencia.Api/BankMore.Transferencia.Api.csproj

# Copiando o restante do repo
COPY . .

# Publicando
RUN dotnet publish BankMore.Transferencia.Api/BankMore.Transferencia.Api.csproj -c Release -o /app/publish

# ===== STAGE 2: runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5039 \
    ASPNETCORE_ENVIRONMENT=Development

RUN mkdir -p /app/data
COPY --from=build /app/publish .
EXPOSE 5039
ENTRYPOINT ["dotnet", "BankMore.Transferencia.Api.dll"]
