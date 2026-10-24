using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lite;

/// <summary>Add/edit one bookmark: name, address and a folder picked from a short list.</summary>
public class BookmarkDialog : ThemedWindow
{
    readonly Bookmark bm; readonly MainWindow main;
    readonly TextBox name, url;
    readonly ListBox folder = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemContainerStyle = K.S("Row"), MaxHeight = 168, Margin = new Thickness(4) };
    List<string> paths = new();

    public BookmarkDialog(MainWindow owner, Bookmark b, bool isNew)
    {
        bm = b; main = owner;
        Title = isNew ? "Bookmark added" : "Edit bookmark"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var s = new StackPanel { Margin = new Thickness(24) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new MaterialDesignThemes.Wpf.PackIcon { Kind = isNew ? MaterialDesignThemes.Wpf.PackIconKind.BookmarkCheckOutline : MaterialDesignThemes.Wpf.PackIconKind.BookmarkOutline, Width = 24, Height = 24, Foreground = K.B("Acc"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = isNew ? "Bookmark added" : "Edit bookmark", Style = K.S("TextSubtitle"), VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(head);
        s.Children.Add(K.Label("Name")); name = K.Box(b.Title); s.Children.Add(name);
        s.Children.Add(K.Label("Address")); url = K.Box(b.Url); s.Children.Add(url);
        s.Children.Add(K.Label("Folder"));
        s.Children.Add(new Border { Style = K.S("Card"), Child = folder });
        var nf = new Button { Style = K.S("BtnGhost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 8, 0, 0), Padding = new Thickness(8, 0, 12, 0) };
        { var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderPlusOutline, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(new TextBlock { Text = "New folder…", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); nf.Content = sp; }
        nf.Click += (_, _) => { var parent = Selected(); var n = main.AskFolderName("New folder"); if (string.IsNullOrEmpty(n)) return; Fill(Store.AddFolder(parent, n)); };
        s.Children.Add(nf);
        var remove = new Button { Content = "Remove bookmark", Style = K.S("BtnDangerGhost") };
        remove.Click += (_, _) => { Store.D.Bookmarks.Remove(bm); Store.Save(); DialogResult = false; };
        var done = K.Btn("Done", (_, _) => Apply(), primary: true); done.IsDefault = true;
        var row = new DockPanel { Margin = new Thickness(0, 24, 0, 0) };
        DockPanel.SetDock(remove, Dock.Left); row.Children.Add(remove);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; right.Children.Add(done); row.Children.Add(right);
        s.Children.Add(row); Content = s;
        Fill(b.Folder);
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
    string Selected() => folder.SelectedIndex >= 0 && folder.SelectedIndex < paths.Count ? paths[folder.SelectedIndex] : "";
    void Fill(string select)
    {
        paths = new List<string> { "" }; folder.Items.Clear(); folder.Items.Add(FolderRow("Bookmarks bar", 0));
        foreach (var f in Store.FoldersSorted()) { paths.Add(f); folder.Items.Add(FolderRow(Store.LeafName(f), f.Count(c => c == '/') + 1)); }
        folder.SelectedIndex = Math.Max(0, paths.IndexOf(select ?? ""));
        folder.ScrollIntoView(folder.SelectedItem);
    }
    static UIElement FolderRow(string text, int depth)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8 + 16 * depth, 0, 8, 0), Height = 32 };
        sp.Children.Add(new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderOutline, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = K.B("Mut") });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Style = K.S("TextBody") });
        return sp;
    }
    void Apply()
    {
        bm.Title = string.IsNullOrWhiteSpace(name.Text) ? bm.Url : name.Text.Trim();
        if (!string.IsNullOrWhiteSpace(url.Text)) bm.Url = url.Text.Trim();
        bm.Folder = Selected(); main.RememberFolder(bm.Folder);
        Store.Save(); DialogResult = true;
    }
}

/// <summary>Tree view of all bookmarks and folders: rename, delete, drag to move, import/export.</summary>
public class BookmarkManager : ThemedWindow
{
    readonly MainWindow main;
    readonly TreeView tree = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(16, 0, 8, 0) };
    readonly TextBox search = new() { Style = K.S("FieldBare") };
    readonly TextBlock searchHint = new() { Text = "Search bookmarks", Style = K.S("TextCaption"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextWrapping = TextWrapping.NoWrap };
    readonly StackPanel empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 340, Visibility = Visibility.Collapsed };
    Point dragStart; object dragItem;

    public BookmarkManager(MainWindow owner)
    {
        main = owner; Title = "Bookmarks"; Width = 800; Height = 620; MinWidth = 520; MinHeight = 380; Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button Ghost(string text, MaterialDesignThemes.Wpf.PackIconKind icon, RoutedEventHandler click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new MaterialDesignThemes.Wpf.PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var btn = new Button { Content = sp, Style = K.S("BtnGhost"), Margin = new Thickness(4, 0, 0, 0) }; btn.Click += click; return btn;
        }
        var header = new DockPanel { Margin = new Thickness(24, 24, 24, 16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Ghost("New folder", MaterialDesignThemes.Wpf.PackIconKind.FolderPlusOutline, (_, _) => { main.NewFolderPrompt(SelFolderForNew()); Fill(); }));
        actions.Children.Add(Ghost("Import", MaterialDesignThemes.Wpf.PackIconKind.Import, (_, _) => Import()));
        actions.Children.Add(Ghost("Export", MaterialDesignThemes.Wpf.PackIconKind.Export, (_, _) => Export()));
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = "Bookmarks", Style = K.S("TextTitle"), VerticalAlignment = VerticalAlignment.Center });

        var sbox = new Grid(); sbox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); sbox.ColumnDefinitions.Add(new ColumnDefinition());
        sbox.Children.Add(new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Magnify, Width = 18, Height = 18, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = K.B("Mut") });
        Grid.SetColumn(search, 1); Grid.SetColumn(searchHint, 1); sbox.Children.Add(search); sbox.Children.Add(searchHint);
        System.Windows.Automation.AutomationProperties.SetName(search, "Search bookmarks");
        var searchBorder = new Border { Style = K.S("SearchBox"), Margin = new Thickness(24, 0, 24, 12), Child = sbox };

        var hint = new Border { BorderBrush = K.B("Outline"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 10, 24, 10), Child = new TextBlock { Text = "Drag a bookmark or folder onto a folder to move it · Right-click for more · Delete removes the selection", Style = K.S("TextCaption") } };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new Grid(); body.Children.Add(tree); body.Children.Add(empty);
        empty.Children.Add(new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.BookmarkOffOutline, Width = 48, Height = 48, Foreground = K.B("Mut"), HorizontalAlignment = HorizontalAlignment.Center });
        empty.Children.Add(new TextBlock { Text = "No bookmarks yet", Style = K.S("TextSubtitle"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 16, 0, 0) });
        empty.Children.Add(new TextBlock { Text = "Press Ctrl+D on any page to bookmark it, or import bookmarks from another browser.", Style = K.S("TextCaption"), FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 16) });
        var imp = new Button { Content = "Import bookmarks…", Style = K.S("Btn"), HorizontalAlignment = HorizontalAlignment.Center }; imp.Click += (_, _) => Import(); empty.Children.Add(imp);
        Grid.SetRow(header, 0); Grid.SetRow(searchBorder, 1); Grid.SetRow(body, 2); Grid.SetRow(hint, 3);
        grid.Children.Add(header); grid.Children.Add(searchBorder); grid.Children.Add(body); grid.Children.Add(hint);
        Content = grid;

        search.TextChanged += (_, _) => { searchHint.Visibility = search.Text == "" ? Visibility.Visible : Visibility.Collapsed; };
        search.TextChanged += (_, _) => Fill();
        tree.MouseDoubleClick += (_, _) => { if (Sel is Bookmark) OpenSel(true); };
        tree.KeyDown += (_, e) => { if (e.Key == Key.Delete) DeleteSel(); else if (e.Key == Key.Enter) OpenSel(true); else if (e.Key == Key.F2) EditSel(); };
        tree.AllowDrop = true;
        tree.PreviewMouseLeftButtonDown += (_, e) => { dragStart = e.GetPosition(null); dragItem = ItemAt(e.OriginalSource as DependencyObject)?.Tag; };
        tree.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragItem == null || dragItem is string { Length: 0 }) return;
            var dlt = e.GetPosition(null) - dragStart; if (Math.Abs(dlt.X) < 8 && Math.Abs(dlt.Y) < 8) return;
            var item = dragItem; dragItem = null; DragDrop.DoDragDrop(tree, new DataObject("lite-bm", item), DragDropEffects.Move);
        };
        tree.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent("lite-bm")) return;
            var src = e.Data.GetData("lite-bm"); var tgt = ItemAt(e.OriginalSource as DependencyObject)?.Tag;
            Move(src, tgt is string f ? f : tgt is Bookmark tb ? tb.Folder : ""); e.Handled = true;
        };
        tree.PreviewMouseRightButtonDown += (_, e) => { var it = ItemAt(e.OriginalSource as DependencyObject); if (it != null) it.IsSelected = true; };
        tree.ContextMenuOpening += (_, e) => { var m = BuildMenu(); if (m == null) e.Handled = true; else tree.ContextMenu = m; };
        Loaded += (_, _) => { Fill(); search.Focus(); };
        Activated += (_, _) => Fill();
    }

    static TreeViewItem ItemAt(DependencyObject o) { while (o != null && o is not TreeViewItem) o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o); return o as TreeViewItem; }

    public void Move(object src, string dest)
    {
        if (src is Bookmark b) b.Folder = dest;
        else if (src is string f && f != "") Store.MoveFolder(f, dest);
        Store.Save(); MainWindow.RefreshAll(); Fill();
    }

    static StackPanel Header(UIElement icon, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 0);
        sp.Children.Add(icon); sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }
    static UIElement FolderIcon() => Ic.Icon(MaterialDesignThemes.Wpf.PackIconKind.FolderOutline, 16);
    TreeViewItem Node(object tag, object header) => new() { Header = header, Tag = tag, IsExpanded = true, Foreground = K.B("Fg") };

    public void Fill()
    {
        var sel = (tree.SelectedItem as TreeViewItem)?.Tag;
        var q = search.Text.Trim().ToLowerInvariant();
        tree.Items.Clear();
        var root = Node("", Header(FolderIcon(), "Bookmarks bar"));
        if (q != "")
        { foreach (var b in Store.D.Bookmarks.Where(b => (b.Title + b.Url).ToLowerInvariant().Contains(q))) root.Items.Add(Node(b, Header(MainWindow.BmIcon(b), b.Title + "   —   " + b.Url))); }
        else AddChildren(root, "");
        tree.Items.Add(root);
        if (sel != null) Reselect(root, sel);
        bool none = Store.D.Bookmarks.Count == 0 && Store.D.Folders.Count == 0;
        empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed; tree.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
    }
    void AddChildren(TreeViewItem parent, string folder)
    {
        foreach (var f in Store.SubFolders(folder)) { var n = Node(f, Header(FolderIcon(), Store.LeafName(f))); AddChildren(n, f); parent.Items.Add(n); }
        foreach (var b in Store.D.Bookmarks.Where(b => b.Folder == folder)) parent.Items.Add(Node(b, Header(MainWindow.BmIcon(b), b.Title + "   —   " + b.Url)));
    }
    bool Reselect(TreeViewItem n, object tag)
    {
        if (Equals(n.Tag, tag)) { n.IsSelected = true; return true; }
        foreach (TreeViewItem c in n.Items) if (Reselect(c, tag)) return true;
        return false;
    }

    object Sel => (tree.SelectedItem as TreeViewItem)?.Tag;
    string SelFolderForNew() => Sel is string f ? f : Sel is Bookmark b ? b.Folder : "";

    void OpenSel(bool current)
    {
        if (Sel is Bookmark b) { if (current) main.Navigate(b.Url); else main.NewTab(b.Url, true); }
        else if (Sel is string f) main.OpenAll(f);
    }
    void EditSel()
    {
        if (Sel is Bookmark b) { new BookmarkDialog(main, b, false) { Owner = this }.ShowDialog(); MainWindow.RefreshAll(); Fill(); }
        else if (Sel is string f && f != "") { var n = main.AskFolderName("Rename folder", Store.LeafName(f)); if (!string.IsNullOrEmpty(n)) { Store.RenameFolder(f, n); MainWindow.RefreshAll(); Fill(); } }
    }
    void DeleteSel()
    {
        if (Sel is Bookmark b) Store.D.Bookmarks.Remove(b);
        else if (Sel is string f && f != "")
        {
            if (MessageBox.Show(this, $"Delete folder \"{Store.LeafName(f)}\" and everything in it?", "Delete folder", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            Store.DeleteFolder(f);
        }
        else return;
        Store.Save(); MainWindow.RefreshAll(); Fill();
    }

    ContextMenu BuildMenu()
    {
        var sel = Sel; if (sel == null) return null;
        var m = new ContextMenu();
        MenuItem I(string h, Action a) => Ic.Item(h, a);
        if (sel is Bookmark b)
        {
            m.Items.Add(I("Open", () => OpenSel(true))); m.Items.Add(I("Open in new tab", () => OpenSel(false)));
            m.Items.Add(I("Edit…", EditSel));
            var mv = Ic.Item("Move to", null);
            mv.Items.Add(Ic.Item("Bookmarks bar", () => Move(b, ""), null, MaterialDesignThemes.Wpf.PackIconKind.FolderOutline));
            foreach (var f in Store.FoldersSorted()) { var fp = f; mv.Items.Add(Ic.Item(new string(' ', 3 * fp.Count(c => c == '/')) + Store.LeafName(fp), () => Move(b, fp), null, MaterialDesignThemes.Wpf.PackIconKind.FolderOutline)); }
            m.Items.Add(mv); m.Items.Add(new Separator()); m.Items.Add(I("Delete", DeleteSel));
        }
        else if (sel is string f)
        {
            m.Items.Add(I("Open all in new tabs", () => OpenSel(false)));
            m.Items.Add(I("New subfolder…", () => { main.NewFolderPrompt(f); Fill(); }));
            if (f != "") { m.Items.Add(I("Rename…", EditSel)); m.Items.Add(new Separator()); m.Items.Add(I("Delete folder", DeleteSel)); }
        }
        return m;
    }

    // ---- Netscape bookmark HTML, with nested folders
    void Export()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "HTML|*.html", FileName = "bookmarks.html" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, ToHtml());
    }
    public static string ToHtml()
    {
        var sb = new System.Text.StringBuilder("<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE>\n<H1>Bookmarks</H1>\n");
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        void Write(string folder, int depth)
        {
            var pad = new string(' ', depth * 4);
            sb.Append(pad).Append("<DL><p>\n");
            foreach (var f in Store.SubFolders(folder)) { sb.Append(pad).Append($"    <DT><H3>{E(Store.LeafName(f))}</H3>\n"); Write(f, depth + 1); }
            foreach (var b in Store.D.Bookmarks.Where(b => b.Folder == folder)) sb.Append(pad).Append($"    <DT><A HREF=\"{E(b.Url)}\">{E(b.Title)}</A>\n");
            sb.Append(pad).Append("</DL><p>\n");
        }
        Write("", 0);
        return sb.ToString();
    }
    void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "HTML|*.html;*.htm" };
        if (dlg.ShowDialog() != true) return;
        int added = FromHtml(File.ReadAllText(dlg.FileName));
        MainWindow.RefreshAll(); Fill();
        MessageBox.Show(this, $"Imported {added} bookmarks.", "Import");
    }
    public static int FromHtml(string html)
    {
        var stack = new Stack<string>(); string cur = "", pending = null; int added = 0;
        static string D(string s) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "")).Trim();
        var rx = new System.Text.RegularExpressions.Regex("<H3[^>]*>(.*?)</H3>|<A\\s[^>]*?HREF=\"([^\"]+)\"[^>]*>(.*?)</A>|<DL[^>]*>|</DL>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match m in rx.Matches(html))
        {
            var t = m.Value.ToUpperInvariant();
            if (t.StartsWith("<H3")) pending = Store.CleanName(D(m.Groups[1].Value));
            else if (t.StartsWith("<DL")) { stack.Push(cur); if (pending != null) { cur = Store.AddFolder(cur, pending) ?? cur; pending = null; } }
            else if (t.StartsWith("</DL")) { if (stack.Count > 0) cur = stack.Pop(); }
            else
            {
                var u = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value);
                if (u.StartsWith("http") && !Store.D.Bookmarks.Any(b => b.Url == u && b.Folder == cur)) { Store.D.Bookmarks.Add(new Bookmark { Url = u, Title = D(m.Groups[3].Value), Folder = cur }); added++; }
            }
        }
        Store.Save();
        return added;
    }
}
