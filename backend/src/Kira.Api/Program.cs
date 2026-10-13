using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Kira.Application;
using Kira.Application.Features.Auth;
using Kira.Infrastructure;
using Kira.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Kira.Domain.Rules;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Options ----------
builder.Services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section))
    .Validate(o => o.Key.Length >= 32, "Jwt:Key must be at least 32 characters. Set it with user-secrets or the Jwt__Key environment variable.")
    .ValidateOnStart();
builder.Services.Configure<AuthOptions>(config.GetSection(AuthOptions.Section));
builder.Services.Configure<KiraRules>(config.GetSection(KiraRules.Section));
builder.Services.Configure<UploadOptions>(config.GetSection(UploadOptions.Section));
var maxUploadMb = config.GetValue("Uploads:MaxFileMb", 25);

// ---------- Layers ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

// ---------- HTTP ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
    c.ProblemDetails.Extensions["traceId"] = c.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ExceptionHandler>();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = (maxUploadMb + 5) * 1024L * 1024L);
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = (maxUploadMb + 5) * 1024L * 1024L;
    k.AddServerHeader = false;
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Behind nginx or a load balancer that terminates TLS.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// ---------- Authentication: short-lived JWT access tokens ----------
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = jwt.SigningKey(),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
});
// Every endpoint requires a signed-in user unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---------- Rate limiting ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    static string Who(HttpContext c) => c.User.FindFirst("sub")?.Value ?? Ip(c);
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Who(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RatePolicies.Auth, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RatePolicies.Ai, c => RateLimitPartition.GetTokenBucketLimiter(Who(c),
        _ => new TokenBucketRateLimiterOptions { TokenLimit = 20, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { status = 429, title = "Too many requests", detail = "You're going a bit fast. Wait a moment and try again." }, ct);
    };
});

// ---------- CORS (only needed when the web app is served from a different origin) ----------
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    .WithExposedHeaders("Content-Disposition")));

// ---------- OpenAPI and health ----------
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<KiraDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (config.GetValue("App:HttpsRedirect", false)) app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || config.GetValue("App:ExposeApiDocs", false))
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapControllers();
// When the Angular build is copied into wwwroot, deep links fall back to index.html.
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.Services.InitialiseDatabaseAsync();
await app.RunAsync();

/// <summary>Exposed for integration tests.</summary>
public partial class Program { }
