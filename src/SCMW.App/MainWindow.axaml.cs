using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace SCMW.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The taskbar/window icon. The exe icon is set in the project file; this makes the running
        // window match it. Cosmetic only — never let a missing resource stop the app opening.
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SCMW-Lone-Wolf/Assets/scmw.png")));
        }
        catch
        {
            // no icon is fine
        }
    }
}
