using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Design",
    "CA1054:URI parameters should not be strings",
    Justification = "Provider base URLs are stored and edited as text; catalog values can also be placeholders.",
    Scope = "namespaceanddescendants",
    Target = "~N:Sebastian.Libs.Providers")]
[assembly: SuppressMessage(
    "Design",
    "CA1055:URI return values should not be strings",
    Justification = "Provider base URLs are stored and edited as text; catalog values can also be placeholders.",
    Scope = "namespaceanddescendants",
    Target = "~N:Sebastian.Libs.Providers")]
[assembly: SuppressMessage(
    "Design",
    "CA1056:URI properties should not be strings",
    Justification = "Provider base URLs are stored and edited as text; catalog values can also be placeholders.",
    Scope = "namespaceanddescendants",
    Target = "~N:Sebastian.Libs.Providers")]
[assembly: SuppressMessage(
    "Design",
    "CA1054:URI parameters should not be strings",
    Justification = "Webhook and install URLs are stored and edited as text.",
    Scope = "namespaceanddescendants",
    Target = "~N:Sebastian.Libs.GitHub.App")]
[assembly: SuppressMessage(
    "Design",
    "CA1056:URI properties should not be strings",
    Justification = "Webhook and install URLs are stored and edited as text.",
    Scope = "namespaceanddescendants",
    Target = "~N:Sebastian.Libs.GitHub.App")]
