namespace Noctis.Mobile.Services;

/// <summary>
/// Lets the user choose a music folder. Android implements it over the Storage Access
/// Framework and returns the persisted tree URI; the string is opaque to the ViewModels,
/// which only store it in AppSettings.MusicFolders and hand it to the scan.
/// Returns null when the user cancels.
/// </summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync();
}
