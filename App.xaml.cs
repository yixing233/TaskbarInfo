using System.Configuration;
using System.Data;
using System.Windows;

namespace TaskbarInfo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static System.Windows.Media.ImageSource? GetAppIcon()
    {
        try
        {
            // Load Desktop Icon from embedded resources
            var uri = new Uri("pack://application:,,,/src/icons/桌面图标.png");
            return System.Windows.Media.Imaging.BitmapFrame.Create(uri);
        }
        catch {}
        return null;
    }
}

