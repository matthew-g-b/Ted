using System.Globalization;

namespace TED.Protocol;

/// <summary>One standard, 11-bit classical CAN frame.</summary>
public sealed record CanFrame(ushort Id, byte[] Data)
{
    public string AsSlcan() => $"t{Id:X3}{Data.Length:X1}{Convert.ToHexString(Data)}";

    public static bool TryParse(string line, out CanFrame? frame)
    {
        frame = null;
        if (line.Length < 5 || line[0] != 't') return false;
        if (!ushort.TryParse(line.AsSpan(1, 3), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out ushort id) || id > 0x7FF) return false;
        if (line[4] < '0' || line[4] > '8') return false;
        int dlc = line[4] - '0';
        if (line.Length != 5 + dlc * 2) return false;
        try
        {
            frame = new CanFrame(id, Convert.FromHexString(line[5..]));
            return true;
        }
        catch (FormatException) { return false; }
    }

    public override string ToString() => $"{Id:X3} [{Data.Length}] {Convert.ToHexString(Data)}";
}
