using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TED.Controls;

/// <summary>
/// Engine Simulator-style mechanical cutaway of an inline-4. The geometry is illustrative;
/// rotational speed follows observed ECU RPM, but absolute crank position, valve timing,
/// spark events and cylinder phase are NOT measured. No engine-control commands are sent.
/// </summary>
public sealed class InlineFourView : FrameworkElement
{
    private readonly Brush _steel=BrushOf("#D3D6D9");
    private readonly Brush _block=BrushOf("#353A3E");
    private readonly Brush _blockDark=BrushOf("#22272B");
    private readonly Brush _pink=BrushOf("#F394BE");
    private readonly Brush _orange=BrushOf("#F4802A");
    private readonly Brush _blue=BrushOf("#77CEE0");
    private readonly Brush _dim=BrushOf("#525960");
    private readonly Pen _outline=PenOf("#BBC1C6",1.2);
    private readonly Pen _rod=PenOf("#E5E7E9",5);
    private readonly Pen _crank=PenOf("#A6ACB1",4);
    private readonly Pen _journal=PenOf("#ECEEEF",1.8);
    private readonly Pen _axis=PenOf("#353B40",1);
    private double _radians;
    private double _rpm;
    public void SetRpm(double rpm) => _rpm=Math.Max(0,rpm);
    public void Advance(double dt)
    {
        if (_rpm>0) _radians=(_radians+_rpm*(2*Math.PI/60)*Math.Clamp(dt,0,.12))%(4*Math.PI);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w=ActualWidth,h=ActualHeight;
        if(w<100||h<100)return;
        // Render in normalized engine coordinates, aligned like Engine Simulator's engine view.
        double sx=Math.Min(w/530,h/345);
        double xmid=w/2, ymid=h/2;
        dc.PushTransform(new TranslateTransform(xmid,ymid));
        dc.PushTransform(new ScaleTransform(sx,sx));
        dc.PushTransform(new TranslateTransform(-265,-172));
        double centerY=251;double r=29;double rodLen=88;
        double[] centers={76,202,328,454};
        dc.DrawRoundedRectangle(_blockDark,_outline,new Rect(14,203,501,98),6,6);
        dc.DrawRoundedRectangle(_block,_outline,new Rect(20,215,489,56),5,5);
        dc.DrawLine(_crank,new Point(33,centerY),new Point(500,centerY));
        for(int i=0;i<4;i++)
        {
            double x=centers[i];
            double angle=_radians+((i==0||i==3)?0:Math.PI);
            double sn=Math.Sin(angle),cs=Math.Cos(angle);
            double pinX=x+r*sn,pinY=centerY-r*cs;
            double pistonPinY=centerY-(r*cs+Math.Sqrt(Math.Max(1,rodLen*rodLen-r*r*sn*sn)));
            double pistonTop=pistonPinY-17;
            // Cylinder sleeve and head. Different light/dark outlines give the machined-metal look.
            dc.DrawRectangle(_block,_outline,new Rect(x-47,39,94,178));
            dc.DrawRectangle(_blockDark,_axis,new Rect(x-34,50,68,150));
            dc.DrawRectangle(null,_outline,new Rect(x-35,50,70,150));
            dc.DrawRoundedRectangle(_dim,_outline,new Rect(x-50,19,100,30),3,3);
            // stylized cam caps and intake/exhaust poppet valves: fixed cycle illustration only
            dc.DrawEllipse(_orange,null,new Point(x-26,16),7,7);
            dc.DrawEllipse(_blue,null,new Point(x+26,16),7,7);
            dc.DrawLine(PenOf("#F4802A",3),new Point(x-26,25),new Point(x-23,50));
            dc.DrawLine(PenOf("#77CEE0",3),new Point(x+26,25),new Point(x+23,50));
            dc.DrawLine(PenOf("#F4802A",9),new Point(x-34,52),new Point(x-13,52));
            dc.DrawLine(PenOf("#77CEE0",9),new Point(x+13,52),new Point(x+34,52));
            // A rough ignition pulse is derived from modeled four-stroke phase, not ECU ignition.
            int[] order={0,2,3,1};
            int firingCylinder=order[(int)Math.Floor(_radians/Math.PI)%4];
            double subPhase=_radians%Math.PI;
            Brush spark=(i==firingCylinder && subPhase<.35 && _rpm>0)?_orange:_dim;
            dc.DrawEllipse(spark,null,new Point(x,45),4,4);
            // Crank throw, connecting rod, sliding piston
            dc.DrawLine(_crank,new Point(x,centerY),new Point(pinX,pinY));
            dc.DrawLine(_rod,new Point(pinX,pinY),new Point(x,pistonPinY));
            dc.DrawEllipse(_steel,_journal,new Point(pinX,pinY),5,5);
            dc.DrawRoundedRectangle(_steel,_outline,new Rect(x-30,pistonTop,60,25),3,3);
            dc.DrawLine(_axis,new Point(x-28,pistonTop+8),new Point(x+28,pistonTop+8));
            dc.DrawEllipse(_block,null,new Point(x,pistonPinY),4,4);
            // bank labels under the engine block
            DrawText(dc,(i+1).ToString(CultureInfo.InvariantCulture),new Point(x,319),15,_steel);
        }
        dc.DrawEllipse(_blockDark,_journal,new Point(263,251),17,17);
        dc.DrawEllipse(_steel,null,new Point(263,251),7,7);
        dc.Pop();dc.Pop();dc.Pop();
    }
    private void DrawText(DrawingContext dc,string s,Point middle,double size,Brush color)
    {
        var f=new FormattedText(s,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface("Consolas"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(f,new Point(middle.X-f.Width/2,middle.Y-f.Height/2));
    }
    private static Brush BrushOf(string c)
    { var b=(Brush)new BrushConverter().ConvertFromString(c)!; b.Freeze();return b; }
    private static Pen PenOf(string c,double width)
    {var p=new Pen(BrushOf(c),width);p.Freeze();return p;}
}
