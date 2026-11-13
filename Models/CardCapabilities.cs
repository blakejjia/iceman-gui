using System;

namespace iceman_gui.Models;

public enum MagicWriteMode
{
    None,
    Gen1a,
    Gen2
}

/// <summary>
/// What the connected card actually supports, derived from the scan result.
/// The UI must gate actions on these flags instead of guessing from the
/// protocol family alone.
/// </summary>
public sealed record CardCapabilities(
    bool IsClassic,
    string SizeArgument,
    MagicWriteMode Magic,
    bool CanBackup,
    bool CanWriteData,
    bool CanChangeUid,
    string Summary)
{
    public static CardCapabilities For(TagInfo? tag)
    {
        if (tag == null)
        {
            return new CardCapabilities(false, string.Empty, MagicWriteMode.None,
                false, false, false, "Read a card first.");
        }

        string type = tag.TagType ?? string.Empty;
        bool isClassic = type.Contains("MIFARE Classic", StringComparison.OrdinalIgnoreCase)
                         || type.Contains("MIFARE Mini", StringComparison.OrdinalIgnoreCase);

        string sizeArgument = type.Contains("4K", StringComparison.OrdinalIgnoreCase) ? "--4k"
            : type.Contains("2K", StringComparison.OrdinalIgnoreCase) ? "--2k"
            : type.Contains("Mini", StringComparison.OrdinalIgnoreCase) ? "--mini"
            : "--1k";

        MagicWriteMode magic = tag.IsGen1a ? MagicWriteMode.Gen1a
            : tag.IsGen2Cuid ? MagicWriteMode.Gen2
            : MagicWriteMode.None;

        string summary = !isClassic
            ? "Read-only: automatic backup supports MIFARE Classic cards."
            : magic switch
            {
                MagicWriteMode.Gen1a => "UID writable (Gen 1a); full clone supported.",
                MagicWriteMode.Gen2 => "UID writable (Gen 2/CUID); full clone supported.",
                _ => "Clone data only; the UID is locked on this card."
            };

        return new CardCapabilities(
            isClassic,
            sizeArgument,
            magic,
            CanBackup: isClassic,
            CanWriteData: isClassic,
            CanChangeUid: magic != MagicWriteMode.None,
            summary);
    }
}
