using Pine.Core.Diagnostics;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>Exposes <see cref="LogRedactor"/> to the application layer (TD-84).</summary>
public sealed class LogRedactorAdapter : IDiagnosticTextRedactor
{
    /// <inheritdoc />
    public string? Redact(string? value) => LogRedactor.Redact(value);

    /// <inheritdoc />
    public bool IsSensitiveKey(string? key) => LogRedactor.IsSensitiveKey(key);
}
