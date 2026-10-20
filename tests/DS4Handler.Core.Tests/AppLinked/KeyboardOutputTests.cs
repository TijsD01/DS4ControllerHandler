using DS4Handler.App.Engine;

namespace DS4Handler.Core.Tests.AppLinked;

public class KeyboardOutputTests
{
    [Fact]
    public void OwnKeyIsRecognisedByItsMarker()
    {
        Assert.True(KeyboardOutput.IsOwnKey(KeyboardOutput.Marker));
        Assert.False(KeyboardOutput.IsOwnKey(IntPtr.Zero)); // a real keyboard leaves extra info at 0
        Assert.False(KeyboardOutput.IsOwnKey(KeyboardOutput.Marker + 1));
    }
}
