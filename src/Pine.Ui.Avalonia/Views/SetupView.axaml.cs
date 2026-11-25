using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Pine.Ui.Avalonia.Views;

public partial class SetupView : UserControl
{
    private const double CompactWidth = 700;
    private bool? _compact;

    public SetupView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0)
            return;

        var compact = width < CompactWidth;
        if (_compact == compact)
            return;

        var first = _compact is null;
        _compact = compact;
        if (first && !compact)
            return;

        var pageScroll = this.FindControl<ScrollViewer>("PageScroll")!;
        var formScroll = this.FindControl<ScrollViewer>("FormScroll")!;
        var rootGrid = this.FindControl<Grid>("RootGrid")!;
        var bannerBorder = this.FindControl<Border>("BannerBorder")!;
        var bannerStack = this.FindControl<StackPanel>("BannerStack")!;
        var brandHeader = this.FindControl<StackPanel>("BrandHeader")!;
        var brandLogo = this.FindControl<Image>("BrandLogo")!;
        var brandTitleStack = this.FindControl<StackPanel>("BrandTitleStack")!;
        var brandTitle = this.FindControl<TextBlock>("BrandTitle")!;
        var brandTagline = this.FindControl<TextBlock>("BrandTagline")!;
        var bannerFeatures = this.FindControl<StackPanel>("BannerFeatures")!;
        var formBorder = this.FindControl<Border>("FormBorder")!;
        var step1Stack = this.FindControl<StackPanel>("Step1Stack")!;
        var step2Stack = this.FindControl<StackPanel>("Step2Stack")!;
        var step1Title = this.FindControl<TextBlock>("Step1Title")!;
        var step2Title = this.FindControl<TextBlock>("Step2Title")!;
        var step1Continue = this.FindControl<Button>("Step1ContinueButton")!;
        var step2Card = this.FindControl<Border>("Step2Card")!;
        var step2Actions = this.FindControl<Grid>("Step2ActionsGrid")!;
        var backButton = this.FindControl<Button>("BackButton")!;
        var initButton = this.FindControl<Button>("InitButton")!;

        if (compact)
        {
            pageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            formScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            rootGrid.ColumnDefinitions = new ColumnDefinitions("*");
            rootGrid.RowDefinitions = new RowDefinitions("Auto,*");

            Grid.SetColumn(bannerBorder, 0);
            Grid.SetRow(bannerBorder, 0);
            bannerBorder.CornerRadius = new CornerRadius(0, 0, 24, 24);

            bannerStack.Margin = new Thickness(20, 20, 20, 20);
            bannerStack.Spacing = 12;
            bannerStack.HorizontalAlignment = HorizontalAlignment.Stretch;

            brandHeader.Orientation = Orientation.Horizontal;
            brandHeader.HorizontalAlignment = HorizontalAlignment.Left;
            brandHeader.Spacing = 14;
            brandLogo.Width = 48;
            brandLogo.Height = 48;

            brandTitleStack.HorizontalAlignment = HorizontalAlignment.Left;
            brandTitleStack.Spacing = 2;
            brandTitle.FontSize = 28;
            brandTitle.HorizontalAlignment = HorizontalAlignment.Left;
            brandTagline.FontSize = 14;
            brandTagline.HorizontalAlignment = HorizontalAlignment.Left;
            brandTagline.TextAlignment = global::Avalonia.Media.TextAlignment.Left;

            bannerFeatures.Margin = new Thickness(0, 4, 0, 0);
            bannerFeatures.Spacing = 4;
            bannerFeatures.HorizontalAlignment = HorizontalAlignment.Left;
            foreach (var child in bannerFeatures.Children)
            {
                if (child is TextBlock tb)
                    tb.FontSize = 13;
            }

            Grid.SetColumn(formBorder, 0);
            Grid.SetRow(formBorder, 1);
            formBorder.Padding = new Thickness(20, 24, 20, 32);

            step1Stack.Spacing = 18;
            step1Stack.VerticalAlignment = VerticalAlignment.Top;
            step2Stack.Spacing = 18;
            step2Stack.VerticalAlignment = VerticalAlignment.Top;

            step1Title.FontSize = 24;
            step2Title.FontSize = 24;
            step2Card.Padding = new Thickness(16);

            step1Continue.HorizontalAlignment = HorizontalAlignment.Stretch;

            // Stack primary CTA above Back on narrow phones so neither label truncates.
            step2Actions.ColumnDefinitions = new ColumnDefinitions("*");
            step2Actions.RowDefinitions = new RowDefinitions("Auto,10,Auto");
            Grid.SetColumn(initButton, 0);
            Grid.SetRow(initButton, 0);
            Grid.SetColumn(backButton, 0);
            Grid.SetRow(backButton, 2);
        }
        else
        {
            pageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            formScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            rootGrid.ColumnDefinitions = new ColumnDefinitions("4*,6*");
            rootGrid.RowDefinitions = new RowDefinitions();

            Grid.SetColumn(bannerBorder, 0);
            Grid.SetRow(bannerBorder, 0);
            bannerBorder.CornerRadius = new CornerRadius(0);

            bannerStack.Margin = new Thickness(40);
            bannerStack.Spacing = 15;
            bannerStack.HorizontalAlignment = HorizontalAlignment.Center;

            brandHeader.Orientation = Orientation.Vertical;
            brandHeader.HorizontalAlignment = HorizontalAlignment.Center;
            brandHeader.Spacing = 12;
            brandLogo.Width = 120;
            brandLogo.Height = 120;

            brandTitleStack.HorizontalAlignment = HorizontalAlignment.Center;
            brandTitleStack.Spacing = 6;
            brandTitle.FontSize = 72;
            brandTitle.HorizontalAlignment = HorizontalAlignment.Center;
            brandTagline.FontSize = 20;
            brandTagline.HorizontalAlignment = HorizontalAlignment.Center;
            brandTagline.TextAlignment = global::Avalonia.Media.TextAlignment.Center;

            bannerFeatures.Margin = new Thickness(0, 30, 0, 0);
            bannerFeatures.Spacing = 10;
            bannerFeatures.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (var child in bannerFeatures.Children)
            {
                if (child is TextBlock tb)
                    tb.FontSize = 16;
            }

            Grid.SetColumn(formBorder, 1);
            Grid.SetRow(formBorder, 0);
            formBorder.Padding = new Thickness(60, 40);

            step1Stack.Spacing = 24;
            step1Stack.VerticalAlignment = VerticalAlignment.Center;
            step2Stack.Spacing = 24;
            step2Stack.VerticalAlignment = VerticalAlignment.Center;

            step1Title.FontSize = 32;
            step2Title.FontSize = 32;
            step2Card.Padding = new Thickness(20);

            step1Continue.HorizontalAlignment = HorizontalAlignment.Right;

            step2Actions.ColumnDefinitions = new ColumnDefinitions("*,16,Auto");
            step2Actions.RowDefinitions = new RowDefinitions();
            Grid.SetColumn(backButton, 0);
            Grid.SetRow(backButton, 0);
            Grid.SetColumn(initButton, 2);
            Grid.SetRow(initButton, 0);
        }
    }
}
