using DS4Handler.App.Lang;

namespace DS4Handler.App.ViewModels;

/// <summary>
/// All fixed tooltip texts in one place (plain language, for gamers without technical background); the
/// text lives in Lang/CodeStrings*.resx under "Tip_Name". Used from XAML with {x:Static vm:Tips.Name}.
/// Properties, not constants, so the text follows the UI language. Tooltips that depend on live values
/// are built in the view models.
/// </summary>
public static class Tips
{
    // ----- Top bar -----
    public static string Brand => Loc.Get("Tip_Brand");
    public static string NavSettings => Loc.Get("Tip_NavSettings");
    public static string ControllerTab => Loc.Get("Tip_ControllerTab");
    public static string NoControllerTab => Loc.Get("Tip_NoControllerTab");
    public static string DriversSection => Loc.Get("Tip_DriversSection");

    // ----- Controller page -----
    public static string ControllerTitle => Loc.Get("Tip_ControllerTitle");
    public static string TabLive => Loc.Get("Tip_TabLive");
    public static string TabCalibration => Loc.Get("Tip_TabCalibration");
    public static string TabAdjust => Loc.Get("Tip_TabAdjust");
    public static string TabButtons => Loc.Get("Tip_TabButtons");
    public static string CalibrationSummary => Loc.Get("Tip_CalibrationSummary");

    // ----- Controller card -----
    public static string ControllerName => Loc.Get("Tip_ControllerName");
    public static string ControllerId => Loc.Get("Tip_ControllerId");
    public static string ControllerIdUnstable => Loc.Get("Tip_ControllerIdUnstable");
    public static string Usb => Loc.Get("Tip_Usb");
    public static string Bluetooth => Loc.Get("Tip_Bluetooth");
    public static string ReportRate => Loc.Get("Tip_ReportRate");
    public static string Latency => Loc.Get("Tip_Latency");

    // ----- Live view -----
    public static string StickView => Loc.Get("Tip_StickView");
    public static string Deadzone => Loc.Get("Tip_Deadzone");
    public static string TriggerBar => Loc.Get("Tip_TriggerBar");
    public static string LegendRaw => Loc.Get("Tip_LegendRaw");
    public static string LegendOutput => Loc.Get("Tip_LegendOutput");
    public static string LegendDeadzone => Loc.Get("Tip_LegendDeadzone");
    public static string Pad => Loc.Get("Tip_Pad");
    public static string PadInput => Loc.Get("Tip_PadInput");
    public static string PadOutput => Loc.Get("Tip_PadOutput");

    // ----- Calibration -----
    public static string CalibrationSection => Loc.Get("Tip_CalibrationSection");
    public static string Learning => Loc.Get("Tip_Learning");
    public static string JitterFilter => Loc.Get("Tip_JitterFilter");
    public static string HideFromGames => Loc.Get("Tip_HideFromGames");
    public static string HideUnavailable => Loc.Get("Tip_HideUnavailable");
    public static string Output => Loc.Get("Tip_Output");
    public static string OutputXbox => Loc.Get("Tip_OutputXbox");
    public static string OutputDs4 => Loc.Get("Tip_OutputDs4");
    public static string Reset => Loc.Get("Tip_Reset");
    public static string Recenter => Loc.Get("Tip_Recenter");
    public static string CancelGuided => Loc.Get("Tip_CancelGuided");
    public static string CalibrateTrigger => Loc.Get("Tip_CalibrateTrigger");
    public static string StickSummary => Loc.Get("Tip_StickSummary");
    public static string TriggerSummary => Loc.Get("Tip_TriggerSummary");

    // ----- Empty state -----
    public static string EmptyState => Loc.Get("Tip_EmptyState");

    // ----- Settings -----
    public static string StartWithWindows => Loc.Get("Tip_StartWithWindows");
    public static string StartMinimized => Loc.Get("Tip_StartMinimized");
    public static string HidHideEnabled => Loc.Get("Tip_HidHideEnabled");
    public static string UnhideOnExit => Loc.Get("Tip_UnhideOnExit");
    public static string RecheckHidHide => Loc.Get("Tip_RecheckHidHide");
    public static string DownloadHidHide => Loc.Get("Tip_DownloadHidHide");
    public static string DownloadViGEm => Loc.Get("Tip_DownloadViGEm");
    public static string OpenProfiles => Loc.Get("Tip_OpenProfiles");
    public static string CheckForUpdates => Loc.Get("Tip_CheckForUpdates");
    public static string CheckNow => Loc.Get("Tip_CheckNow");
    public static string UpdateNow => Loc.Get("Tip_UpdateNow");
    public static string UpdateLater => Loc.Get("Tip_UpdateLater");
    public static string UpdateDownloadPage => Loc.Get("Tip_UpdateDownloadPage");
    public static string OpenLog => Loc.Get("Tip_OpenLog");
    public static string Quit => Loc.Get("Tip_Quit");

    // ----- Header chips -----
    public static string DriverReady => Loc.Get("Tip_DriverReady");
    public static string DriverMissing => Loc.Get("Tip_DriverMissing");
    public static string DriverChecking => Loc.Get("Tip_DriverChecking");

    // ----- Profielen -----
    public static string NavProfiles => Loc.Get("Tip_NavProfiles");
    public static string ProfilesIntro => Loc.Get("Tip_ProfilesIntro");
    public static string ProfileName => Loc.Get("Tip_ProfileName");
    public static string ProfileRename => Loc.Get("Tip_ProfileRename");
    public static string ProfileMac => Loc.Get("Tip_ProfileMac");
    public static string ProfileNoMac => Loc.Get("Tip_ProfileNoMac");
    public static string ProfileModel => Loc.Get("Tip_ProfileModel");
    public static string ProfileConnected => Loc.Get("Tip_ProfileConnected");
    public static string ProfileLastSeen => Loc.Get("Tip_ProfileLastSeen");
    public static string ProfileSummary => Loc.Get("Tip_ProfileSummary");
    public static string ProfileUsedBy => Loc.Get("Tip_ProfileUsedBy");
    public static string ProfileCopy => Loc.Get("Tip_ProfileCopy");
    public static string ProfileCopyNone => Loc.Get("Tip_ProfileCopyNone");
    public static string ProfileExport => Loc.Get("Tip_ProfileExport");
    public static string ProfileImport => Loc.Get("Tip_ProfileImport");
    public static string ProfileDelete => Loc.Get("Tip_ProfileDelete");
    public static string ProfileDeleteBlocked => Loc.Get("Tip_ProfileDeleteBlocked");
    public static string ProfileChoose => Loc.Get("Tip_ProfileChoose");
    public static string LinkOwn => Loc.Get("Tip_LinkOwn");
    public static string LinkShared => Loc.Get("Tip_LinkShared");
    public static string ProfileOnCard => Loc.Get("Tip_ProfileOnCard");
    public static string ProfileOwnOnCard => Loc.Get("Tip_ProfileOwnOnCard");
    public static string ProfileBlockedOnCard => Loc.Get("Tip_ProfileBlockedOnCard");
    public static string OpenProfileFolder => Loc.Get("Tip_OpenProfileFolder");

    // ----- Afstellen -----
    public static string TunePicker => Loc.Get("Tip_TunePicker");
    public static string TuneSticks => Loc.Get("Tip_TuneSticks");
    public static string TuneTriggers => Loc.Get("Tip_TuneTriggers");
    public static string TuneSpeed => Loc.Get("Tip_TuneSpeed");
    public static string TuneSaved => Loc.Get("Tip_TuneSaved");
    public static string CurveStick => Loc.Get("Tip_CurveStick");
    public static string CurveTrigger => Loc.Get("Tip_CurveTrigger");
    public static string TuneSection => Loc.Get("Tip_TuneSection");
    public static string TuneResetAll => Loc.Get("Tip_TuneResetAll");
    public static string TuneResetSection => Loc.Get("Tip_TuneResetSection");
    public static string TuneShape => Loc.Get("Tip_TuneShape");
    public static string TuneShapeRadial => Loc.Get("Tip_TuneShapeRadial");
    public static string TuneShapeSquare => Loc.Get("Tip_TuneShapeSquare");
    public static string TuneShapeAxial => Loc.Get("Tip_TuneShapeAxial");
    public static string TuneInner => Loc.Get("Tip_TuneInner");
    public static string TuneOuter => Loc.Get("Tip_TuneOuter");
    public static string TuneAnti => Loc.Get("Tip_TuneAnti");
    public static string TuneTriggerAnti => Loc.Get("Tip_TuneTriggerAnti");
    public static string TuneCurve => Loc.Get("Tip_TuneCurve");
    public static string TuneSensitivity => Loc.Get("Tip_TuneSensitivity");
    public static string TuneFilterResponse => Loc.Get("Tip_TuneFilterResponse");
    public static string TuneSquareOutput => Loc.Get("Tip_TuneSquareOutput");
    public static string TuneInvertX => Loc.Get("Tip_TuneInvertX");
    public static string TuneInvertY => Loc.Get("Tip_TuneInvertY");
    public static string TuneTriggerInner => Loc.Get("Tip_TuneTriggerInner");
    public static string TuneTriggerOuter => Loc.Get("Tip_TuneTriggerOuter");
    public static string TunePoll => Loc.Get("Tip_TunePoll");
    public static string TunePollUnavailable => Loc.Get("Tip_TunePollUnavailable");
    public static string TuneOutputRate => Loc.Get("Tip_TuneOutputRate");

    // ----- Knoppen -----
    public static string RemapPicture => Loc.Get("Tip_RemapPicture");
    public static string RemapTarget => Loc.Get("Tip_RemapTarget");
    public static string RemapTriggerTarget => Loc.Get("Tip_RemapTriggerTarget");
    public static string RemapResetRow => Loc.Get("Tip_RemapResetRow");
    public static string RemapResetAll => Loc.Get("Tip_RemapResetAll");
    public static string RemapSwapSticks => Loc.Get("Tip_RemapSwapSticks");
    public static string RemapCapture => Loc.Get("Tip_RemapCapture");
    public static string RemapChanged => Loc.Get("Tip_RemapChanged");
}
