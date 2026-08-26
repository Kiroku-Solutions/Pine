using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Transactions;
using Pine.Core.Ai;
using Pine.Core.Configuration;

namespace Pine.Application.Services;

public class CategorizationTrainingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CategorizationTrainingBackgroundService> _logger;
    private const string LastTrainedCountSettingKey = "Ai.Categorization.LastTrainedCount";
    private const int BatchThreshold = 10; // Number of new categorized transactions required to trigger training

    public CategorizationTrainingBackgroundService(IServiceProvider serviceProvider, ILogger<CategorizationTrainingBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
#pragma warning disable CA1848, CA2254
                _logger.LogInformation("Categorization Training Background Service is starting.");

        // Wait for the app to fully start and the user to unlock the vault
        // before attempting database access.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        // Poll every 5 minutes
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Vault"))
            {
                // Vault is still locked, skip this cycle silently
                _logger.LogDebug("Skipping training cycle: Vault is locked.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing categorization batch training.");
#pragma warning restore CA1848, CA2254
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        
        // Wait, we need ITransactionRepository to count categorized transactions.
        // I will assume ITransactionRepository exists and has GetAllAsync(). Let's resolve it.
        var transactionRepository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var settingRepository = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var trainer = scope.ServiceProvider.GetRequiredService<ICategorizationTrainer>();

        var allTransactions = await transactionRepository.GetAllAsync(cancellationToken);
        
        // Filter transactions that have splits with categories assigned
        // Assuming a transaction is categorized if any of its splits has a non-empty category.
        var categorizedTransactions = allTransactions
            .Where(t => t.Splits.Any(s => !string.IsNullOrWhiteSpace(s.Category)))
            .ToList();

        int currentCategorizedCount = categorizedTransactions.Count;

        var lastTrainedCountSetting = await settingRepository.GetAsync(LastTrainedCountSettingKey, cancellationToken);
        int lastTrainedCount = 0;
        
        if (lastTrainedCountSetting != null && int.TryParse(lastTrainedCountSetting.Value, out var parsedCount))
        {
            lastTrainedCount = parsedCount;
        }

        if (currentCategorizedCount - lastTrainedCount >= BatchThreshold)
        {
#pragma warning disable CA1848, CA2254, CA1873
            _logger.LogInformation($"Threshold reached. Training AI model with {currentCategorizedCount} transactions.");
#pragma warning restore CA1848, CA2254, CA1873

            // Prepare training data: mapping Payee and Split Category
            var trainingData = categorizedTransactions.SelectMany(t => 
                t.Splits.Where(s => !string.IsNullOrWhiteSpace(s.Category))
                .Select(s => (
                    Text: t.Payee, 
                    Amount: s.Amount.Amount, 
                    CategoryId: Guid.TryParse(s.Category, out var g) ? g : Guid.Empty
                ))
            ).Where(d => d.CategoryId != Guid.Empty).ToList();

            if (trainingData.Count > 0)
            {
                await trainer.TrainModelAsync(trainingData, cancellationToken);
                
                // Update the setting
                if (lastTrainedCountSetting == null)
                {
                    lastTrainedCountSetting = Pine.Core.Settings.Setting.Create(LastTrainedCountSettingKey, currentCategorizedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    lastTrainedCountSetting.UpdateValue(currentCategorizedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                await settingRepository.SaveAsync(lastTrainedCountSetting, cancellationToken);
                
#pragma warning disable CA1848
                _logger.LogInformation("AI model training completed and state saved.");
#pragma warning restore CA1848
            }
        }
    }
}
