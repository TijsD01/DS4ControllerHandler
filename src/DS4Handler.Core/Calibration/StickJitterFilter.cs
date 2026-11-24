namespace DS4Handler.Core.Calibration;

/// <summary>
/// Adaptive jitter filter for a calibrated 2D stick position, tuned from a learned jitter amplitude:
/// a constant-velocity (alpha-beta) tracker whose bandwidth rises with speed, as in the One Euro
/// filter (Casiez, Roussel and Vogel, CHI 2012).
///
/// <list type="bullet">
/// <item>The position is filtered as one vector, so the direction does not wobble.</item>
/// <item>The tracker predicts the next position from its velocity, so a steady move has no lag;
/// only the jitter around it is smoothed. While the stick is held it acts as a plain low-pass.</item>
/// <item>A jump far bigger than the jitter (a flick) is taken at once, so it is followed within a
/// sample or two.</item>
/// <item>While still or very slow, a small moving deadband (a fraction of the jitter) holds the
/// output still until the filtered position really moves away.</item>
/// <item>How strong all this is follows from the jitter: a clean stick is passed through
/// unchanged, a shaky one gets more filtering.</item>
/// <item>An input of exactly zero (inside the deadzone) gives exactly zero, and a full push
/// still gives exactly 1.0.</item>
/// </list>
/// Nothing allocates; results depend only on the inputs (deterministic).
/// </summary>
public sealed class StickJitterFilter
{
    private readonly StickCalibrationOptions _o;

    private float _fx, _fy;   // tracked position
    private float _vx, _vy;   // tracked velocity (units per second)
    private float _sx, _sy;   // slow tracker for the speed estimate
    private float _svx, _svy;
    private float _ox, _oy;   // output (deadband reference)
    private bool _primed;

    public StickJitterFilter(StickCalibrationOptions? options = null) => _o = options ?? new StickCalibrationOptions();

    /// <summary>Last speed estimate in output units per second (for diagnostics and tests).</summary>
    public float Speed { get; private set; }

    /// <summary>Forgets the filter state; the next input passes through unchanged.</summary>
    public void Reset()
    {
        _fx = _fy = _vx = _vy = _sx = _sy = _svx = _svy = _ox = _oy = 0f;
        _primed = false;
        Speed = 0f;
    }

    /// <summary>
    /// How strongly a stick with this jitter (normalized raw units, per axis std) is filtered:
    /// 0 = not at all, 1 = fully.
    /// </summary>
    public float StrengthFor(float jitter)
    {
        float span = Math.Max(1e-6f, _o.JitterFull - _o.JitterFloor);
        return Math.Clamp((jitter - _o.JitterFloor) / span, 0f, 1f);
    }

    /// <summary>
    /// Filters one calibrated position (-1..1, inside the unit circle).
    /// </summary>
    /// <param name="x">Calibrated X.</param>
    /// <param name="y">Calibrated Y.</param>
    /// <param name="jitter">Learned jitter in output units (per axis std).</param>
    /// <param name="strength">Filter strength 0..1, see <see cref="StrengthFor"/>.</param>
    /// <param name="dt">Seconds since the previous sample.</param>
    /// <param name="response">
    /// User choice 0..1, already clamped by the caller, between calm and fast (see <see cref="Tuning.StickTuning.FilterResponse"/>).
    /// Steady moves have no lag either way; 1 follows changes of direction and stops more closely
    /// at the cost of more leftover tremble (higher bandwidth, lower motion threshold, no deadband).
    /// </param>
    public (float X, float Y) Filter(float x, float y, float jitter, float strength, float dt, float response = 0f)
    {
        if (x == 0f && y == 0f)
        {
            // Inside the deadzone: exactly zero, and start fresh from zero.
            _fx = _fy = _vx = _vy = _sx = _sy = _svx = _svy = _ox = _oy = 0f;
            _primed = true;
            Speed = 0f;
            return (0f, 0f);
        }

        if (!_primed || strength <= 0f)
        {
            // Clean stick (or first sample): pass through, but keep the speed estimate in step so
            // that switching the filter on later is seamless.
            if (_primed) TrackSpeed(x, y, dt);
            else { _sx = x; _sy = y; }
            _fx = _ox = x;
            _fy = _oy = y;
            _vx = _vy = 0f;
            _primed = true;
            return (x, y);
        }

        // Bandwidth follows a slow, low-noise speed estimate. Using the tracker's own velocity would
        // feed back: more bandwidth, noisier velocity, more bandwidth.
        float speed = TrackSpeed(x, y, dt);

        // Constant-velocity (alpha-beta) tracker: predict where the stick is now and correct by the
        // surprise. A steady move is predicted exactly, so unlike a plain low-pass it has no lag;
        // only the noise in the surprise is smoothed.
        float px = _fx + _vx * dt, py = _fy + _vy * dt;
        float ex = x - px, ey = y - py;
        float err = MathF.Sqrt(ex * ex + ey * ey);
        // A surprise well outside the jitter (a flick, or a sudden stop) is real: from the motion
        // threshold up to twice that, the step is taken more and more fully and the velocity is
        // dropped, so a flick that stops does not overshoot. Graded rather than all-or-nothing, so
        // one wild sample does not reset the filter to a noisy position (which would set off the next).
        float threshold = _o.JitterMotionFactor * (1f - 0.5f * response) * jitter + _o.JitterMotionMargin;
        float jump = Math.Clamp(err / threshold - 1f, 0f, 1f);

        // One Euro style: the bandwidth rises with speed. A weak filter has a very high bandwidth.
        float s2 = Math.Max(strength * strength, 1e-4f);
        float cutoff = (_o.JitterMinCutoffHz / s2 + _o.JitterBeta * speed) * (1f + 2f * response);
        // Benedict-Bordner gains (least noise for how fast the velocity follows). While the stick is
        // held the position gain is that of a plain low-pass of this bandwidth (calmest); while it
        // moves, that of a critically damped tracker (twice as quick to follow).
        float slow = Math.Clamp(1f - speed / _o.JitterSlowSpeed, 0f, 1f);
        float a = Alpha(cutoff, dt), g = a * (2f - a - slow * (1f - a)), h = g * g / (2f - g);
        g += (1f - g) * jump;
        _fx = px + g * ex;
        _fy = py + g * ey;
        _vx = (_vx + h * ex / dt) * (1f - jump);
        _vy = (_vy + h * ey / dt) * (1f - jump);

        // Moving deadband while slow: the output only follows once the tracked position moves
        // further than a fraction of the jitter; it is gone at aiming speed (and on a jump), so it
        // adds no lag there.
        float band = _o.JitterDeadbandFactor * jitter * strength * (1f - response) * slow * (1f - jump);
        float dx = _fx - _ox, dy = _fy - _oy;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > band)
        {
            float k = (d - band) / d;
            _ox += dx * k;
            _oy += dy * k;
        }

        float rx = _ox, ry = _oy;
        float r = MathF.Sqrt(rx * rx + ry * ry);
        if (r > 1f)
        {
            rx /= r;
            ry /= r;
            r = 1f;
        }

        // Full push stays full: as the input saturates, the output radius blends to exactly 1
        // (the direction stays filtered).
        float inR = MathF.Sqrt(x * x + y * y);
        float band2 = _o.JitterSaturationBand;
        if (r >= 0.5f && rx * x + ry * y > 0f && inR > 1f - band2)
        {
            float w = Math.Clamp((inR - (1f - band2)) / band2, 0f, 1f);
            float target = r + (1f - r) * w;
            if (w >= 1f) target = 1f;
            rx *= target / r;
            ry *= target / r;
        }
        return (rx, ry);
    }

    /// <summary>Fixed-bandwidth tracker of the input; returns its speed, held at peaks and decaying slowly.</summary>
    private float TrackSpeed(float x, float y, float dt)
    {
        // Critically damped: the speed estimate does not ring after a flick.
        float a = Alpha(_o.JitterSpeedCutoffHz, dt), g = a * (2f - a), h = a * a;
        float ex = x - (_sx + _svx * dt), ey = y - (_sy + _svy * dt);
        _sx += _svx * dt + g * ex;
        _sy += _svy * dt + g * ey;
        _svx += h * ex / dt;
        _svy += h * ey / dt;
        // Held at peaks so the bandwidth does not drop at every turn of a slow aim.
        float s = MathF.Sqrt(_svx * _svx + _svy * _svy);
        Speed = Math.Max(s, Speed + (s - Speed) * a);
        return Speed;
    }

    private static float Alpha(float cutoffHz, float dt)
    {
        float tau = 1f / (2f * MathF.PI * cutoffHz);
        return dt / (dt + tau);
    }
}
