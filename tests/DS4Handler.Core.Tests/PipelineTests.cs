using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Output;

namespace DS4Handler.Core.Tests;

/// <summary>
/// Byte-level end-to-end runs: raw HID report bytes → ReportParser → ControllerCalibration →
/// XboxMapper / Ds4Mapper, for every supported controller/connection layout.
/// </summary>
public class PipelineTests
{
    private const int Hz = 250;

    public enum Layout { Ds4Usb, Ds4Bt, DualSenseUsb, DualSenseBt }

    public static IEnumerable<object[]> Layouts() =>
        Enum.GetValues<Layout>().Select(l => new object[] { l });

    /// <summary>Builds realistic raw input reports for one layout, writing only the input fields.</summary>
    private sealed class ReportBuilder
    {
        public readonly Layout Layout;
        public readonly byte[] Buffer;
        private byte _counter;

        public ReportBuilder(Layout layout)
        {
            Layout = layout;
            Buffer = layout switch
            {
                Layout.Ds4Usb => new byte[64],
                Layout.Ds4Bt => new byte[78],
                Layout.DualSenseUsb => new byte[64],
                _ => new byte[78],
            };
            Buffer[0] = layout switch
            {
                Layout.Ds4Usb or Layout.DualSenseUsb => 0x01,
                Layout.Ds4Bt => 0x11,
                _ => 0x31,
            };
            if (layout == Layout.Ds4Bt) { Buffer[1] = 0xC0; Buffer[2] = 0x00; }
            if (layout == Layout.DualSenseBt) Buffer[1] = 0x10; // BT sequence tag byte
        }

        public ControllerModel Model => Layout is Layout.Ds4Usb or Layout.Ds4Bt ? ControllerModel.DualShock4 : ControllerModel.DualSense;
        public ConnectionType Connection => Layout is Layout.Ds4Bt or Layout.DualSenseBt ? ConnectionType.Bluetooth : ConnectionType.Usb;
        private int Offset => Layout switch { Layout.Ds4Usb or Layout.DualSenseUsb => 1, Layout.Ds4Bt => 3, _ => 2 };

        /// <summary>hat: 0..7 or 8 = released. face: 0x10 square, 0x20 cross, 0x40 circle, 0x80 triangle.</summary>
        public byte[] Write(byte lx, byte ly, byte rx, byte ry, byte l2, byte r2, int hat = 8, byte face = 0, byte shoulders = 0, byte ps = 0)
        {
            var d = Buffer.AsSpan(Offset);
            d[0] = lx; d[1] = ly; d[2] = rx; d[3] = ry;
            unchecked { _counter++; }
            if (Model == ControllerModel.DualShock4)
            {
                d[4] = (byte)(hat | face);
                d[5] = (byte)(shoulders | (l2 > 0 ? 0x04 : 0) | (r2 > 0 ? 0x08 : 0));
                d[6] = (byte)(ps | (_counter << 2));
                d[7] = l2; d[8] = r2;
                if (d.Length > 29) d[29] = 0x08; // battery 80%
            }
            else
            {
                d[4] = l2; d[5] = r2;
                d[6] = _counter;
                d[7] = (byte)(hat | face);
                d[8] = (byte)(shoulders | (l2 > 0 ? 0x04 : 0) | (r2 > 0 ? 0x08 : 0));
                d[9] = ps;
                if (d.Length > 52) d[52] = 0x07;
            }
            return Buffer;
        }
    }

    private sealed class Pipeline
    {
        public readonly ReportBuilder Rb;
        public readonly ControllerCalibration Cal;
        public XboxReport LastXbox;
        public Ds4OutputReport LastDs4;
        public CalibratedState LastState;

        public Pipeline(Layout layout)
        {
            Rb = new ReportBuilder(layout);
            Cal = new ControllerCalibration("pipeline-" + layout, Rb.Model);
        }

        public void Feed(byte lx, byte ly, byte rx, byte ry, byte l2, byte r2, int hat = 8, byte face = 0, byte shoulders = 0, byte ps = 0)
        {
            var bytes = Rb.Write(lx, ly, rx, ry, l2, r2, hat, face, shoulders, ps);
            Assert.True(ReportParser.TryParse(Rb.Model, bytes, out var raw, out var conn, Rb.Connection));
            Assert.Equal(Rb.Connection, conn);
            Assert.Equal(lx, raw.LeftX);
            Assert.Equal(r2, raw.R2);
            LastState = Cal.Process(raw);
            LastXbox = XboxMapper.Map(LastState);
            LastDs4 = Ds4Mapper.Map(LastState);
        }

        public void Idle(int n, byte r2 = 0) { for (int i = 0; i < n; i++) Feed(128, 128, 128, 128, 0, r2); }
    }

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    // ------------------------------------------------------------------ friend's worn R2

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Friends_worn_R2_reaches_full_throttle_end_to_end(Layout layout)
    {
        var p = new Pipeline(layout);
        var rng = new Random(301);
        p.Idle(2 * Hz);

        // Before learning: full press on the worn trigger gives only ~78%.
        for (int i = 0; i <= 15; i++) p.Feed(128, 128, 128, 128, 0, (byte)(200 * i / 15));
        for (int i = 0; i < Hz; i++) p.Feed(128, 128, 128, 128, 0, (byte)(199 + rng.Next(0, 2)));
        Assert.InRange(p.LastXbox.RightTrigger, 190, 205);
        for (int i = 15; i >= 0; i--) p.Feed(128, 128, 128, 128, 0, (byte)(200 * i / 15));
        p.Idle(40);

        // Rocket League: kickoffs, boosting with R2 pinned at the worn stop, feathering, demos.
        int fullPresses = 1;
        bool full = false;
        while (fullPresses < 12 && !full)
        {
            for (int i = 0; i <= 10; i++) p.Feed(128, 128, 128, 128, 0, (byte)(200 * i / 10));
            for (int i = 0; i < 3 * Hz; i++)
            {
                double a = 2 * Math.PI * i / Hz;
                p.Feed(B(128 + 100 * Math.Cos(a)), 128, B(128 + 60 * Math.Sin(a)), 128, 0, (byte)(199 + rng.Next(0, 2)),
                    face: (byte)(i % 200 < 10 ? 0x20 : 0));
                full = p.LastXbox.RightTrigger == 255 && p.LastDs4.R2 == 255;
            }
            for (int i = 10; i >= 0; i--) p.Feed(128, 128, 128, 128, 0, (byte)(200 * i / 10));
            p.Idle(rng.Next(20, 80));
            int lvl = rng.Next(80, 170);
            for (int i = 0; i < 40; i++) p.Feed(128, 128, 128, 128, 0, (byte)(lvl + rng.Next(-2, 3)));
            p.Idle(30);
            fullPresses++;
        }

        Assert.True(full, $"never reached RightTrigger 255 in {fullPresses} presses; end stop {p.Cal.R2.EndStop}");
        Assert.True(fullPresses <= 8, $"took {fullPresses} full presses");

        // Now: every sample of a full press reads 255 on both outputs; release reads 0.
        for (int i = 0; i < Hz; i++)
        {
            p.Feed(128, 128, 128, 128, 0, (byte)(199 + rng.Next(0, 2)));
            Assert.Equal(255, p.LastXbox.RightTrigger);
            Assert.Equal(255, p.LastDs4.R2);
            Assert.True(p.LastDs4.Buttons.HasFlag(Ds4OutputButtons.TriggerRight));
        }
        p.Idle(10);
        Assert.Equal(0, p.LastXbox.RightTrigger);
        Assert.Equal(0, p.LastDs4.R2);
        Assert.False(p.LastDs4.Buttons.HasFlag(Ds4OutputButtons.TriggerRight));
        // L2 untouched.
        Assert.False(p.Cal.L2.EndStopLearned);
        // Half press still gives about half.
        p.Feed(128, 128, 128, 128, 0, 100);
        Assert.InRange(p.LastXbox.RightTrigger, 115, 140);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Buttons_and_sticks_survive_the_whole_pipeline(Layout layout)
    {
        var p = new Pipeline(layout);
        p.Idle(Hz);
        p.Feed(255, 0, 0, 255, 0, 0, hat: 1, face: 0x20 | 0x80, shoulders: 0x01 | 0x20, ps: 0x01);
        Assert.Equal(XboxButtons.A | XboxButtons.Y | XboxButtons.Up | XboxButtons.Right | XboxButtons.LeftShoulder | XboxButtons.Start | XboxButtons.Guide,
            p.LastXbox.Buttons);
        // Raw corners are a full diagonal push: output is radial (length 1), so ~0.707 per axis.
        Assert.True(p.LastXbox.LeftThumbX > 22000 && p.LastXbox.LeftThumbY > 22000, $"{p.LastXbox}");
        Assert.True(p.LastXbox.RightThumbX < -22000 && p.LastXbox.RightThumbY < -22000, $"{p.LastXbox}");
        float len = MathF.Sqrt(p.LastState.LeftX * p.LastState.LeftX + p.LastState.LeftY * p.LastState.LeftY);
        Assert.InRange(len, 0.99f, 1.0001f);
        Assert.Equal(Ds4DPad.NorthEast, p.LastDs4.DPad);
        Assert.True(p.LastDs4.LeftX > 210 && p.LastDs4.LeftY < 45, $"{p.LastDs4}");
        Assert.Equal(Ds4SpecialButtons.Ps, p.LastDs4.Special);
        p.Idle(5);
        Assert.Equal(XboxButtons.None, p.LastXbox.Buttons);
        Assert.Equal((short)0, p.LastXbox.LeftThumbX);
        Assert.Equal((byte)128, p.LastDs4.LeftX);
    }

    [Fact]
    public void DualSense_bluetooth_simple_report_is_not_fed_to_learning()
    {
        // 0x01 on a DualSense known to be on BT is the reduced layout: must be rejected.
        var bytes = new byte[10];
        bytes[0] = 0x01; bytes[1] = 128; bytes[5] = 200;
        Assert.False(ReportParser.TryParse(ControllerModel.DualSense, bytes, out _, out _, ConnectionType.Bluetooth));
    }

    [Fact]
    public void Ds4_bluetooth_reduced_report_with_hint_is_labelled_bluetooth()
    {
        var bytes = new byte[10];
        bytes[0] = 0x01; bytes[1] = 128; bytes[2] = 128; bytes[3] = 128; bytes[4] = 128; bytes[5] = 8; bytes[9] = 200;
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, bytes, out var raw, out var conn, ConnectionType.Bluetooth));
        Assert.Equal(ConnectionType.Bluetooth, conn);
        Assert.Equal(200, raw.R2);
    }

    // ------------------------------------------------------------------ guided calibration

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Guided_trigger_calibration_sets_worn_stop_in_three_presses(Layout layout)
    {
        var p = new Pipeline(layout);
        var rng = new Random(302);
        p.Idle(Hz);
        p.Cal.R2.StartGuidedEndStop();
        Assert.True(p.Cal.R2.GuidedInProgress);
        for (int press = 0; press < 3; press++)
        {
            Assert.Equal(3 - press, p.Cal.R2.GuidedPressesRemaining);
            for (int i = 0; i <= 10; i++) p.Feed(128, 128, 128, 128, 0, (byte)(185 * i / 10));
            // Wizard press: shaky (+-3) because the user is squeezing hard.
            for (int i = 0; i < Hz / 2; i++) p.Feed(128, 128, 128, 128, 0, (byte)(183 + rng.Next(-1, 3)));
            for (int i = 10; i >= 0; i--) p.Feed(128, 128, 128, 128, 0, (byte)(185 * i / 10));
            p.Idle(40);
        }
        Assert.False(p.Cal.R2.GuidedInProgress);
        Assert.Equal(PlateauKind.Guided, p.Cal.R2.EndStopKind);
        Assert.InRange(p.Cal.R2.EndStop, 178f, 185f);
        p.Feed(128, 128, 128, 128, 0, 182);
        Assert.Equal(255, p.LastXbox.RightTrigger);
        Assert.Equal(255, p.LastDs4.R2);

        // Automatic learning must not lower a guided stop: 20 steady partial holds at 165.
        for (int k = 0; k < 20; k++)
        {
            for (int i = 0; i <= 10; i++) p.Feed(128, 128, 128, 128, 0, (byte)(165 * i / 10));
            for (int i = 0; i < 2 * Hz; i++) p.Feed(128, 128, 128, 128, 0, (byte)(165 + rng.Next(0, 2)));
            for (int i = 10; i >= 0; i--) p.Feed(128, 128, 128, 128, 0, (byte)(165 * i / 10));
            p.Idle(40);
        }
        Assert.InRange(p.Cal.R2.EndStop, 178f, 185f);
    }

    [Fact]
    public void Guided_trigger_calibration_works_with_learning_disabled_and_can_be_cancelled()
    {
        var p = new Pipeline(Layout.Ds4Usb);
        p.Cal.LearningEnabled = false;
        p.Idle(Hz);
        p.Cal.R2.StartGuidedEndStop();
        for (int i = 0; i <= 10; i++) p.Feed(128, 128, 128, 128, 0, (byte)(190 * i / 10));
        for (int i = 0; i < Hz / 2; i++) p.Feed(128, 128, 128, 128, 0, 190);
        for (int i = 10; i >= 0; i--) p.Feed(128, 128, 128, 128, 0, (byte)(190 * i / 10));
        p.Idle(40);
        Assert.Equal(2, p.Cal.R2.GuidedPressesRemaining);
        p.Cal.R2.CancelGuided();
        Assert.False(p.Cal.R2.GuidedInProgress);
        Assert.Equal(255f, p.Cal.R2.EndStop);

        p.Cal.R2.StartGuidedEndStop();
        for (int press = 0; press < 3; press++)
        {
            for (int i = 0; i <= 10; i++) p.Feed(128, 128, 128, 128, 0, (byte)(190 * i / 10));
            for (int i = 0; i < Hz / 2; i++) p.Feed(128, 128, 128, 128, 0, 190);
            for (int i = 10; i >= 0; i--) p.Feed(128, 128, 128, 128, 0, (byte)(190 * i / 10));
            p.Idle(40);
        }
        Assert.InRange(p.Cal.R2.EndStop, 186f, 190.5f);
        Assert.False(p.Cal.LearningEnabled);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Guided_stick_recenter_fixes_large_drift_immediately(Layout layout)
    {
        var p = new Pipeline(layout);
        var rng = new Random(303);
        // Stick drifting at +0.28 X / -0.1 Y (beyond MaxDrift 0.2, so normal learning ignores it).
        byte dx = B(127.5 + 0.28 * 127.5), dy = B(127.5 + 0.10 * 127.5);
        for (int i = 0; i < 3 * Hz; i++) p.Feed((byte)(dx + rng.Next(0, 2)), dy, 128, 128, 0, 0);
        Assert.True(p.LastXbox.LeftThumbX > 5000, $"expected visible drift before guiding, got {p.LastXbox.LeftThumbX}");

        p.Cal.LeftStick.StartGuidedCenter();
        Assert.True(p.Cal.LeftStick.GuidedInProgress);
        for (int i = 0; i < Hz; i++) p.Feed((byte)(dx + rng.Next(0, 2)), dy, 128, 128, 0, 0);
        Assert.False(p.Cal.LeftStick.GuidedInProgress);
        Assert.InRange(p.Cal.LeftStick.CenterX, 0.26f, 0.30f);

        for (int i = 0; i < Hz; i++)
        {
            p.Feed((byte)(dx + rng.Next(0, 2)), (byte)(dy + rng.Next(0, 2)), 128, 128, 0, 0);
            Assert.Equal((short)0, p.LastXbox.LeftThumbX);
            Assert.Equal((short)0, p.LastXbox.LeftThumbY);
            Assert.Equal((byte)128, p.LastDs4.LeftX);
        }
        // Full push left (away from the drift) still reaches full output.
        for (int i = 0; i < 10; i++) p.Feed(0, 128, 128, 128, 0, 0);
        Assert.True(p.LastXbox.LeftThumbX <= -32000, $"full left gives {p.LastXbox.LeftThumbX}");
        // Right stick untouched.
        Assert.InRange(p.Cal.RightStick.CenterX, -0.01f, 0.01f);
    }

    [Fact]
    public void Guided_stick_recenter_waits_for_a_still_stick()
    {
        var p = new Pipeline(Layout.DualSenseUsb);
        p.Cal.LeftStick.StartGuidedCenter();
        // User is still moving the stick: nothing must be learned.
        for (int i = 0; i < 2 * Hz; i++) p.Feed(B(128 + 40 * Math.Sin(i / 10.0)), 128, 128, 128, 0, 0);
        Assert.True(p.Cal.LeftStick.GuidedInProgress);
        Assert.Equal(0f, p.Cal.LeftStick.CenterX);
        p.Cal.LeftStick.CancelGuided();
        Assert.False(p.Cal.LeftStick.GuidedInProgress);
    }

    // ------------------------------------------------------------------ allocations

    private static long Run(Pipeline p, byte[][] reports, int count, ref long checksum)
    {
        var model = p.Rb.Model;
        var conn = p.Rb.Connection;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            var bytes = reports[i % reports.Length];
            if (!ReportParser.TryParse(model, bytes, out var raw, out _, conn)) continue;
            var s = p.Cal.Process(raw);
            var x = XboxMapper.Map(s);
            var d = Ds4Mapper.Map(s);
            checksum += x.LeftThumbX + x.RightTrigger + d.R2 + d.LeftX;
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Pre-builds a stream of report byte arrays so building them is not measured.</summary>
    private static byte[][] BuildStream(Layout layout, bool withPressesAndReleases)
    {
        var rb = new ReportBuilder(layout);
        var rng = new Random(304);
        var list = new List<byte[]>();
        for (int i = 0; i < 20_000; i++)
        {
            double a = 2 * Math.PI * i / 300.0;
            byte lx = B(128 + 120 * Math.Cos(a)), ly = B(128 + 120 * Math.Sin(a));
            byte rx = B(128 + 70 * Math.Sin(a * 1.3)), ry = B(128 + 70 * Math.Cos(a * 0.7));
            byte r2 = 0, l2 = 0;
            if (withPressesAndReleases)
            {
                int ph = i % 1000;
                // sticks released (still) for part of each cycle so releases are detected
                if (ph >= 600) { lx = 128; ly = 128; rx = 128; ry = 128; }
                r2 = ph < 300 ? (byte)Math.Min(200, ph * 10) : ph < 320 ? (byte)(200 - (ph - 300) * 10) : (byte)0;
                l2 = ph is >= 400 and < 500 ? (byte)(150 + rng.Next(0, 2)) : (byte)0;
            }
            else
            {
                r2 = (byte)(30 + rng.Next(0, 3)); // light, below press threshold
            }
            list.Add((byte[])rb.Write(lx, ly, rx, ry, l2, r2, face: (byte)(i % 50 == 0 ? 0x20 : 0)).Clone());
        }
        return list.ToArray();
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Steady_play_allocates_nothing_per_report(Layout layout)
    {
        var p = new Pipeline(layout);
        var reports = BuildStream(layout, withPressesAndReleases: false);
        long sum = 0;
        Run(p, reports, 20_000, ref sum); // warm-up (JIT, tiering)
        Run(p, reports, 20_000, ref sum);
        long bytes = Run(p, reports, 100_000, ref sum);
        Assert.True(bytes == 0, $"{bytes} bytes allocated over 100k reports ({bytes / 100_000.0:F2} B/report)");
        Assert.NotEqual(0, sum);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Play_with_presses_and_releases_allocates_nothing_per_report(Layout layout)
    {
        var p = new Pipeline(layout);
        var reports = BuildStream(layout, withPressesAndReleases: true);
        long sum = 0;
        Run(p, reports, 40_000, ref sum);
        long bytes = Run(p, reports, 100_000, ref sum);
        Assert.True(bytes == 0, $"{bytes} bytes allocated over 100k reports (100 trigger presses, ~100 stick releases)");
    }
}
