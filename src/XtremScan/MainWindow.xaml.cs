using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XtremScan.Pages;

namespace XtremScan;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "XtremScan.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Legacy laisse la barre blanche d'avant Windows 11. UseDefaultAppMode reprend celle du système.
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
    }

    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        Nav.IsPaneOpen = false;
        if (Nav.MenuItems.Count > 0)
        {
            Nav.SelectedItem = Nav.MenuItems[0];
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var page = item.Tag switch
        {
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(ScanPage),
        };

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
