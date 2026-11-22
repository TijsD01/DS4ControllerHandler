using System.Windows;

namespace DS4Handler.App.Controls;

/// <summary>
/// Animation durations for the theme (use with x:Static). Zero when "Show animations in Windows" is off
/// (reduced motion), so animations snap to their end state. Release is longer than press so it reads as settling.
/// </summary>
public static class Motion
{
    private static Duration Ms(int ms) => new(SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero);

    /// <summary>Button pressed down.</summary>
    public static Duration Press => Ms(80);
    /// <summary>Button springs back.</summary>
    public static Duration Release => Ms(150);
    /// <summary>Page or dialog appears.</summary>
    public static Duration Enter => Ms(180);
}
