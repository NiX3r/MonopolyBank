using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace MonopolyBank.E2E;

/// <summary>
/// Publishes the app (Release) once and serves it as plain static files, the same way IIS does,
/// plus a fake ping endpoint that counts hits. Set MB_BASE_URL to test an already deployed site instead.
/// </summary>
[SetUpFixture]
public sealed class AppServer
{
    public static string BaseUrl { get; private set; } = "";
    public static int Pings;
    public static long LastPingBodyLength = -1;
    public static bool IsLocal { get; private set; }

    private static WebApplication? _app;

    [OneTimeSetUp]
    public async Task Start()
    {
        var external = Environment.GetEnvironmentVariable("MB_BASE_URL");
        if (!string.IsNullOrWhiteSpace(external))
        {
            BaseUrl = external.TrimEnd('/') + "/";
            return;
        }

        var root = FindRepoRoot();
        var outDir = Path.Combine(root, "artifacts", "e2e-site");
        await Publish(Path.Combine(root, "src", "MonopolyBank", "MonopolyBank.csproj"), outDir);
        var wwwroot = Path.Combine(outDir, "wwwroot");

        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".webmanifest"] = "application/manifest+json";
        types.Mappings[".dat"] = "application/octet-stream";
        types.Mappings[".blat"] = "application/octet-stream";
        types.Mappings[".wasm"] = "application/wasm";
        var files = new PhysicalFileProvider(wwwroot);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        _app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
        _app.MapPost("/api/ping", async (HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            LastPingBodyLength = (await reader.ReadToEndAsync()).Length;
            Interlocked.Increment(ref Pings);
            return Results.NoContent();
        });
        _app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseUrl = address.TrimEnd('/') + "/";
        IsLocal = true;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    private static async Task Publish(string project, string outDir)
    {
        var psi = new ProcessStartInfo("dotnet", ["publish", project, "-c", "Release", "-o", outDir, "--nologo"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"dotnet publish failed:\n{await stdout}\n{await stderr}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MonopolyBank.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("MonopolyBank.slnx not found");
    }
}
