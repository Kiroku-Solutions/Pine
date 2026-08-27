namespace Pine.Tests.Integration.Security;

/// <summary>
/// A throwaway directory on disk that behaves like a vault folder, removed on dispose.
/// </summary>
internal sealed class VaultTestDirectory : IDisposable
{
    public VaultTestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pine-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string HeaderPath => System.IO.Path.Combine(Path, "pine.vault.json");

    public string LegacySaltPath => System.IO.Path.Combine(Path, "pine.salt");

    public void WriteHeaderJson(string json) => File.WriteAllText(HeaderPath, json);

    public void WriteLegacySalt(byte[] salt) => File.WriteAllBytes(LegacySaltPath, salt);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleaning up a temp directory is best-effort; a leftover folder must never fail a test.
        }
    }
}
