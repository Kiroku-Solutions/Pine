using System;
using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// Every field the header reader requires is refused by name when it is absent or unusable (TD-05).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS ADDS. <c>Pine.Tests.Integration/Security/FileVaultHeaderStoreTests</c> covers the round
/// trip, the version and KDF refusals, the salt and the key wrap. It never removes the KDF section,
/// the KDF id, any of the five KDF parameters, the creation time, the vault id or the salt, and never
/// hands the reader a literal <c>null</c> -- so eleven of the reader's "never invent, only reject"
/// branches were executed by no test in either suite. Each of them is the difference between
/// refusing a header and deriving the wrong key from a default, whose only symptom would surface
/// inside SQLCipher looking exactly like data loss.
/// </para>
/// <para>
/// WHY IN THE UNIT SUITE. This is file I/O on a temporary directory, with no database, no native
/// library and no clock, so it is as fast and as isolated as anything else here. It was written when
/// the unit-suite coverage floor for <c>Pine.Infrastructure</c> was found breached at 77.63% against
/// 78% -- by the previous commit, which added <c>VaultRestoreService</c> and tested it only in the
/// integration suite, which the floor does not measure. The floor was answered with tests that close
/// real gaps, not with the nearest uncovered lines.
/// </para>
/// <para>
/// Each case starts from a header the store itself wrote -- so the JSON is exactly what the product
/// produces -- and removes or breaks ONE field. The positive control reads that same file back
/// untouched; without it, every refusal below would also pass against a reader that refused
/// everything.
/// </para>
/// </remarks>
public sealed class FileVaultHeaderStoreFieldPresenceTests : IDisposable
{
    private readonly string _vaultDirectory =
        Path.Combine(Path.GetTempPath(), "pine-header-presence-" + Guid.NewGuid().ToString("N"));

    private readonly FileVaultHeaderStore _store = new();

    public FileVaultHeaderStoreFieldPresenceTests()
    {
        _store.Write(_vaultDirectory, new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero),
            KdfParameters.Argon2idV1,
            new byte[KdfParameters.Argon2idV1.SaltLengthBytes],
            new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, new byte[12], new byte[32], new byte[16])));
    }

    private string HeaderPath => Path.Combine(_vaultDirectory, FileVaultHeaderStore.HeaderFileName);

    public void Dispose()
    {
        if (Directory.Exists(_vaultDirectory))
        {
            Directory.Delete(_vaultDirectory, recursive: true);
        }
    }

    /// <summary>Positive control: the untouched file the store wrote reads back.</summary>
    [Fact]
    public void TheUntouchedHeader_ReadsBack()
    {
        _store.Exists(_vaultDirectory).Should().BeTrue();

        var header = _store.Read(_vaultDirectory);

        header.KdfParameters.Should().Be(KdfParameters.Argon2idV1);
        header.CreatedAtUtc.Should().Be(new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("headerVersion", "no valid headerVersion")]
    [InlineData("kdf", "no kdf section")]
    [InlineData("kdf.memoryKib", "missing one or more kdf parameters")]
    [InlineData("kdf.iterations", "missing one or more kdf parameters")]
    [InlineData("kdf.parallelism", "missing one or more kdf parameters")]
    [InlineData("kdf.keyLengthBytes", "missing one or more kdf parameters")]
    [InlineData("kdf.saltLengthBytes", "missing one or more kdf parameters")]
    [InlineData("createdAtUtc", "no createdAtUtc")]
    [InlineData("vaultId", "no valid vaultId")]
    [InlineData("saltBase64", "no salt")]
    public void AMissingField_IsRefusedByName_NeverDefaulted(string field, string expectedReason)
    {
        Rewrite(root => Parent(root, field).Remove(Leaf(field)));

        var act = () => _store.Read(_vaultDirectory);

        act.Should().Throw<VaultHeaderCorruptException>()
            .WithMessage($"*{expectedReason}*")
            .Which.FilePath.Should().Be(HeaderPath);
    }

    [Theory]
    [InlineData("headerVersion", "0", "no valid headerVersion")]
    [InlineData("kdf.kdfId", "\"  \"", "no kdfId")]
    [InlineData("vaultId", "\"not-a-guid\"", "no valid vaultId")]
    [InlineData("saltBase64", "\"\"", "no salt")]
    public void AnUnusableField_IsRefusedByName(string field, string jsonValue, string expectedReason)
    {
        Rewrite(root => Parent(root, field)[Leaf(field)] = JsonNode.Parse(jsonValue));

        var act = () => _store.Read(_vaultDirectory);

        act.Should().Throw<VaultHeaderCorruptException>().WithMessage($"*{expectedReason}*");
    }

    /// <summary>A file that is literally <c>null</c> is empty, not a header with every field defaulted.</summary>
    [Fact]
    public void ALiteralNull_IsRefusedAsEmpty()
    {
        File.WriteAllText(HeaderPath, "null");

        var act = () => _store.Read(_vaultDirectory);

        act.Should().Throw<VaultHeaderCorruptException>().WithMessage("*empty*");
    }

    private void Rewrite(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(HeaderPath))!.AsObject();
        edit(root);
        File.WriteAllText(HeaderPath, root.ToJsonString());
    }

    private static JsonObject Parent(JsonObject root, string field) =>
        field.Contains('.', StringComparison.Ordinal)
            ? root[field[..field.IndexOf('.', StringComparison.Ordinal)]]!.AsObject()
            : root;

    private static string Leaf(string field) =>
        field.Contains('.', StringComparison.Ordinal) ? field[(field.IndexOf('.', StringComparison.Ordinal) + 1)..] : field;
}
