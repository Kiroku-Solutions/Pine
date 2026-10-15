using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Ai;

public interface ICategorizationTrainer
{
    /// <summary>
    /// Trains the categoriser on labelled history.
    /// </summary>
    /// <remarks>
    /// The label is a category NAME, not an identifier — see <see cref="TrainingExample"/> for why
    /// the identifier was removed (TD-32). Implementations may refuse a set that is too small to
    /// learn from; that refusal is a normal outcome, not an error.
    /// </remarks>
    Task TrainModelAsync(IEnumerable<TrainingExample> history, CancellationToken cancellationToken = default);
}
