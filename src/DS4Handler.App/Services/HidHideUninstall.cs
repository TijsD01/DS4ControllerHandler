using DS4Handler.App.Infrastructure;
using DS4Handler.HidHide;
using Microsoft.Win32;

namespace DS4Handler.App.Services;

public enum UninstallOutcome
{
    /// <summary>HidHide was cleaned up (which may have meant nothing to do).</summary>
    Done,
    NotInstalled,
    /// <summary>Another program kept HidHide open the whole time.</summary>
    Busy,
    TimedOut,
    Failed,
}

/// <summary>What <see cref="HidHideUninstall.Run"/> did.</summary>
public sealed record UninstallReport(UninstallOutcome Outcome, int Owned, HidHideCleanupResult? Cleanup = null)
{
    /// <summary>True when no controller this app hid is left hidden.</summary>
    public bool Complete => Outcome == UninstallOutcome.Done || (Outcome == UninstallOutcome.NotInstalled && Owned == 0);
}

/// <summary>
/// Undoes everything this app changed in HidHide when it is uninstalled (DS4Handler.exe --unhide): shows the
/// controllers it hid, takes its exe off the allow list and turns the global switch off when the app turned it on
/// and nothing else is hidden. With <c>allUsers</c> (an all-users install, uninstalled as administrator) the
/// ownership in every user profile is used, not only the administrator's own settings.
/// </summary>
public static class HidHideUninstall
{
    private static readonly TimeSpan BusyRetry = TimeSpan.FromSeconds(5);

    public static UninstallReport Run(SettingsStore store, bool allUsers, TimeSpan timeout)
    {
        var owned = new List<string>(store.Read(s => s.HiddenInstanceIds.ToArray()));
        bool activatedByApp = store.Read(s => s.HidHideActivatedByApp);
        if (allUsers)
        {
            foreach (var path in OtherUsersSettingsFiles(store.FilePath))
            {
                if (SettingsStore.ReadHidHideOwnership(path) is not { } other) continue;
                foreach (var id in other.HiddenInstanceIds)
                    if (!owned.Contains(id, StringComparer.OrdinalIgnoreCase)) owned.Add(id);
                activatedByApp |= other.ActivatedByApp;
                if (other.HiddenInstanceIds.Count > 0) Log.Info($"Verwijderen: {other.HiddenInstanceIds.Count} verborgen controller(s) gevonden in {path}.");
            }
        }

        var work = Task.Run(() => CleanUp(owned, activatedByApp));
        UninstallReport report;
        if (!work.Wait(timeout)) report = new UninstallReport(UninstallOutcome.TimedOut, owned.Count);
        else report = work.Result;

        if (report.Cleanup is { } done)
        {
            // Only our own file: other users' settings are read, never written (they go with the app).
            store.Update(s =>
            {
                foreach (var id in done.Unhidden.Concat(done.AlreadyVisible))
                    s.HiddenInstanceIds.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                if (done.Deactivate) s.HidHideActivatedByApp = false;
            });
        }
        LogReport(report, activatedByApp, timeout);
        return report;
    }

    private static UninstallReport CleanUp(List<string> owned, bool activatedByApp)
    {
        var deadline = DateTime.UtcNow + BusyRetry;
        while (true)
        {
            try
            {
                var result = new HidHideClient().CleanUp(owned, Environment.ProcessPath, activatedByApp);
                return new UninstallReport(UninstallOutcome.Done, owned.Count, result);
            }
            catch (HidHideException ex) when (ex.Kind == HidHideErrorKind.Busy && DateTime.UtcNow < deadline)
            {
                // Right after taskkill the old process may still be closing its HidHide handle.
                Thread.Sleep(500);
            }
            catch (HidHideException ex) when (ex.Kind == HidHideErrorKind.Busy)
            {
                Log.Warn("HidHide bleef bezet", ex);
                return new UninstallReport(UninstallOutcome.Busy, owned.Count);
            }
            catch (HidHideException ex) when (ex.Kind == HidHideErrorKind.NotInstalled)
            {
                return new UninstallReport(UninstallOutcome.NotInstalled, owned.Count);
            }
            catch (Exception ex)
            {
                Log.Warn("HidHide opruimen mislukt", ex);
                return new UninstallReport(UninstallOutcome.Failed, owned.Count);
            }
        }
    }

    private static void LogReport(UninstallReport r, bool activatedByApp, TimeSpan timeout)
    {
        switch (r.Outcome)
        {
            case UninstallOutcome.Done:
                var c = r.Cleanup!;
                Log.Info(c.Unhidden.Count > 0
                    ? $"Verwijderen: {c.Unhidden.Count} controller(s) weer zichtbaar gemaakt."
                    : "Verwijderen: geen door DS4 Handler verborgen controllers meer in HidHide.");
                if (c.WhitelistChanged) Log.Info("Verwijderen: DS4Handler.exe uit de HidHide-uitzonderingen gehaald.");
                if (c.Deactivate) Log.Info("Verwijderen: HidHide-verbergen (algemene schakelaar) weer uitgezet.");
                else if (activatedByApp && c.OthersStillHidden > 0)
                    Log.Info($"Verwijderen: HidHide-verbergen blijft aan; nog {c.OthersStillHidden} apparaat/apparaten verborgen door de gebruiker of een ander programma.");
                break;
            case UninstallOutcome.NotInstalled:
                if (r.Owned == 0) Log.Info("Verwijderen: HidHide is niet geïnstalleerd; niets zichtbaar te maken.");
                else Log.Warn($"Verwijderen: HidHide is niet (meer) geïnstalleerd; {r.Owned} controller(s) staan nog als verborgen genoteerd maar konden niet gecontroleerd worden.");
                break;
            case UninstallOutcome.Busy:
                Log.Warn($"Verwijderen: HidHide is bezet door een ander programma (zoals HidHide Configuration Client of DS4Windows); {r.Owned} controller(s) blijven verborgen. Haal ze zelf van de lijst in HidHide Configuration Client.");
                break;
            case UninstallOutcome.TimedOut:
                Log.Warn($"Verwijderen: HidHide reageerde niet binnen {timeout.TotalSeconds:0} s; {r.Owned} controller(s) zijn mogelijk nog verborgen.");
                break;
            default:
                Log.Warn($"Verwijderen: HidHide opruimen mislukt; {r.Owned} controller(s) zijn mogelijk nog verborgen.");
                break;
        }
    }

    /// <summary>
    /// settings.json of every other user profile on this PC (from the registry's profile list; a redirected
    /// AppData folder is not found). Only readable when running as administrator; unreadable ones are skipped.
    /// </summary>
    private static IEnumerable<string> OtherUsersSettingsFiles(string ownFile)
    {
        var result = new List<string>();
        try
        {
            using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (list == null) return result;
            foreach (var sid in list.GetSubKeyNames())
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)) continue; // real accounts only
                using var key = list.OpenSubKey(sid);
                if (key?.GetValue("ProfileImagePath") is not string profile || string.IsNullOrWhiteSpace(profile)) continue;
                var file = Path.Combine(Environment.ExpandEnvironmentVariables(profile),
                    "AppData", "Roaming", Path.GetFileName(Log.DataDirectory), "settings.json");
                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(ownFile), StringComparison.OrdinalIgnoreCase))
                    result.Add(file);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Gebruikersprofielen opvragen mislukt", ex);
        }
        return result;
    }
}
