using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace iceman_gui.Models;

public partial class MifareBlock : ObservableObject
{
    private static readonly SolidColorBrush CyanBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8);
    private static readonly SolidColorBrush GreenBrush = CreateFrozenBrush(0x4A, 0xDE, 0x80);
    private static readonly SolidColorBrush GreenBorderBrush = CreateFrozenBrush(0x22, 0xC5, 0x5E);
    private static readonly SolidColorBrush GreenBgBrush = CreateFrozenBrush(0x14, 0x29, 0x1E);
    private static readonly SolidColorBrush RedBrush = CreateFrozenBrush(0xF8, 0x71, 0x71);
    private static readonly SolidColorBrush RedBorderBrush = CreateFrozenBrush(0xEF, 0x44, 0x44);
    private static readonly SolidColorBrush RedBgBrush = CreateFrozenBrush(0x2A, 0x12, 0x15);
    private static readonly SolidColorBrush DefaultBgBrush = CreateFrozenBrush(0x14, 0x14, 0x16);
    private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private string _originalHex = new string('0', 32);
    private string _editHex = new string('0', 32);
    private string _dataAscii = "................";
    private string _validationTip = "Original card data";
    private bool _isModified;
    private bool _isValid = true;
    private Brush _statusForeground = CyanBrush;
    private Brush _statusBorder = TransparentBrush;
    private Brush _statusBackground = DefaultBgBrush;

    public int BlockNumber { get; set; }

    [JsonPropertyName("DataHex")]
    public string DataHex
    {
        get => _editHex;
        set
        {
            string val = (value ?? string.Empty).Trim().ToUpperInvariant();
            _originalHex = val;
            _editHex = val;
            _dataAscii = HexToAscii(val);
            RecomputeState();
            OnPropertyChanged(nameof(DataHex));
            OnPropertyChanged(nameof(EditHex));
            OnPropertyChanged(nameof(OriginalHex));
        }
    }

    [JsonIgnore]
    public string OriginalHex
    {
        get => _originalHex;
        set
        {
            _originalHex = (value ?? string.Empty).Trim().ToUpperInvariant();
            RecomputeState();
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string EditHex
    {
        get => _editHex;
        set
        {
            if (SetProperty(ref _editHex, value))
            {
                RecomputeState();
            }
        }
    }

    [JsonPropertyName("DataAscii")]
    public string DataAscii
    {
        get => _dataAscii;
        set => SetProperty(ref _dataAscii, value);
    }

    public bool IsSectorTrailer { get; set; }
    public bool IsBlockZero => BlockNumber == 0;

    [JsonIgnore]
    public bool IsModified
    {
        get => _isModified;
        private set => SetProperty(ref _isModified, value);
    }

    [JsonIgnore]
    public bool IsValid
    {
        get => _isValid;
        private set => SetProperty(ref _isValid, value);
    }

    [JsonIgnore]
    public bool IsInvalid => !IsValid;

    [JsonIgnore]
    public bool CanWrite => IsModified && IsValid;

    [JsonIgnore]
    public bool CanRevert => IsModified;

    [JsonIgnore]
    public Brush StatusForeground
    {
        get => _statusForeground;
        private set => SetProperty(ref _statusForeground, value);
    }

    [JsonIgnore]
    public Brush StatusBorder
    {
        get => _statusBorder;
        private set => SetProperty(ref _statusBorder, value);
    }

    [JsonIgnore]
    public Brush StatusBackground
    {
        get => _statusBackground;
        private set => SetProperty(ref _statusBackground, value);
    }

    [JsonIgnore]
    public string ValidationTip
    {
        get => _validationTip;
        private set => SetProperty(ref _validationTip, value);
    }

    public void Revert()
    {
        EditHex = OriginalHex;
    }

    public void MarkSaved()
    {
        OriginalHex = (_editHex ?? string.Empty).Trim().ToUpperInvariant();
    }

    private void RecomputeState()
    {
        string raw = (_editHex ?? string.Empty).Trim();

        // Update ASCII preview live
        DataAscii = HexToAscii(raw);

        bool hasInvalidChars = !Regex.IsMatch(raw, @"^[0-9A-Fa-f]*$");
        bool exactLength = raw.Length == 32;

        if (hasInvalidChars)
        {
            IsValid = false;
            IsModified = true;
            ValidationTip = "Illegal: Contains invalid non-hex characters (only 0-9, A-F allowed).";
            StatusForeground = RedBrush;
            StatusBorder = RedBorderBrush;
            StatusBackground = RedBgBrush;
        }
        else if (!exactLength)
        {
            IsValid = false;
            IsModified = true;
            ValidationTip = $"Illegal length: {raw.Length}/32 hex digits (must be 16 bytes).";
            StatusForeground = RedBrush;
            StatusBorder = RedBorderBrush;
            StatusBackground = RedBgBrush;
        }
        else
        {
            IsValid = true;
            bool differs = !string.Equals(raw, _originalHex, StringComparison.OrdinalIgnoreCase);
            IsModified = differs;

            if (differs)
            {
                ValidationTip = "Modified: Valid 32 hex digits. Ready to write.";
                StatusForeground = GreenBrush;
                StatusBorder = GreenBorderBrush;
                StatusBackground = GreenBgBrush;
            }
            else
            {
                ValidationTip = "Original card dump data.";
                StatusForeground = CyanBrush;
                StatusBorder = TransparentBrush;
                StatusBackground = DefaultBgBrush;
            }
        }

        OnPropertyChanged(nameof(IsInvalid));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(CanRevert));
    }

    public static string HexToAscii(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < hex.Length - 1; i += 2)
        {
            try
            {
                byte b = Convert.ToByte(hex.Substring(i, 2), 16);
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }
            catch
            {
                sb.Append('.');
            }
        }
        return sb.ToString();
    }
}

public class MifareSector
{
    public int SectorNumber { get; set; }
    public string Title => $"Sector {SectorNumber:D2}";
    public string KeyA { get; set; } = "FFFFFFFFFFFF";
    public string KeyB { get; set; } = "FFFFFFFFFFFF";
    public string AccessBits { get; set; } = "FF078069";
    public List<MifareBlock> Blocks { get; set; } = new List<MifareBlock>();
}

public class MifareCardData
{
    public string Uid { get; set; } = string.Empty;
    public string CardType { get; set; } = "MIFARE Classic 1K";
    public DateTime DumpTime { get; set; } = DateTime.Now;
    public List<MifareSector> Sectors { get; set; } = new List<MifareSector>();

    public static MifareCardData CreateEmpty1K(string uid = "00000000")
    {
        var card = new MifareCardData { Uid = uid };
        for (int s = 0; s < 16; s++)
        {
            var sector = new MifareSector { SectorNumber = s };
            for (int b = 0; b < 4; b++)
            {
                int blkNum = s * 4 + b;
                sector.Blocks.Add(new MifareBlock
                {
                    BlockNumber = blkNum,
                    DataHex = new string('0', 32),
                    DataAscii = "................",
                    IsSectorTrailer = (b == 3)
                });
            }
            card.Sectors.Add(sector);
        }
        return card;
    }

    public void SaveAsJson(string filePath)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(filePath, json);
    }

    public static MifareCardData? LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<MifareCardData>(json);
    }

    public void SaveAsEml(string filePath)
    {
        var sb = new StringBuilder();
        foreach (var sector in Sectors)
        {
            foreach (var block in sector.Blocks)
            {
                sb.AppendLine(block.DataHex);
            }
        }
        File.WriteAllText(filePath, sb.ToString());
    }
}
