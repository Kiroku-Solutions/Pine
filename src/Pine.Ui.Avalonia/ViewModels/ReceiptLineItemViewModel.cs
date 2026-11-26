using CommunityToolkit.Mvvm.ComponentModel;
using Pine.Core.Transactions;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// One editable row of the receipt itemisation.
/// </summary>
/// <remarks>
/// AMOUNTS HERE ARE IN RECEIPT SIGN -- positive for a charge -- because that is what the user is
/// reading off the paper in front of them. The conversion to the ledger sign happens once, in
/// <see cref="ScanReceiptViewModel"/>, when the command is built. A discount is the exception and
/// carries a negative amount, exactly as it reduces the printed total.
/// </remarks>
public partial class ReceiptLineItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal Quantity { get; set; }

    [ObservableProperty]
    public partial decimal UnitPrice { get; set; }

    [ObservableProperty]
    public partial decimal TotalPrice { get; set; }

    /// <summary>
    /// What this row represents (TD-19). Products are the default; the other kinds are what let a
    /// taxed or tipped receipt reconcile exactly instead of being waved through by a tolerance.
    /// </summary>
    [ObservableProperty]
    public partial LineItemKind Kind { get; set; } = LineItemKind.Product;

    /// <summary>
    /// Whether this row is an adjustment rather than something that was bought. Adjustment rows have
    /// no meaningful quantity, so the auto-calculation below leaves them alone.
    /// </summary>
    public bool IsAdjustment => Kind is not (LineItemKind.Product or LineItemKind.Deposit);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Auto-calculate TotalPrice if Quantity or UnitPrice changes.
        //
        // NOT FOR ADJUSTMENT ROWS. A tax line carries an amount, not a quantity times a unit price;
        // recomputing it would drive the figure to zero the moment the row was created and quietly
        // destroy the reconciliation the user had just achieved.
        if (e.PropertyName == nameof(Quantity) || e.PropertyName == nameof(UnitPrice))
        {
            if (!IsAdjustment)
            {
                TotalPrice = Quantity * UnitPrice;
            }
        }
        else if (e.PropertyName == nameof(TotalPrice) && IsAdjustment && UnitPrice != TotalPrice)
        {
            UnitPrice = TotalPrice;
        }

        if (e.PropertyName == nameof(Kind))
        {
            OnPropertyChanged(nameof(IsAdjustment));
        }
    }
}
