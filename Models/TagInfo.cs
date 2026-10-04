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
    public string Frequency { get; set; } = "HF (13.56 MHz)";
    public string FacilityCode { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public DateTime ScanTime { get; set; } = DateTime.Now;
    public string RawOutput { get; set; } = string.Empty;

    public bool IsMifare => TagType.Contains("MIFARE", StringComparison.OrdinalIgnoreCase);
    public bool IsGen2Cuid => MagicType.Contains("Gen 2", StringComparison.OrdinalIgnoreCase) || MagicType.Contains("CUID", StringComparison.OrdinalIgnoreCase);
    public bool IsGen1a => MagicType.Contains("Gen 1", StringComparison.OrdinalIgnoreCase);
}
