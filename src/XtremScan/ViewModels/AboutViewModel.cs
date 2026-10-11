using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XtremScan.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public AboutViewModel()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = version is null ? "0.11.3" : version.ToString(3);
    }

    public string VersionText { get; }
}
