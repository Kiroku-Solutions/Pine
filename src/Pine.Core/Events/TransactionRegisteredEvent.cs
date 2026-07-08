using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Events;

public sealed record TransactionRegisteredEvent(Guid TransactionId, Guid AccountId, Money Amount, DateTimeOffset Timestamp) : IDomainEvent;
