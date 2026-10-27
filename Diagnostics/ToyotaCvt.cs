namespace TED.Diagnostics;

public static class ToyotaCvt
{
    // Observed from THIS Toyota via physically addressed ECU 0x7E0 -> 0x7E8.
    // ECU payload after ISO-TP reassembly: 61 82 A B C D E F.
    // Published OBDb Corolla candidate scaling; not independently calibrated.
    // E F (seen as 07 D4 in sample captures) remain UNKNOWN.
    public static bool TryTemperatures(byte[] response, out double panC, out double converterC)
    {
        panC = converterC = 0;
        if (response.Length != 8 || response[0] != 0x61 || response[1] != 0x82) return false;
        panC = ((response[2]<<8)|response[3])/256.0-40.0;
        converterC = ((response[4]<<8)|response[5])/256.0-40.0;
        return panC is >= -40 and <= 215 && converterC is >= -40 and <= 215;
    }

    public static bool TryMode01(byte[] response, byte pid, out double value)
    {
        value=0;
        if(response.Length<3 || response[0]!=0x41 || response[1]!=pid) return false;
        byte a=response[2];
        value=pid switch
        {
            0x04 => a*100.0/255,             // calculated load (%)
            0x05 => a-40,                    // coolant °C
            0x0B => a,                       // intake manifold absolute kPa
            0x0C when response.Length>=4 => ((a<<8)|response[3])/4.0,  // RPM
            0x0D => a,                       // vehicle speed km/h
            0x0E => a/2.0 - 64.0,           // ignition timing degrees before TDC
            0x0F => a-40,                    // intake air °C
            0x10 when response.Length>=4 => ((a<<8)|response[3])/100.0, // MAF g/s
            0x11 => a*100.0/255,            // throttle position (%)
            _ => double.NaN
        };
        return !double.IsNaN(value);
    }
}
