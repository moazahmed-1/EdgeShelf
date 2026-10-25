using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace EdgeShelf;

public enum ItemKind
{
    File,
    Folder,
    Text
}

/// <summary>One item on the shelf: a file, a folder or a piece of text.</summary>
public sealed class ShelfItem
{
    private ImageSource? _icon;
    private bool _iconLoaded;

    public ItemKind Kind { get; }

    /// <summary>File/folder path, or the text itself.</summary>
    public string Value { get; }

    public ShelfItem(ItemKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>Explorer icon, loaded the first time it's needed.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                if (Kind != ItemKind.Text)
                    _icon = ShellIcons.Load(Value);
            }
            return _icon;
        }
    }

    public bool IsText => Kind == ItemKind.Text;

    // ---- Which part of the card template shows ----
    public Visibility TextVisibility => IsText ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FileVisibility => IsText ? Visibility.Collapsed : Visibility.Visible;
    public Visibility IconVisibility => !IsText && Icon != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FallbackDocVisibility => Kind == ItemKind.File && Icon == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FallbackFolderVisibility => Kind == ItemKind.Folder && Icon == null ? Visibility.Visible : Visibility.Collapsed;

    public string Label
    {
        get
        {
            string name = Path.GetFileName(Value.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? Value : name;
        }
    }

    /// <summary>Short one-paragraph preview for text cards.</summary>
    public string Preview
    {
        get
        {
            if (!IsText) return "";
            string flat = string.Join(" ", Value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return flat.Length > 110 ? flat.Substring(0, 110).TrimEnd() + "…" : flat;
        }
    }

    /// <summary>Arabic/Hebrew text is laid out right-to-left, everything else left-to-right.</summary>
    public FlowDirection TextFlow
    {
        get
        {
            // The first letter decides the direction
            foreach (char c in Value)
            {
                if (!char.IsLetter(c)) continue;
                return c >= 0x0590 && c <= 0x08FF ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            }
            return FlowDirection.LeftToRight;
        }
    }

    public string Tooltip
    {
        get
        {
            if (!IsText) return Value;
            return Value.Length > 300 ? Value.Substring(0, 300) + "…" : Value;
        }
    }

    /// <summary>Data sent when the item is dragged out of the shelf. Null if the file is gone.</summary>
    public DataObject? CreateDataObject()
    {
        if (Kind == ItemKind.Text)
            return new DataObject(DataFormats.UnicodeText, Value);

        if (!File.Exists(Value) && !Directory.Exists(Value))
            return null;

        return new DataObject(DataFormats.FileDrop, new[] { Value });
    }
}
