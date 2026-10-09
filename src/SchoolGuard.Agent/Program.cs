using System.Reflection;
using SchoolGuard.Agent;
using SchoolGuard.Common;

var cfg = AppConfig.Load();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory // un servicio arranca en System32; hay que fijar la raíz
});
builder.Services.AddWindowsService(o => o.ServiceName = "SchoolGuardAgent");
builder.WebHost.UseUrls($"http://127.0.0.1:{cfg.PanelPort}"); // el panel solo se ve desde la propia PC
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton<Blocklist>();
builder.Services.AddSingleton<BlockLog>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddHostedService<DnsServer>();
builder.Services.AddHostedService<DnsEnforcer>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var version = (Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "0.0.0").Split('+')[0];

// ---- público ----
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", version }));
app.MapGet("/api/status", (AuthService a) => Results.Ok(new { hasUsers = a.HasUsers, version }));

app.MapPost("/api/setup", (Cred c, AuthService a) =>
    a.HasUsers || !a.CreateUser(c.User, c.Password)
        ? Results.BadRequest(new { error = "No se pudo crear (ya existe un administrador o la clave tiene menos de 8 caracteres)." })
        : Results.Ok());

app.MapPost("/api/login", (Cred c, AuthService a) =>
    a.Login(c.User, c.Password) is { } t ? Results.Ok(new { token = t }) : Results.Unauthorized());

// ---- protegido por login ----
var secure = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
{
    var auth = ctx.HttpContext.RequestServices.GetRequiredService<AuthService>();
    var h = ctx.HttpContext.Request.Headers.Authorization.ToString();
    var token = h.StartsWith("Bearer ") ? h[7..] : "";
    return auth.Valid(token) ? await next(ctx) : Results.Unauthorized();
});

secure.MapGet("/domains", (Blocklist b) => b.All());

secure.MapPost("/domains", async (AddReq r, Blocklist b) =>
{
    var host = Blocklist.Normalize(r.Url);
    if (host is null) return Results.BadRequest(new { error = "URL inválida." });
    if (b.IsBlocked(host)) return Results.Conflict(new { error = "Ya está bloqueado (por el dominio o una palabra clave)." });

    bool reachable = await Reach(host);
    if (!reachable && !r.Force)
        return Results.UnprocessableEntity(new { error = "El sitio no responde.", host, canForce = true });

    b.Add(host);
    return Results.Ok(new { host, reachable });
});

secure.MapDelete("/domains/{host}", (string host, Blocklist b) => b.Remove(host) ? Results.Ok() : Results.NotFound());
secure.MapGet("/keywords", (Blocklist b) => b.Keywords());
secure.MapPost("/keywords", (KwReq r, Blocklist b) =>
    b.AddKeyword(r.Word) ? Results.Ok() : Results.BadRequest(new { error = "Palabra inválida, muy corta (mínimo 3) o repetida." }));
secure.MapDelete("/keywords/{word}", (string word, Blocklist b) => b.RemoveKeyword(word) ? Results.Ok() : Results.NotFound());
secure.MapGet("/logs", (BlockLog l) => l.Recent());

app.Run();

// Cualquier respuesta HTTP (aunque sea 403) cuenta como "el sitio existe y funciona".
static async Task<bool> Reach(string host)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
    foreach (var scheme in new[] { "https", "http" })
    {
        try { using var r = await http.GetAsync($"{scheme}://{host}", HttpCompletionOption.ResponseHeadersRead); return true; }
        catch { }
    }
    return false;
}

record Cred(string User, string Password);
record AddReq(string Url, bool Force);
record KwReq(string Word);
