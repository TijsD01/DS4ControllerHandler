using DS4Handler.App.Engine;
using HidSharp;

namespace DS4Handler.Core.Tests.AppLinked;

public class HidInputQueueTests
{
    // Guards the shortcut in HidInputQueue: a HidSharp update that renames the field fails here, not silently on users.
    [Fact]
    public void HidSharpWindowsStream_StillHasItsHandleField()
    {
        var type = typeof(HidStream).Assembly.GetType("HidSharp.Platform.Windows.WinHidStream");
        Assert.NotNull(type);
        Assert.NotNull(HidInputQueue.HandleField(type!));
    }

}
