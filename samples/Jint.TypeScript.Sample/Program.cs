using Jint.TypeScript.Sample;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_000_000);
builder.Services.AddSingleton<PlaygroundRunner>();
builder.Services.AddResponseCompression();
var app = builder.Build();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/examples", () => ExampleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Scripts"),
    Path.Combine(AppContext.BaseDirectory, "Inputs")));
app.MapPost("/api/run", async (RunRequest request, PlaygroundRunner runner, HttpContext context) =>
{
    // A custom header prevents a simple cross-origin execution request from another website.
    if (context.Request.Headers["X-Playground"] != "1") return Results.BadRequest();
    return Results.Json(await runner.RunAsync(request, context.RequestAborted));
});
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.Run();
