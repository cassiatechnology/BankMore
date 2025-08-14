using System.Data;
using System.Text;
using BankMore.Transferencia.Application;
using Dapper;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

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

var app = builder.Build();
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
