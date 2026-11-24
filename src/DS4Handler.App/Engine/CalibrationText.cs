using System.Globalization;
using DS4Handler.Core.Calibration;

namespace DS4Handler.App.Engine;

/// <summary>What was learned, in plain Dutch. Shared by the Windows view models and the Mac app.</summary>
internal static class CalibrationText
{
    private static readonly StickCalibrationOptions Defaults = new();
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static string Pct(double fraction) => Math.Round(fraction * 100).ToString("0", Nl) + "%";

    public static string Signed(double v) => v.ToString("+0.00;−0.00;0.00", Nl);

    /// <returns>The sentence, and whether the stick is still learning (orange instead of green).</returns>
    public static (string Text, bool Learning) Stick(string name, StickSummary s, bool guided)
    {
        if (guided) return ($"{name}: laat de stick los… het rustpunt wordt gemeten.", true);

        var text = new System.Text.StringBuilder();
        double drift = Math.Sqrt(s.CenterX * s.CenterX + s.CenterY * s.CenterY);
        bool learning = s.Releases < Defaults.BootstrapReleases;
        if (learning)
            text.Append($"{name}: nog aan het leren — laat de stick af en toe even los. ");
        else if (drift >= 0.02)
            text.Append($"{name} rust op ({Signed(s.CenterX)}; {Signed(s.CenterY)}) in plaats van in het midden — dat punt telt nu als midden. ");
        else
            text.Append($"{name} rust netjes in het midden. ");

        text.Append($"Deadzone {Pct(s.Deadzone)}.");

        if (s.WeakestReach is { } reach && reach < 0.97)
            text.Append($" Haalt in sommige richtingen maar {Pct(reach)} van de rand — wordt bijgeschaald naar 100%.");

        if (s.JitterFilterStrength > 0)
        {
            string jitter = "±" + (s.Jitter * 100).ToString(s.Jitter < 0.1 ? "0.0" : "0", Nl) + "%";
            text.Append(s.JitterFilterEnabled
                ? $" Trilling {name.ToLower(Nl)}: {jitter} (wordt weggefilterd)."
                : $" Trilling {name.ToLower(Nl)}: {jitter} (trillingsfilter staat uit).");
        }
        return (text.ToString(), learning);
    }

    /// <returns>The sentence, and whether the trigger is still learning (orange instead of green).</returns>
    public static (string Text, bool Learning) Trigger(string name, in TriggerLive t)
    {
        if (t.GuidedInProgress) return ($"{name}: assistent loopt — druk de trigger een paar keer helemaal in.", true);

        string text;
        if (!t.EndStopLearned)
        {
            text = $"{name}: nog aan het leren — druk hem tijdens het spelen een paar keer helemaal in. Tot dan geldt het volle bereik.";
        }
        else
        {
            string how = t.EndStopKind == PlateauKind.Guided ? "Ingesteld met de assistent." : "Automatisch geleerd tijdens het spelen.";
            text = t.EndStop >= 250
                ? $"{name} haalt het volle bereik — geen correctie nodig. {how}"
                : $"{name} komt tot {Pct(t.EndStop / 255.0)} — wordt opgeschaald naar 100%. {how}";
        }
        if (t.Rest > 4) text += $" Rustwaarde {Pct(t.Rest / 255.0)} wordt genegeerd.";
        return (text, !t.EndStopLearned);
    }

    /// <summary>"Druk L2 nog 3× helemaal in" while the guided end stop runs, else empty.</summary>
    public static string GuidedHint(string name, in TriggerLive t) =>
        !t.GuidedInProgress ? ""
        : t.GuidedPressesRemaining > 0 ? $"Druk {name} nog {t.GuidedPressesRemaining}× helemaal in"
        : $"Laat {name} los…";
}
