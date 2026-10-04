using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace iceman_gui.Models;

public class MifareBlock
{
    public int BlockNumber { get; set; }
    public string DataHex { get; set; } = new string('0', 32);
    public string DataAscii { get; set; } = string.Empty;
    public bool IsSectorTrailer { get; set; }

    public static string HexToAscii(string hex)
    {
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
