using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DS4Handler.App.Engine;
using DS4Handler.App.Services;
using DS4Handler.Core.Input;
using DS4Handler.Core.Mapping;

namespace DS4Handler.App.ViewModels;

/// <summary>One entry in a row's target drop-down.</summary>
public sealed class RemapOption : ObservableObject
{
    public const string GroupButtons = "CONTROLLERKNOPPEN";
    public const string GroupTriggers = "TRIGGERS";
    public const string GroupOther = ""; // keyboard and "Uit": shown under a plain divider

    private string _label;

    public RemapOption(string group, RemapTarget target, string label, bool isDefault = false, bool picksKey = false)
    {
        Group = group;
        Target = target;
        _label = label;
        IsDefault = isDefault;
        PicksKey = picksKey;
    }

    public string Group { get; }
    public RemapTarget Target { get; }

    /// <summary>What the source does when nothing is changed (shown as "standaard" in the list).</summary>
    public bool IsDefault { get; }

    /// <summary>"Toetsenbord…": choosing it starts listening for a key instead of picking a target.</summary>
    public bool PicksKey { get; }

    public string Label { get => _label; set => SetField(ref _label, value); }

    public override string ToString() => Label; // text search and screen readers
}

/// <summary>One physical button and what the game gets for it.</summary>
public sealed class RemapRow : ObservableObject
{
    private readonly RemapViewModel _owner;
    private readonly ObservableCollection<RemapOption> _items = new();
    private readonly Dictionary<PadControl, RemapOption> _pad = new();
    private readonly RemapOption _pickKey, _off;
    private readonly RemapOption? _nothing;
    private RemapOption? _key;
    private RemapOption? _selected;
    private RemapTarget _target;
    private bool _found, _capturing, _refused;
    private string _captureNote = "";

    internal RemapRow(RemapViewModel owner, PadControl source, string badge, string name)
    {
        _owner = owner;
        Source = source;
        Badge = badge;
        Name = name;
        Mask = ButtonRemap.ToButtons(source);

        foreach (var c in Enum.GetValues<PadControl>())
        {
            if (c == PadControl.Mute) continue; // not a pad output
            var group = c is PadControl.L2 or PadControl.R2 ? RemapOption.GroupTriggers : RemapOption.GroupButtons;
            var option = new RemapOption(group, RemapTarget.ToPad(c), "", isDefault: c == source);
            _pad[c] = option;
        }
        // Buttons first, then triggers (the order of the groups), each in PadControl order.
        foreach (var o in _pad.Values.Where(o => o.Group == RemapOption.GroupButtons)) _items.Add(o);
        foreach (var o in _pad.Values.Where(o => o.Group == RemapOption.GroupTriggers)) _items.Add(o);
        _pickKey = new RemapOption(RemapOption.GroupOther, RemapTarget.ToKey(0), "Toetsenbord…", picksKey: true);
        _off = new RemapOption(RemapOption.GroupOther, RemapTarget.Disabled, "Uit");
        if (source == PadControl.Mute)
        {
            // The mute button has no counterpart on the virtual pad: by default it does nothing.
            _nothing = new RemapOption(RemapOption.GroupOther, RemapTarget.Same, "Niets", isDefault: true);
            _items.Add(_nothing);
        }
        _items.Add(_pickKey);
        _items.Add(_off);

        var view = new ListCollectionView(_items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RemapOption.Group)));
        Options = view;

        ResetCommand = new RelayCommand(() => _owner.Pick(this, RemapTarget.Same));
        CancelCaptureCommand = new RelayCommand(() => _owner.CancelCapture());
    }

    public PadControl Source { get; }

    /// <summary>Short mark in front of the name: "✕", "L1", "↑".</summary>
    public string Badge { get; }

    /// <summary>The badge is a symbol (✕ ○ □ △ arrows) rather than letters: drawn larger in a symbol font.</summary>
    public bool BadgeIsSymbol => Badge.Length == 1;
    public string Name { get; }
    internal Buttons Mask { get; }

    /// <summary>Drop-down items, grouped by <see cref="RemapOption.Group"/>.</summary>
    public ICollectionView Options { get; }

    public ICommand ResetCommand { get; }
    public ICommand CancelCaptureCommand { get; }

    /// <summary>
    /// The drop-down list is open (set by the view). Cleared only after the close has been handled, because
    /// the ComboBox may close the list before it commits the pick (Enter).
    /// </summary>
    public bool IsListOpen { get; set; }

    public string AutomationName => $"{Name}: wat de game krijgt";

    /// <summary>Differs from the default: accent marker and a "Standaard" link.</summary>
    public bool IsChanged => _target.Kind != RemapKind.Same;

    /// <summary>Pulses when the physical button is pressed on this tab (the view restarts the highlight on every true).</summary>
    public bool IsFound { get => _found; internal set => SetField(ref _found, value); }

    /// <summary>Waiting for a keyboard key ("Druk op een toets").</summary>
    public bool IsCapturing { get => _capturing; internal set => SetField(ref _capturing, value); }

    /// <summary>Line under the row while listening: how to cancel, or why a key was refused.</summary>
    public string CaptureNote { get => _captureNote; internal set => SetField(ref _captureNote, value); }

    /// <summary>The last key pressed while listening cannot be used (the note turns into a warning).</summary>
    public bool CaptureRefused { get => _refused; internal set => SetField(ref _refused, value); }

    /// <summary>Tooltip of the row: triggers explain how they mix with buttons.</summary>
    public string Tip => Source is PadControl.L2 or PadControl.R2 ? Tips.RemapTarget + "\n\n" + Tips.RemapTriggerTarget : Tips.RemapTarget;

    public RemapOption? SelectedOption
    {
        get => _selected;
        set
        {
            if (value == null || value == _selected) return;
            if (value.PicksKey)
            {
                // Only a choice made in the open list listens for a key; a closed box that lands on it
                // (text search) must never map the next key by accident.
                if (IsListOpen) _owner.StartCapture(this);
                RevertSelection();
                return;
            }
            _owner.Pick(this, value.Target);
        }
    }

    /// <summary>The ComboBox ignores a change made while it is setting the value, so put the old item back afterwards.</summary>
    private void RevertSelection() =>
        Dispatcher.CurrentDispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedOption)), DispatcherPriority.Background);

    internal void Load(RemapTarget target)
    {
        bool changed = IsChanged;
        _target = target;
        if (target.Kind == RemapKind.Key)
        {
            string label = "Toets " + KeyNames.Name(target.ScanCode);
            if (_key == null)
            {
                _key = new RemapOption(RemapOption.GroupOther, target, label);
                _items.Insert(_items.IndexOf(_pickKey), _key);
            }
            else if (_key.Target != target)
            {
                // A new item, not a relabel: its Target identifies the key.
                var old = _key;
                _key = new RemapOption(RemapOption.GroupOther, target, label);
                _items[_items.IndexOf(old)] = _key;
            }
        }
        else if (_key != null)
        {
            _items.Remove(_key);
            _key = null;
        }

        _selected = target.Kind switch
        {
            RemapKind.Pad => _pad[target.Output],
            RemapKind.Key => _key,
            RemapKind.None => _off,
            _ => _nothing ?? _pad[Source],
        };
        OnPropertyChanged(nameof(SelectedOption));
        if (changed != IsChanged) OnPropertyChanged(nameof(IsChanged));
    }

    /// <summary>Target names follow what the game sees: Xbox names or PlayStation names.</summary>
    internal void SetLabels(bool xbox)
    {
        foreach (var (control, option) in _pad) option.Label = RemapNames.Target(control, xbox);
    }
}

/// <summary>A titled block of rows ("SYMBOOLKNOPPEN").</summary>
public sealed record RemapGroup(string Title, IReadOnlyList<RemapRow> Rows);

/// <summary>
/// The "Knoppen" tab of one controller: which button the game gets for each physical button, a
/// keyboard key, or nothing, plus swapping the sticks. Every change is applied at once through
/// <see cref="ControllerSession.ApplyTuning"/> with the same generation guard as <see cref="TuningViewModel"/>.
/// </summary>
public sealed class RemapViewModel : ObservableObject
{
    private readonly ControllerSession _session;
    private readonly AppHost _host;
    private readonly Func<string, string, string, bool> _confirm;
    private readonly Dictionary<PadControl, RemapRow> _rows = new();
    private ButtonRemap _current;
    private int _generation;
    private bool _xbox, _canSave = true, _blocked;
    private Buttons _held;
    private RemapRow? _capturing;

    public RemapViewModel(ControllerSession session, AppHost host, ControllerViewModel pad, bool xbox, Func<string, string, string, bool> confirm)
    {
        _session = session;
        _host = host;
        _confirm = confirm;
        _xbox = xbox;
        Pad = pad;
        bool dualSense = session.Model != ControllerModel.DualShock4;

        RemapRow Row(PadControl c) => _rows[c] = new RemapRow(this, c, RemapNames.Badge(c), RemapNames.Source(c, dualSense));
        var groups = new List<RemapGroup>
        {
            new("SYMBOOLKNOPPEN", new[] { Row(PadControl.Cross), Row(PadControl.Circle), Row(PadControl.Square), Row(PadControl.Triangle) }),
            new("SCHOUDERKNOPPEN EN TRIGGERS", new[] { Row(PadControl.L1), Row(PadControl.R1), Row(PadControl.L2), Row(PadControl.R2) }),
            new("PIJLTJES", new[] { Row(PadControl.DpadUp), Row(PadControl.DpadDown), Row(PadControl.DpadLeft), Row(PadControl.DpadRight) }),
            new("STICKS INDRUKKEN", new[] { Row(PadControl.L3), Row(PadControl.R3) }),
        };
        var middle = new List<RemapRow> { Row(PadControl.Share), Row(PadControl.Options), Row(PadControl.PS), Row(PadControl.TouchpadClick) };
        if (dualSense) middle.Add(Row(PadControl.Mute)); // the DualShock 4 has no mute button
        groups.Add(new("MIDDEN", middle));
        Groups = groups;

        _generation = session.TuningGeneration; // before Tuning, see ControllerSession.TuningGeneration
        _current = session.Tuning.Remap;
        ResetAllCommand = new RelayCommand(ResetAll, () => !_current.IsIdentity);
        foreach (var row in _rows.Values) row.SetLabels(xbox);
        Load(_current);
        UpdateFlags();
    }

    /// <summary>The controller page, for the live picture (pressed buttons, sticks, triggers).</summary>
    public ControllerViewModel Pad { get; }

    public IReadOnlyList<RemapGroup> Groups { get; }
    public ICommand ResetAllCommand { get; }

    /// <summary>A physical button was pressed on this tab: the view scrolls its row into sight.</summary>
    public event Action<RemapRow>? RowFound;

    /// <summary>Listening for a key started: the view moves focus to the "Druk op een toets" box.</summary>
    public event Action<RemapRow>? CaptureStarted;

    /// <summary>Listening for a key stopped (key taken or cancelled): the view puts focus back on the row.</summary>
    public event Action<RemapRow>? CaptureEnded;

    public bool IsCapturing => _capturing != null;

    public bool SwapSticks
    {
        get => _current.SwapSticks;
        set { if (value != _current.SwapSticks) Apply(_current.WithSwapSticks(value)); }
    }

    /// <summary>"3 knoppen aangepast", or that everything works as printed on the controller.</summary>
    public string ChangedText
    {
        get
        {
            int n = _rows.Values.Count(r => r.IsChanged);
            return n == 0 ? "Elke knop doet wat erop staat." : n == 1 ? "1 knop aangepast." : $"{n} knoppen aangepast.";
        }
    }

    /// <summary>Under the picture: the names on the right are what the game sees.</summary>
    public string OutputHint => _xbox
        ? "Rechts zie je wat de game krijgt, met Xbox-namen (de game ziet een Xbox 360-controller)."
        : "Rechts zie je wat de game krijgt, met PlayStation-namen (de game ziet een DualShock 4).";

    private bool CanSave { get => _canSave; set { if (SetField(ref _canSave, value)) OnPropertyChanged(nameof(SaveNote)); } }

    public string SaveNote => CanSave ? ""
        : _blocked ? "Wijzigingen werken nu, maar worden niet bewaard: dit profiel is in gebruik door een andere controller."
        : "Wijzigingen werken nu, maar worden niet bewaard: het profielbestand kon niet worden gelezen. Het bestand blijft ongemoeid.";

    internal void SetOutputXbox(bool xbox)
    {
        if (_xbox == xbox) return;
        _xbox = xbox;
        foreach (var row in _rows.Values) row.SetLabels(xbox);
        OnPropertyChanged(nameof(OutputHint));
    }

    /// <summary>
    /// Called on every UI tick with the physical buttons (triggers count as pressed past half way).
    /// While the tab is shown, a newly pressed button highlights its row.
    /// </summary>
    internal void Update(Buttons raw, byte l2, byte r2, bool shown)
    {
        if (l2 > 127) raw |= Buttons.L2Digital;
        if (r2 > 127) raw |= Buttons.R2Digital;
        var pressed = raw & ~_held;
        _held = raw;
        if (!shown || pressed == Buttons.None || _capturing != null) return;
        foreach (var group in Groups)
            foreach (var row in group.Rows)
            {
                if ((pressed & row.Mask) == 0) continue;
                row.IsFound = false; // restart the highlight
                row.IsFound = true;
                RowFound?.Invoke(row);
                return;
            }
    }

    /// <summary>Called by the controller view model about twice per second.</summary>
    public void Refresh()
    {
        UpdateFlags();
        if (_session.TuningGeneration != _generation)
        {
            // Another profile (link, unlink, import): its buttons win.
            CancelCapture();
            ReloadFromSession();
            return;
        }
        var live = _session.Tuning.Remap;
        if (!live.Equals(_current)) Load(live);
    }

    private void UpdateFlags()
    {
        bool blocked = _session.ProfileBlocked;
        if (blocked != _blocked)
        {
            _blocked = blocked;
            OnPropertyChanged(nameof(SaveNote));
        }
        CanSave = _session.CanSaveTuning;
    }

    private void ReloadFromSession()
    {
        _generation = _session.TuningGeneration;
        Load(_session.Tuning.Remap);
    }

    private void Load(ButtonRemap remap)
    {
        _current = remap;
        foreach (var row in _rows.Values) row.Load(remap[row.Source]);
        OnPropertyChanged(nameof(SwapSticks));
        OnPropertyChanged(nameof(ChangedText));
        CommandManager.InvalidateRequerySuggested();
    }

    internal void Pick(RemapRow row, RemapTarget target)
    {
        if (target.Kind == RemapKind.Pad && target.Output == PadControl.Mute) return; // never offered; With would throw
        Apply(_current.With(row.Source, target));
    }

    private void Apply(ButtonRemap remap)
    {
        if (remap.Equals(_current)) return;
        var tuning = _session.Tuning with { Remap = remap };
        if (!_host.SetTuning(_session, tuning, _generation))
        {
            // The profile changed underneath us: show its buttons instead of writing ours into it.
            ReloadFromSession();
            return;
        }
        Load(remap);
    }

    private void ResetAll()
    {
        if (!_confirm(
                "Alle knoppen op standaard?",
                $"Elke knop van deze {Pad.Title} doet weer wat erop staat, toetsen worden losgekoppeld en de sticks staan weer op hun eigen plek. Afstellen en kalibratie blijven zoals ze zijn.",
                "Op standaard zetten"))
            return;
        Apply(ButtonRemap.Identity);
    }

    // ----- Keyboard key -----

    internal void StartCapture(RemapRow row)
    {
        if (_capturing != null && _capturing != row) CancelCapture();
        _capturing = row;
        row.CaptureNote = "Esc stopt zonder iets te wijzigen.";
        row.CaptureRefused = false;
        row.IsCapturing = true;
        OnPropertyChanged(nameof(IsCapturing));
        CaptureStarted?.Invoke(row);
    }

    public void CancelCapture()
    {
        if (_capturing is not { } row) return;
        _capturing = null;
        row.IsCapturing = false;
        row.CaptureNote = "";
        row.CaptureRefused = false;
        OnPropertyChanged(nameof(IsCapturing));
        CaptureEnded?.Invoke(row);
    }

    /// <summary>
    /// A key went down while listening. Returns true when it was used (taken, refused or Esc), so the
    /// view marks the key event handled and it does not reach buttons or the tab bar.
    /// </summary>
    public bool HandleKey(Key key)
    {
        if (_capturing is not { } row) return false;
        if (key == Key.Escape)
        {
            CancelCapture();
            return true;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        ushort scan = vk == 0 ? (ushort)0 : KeyNames.ScanCode(vk);
        if (scan == 0)
        {
            row.CaptureNote = "Deze toets wordt niet ondersteund. Kies een andere, of Esc om te stoppen.";
            row.CaptureRefused = true;
            return true;
        }
        CancelCapture();
        Pick(row, RemapTarget.ToKey(scan));
        return true;
    }
}

/// <summary>Names of sources (always PlayStation) and targets (Xbox or PlayStation, following the output).</summary>
internal static class RemapNames
{
    public static string Badge(PadControl c) => c switch
    {
        PadControl.Cross => "✕",
        PadControl.Circle => "○",
        PadControl.Square => "□",
        PadControl.Triangle => "△",
        PadControl.DpadUp => "↑",
        PadControl.DpadDown => "↓",
        PadControl.DpadLeft => "←",
        PadControl.DpadRight => "→",
        PadControl.Share => "SH",
        PadControl.Options => "OP",
        PadControl.TouchpadClick => "TP",
        PadControl.Mute => "MIC",
        _ => c.ToString(), // L1, R1, L2, R2, L3, R3, PS
    };

    public static string Source(PadControl c, bool dualSense) => c switch
    {
        PadControl.Cross => "Kruisje",
        PadControl.Circle => "Rondje",
        PadControl.Square => "Vierkantje",
        PadControl.Triangle => "Driehoekje",
        PadControl.L1 => "Linker schouderknop",
        PadControl.R1 => "Rechter schouderknop",
        PadControl.L2 => "Linker trigger",
        PadControl.R2 => "Rechter trigger",
        PadControl.Share => dualSense ? "Create" : "Share",
        PadControl.Options => "Options",
        PadControl.L3 => "Linker stick indrukken",
        PadControl.R3 => "Rechter stick indrukken",
        PadControl.PS => "PlayStation-knop",
        PadControl.TouchpadClick => "Touchpad-klik",
        PadControl.Mute => "Microfoonknop",
        PadControl.DpadUp => "Omhoog",
        PadControl.DpadDown => "Omlaag",
        PadControl.DpadLeft => "Links",
        PadControl.DpadRight => "Rechts",
        _ => c.ToString(),
    };

    public static string Target(PadControl c, bool xbox) => xbox
        ? c switch
        {
            PadControl.Cross => "A",
            PadControl.Circle => "B",
            PadControl.Square => "X",
            PadControl.Triangle => "Y",
            PadControl.L1 => "LB",
            PadControl.R1 => "RB",
            PadControl.L2 => "LT",
            PadControl.R2 => "RT",
            PadControl.Share => "Back",
            PadControl.Options => "Start",
            PadControl.L3 => "LS (linker stick in)",
            PadControl.R3 => "RS (rechter stick in)",
            PadControl.PS => "Guide",
            PadControl.TouchpadClick => "Touchpad (niet op Xbox)",
            PadControl.DpadUp => "D-pad omhoog",
            PadControl.DpadDown => "D-pad omlaag",
            PadControl.DpadLeft => "D-pad links",
            PadControl.DpadRight => "D-pad rechts",
            _ => c.ToString(),
        }
        : c switch
        {
            PadControl.Cross => "✕ Kruisje",
            PadControl.Circle => "○ Rondje",
            PadControl.Square => "□ Vierkantje",
            PadControl.Triangle => "△ Driehoekje",
            PadControl.L3 => "L3 (linker stick in)",
            PadControl.R3 => "R3 (rechter stick in)",
            PadControl.PS => "PS-knop",
            PadControl.TouchpadClick => "Touchpad-klik",
            PadControl.DpadUp => "↑ Omhoog",
            PadControl.DpadDown => "↓ Omlaag",
            PadControl.DpadLeft => "← Links",
            PadControl.DpadRight => "→ Rechts",
            _ => c.ToString(), // L1, R1, L2, R2, Share, Options
        };
}

/// <summary>Scan codes and their names for the current keyboard layout.</summary>
internal static class KeyNames
{
    private const uint MapvkVkToVscEx = 4;

    /// <summary>Set-1 scan code of a virtual key (0xE0xx for extended keys), or 0 when it cannot be sent.</summary>
    public static ushort ScanCode(int vk)
    {
        uint scan = MapVirtualKeyW((uint)vk, MapvkVkToVscEx);
        // Only plain and 0xE0-extended codes (1..0xE0FF); e.g. Pause (0xE11D) cannot be sent as one scan code.
        return RemapTarget.IsSendableScanCode((int)scan) ?(ushort)scan : (ushort)0;
    }

    /// <summary>"Spatiebalk", "W", "Rechter Ctrl"; a hex code when Windows has no name.</summary>
    public static string Name(ushort scan)
    {
        int lParam = (scan & 0xFF) << 16 | ((scan >> 8) == 0xE0 ? 1 << 24 : 0);
        var text = new StringBuilder(64);
        int length = GetKeyNameTextW(lParam, text, text.Capacity);
        if (length <= 0) return $"0x{scan:X2}";
        // Windows gives some names in capitals ("SPACE", "RIGHT CTRL"); show them like the rest of the app.
        string name = text.ToString();
        return name.Length > 1 && name == name.ToUpperInvariant()
            ? name[0] + name[1..].ToLowerInvariant()
            : name;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(int lParam, StringBuilder text, int size);
}
