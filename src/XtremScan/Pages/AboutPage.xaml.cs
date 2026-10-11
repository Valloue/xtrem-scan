using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using XtremScan.ViewModels;

namespace XtremScan.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        ViewModel = App.Services.GetRequiredService<AboutViewModel>();
        InitializeComponent();
    }

    public AboutViewModel ViewModel { get; }
}
