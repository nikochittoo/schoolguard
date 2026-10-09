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
builder.Services.AddHostedService<UninstallGate>();

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

// Lista manual
secure.MapGet("/domains", (Blocklist b) => b.All());

secure.MapPost("/domains", async (AddReq r, Blocklist b) =>
{
    var host = Blocklist.Normalize(r.Url);
    if (host is null) return Results.BadRequest(new { error = "URL inválida." });
    if (b.IsBlocked(host)) return Results.Conflict(new { error = "Ya está bloqueado (por el dominio, una categoría o una palabra clave)." });

    bool reachable = await Reach(host);
    if (!reachable && !r.Force)
        return Results.UnprocessableEntity(new { error = "El sitio no responde.", host, canForce = true });

    b.Add(host);
    return Results.Ok(new { host, reachable });
});

secure.MapDelete("/domains/{host}", (string host, Blocklist b) => b.Remove(host) ? Results.Ok() : Results.NotFound());

// Categorías (listas públicas importadas)
secure.MapGet("/categories", (Blocklist b) => b.Categories());

secure.MapPost("/categories/import", async (ImportReq r, Blocklist b, CancellationToken ct) =>
{
    var name = (r.Name ?? "").Trim().ToLowerInvariant();
    if (!Blocklist.ValidCategory(name))
        return Results.BadRequest(new { error = "Nombre inválido: usá minúsculas, números, - o _ (hasta 30 caracteres)." });
    try
    {
        var set = await ListImporter.FetchAsync(r.Url, ct);
        if (set.Count == 0) return Results.BadRequest(new { error = "La lista no tiene dominios reconocibles." });
        b.SetCategory(name, set);
        return Results.Ok(new { name, count = set.Count });
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = "No se pudo descargar la lista: " + e.Message });
    }
});

secure.MapPost("/categories/{name}/enabled", (string name, EnabledReq r, Blocklist b) =>
    b.SetEnabled(name, r.Enabled) ? Results.Ok() : Results.NotFound());
secure.MapDelete("/categories/{name}", (string name, Blocklist b) =>
    b.RemoveCategory(name) ? Results.Ok() : Results.NotFound());

// Lista blanca
secure.MapGet("/whitelist", (Blocklist b) => b.Whitelist());
secure.MapPost("/whitelist", (AddReq r, Blocklist b) =>
{
    var host = Blocklist.Normalize(r.Url);
    if (host is null) return Results.BadRequest(new { error = "URL inválida." });
    return b.AddWhite(host) ? Results.Ok(new { host }) : Results.Conflict(new { error = "Ya estaba en la lista blanca." });
});
secure.MapDelete("/whitelist/{host}", (string host, Blocklist b) => b.RemoveWhite(host) ? Results.Ok() : Results.NotFound());

// Palabras clave
secure.MapGet("/keywords", (Blocklist b) => b.Keywords());
secure.MapPost("/keywords", (KwReq r, Blocklist b) =>
    b.AddKeyword(r.Word) ? Results.Ok() : Results.BadRequest(new { error = "Palabra inválida, muy corta (mínimo 3) o repetida." }));
secure.MapDelete("/keywords/{word}", (string word, Blocklist b) => b.RemoveKeyword(word) ? Results.Ok() : Results.NotFound());

// Registro y desinstalación
secure.MapGet("/logs", (BlockLog l) => l.Recent());

secure.MapPost("/allow-uninstall", () =>
{
    File.WriteAllText(AppPaths.UninstallFlag, DateTime.UtcNow.ToString("o"));
    return Results.Ok(new { minutes = 30 });
});
secure.MapPost("/cancel-uninstall", () =>
{
    try { File.Delete(AppPaths.UninstallFlag); } catch { }
    return Results.Ok();
});

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
record ImportReq(string Name, string Url);
record EnabledReq(bool Enabled);
