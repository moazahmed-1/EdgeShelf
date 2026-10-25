using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EdgeShelf;

/// <summary>إعدادات البرنامج (دلوقتي: الجنب اللي الدرج عليه).</summary>
internal class AppSettings
{
    public string Side { get; set; } = "Right"; // Right or Left

    /// <summary>How see-through the shelf is: Clear, Glass or Dark.</summary>
    public string Glass { get; set; } = "Glass";

    public static string DataDir
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeShelf");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // لو الملف بايظ نرجع للإعدادات الافتراضية
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // نتجاهل
        }
    }
}

public record ItemDto(string Kind, string Value);

/// <summary>حفظ محتويات الدرج عشان ترجع بعد ما تقفل البرنامج وتفتحه.</summary>
internal static class ItemStore
{
    private static string FilePath => Path.Combine(AppSettings.DataDir, "items.json");

    public static List<ShelfItem> Load()
    {
        var result = new List<ShelfItem>();
        try
        {
            if (!File.Exists(FilePath)) return result;

            var dtos = JsonSerializer.Deserialize<List<ItemDto>>(File.ReadAllText(FilePath));
            if (dtos == null) return result;

            foreach (var dto in dtos)
            {
                if (Enum.TryParse<ItemKind>(dto.Kind, out var kind) && !string.IsNullOrEmpty(dto.Value))
                    result.Add(new ShelfItem(kind, dto.Value));
            }
        }
        catch
        {
            // لو الملف بايظ نبدأ بدرج فاضي
        }
        return result;
    }

    public static void Save(IEnumerable<ShelfItem> items)
    {
        try
        {
            var dtos = items.Select(i => new ItemDto(i.Kind.ToString(), i.Value)).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(dtos));
        }
        catch
        {
            // نتجاهل
        }
    }
}
