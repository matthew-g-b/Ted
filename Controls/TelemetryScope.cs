using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TED.Controls;

/// <summary>Compact Engine Simulator-style oscilloscope using history of real PID samples.</summary>
public sealed class TelemetryScope : FrameworkElement
{
    private readonly List<(DateTime At,double A,double? B)> _points = new();
    private static readonly Pen Guide = PenOf("#272B30", .8);
    private static readonly Pen First = PenOf("#F4802A", 1.6);
    private static readonly Pen Second = PenOf("#77CEE0", 1.45);
    private static readonly Brush Subtle = BrushOf("#91989B");
    public double UpperBound { get; set; } = 100;
    public double LowerBound { get; set; } = 0;
    public int WindowSeconds { get; set; } = 180;
    public void Add(double a, double? b=null)
    {
        _points.Add((DateTime.Now, a,b));
        DateTime cutoff=DateTime.Now.AddSeconds(-Math.Max(WindowSeconds,10));
        _points.RemoveAll(p=>p.At < cutoff);
        InvalidateVisual();
    }
    public void Clear() { _points.Clear();InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualHeight<28 || ActualWidth<55) return;
        double left=27, right=Math.Max(left+1, ActualWidth-7);
        double top=5,bottom=Math.Max(top+1,ActualHeight-18);
        for(int i=0;i<=4;i++)
        {
            double y=top+(bottom-top)*i/4;
            dc.DrawLine(Guide,new Point(left,y),new Point(right,y));
            Text(dc,(UpperBound-(UpperBound-LowerBound)*i/4).ToString("0",CultureInfo.InvariantCulture),new Point(2,y-6));
        }
        for(int i=0;i<=4;i++)
        {
            double x=left+(right-left)*i/4;
            dc.DrawLine(Guide,new Point(x,top),new Point(x,bottom));
        }
        if(_points.Count<1 || UpperBound<=LowerBound) return;
        DateTime now=DateTime.Now;
        Plot(dc,First, x=>x.A,now,left,right,top,bottom);
        Plot(dc,Second, x=>x.B,now,left,right,top,bottom);
    }
    private void Plot(DrawingContext dc, Pen pen,Func<(DateTime At,double A,double? B),double?> get,
        DateTime now,double left,double right,double top,double bottom)
    {
        StreamGeometry g = new();bool started=false;
        using(var s=g.Open())
        {
            foreach(var p in _points)
            {
                double? value=get(p);
                if(!value.HasValue) {started=false;continue;}
                double age=(now-p.At).TotalSeconds;
                if(age>WindowSeconds) continue;
                double x=right-Math.Max(0,age)/WindowSeconds*(right-left);
                double t=Math.Clamp((value.Value-LowerBound)/(UpperBound-LowerBound),0,1);
                Point point=new(x,bottom-t*(bottom-top));
                if(!started) {s.BeginFigure(point,false,false); started=true;}
                else s.LineTo(point,true,false);
            }
        }
        g.Freeze();dc.DrawGeometry(null,pen,g);
    }
    private void Text(DrawingContext dc,string s,Point pos)
    {
        var f=new FormattedText(s,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface("Consolas"),9,Subtle,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(f,pos);
    }
    private static Brush BrushOf(string c)
    { var b=(Brush)new BrushConverter().ConvertFromString(c)!;b.Freeze();return b; }
    private static Pen PenOf(string c,double n)
    {var p=new Pen(BrushOf(c),n);p.Freeze();return p;}
}
