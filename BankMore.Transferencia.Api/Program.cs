using BankMore.Transferencia.Application;
using BankMore.Transferencia.Application.Transferencias;
using BankMore.Transferencia.Infrastructure.Db;
using BankMore.Transferencia.Infrastructure.Repositories;
using Dapper;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using System.Text;
using BankMore.Transferencia.Application.ContaCorrente;   // IContaCorrenteClient
using BankMore.Transferencia.Infrastructure.Clients;      // ContaCorrenteClient


var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// Controllers + autorização global
builder.Services.AddControllers(o =>
{
    var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.Filters.Add(new AuthorizeFilter(policy));
});

// Swagger + Bearer
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new() { Title = "BankMore Transferência API", Version = "v1" });
    opt.EnableAnnotations();
    opt.AddSecurityDefinition("Bearer", new()
    {
        Description = "Bearer {token}",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    opt.AddSecurityRequirement(new()
    {
        { new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

// JWT (forçando 403 quando inválido/expirado)
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // em prod: true
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
        options.Events = new JwtBearerEvents
        {
            OnChallenge = ctx => { ctx.HandleResponse(); ctx.Response.StatusCode = 403; return Task.CompletedTask; },
            OnForbidden = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; }
        };
    });
builder.Services.AddAuthorization();

// MediatR (v12+)
builder.Services.AddMediatR(cfgM => cfgM.RegisterServicesFromAssemblies(typeof(AssemblyMarker).Assembly));

// Dapper/SQLite
SqlMapper.AddTypeHandler(new DapperSqliteDecimalHandler());
builder.Services.AddScoped<IDbConnection>(_ =>
{
    var cs = cfg.GetConnectionString("Transferencia")!;
    var conn = new SqliteConnection(cs);
    conn.Open();
    return conn;
});

// DDD Ports & Adapters: a Application depende da interface; a Infrastructure fornece a implementação.
// Aqui conectamos as duas via DI.
builder.Services.AddScoped<ITransferenciaRepository, TransferenciaRepository>();

// Camada Application: porta para chamar a ContaCorrente.Api via HTTP
builder.Services.AddHttpClient<IContaCorrenteClient, ContaCorrenteClient>((sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg.GetSection("ContaCorrente")["BaseUrl"];

    if (string.IsNullOrWhiteSpace(baseUrl))
        throw new InvalidOperationException("Config 'ContaCorrente:BaseUrl' não definida.");

    http.BaseAddress = new Uri(baseUrl, UriKind.Absolute); // ex.: http://localhost:5175
    // (opcional) headers padrões:
    // http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

var app = builder.Build();

// Inicializa o banco (cria diretório/arquivo .db e tabelas executando o SQL embedded)
// Conceito: camada Infra faz bootstrap; aqui no startup apenas disparamos.
// Por que aqui? Executa uma única vez no início da aplicação.
var cs = builder.Configuration.GetConnectionString("Transferencia");
if (string.IsNullOrWhiteSpace(cs))
{
    // Falha de configuração evidente → evita null ref silenciosa depois
    throw new InvalidOperationException("ConnectionStrings:Transferencia não configurada.");
}

// Usa o logger da aplicação para acompanhar a execução (o initializer aceita um callback de log)
DbInitializer.EnsureCreated(cs!, msg => app.Logger.LogInformation(msg));

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { ok = true, service = "transferencia" })).AllowAnonymous();
app.Run();

public sealed class DapperSqliteDecimalHandler : SqlMapper.TypeHandler<decimal>
{
    public override decimal Parse(object value) => Convert.ToDecimal(value);
    public override void SetValue(IDbDataParameter parameter, decimal value) => parameter.Value = value;
}
