# ===== STAGE 1: build/publish =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiando somente os .csproj primeiro (cache do restore)
COPY BankMore.ContaCorrente.Api/BankMore.ContaCorrente.Api.csproj BankMore.ContaCorrente.Api/
COPY BankMore.ContaCorrente.Application/BankMore.ContaCorrente.Application.csproj BankMore.ContaCorrente.Application/
COPY BankMore.ContaCorrente.Domain/BankMore.ContaCorrente.Domain.csproj BankMore.ContaCorrente.Domain/
COPY BankMore.ContaCorrente.Infrastructure/BankMore.ContaCorrente.Infrastructure.csproj BankMore.ContaCorrente.Infrastructure/

# Restaurando dependências
RUN dotnet restore BankMore.ContaCorrente.Api/BankMore.ContaCorrente.Api.csproj

# Copiando o restante do código do repo
COPY . .

# Publicando em Release
RUN dotnet publish BankMore.ContaCorrente.Api/BankMore.ContaCorrente.Api.csproj -c Release -o /app/publish

# ===== STAGE 2: runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5175 \
    ASPNETCORE_ENVIRONMENT=Development

RUN mkdir -p /app/data
COPY --from=build /app/publish .
EXPOSE 5175
ENTRYPOINT ["dotnet", "BankMore.ContaCorrente.Api.dll"]
