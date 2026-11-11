using System.Reflection;

namespace LabelEditor.Architecture.Tests;

/// <summary>
/// Enforces the dependency direction: App → Presentation → Application → Domain, and
/// Infrastructure → Application. Inner layers never reference outer ones or UI frameworks.
/// </summary>
public class DependencyRulesTests
{
    private static readonly Assembly Domain = typeof(Domain.Layout.LabelLayout).Assembly;
    private static readonly Assembly Application = typeof(Application.Documents.EditorSession).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.Ledit.LeditLabelFormat).Assembly;

    private static IReadOnlyList<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    [Fact]
    public void Domain_depends_on_nothing_but_the_runtime()
    {
        var offending = References(Domain).Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard" && n != "mscorlib").ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Application_depends_only_on_domain_and_abstractions()
    {
        var references = References(Application);

        Assert.Contains("LabelEditor.Domain", references);
        Assert.DoesNotContain(references, n => n.StartsWith("LabelEditor.", StringComparison.Ordinal) && n != "LabelEditor.Domain");
        Assert.DoesNotContain(references, n => n.StartsWith("Avalonia", StringComparison.Ordinal) || n.StartsWith("CommunityToolkit", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_does_not_reference_presentation_or_ui()
    {
        var references = References(Infrastructure);

        Assert.DoesNotContain(references, n => n is "LabelEditor.Presentation" or "LabelEditor.App");
        Assert.DoesNotContain(references, n => n.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_types_are_immutable_records_or_static_helpers()
    {
        var mutable = Domain.GetExportedTypes()
            .Where(t => t.IsClass)
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p)).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(mutable);
    }

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
