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
        // googletagmanager: Google Analytics (Consent Mode v2) loaded by the landing page.
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' https://www.googletagmanager.com",
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
        "img-src 'self' data: https:",
        "font-src 'self' data: https://fonts.gstatic.com",
        "connect-src 'self' https://localhost:* http://localhost:* wss://localhost:* ws://localhost:* https://www.schemastore.org https://www.googletagmanager.com https://www.google-analytics.com https://*.google-analytics.com https://*.analytics.google.com",
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

// Marketing sub-pages (website-overhaul §D2) ship as static .html siblings of
// index.html but are linked with clean, extensionless URLs (e.g. /solutions).
// UseStaticFiles only serves the literal /solutions.html, and GitHub Pages
// resolves the extensionless form for us — this middleware gives the container
// the same behaviour so both hosts answer the same links.
var marketingPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "/wallet-info", "/designer-overview", "/solutions", "/compare", "/developers", "/contact"
};
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is not null && marketingPages.Contains(path.TrimEnd('/')))
    {
        var fileName = path.Trim('/') + ".html";
        var filePath = Path.Combine(app.Environment.WebRootPath, fileName);
        if (File.Exists(filePath))
        {
            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(filePath);
            return;
        }
    }
    await next();
});

// URL rewriting: map /app/* to root for static web assets.
// The Blazor WASM client (Sorcha.UI.Web.Client) is served under /app/ while
// the marketing landing page lives at /. Files like the framework bundle,
// scoped-CSS bundles, and content assets all live in wwwroot/, but the
// browser requests them with the /app/ prefix. Each rewrite maps a
// browser-visible /app/<path> back to the on-disk wwwroot/<path>.
var rewriteOptions = new RewriteOptions()
    .AddRewrite(@"^app/_framework/(.*)$", "_framework/$1", skipRemainingRules: true)
    .AddRewrite(@"^app/_content/(.*)$", "_content/$1", skipRemainingRules: true)
    .AddRewrite(@"^app/Sorcha\.UI\.Web\.styles\.css$", "Sorcha.UI.Web.styles.css", skipRemainingRules: true)
    // Blazor scoped-CSS bundle. The file name embeds a build-time content
    // hash (Sorcha.UI.Web.Client.<hash>.bundle.scp.css) that changes with
    // every build, so the rule has to match the pattern, not a literal name.
    .AddRewrite(@"^app/(Sorcha\.UI\.Web\.Client\.[^/]+\.bundle\.scp\.css)$", "$1", skipRemainingRules: true)
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
