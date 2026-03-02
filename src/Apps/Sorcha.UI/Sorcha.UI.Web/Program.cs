// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Enable static web assets in development (serves _content from NuGet packages)
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseStaticWebAssets();
}

// Configure Data Protection to use shared volume in Docker
var dataProtectionPath = Path.Combine("/home/app/.aspnet/DataProtection-Keys");
if (Directory.Exists("/home/app/.aspnet"))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}

// Add HttpClient for backend API calls
builder.Services.AddHttpClient("BackendApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Configure ForwardedHeaders for Docker/reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders();

// Security headers
app.Use(async (context, next) =>
{
    var csp = string.Join("; ", new[]
    {
        "default-src 'self'",
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval'",
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
        "img-src 'self' data: https:",
        "font-src 'self' data: https://fonts.gstatic.com",
        "connect-src 'self' https://localhost:* http://localhost:* wss://localhost:* ws://localhost:* https://www.schemastore.org",
        "worker-src 'self' blob:",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        "form-action 'self'"
    });

    context.Response.Headers["Content-Security-Policy"] = csp;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// Handle root URL first - serve landing page directly
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        context.Response.ContentType = "text/html";
        var landingPath = Path.Combine(app.Environment.WebRootPath, "index.html");
        await context.Response.SendFileAsync(landingPath);
        return;
    }
    await next();
});

// URL rewriting: map /app/* to root for static web assets
var rewriteOptions = new RewriteOptions()
    .AddRewrite(@"^app/_framework/(.*)$", "_framework/$1", skipRemainingRules: true)
    .AddRewrite(@"^app/_content/(.*)$", "_content/$1", skipRemainingRules: true)
    .AddRewrite(@"^app/Sorcha\.UI\.Web\.styles\.css$", "Sorcha.UI.Web.styles.css", skipRemainingRules: true)
    .AddRewrite(@"^app/appsettings\.(.*)$", "appsettings.$1", skipRemainingRules: true)
    .AddRewrite(@"^app/i18n/(.*)$", "i18n/$1", skipRemainingRules: true);
app.UseRewriter(rewriteOptions);

// Serve Blazor framework files
app.UseBlazorFrameworkFiles();

// Serve static files (landing page, _content, custom assets)
app.UseStaticFiles();

app.UseRouting();

// SPA fallback for /app/* routes
app.MapFallbackToFile("/app/{**path}", "app/index.html");

app.Run();
