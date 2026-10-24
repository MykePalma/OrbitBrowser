using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lite;

/// <summary>
/// Base for Orbit's own windows. It applies the shared "AppWindow" style (dark background, light text, Windows font) and a dark title bar
/// before the first frame. A style is used because an implicit Style would not match subclasses and Windows' built-in window style
/// would override plain property defaults.
/// </summary>
public class ThemedWindow : Window
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    public ThemedWindow() { if (Application.Current?.TryFindResource("AppWindow") is Style st) Style = st; SourceInitialized += (_, _) => { try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4); } catch { } }; }
}
