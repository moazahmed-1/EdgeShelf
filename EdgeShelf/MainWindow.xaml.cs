using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EdgeShelf;

public partial class MainWindow : Window
{
    // ---- Size ----
    private const double PanelWidth = 136;      // width of the open shelf (= window width, never changes)
    private const double PillWidth = 5;         // closed handle
    private const double PillHeight = 80;
    private const double HeightRatio = 0.62;    // shelf height relative to the screen work area
    private const double PanelRadius = 18;

    // ---- Mouse detection ----
    private const double EdgeZone = 4;          // how close to the screen edge counts as "at the edge"
    private const double HoverHalfRange = 60;   // vertical hover range around the handle
    private const int PollMs = 40;
    private const int FullscreenCheckMs = 150;  // how often we look for a full-screen game/video
    private const int DwellMs = 180;            // hover time at the edge before opening
    private const int LeaveMs = 380;            // time away from the shelf before closing

    // ---- Animation ----
    private const int OpenMs = 420;
    private const int CloseMs = 260;

    private const int MaxItems = 40;

    // iOS-style curves: fast start, long soft landing
    private static readonly CubicBezierEase OpenEase = MakeEase(0.32, 0.72, 0.0, 1.0);
    private static readonly CubicBezierEase CloseEase = MakeEase(0.4, 0.0, 0.2, 1.0);

    private static readonly Color PillColor = Color.FromArgb(0x8C, 0xF2, 0xF4, 0xFA);

    private readonly ObservableCollection<ShelfItem> _items = new();
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _trimTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly SolidColorBrush _fill = new(PillColor);

    private TrayIcon? _tray;

    private double _panelHeight = 600;
    private double _scale = 1.0;
    private long _lastTick;
    private long _lastFullscreenCheck;
    private IntPtr _hwnd;
    private bool _hiddenForFullscreen;
    private int _animToken;

    private bool _expanded;
    private bool _paused;
    private bool _isDraggingOut;
    private bool _prevButtonDown;
    private bool _pressedAtEdge;
    private int _dwellMs;
    private int _leaveMs;

    private ShelfItem? _pressedItem;
    private Point _pressPoint;
    private bool _dragStarted;

    private bool OnRight => _settings.Side != "Left";

    private Color TintColor
    {
        get
        {
            byte alpha = _settings.Glass switch
            {
                "Clear" => 0x2E,
                "Dark" => 0xC8,
                _ => 0x5C
            };
            return Color.FromArgb(alpha, 0x14, 0x16, 0x1C);
        }
    }

    private static CubicBezierEase MakeEase(double x1, double y1, double x2, double y2)
    {
        var ease = new CubicBezierEase(x1, y1, x2, y2);
        ease.Freeze();
        return ease;
    }

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();

        foreach (var item in ItemStore.Load())
            _items.Add(item);

        _items.CollectionChanged += (_, _) =>
        {
            UpdateEmptyHint();
            ItemStore.Save(_items);
        };
        UpdateEmptyHint();

        RootBorder.Background = _fill;
        RootBorder.SizeChanged += (_, _) => UpdateRadius();

        PositionWindow();
        ApplySide();

        _pollTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(PollMs) };
        _pollTimer.Tick += Tick;

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBox.Visibility = Visibility.Collapsed;
        };

        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            if (!_expanded && !_isDraggingOut) TrimMemory();
        };

        Loaded += (_, _) =>
        {
            _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _tray = new TrayIcon("EdgeShelf", BuildMenu);
            _lastTick = Environment.TickCount64;
            _pollTimer.Start();

            // Fill the list after startup finishes, so launching stays instant
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                ItemsHost.ItemsSource = _items;
                ScheduleTrim();
            }));
        };

        Application.Current.Exit += (_, _) => _tray?.Dispose();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Keep the shelf out of Alt+Tab and never steal focus from the app you're working in
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwnd = hwnd;
        int style = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
            style | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _scale = newDpi.DpiScaleX;
        base.OnDpiChanged(oldDpi, newDpi);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_SETTINGCHANGE = 0x001A;
        const long SPI_SETWORKAREA = 0x002F;

        if (msg == WM_DISPLAYCHANGE || (msg == WM_SETTINGCHANGE && wParam.ToInt64() == SPI_SETWORKAREA))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PositionWindow();
                SettleInstant();
            }));
        }
        return IntPtr.Zero;
    }

    // =========================================================
    //  Position and shape
    // =========================================================

    private void PositionWindow()
    {
        var wa = SystemParameters.WorkArea;
        _panelHeight = Math.Max(320, wa.Height * HeightRatio);

        Width = PanelWidth;
        Height = _panelHeight;
        Left = OnRight ? wa.Right - PanelWidth : wa.Left;
        Top = wa.Top + (wa.Height - _panelHeight) / 2;

        ContentGrid.Width = PanelWidth;
        ContentGrid.Height = _panelHeight;
    }

    /// <summary>Applies the chosen side (left/right) to everything that depends on it.</summary>
    private void ApplySide()
    {
        var align = OnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        RootBorder.HorizontalAlignment = align;
        ContentGrid.HorizontalAlignment = align;
        DropRing.CornerRadius = OnRight ? new CornerRadius(PanelRadius, 0, 0, PanelRadius)
                                        : new CornerRadius(0, PanelRadius, PanelRadius, 0);
        PositionWindow();
        SettleInstant();
    }

    /// <summary>Round only the inner corners, and never more than half the current size (so the thin handle stays a clean capsule).</summary>
    private void UpdateRadius()
    {
        double w = RootBorder.ActualWidth;
        double h = RootBorder.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double r = Math.Min(PanelRadius, Math.Min(w, h) / 2.0);
        var radius = OnRight ? new CornerRadius(r, 0, 0, r) : new CornerRadius(0, r, r, 0);
        if (RootBorder.CornerRadius != radius)
            RootBorder.CornerRadius = radius;
    }

    /// <summary>Jump straight to the final look of the current state (no animation).</summary>
    private void SettleInstant()
    {
        _animToken++;

        RootBorder.BeginAnimation(WidthProperty, null);
        RootBorder.BeginAnimation(HeightProperty, null);
        _fill.BeginAnimation(SolidColorBrush.ColorProperty, null);
        ContentGrid.BeginAnimation(OpacityProperty, null);

        RootBorder.Width = _expanded ? PanelWidth : PillWidth;
        RootBorder.Height = _expanded ? _panelHeight : PillHeight;
        _fill.Color = _expanded ? TintColor : PillColor;
        ContentGrid.Opacity = _expanded ? 1 : 0;
        ContentGrid.IsHitTestVisible = _expanded;
        UpdateRadius();

        if (!_expanded) ScheduleTrim();
    }

    /// <summary>
    /// Morph between the thin handle and the full shelf: size and colour move together on one curve,
    /// then the content fades in a moment later (like a Dynamic Island).
    /// </summary>
    private void Animate(bool expand)
    {
        int token = ++_animToken;
        var duration = TimeSpan.FromMilliseconds(expand ? OpenMs : CloseMs);
        var ease = expand ? OpenEase : CloseEase;

        var widthAnim = new DoubleAnimation(expand ? PanelWidth : PillWidth, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(expand ? _panelHeight : PillHeight, duration) { EasingFunction = ease };
        var colorAnim = new ColorAnimation(expand ? TintColor : PillColor, duration) { EasingFunction = ease };

        // When the motion ends, replace the animations with plain values so nothing keeps running
        widthAnim.Completed += (_, _) =>
        {
            if (token == _animToken) SettleInstant();
        };

        RootBorder.BeginAnimation(WidthProperty, widthAnim);
        RootBorder.BeginAnimation(HeightProperty, heightAnim);
        _fill.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

        DoubleAnimation fade = expand
            ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(130) }
            : new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        ContentGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        _leaveMs = 0;
        _trimTimer.Stop();
        ContentGrid.IsHitTestVisible = true;
        Animate(true);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _dwellMs = 0;
        ContentGrid.IsHitTestVisible = false;
        Animate(false);
    }

    private void SetHiddenForFullscreen(bool hide)
    {
        _hiddenForFullscreen = hide;
        _dwellMs = 0;
        _leaveMs = 0;

        if (hide)
        {
            // Close instantly (no animation), then take the window off the screen
            _expanded = false;
            SettleInstant();
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
        }
        else
        {
            Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        }
    }

    private void UpdateEmptyHint()
    {
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Toast(string message)
    {
        ToastText.Text = message;
        ToastBox.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // =========================================================
    //  Memory
    // =========================================================

    private void ScheduleTrim()
    {
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    /// <summary>
    /// While the shelf sits closed we hand unused memory back to Windows.
    /// That's what keeps the number in Task Manager low.
    /// </summary>
    private static void TrimMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch
        {
            // not important
        }
    }

    // =========================================================
    //  Opening and closing (mouse tracking)
    // =========================================================

    private void Tick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        int dt = (int)Math.Min(now - _lastTick, 200);
        _lastTick = now;

        // A game, video or full-screen browser is running: vanish completely until it ends
        if (now - _lastFullscreenCheck >= FullscreenCheckMs)
        {
            _lastFullscreenCheck = now;
            bool fullscreen = Native.IsFullscreenActive(_hwnd);
            if (fullscreen != _hiddenForFullscreen)
                SetHiddenForFullscreen(fullscreen);
        }
        if (_hiddenForFullscreen) return;

        if (!Native.GetCursorPos(out var p)) return;

        // Watch the other button if the mouse buttons are swapped (left-handed setup)
        int vk = Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0 ? 0x02 : 0x01;
        bool down = (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

        double x = p.X / _scale;
        double y = p.Y / _scale;
        var wa = SystemParameters.WorkArea;

        double winLeft = OnRight ? wa.Right - PanelWidth : wa.Left;
        double winTop = Top;
        double centerY = winTop + _panelHeight / 2;

        bool inDragRangeY = y >= winTop && y <= winTop + _panelHeight;
        bool inHoverRangeY = Math.Abs(y - centerY) <= HoverHalfRange;
        bool atEdge = OnRight
            ? x >= wa.Right - EdgeZone && x <= wa.Right + 1
            : x <= wa.Left + EdgeZone && x >= wa.Left - 1;

        // Remember if the press started at the edge (e.g. grabbing a scrollbar) so it doesn't open the shelf
        if (!_prevButtonDown && down)
            _pressedAtEdge = atEdge && inDragRangeY;
        _prevButtonDown = down;

        if (_paused)
        {
            if (_expanded) Collapse();
            return;
        }

        if (!_expanded)
        {
            if (atEdge && down && !_pressedAtEdge && inDragRangeY)
            {
                // Something is being dragged towards the edge: open right away
                Expand();
            }
            else if (atEdge && !down && inHoverRangeY)
            {
                _dwellMs += dt;
                if (_dwellMs >= DwellMs) Expand();
            }
            else
            {
                _dwellMs = 0;
            }
        }
        else
        {
            if (_isDraggingOut)
            {
                _leaveMs = 0;
                return;
            }

            const double margin = 12;
            bool inside =
                x >= winLeft - margin && x <= winLeft + PanelWidth + margin &&
                y >= winTop - margin && y <= winTop + _panelHeight + margin;

            if (inside)
            {
                _leaveMs = 0;
            }
            else
            {
                _leaveMs += dt;
                if (_leaveMs >= LeaveMs) Collapse();
            }
        }
    }

    // =========================================================
    //  Adding items
    // =========================================================

    private int AddPaths(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            bool exists = _items.Any(i =>
                i.Kind != ItemKind.Text && string.Equals(i.Value, path, StringComparison.OrdinalIgnoreCase));
            if (exists) continue;

            ItemKind kind;
            if (Directory.Exists(path)) kind = ItemKind.Folder;
            else if (File.Exists(path)) kind = ItemKind.File;
            else continue;

            _items.Insert(0, new ShelfItem(kind, path));
            added++;
        }
        TrimItems();
        return added;
    }

    private bool AddText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        if (_items.Any(i => i.Kind == ItemKind.Text && i.Value == text)) return false;

        _items.Insert(0, new ShelfItem(ItemKind.Text, text));
        TrimItems();
        return true;
    }

    private void TrimItems()
    {
        while (_items.Count > MaxItems)
            _items.RemoveAt(_items.Count - 1);
    }

    private void SetDropHighlight(bool on)
    {
        DropRing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        SetDropHighlight(true);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        e.Handled = true;

        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    int n = AddPaths(files);
                    Toast(n > 0 ? "Added" : "Already on the shelf");
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                bool added = AddText(e.Data.GetData(DataFormats.UnicodeText) as string);
                Toast(added ? "Text added" : "Text already on the shelf");
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                bool added = AddText(e.Data.GetData(DataFormats.Text) as string);
                Toast(added ? "Text added" : "Text already on the shelf");
            }
            else
            {
                Toast("Unsupported item");
            }
        }
        catch
        {
            Toast("Couldn't add that");
        }
    }

    // =========================================================
    //  Buttons
    // =========================================================

    private void AddClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int n = 0;
            if (Clipboard.ContainsFileDropList())
            {
                var list = Clipboard.GetFileDropList();
                n += AddPaths(list.Cast<string>());
            }
            else if (Clipboard.ContainsText())
            {
                if (AddText(Clipboard.GetText())) n++;
            }
            Toast(n > 0 ? "Added from clipboard" : "Nothing new on the clipboard");
        }
        catch
        {
            Toast("Clipboard is busy, try again");
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        Toast("Shelf cleared");
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ShelfItem item)
            _items.Remove(item);
    }

    // =========================================================
    //  Working with an item: drag out / click / double-click
    // =========================================================

    private static bool IsFromButton(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is Button) return true;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsFromButton(e.OriginalSource as DependencyObject)) return;

        _pressedItem = (sender as FrameworkElement)?.DataContext as ShelfItem;
        _pressPoint = e.GetPosition(this);
        _dragStarted = false;

        // Double-click opens the file or folder
        if (e.ClickCount == 2 && _pressedItem != null && _pressedItem.Kind != ItemKind.Text)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_pressedItem.Value) { UseShellExecute = true });
            }
            catch
            {
                Toast("Couldn't open it");
            }
            _pressedItem = null;
        }
    }

    private void Item_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedItem == null || _dragStarted || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStarted = true;
        var item = _pressedItem;
        _pressedItem = null;

        var data = item.CreateDataObject();
        if (data == null)
        {
            Toast("File was moved or deleted");
            return;
        }

        _isDraggingOut = true;
        try
        {
            // Copy: the original stays where it is, you get a copy wherever you drop it
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
        finally
        {
            _isDraggingOut = false;
        }
    }

    private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsFromButton(e.OriginalSource as DependencyObject)) return;

        var item = _pressedItem;
        bool wasDrag = _dragStarted;
        _pressedItem = null;
        _dragStarted = false;

        // One click on a text card copies it to the clipboard
        if (item != null && !wasDrag && item.Kind == ItemKind.Text)
        {
            try
            {
                Clipboard.SetText(item.Value);
                Toast("Copied. Paste anywhere");
            }
            catch
            {
                Toast("Clipboard is busy, try again");
            }
        }
    }

    // =========================================================
    //  Tray icon menu (right-click the icon next to the clock)
    // =========================================================

    private List<TrayMenuEntry> BuildMenu()
    {
        return new List<TrayMenuEntry>
        {
            new() { Text = OnRight ? "Move to the left edge" : "Move to the right edge", Click = SwitchSide },
            new() { Separator = true },
            new() { Text = "Look: Clear", Checked = _settings.Glass == "Clear", Click = () => SetGlass("Clear") },
            new() { Text = "Look: Glass", Checked = _settings.Glass == "Glass", Click = () => SetGlass("Glass") },
            new() { Text = "Look: Dark", Checked = _settings.Glass == "Dark", Click = () => SetGlass("Dark") },
            new() { Separator = true },
            new() { Text = "Pause", Checked = _paused, Click = () => _paused = !_paused },
            new() { Text = "Start with Windows", Checked = Autostart.IsEnabled(), Click = () => Autostart.Set(!Autostart.IsEnabled()) },
            new() { Separator = true },
            new() { Text = "Exit", Click = () => Application.Current.Shutdown() }
        };
    }

    private void SwitchSide()
    {
        _settings.Side = OnRight ? "Left" : "Right";
        _settings.Save();
        _expanded = false;
        ApplySide();
    }

    private void SetGlass(string preset)
    {
        _settings.Glass = preset;
        _settings.Save();
        SettleInstant();
    }
}
