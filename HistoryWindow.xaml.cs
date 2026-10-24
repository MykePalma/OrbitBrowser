using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace Lite;

public class HistoryDay { public string Label { get; set; } = ""; }
public class HistoryMore { public string Label { get; set; } = ""; }
public class HistoryRowVm
{
    public HistoryItem Item { get; set; }
    public string Title => string.IsNullOrWhiteSpace(Item.Title) ? Item.Url : Item.Title;
    public string Url => Item.Url;
    public string Host { get; set; } = "";
    public string Time => Item.Time.ToString("t", CultureInfo.CurrentCulture);
    public string Initial { get { var h = Host.StartsWith("www.") ? Host[4..] : Host; return h.Length > 0 ? char.ToUpperInvariant(h[0]).ToString() : "?"; } }
    /// <summary>A calm, desaturated colour derived from the site name so rows are easy to tell apart without being loud.</summary>
    public Brush Avatar
    {
        get
        {
            int h = 17; foreach (var ch in Host) h = h * 31 + ch; double hue = Math.Abs(h) % 360;
            double s = 0.45, l = 0.74, c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = l - c / 2; double r, g, b;
            (r, g, b) = hue < 60 ? (c, x, 0.0) : hue < 120 ? (x, c, 0.0) : hue < 180 ? (0.0, c, x) : hue < 240 ? (0.0, x, c) : hue < 300 ? (x, 0.0, c) : (c, 0.0, x);
            var br = new SolidColorBrush(Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255))); br.Freeze(); return br;
        }
    }
}

/// <summary>Headings and "show more" are plain content; only page rows are selectable.</summary>
class HistoryRowStyles : StyleSelector
{
    readonly Style row, plain;
    public HistoryRowStyles(Style row, Style plain) { this.row = row; this.plain = plain; }
    public override Style SelectStyle(object item, DependencyObject container) => item is HistoryRowVm ? row : plain;
}

public partial class HistoryWindow : ThemedWindow
{
    readonly MainWindow main;
    int limit = 300;

    public HistoryWindow(MainWindow owner)
    {
        InitializeComponent(); main = owner; Owner = owner;
        List.ItemContainerStyleSelector = new HistoryRowStyles((Style)FindResource("Row"), (Style)FindResource("StaticRow"));
        Loaded += (_, _) => { Fill(); Search.Focus(); };
    }

    static string DayLabel(DateTime d)
    {
        var today = DateTime.Today;
        if (d.Date == today) return "Today"; if (d.Date == today.AddDays(-1)) return "Yesterday";
        return d.ToString(d.Year == today.Year ? "dddd, d MMMM" : "d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    void Fill()
    {
        var q = Search.Text.Trim();
        var all = Store.D.History.OrderByDescending(h => h.Time).Where(h => q == "" || (h.Title + " " + h.Url).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var items = new List<object>(); DateTime? day = null; int shown = 0;
        foreach (var h in all.Take(limit))
        {
            if (day != h.Time.Date) { day = h.Time.Date; items.Add(new HistoryDay { Label = DayLabel(h.Time) }); }
            string host = ""; try { host = new Uri(h.Url).Host; } catch { }
            items.Add(new HistoryRowVm { Item = h, Host = host }); shown++;
        }
        if (all.Count > shown) items.Add(new HistoryMore { Label = $"Show {Math.Min(300, all.Count - shown):N0} more" });
        List.ItemsSource = items;

        bool none = all.Count == 0;
        Empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed; List.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        if (none)
        {
            bool searching = q != "";
            EmptyIcon.Kind = searching ? PackIconKind.MagnifyClose : PackIconKind.History;
            EmptyTitle.Text = searching ? $"No results for “{q}”" : "No browsing history yet";
            EmptyText.Text = searching ? "Check the spelling or try a different word." : "Pages you visit show up here so you can find them again. Private windows are never recorded.";
        }
        ClearBtn.IsEnabled = Store.D.History.Count > 0;
        Count.Text = q == "" ? $"{Store.D.History.Count:N0} pages" : $"{all.Count:N0} of {Store.D.History.Count:N0} pages match";
    }

    void Search_Changed(object sender, TextChangedEventArgs e) { limit = 300; SearchHint.Visibility = Search.Text == "" ? Visibility.Visible : Visibility.Collapsed; SearchClear.Visibility = Search.Text == "" ? Visibility.Collapsed : Visibility.Visible; Fill(); }
    void SearchClear_Click(object sender, RoutedEventArgs e) { Search.Clear(); Search.Focus(); }
    void Search_Key(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Search.Text != "") { Search.Clear(); e.Handled = true; }
        else if (e.Key == Key.Down && List.Items.Count > 0) { var first = List.Items.OfType<HistoryRowVm>().FirstOrDefault(); if (first != null) { List.SelectedItem = first; (List.ItemContainerGenerator.ContainerFromItem(first) as ListBoxItem)?.Focus(); } e.Handled = true; }
    }
    void More_Click(object sender, RoutedEventArgs e) { limit += 300; Fill(); }

    IEnumerable<HistoryRowVm> Selected => List.SelectedItems.OfType<HistoryRowVm>().ToList();
    void Open(bool newTab) { foreach (var r in Selected.Take(newTab ? 20 : 1)) { if (newTab) main.NewTab(r.Url, true); else main.Navigate(r.Url); } }
    void List_DoubleClick(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is DependencyObject o && !IsButton(o)) Open(false); }
    static bool IsButton(DependencyObject o) { while (o != null) { if (o is Button) return true; o = VisualTreeHelper.GetParent(o); } return false; }
    void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Open(Keyboard.Modifiers == ModifierKeys.Control); e.Handled = true; }
        else if (e.Key == Key.Delete) { Remove(Selected); e.Handled = true; }
        else if (e.Key == Key.Up && List.SelectedIndex <= 1) { Search.Focus(); }
    }
    void DeleteRow_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is HistoryRowVm r) Remove(new[] { r }); e.Handled = true; }
    void Remove(IEnumerable<HistoryRowVm> rows)
    {
        var list = rows.ToList(); if (list.Count == 0) return;
        foreach (var r in list) Store.D.History.Remove(r.Item);
        Store.Save(); Fill();
    }
    void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        int n = Store.D.History.Count; if (n == 0) return;
        if (!ConfirmWindow.Ask(this, "Clear browsing history?", $"This removes {n:N0} pages from your history on this computer. Bookmarks aren't affected.", "Clear history", destructive: true)) return;
        Store.D.History.Clear(); Store.Save(); Fill();
    }
}
