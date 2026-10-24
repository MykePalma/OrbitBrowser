using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Lite;

/// <summary>
/// A small translucent copy of the dragged tab that follows the mouse. It is click-through,
/// so the tab strip underneath still receives the drag-over / drop events.
/// </summary>
public class DragGhost : Window
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    readonly TextBlock hint = new() { FontSize = 11, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border hintBox;

    public DragGhost(Tab t)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Focusable = false;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        if (t.Favicon != null) row.Children.Add(new Image { Source = t.Favicon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(new TextBlock { Text = t.Title, MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = K.B("Fg") });

        var tint = t.Tint is Color c ? new SolidColorBrush(c) { Opacity = 0.35 } : null;
        var card = new Grid { Height = 32, MinWidth = 120 };
        card.Children.Add(new Border { Background = K.B("Bar"), CornerRadius = new CornerRadius(8) });
        if (tint != null) card.Children.Add(new Border { Background = tint, CornerRadius = new CornerRadius(8) });
        card.Children.Add(new Border { BorderBrush = K.B("Acc"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) });
        card.Children.Add(row);

        hintBox = new Border { Background = K.B("Bar"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        hint.Foreground = K.B("Fg"); hintBox.Child = hint;

        var stack = new StackPanel { Margin = new Thickness(12) };   // room for the shadow
        stack.Children.Add(card); stack.Children.Add(hintBox);
        stack.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5 };
        Content = stack; Opacity = 0.92;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }

    /// <summary>Moves the ghost to the mouse. <paramref name="overStrip"/> is true while a tab strip would accept the drop.</summary>
    public void Follow(bool overStrip)
    {
        GetCursorPos(out var p);
        var src = PresentationSource.FromVisual(this);
        double sx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1, sy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1;
        Left = p.X * sx - 70; Top = p.Y * sy - 30;
        hint.Text = "Release to open in a new window";
        hintBox.Visibility = overStrip ? Visibility.Collapsed : Visibility.Visible;
    }
}
