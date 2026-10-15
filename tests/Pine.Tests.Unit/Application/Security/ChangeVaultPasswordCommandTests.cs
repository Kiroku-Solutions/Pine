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
/// The password-change handler after TD-86/TD-87: it destroys the platform wrap, delegates the crash-safe key rotation to
/// <see cref="IVaultKeyRotationService"/> (proven on a real SQLCipher vault in the integration suite) and swaps the
/// session's key. These tests pin ORDER and SCOPE.
/// </summary>
public class ChangeVaultPasswordCommandTests
{
    private const string VaultDirectory = @"C:\vaults\personal";
    private const string CurrentPassword = "correct horse battery staple";
    private const string NewPassword = "seven tired lemons argue quietly";

    private readonly IVaultSession _session = Substitute.For<IVaultSession>();
    private readonly IVaultKeyRotationService _rotation = Substitute.For<IVaultKeyRotationService>();
    private readonly IPlatformUnlock _platformUnlock = Substitute.For<IPlatformUnlock>();
    private readonly byte[] _newKey = Enumerable.Repeat((byte)5, 32).ToArray();

    public ChangeVaultPasswordCommandTests()
    {
        _session.IsUnlocked.Returns(true);
        _session.DbPath.Returns(VaultDirectory);
        _rotation.ChangePassword(VaultDirectory, CurrentPassword, NewPassword).Returns(_ => (byte[])_newKey.Clone());
    }

    private ChangeVaultPasswordCommandHandler CreateHandler() => new(_session, _rotation, _platformUnlock);

    private static ChangeVaultPasswordCommand Command() => new(CurrentPassword, NewPassword);

    [Fact]
    public async Task Handle_ShouldRotateForTheVaultDirectory_AndSwapTheSessionKey()
    {
        byte[]? seen = null;
        _session.When(s => s.Unlock(VaultDirectory, Arg.Any<byte[]>())).Do(c => seen = (byte[])c.Arg<byte[]>().Clone());

        await CreateHandler().Handle(Command(), CancellationToken.None);

        _rotation.Received(1).ChangePassword(VaultDirectory, CurrentPassword, NewPassword);
        seen.Should().Equal(_newKey, "the session must carry the rotated data key afterwards");
    }

    [Fact]
    public async Task Handle_ShouldInvalidateThePlatformWrap_ForTheVaultDirectory_BeforeRotating()
    {
        await CreateHandler().Handle(Command(), CancellationToken.None);

        Received.InOrder(() =>
        {
            _platformUnlock.InvalidateWrap(VaultDirectory);
            _rotation.ChangePassword(VaultDirectory, CurrentPassword, NewPassword);
        });
    }

    [Fact]
    public async Task Handle_ShouldFailClosedWithoutRotating_WhenTheWrapCannotBeDestroyed()
    {
        _platformUnlock.When(p => p.InvalidateWrap(VaultDirectory)).Throw(new PlatformUnlockInvalidationException());

        var act = () => CreateHandler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<PlatformUnlockInvalidationException>();
        _rotation.DidNotReceiveWithAnyArgs().ChangePassword(default!, default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldNotSwapTheSessionKey_WhenTheRotationFails()
    {
        _rotation.When(r => r.ChangePassword(VaultDirectory, CurrentPassword, NewPassword)).Throw(new VaultKeyUnwrapException());

        var act = () => CreateHandler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<VaultKeyUnwrapException>();
        _session.DidNotReceiveWithAnyArgs().Unlock(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldRefuse_WhenTheVaultIsLocked()
    {
        _session.IsUnlocked.Returns(false);
        _session.DbPath.Returns((string?)null);

        var act = () => CreateHandler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<VaultLockedException>();
        _rotation.DidNotReceiveWithAnyArgs().ChangePassword(default!, default!, default!);
        _platformUnlock.DidNotReceiveWithAnyArgs().InvalidateWrap(default!);
    }

    [Fact]
    public async Task Handle_ShouldNotLockTheSession()
    {
        await CreateHandler().Handle(Command(), CancellationToken.None);

        _session.DidNotReceiveWithAnyArgs().Lock();
        _session.DidNotReceiveWithAnyArgs().Lock(default);
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
