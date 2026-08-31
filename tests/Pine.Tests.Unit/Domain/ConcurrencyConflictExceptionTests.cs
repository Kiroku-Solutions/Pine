using System;
using FluentAssertions;
using Pine.Core.Common;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// TD-17's exception: what the application layer sees when a write is refused because the row moved
/// underneath it.
/// </summary>
/// <remarks>
/// Its message can reach a log file and a dialog, so what it does NOT say is as much the point as
/// what it does. The store's own exception is kept inside it for the log, and nothing about the row —
/// no entity name, no id, no amount — is put into the text.
/// </remarks>
public class ConcurrencyConflictExceptionTests
{
    [Fact]
    public void From_KeepsTheStoresOwnExceptionAsTheInnerOne()
    {
        var stored = new InvalidOperationException("expected to affect 1 row(s), but actually affected 0");

        var exception = ConcurrencyConflictException.From(stored);

        exception.InnerException.Should().BeSameAs(stored,
            "a log needs the provider's account of which entries failed");
    }

    [Fact]
    public void From_DoesNotRepeatTheStoresMessage_InItsOwn()
    {
        var stored = new InvalidOperationException("affected 0 row(s) in table Accounts id 7f3c...");

        var exception = ConcurrencyConflictException.From(stored);

        exception.Message.Should().NotContain("Accounts");
        exception.Message.Should().NotContain("7f3c");
    }

    /// <summary>
    /// The message is the one a user may read, so it has to say what happened and what to do rather
    /// than name a mechanism.
    /// </summary>
    [Fact]
    public void TheDefaultMessage_TellsTheUserWhatToDo()
    {
        var exception = new ConcurrencyConflictException();

        exception.Message.Should().Contain("changed by another operation");
        exception.Message.Should().Contain("Reload");
        exception.Message.Should().NotContain("concurrency token");
        exception.Message.Should().NotContain("SQL");
    }
}
