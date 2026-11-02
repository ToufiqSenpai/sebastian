using Microsoft.UI;
using Microsoft.UI.Reactor;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Sebastian.Windows.Helpers;

internal static class FilePicker
{
    public static async Task<StorageFile?> PickFileAsync(ReactorWindow window, string fileType)
    {
        return await CreatePicker(window, fileType).PickSingleFileAsync();
    }

    public static async Task<IReadOnlyList<StorageFile>?> PickFilesAsync(
        ReactorWindow window,
        string fileType)
    {
        return await CreatePicker(window, fileType).PickMultipleFilesAsync();
    }

    private static FileOpenPicker CreatePicker(ReactorWindow window, string fileType)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(fileType);
        var hwnd = Win32Interop.GetWindowFromWindowId(window.NativeWindow.AppWindow.Id);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        return picker;
    }
}
