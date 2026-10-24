using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace Lite;

/// <summary>Material Design icons for menus. Items pick their icon from the header text.</summary>
static class Ic
{
    static readonly Dictionary<string, PackIconKind> Map = new()
    {
        ["New tab"] = PackIconKind.TabPlus,
        ["New window"] = PackIconKind.DockWindow,
        ["New private window"] = PackIconKind.Incognito,
        ["Bookmarks"] = PackIconKind.BookmarkMultipleOutline,
        ["Bookmark manager"] = PackIconKind.BookmarkMultipleOutline,
        ["History"] = PackIconKind.History,
        ["Downloads"] = PackIconKind.Download,
        ["Zoom in"] = PackIconKind.MagnifyPlusOutline,
        ["Zoom out"] = PackIconKind.MagnifyMinusOutline,
        ["Reset zoom"] = PackIconKind.MagnifyScan,
        ["Full screen"] = PackIconKind.Fullscreen,
        ["Find in page…"] = PackIconKind.Magnify,
        ["Print…"] = PackIconKind.Printer,
        ["Save page as PDF…"] = PackIconKind.FilePdfBox,
        ["View page source"] = PackIconKind.CodeTags,
        ["Developer tools"] = PackIconKind.Wrench,
        ["Settings"] = PackIconKind.Cog,
        ["About Orbit"] = PackIconKind.InformationOutline,
        ["Exit"] = PackIconKind.ExitToApp,
        // tab menu
        ["Reload"] = PackIconKind.Refresh,
        ["Duplicate"] = PackIconKind.ContentCopy,
        ["Pin tab"] = PackIconKind.PinOutline,
        ["Unpin tab"] = PackIconKind.PinOffOutline,
        ["Mute site"] = PackIconKind.VolumeOff,
        ["Unmute site"] = PackIconKind.VolumeHigh,
        ["Close tab"] = PackIconKind.Close,
        ["Close other tabs"] = PackIconKind.CloseBoxMultipleOutline,
        ["Close tabs to the right"] = PackIconKind.ArrowExpandRight,
        ["Reopen closed tab"] = PackIconKind.Restore,
        ["Open split view"] = PackIconKind.ViewSplitVertical,
        ["Close split view"] = PackIconKind.ViewAgendaOutline,
        ["Split with current tab"] = PackIconKind.ViewSplitVertical,
        ["Open in new window"] = PackIconKind.DockWindow,
        // bookmarks
        ["Open in new tab"] = PackIconKind.OpenInNew,
        ["Open"] = PackIconKind.OpenInApp,
        ["Open all in new tabs"] = PackIconKind.FolderOpenOutline,
        ["New folder…"] = PackIconKind.FolderPlusOutline,
        ["New subfolder…"] = PackIconKind.FolderPlusOutline,
        ["Rename…"] = PackIconKind.RenameOutline,
        ["Edit…"] = PackIconKind.PencilOutline,
        ["Delete"] = PackIconKind.DeleteOutline,
        ["Delete folder"] = PackIconKind.FolderRemoveOutline,
        ["Move to"] = PackIconKind.FolderMoveOutline,
        ["Hide bookmarks bar"] = PackIconKind.EyeOffOutline,
        ["(empty)"] = PackIconKind.FolderOutline,
    };

    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }

    static readonly Brush Fg = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public static PackIcon Icon(PackIconKind k, double size = 18) => new() { Kind = k, Width = size, Height = size, Foreground = Fg };

    public static MenuItem Item(string header, Action click, string gesture = null, PackIconKind? kind = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture };
        if (kind != null || Map.TryGetValue(header, out var k)) mi.Icon = Icon(kind ?? Map[header]);
        if (click != null) mi.Click += (_, _) => click();
        return mi;
    }
}
