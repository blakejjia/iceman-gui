using System;

namespace iceman_gui.Models;

public class TagInfo
{
    public string Uid { get; set; } = string.Empty;
    public string FormattedUid { get; set; } = string.Empty;
    public string Atqa { get; set; } = string.Empty;
    public string Sak { get; set; } = string.Empty;
    public string TagType { get; set; } = string.Empty;
    public string MagicType { get; set; } = string.Empty;
    public string Prng { get; set; } = string.Empty;
    public string Frequency { get; set; } = "-";
    public string FacilityCode { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string Standard { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string UidLength { get; set; } = string.Empty;
    public string Ats { get; set; } = string.Empty;
    public string AtsDetails { get; set; } = string.Empty;
    public string HistoricalBytes { get; set; } = string.Empty;
    public string SupportedProtocols { get; set; } = string.Empty;
    public DateTime ScanTime { get; set; } = DateTime.Now;
    public string RawOutput { get; set; } = string.Empty;

    public string FormattedTimestamp => ScanTime.ToString("yyyy-MM-dd HH:mm:ss");
    public bool HasAts => !string.IsNullOrEmpty(Ats);
    public bool HasManufacturer => !string.IsNullOrEmpty(Manufacturer);
    public bool HasStandard => !string.IsNullOrEmpty(Standard);
    public bool HasAtsDetails => !string.IsNullOrEmpty(AtsDetails);
    public bool HasHistoricalBytes => !string.IsNullOrEmpty(HistoricalBytes);

    public bool IsMifare => TagType.Contains("MIFARE", StringComparison.OrdinalIgnoreCase);
    public bool IsGen2Cuid => MagicType.Contains("Gen 2", StringComparison.OrdinalIgnoreCase) || MagicType.Contains("CUID", StringComparison.OrdinalIgnoreCase);
    public bool IsGen1a => MagicType.Contains("Gen 1", StringComparison.OrdinalIgnoreCase);
}
