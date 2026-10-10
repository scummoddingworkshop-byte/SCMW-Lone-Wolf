using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>One step of the build's step bar.</summary>
public partial class ModBuildStep : ObservableObject
{
    public ModBuildStep(string label) => Label = label;

    public string Label { get; }

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isDone;
}
