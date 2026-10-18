namespace DS4Handler.Core.Calibration;

/// <summary>
/// Tunables for <see cref="StickCalibrator"/>. Distances are in normalized stick units,
/// where 1.0 is the distance from the middle of the raw range (127.5) to its edge.
/// Sample counts assume about 250 reports per second.
/// </summary>
public sealed record StickCalibrationOptions
{
    /// <summary>Number of recent samples checked together to decide whether the stick is still.</summary>
    public int RestWindow { get; init; } = 16;

    /// <summary>All samples in the window must be this close to their mean for the stick to count as still.</summary>
    public float RestTolerance { get; init; } = 0.02f;

    /// <summary>Normal learning (start-up releases and drift jumps) never puts the center further than this from the middle.</summary>
    public float MaxDrift { get; init; } = 0.2f;

    /// <summary>The stick must go at least this far from the center before the next rest counts as a release.</summary>
    public float ReleaseDeflection { get; init; } = 0.4f;

    /// <summary>Still samples in a row needed after a deflection before it counts as a release (about 0.5 s).</summary>
    public int ReleaseStillSamples { get; init; } = 125;

    /// <summary>The first few releases may land anywhere within <see cref="MaxDrift"/>; the center is their median.</summary>
    public int BootstrapReleases { get; init; } = 5;

    /// <summary>Releases in a row that must agree on a new spot outside the gate before the center jumps there.</summary>
    public int RecenterReleases { get; init; } = 5;

    /// <summary>Those releases must all be within this distance of their median to count as agreeing.</summary>
    public float RecenterClusterRadius { get; init; } = 0.04f;

    /// <summary>
    /// Largest sudden jump of the center. Bigger offsets that keep repeating are more likely a held aim
    /// than drift; slow changes are still followed, and Reset clears everything.
    /// </summary>
    public float RecenterMaxJump { get; init; } = 0.11f;

    /// <summary>Furthest from the middle a guided center (see <see cref="StickCalibrator.StartGuidedCenter"/>) may be set; also the cap on imported centers.</summary>
    public float GuidedMaxDrift { get; init; } = 0.35f;

    /// <summary>After the first releases, a release only counts when it lands within the deadzone plus this margin.</summary>
    public float CenterGateMargin { get; init; } = 0.04f;

    /// <summary>Slowest rate (per release) at which the center follows where the stick comes to rest.</summary>
    public float CenterRate { get; init; } = 0.1f;

    /// <summary>Rate (per release) at which the noise estimate follows the measured jitter.</summary>
    public float NoiseRate { get; init; } = 0.1f;

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

    /// <summary>Samples in a row that must reach a radius before it counts as the stick's reach (ignores glitches).</summary>
    public int OuterHoldSamples { get; init; } = 4;

    /// <summary>A learned reach shorter than this is not trusted and the nominal 1.0 is used instead.</summary>
    public float MinTrustedOuterRadius { get; init; } = 0.78f;

    /// <summary>Learned reach is capped at a circle of this radius around the middle of the raw range.</summary>
    public float MaxOuterRadius { get; init; } = 1.25f;

    /// <summary>When the center moves this far from where the reach was learned, the learned reach is forgotten.</summary>
    public float OuterResetDistance { get; init; } = 0.03f;

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

    /// <summary>Furthest distance from the center reached per direction sector (0 = not seen). Empty means unknown.</summary>
    public float[] OuterRadii { get; init; } = Array.Empty<float>();

    /// <summary>Number of still samples learned from so far.</summary>
    public int RestSamples { get; init; }

    /// <summary>Number of releases learned from so far.</summary>
    public int Releases { get; init; }
}

/// <summary>
/// Learns the drift, jitter and reach of an analog stick from normal play and maps raw values to a
/// clean -1..1 range per axis (right and up are positive).
///
/// How it learns:
/// <list type="number">
/// <item>Releases: a thumb can hold a stick very still, so stillness alone does not prove the stick
/// was let go. A release only counts when the stick was first pushed clearly away (at least
/// <see cref="StickCalibrationOptions.ReleaseDeflection"/>) and then stays still for about half a
/// second. Each release is one vote, however long the stick then rests. The very first rest after
/// start-up also counts.</item>
/// <item>Center: the first few releases may land anywhere within
/// <see cref="StickCalibrationOptions.MaxDrift"/>, and the center is their median, so drift is found
/// quickly and a couple of held aims among them do not matter. After that, a release only counts
/// when it lands close to the learned center (within the deadzone plus a small margin), so a held aim
/// offset is not learned as drift. The center moves part of the way towards each accepted release.</item>
/// <item>Drift jumps: if several releases in a row all land outside that gate on the same new spot
/// (with no normal release in between, and not too far away), the drift has changed (a dropped
/// controller, or drift that grew between sessions), and the center jumps to that spot.</item>
/// <item>Noise: for each accepted release, the average jitter while still.</item>
/// <item>Rest spread: worn springs return to a slightly different spot on every release. For each
/// accepted release it measures how far it landed from the learned center and keeps a running
/// average that grows quickly and shrinks slowly.</item>
/// <item>Deadzone: a round deadzone around the learned center, sized from the noise and the spread.
/// Outside it the output is rescaled so it starts smoothly from zero at the deadzone edge.</item>
/// <item>Reach: the circle around the center is split into sectors. Each sector remembers the
/// furthest point the stick has reached and held for a few samples in about the same direction
/// (so glitches and repeated packets are ignored), capped at a sane maximum. A sector is trusted once it reaches at least
/// <see cref="StickCalibrationOptions.MinTrustedOuterRadius"/>. Between sectors the edge is
/// interpolated, and a full push in any trusted direction maps to 1.0. When the center moves
/// noticeably the learned reach is forgotten and learned again.</item>
/// </list>
/// </summary>
public sealed class StickCalibrator
{
    private readonly StickCalibrationOptions _o;
    private readonly float[] _winX;
    private readonly float[] _winY;
    private int _winCount;
    private int _winIndex;

    // Release detection.
    private bool _armed = true;
    private int _stillRun;
    private float _stillSumX, _stillSumY, _stillSumJitter;

    // Glitch filter for reach.
    private readonly float[] _recentRadii;
    private readonly int[] _recentSectors;
    private int _recentCount;
    private int _recentIndex;

    private float _centerX;
    private float _centerY;
    private float _noise;
    private float _spread;
    private readonly float[] _outer;
    private float _outerRefX, _outerRefY;
    private int _restSamples;
    private int _releases;
    private bool _guided;
    private readonly List<(float X, float Y)> _boot = new();
    private readonly List<(float X, float Y)> _rejected = new();

    public StickCalibrator(StickCalibrationOptions? options = null)
    {
        _o = options ?? new StickCalibrationOptions();
        _winX = new float[Math.Max(2, _o.RestWindow)];
        _winY = new float[_winX.Length];
        _outer = new float[Math.Max(4, _o.OuterSectors)];
        _recentRadii = new float[Math.Max(1, _o.OuterHoldSamples)];
        _recentSectors = new int[_recentRadii.Length];
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

    /// <summary>Number of releases learned from so far.</summary>
    public int Releases => _releases;

    /// <summary>True while a guided centering started with <see cref="StartGuidedCenter"/> is waiting for a still stick.</summary>
    public bool GuidedInProgress => _guided;

    /// <summary>
    /// Guided centering for a setup wizard: the user lets go of the stick, and the next still period
    /// (about half a second) becomes the center directly, ignoring the usual gates. Use it when the
    /// drift changed too much for normal learning to follow. Works even while learning is disabled.
    /// Spread is reset, noise is taken from that still period, and the learned reach is cleared.
    /// </summary>
    public void StartGuidedCenter()
    {
        _guided = true;
        ClearStillRun();
    }

    /// <summary>Stops a guided centering without changing anything.</summary>
    public void CancelGuided()
    {
        _guided = false;
        ClearStillRun();
    }

    /// <summary>Feeds one raw sample (0..255 per axis), updates the model and returns the calibrated position.</summary>
    public (float X, float Y) Process(byte rawX, byte rawY)
    {
        if (LearningEnabled || _guided) Learn(ToX(rawX), ToY(rawY));
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
        _releases = 0;
        _boot.Clear();
        _rejected.Clear();
        _guided = false;
        Array.Clear(_outer);
        _outerRefX = 0;
        _outerRefY = 0;
        _winCount = 0;
        _winIndex = 0;
        _armed = true;
        ClearStillRun();
        _recentCount = 0;
        _recentIndex = 0;
    }

    public StickCalibrationData Export() => new()
    {
        CenterX = _centerX,
        CenterY = _centerY,
        NoiseRadius = _noise,
        RestSpread = _spread,
        OuterRadii = _outer.Any(v => v > 0) ? (float[])_outer.Clone() : Array.Empty<float>(),
        RestSamples = _restSamples,
        Releases = _releases,
    };

    /// <summary>Restores a saved state. Null or corrupt values fall back to factory defaults.</summary>
    public void Import(StickCalibrationData? data)
    {
        Reset();
        if (data is null) return;

        float cx = Sanitize(data.CenterX), cy = Sanitize(data.CenterY);
        float len = MathF.Sqrt(cx * cx + cy * cy);
        if (len > _o.GuidedMaxDrift)
        {
            cx *= _o.GuidedMaxDrift / len;
            cy *= _o.GuidedMaxDrift / len;
        }
        _centerX = cx;
        _centerY = cy;
        _outerRefX = cx;
        _outerRefY = cy;
        _noise = Math.Clamp(Sanitize(data.NoiseRadius), 0f, 1f);
        _spread = Math.Clamp(Sanitize(data.RestSpread), 0f, 1f);
        _restSamples = Math.Max(0, data.RestSamples);
        _releases = Math.Max(0, data.Releases);
        // Mid-bootstrap: the individual releases are not saved, so stand in the saved center for them.
        if (_releases < _o.BootstrapReleases)
            for (int i = 0; i < _releases; i++) _boot.Add((cx, cy));
        if (data.OuterRadii is { } radii && radii.Length == _outer.Length)
        {
            for (int i = 0; i < _outer.Length; i++) _outer[i] = Math.Clamp(Sanitize(radii[i]), 0f, _o.MaxOuterRadius + 0.3f);
        }
    }

    private static float Sanitize(float v) => float.IsFinite(v) ? v : 0f;

    private static float ToX(byte raw) => (raw - 127.5f) / 127.5f;

    // Raw Y grows downward; flip it so up is positive.
    private static float ToY(byte raw) => -(raw - 127.5f) / 127.5f;

    private void ClearStillRun()
    {
        _stillRun = 0;
        _stillSumX = _stillSumY = _stillSumJitter = 0;
    }

    private void Learn(float x, float y)
    {
        _winX[_winIndex] = x;
        _winY[_winIndex] = y;
        _winIndex = (_winIndex + 1) % _winX.Length;
        if (_winCount < _winX.Length) _winCount++;

        if (_guided)
        {
            LearnGuided();
            return;
        }
        if (!LearningEnabled) return;

        float ox = x - _centerX, oy = y - _centerY;
        if (ox * ox + oy * oy >= _o.ReleaseDeflection * _o.ReleaseDeflection)
        {
            _armed = true;
            ClearStillRun();
        }
        else if (_armed && IsStill(out float mx, out float my, out float jitter))
        {
            _stillRun++;
            _stillSumX += mx;
            _stillSumY += my;
            _stillSumJitter += jitter;
            if (_stillRun >= _o.ReleaseStillSamples)
            {
                _armed = false;
                LearnRelease(_stillSumX / _stillRun, _stillSumY / _stillRun, _stillSumJitter / _stillRun, _stillRun);
                ClearStillRun();
            }
        }
        else
        {
            ClearStillRun();
        }

        LearnOuter(x, y);
    }

    private void LearnGuided()
    {
        if (!IsStill(out float mx, out float my, out float jitter))
        {
            ClearStillRun();
            return;
        }
        _stillRun++;
        _stillSumX += mx;
        _stillSumY += my;
        _stillSumJitter += jitter;
        if (_stillRun < _o.ReleaseStillSamples) return;

        float cx = _stillSumX / _stillRun, cy = _stillSumY / _stillRun;
        float len = Dist(cx, cy);
        if (len > _o.GuidedMaxDrift)
        {
            cx *= _o.GuidedMaxDrift / len;
            cy *= _o.GuidedMaxDrift / len;
        }
        _centerX = cx;
        _centerY = cy;
        _noise = _stillSumJitter / _stillRun;
        _spread = 0;
        _restSamples += _stillRun;
        _releases = Math.Max(_releases + 1, _o.BootstrapReleases);
        _boot.Clear();
        _rejected.Clear();
        Array.Clear(_outer);
        _outerRefX = cx;
        _outerRefY = cy;
        _guided = false;
        _armed = false;
        ClearStillRun();
    }

    private bool IsStill(out float mx, out float my, out float maxDist)
    {
        mx = my = maxDist = 0;
        if (_winCount < _winX.Length) return false;
        for (int i = 0; i < _winX.Length; i++)
        {
            mx += _winX[i];
            my += _winY[i];
        }
        mx /= _winX.Length;
        my /= _winY.Length;
        for (int i = 0; i < _winX.Length; i++)
        {
            float ex = _winX[i] - mx, ey = _winY[i] - my;
            maxDist = Math.Max(maxDist, MathF.Sqrt(ex * ex + ey * ey));
        }
        return maxDist <= _o.RestTolerance;
    }

    private void LearnRelease(float mx, float my, float jitter, int samples)
    {
        if (_releases < _o.BootstrapReleases)
        {
            if (Dist(mx, my) > _o.MaxDrift) return;
            // Early on: any rest within MaxDrift counts, and the center is the median of them, so a
            // couple of held aims among the first releases cannot pull it away.
            _boot.Add((mx, my));
            _releases++;
            _restSamples += samples;
            _centerX = Median(_boot.Select(b => b.X));
            _centerY = Median(_boot.Select(b => b.Y));
            _noise += (jitter - _noise) * Math.Max(_o.NoiseRate, 1f / _releases);
            if (_releases >= _o.BootstrapReleases)
            {
                _spread = Median(_boot.Select(b => Dist(b.X - _centerX, b.Y - _centerY)));
                _boot.Clear();
            }
            AfterCenterMoved();
            return;
        }

        float ox = mx - _centerX, oy = my - _centerY;
        float d = Dist(ox, oy);
        if (d > Deadzone + _o.CenterGateMargin)
        {
            // Probably a held aim, but if many releases in a row agree on a new spot the drift has changed.
            ConsiderRecenter(mx, my, jitter, samples);
            return;
        }
        _rejected.Clear();

        float spreadRate = d > _spread ? _o.SpreadRiseRate : _o.SpreadFallRate;
        _spread += (d - _spread) * spreadRate;

        _releases++;
        _restSamples += samples;
        // One vote per release: the center moves part of the way towards it.
        _centerX += ox * _o.CenterRate;
        _centerY += oy * _o.CenterRate;
        _noise += (jitter - _noise) * _o.NoiseRate;
        AfterCenterMoved();
    }

    private void ConsiderRecenter(float mx, float my, float jitter, int samples)
    {
        _rejected.Add((mx, my));
        if (_rejected.Count > _o.RecenterReleases) _rejected.RemoveAt(0);
        if (_rejected.Count < _o.RecenterReleases) return;

        float cx = Median(_rejected.Select(r => r.X));
        float cy = Median(_rejected.Select(r => r.Y));
        if (_rejected.Any(r => Dist(r.X - cx, r.Y - cy) > _o.RecenterClusterRadius)) return;
        if (Dist(cx - _centerX, cy - _centerY) > _o.RecenterMaxJump || Dist(cx, cy) > _o.MaxDrift) return;

        _centerX = cx;
        _centerY = cy;
        _spread = Median(_rejected.Select(r => Dist(r.X - cx, r.Y - cy)));
        _noise += (jitter - _noise) * _o.NoiseRate;
        _releases++;
        _restSamples += samples;
        _rejected.Clear();
        AfterCenterMoved();
    }

    private void AfterCenterMoved()
    {
        float rx = _centerX - _outerRefX, ry = _centerY - _outerRefY;
        if (rx * rx + ry * ry > _o.OuterResetDistance * _o.OuterResetDistance)
        {
            // Reach was measured from a center that no longer applies.
            Array.Clear(_outer);
            _outerRefX = _centerX;
            _outerRefY = _centerY;
        }
    }

    private static float Dist(float x, float y) => MathF.Sqrt(x * x + y * y);

    private static float Median(IEnumerable<float> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        if (v.Length == 0) return 0f;
        int m = v.Length / 2;
        return v.Length % 2 == 1 ? v[m] : (v[m - 1] + v[m]) / 2f;
    }

    private void LearnOuter(float x, float y)
    {
        float dx = x - _centerX, dy = y - _centerY;
        float r = MathF.Sqrt(dx * dx + dy * dy);

        float angle = MathF.Atan2(dy, dx);
        int s = SectorOf(angle);
        _recentRadii[_recentIndex] = r;
        _recentSectors[_recentIndex] = s;
        _recentIndex = (_recentIndex + 1) % _recentRadii.Length;
        if (_recentCount < _recentRadii.Length) _recentCount++;
        if (_recentCount < _recentRadii.Length || r <= 0) return;

        // Only trust a radius that several samples in a row reached in about the same direction.
        int n = _outer.Length;
        foreach (int other in _recentSectors)
        {
            int diff = Math.Abs(other - s) % n;
            if (Math.Min(diff, n - diff) > 1) return;
        }
        float confirmed = Math.Min(_recentRadii.Min(), MaxReach(dx / r, dy / r));

        if (confirmed > _outer[s]) _outer[s] = confirmed;
    }

    /// <summary>Distance from the center to the cap circle around the raw middle, along a direction.</summary>
    private float MaxReach(float ux, float uy)
    {
        float cu = _centerX * ux + _centerY * uy;
        float cc = _centerX * _centerX + _centerY * _centerY;
        float rr = _o.MaxOuterRadius * _o.MaxOuterRadius;
        return -cu + MathF.Sqrt(Math.Max(0f, cu * cu - cc + rr));
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
