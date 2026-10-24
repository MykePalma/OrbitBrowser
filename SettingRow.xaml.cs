using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Lite;

/// <summary>One line in a settings card: a title, an optional description and the control that changes it. Styled in Theme.xaml.</summary>
[ContentProperty(nameof(Control))]
public class SettingRow : System.Windows.Controls.Control
{
    static SettingRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty ControlProperty = DependencyProperty.Register(nameof(Control), typeof(object), typeof(SettingRow));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object Control { get => GetValue(ControlProperty); set => SetValue(ControlProperty, value); }
}
