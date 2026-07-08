using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Events;

public sealed record AccountCreatedEvent(Guid AccountId, string Name, Money InitialBalance, DateTimeOffset Timestamp) : IDomainEvent;
