using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctis.Localization;

namespace Noctis.ViewModels;

/// <summary>
/// One entry in the Settings rail. <see cref="Key"/> is the tab constant
/// (<see cref="SettingsViewModel.TabGeneral"/> …): the identity every comparison uses.
/// <see cref="Label"/> is the localized text shown on the rail and is purely cosmetic.
/// </summary>
public sealed partial class SettingsSection : ObservableObject
{
    public string Key { get; }
    /// <summary>Localized rail text (resx Settings.Tab.&lt;tab&gt;). Re-read via <see cref="Relabel"/>.</summary>
    public string Label => Loc.T(SettingsViewModel.TabLabelKey(Key));
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

    /// <summary>After a language switch: the label re-reads, the key never changes.</summary>
    public void Relabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// A rail group: its header and the pages under it, in display order. <see cref="Name"/> is the
/// English identity ("App", "Playback", …); <see cref="Label"/> the localized header.
/// </summary>
public sealed class SettingsSectionGroup : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<SettingsSection> Sections { get; }
    public string Label => Loc.T("Settings.Group." + Name);

    public SettingsSectionGroup(string name, IReadOnlyList<SettingsSection> sections)
    {
        Name = name;
        Sections = sections;
    }

    public void Relabel()
    {
        OnPropertyChanged(nameof(Label));
        foreach (var s in Sections) s.Relabel();
    }
}
