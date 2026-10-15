using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Pine.Application.Settings.Commands;
using Pine.Core.Transactions;
using Pine.Core.Ai;
using Pine.Core.Configuration;

namespace Pine.Application.Services;

public class CategorizationTrainingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CategorizationTrainingBackgroundService> _logger;
    private const string LastTrainedCountSettingKey = "Ai.Categorization.LastTrainedCount";

    /// <summary>
    /// The in-vault Setting holding the user's consent to train the categoriser on their ledger
    /// (TD-89). Training runs only when it holds exactly <see cref="TrainingConsentGranted"/>;
    /// absent, empty, or anything else means no.
    /// </summary>
    /// <remarks>
    /// An encrypted Setting, not Config (TD-119): it is an operative preference that travels with
    /// the <c>.pine</c> file, and it is the consent for data that lives inside that file.
    /// </remarks>
    public const string TrainingConsentSettingKey = "Ai.Categorization.TrainingConsent";

    /// <summary>The only value of <see cref="TrainingConsentSettingKey"/> that permits training.</summary>
    public const string TrainingConsentGranted = "true";
    private readonly PineAiOptions _aiOptions;

    /// <summary>Transactions fetched per query when the training set is read (TD-133).</summary>
    internal int TrainingPageSize { get; init; } = 500;

    /// <summary>Creates the background trainer.</summary>
    /// <param name="aiOptions">
    /// Carries the batch threshold, the startup delay and the poll interval. All three were
    /// literals in this file, and the two timings were literals inside <c>ExecuteAsync</c> where
    /// nothing could reach them — including a test, which is why this service had none (TD-27).
    /// </param>
    public CategorizationTrainingBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<CategorizationTrainingBackgroundService> logger,
        IOptions<PineAiOptions> aiOptions)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _aiOptions = aiOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
#pragma warning disable CA1848, CA2254
                _logger.LogInformation("Categorization Training Background Service is starting.");

        // Wait for the app to fully start and the user to unlock the vault
        // before attempting database access.
        await Task.Delay(_aiOptions.TrainingStartupDelay, stoppingToken);

        using var timer = new PeriodicTimer(_aiOptions.TrainingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Pine.Core.Security.VaultLockedException)
            {
                // The typed form (TD-16). Anything reached through IVaultFileCipher or the model
                // store reports a locked vault as this rather than as a string to be sniffed.
                _logger.LogDebug("Skipping training cycle: Vault is locked.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Vault"))
            {
                // The UNTYPED form, still reachable: the PineDbContext factory in each composition
                // root throws InvalidOperationException("Cannot access database: Vault is locked.")
                // and the repositories resolved above go through it. This clause is TD-23's example
                // of message sniffing and it stays until that factory throws a type instead --
                // deleting it now would turn "the user locked up and went home" back into an Error
                // logged every five minutes.
                _logger.LogDebug("Skipping training cycle: Vault is locked.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing categorization batch training.");
#pragma warning restore CA1848, CA2254
            }
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        // An explicit scope, because this service is a singleton and the repositories below are
        // Scoped: resolving them from the root provider would pin one PineDbContext (and therefore
        // one vault's key) for the entire process lifetime.
        using var scope = _serviceProvider.CreateScope();

        // Reads only. The counting pass needs every transaction with a categorised split, and the
        // last-trained watermark, both of which are plain queries.
        var settingRepository = scope.ServiceProvider.GetRequiredService<ISettingRepository>();

        // TD-89: DEFAULT OFF. Derived personal data is not produced until the user has said yes, and
        // the check comes BEFORE the ledger is read so that a declining user's transactions are not
        // even loaded by this service.
        var consent = await settingRepository.GetAsync(TrainingConsentSettingKey, cancellationToken);
        if (!string.Equals(consent?.Value, TrainingConsentGranted, StringComparison.Ordinal))
        {
            return;
        }

        var transactionRepository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var trainer = scope.ServiceProvider.GetRequiredService<ICategorizationTrainer>();

        // The WRITE goes through MediatR, never through settingRepository. Repositories no longer
        // call SaveChangesAsync (TD-01), so a direct SaveAsync here would stage the new watermark
        // on this scope's context and then throw it away when the scope is disposed - the service
        // would retrain the model on every single poll, forever. ISender is a singleton decorator
        // that opens its own scope per Send, so the command below gets its own context and its own
        // committed transaction.
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // TD-133: the ledger is no longer materialised in one query. The store counts first (a cheap
        // upper bound), and only when that could reach the threshold are the categorised
        // transactions walked one page at a time.
        var possibleCount = await transactionRepository.CountPossiblyCategorisedAsync(cancellationToken);

        var lastTrainedCountSetting = await settingRepository.GetAsync(LastTrainedCountSettingKey, cancellationToken);
        int lastTrainedCount = 0;
        
        if (lastTrainedCountSetting != null && int.TryParse(lastTrainedCountSetting.Value, out var parsedCount))
        {
            lastTrainedCount = parsedCount;
        }

        // The store's count is an upper bound on the exact one, so a miss here is a real miss.
        if (possibleCount - lastTrainedCount < _aiOptions.TrainingBatchThreshold)
        {
            return;
        }

        // Walk the categorised transactions page by page, keeping only the lightweight examples and
        // the exact count; no page outlives its iteration.
        var trainingExamples = new System.Collections.Generic.List<TrainingExample>();
        int currentCategorizedCount = 0;
        for (var skip = 0; ; skip += TrainingPageSize)
        {
            var page = await transactionRepository.GetCategorisedPageAsync(skip, TrainingPageSize, cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            currentCategorizedCount += CategorisationTrainingData.CountCategorised(page);
            trainingExamples.AddRange(CategorisationTrainingData.From(page));

            if (page.Count < TrainingPageSize)
            {
                break;
            }
        }

        if (currentCategorizedCount - lastTrainedCount >= _aiOptions.TrainingBatchThreshold)
        {
#pragma warning disable CA1848, CA2254, CA1873
            _logger.LogInformation($"Threshold reached. Training AI model with {currentCategorizedCount} transactions.");
#pragma warning restore CA1848, CA2254, CA1873

            // TD-32. This projection lives in CategorisationTrainingData so that it can be tested,
            // because for as long as it was written inline here it threw away every example: it
            // parsed the split's category NAME as a Guid and then filtered out everything that
            // failed to parse, which was all of it.
            var trainingData = trainingExamples;

            if (trainingData.Count > 0)
            {
                await trainer.TrainModelAsync(trainingData, cancellationToken);
                
                // Persist the new watermark. SaveSettingCommand is transactional, so if this
                // throws nothing is committed and the next poll simply retrains.
                await sender.Send(
                    new SaveSettingCommand(
                        LastTrainedCountSettingKey,
                        currentCategorizedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    cancellationToken);
                
#pragma warning disable CA1848
                _logger.LogInformation("AI model training completed and state saved.");
#pragma warning restore CA1848
            }
        }
    }
}
