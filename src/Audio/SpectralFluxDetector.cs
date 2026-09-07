#nullable enable
namespace Loupedeck.HapticAudioFeedback;

using NAudio.Dsp;

// Causal, trailing Hann-window FFT. Fixed buffers; independent channel powers prevent phase cancellation.
internal sealed class SpectralFluxDetector
{
    private readonly float[][] _samples;
    private readonly float[] _window;
    private readonly Complex[] _fft;
    private readonly double[] _power, _previous, _current;
    private readonly int _size, _exponent, _hop;
    private readonly (int First, int Last) _bass, _high;
    private readonly int _bassRadius, _highRadius;
    private int _position, _filled, _since;
    public double Bass { get; private set; }
    public double High { get; private set; }
    public bool Ready { get; private set; }

    public SpectralFluxDetector(int rate, int channels, AudioSettings settings)
    {
        _bassRadius = settings.BassVibratoSuppressionBins; _highRadius = settings.HighVibratoSuppressionBins;
        _size = 256; _exponent = 8;
        while (_size < rate * .021 && _size < 8192) { _size *= 2; _exponent++; }
        _hop = Math.Max(1, (int)Math.Round(rate * .005)) * 2;
        _samples = Enumerable.Range(0, channels).Select(_ => new float[_size]).ToArray();
        _window = new float[_size];
        for (int i = 0; i < _size; i++) _window[i] = (float)(.5 - .5 * Math.Cos(2 * Math.PI * i / (_size - 1)));
        _fft = new Complex[_size];
        _power = new double[_size / 2 + 1]; _previous = new double[_power.Length]; _current = new double[_power.Length];
        (int, int) Bins(double center, double q)
        {
            center = Math.Min(center, rate * .4);
            int first = Math.Clamp((int)Math.Floor((center - center / (2 * q)) * _size / rate), 1, _power.Length - 1);
            int last = Math.Clamp((int)Math.Ceiling((center + center / (2 * q)) * _size / rate), first, _power.Length - 1);
            return (first, last);
        }
        _bass = Bins(settings.LowCenterHz, settings.BassFilterQ);
        _high = Bins(settings.HighCenterHz, settings.HighFilterQ);
    }

    public void Add(int channel, float value) => _samples[channel][_position] = value;
    public void Advance()
    {
        Ready = false;
        _position = (_position + 1) % _size;
        _filled = Math.Min(_filled + 1, _size);
        if (++_since < _hop) return;
        _since = 0;
        if (_filled < _size) return;
        Array.Clear(_power);
        foreach (var channel in _samples)
        {
            for (int i = 0; i < _size; i++) { _fft[i].X = channel[(_position + i) % _size] * _window[i]; _fft[i].Y = 0; }
            FastFourierTransform.FFT(true, _exponent, _fft);
            for (int i = 1; i < _power.Length; i++) _power[i] += (double)_fft[i].X * _fft[i].X + (double)_fft[i].Y * _fft[i].Y;
        }
        for (int i = 1; i < _power.Length; i++) _current[i] = Math.Log(1 + 1000 * Math.Sqrt(_power[i] / _samples.Length));
        Bass = Flux(_bass, _bassRadius); High = Flux(_high, _highRadius);
        Array.Copy(_current, _previous, _current.Length);
        Ready = true;
    }
    private double Flux((int First, int Last) band, int radius)
    {
        double rise = 0, total = 0;
        for (int i = band.First; i <= band.Last; i++)
        {
            double reference = _previous[i];
            for (int neighbor = Math.Max(1, i - radius); neighbor <= Math.Min(_previous.Length - 1, i + radius); neighbor++)
                reference = Math.Max(reference, _previous[neighbor]);
            rise += Math.Max(0, _current[i] - reference); total += _current[i];
        }
        return total > 1e-6 ? Math.Clamp(rise / total, 0, 1) : 0;
    }
}
