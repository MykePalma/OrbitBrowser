using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lite;

/// <summary>Add/edit one bookmark: name, address, folder (with "New folder…").</summary>
public class BookmarkDialog : Window
{
    readonly Bookmark bm; readonly MainWindow main;
    readonly TextBox name, url; readonly ComboBox folder = new() { Margin = new Thickness(4) };
    List<string> paths = new();

    public BookmarkDialog(MainWindow owner, Bookmark b, bool isNew)
    {
        bm = b; main = owner;
        Title = isNew ? "Bookmark added" : "Edit bookmark"; Width = 420; Height = 300; ResizeMode = ResizeMode.NoResize;
        Background = K.B("Bg"); Foreground = K.B("Fg"); Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontSize = 14;
        var s = new StackPanel { Margin = new Thickness(14) };
        s.Children.Add(new TextBlock { Text = "Name" }); name = K.Box(b.Title); s.Children.Add(name);
        s.Children.Add(new TextBlock { Text = "Address" }); url = K.Box(b.Url); s.Children.Add(url);
        s.Children.Add(new TextBlock { Text = "Folder" });
        var row = new DockPanel();
        var nf = K.Btn("New folder…", (_, _) =>
        {
            var parent = Selected(); var n = main.AskFolderName("New folder"); if (string.IsNullOrEmpty(n)) return;
            Fill(Store.AddFolder(parent, n));
        });
        DockPanel.SetDock(nf, Dock.Right); row.Children.Add(nf); row.Children.Add(folder); s.Children.Add(row);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        btns.Children.Add(K.Btn("Remove", (_, _) => { Store.D.Bookmarks.Remove(bm); Store.Save(); DialogResult = false; }));
        var done = K.Btn("Done", (_, _) => Apply()); done.IsDefault = true; btns.Children.Add(done);
        s.Children.Add(btns); Content = s;
        Fill(b.Folder);
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
    string Selected() => folder.SelectedIndex >= 0 && folder.SelectedIndex < paths.Count ? paths[folder.SelectedIndex] : "";
    void Fill(string select)
    {
        paths = new List<string> { "" }; folder.Items.Clear(); folder.Items.Add("📁 Bookmarks bar");
        foreach (var f in Store.FoldersSorted()) { paths.Add(f); folder.Items.Add(new string(' ', 4 * f.Count(c => c == '/')) + "📁 " + Store.LeafName(f)); }
        folder.SelectedIndex = Math.Max(0, paths.IndexOf(select ?? ""));
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
public class BookmarkManager : Window
{
    readonly MainWindow main;
    readonly TreeView tree = new() { Background = K.B("Bg"), Foreground = K.B("Fg"), BorderThickness = new Thickness(0) };
    readonly TextBox search = K.Box();
    Point dragStart; object dragItem;

    public BookmarkManager(MainWindow owner)
    {
        main = owner; Title = "Bookmarks"; Width = 760; Height = 560; Background = K.B("Bg"); Foreground = K.B("Fg"); Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontSize = 14;
        var bar = new WrapPanel { Margin = new Thickness(4) };
        bar.Children.Add(K.Btn("New folder", (_, _) => { main.NewFolderPrompt(SelFolderForNew()); Fill(); }));
        bar.Children.Add(K.Btn("Open", (_, _) => OpenSel(true)));
        bar.Children.Add(K.Btn("Open in new tab", (_, _) => OpenSel(false)));
        bar.Children.Add(K.Btn("Rename / Edit", (_, _) => EditSel()));
        bar.Children.Add(K.Btn("Delete", (_, _) => DeleteSel()));
        bar.Children.Add(K.Btn("Export HTML", (_, _) => Export()));
        bar.Children.Add(K.Btn("Import HTML", (_, _) => Import()));
        var top = new StackPanel(); top.Children.Add(search); top.Children.Add(bar);
        top.Children.Add(new TextBlock { Text = "Drag bookmarks and folders onto a folder to move them. Right-click for more.", Foreground = K.B("Mut"), FontSize = 12, Margin = new Thickness(8, 0, 8, 6) });
        var d = new DockPanel(); DockPanel.SetDock(top, Dock.Top); d.Children.Add(top); d.Children.Add(tree); Content = d;

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
