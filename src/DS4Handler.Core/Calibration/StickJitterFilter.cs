namespace DS4Handler.Core.Calibration;

/// <summary>
/// Adaptive low-pass filter for a calibrated 2D stick position, after the One Euro filter
/// (Casiez, Roussel and Vogel, CHI 2012), tuned from a learned jitter amplitude.
///
/// <list type="bullet">
/// <item>The position is filtered as one vector with one shared speed estimate, so the
/// direction does not wobble.</item>
/// <item>The cutoff rises with speed: while the stick is still or moving slowly, a low cutoff
/// removes jitter; during fast moves the cutoff is high, so there is almost no lag.</item>
/// <item>A jump far bigger than the jitter (a flick) counts as fast motion straight away, so
/// it is followed within a sample or two.</item>
/// <item>While slow, a tiny moving deadband (hysteresis, a fraction of the jitter, about what is left after the low-pass) holds the
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

    private float _fx, _fy;   // low-passed position
    private float _vx, _vy;   // low-passed velocity (units per second)
    private float _ox, _oy;   // output (deadband reference)
    private bool _primed;

    public StickJitterFilter(StickCalibrationOptions? options = null) => _o = options ?? new StickCalibrationOptions();

    /// <summary>Last speed estimate in output units per second (for diagnostics and tests).</summary>
    public float Speed { get; private set; }

    /// <summary>Forgets the filter state; the next input passes through unchanged.</summary>
    public void Reset()
    {
        _fx = _fy = _vx = _vy = _ox = _oy = 0f;
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
    /// User choice 0..1 between calm and fast (see <see cref="Tuning.StickTuning.FilterResponse"/>). 0 is the
    /// learned default; 1 raises the cutoff 5×, makes it follow speed 4× sooner, halves the motion threshold
    /// and drops the moving deadband (slow aiming on a worn stick: about 4 ms lag instead of 16 ms).
    /// </param>
    public (float X, float Y) Filter(float x, float y, float jitter, float strength, float dt, float response = 0f)
    {
        response = float.IsFinite(response) ? Math.Clamp(response, 0f, 1f) : 0f;
        if (x == 0f && y == 0f)
        {
            // Inside the deadzone: exactly zero, and start fresh from zero.
            _fx = _fy = _vx = _vy = _ox = _oy = 0f;
            _primed = true;
            Speed = 0f;
            return (0f, 0f);
        }

        if (!_primed || strength <= 0f)
        {
            // Clean stick (or first sample): pass through, but keep the state in step so that
            // switching the filter on later is seamless.
            if (_primed && dt > 0f)
            {
                float a0 = Alpha(_o.JitterSpeedCutoffHz, dt);
                _vx += ((x - _fx) / dt - _vx) * a0;
                _vy += ((y - _fy) / dt - _vy) * a0;
            }
            _fx = _ox = x;
            _fy = _oy = y;
            _primed = true;
            Speed = MathF.Sqrt(_vx * _vx + _vy * _vy);
            return (x, y);
        }

        // Velocity from the change against the filtered position, low-passed (One Euro "dcutoff").
        float ex = x - _fx, ey = y - _fy;
        float rawVx = ex / dt, rawVy = ey / dt;
        float ad = Alpha(_o.JitterSpeedCutoffHz, dt);
        _vx += (rawVx - _vx) * ad;
        _vy += (rawVy - _vy) * ad;
        float speed = MathF.Sqrt(_vx * _vx + _vy * _vy);

        // A jump well outside the jitter is real motion: react on this very sample.
        float err = MathF.Sqrt(ex * ex + ey * ey);
        float motionFactor = _o.JitterMotionFactor * (1f - 0.5f * response);
        if (err > motionFactor * jitter + _o.JitterMotionMargin && err / dt > speed)
        {
            // Remember it, so the rest of the move is followed closely too.
            _vx = rawVx;
            _vy = rawVy;
            speed = err / dt;
        }
        Speed = speed;

        // One Euro: cutoff = min cutoff + beta * speed. A weak filter has a very high min cutoff.
        float s2 = Math.Max(strength * strength, 1e-4f);
        float cutoff = _o.JitterMinCutoffHz * (1f + 4f * response) / s2 + _o.JitterBeta * (1f + 3f * response) * speed;
        float a = Alpha(cutoff, dt);
        _fx += ex * a;
        _fy += ey * a;

        // Moving deadband while slow: the output only follows once the filtered position moves
        // further than the jitter; it fades out as the stick speeds up.
        float band = _o.JitterDeadbandFactor * jitter * strength * (1f - response)
                     * Math.Clamp(1f - speed / _o.JitterSlowSpeed, 0f, 1f);
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

    private static float Alpha(float cutoffHz, float dt)
    {
        float tau = 1f / (2f * MathF.PI * cutoffHz);
        return dt / (dt + tau);
    }
}
