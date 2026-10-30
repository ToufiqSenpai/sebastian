namespace Sebastian.GitHub.Issues;

internal static class SpecTemplateValidator
{
    // Keep in sync with skills/to-spec/SKILL.md.
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

    public static IReadOnlyList<string> FindMissingSections(string? body)
    {
        var filledSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;

        foreach (var rawLine in (body ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentSection = line[3..].Trim();
                continue;
            }

            if (currentSection is null || IsEmptyContent(line))
            {
                continue;
            }

            filledSections.Add(currentSection);
        }

        return RequiredSections.Where(section => !filledSections.Contains(section)).ToArray();
    }

    private static bool IsEmptyContent(string line) =>
        line.Length == 0 ||
        line.StartsWith("<!--", StringComparison.Ordinal) ||
        string.Equals(line, "_No response_", StringComparison.OrdinalIgnoreCase);
}
