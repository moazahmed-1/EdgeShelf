using System;
using Microsoft.Win32;

namespace EdgeShelf;

/// <summary>تشغيل البرنامج مع ويندوز (بيتسجل في خاصية Run للمستخدم الحالي بس).</summary>
internal static class Autostart
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EdgeShelf";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            if (key == null) return;

            if (enabled)
                key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
            else
                key.DeleteValue(ValueName, false);
        }
        catch
        {
            // مش مشكلة كبيرة لو فشل
        }
    }
}
