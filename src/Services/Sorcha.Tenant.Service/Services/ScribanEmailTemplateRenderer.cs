// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Scriban-backed template renderer. On construction, walks the assembly's embedded
/// resources under <c>Sorcha.Tenant.Service.Emails.Templates.*</c>, parses every
/// <c>.html</c> and <c>.txt</c> file into a Scriban <see cref="Template"/>, and keeps
/// them in memory for the life of the process. Fails fast on parse errors so template
/// authoring mistakes are caught at boot, not at first email.
/// </summary>
public sealed class ScribanEmailTemplateRenderer : IEmailTemplateRenderer
{
    private const string ResourcePrefix = "Sorcha.Tenant.Service.Emails.Templates.";

    private readonly Dictionary<string, Template> _htmlTemplates;
    private readonly Dictionary<string, Template> _textTemplates;
    private readonly Dictionary<string, string> _htmlSources;
    private readonly Dictionary<string, string> _textSources;

    /// <summary>
    /// Initializes the renderer by loading and parsing every embedded template.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when a template fails to parse. The inner message identifies
    /// the template name and parse error.
    /// </exception>
    public ScribanEmailTemplateRenderer()
        : this(typeof(ScribanEmailTemplateRenderer).Assembly)
    {
    }

    internal ScribanEmailTemplateRenderer(Assembly assembly)
    {
        (_htmlTemplates, _htmlSources) = LoadTemplates(assembly, ".html");
        (_textTemplates, _textSources) = LoadTemplates(assembly, ".txt");
    }

    /// <inheritdoc />
    public (string Html, string Text) Render(string templateName, object model)
    {
        if (!_htmlTemplates.TryGetValue(templateName, out var htmlTemplate))
            throw new KeyNotFoundException(
                $"Email template '{templateName}.html' is not registered. " +
                $"Available: {string.Join(", ", _htmlTemplates.Keys)}");

        if (!_textTemplates.TryGetValue(templateName, out var textTemplate))
            throw new KeyNotFoundException(
                $"Email template '{templateName}.txt' is not registered. " +
                $"Available: {string.Join(", ", _textTemplates.Keys)}");

        var htmlContext = BuildContext(model, _htmlSources);
        var html = htmlTemplate.Render(htmlContext);

        var textContext = BuildContext(model, _textSources);
        var text = textTemplate.Render(textContext);

        return (html, text);
    }

    private static (Dictionary<string, Template> templates, Dictionary<string, string> sources)
        LoadTemplates(Assembly assembly, string extension)
    {
        var templates = new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;
            if (!resourceName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            // e.g. "Sorcha.Tenant.Service.Emails.Templates.welcome-public.html"
            //   → "welcome-public"
            var withoutPrefix = resourceName.Substring(ResourcePrefix.Length);
            var withoutExtension = withoutPrefix.Substring(
                0, withoutPrefix.Length - extension.Length);

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();

            var template = Template.Parse(source, sourceFilePath: resourceName);
            if (template.HasErrors)
            {
                var errors = string.Join("; ", template.Messages);
                throw new InvalidOperationException(
                    $"Failed to parse email template '{withoutExtension}{extension}': {errors}");
            }

            templates[withoutExtension] = template;
            // Keyed by filename-with-extension so the include loader can look up by
            // the name written in the template (e.g. {{ include 'base.html' }}).
            sources[$"{withoutExtension}{extension}"] = source;
        }

        return (templates, sources);
    }

    // Scriban's standard renamer converts PascalCase to snake_case correctly, including
    // runs of uppercase letters (e.g. HTMLBody → html_body). Use it instead of a
    // hand-rolled algorithm — equivalent result for our property set, battle-tested.
    private static readonly MemberRenamerDelegate SnakeCaseRenamer =
        StandardMemberRenamer.Default;

    private static TemplateContext BuildContext(object model, IReadOnlyDictionary<string, string> sources)
    {
        var scriptObject = new ScriptObject();
        scriptObject.Import(model, renamer: SnakeCaseRenamer);

        var context = new TemplateContext
        {
            MemberRenamer = SnakeCaseRenamer,
            TemplateLoader = new InMemoryTemplateLoader(sources),
        };
        context.PushGlobal(scriptObject);
        return context;
    }

    /// <summary>
    /// Resolves <c>{{ include 'base.html' }}</c> against a dictionary of pre-loaded
    /// template source strings. No disk I/O.
    /// </summary>
    private sealed class InMemoryTemplateLoader : ITemplateLoader
    {
        private readonly IReadOnlyDictionary<string, string> _sources;

        public InMemoryTemplateLoader(IReadOnlyDictionary<string, string> sources)
        {
            _sources = sources;
        }

        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName) => templateName;

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            if (_sources.TryGetValue(templatePath, out var source))
                return source;

            throw new KeyNotFoundException(
                $"Included email template '{templatePath}' not found. " +
                $"Available: {string.Join(", ", _sources.Keys)}");
        }

        public ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => new(Load(context, callerSpan, templatePath));
    }
}
