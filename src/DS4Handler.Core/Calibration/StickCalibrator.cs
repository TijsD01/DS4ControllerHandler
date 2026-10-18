namespace DS4Handler.Core.Calibration;

/// <summary>
/// Tunables for <see cref="StickCalibrator"/>. Distances are in normalized stick units,
/// where 1.0 is the distance from the middle of the raw range (127.5) to its edge.
/// </summary>
public sealed record StickCalibrationOptions
{
    /// <summary>Number of recent samples checked together to decide whether the stick is at rest.</summary>
    public int RestWindow { get; init; } = 16;

    /// <summary>All samples in the window must be this close to their mean for the stick to count as released.</summary>
    public float RestTolerance { get; init; } = 0.02f;

    /// <summary>A rest position further than this from the middle is ignored (it is a held stick, not drift).</summary>
    public float MaxDrift { get; init; } = 0.3f;

    /// <summary>Slowest rate (per rest sample) at which the center follows the rest position once warmed up.</summary>
    public float CenterRate { get; init; } = 0.002f;

    /// <summary>Rate (per rest sample) at which the noise estimate follows the measured jitter once warmed up.</summary>
    public float NoiseRate { get; init; } = 0.01f;

    /// <summary>Rest samples needed before release-to-release spread is measured (the center has to settle first).</summary>
    public int SpreadWarmupSamples { get; init; } = 200;

    /// <summary>Rate at which the spread estimate grows when a release lands further away than expected.</summary>
    public float SpreadRiseRate { get; init; } = 0.3f;

    /// <summary>Rate at which the spread estimate shrinks when releases land closer than expected.</summary>
    public float SpreadFallRate { get; init; } = 0.05f;

    /// <summary>Smallest deadzone ever used.</summary>
    public float MinDeadzone { get; init; } = 0.03f;

    /// <summary>Largest deadzone ever used, so a badly worn stick still has usable travel.</summary>
    public float MaxDeadzone { get; init; } = 0.35f;

    /// <summary>How much the measured jitter counts towards the deadzone.</summary>
    public float NoiseFactor { get; init; } = 2f;

    /// <summary>How much the release-to-release spread counts towards the deadzone.</summary>
    public float SpreadFactor { get; init; } = 2.5f;

    /// <summary>Extra deadzone added on top of noise and spread.</summary>
    public float DeadzoneMargin { get; init; } = 0.01f;

    /// <summary>Number of direction sectors used to learn how far the stick reaches.</summary>
    public int OuterSectors { get; init; } = 16;

    /// <summary>A learned reach shorter than this is not trusted and the nominal 1.0 is used instead.</summary>
    public float MinTrustedOuterRadius { get; init; } = 0.78f;

    /// <summary>Full output is reached this much before the learned edge, so jitter at full push still reads as full.</summary>
    public float OuterMargin { get; init; } = 0.02f;
}

/// <summary>Learned state of one analog stick, for saving to a profile.</summary>
public sealed record StickCalibrationData
{
    /// <summary>Learned rest position on the X axis (-1..1, right is positive).</summary>
    public float CenterX { get; init; }

    /// <summary>Learned rest position on the Y axis (-1..1, up is positive).</summary>
    public float CenterY { get; init; }

    /// <summary>Typical jitter of the released stick.</summary>
    public float NoiseRadius { get; init; }

    /// <summary>Typical distance between where the stick comes to rest on different releases.</summary>
    public float RestSpread { get; init; }

    /// <summary>Furthest distance from the center seen per direction sector (0 = not seen). Empty means unknown.</summary>
    public float[] OuterRadii { get; init; } = Array.Empty<float>();

    /// <summary>Number of rest samples learned from so far.</summary>
    public int RestSamples { get; init; }
}

/// <summary>
/// Learns the drift, jitter and reach of an analog stick from normal play and maps raw values to a
/// clean -1..1 range per axis (right and up are positive).
///
/// How it learns:
/// <list type="number">
/// <item>Center: it keeps the last few samples. A released stick barely moves, while a thumb holding
/// the stick always trembles a little. When every sample in the window is very close to their mean,
/// and that mean is near the middle, the stick is resting and the center slowly moves towards that
/// mean. At first all rest samples are averaged equally so drift is found quickly; after that the
/// center moves slowly so a single odd release cannot pull it far.</item>
/// <item>Noise: while resting, it keeps a running average of how far samples wander from their mean.</item>
/// <item>Rest spread: worn springs return to a slightly different spot on every release. At the start
/// of each rest it measures how far that spot is from the learned center and keeps a running average
/// that grows quickly and shrinks slowly.</item>
/// <item>Deadzone: a round deadzone around the learned center, sized from the noise and the spread.
/// Outside it the output is rescaled so it starts smoothly from zero at the deadzone edge.</item>
/// <item>Reach: the circle around the center is split into sectors. Each sector remembers the
/// furthest point the stick has reached (a value only counts when two samples in a row agree, so
/// single spikes are ignored). A sector is trusted once it reaches at least
/// <see cref="StickCalibrationOptions.MinTrustedOuterRadius"/>. Between sectors the edge is
/// interpolated, and a full push in any trusted direction maps to 1.0.</item>
/// </list>
/// </summary>
public sealed class StickCalibrator
{
    private readonly StickCalibrationOptions _o;
    private readonly float[] _winX;
    private readonly float[] _winY;
    private int _winCount;
    private int _winIndex;
    private bool _wasResting;

    private float _prevRadius = -1f;

    private float _centerX;
    private float _centerY;
    private float _noise;
    private float _spread;
    private readonly float[] _outer;
    private int _restSamples;

    public StickCalibrator(StickCalibrationOptions? options = null)
    {
        _o = options ?? new StickCalibrationOptions();
        _winX = new float[Math.Max(2, _o.RestWindow)];
        _winY = new float[_winX.Length];
        _outer = new float[Math.Max(4, _o.OuterSectors)];
    }

    /// <summary>Learned rest position on the X axis (-1..1).</summary>
    public float CenterX => _centerX;

    /// <summary>Learned rest position on the Y axis (-1..1, up is positive).</summary>
    public float CenterY => _centerY;

    /// <summary>Current round deadzone around the learned center.</summary>
    public float Deadzone => Math.Clamp(
        _noise * _o.NoiseFactor + _spread * _o.SpreadFactor + _o.DeadzoneMargin,
        _o.MinDeadzone, _o.MaxDeadzone);

    /// <summary>When false, the calibrator only maps values and stops learning.</summary>
    public bool LearningEnabled { get; set; } = true;

    /// <summary>Feeds one raw sample (0..255 per axis), updates the model and returns the calibrated position.</summary>
    public (float X, float Y) Process(byte rawX, byte rawY)
    {
        if (LearningEnabled) Learn(ToX(rawX), ToY(rawY));
        return Map(rawX, rawY);
    }

    /// <summary>Maps a raw sample with the current model, without learning.</summary>
    public (float X, float Y) Map(byte rawX, byte rawY)
    {
        float dx = ToX(rawX) - _centerX;
        float dy = ToY(rawY) - _centerY;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        float dz = Deadzone;
        if (r <= dz) return (0f, 0f);

        float edge = Math.Max(dz + 0.05f, EdgeAt(MathF.Atan2(dy, dx)) - _o.OuterMargin);
        float scaled = Math.Clamp((r - dz) / (edge - dz), 0f, 1f);
        return (dx / r * scaled, dy / r * scaled);
    }

    /// <summary>Forgets everything learned and goes back to the factory mapping.</summary>
    public void Reset()
    {
        _centerX = 0;
        _centerY = 0;
        _noise = 0;
        _spread = 0;
        _restSamples = 0;
        Array.Clear(_outer);
        _winCount = 0;
        _winIndex = 0;
        _wasResting = false;
        _prevRadius = -1f;
    }

    public StickCalibrationData Export() => new()
    {
        CenterX = _centerX,
        CenterY = _centerY,
        NoiseRadius = _noise,
        RestSpread = _spread,
        OuterRadii = _outer.Any(v => v > 0) ? (float[])_outer.Clone() : Array.Empty<float>(),
        RestSamples = _restSamples,
    };

    public void Import(StickCalibrationData data)
    {
        Reset();
        float cx = Sanitize(data.CenterX), cy = Sanitize(data.CenterY);
        float len = MathF.Sqrt(cx * cx + cy * cy);
        if (len > _o.MaxDrift)
        {
            cx *= _o.MaxDrift / len;
            cy *= _o.MaxDrift / len;
        }
        _centerX = cx;
        _centerY = cy;
        _noise = Math.Clamp(Sanitize(data.NoiseRadius), 0f, 1f);
        _spread = Math.Clamp(Sanitize(data.RestSpread), 0f, 1f);
        _restSamples = Math.Max(0, data.RestSamples);
        if (data.OuterRadii is { Length: > 0 } radii && radii.Length == _outer.Length)
        {
            for (int i = 0; i < _outer.Length; i++) _outer[i] = Math.Clamp(Sanitize(radii[i]), 0f, 2f);
        }
    }

    private static float Sanitize(float v) => float.IsFinite(v) ? v : 0f;

    private static float ToX(byte raw) => (raw - 127.5f) / 127.5f;

    // Raw Y grows downward; flip it so up is positive.
    private static float ToY(byte raw) => -(raw - 127.5f) / 127.5f;

    private void Learn(float x, float y)
    {
        _winX[_winIndex] = x;
        _winY[_winIndex] = y;
        _winIndex = (_winIndex + 1) % _winX.Length;
        if (_winCount < _winX.Length) _winCount++;

        bool resting = false;
        if (_winCount == _winX.Length)
        {
            float mx = 0, my = 0;
            for (int i = 0; i < _winX.Length; i++)
            {
                mx += _winX[i];
                my += _winY[i];
            }
            mx /= _winX.Length;
            my /= _winY.Length;

            float maxDist = 0;
            for (int i = 0; i < _winX.Length; i++)
            {
                float ex = _winX[i] - mx, ey = _winY[i] - my;
                maxDist = Math.Max(maxDist, MathF.Sqrt(ex * ex + ey * ey));
            }

            if (maxDist <= _o.RestTolerance && MathF.Sqrt(mx * mx + my * my) <= _o.MaxDrift)
            {
                resting = true;
                LearnRest(mx, my, maxDist, startOfRest: !_wasResting);
            }
        }
        _wasResting = resting;

        LearnOuter(x, y);
    }

    private void LearnRest(float mx, float my, float jitter, bool startOfRest)
    {
        if (startOfRest && _restSamples >= _o.SpreadWarmupSamples)
        {
            float ox = mx - _centerX, oy = my - _centerY;
            float d = MathF.Sqrt(ox * ox + oy * oy);
            float rate = d > _spread ? _o.SpreadRiseRate : _o.SpreadFallRate;
            _spread += (d - _spread) * rate;
        }

        _restSamples++;
        // Plain average while there is little data, then a slow running average.
        float warm = 1f / _restSamples;
        float centerRate = Math.Max(_o.CenterRate, warm);
        _centerX += (mx - _centerX) * centerRate;
        _centerY += (my - _centerY) * centerRate;
        _noise += (jitter - _noise) * Math.Max(_o.NoiseRate, warm);
    }

    private void LearnOuter(float x, float y)
    {
        float dx = x - _centerX, dy = y - _centerY;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        // Only trust a radius that two samples in a row reached.
        float confirmed = _prevRadius < 0 ? 0f : Math.Min(r, _prevRadius);
        _prevRadius = r;
        if (confirmed <= 0) return;

        int s = SectorOf(MathF.Atan2(dy, dx));
        if (confirmed > _outer[s]) _outer[s] = confirmed;
    }

    private float SectorWidth => 2f * MathF.PI / _outer.Length;

    private int SectorOf(float angle)
    {
        int s = (int)MathF.Round(Normalize(angle) / SectorWidth);
        return s % _outer.Length;
    }

    private static float Normalize(float angle)
    {
        float a = angle % (2f * MathF.PI);
        return a < 0 ? a + 2f * MathF.PI : a;
    }

    private float TrustedRadius(int s) => _outer[s] >= _o.MinTrustedOuterRadius ? _outer[s] : 1f;

    /// <summary>Learned outer edge in the given direction, interpolated between neighbouring sectors.</summary>
    private float EdgeAt(float angle)
    {
        float p = Normalize(angle) / SectorWidth;
        int i0 = (int)MathF.Floor(p) % _outer.Length;
        int i1 = (i0 + 1) % _outer.Length;
        float t = p - MathF.Floor(p);
        return TrustedRadius(i0) * (1f - t) + TrustedRadius(i1) * t;
    }
}
