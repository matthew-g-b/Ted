using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TED.Controls;
using TED.Diagnostics;
using TED.Protocol;

namespace TED;

public partial class MainWindow : Window
{
    private SlcanConnection? _link;
    private IsoTpClient? _isotp;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly DispatcherTimer _refresh;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastClock;
    private DateTime? _lastEngineResponse;
    private bool _closingNow;
    private bool _versionSeen;

    public MainWindow()
    {
        InitializeComponent();
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _refresh.Tick += (_,_) => Tick();
        _refresh.Start();
        Log("TED v0.2 / Engine Simulator-style instrumentation");
        Log("Known ECU addresses: TX 7E0 RX 7E8; read-only requests.");
        Log("01 0C RPM, 01 0D SPEED, 21 82 CVT TEMPERATURES (+ 7 standard PIDs).");
        Log("* CVT temperature scaling is PROVISIONAL; CVT ratio remains N/A.");
    }

    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        ConnectButton.IsEnabled=false;
        try
        {
            string name=PortBox.Text.Trim();
            _link=new SlcanConnection(name);
            _link.LineReceived += OnSerialLine;
            _link.Open();
            Log("PORT OPEN: "+name);
            _versionSeen=false;
            _link.Command("V");
            await Task.Delay(300);
            _link.FlushUnterminatedVersion();
            foreach(string cmd in new[]{"C","S6","M0","O"})
            {
                _link.Command(cmd);
                Log("TX adapter: "+cmd);
                await Task.Delay(45);
            }
            _isotp=new IsoTpClient(_link);
            _isotp.FlowControlSent += OnFlowControl;
            ConnectionState.Text="CAN OPEN REQUESTED / 500K";
            Log("CAN open requested; this firmware does not ACK setup commands.");
            PortBox.IsEnabled=false;
            DisconnectButton.IsEnabled=true;
            StartButton.IsEnabled=true;
        }
        catch(Exception ex)
        {
            Log("CONNECT ERROR: "+ex.Message);
            try { _isotp?.Dispose(); } catch { }
            try { _link?.Dispose(); } catch { }
            _isotp=null;_link=null;
            PortBox.IsEnabled=true;
            ConnectButton.IsEnabled=true;
            ConnectionState.Text="DISCONNECTED";
            MessageBox.Show(this,ex.Message,"TED / Connection Error");
        }
    }

    private void OnSerialLine(string line)
    {
        Log("RX "+line);
        if(!_versionSeen && line.Contains("canable2",StringComparison.OrdinalIgnoreCase))
        {
            _versionSeen=true;
            Dispatcher.InvokeAsync(() => FirmwareStatus.Text="CANABLE2 / SLCAN");
        }
    }
    private void OnFlowControl(ushort id) => Log($"TX FC {id:X3} 30 00 00 00 00 00 00 00");

    private void Start_Click(object sender,RoutedEventArgs e)
    {
        if (_isotp is null || _readCts is not null) return;
        _readCts=new CancellationTokenSource();
        _lastEngineResponse=null;
        foreach(EngineSimGauge g in new[]{RpmGauge,SpeedGauge,MapGauge,LoadGauge,MafGauge,
                 ThrottleGauge,CoolantGauge,IntakeGauge,PanGauge,ConverterGauge}) g.Value=null;
        MotorView.SetRpm(0);
        foreach(TelemetryScope s in new[]{RpmScope,SpeedScope,CvtScope,MafScope,LoadScope})s.Clear();
        LastReading.Text="WAITING FOR ECU RESPONSE";
        StartButton.IsEnabled=false;StopButton.IsEnabled=true;
        ConnectionState.Text="POLLING 7E0 / 7E8";
        Log("POLL START: standard Mode 01 + Toyota 21 82, read-only.");
        _readTask=PollAsync(_readCts.Token);
    }

    private async Task PollAsync(CancellationToken token)
    {
        // Serialize requests on the same ECU: ISO-TP client permits one outstanding request.
        // The order puts RPM, speed and CVT temperature ahead of secondary instruments.
        try
        {
            while(!token.IsCancellationRequested)
            {
                await QueryStandard(0x0C,RpmGauge,RpmScope,token,asRpm:true);
                await QueryStandard(0x0D,SpeedGauge,SpeedScope,token);
                await QueryCvt(token);
                await QueryStandard(0x04,LoadGauge,LoadScope,token);
                await QueryStandard(0x0B,MapGauge,null,token);
                await QueryStandard(0x10,MafGauge,MafScope,token);
                await QueryStandard(0x11,ThrottleGauge,null,token);
                await QueryStandard(0x05,CoolantGauge,null,token);
                await QueryStandard(0x0F,IntakeGauge,null,token);
                await QueryStandard(0x0E,null,null,token);
                await Task.Delay(350,token);
            }
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            Log("POLL LOOP STOPPED: "+ex.Message);
            // The UI remains connected, allowing a manual stop/restart.
        }
    }

    private async Task QueryStandard(byte pid,EngineSimGauge? gauge,TelemetryScope? scope,
        CancellationToken token,bool asRpm=false)
    {
        if(_isotp is null) return;
        try
        {
            byte[] request={0x01,pid};
            Log($"TX 7E0 02 01 {pid:X2} 00 00 00 00 00");
            byte[] answer=await _isotp.RequestAsync(0x7E0,0x7E8,request,
                TimeSpan.FromMilliseconds(950),token);
            if(ToyotaCvt.TryMode01(answer,pid,out double value))
            {
                if(gauge != null)gauge.Value=value;
                if(scope != null)scope.Add(value);
                if(asRpm)MotorView.SetRpm(value);
                if(pid==0x0E)SparkStatus.Text=$"{value:F1}° BTDC";
                HeardFromEcu();
            }
            else Log($"PID {pid:X2}: unexpected ECU payload {Convert.ToHexString(answer)}");
        }
        catch(TimeoutException){Log($"PID {pid:X2}: no ECU response");}
        catch(OperationCanceledException){throw;}
        catch(Exception e){Log($"PID {pid:X2}: {e.Message}");}
    }

    private async Task QueryCvt(CancellationToken token)
    {
        if(_isotp is null)return;
        try
        {
            Log("TX 7E0 02 21 82 00 00 00 00 00");
            byte[] answer=await _isotp.RequestAsync(0x7E0,0x7E8,new byte[]{0x21,0x82},
                TimeSpan.FromMilliseconds(1200),token);
            if(ToyotaCvt.TryTemperatures(answer,out double pan,out double converter))
            {
                PanGauge.Value=pan;
                ConverterGauge.Value=converter;
                CvtScope.Add(pan,converter);
                HeardFromEcu();
                Log($"21 82 => PAN {pan:F1}°C / CONVERTER {converter:F1}°C [PROVISIONAL]");
            }
            else Log("21 82: unexpected ECU payload "+Convert.ToHexString(answer));
        }
        catch(TimeoutException){Log("21 82: no ECU response");}
        catch(OperationCanceledException){throw;}
        catch(Exception ex){Log("21 82: "+ex.Message);}
    }

    private void HeardFromEcu()
    {
        _lastEngineResponse=DateTime.Now;
        LastSampleStatus.Text=DateTime.Now.ToString("HH:mm:ss");
        LastReading.Text="LIVE DATA / CVT TEMPERATURE SCALE UNVERIFIED";
    }

    private async void Stop_Click(object sender,RoutedEventArgs e)=>await StopReadingAsync();
    private async Task StopReadingAsync()
    {
        if(_readCts is null)return;
        StopButton.IsEnabled=false;
        _readCts.Cancel();
        _isotp?.CancelPending();
        try{if(_readTask is not null) await _readTask;}catch(OperationCanceledException){}
        _readCts.Dispose();_readCts=null;_readTask=null;
        MotorView.SetRpm(0);
        StartButton.IsEnabled=_isotp is not null;
        ConnectionState.Text="CAN OPEN / IDLE";
        LastReading.Text="STOPPED / VALUES ARE LAST RECEIVED";
        Log("Polling stopped.");
    }
    private async void Disconnect_Click(object sender,RoutedEventArgs e)=>await DisconnectCoreAsync();
    private async Task DisconnectCoreAsync()
    {
        await StopReadingAsync();
        if(_isotp != null){_isotp.FlowControlSent-=OnFlowControl;_isotp.Dispose();_isotp=null;}
        if(_link != null)
        {
            _link.LineReceived-=OnSerialLine;
            try{_link.Dispose();}catch(Exception e){Log("DISCONNECT WARNING: "+e.Message);}
            _link=null;
        }
        MotorView.SetRpm(0);
        FirmwareStatus.Text="NOT CONNECTED";
        ConnectionState.Text="DISCONNECTED";
        PortBox.IsEnabled=true;ConnectButton.IsEnabled=true;
        DisconnectButton.IsEnabled=false;StartButton.IsEnabled=false;StopButton.IsEnabled=false;
        Log("Disconnected.");
    }
    private void Log(string content)
    {
        _lines.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {content}");
        while(_lines.Count>2200 && _lines.TryDequeue(out _)) { }
    }
    private void Tick()
    {
        double now=_clock.Elapsed.TotalSeconds;
        MotorView.Advance(now-_lastClock);_lastClock=now;
        if(_readCts is not null && _lastEngineResponse.HasValue &&
            (DateTime.Now-_lastEngineResponse.Value).TotalSeconds>5)
        {
            MotorView.SetRpm(0);
            LastReading.Text="STALE / ECU HAS NOT RESPONDED FOR 5S";
        }
        var sb=new StringBuilder();int count=0;
        while(count++<100 && _lines.TryDequeue(out string? line))sb.AppendLine(line);
        if(sb.Length>0)
        {
            LogBox.AppendText(sb.ToString());
            if(LogBox.Text.Length>110000)LogBox.Text=LogBox.Text[^75000..];
            LogBox.ScrollToEnd();
        }
    }
    private async void Window_Closing(object? sender,CancelEventArgs e)
    {
        if(_closingNow)return;
        e.Cancel=true;
        try{await DisconnectCoreAsync();}catch { }
        _refresh.Stop();_closingNow=true;Close();
    }
}
