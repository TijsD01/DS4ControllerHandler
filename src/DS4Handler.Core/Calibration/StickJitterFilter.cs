namespace DS4Handler.Core.Calibration;

/// <summary>
/// Adaptive jitter filter for a calibrated 2D stick position, tuned from a learned jitter amplitude:
/// the One Euro filter (Casiez, Roussel and Vogel, CHI 2012) with a constant-velocity (alpha-beta)
/// tracker layered on top once a move is clearly established.
///
/// <list type="bullet">
/// <item>The position is filtered as one vector, so the direction does not wobble.</item>
/// <item>While the stick is held, starts to move or moves very slowly, this is the plain One Euro
/// low-pass: the cutoff rises with speed, so a held stick is calm and the start of a move is
/// caught as soon as the filter starts to trail.</item>
/// <item>Once a separate, slow tracker of the input shows a speed well above what the jitter can
/// fake, the filter also predicts the next position from its velocity, so a steady move (aiming)
/// has almost no lag; only the jitter around it is smoothed.</item>
/// <item>A jump far bigger than the jitter (a flick) counts as fast motion straight away, so it is
/// followed within a sample or two, and the prediction starts over so a flick that stops does
/// not overshoot.</item>
/// <item>While still or slow, a small moving deadband (a fraction of the jitter) holds the output
/// still until the filtered position really moves away.</item>
/// <item>How strong all this is follows from the jitter: a clean stick is passed through
/// unchanged, a shaky one gets more filtering.</item>
/// <item>An input of exactly zero (inside the deadzone) gives exactly zero, and a full push
/// still gives exactly 1.0.</item>
/// </list>
/// Nothing allocates; results depend only on the inputs (deterministic).
/// </summary>
public sealed class StickJitterFilter
{
    // Bandwidth (Hz) of the slow input tracker that decides whether a move is established; low so
    // its speed estimate is quiet.
    private const float TrackCutoffHz = 3f;
    // Tracked speed below this many times the jitter (per second) can be faked by jitter, so it
    // counts as zero: up to there the filter is exactly the plain One Euro filter.
    private const float TrackNoiseFloor = 30f;
    // Tracked speed above the floor (units per second) at which prediction is fully on.
    private const float TrackFullSpeed = 0.2f;
    // How fast the cutoff rises with the tracked speed (Hz per unit per second); keeps a stop
    // out of a sweep from overshooting much.
    private const float TrackBeta = 5f;

    private readonly StickCalibrationOptions _o;

    private float _fx, _fy;   // filtered position
    private float _vx, _vy;   // its velocity, used for prediction (units per second)
    private float _rvx, _rvy; // low-passed surprise per second (One Euro speed)
    private float _sx, _sy;   // slow tracker of the input, for the tracked speed
    private float _svx, _svy;
    private float _trackSpeed;
    private float _ox, _oy;   // output (deadband reference)
    private bool _primed;

    public StickJitterFilter(StickCalibrationOptions? options = null) => _o = options ?? new StickCalibrationOptions();

    /// <summary>Last One Euro speed estimate in output units per second (for diagnostics and tests).</summary>
    public float Speed { get; private set; }

    /// <summary>Forgets the filter state; the next input passes through unchanged.</summary>
    public void Reset()
    {
        _fx = _fy = _vx = _vy = _rvx = _rvy = _sx = _sy = _svx = _svy = _trackSpeed = _ox = _oy = 0f;
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
    /// <param name="dt">Seconds since the previous sample; not positive (or NaN) means the default interval.</param>
    /// <param name="response">
    /// User choice 0..1, already clamped by the caller, between calm and fast (see <see cref="Tuning.StickTuning.FilterResponse"/>). 0 is the
    /// learned default; 1 raises the cutoff 5×, makes it follow speed 4× sooner, halves the motion threshold
    /// and drops the moving deadband (a held stick shakes more, the start of a move and very slow
    /// aiming follow more closely). Once a move is established it is predicted, so steady aiming
    /// has little lag at any setting.
    /// </param>
    public (float X, float Y) Filter(float x, float y, float jitter, float strength, float dt, float response = 0f)
    {
        // A zero or NaN interval would turn every later output into NaN.
        if (!(dt > 0f)) dt = (float)_o.DefaultSampleSeconds;

        if (x == 0f && y == 0f)
        {
            // Inside the deadzone: exactly zero, and start fresh from zero.
            _fx = _fy = _vx = _vy = _rvx = _rvy = _sx = _sy = _svx = _svy = _trackSpeed = _ox = _oy = 0f;
            _primed = true;
            Speed = 0f;
            return (0f, 0f);
        }

        if (!_primed || strength <= 0f)
        {
            // Clean stick (or first sample): pass through, but keep the speed estimates in step so
            // that switching the filter on later is seamless.
            if (_primed)
            {
                float a0 = Alpha(_o.JitterSpeedCutoffHz, dt);
                _rvx += ((x - _fx) / dt - _rvx) * a0;
                _rvy += ((y - _fy) / dt - _rvy) * a0;
                TrackSpeed(x, y, dt);
            }
            else
            {
                _sx = x;
                _sy = y;
            }
            _fx = _ox = x;
            _fy = _oy = y;
            _vx = _vy = 0f;
            _primed = true;
            Speed = MathF.Sqrt(_rvx * _rvx + _rvy * _rvy);
            return (x, y);
        }

        float trackSpeed = Math.Max(0f, TrackSpeed(x, y, dt) - TrackNoiseFloor * jitter);

        // The surprise against the predicted position (without prediction: the filtered position).
        float px = _fx + _vx * dt, py = _fy + _vy * dt;
        float ex = x - px, ey = y - py;
        float rawVx = ex / dt, rawVy = ey / dt;

        // One Euro speed: the surprise per second, low-passed ("dcutoff").
        float ad = Alpha(_o.JitterSpeedCutoffHz, dt);
        _rvx += (rawVx - _rvx) * ad;
        _rvy += (rawVy - _rvy) * ad;
        float speed = MathF.Sqrt(_rvx * _rvx + _rvy * _rvy);

        // A jump well outside the jitter is real motion: react on this very sample. From the
        // motion threshold to twice that, the prediction is dropped more and more, so a flick (or
        // a sudden stop) never carries on past where the stick is.
        float err = MathF.Sqrt(ex * ex + ey * ey);
        float threshold = _o.JitterMotionFactor * (1f - 0.5f * response) * jitter + _o.JitterMotionMargin;
        float jump = Math.Clamp(err / threshold - 1f, 0f, 1f);
        if (jump > 0f)
        {
            if (err / dt > speed)
            {
                // Remember it, so the rest of the move is followed closely too.
                _rvx = rawVx;
                _rvy = rawVy;
                speed = err / dt;
            }
            // The slow tracker starts over too: the step left in it would keep prediction on while
            // the stick is held after a flick.
            _sx = x;
            _sy = y;
            _svx *= 1f - jump;
            _svy *= 1f - jump;
            _trackSpeed *= 1f - jump;
            trackSpeed = Math.Max(0f, _trackSpeed - TrackNoiseFloor * jitter);
        }
        Speed = speed;

        // 1 while the tracked speed is within what jitter can fake, 0 once a move is established.
        float slow = Math.Clamp(1f - trackSpeed / TrackFullSpeed, 0f, 1f);
        slow *= 2f - slow;

        // One Euro: cutoff = min cutoff + beta * speed. A weak filter has a very high min cutoff.
        float s2 = Math.Max(strength * strength, 1e-4f);
        float cutoff = _o.JitterMinCutoffHz * (1f + 4f * response) / s2 + _o.JitterBeta * (1f + 3f * response) * speed
                       + TrackBeta * (1f + 2f * response) * trackSpeed;
        // Slow: a plain low-pass (no prediction). Moving: Benedict-Bordner alpha-beta gains with the
        // position gain of a critically damped tracker, so a steady move is predicted without lag.
        float a = Alpha(cutoff, dt), g = a * (2f - a - slow * (1f - a)), h = g * g / (2f - g);
        _fx = px + g * ex;
        _fy = py + g * ey;
        float keep = (1f - jump) * (1f - slow);
        _vx = (_vx + h * rawVx) * keep;
        _vy = (_vy + h * rawVy) * keep;

        // Moving deadband while slow: the output only follows once the filtered position moves
        // further than the jitter; it fades out as the stick speeds up or a move is established.
        float band = _o.JitterDeadbandFactor * jitter * strength * (1f - response) * slow
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

    /// <summary>Critically damped tracker of the input; returns its speed, held at peaks and decaying slowly.</summary>
    private float TrackSpeed(float x, float y, float dt)
    {
        float a = Alpha(TrackCutoffHz, dt), g = a * (2f - a), h = a * a;
        float ex = x - (_sx + _svx * dt), ey = y - (_sy + _svy * dt);
        _sx += _svx * dt + g * ex;
        _sy += _svy * dt + g * ey;
        _svx += h * ex / dt;
        _svy += h * ey / dt;
        // Held at peaks so prediction does not drop out at every turn of a slow aim.
        float s = MathF.Sqrt(_svx * _svx + _svy * _svy);
        _trackSpeed = Math.Max(s, _trackSpeed + (s - _trackSpeed) * a);
        return _trackSpeed;
    }

    private static float Alpha(float cutoffHz, float dt)
    {
        float tau = 1f / (2f * MathF.PI * cutoffHz);
        return dt / (dt + tau);
    }
}
