using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace Lite;

/// <summary>Small helpers that build controls from the shared theme (Theme.xaml) so code-built dialogs look like the XAML ones.</summary>
static class K
{
    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    public static Style S(string key) => (Style)Application.Current.FindResource(key);

    public static Button Btn(string text, RoutedEventHandler click, bool primary = false, bool danger = false)
    {
        var b = new Button { Content = text, Style = S(danger ? "BtnDanger" : primary ? "BtnPrimary" : "Btn"), MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        if (click != null) b.Click += click;
        return b;
    }
    public static TextBox Box(string text = "") => new() { Text = text, Style = S("TextField") };
    public static PasswordBox Pass() => new() { Style = S("FieldPassword") };
    public static TextBlock Label(string text) => new() { Text = text, Style = S("TextCaption"), Margin = new Thickness(0, 12, 0, 4) };
    public static CheckBox Check(string text, bool on) => new() { Content = text, IsChecked = on, Style = S("Check"), Margin = new Thickness(0, 8, 0, 0) };

    public static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }
}

public class AuthWindow : ThemedWindow
{
    public string User => u.Text; public string Pass => p.Password;
    readonly TextBox u = K.Box(); readonly PasswordBox p = K.Pass();
    public AuthWindow(string site)
    {
        Title = "Sign in"; Width = 400; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var s = new StackPanel { Margin = new Thickness(24) };
        s.Children.Add(new TextBlock { Text = "Sign in", Style = K.S("TextSubtitle") });
        s.Children.Add(new TextBlock { Text = site + " requires a username and password.", Style = K.S("TextCaption"), Margin = new Thickness(0, 4, 0, 0) });
        s.Children.Add(K.Label("Username")); s.Children.Add(u);
        s.Children.Add(K.Label("Password")); s.Children.Add(p);
        var cancel = K.Btn("Cancel", (_, _) => DialogResult = false); cancel.IsCancel = true;
        var ok = K.Btn("Sign in", (_, _) => DialogResult = true, primary: true); ok.IsDefault = true;
        s.Children.Add(K.Buttons(cancel, ok)); Content = s; Loaded += (_, _) => u.Focus();
    }
}

/// <summary>One-line text input (used for folder names and JavaScript prompt()).</summary>
public class PromptWindow : ThemedWindow
{
    readonly TextBox box;
    public string Value => box.Text;
    public PromptWindow(string title, string msg, string def)
    {
        Title = title; Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var s = new StackPanel { Margin = new Thickness(24) };
        s.Children.Add(new TextBlock { Text = title, Style = K.S("TextSubtitle"), TextTrimming = TextTrimming.CharacterEllipsis });
        s.Children.Add(new TextBlock { Text = msg, Style = K.S("TextBody"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        box = K.Box(def); s.Children.Add(box);
        var cancel = K.Btn("Cancel", (_, _) => DialogResult = false); cancel.IsCancel = true;
        var ok = K.Btn("OK", (_, _) => DialogResult = true, primary: true); ok.IsDefault = true;
        s.Children.Add(K.Buttons(cancel, ok)); Content = s; Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}

/// <summary>Themed replacement for MessageBox. Risky actions get a red button that names the action ("Clear history"), never "OK".</summary>
public class ConfirmWindow : ThemedWindow
{
    public static bool Ask(Window owner, string title, string message, string confirmText, bool destructive = false)
        => new ConfirmWindow(owner, title, message, confirmText, destructive).ShowDialog() == true;

    ConfirmWindow(Window owner, string title, string message, string confirmText, bool destructive)
    {
        Title = title; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Owner = owner; WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        var s = new StackPanel { Margin = new Thickness(24) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new PackIcon { Kind = destructive ? PackIconKind.AlertOutline : PackIconKind.HelpCircleOutline, Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = K.B(destructive ? "Danger" : "Acc") });
        head.Children.Add(new TextBlock { Text = title, Style = K.S("TextSubtitle"), VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(head);
        s.Children.Add(new TextBlock { Text = message, Style = K.S("TextBody"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(36, 12, 0, 0) });
        var cancel = K.Btn("Cancel", (_, _) => DialogResult = false); cancel.IsCancel = true;
        var ok = K.Btn(confirmText, (_, _) => DialogResult = true, primary: !destructive, danger: destructive); ok.IsDefault = !destructive;
        s.Children.Add(K.Buttons(cancel, ok)); Content = s;
        Loaded += (_, _) => (destructive ? cancel : ok).Focus();    // destructive prompts default to the safe choice
    }
}
