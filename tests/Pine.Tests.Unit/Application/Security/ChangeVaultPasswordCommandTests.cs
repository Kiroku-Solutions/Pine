using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Security;
using Pine.Application.Security.Commands;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Application.Security;

/// <summary>
/// Covers the password change that envelope encryption makes possible (TD-06).
/// </summary>
/// <remarks>
/// <para>
/// THE HANDLER'S WHOLE JOB IS ORDER AND SCOPE, so that is what these tests pin. Read the header,
/// ask the crypto adapter to re-wrap, write the result - and touch neither the database nor the
/// open session. The crypto itself is proven against real AES-GCM in the integration suite; here
/// the key service is a substitute precisely so that a mistake in sequencing cannot hide behind a
/// passing round trip.
/// </para>
/// <para>
/// The most load-bearing assertion in this file is the one that says NOTHING IS WRITTEN when the
/// current password is wrong. A password change that half-happened would leave a vault whose header
/// no password opens, and there is no recovery phrase (15-Security §7) to fall back on.
/// </para>
/// </remarks>
public class ChangeVaultPasswordCommandTests
{
    private const string VaultDirectory = @"C:\vaults\personal";
    private const string CurrentPassword = "correct horse battery staple";
    private const string NewPassword = "seven tired lemons argue quietly";

    private readonly IVaultSession _session = Substitute.For<IVaultSession>();
    private readonly IVaultHeaderStore _headerStore = Substitute.For<IVaultHeaderStore>();
    private readonly IVaultKeyService _keyService = Substitute.For<IVaultKeyService>();

    public ChangeVaultPasswordCommandTests()
    {
        _session.IsUnlocked.Returns(true);
        _session.DbPath.Returns(VaultDirectory);
    }

    private static VaultKeyWrap AnyWrap(byte marker = 0)
    {
        var ciphertext = new byte[32];
        ciphertext[0] = marker;

        return new VaultKeyWrap(
            VaultKeyWrapAlgorithms.Aes256Gcm,
            new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes],
            ciphertext,
            new byte[VaultKeyWrap.Aes256GcmTagLengthBytes]);
    }

    private static VaultHeader AnyHeader(byte marker = 0) => new(
        VaultHeader.CurrentVersion,
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero),
        KdfParameters.Argon2idV1,
        Convert.FromHexString("000102030405060708090a0b0c0d0e0f"),
        AnyWrap(marker));

    private ChangeVaultPasswordCommandHandler CreateHandler() =>
        new(_session, _headerStore, _keyService);

    private static ChangeVaultPasswordCommand Command() =>
        new(CurrentPassword, NewPassword);

    [Fact]
    public async Task Handle_ShouldWriteTheRewrappedHeader_ForTheOpenVaultsDirectory()
    {
        // Arrange
        var current = AnyHeader(marker: 1);
        var rewrapped = AnyHeader(marker: 2);
        _headerStore.Read(VaultDirectory).Returns(current);
        _keyService.ChangePassword(CurrentPassword, NewPassword, current).Returns(rewrapped);

        // Act
        await CreateHandler().Handle(Command(), CancellationToken.None);

        // Assert
        _headerStore.Received(1).Write(VaultDirectory, rewrapped);
    }

    [Fact]
    public async Task Handle_ShouldRefuse_WhenTheVaultIsLocked()
    {
        // Arrange - "change the password of the vault you are not looking at" is not a coherent
        // request, and a locked session has neither a directory nor a key to work from.
        _session.IsUnlocked.Returns(false);
        _session.DbPath.Returns((string?)null);

        // Act
        var act = () => CreateHandler().Handle(Command(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<VaultLockedException>();
        _headerStore.DidNotReceiveWithAnyArgs().Read(default!);
        _headerStore.DidNotReceiveWithAnyArgs().Write(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldWriteNothing_WhenTheCurrentPasswordIsWrong()
    {
        // Arrange - the single most important assertion here. A change that wrote a header derived
        // from a failed unwrap would leave a vault no password opens, and there is no recovery
        // phrase to fall back on.
        var current = AnyHeader();
        _headerStore.Read(VaultDirectory).Returns(current);
        _keyService.ChangePassword(CurrentPassword, NewPassword, current).Throws(new VaultKeyUnwrapException());

        // Act
        var act = () => CreateHandler().Handle(Command(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<VaultKeyUnwrapException>();
        _headerStore.DidNotReceiveWithAnyArgs().Write(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldLetALegacyVaultFailureSurface_WithoutWriting()
    {
        // Arrange - a pre-envelope vault cannot be re-wrapped, and the read is where that is found.
        _headerStore.Read(VaultDirectory).Throws(new LegacyVaultFormatException(VaultDirectory, 1));

        // Act
        var act = () => CreateHandler().Handle(Command(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<LegacyVaultFormatException>();
        _keyService.DidNotReceiveWithAnyArgs().ChangePassword(default!, default!, default!);
        _headerStore.DidNotReceiveWithAnyArgs().Write(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldNotLockTheSession_BecauseTheDataKeyDidNotChange()
    {
        // Arrange
        var current = AnyHeader(marker: 1);
        _headerStore.Read(VaultDirectory).Returns(current);
        _keyService.ChangePassword(CurrentPassword, NewPassword, current).Returns(AnyHeader(marker: 2));

        // Act
        await CreateHandler().Handle(Command(), CancellationToken.None);

        // Assert - the key the session holds is still the correct key for the database on disk.
        // Locking here would log the user out of a vault they are actively using, as a side effect
        // of a settings change, and force them to retype the password they just set.
        _session.DidNotReceive().Lock();
        _session.DidNotReceiveWithAnyArgs().Lock(default);
        _session.DidNotReceiveWithAnyArgs().Unlock(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldNeverAskTheKeyServiceForThePlaintextDataKey()
    {
        // Arrange - unwrap-then-rewrap happens inside IVaultKeyService.ChangePassword precisely so
        // that no plaintext data key passes through the application layer, where nothing would zero
        // it. If the handler ever started calling UnwrapDataKey itself, that guarantee is gone.
        var current = AnyHeader(marker: 1);
        _headerStore.Read(VaultDirectory).Returns(current);
        _keyService.ChangePassword(CurrentPassword, NewPassword, current).Returns(AnyHeader(marker: 2));

        // Act
        await CreateHandler().Handle(Command(), CancellationToken.None);

        // Assert
        _keyService.DidNotReceiveWithAnyArgs().UnwrapDataKey(default!, default!);
    }
}

/// <summary>
/// Covers the rules that stop a password change before the header file is touched.
/// </summary>
/// <remarks>
/// The entropy floor is the reason this validator exists at all: without it, "create a strong
/// vault, then change the password to something trivial" would be a documented way to walk straight
/// around the gate <c>SetupViewModel</c> enforces at creation.
/// </remarks>
public class ChangeVaultPasswordCommandValidatorTests
{
    private const string StrongCurrent = "correct horse battery staple";
    private const string StrongNew = "seven tired lemons argue quietly";

    private static readonly ChangeVaultPasswordCommandValidator Validator = new();

    private static async Task<string[]> ValidateAsync(string current, string @new)
    {
        var errors = await Validator.ValidateAsync(new ChangeVaultPasswordCommand(current, @new), CancellationToken.None);

        return errors.Select(e => e.Message).ToArray();
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptAStrongDistinctNewPassword()
    {
        (await ValidateAsync(StrongCurrent, StrongNew)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_ShouldRejectAnEmptyCurrentPassword(string current)
    {
        (await ValidateAsync(current, StrongNew)).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_ShouldRejectAnEmptyNewPassword(string @new)
    {
        (await ValidateAsync(StrongCurrent, @new)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectANewPasswordBelowTheEntropyFloor()
    {
        // The gate that makes the validator worth having. "hunter2" would otherwise be reachable
        // from a vault that was only allowed to exist because its first password was strong.
        PasswordStrengthPolicy.Evaluate("hunter2").MeetsFloor.Should().BeFalse("the fixture must actually be weak");

        (await ValidateAsync(StrongCurrent, "hunter2")).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectANewPasswordIdenticalToTheCurrentOne()
    {
        // A no-op change reported as success would tell the user something untrue about their
        // vault - notably that an old password they meant to retire no longer opens it.
        (await ValidateAsync(StrongCurrent, StrongCurrent)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ShouldNeverEchoEitherPassword_InAnyMessage()
    {
        // TD-24 and 20-Coding-Standards §10: no message may contain a secret. Validation errors
        // travel - into RequestValidationException, onto a screen, into a screenshot, into a log.
        var weak = "hunter2";

        var messages = await ValidateAsync(weak, weak);

        messages.Should().NotBeEmpty();

        foreach (var message in messages)
        {
            message.Should().NotContain(weak);
        }

        var strongCase = await ValidateAsync(StrongCurrent, StrongCurrent);

        foreach (var message in strongCase)
        {
            message.Should().NotContain(StrongCurrent);
        }
    }
}
