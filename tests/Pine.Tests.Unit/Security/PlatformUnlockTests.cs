using System.Security.Cryptography;
using FluentAssertions;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Ui.Windows.Security;

namespace Pine.Tests.Unit.Security;

/// <summary>TD-87: opt-in platform key wrap. Contract tests use a fake; one real DPAPI round trip runs on Windows only.</summary>
public sealed class PlatformUnlockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pine-pu-" + Guid.NewGuid().ToString("N"));
    private string VaultA => Path.Combine(_root, "VaultA_1");
    private string VaultB => Path.Combine(_root, "VaultB_2");

    public PlatformUnlockTests()
    {
        Directory.CreateDirectory(VaultA);
        Directory.CreateDirectory(VaultB);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    /// <summary>XOR "protector" that binds the context, so a wrong context yields a throw like DPAPI does.</summary>
    private sealed class FakeProtector : IPlatformKeyProtector
    {
        public string Name { get; set; } = "fake";
        public bool IsAvailable { get; set; } = true;
        public bool Corrupt { get; set; }

        public byte[] Protect(byte[] secret, byte[] context) => [.. context, .. secret];

        public byte[] Unprotect(byte[] blob, byte[] context)
        {
            if (Corrupt) return new byte[5];
            if (!blob.AsSpan().StartsWith(context)) throw new CryptographicException();
            return blob[context.Length..];
        }
    }

    [Fact]
    public void Enable_then_TryUnlock_returns_the_same_key()
    {
        var sut = new PlatformUnlock(new FakeProtector());
        var key = Key();

        sut.Enable(VaultA, key);

        sut.IsEnabled(VaultA).Should().BeTrue();
        sut.TryUnlock(VaultA).Should().Equal(key);
    }

    [Fact]
    public void Not_enabled_returns_null()
    {
        new PlatformUnlock(new FakeProtector()).TryUnlock(VaultA).Should().BeNull();
    }

    [Fact]
    public void No_protector_is_unavailable_and_Enable_throws_and_writes_nothing()
    {
        var sut = new PlatformUnlock();

        sut.IsAvailable.Should().BeFalse();
        var act = () => sut.Enable(VaultA, Key());
        act.Should().Throw<PlatformUnlockUnavailableException>();
        sut.IsEnabled(VaultA).Should().BeFalse();
    }

    [Fact]
    public void Unavailable_protector_never_unlocks_even_with_a_blob_on_disk()
    {
        var fake = new FakeProtector();
        new PlatformUnlock(fake).Enable(VaultA, Key());
        fake.IsAvailable = false;

        new PlatformUnlock(fake).TryUnlock(VaultA).Should().BeNull();
    }

    [Fact]
    public void Wrap_copied_to_another_vault_does_not_unlock_it()
    {
        var sut = new PlatformUnlock(new FakeProtector());
        sut.Enable(VaultA, Key());
        File.Copy(Path.Combine(VaultA, PlatformUnlock.FileName), Path.Combine(VaultB, PlatformUnlock.FileName));

        sut.TryUnlock(VaultB).Should().BeNull();
    }

    [Fact]
    public void Wrap_made_by_a_different_protector_is_refused()
    {
        new PlatformUnlock(new FakeProtector { Name = "one" }).Enable(VaultA, Key());

        new PlatformUnlock(new FakeProtector { Name = "two" }).TryUnlock(VaultA).Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Version\":1,\"Protector\":\"fake\",\"Blob\":\"!!!\"}")]
    [InlineData("{\"Version\":9,\"Protector\":\"fake\",\"Blob\":\"AAAA\"}")]
    public void Corrupt_file_fails_closed(string content)
    {
        File.WriteAllText(Path.Combine(VaultA, PlatformUnlock.FileName), content);

        new PlatformUnlock(new FakeProtector()).TryUnlock(VaultA).Should().BeNull();
    }

    [Fact]
    public void Protector_returning_a_wrong_length_key_fails_closed()
    {
        var fake = new FakeProtector();
        var sut = new PlatformUnlock(fake);
        sut.Enable(VaultA, Key());
        fake.Corrupt = true;

        sut.TryUnlock(VaultA).Should().BeNull();
    }

    [Fact]
    public void Disable_removes_the_wrap_and_unlock_stops_working()
    {
        var sut = new PlatformUnlock(new FakeProtector());
        sut.Enable(VaultA, Key());

        sut.Disable(VaultA);

        sut.IsEnabled(VaultA).Should().BeFalse();
        sut.TryUnlock(VaultA).Should().BeNull();
    }

    [Fact]
    public void Enable_rejects_a_key_that_is_not_32_bytes()
    {
        var act = () => new PlatformUnlock(new FakeProtector()).Enable(VaultA, new byte[16]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Dpapi_round_trips_through_PlatformUnlock_and_rejects_a_wrong_context()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI exists only on Windows; skipped elsewhere.

        var dpapi = new DpapiPlatformKeyProtector();
        dpapi.IsAvailable.Should().BeTrue();
        var sut = new PlatformUnlock(dpapi);
        var key = Key();

        sut.Enable(VaultA, key);

        File.ReadAllText(Path.Combine(VaultA, PlatformUnlock.FileName))
            .Should().NotContain(Convert.ToBase64String(key), "the key must be sealed, not stored");
        sut.TryUnlock(VaultA).Should().Equal(key);

        var blob = dpapi.Protect(key, [1, 2, 3]);
        var act = () => dpapi.Unprotect(blob, [9, 9, 9]);
        act.Should().Throw<CryptographicException>();
    }
}
