using System.Text;
using System.Text.Json;
using DS4Handler.App.Services;

namespace DS4Handler.Core.Tests.AppLinked;

/// <summary>
/// SettingsStore (src/DS4Handler.App/Services/AppSettings.cs, compiled in): a settings.json that cannot be read must
/// never cost the list of controllers this app hid in HidHide.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private const string Id1 = @"HID\VID_054C&PID_09CC&MI_03\7&1234&0&0000";
    private const string Id2 = @"HID\VID_054C&PID_0CE6\9&FF&0&0000";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DS4HandlerSettingsTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string ValidJson(bool activated = true) => JsonSerializer.Serialize(new AppSettings
    {
        StartWithWindows = true,
        HiddenInstanceIds = new() { Id1, Id2 },
        HidHideActivatedByApp = activated,
        Controllers = new() { ["054C-09CC-AB"] = new ControllerPreferences { Name = "Sem" } },
    }, new JsonSerializerOptions { WriteIndented = true });

    [Fact]
    public void NoFile_GivesDefaults_AndIsNotCreatedByLoading()
    {
        var store = new SettingsStore(FilePath);
        Assert.False(store.ExistedAtStartup);
        Assert.False(store.ReadOnly);
        Assert.Empty(store.Settings.HiddenInstanceIds);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ValidFile_Loads_AndSaveLoadRoundTrips_WithBom()
    {
        File.WriteAllText(FilePath, ValidJson(), Encoding.UTF8); // with BOM, as SaveLocked writes it
        var store = new SettingsStore(FilePath);
        Assert.True(store.ExistedAtStartup);
        Assert.Equal(new[] { Id1, Id2 }, store.Settings.HiddenInstanceIds);
        Assert.True(store.Settings.HidHideActivatedByApp);
        Assert.Equal("Sem", store.Settings.For("054c-09cc-ab").Name); // case-insensitive after load
        Assert.False(File.Exists(FilePath + ".bad"));

        store.Update(s => s.HiddenInstanceIds.Add("HID\\NEW"));
        var again = new SettingsStore(FilePath);
        Assert.Equal(new[] { Id1, Id2, "HID\\NEW" }, again.Settings.HiddenInstanceIds);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void NullsAndBlanks_InAValidFile_AreDropped()
    {
        File.WriteAllText(FilePath, "{\"HiddenInstanceIds\": [\"A\", \"\", \" \", null], \"Controllers\": {\"x\": null}}");
        var store = new SettingsStore(FilePath);
        Assert.Equal(new[] { "A" }, store.Settings.HiddenInstanceIds);
        Assert.Empty(store.Settings.Controllers);
        Assert.False(store.ReadOnly);
    }

    [Fact]
    public void NullList_InAValidFile_GivesEmpty()
    {
        File.WriteAllText(FilePath, "{\"HiddenInstanceIds\": null, \"Controllers\": null}");
        var store = new SettingsStore(FilePath);
        Assert.Empty(store.Settings.HiddenInstanceIds);
        Assert.Empty(store.Settings.Controllers);
    }

    [Fact]
    public void TruncatedFile_KeepsHidHideOwnership_MakesBackup_AndSavingKeepsTheIds()
    {
        var cut = ValidJson()[..^2]; // cut off "\n}" right after the last value
        File.WriteAllText(FilePath, cut);

        var store = new SettingsStore(FilePath);
        Assert.True(store.ExistedAtStartup);
        Assert.False(store.ReadOnly);
        Assert.Equal(new[] { Id1, Id2 }, store.Settings.HiddenInstanceIds);
        Assert.True(store.Settings.HidHideActivatedByApp);
        Assert.False(store.Settings.StartWithWindows); // everything else is back to defaults
        Assert.Equal(cut, File.ReadAllText(FilePath + ".bad"));

        store.Save();
        var reloaded = new SettingsStore(FilePath);
        Assert.Equal(new[] { Id1, Id2 }, reloaded.Settings.HiddenInstanceIds);
        Assert.True(reloaded.Settings.HidHideActivatedByApp);
    }

    [Theory]
    [InlineData("{\"StartWithWindows\": \"ja\", \"HiddenInstanceIds\": [\"HID\\\\A\"], \"HidHideActivatedByApp\": true}")] // wrong type elsewhere
    [InlineData("{\"Controllers\": [], \"HiddenInstanceIds\": [\"HID\\\\A\"], \"HidHideActivatedByApp\": true}")]          // array for a dictionary
    [InlineData("{\"HiddenInstanceIds\": [\"HID\\\\A\"], \"HidHideActivatedByApp\": true,}")]                              // trailing comma
    [InlineData("{\"HiddenInstanceIds\": [\"HID\\\\A\"], \"HidHideActivatedByApp\": true} trailing garbage")]
    public void InvalidJson_ElsewhereInTheFile_StillKeepsTheIds(string json)
    {
        File.WriteAllText(FilePath, json);
        var store = new SettingsStore(FilePath);
        Assert.Equal(new[] { @"HID\A" }, store.Settings.HiddenInstanceIds);
        Assert.True(store.Settings.HidHideActivatedByApp);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\0\0\0\0\0\0\0\0")] // what a power cut often leaves behind
    public void EmptyOrNonsenseFile_GivesDefaults_WithBackup(string text)
    {
        File.WriteAllText(FilePath, text);
        var store = new SettingsStore(FilePath);
        Assert.Empty(store.Settings.HiddenInstanceIds);
        Assert.False(store.ReadOnly);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void BackupImpossible_FileIsNeverOverwritten()
    {
        File.WriteAllText(FilePath, "{\"HiddenInstanceIds\": [\"HID\\\\A\"], oops");
        Directory.CreateDirectory(FilePath + ".bad"); // File.Copy onto a directory fails
        var store = new SettingsStore(FilePath);
        Assert.True(store.ReadOnly);
        Assert.Equal(new[] { @"HID\A" }, store.Settings.HiddenInstanceIds);

        store.Update(s => s.StartWithWindows = true);
        store.Save();
        Assert.Equal("{\"HiddenInstanceIds\": [\"HID\\\\A\"], oops", File.ReadAllText(FilePath));
    }

    [Fact]
    public void LockedFile_IsNotReplacedWithDefaults()
    {
        File.WriteAllText(FilePath, ValidJson());
        SettingsStore store;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool enforced;
            try { File.ReadAllText(FilePath); enforced = false; }
            catch (IOException) { enforced = true; }
            if (!enforced) return; // this platform does not enforce FileShare.None within one process: nothing to test
            store = new SettingsStore(FilePath);
        }
        Assert.True(store.ReadOnly);
        store.Update(s => s.HiddenInstanceIds.Clear());
        Assert.Equal(ValidJson(), File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void ReadHidHideOwnership_ReadsValidAndDamagedFiles_AndNeverWrites()
    {
        Assert.Null(SettingsStore.ReadHidHideOwnership(FilePath));
        Assert.False(File.Exists(FilePath));

        File.WriteAllText(FilePath, ValidJson(activated: false));
        var valid = SettingsStore.ReadHidHideOwnership(FilePath);
        Assert.Equal(new[] { Id1, Id2 }, valid!.Value.HiddenInstanceIds);
        Assert.False(valid.Value.ActivatedByApp);

        var damaged = ValidJson()[..^20];
        File.WriteAllText(FilePath, damaged);
        var salvaged = SettingsStore.ReadHidHideOwnership(FilePath);
        Assert.Equal(new[] { Id1, Id2 }, salvaged!.Value.HiddenInstanceIds);
        Assert.False(File.Exists(FilePath + ".bad")); // another user's file: read only
        Assert.Equal(damaged, File.ReadAllText(FilePath));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName));
    }
}

/// <summary>
/// HidHideUninstall (compiled in). On Linux the HidHide driver is "not installed", which exercises the paths that do
/// not touch the driver: the report, the exit-code rule and what happens to settings.json.
/// </summary>
public sealed class HidHideUninstallTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DS4HandlerUninstallTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public HidHideUninstallTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData(UninstallOutcome.Done, 0, true)]
    [InlineData(UninstallOutcome.Done, 3, true)]
    [InlineData(UninstallOutcome.NotInstalled, 0, true)]
    [InlineData(UninstallOutcome.NotInstalled, 2, false)]
    [InlineData(UninstallOutcome.Busy, 0, false)]
    [InlineData(UninstallOutcome.Busy, 2, false)]
    [InlineData(UninstallOutcome.TimedOut, 0, false)]
    [InlineData(UninstallOutcome.Failed, 1, false)]
    public void Complete_OnlyWhenNothingOwnedCanStillBeHidden(UninstallOutcome outcome, int owned, bool complete)
        => Assert.Equal(complete, new UninstallReport(outcome, owned).Complete);

    [Fact]
    public void NoSettingsFile_IsNotCreated()
    {
        var store = new SettingsStore(FilePath);
        var report = HidHideUninstall.Run(store, allUsers: false, TimeSpan.FromSeconds(5));
        Assert.Equal(UninstallOutcome.NotInstalled, report.Outcome);
        Assert.True(report.Complete);
        Assert.False(File.Exists(FilePath));
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void DriverMissing_WithOwnedIds_IsIncomplete_AndKeepsTheList()
    {
        var json = "{\"HiddenInstanceIds\": [\"HID\\\\A\", \"HID\\\\B\"], \"HidHideActivatedByApp\": true}";
        File.WriteAllText(FilePath, json);
        var store = new SettingsStore(FilePath);
        var report = HidHideUninstall.Run(store, allUsers: false, TimeSpan.FromSeconds(5));
        Assert.Equal(UninstallOutcome.NotInstalled, report.Outcome);
        Assert.Equal(2, report.Owned);
        Assert.False(report.Complete);
        Assert.Null(report.Cleanup);
        Assert.Equal(json, File.ReadAllText(FilePath)); // nothing was undone, so nothing is forgotten
    }

    [Fact]
    public void AllUsers_WithoutAProfileRegistry_DoesNotThrow()
    {
        var store = new SettingsStore(FilePath);
        var report = HidHideUninstall.Run(store, allUsers: true, TimeSpan.FromSeconds(5));
        Assert.Equal(UninstallOutcome.NotInstalled, report.Outcome);
        Assert.False(File.Exists(FilePath));
    }
}
