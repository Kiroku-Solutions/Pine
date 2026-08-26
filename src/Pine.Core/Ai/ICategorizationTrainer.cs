using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

public interface ICategorizationTrainer
{
    Task TrainModelAsync(IEnumerable<(string Text, decimal Amount, Guid CategoryId)> history, CancellationToken cancellationToken = default);
}
