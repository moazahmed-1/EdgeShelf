using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EdgeShelf;

internal sealed class TrayMenuEntry
{
    public string Text = "";
    public bool Checked;
    public bool Separator;
    public Action? Click;
}

/// <summary>
/// Notification-area icon built directly on Win32.
/// (Replaces WinForms' NotifyIcon, which pulled in two big libraries just for one icon.)
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WM_TRAY = 0x8001;      // WM_APP + 1
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_NULL = 0x0000;

    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;

    private const uint MF_STRING = 0x0;
    private const uint MF_CHECKED = 0x8;
    private const uint MF_SEPARATOR = 0x800;

    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint TPM_BOTTOMALIGN = 0x20;
    private const uint TPM_NONOTIFY = 0x80;
    private const uint TPM_RETURNCMD = 0x100;

    private readonly HwndSource _source;
    private readonly Func<List<TrayMenuEntry>> _buildMenu;
    private readonly string _tip;
    private readonly uint _taskbarCreated;
    private readonly bool _ownsIcon;
    private IntPtr _icon;
    private bool _disposed;

    public TrayIcon(string tip, Func<List<TrayMenuEntry>> buildMenu)
    {
        _tip = tip;
        _buildMenu = buildMenu;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // Invisible message-only window that receives the icon's mouse messages
        var parameters = new HwndSourceParameters("EdgeShelfTray")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _icon = LoadAppIcon(out _ownsIcon);
        AddIcon();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            if (ev == WM_RBUTTONUP || ev == WM_LBUTTONUP)
                ShowMenu();
            handled = true;
        }
        else if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
        {
            // Explorer restarted: put the icon back
            AddIcon();
        }
        return IntPtr.Zero;
    }

    private NOTIFYICONDATA NewData()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            szTip = _tip,
            szInfo = "",
            szInfoTitle = ""
        };
        return data;
    }

    private void AddIcon()
    {
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAY;
        data.hIcon = _icon;
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private void ShowMenu()
    {
        var entries = _buildMenu();
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Separator)
                {
                    AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                }
                else
                {
                    uint flags = MF_STRING | (entry.Checked ? MF_CHECKED : 0);
                    AppendMenu(menu, flags, (UIntPtr)(uint)(i + 1), entry.Text);
                }
            }

            Native.GetCursorPos(out var pt);

            // Required so the menu closes when you click somewhere else
            SetForegroundWindow(_source.Handle);
            uint cmd = TrackPopupMenu(
                menu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY | TPM_BOTTOMALIGN,
                pt.X, pt.Y, 0, _source.Handle, IntPtr.Zero);
            PostMessage(_source.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd >= 1 && cmd <= entries.Count)
                entries[(int)cmd - 1].Click?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static IntPtr LoadAppIcon(out bool owns)
    {
        owns = false;
        try
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var small = new IntPtr[1];
                if (ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                {
                    owns = true;
                    return small[0];
                }
            }
        }
        catch
        {
            // fall through
        }
        return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var data = NewData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _source.Dispose();
        if (_ownsIcon && _icon != IntPtr.Zero) Native.DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }

    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
