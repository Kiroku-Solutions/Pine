using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Ai;

namespace Pine.Application.Transactions.Commands;

/// <summary>
/// Persists a user's interactive bounding-box tags for a scanned receipt and schedules
/// low-resource background training of the local per-vault receipt layout model.
/// </summary>
public sealed record LearnReceiptLayoutCommand(ReceiptLayoutAnnotation Annotation) : IRequest<ReceiptTrainingStatus>;

public sealed class LearnReceiptLayoutCommandHandler : IRequestHandler<LearnReceiptLayoutCommand, ReceiptTrainingStatus>
{
    private readonly IReceiptFieldLearner _learner;

    public LearnReceiptLayoutCommandHandler(IReceiptFieldLearner learner)
    {
        _learner = learner;
    }

    public Task<ReceiptTrainingStatus> Handle(LearnReceiptLayoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _learner.RecordAnnotationAndTrainInBackgroundAsync(request.Annotation, cancellationToken);
    }
}
