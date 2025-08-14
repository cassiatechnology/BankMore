using System.Data;
using System.Text;
using BankMore.ContaCorrente.Application;
using Dapper;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// 1) Controllers + Autorização global (todas as rotas exigem usuário autenticado)
builder.Services.AddControllers(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});

// 2) Swagger com esquema Bearer (para inserir o token na UI)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new() { Title = "BankMore Conta Corrente API", Version = "v1" });
    opt.EnableAnnotations();
    opt.AddSecurityDefinition("Bearer", new()
    {
        Description = "Insira o token no formato: Bearer {seu_token}",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    opt.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// 3) JWT (mapeando falhas para 403 conforme requisito)
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // em produção, use true
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = cfg["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = cfg["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // Por padrão seria 401; vamos responder 403 como especificado
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Registra o serviço de token JWT (implementação concreta)
builder.Services.AddScoped<BankMore.ContaCorrente.Application.Auth.ITokenService, BankMore.ContaCorrente.Api.Services.JwtTokenService>();

// 4) MediatR (v12+) registrando os handlers do assembly Application
builder.Services.AddMediatR(cfgM =>
    cfgM.RegisterServicesFromAssemblies(typeof(AssemblyMarker).Assembly));

// 5) Dapper/SQLite - registra uma conexão por request
SqlMapper.AddTypeHandler(new DapperSqliteDecimalHandler()); // opcional (decimais no SQLite)
builder.Services.AddScoped<IDbConnection>(_ =>
{
    var cs = cfg.GetConnectionString("ContaCorrente")!;
    var conn = new SqliteConnection(cs);
    conn.Open();
    return conn;
});

var app = builder.Build();

// Swagger (em Dev sempre ligado; em Prod pode condicionar por env)
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health aberto (se quiser 100% fechado, remova AllowAnonymous)
app.MapGet("/health", () => Results.Ok(new { ok = true, service = "conta-corrente" }))
   .AllowAnonymous();

app.Run();

/// <summary>
/// Handler opcional para decimal no SQLite (evita conversões imprecisas).
/// </summary>
public sealed class DapperSqliteDecimalHandler : SqlMapper.TypeHandler<decimal>
{
    public override decimal Parse(object value) => Convert.ToDecimal(value);
    public override void SetValue(IDbDataParameter parameter, decimal value) => parameter.Value = value;
}
