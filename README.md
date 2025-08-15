# 🏦 BankMore – Conta Corrente e Transferências

Este projeto é composto por dois microsserviços desenvolvidos em **.NET 8** que simulam operações de conta corrente e transferências bancárias, seguindo princípios de arquitetura limpa, padrões de projeto e boas práticas de desenvolvimento.

## 📌 Visão Geral

- **BankMore.ContaCorrente.Api**  
  API para cadastro, login, movimentações e consulta de saldo.

- **BankMore.Transferencia.Api**  
  API para realizar transferências entre contas.

- Banco de dados local com **SQLite**.
- Containers orquestrados via **Docker Compose**.
- Documentação e teste de endpoints via **Swagger**.

---

## 🏗 Arquitetura e Padrões

- **Clean Architecture - DDD** (camadas `Api`, `Application`, `Domain` e `Infrastructure`)
- **CQRS** com **MediatR**
- **DTOs** para leitura e escrita
- **Records** para imutabilidade
- **Injeção de dependência** nativa do .NET
- **Autenticação JWT** com ASP.NET Core Identity simplificado
- **Idempotência** em movimentações e transferências

---

## 🚀 Executando o Projeto

### 1. Requisitos
- [Docker Desktop](https://www.docker.com/products/docker-desktop) instalado e ativo.
- Porta **5175** livre para a API de Conta Corrente.
- Porta **5039** livre para a API de Transferências.

### 2. Rodando com Docker
Na raiz do projeto, execute:

```bash
docker compose up --build -d
```

Isso irá subir:
- **contacorrente-api** → http://localhost:5175/swagger
- **transferencia-api** → http://localhost:5039/swagger

### 3. Rodando localmente (sem Docker)
- Abrir a solução no **Visual Studio 2022** ou via terminal:
```bash
dotnet run --project BankMore.ContaCorrente.Api
dotnet run --project BankMore.Transferencia.Api
```

---

## 🧪 Testes Automatizados

Para rodar os testes:

```bash
dotnet test
```

---

## 📬 Endpoints e Exemplos

### 1. Cadastrar Conta
```bash
curl -X POST http://localhost:5175/api/conta-corrente/cadastro   -H "Content-Type: application/json"   -d '{"cpf":"12345678900","senha":"minhasenha"}'
```

### 2. Login
```bash
curl -X POST http://localhost:5175/api/conta-corrente/login   -H "Content-Type: application/json"   -d '{"cpfOuConta":"12345678900","senha":"minhasenha"}'
```
> Retorna um **JWT** para autenticação.

### 3. Consultar Saldo
```bash
curl -X GET http://localhost:5175/api/conta-corrente/saldo   -H "Authorization: Bearer SEU_TOKEN"
```

### 4. Movimentar Conta
```bash
curl -X POST http://localhost:5175/api/conta-corrente/movimentacoes   -H "Authorization: Bearer SEU_TOKEN"   -H "Content-Type: application/json"   -d '{"idempotencyKey":"abc123","tipo":"C","valor":100.50}'
```

### 5. Inativar Conta
```bash
curl -X POST http://localhost:5175/api/conta-corrente/inativar   -H "Authorization: Bearer SEU_TOKEN"   -H "Content-Type: application/json"   -d '{"senha":"minhasenha"}'
```

### 6. Efetuar Transferência
```bash
curl -X POST http://localhost:5039/api/transferencias   -H "Authorization: Bearer SEU_TOKEN"   -H "Content-Type: application/json"   -d '{"idempotencyKey":"xyz789","numeroContaDestino":"0002","valor":50}'
```

---

## 🛠 Troubleshooting
- **Portas ocupadas** → Feche aplicações que usam as portas 5175 e 5039.
- **Banco corrompido** → Apague a pasta `/data` e suba os containers novamente.
- **Erro de chave JWT** → Verifique se a variável `JWT_KEY` está configurada no `docker-compose.yml` ou no `appsettings.json`.

---

## 📅 Próximos Passos
- Adicionar integração com API externa para validação de CPF.
- Implementar testes de integração com banco em memória.
- Adicionar monitoramento com Prometheus + Grafana.

---
💻 **Desenvolvido por Danilo Ferreira de Cássia**
