using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pine.Core.Security;
using Pine.Infrastructure.Ai.Categorisation;
using Pine.Infrastructure.Security;
using Xunit;

namespace Pine.Tests.Unit.Ai;

/// <summary>
/// Covers <see cref="VaultCategorizerModelStore"/>, the class TD-16 introduced to close the three
/// defects that lived in <c>MlNetCategorizer</c>'s single <c>private const string ModelPath =
/// "Assets/Ai/categorizer_model.zip"</c>: a relative path, a plaintext file, and one model shared
/// by every vault.
/// </summary>
/// <remarks>
/// <para>
/// THE REAL CIPHER AND A REAL DIRECTORY, NOT SUBSTITUTES. The claims worth testing here are "the
/// bytes on disk are not the model" and "a model from another vault does not load" -- and a
/// substituted <see cref="IVaultFileCipher"/> would let both pass while being false, because the
/// substitute would return whatever it was told to. So every test drives the actual
/// <see cref="AesGcmVaultFileCipher"/> over an actual file in an actual throwaway directory. The
/// only fake is the vault session itself, which is a key, a directory and a lock bit.
/// </para>
/// <para>
/// A DIRECTORY PER TEST, created in the constructor and deleted in <see cref="Dispose"/>. xUnit
/// builds a fresh instance of this class for every test, so this is one vault per test with no
/// shared state -- which matters more than usual here, because half these tests are about a file
/// left over from a PREVIOUS write, and a leaked directory would make them lie. Nothing here ever
/// resolves a real vault path, for the same reason the diagnostics tests never resolve
/// <c>%LOCALAPPDATA%\Pine\logs</c>.
/// </para>
/// <para>
/// THE PATH IS BUILT FROM THE PUBLIC CONSTANTS, never from literals. Those constants are public
/// precisely so a test (and the backup logic) can name the location without duplicating it; a test
/// that hardcoded "ai/categorizer.model" would keep passing after a rename that broke every
/// existing vault.
/// </para>
/// </remarks>
public sealed class VaultCategorizerModelStoreTests : IDisposable
{
    private readonly string _vaultDirectory;
    private readonly StubVaultAccessor _vault;
    private readonly VaultCategorizerModelStore _store;

    public VaultCategorizerModelStoreTests()
    {
        _vaultDirectory = Path.Combine(
            Path.GetTempPath(),
            "PineCategorizerModelStoreTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_vaultDirectory);

        _vault = new StubVaultAccessor { Key = KeyA(), DbPath = _vaultDirectory };
        _store = new VaultCategorizerModelStore(
            _vault,
            new AesGcmVaultFileCipher(_vault),
            NullLogger<VaultCategorizerModelStore>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_vaultDirectory))
            {
                Directory.Delete(_vaultDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A failing test must not also fail the run by leaving an undeletable scratch file
            // behind; the directory is under the OS temp path and will be swept.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>The vault session: a key, a directory, and a lock bit a test can flip.</summary>
    private sealed class StubVaultAccessor : IVaultDataKeyAccessor
    {
        public bool IsUnlocked { get; set; } = true;

        public byte[]? Key { get; set; }

        public string? DbPath { get; set; }
    }

    private static byte[] KeyA()
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(i + 1);
        }

        return key;
    }

    /// <summary>A second vault's data key, for the "copied between vaults" test.</summary>
    private static byte[] KeyB()
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(0xFF - i);
        }

        return key;
    }

    /// <summary>The directory holding derived AI artefacts, named through the public constant.</summary>
    private string AiDirectory => Path.Combine(_vaultDirectory, VaultCategorizerModelStore.ModelDirectoryName);

    /// <summary>The sealed model's path, named through the public constants.</summary>
    private string ModelPath => Path.Combine(AiDirectory, VaultCategorizerModelStore.ModelFileName);

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            var matched = true;
            for (var offset = 0; offset < needle.Length; offset++)
            {
                if (haystack[start + offset] != needle[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The first-run case, and the one the categoriser hits on every brand-new vault: no model, no
    /// <c>ai</c> directory, and no exception. "There is nothing to load yet" is an ordinary state,
    /// not a fault, and the caller distinguishes it from a damaged model by nothing at all -- both
    /// are null, deliberately.
    /// </summary>
    [Fact]
    public void TryRead_ShouldReturnNull_WhenNoModelHasEverBeenWritten()
    {
        _store.TryRead().Should().BeNull();
    }

    [Fact]
    public void WriteThenTryRead_ShouldRoundTripTheExactBytes()
    {
        var model = new byte[] { 1, 2, 3, 250, 251, 252, 0, 0, 42 };

        _store.Write(model);

        _store.TryRead().Should().Equal(model);
    }

    /// <summary>
    /// Defect one of three: the old path was RELATIVE, so it resolved against whatever the process
    /// working directory happened to be. The model now lands at a path built from the vault's own
    /// directory, in a named sub-directory the store creates for itself.
    /// </summary>
    [Fact]
    public void Write_ShouldPlaceTheModelInsideTheVaultsAiDirectory()
    {
        _store.Write(new byte[] { 7, 7, 7 });

        Directory.Exists(AiDirectory).Should().BeTrue();
        File.Exists(ModelPath).Should().BeTrue();
    }

    /// <summary>
    /// Defect two of three, and the reason this whole port exists: the old model was PLAINTEXT, sat
    /// next to the executable, and had been trained on real payees and real amounts. The file is read
    /// back raw and searched byte by byte for the model it was given.
    /// </summary>
    [Fact]
    public void Write_ShouldNotLeaveTheModelBytesOnDiskInTheClear()
    {
        var model = "PAYEE=Sainsburys;AMOUNT=42.17;CATEGORY=Groceries"u8.ToArray();

        _store.Write(model);

        var onDisk = File.ReadAllBytes(ModelPath);

        ContainsSubsequence(onDisk, model).Should()
            .BeFalse("the whole point of TD-16 is that a model trained on real ledger data never touches disk in the clear");
    }

    /// <summary>
    /// Replacement, which is what every training cycle after the first one does. The interesting
    /// half is that the SECOND model comes back rather than the first: a write-temp-then-move that
    /// forgot the move would leave the old model in place and this is what would catch it.
    /// </summary>
    [Fact]
    public void Write_ShouldReplaceThePreviousModel_SoTryReadReturnsTheSecond()
    {
        var first = new byte[] { 1, 1, 1, 1 };
        var second = new byte[] { 2, 2, 2, 2, 2, 2, 2, 2, 2 };

        _store.Write(first);
        _store.Write(second);

        _store.TryRead().Should().Equal(second);
    }

    /// <summary>
    /// ABSENT, UNREADABLE AND UNDECRYPTABLE ARE ALL NULL, DELIBERATELY. A model is derived data that
    /// can be rebuilt from the ledger, so discarding a damaged one costs a training cycle -- whereas
    /// raising would let a single corrupt cache file stop a user opening their own vault. This test
    /// is the one that pins that policy down, because it is the one that would otherwise silently
    /// regress into a thrown exception the moment somebody "improved" the error handling.
    /// </summary>
    [Fact]
    public void TryRead_ShouldReturnNull_WhenTheStoredFileIsGarbage()
    {
        Directory.CreateDirectory(AiDirectory);
        File.WriteAllBytes(ModelPath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03, 0x04 });

        _store.TryRead().Should().BeNull();
    }

    /// <summary>
    /// A truncated file is the specific damage a torn write would produce, and it must be discarded
    /// the same way rather than reaching a slice that throws out of a method documented not to.
    /// </summary>
    [Fact]
    public void TryRead_ShouldReturnNull_WhenTheStoredFileIsTruncated()
    {
        _store.Write(new byte[] { 5, 6, 7, 8, 9, 10 });

        var sealedBlob = File.ReadAllBytes(ModelPath);
        File.WriteAllBytes(ModelPath, sealedBlob[..(sealedBlob.Length / 2)]);

        _store.TryRead().Should().BeNull();
    }

    /// <summary>
    /// Defect three of three: the old model was shared by every vault, so "Personal" and "Business"
    /// -- the reason multi-vault exists at all -- trained one model between them. Here the file stays
    /// exactly where it is and only the vault's data key changes, which is what happens if a model
    /// file is copied into a different vault's directory. It must not open, and it must not throw.
    /// </summary>
    [Fact]
    public void TryRead_ShouldReturnNull_WhenTheModelWasSealedByADifferentVaultsKey()
    {
        _store.Write("a model trained on someone else's ledger"u8.ToArray());

        _vault.Key = KeyB();

        _store.TryRead().Should().BeNull();
    }

    /// <summary>
    /// An empty model is not a model. Storing one would replace a good model with a file that
    /// authenticates perfectly and predicts nothing, which is worse than having no model at all --
    /// because the absent case is the one the categoriser knows how to recover from.
    /// </summary>
    [Fact]
    public void Write_ShouldThrowArgumentException_ForAnEmptyModel()
    {
        var act = () => _store.Write(Array.Empty<byte>());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The empty-model refusal happens before anything touches the disk, so a rejected write must
    /// not have created the directory or left a file behind.
    /// </summary>
    [Fact]
    public void Write_ShouldNotTouchTheDisk_WhenTheModelIsEmpty()
    {
        var act = () => _store.Write(Array.Empty<byte>());

        act.Should().Throw<ArgumentException>();
        File.Exists(ModelPath).Should().BeFalse();
    }

    /// <summary>
    /// THE LOAD-BEARING MEMBER. The categoriser is a singleton holding an expensive prediction
    /// engine, so it necessarily outlives any one vault session; it compares this value at the
    /// moment of use against the value it loaded under, which is what makes a stale engine
    /// impossible WITHOUT depending on a lock event arriving in time.
    /// </summary>
    [Fact]
    public void VaultFingerprint_ShouldBeTheVaultDirectory_WhenUnlocked()
    {
        _store.VaultFingerprint.Should().Be(_vaultDirectory);
    }

    /// <summary>
    /// Null when locked, so the comparison in the categoriser fails closed: a captured fingerprint
    /// can never equal null, so a locked vault always looks like "not the vault I loaded from".
    /// </summary>
    [Fact]
    public void VaultFingerprint_ShouldBeNull_WhenTheVaultIsLocked()
    {
        _vault.IsUnlocked = false;

        _store.VaultFingerprint.Should().BeNull();
    }

    /// <summary>
    /// A DISTINCT TYPE, per TD-23: the training background service must be able to recognise "the
    /// user locked up and went home" by catching a type, not by sniffing an exception message for
    /// the substring "Vault". Note that this is the one failure the store does NOT swallow -- a
    /// locked vault is not a damaged model, and answering null would tell the caller to retrain
    /// when it should instead wait.
    /// </summary>
    [Fact]
    public void TryRead_ShouldThrowVaultLocked_WhenTheVaultIsLocked()
    {
        _store.Write(new byte[] { 1, 2, 3 });
        _vault.IsUnlocked = false;

        var act = () => _store.TryRead();

        act.Should().Throw<VaultLockedException>();
    }

    [Fact]
    public void Write_ShouldThrowVaultLocked_WhenTheVaultIsLocked()
    {
        _vault.IsUnlocked = false;

        var act = () => _store.Write(new byte[] { 1, 2, 3 });

        act.Should().Throw<VaultLockedException>();
    }

    /// <summary>
    /// The replacement is write-temp-then-move, so that a crash halfway leaves the OLD model rather
    /// than a truncated one. The temp file is an implementation detail of that atomicity -- but a
    /// temp file that survives a SUCCESSFUL write is litter accumulating inside the user's vault
    /// directory, and it is also a second copy of the sealed model nobody is tracking. After a
    /// completed write the directory must hold the model and nothing else.
    /// </summary>
    [Fact]
    public void Write_ShouldLeaveNoTemporaryFileBehind_AfterASuccessfulWrite()
    {
        _store.Write(new byte[] { 3, 1, 4, 1, 5, 9, 2, 6 });
        _store.Write(new byte[] { 2, 7, 1, 8, 2, 8 });

        Directory.GetFiles(AiDirectory, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(AiDirectory).Should().ContainSingle()
            .Which.Should().Be(ModelPath);
    }
}
