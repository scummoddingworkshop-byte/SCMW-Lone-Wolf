using System;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SCMW.App;

/// <summary>
/// Loads the rail's little line icons. Two versions ship for each area: a light one for the normal row,
/// and a dark one for the selected row (which sits on the copper accent). A missing file just means
/// "no icon", so the rail keeps working.
/// </summary>
public static class RailIcons
{
    public static (IImage? Light, IImage? Dark) For(ScreenKey key)
    {
        var name = Name(key);
        return (Load(name), Load(name + "-on"));
    }

    private static string Name(ScreenKey key) => key switch
    {
        ScreenKey.Workspace => "home",              // placeholder icon (no Workspace artwork yet)
        ScreenKey.Home => "home",
        ScreenKey.Projects => "projects",
        ScreenKey.Items => "items",
        ScreenKey.BaseBuilding => "basebuilding",   // placeholder icon (a copy of build) for now
        ScreenKey.Recipes => "recipes",             // placeholder icon (a copy of items) for now
        ScreenKey.Preview => "build",               // placeholder icon (a copy of build) for now
        ScreenKey.Build => "build",
        ScreenKey.Mods => "mods",
        ScreenKey.Backups => "backups",
        ScreenKey.Settings => "settings",
        ScreenKey.Help => "help",
        _ => "home",
    };

    private static IImage? Load(string name)
    {
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://SCMW-Lone-Wolf/Assets/rail/{name}.png")));
        }
        catch
        {
            return null;   // no icon is fine
        }
    }
}
