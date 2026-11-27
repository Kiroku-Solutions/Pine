using System;
using System.Collections.Generic;
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

/// <summary>
/// Runs zoomed on-demand OCR over a single user-drawn bounding box when full-page OCR missed faint text.
/// </summary>
public sealed record RecognizeReceiptRegionCommand(byte[] AlignedImageBytes, NormalizedRect Region) : IRequest<string>;

public sealed class RecognizeReceiptRegionCommandHandler : IRequestHandler<RecognizeReceiptRegionCommand, string>
{
    private readonly IReceiptRegionReader _reader;

    public RecognizeReceiptRegionCommandHandler(IReceiptRegionReader reader)
    {
        _reader = reader;
    }

    public Task<string> Handle(RecognizeReceiptRegionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _reader.ReadRegionAsync(request.AlignedImageBytes, request.Region, cancellationToken);
    }
}

/// <summary>
/// Returns the user's saved custom receipt tag names and current local model training status.
/// </summary>
public sealed record GetReceiptTrainingStatusQuery() : IRequest<ReceiptTrainingStatus>;

public sealed class GetReceiptTrainingStatusQueryHandler : IRequestHandler<GetReceiptTrainingStatusQuery, ReceiptTrainingStatus>
{
    private readonly IReceiptFieldLearner _learner;

    public GetReceiptTrainingStatusQueryHandler(IReceiptFieldLearner learner)
    {
        _learner = learner;
    }

    public Task<ReceiptTrainingStatus> Handle(GetReceiptTrainingStatusQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(_learner.GetStatus());
}
