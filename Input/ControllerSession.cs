// Shared by both pages. Only the session selects which owner may poll a source.
sealed class ControllerSession
{
    ControllerSource? source;
    public event Action? SourceChanged;
    public ControllerSource? Source
    {
        get => source;
        set { source = value; SourceChanged?.Invoke(); }
    }
    public int[] Mapping { get; } = [0, 1, 2, 3];
    public bool[] Invert { get; } = new bool[4];
    public bool IsScanning, IsConnecting, AnalyzerBusy;
    public AnalyzerCapture? AnalyzerRecording { get; private set; }
    public Func<Task>? StopAnalyzer;
    public event Action<ControllerFrame?>? Sampled;
    public ControllerFrame? Sample()
    {
        var frame = (IsScanning && Source is not KeyboardSource) || IsConnecting ? null : AnalyzerRecording != null ? AnalyzerRecording.Latest : Source?.Read();
        Sampled?.Invoke(frame);
        return frame;
    }
    public void StartCapture(Action<AnalyzerReading> observe)
    {
        if (Source == null || IsScanning || IsConnecting || AnalyzerRecording != null)
            throw new InvalidOperationException("Controller is not available for capture.");
        AnalyzerRecording = new AnalyzerCapture(Source, Mapping, Invert, observe);
        AnalyzerBusy = true;
    }
    public async Task StopCapture()
    {
        var capture = AnalyzerRecording;
        if (capture != null) await capture.StopAsync();
        AnalyzerRecording = null;
    }
    public async Task CloseSource()
    {
        if (StopAnalyzer != null) await StopAnalyzer();
        await StopCapture();
        AnalyzerBusy = false;
        var previous = Source; Source = null;
        if (previous != null) await previous.DisposeAsync();
    }
}

static class AxisMapping
{
    public static double Read(ControllerFrame? frame, int axis, int[] mapping, bool[] invert)
    {
        int index = mapping[axis];
        return frame != null && index >= 0 && index < frame.Values.Length
            ? frame.Values[index] * (invert[axis] ? -1 : 1) : double.NaN;
    }
}

readonly record struct AxisConfiguration(int LX, int LY, int RX, int RY, bool InvertLX, bool InvertLY, bool InvertRX, bool InvertRY)
{
    public static AxisConfiguration Capture(int[] mapping, bool[] invert) => new(mapping[0], mapping[1], mapping[2], mapping[3], invert[0], invert[1], invert[2], invert[3]);
}
