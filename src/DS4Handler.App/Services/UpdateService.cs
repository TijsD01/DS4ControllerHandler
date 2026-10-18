using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using DS4Handler.App.Infrastructure;
using DS4Handler.Core.Updates;
using Microsoft.Win32;

namespace DS4Handler.App.Services;

/// <summary>
/// Looks on GitHub Releases for a newer version, downloads its setup and starts it. The setup
/// upgrades the installed app in place; profiles and settings live in %APPDATA% and stay.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Same AppId as installer/DS4Handler.iss; Inno Setup registers the uninstall key under it.</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6C1E3B52-7A4D-4F0B-9C2E-5D8A1F3E9B47}_is1";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The version of this exe, as stamped by the release workflow.</summary>
    public static ReleaseVersion CurrentVersion { get; } = ReadCurrentVersion();

    /// <summary>
    /// True when this exe was put here by the installer. The portable exe can't update itself
    /// in place, so it gets a link to the download page instead.
    /// </summary>
    public static bool IsInstalled => InstalledForAllUsers.HasValue;

    /// <summary>null = not installed (portable); true = installed for all users (Program Files); false = for this user.</summary>
    private static bool? InstalledForAllUsers { get; } = DetectInstalled();

    /// <summary>Returns the newer version, or null when up to date or when GitHub can't be reached.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(UpdateCheck.ReleasesApiUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"Zoeken naar updates: GitHub gaf {(int)response.StatusCode}.");
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var update = UpdateCheck.FindUpdate(CurrentVersion, UpdateCheck.ParseReleases(json, CurrentVersion.IsPrerelease && !IsDevBuild));
            Log.Info(update == null
                ? $"Geen nieuwere versie dan {CurrentVersion}."
                : $"Nieuwe versie beschikbaar: {update.Latest.Version} (nu {CurrentVersion}).");
            return update;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn("Zoeken naar updates mislukt", ex);
            return null;
        }
    }

    /// <summary>Downloads the setup to %TEMP% and returns its path. Reports progress from 0 to 1.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!UpdateCheck.IsTrustedDownload(release.SetupUrl))
            throw new InvalidOperationException("Onverwacht downloadadres: " + release.SetupUrl);

        var folder = Path.Combine(Path.GetTempPath(), "DS4Handler-Update");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(release.SetupName));
        var partial = target + ".part";

        using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.SetupSize;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
            }
            if (release.SetupSize > 0 && done != release.SetupSize)
                throw new IOException($"Download onvolledig ({done} van {release.SetupSize} bytes).");
        }
        File.Move(partial, target, overwrite: true);
        Log.Info($"Update gedownload: {target}");
        return target;
    }

    /// <summary>
    /// Starts the setup with a small progress window and no questions. It closes this app if it
    /// is still running, replaces the old version and starts the new one (see /UPDATE in the .iss).
    /// </summary>
    public static void StartInstaller(string setupPath)
    {
        Log.Info("Update-installer starten.");
        Process.Start(new ProcessStartInfo(setupPath)
        {
            // Same install mode as last time, so the update replaces that copy instead of adding a second one.
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1 "
                        + (InstalledForAllUsers == true ? "/ALLUSERS" : "/CURRENTUSER"),
            UseShellExecute = true,
        });
    }

    private static bool IsDevBuild => CurrentVersion.Suffix.StartsWith("dev", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub's API refuses requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DS4Handler/{ReadCurrentVersion()}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static ReleaseVersion ReadCurrentVersion()
    {
        var asm = typeof(UpdateService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (ReleaseVersion.TryParse(info, out var v)) return v;
        var n = asm.GetName().Version;
        return n == null ? default : new ReleaseVersion(n.Major, n.Minor, Math.Max(0, n.Build));
    }

    private static bool? DetectInstalled()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            if (string.IsNullOrEmpty(exeDir)) return null;
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = root.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string location && SameFolder(location, exeDir))
                    return root == Registry.LocalMachine;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Controleren of de app geïnstalleerd is mislukt", ex);
        }
        return null;
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
