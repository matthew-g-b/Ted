using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TED.Controls;

/// <summary>
/// Port of the visual geometry of Engine Simulator's Gauge/LabeledGauge.
/// The tick / pointer layout follows the 1.2*pi to -0.2*pi arc in gauge.cpp.
/// Only the value is telemetry; the gauge is native WPF DrawingContext.
/// </summary>
public sealed class EngineSimGauge : FrameworkElement
{
    private static readonly Brush Fore = BrushOf("#FFFFFF");
    private static readonly Brush Muted = BrushOf("#B6B6B6");
    private static readonly Pen Major = PenOf("#EEEEEE", 1.35);
    private static readonly Pen Minor = PenOf("#787A7C", .8);
    private static readonly Pen Needle = PenOf("#EE4445", 2.6);
    private static readonly Pen Rim = PenOf("#A6A6A6", 1.0);

    private double? _value;
    public double? Value { get => _value; set { _value = value; InvalidateVisual(); } }
    public double Minimum { get; set; } = 0;
    public double Maximum { get; set; } = 100;
    public string Unit { get; set; } = "";
    public string Format { get; set; } = "F0";
    public int MajorTicks { get; set; } = 5;
    public int MinorTicks { get; set; } = 40;
    public string Prefix { get; set; } = "";

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 50 || ActualHeight < 40) return;
        double w = ActualWidth, h = ActualHeight;
        double radius = Math.Min(w * .42, h * .52);
        double cx = w * .5, cy = h * .62;
        double start = Math.PI * 1.2;
        double end = -Math.PI * .2;
        int ticks = Math.Max(1, MinorTicks);
        int major = Math.Max(1, MajorTicks);
        for (int i = 0; i <= ticks; i++)
        {
            double t = (double)i / ticks;
            double theta = (1-t)*start + t*end;
            bool majorTick = i * major % ticks == 0;
            double len = majorTick ? 10 : 4.5;
            dc.DrawLine(majorTick ? Major : Minor,
                At(cx, cy, radius-len, theta), At(cx, cy, radius, theta));
            if (majorTick && radius >= 39)
            {
                string label = (Minimum+(Maximum-Minimum)*t).ToString("F0", CultureInfo.InvariantCulture);
                Center(dc, label, At(cx, cy, radius-22, theta), 9, Muted);
            }
        }
        // Engine Simulator's narrow red needle, central pin and radial accents.
        if (_value.HasValue && Maximum > Minimum)
        {
            double t = Math.Clamp((_value.Value-Minimum)/(Maximum-Minimum), 0, 1);
            double theta = (1-t)*start + t*end;
            dc.DrawLine(Needle, At(cx,cy,-radius*.15,theta), At(cx,cy,radius*.80,theta));
        }
        dc.DrawEllipse(Fore, Rim, new Point(cx,cy), 2.8, 2.8);
        string number = _value?.ToString(Format, CultureInfo.InvariantCulture) ?? "--";
        string output = Prefix + number + (Unit.Length>0 ? " " + Unit : "");
        Center(dc, output, new Point(cx, h-14), Math.Clamp(w*.09, 11, 18), Fore);
    }

    private static Point At(double cx, double cy, double radius, double angle) =>
        new(cx+radius*Math.Cos(angle), cy-radius*Math.Sin(angle));

    private void Center(DrawingContext dc,string s, Point p,double size,Brush brush)
    {
        var f = new FormattedText(s,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface("Consolas"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(f,new Point(p.X-f.Width/2,p.Y-f.Height/2));
    }
    private static Brush BrushOf(string color)
    { var b=(Brush)new BrushConverter().ConvertFromString(color)!;b.Freeze();return b; }
    private static Pen PenOf(string color,double thick)
    { var p=new Pen(BrushOf(color),thick);p.Freeze();return p; }
}
