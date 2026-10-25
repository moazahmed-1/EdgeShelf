using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeShelf;

/// <summary>
/// Gets the same high-quality file/folder icon Windows Explorer shows (up to 256x256),
/// then shrinks it to a small bitmap so each item costs very little memory.
/// </summary>
internal static class ShellIcons
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const int SHIL_EXTRALARGE = 0x2; // 48x48
    private const int SHIL_JUMBO = 0x4;      // 256x256
    private const int ILD_TRANSPARENT = 0x1;
    private const int TargetSize = 64;

    private static readonly Guid IidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    public static ImageSource? Load(string path)
    {
        return FromImageList(path, SHIL_JUMBO)
               ?? FromImageList(path, SHIL_EXTRALARGE)
               ?? FromShellIcon(path);
    }

    private static ImageSource? FromImageList(string path, int listSize)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var info = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX);
            if (result == IntPtr.Zero) return null;

            Guid iid = IidImageList;
            if (SHGetImageList(listSize, ref iid, out IImageList? list) != 0 || list == null) return null;

            if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out hIcon) != 0 || hIcon == IntPtr.Zero) return null;

            return ToSmallBitmap(hIcon);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) Native.DestroyIcon(hIcon);
        }
    }

    private static ImageSource? FromShellIcon(string path)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var info = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            hIcon = info.hIcon;
            return ToSmallBitmap(hIcon);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) Native.DestroyIcon(hIcon);
        }
    }

    private static ImageSource? ToSmallBitmap(IntPtr hIcon)
    {
        BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
            hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        if (source.PixelWidth > TargetSize)
        {
            double scale = (double)TargetSize / source.PixelWidth;
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            // OnLoad copies the (now small) pixels, so the 256px original can be released
            source = new CachedBitmap(scaled, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }

        source.Freeze();
        return source;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList? ppv);
}

/// <summary>Partial IImageList definition. Method order matters (unused ones are there to keep the order).</summary>
[ComImport]
[Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IImageList
{
    [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
    [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
    [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
    [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
    [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
    [PreserveSig] int Draw(IntPtr pimldp);
    [PreserveSig] int Remove(int i);
    [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
}
