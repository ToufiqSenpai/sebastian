namespace Sebastian.Tests.GitHub.Issues;

using Sebastian.GitHub.Issues;

public sealed class SpecTemplateValidatorTests
{
    private static readonly string[] RequiredSections =
    [
        "Problem Statement",
        "Solution",
        "User Stories",
        "Tech Stack",
        "Implementation Decisions",
        "Testing Decisions",
        "Out of Scope",
    ];

    [Fact]
    public void CompleteSpec_HasNoMissingSections()
    {
        Assert.Empty(SpecTemplateValidator.FindMissingSections(BuildSpec(RequiredSections)));
    }

    [Fact]
    public void MissingSection_IsReported()
    {
        var body = BuildSpec(RequiredSections.Where(section => section != "Tech Stack"));

        var missing = SpecTemplateValidator.FindMissingSections(body);

        Assert.Equal(["Tech Stack"], missing);
    }

    [Fact]
    public void EmptySection_IsReported()
    {
        var body = BuildSpec(
            RequiredSections,
            section => section == "Tech Stack" ? "## Tech Stack\n" : $"## {section}\nContent.");

        var missing = SpecTemplateValidator.FindMissingSections(body);

        Assert.Equal(["Tech Stack"], missing);
    }

    [Fact]
    public void NoResponsePlaceholder_IsReported()
    {
        var body = BuildSpec(
            RequiredSections,
            section => $"## {section}\n{(section == "Out of Scope" ? "_No response_" : "Content.")}");

        var missing = SpecTemplateValidator.FindMissingSections(body);

        Assert.Equal(["Out of Scope"], missing);
    }

    [Fact]
    public void HtmlCommentOnly_IsReported()
    {
        var body = BuildSpec(
            RequiredSections,
            section => $"## {section}\n{(section == "Testing Decisions" ? "<!-- none -->" : "Content.")}");

        var missing = SpecTemplateValidator.FindMissingSections(body);

        Assert.Equal(["Testing Decisions"], missing);
    }

    [Fact]
    public void LowercaseHeadings_AreAccepted()
    {
        var body = BuildSpec(RequiredSections.Select(section => section.ToLowerInvariant()));

        Assert.Empty(SpecTemplateValidator.FindMissingSections(body));
    }

    [Fact]
    public void CrlfLineEndings_AreAccepted()
    {
        var body = BuildSpec(RequiredSections).Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Empty(SpecTemplateValidator.FindMissingSections(body));
    }

    [Fact]
    public void SubheadingsAndExtraSections_AreIgnored()
    {
        var body = $"""
            ## Problem Statement
            Content.

            ### Details
            More content.

            ## Extra Section
            Extra content.

            {BuildSpec(RequiredSections[1..])}
            """;

        Assert.Empty(SpecTemplateValidator.FindMissingSections(body));
    }

    [Fact]
    public void FurtherNotes_IsOptional()
    {
        var body = BuildSpec(RequiredSections);

        Assert.Empty(SpecTemplateValidator.FindMissingSections(body));
        Assert.Empty(SpecTemplateValidator.FindMissingSections($"{body}\n\n## Further Notes\n"));
    }

    [Fact]
    public void NullBody_ReportsAllRequiredSections()
    {
        Assert.Equal(RequiredSections, SpecTemplateValidator.FindMissingSections(null));
    }

    private static string BuildSpec(IEnumerable<string> sections) =>
        BuildSpec(sections, section => $"## {section}\nContent.");

    private static string BuildSpec(IEnumerable<string> sections, Func<string, string> buildSection) =>
        string.Join("\n\n", sections.Select(buildSection));
}
