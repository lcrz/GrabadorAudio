using System.Configuration;
using System.Data;
using System.Windows;
using NAudio.MediaFoundation;

namespace GrabadorAudio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            MediaFoundationApi.Shutdown();
        }
        catch { }
        base.OnExit(e);
    }
}

