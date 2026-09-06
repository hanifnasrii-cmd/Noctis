using CommunityToolkit.Mvvm.ComponentModel;

namespace Noctis.ViewModels;

/// <summary>
/// One entry in the Settings rail. <see cref="Key"/> is the tab constant
/// (<see cref="SettingsViewModel.TabGeneral"/> …) and doubles as the display label.
/// </summary>
public sealed partial class SettingsSection : ObservableObject
{
    public string Key { get; }
    public string Label => Key;
    /// <summary>StreamGeometry resource key from Assets/Icons.axaml.</summary>
    public string IconKey { get; }
    /// <summary>Rail group header this page sits under ("App", "Playback", …).</summary>
    public string Group { get; }
    public bool IsAbout => Key == SettingsViewModel.TabAbout;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Settings-search hits inside this section; 0 hides the badge.</summary>
    [ObservableProperty] private int _matchCount;

    public bool HasMatches => MatchCount > 0;

    public SettingsSection(string key, string iconKey, string group = "")
    {
        Key = key;
        IconKey = iconKey;
        Group = group;
    }

    partial void OnMatchCountChanged(int value) => OnPropertyChanged(nameof(HasMatches));
}

/// <summary>A rail group: its header and the pages under it, in display order.</summary>
public sealed record SettingsSectionGroup(string Name, IReadOnlyList<SettingsSection> Sections);
