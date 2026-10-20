using System.Text.Json;
using System.Text.Json.Serialization;
using DS4Handler.Core.Mapping;

namespace DS4Handler.Core.Tuning;

/// <summary>
/// Reads the "Tuning" section of a profile tolerantly: a field with a wrong type or value (a hand-edited
/// "Shape": "Hexagon", "Curve": "2", "OutputRateHz": null, 4.5 for an int, ...) or a section that is not
/// an object falls back to its default, so a bad tuning never makes the whole profile, with everything
/// learned, unreadable. Range checks are left to <see cref="InputShaper.Sanitize(InputTuning?)"/>.
/// Writes the normal shape (enum names as strings). Property names match case-insensitively.
/// </summary>
public sealed class InputTuningJsonConverter : JsonConverter<InputTuning>
{
    public override InputTuning Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader); // consumes the value whatever it is
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return InputTuning.Default;
        var d = InputTuning.Default;
        return new InputTuning
        {
            LeftStick = ReadStick(Get(root, nameof(InputTuning.LeftStick))),
            RightStick = ReadStick(Get(root, nameof(InputTuning.RightStick))),
            L2 = ReadTrigger(Get(root, nameof(InputTuning.L2))),
            R2 = ReadTrigger(Get(root, nameof(InputTuning.R2))),
            BluetoothPollIntervalMs = Int(Get(root, nameof(InputTuning.BluetoothPollIntervalMs)), d.BluetoothPollIntervalMs),
            OutputRateHz = Int(Get(root, nameof(InputTuning.OutputRateHz)), d.OutputRateHz),
            Remap = ReadRemap(Get(root, nameof(InputTuning.Remap))),
        };
    }

    private static StickTuning ReadStick(JsonElement? e)
    {
        var d = new StickTuning();
        if (e is not { ValueKind: JsonValueKind.Object } o) return d;
        return new StickTuning
        {
            Shape = Shape(Get(o, nameof(StickTuning.Shape))),
            InnerDeadzone = Float(Get(o, nameof(StickTuning.InnerDeadzone)), d.InnerDeadzone),
            OuterDeadzone = Float(Get(o, nameof(StickTuning.OuterDeadzone)), d.OuterDeadzone),
            AntiDeadzone = Float(Get(o, nameof(StickTuning.AntiDeadzone)), d.AntiDeadzone),
            Curve = Float(Get(o, nameof(StickTuning.Curve)), d.Curve),
            Sensitivity = Float(Get(o, nameof(StickTuning.Sensitivity)), d.Sensitivity),
            SquareOutput = Bool(Get(o, nameof(StickTuning.SquareOutput))),
            InvertX = Bool(Get(o, nameof(StickTuning.InvertX))),
            InvertY = Bool(Get(o, nameof(StickTuning.InvertY))),
        };
    }

    private static TriggerTuning ReadTrigger(JsonElement? e)
    {
        var d = new TriggerTuning();
        if (e is not { ValueKind: JsonValueKind.Object } o) return d;
        return new TriggerTuning
        {
            InnerDeadzone = Float(Get(o, nameof(TriggerTuning.InnerDeadzone)), d.InnerDeadzone),
            OuterDeadzone = Float(Get(o, nameof(TriggerTuning.OuterDeadzone)), d.OuterDeadzone),
            AntiDeadzone = Float(Get(o, nameof(TriggerTuning.AntiDeadzone)), d.AntiDeadzone),
            Curve = Float(Get(o, nameof(TriggerTuning.Curve)), d.Curve),
        };
    }

    private static ButtonRemap ReadRemap(JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.Object } o) return ButtonRemap.Identity;
        var remap = ButtonRemap.Identity;
        if (Bool(Get(o, nameof(ButtonRemap.SwapSticks)))) remap = remap.WithSwapSticks(true);
        if (Get(o, "Map") is not { ValueKind: JsonValueKind.Object } map) return remap;
        foreach (var p in map.EnumerateObject())
        {
            if (p.Name.Length == 0 || char.IsDigit(p.Name[0]) || p.Name[0] == '-') continue;
            if (!Enum.TryParse<PadControl>(p.Name, ignoreCase: true, out var src) || !Enum.IsDefined(src)) continue;

            RemapTarget? target = null;
            if (p.Value.ValueKind == JsonValueKind.String &&
                string.Equals(p.Value.GetString(), "None", StringComparison.OrdinalIgnoreCase))
            {
                target = RemapTarget.Disabled;
            }
            else if (p.Value.ValueKind == JsonValueKind.Object)
            {
                if (Get(p.Value, "Pad") is { ValueKind: JsonValueKind.String } padElem
                    && padElem.GetString() is { } padName
                    && !padName.Any(c => char.IsDigit(c) || c == '-')
                    && Enum.TryParse<PadControl>(padName, ignoreCase: true, out var dstPad)
                    && Enum.IsDefined(dstPad))
                {
                    target = RemapTarget.ToPad(dstPad);
                }
                else if (Get(p.Value, "Key") is { ValueKind: JsonValueKind.Number } keyElem
                    && keyElem.TryGetInt32(out int scanCode)
                    && scanCode is >= 1 and <= 0xE0FF)
                {
                    target = RemapTarget.ToKey((ushort)scanCode);
                }
            }

            if (target is { } t)
            {
                try { remap = remap.With(src, t); }
                catch (ArgumentException) { }
            }
        }
        return remap;
    }

    private static JsonElement? Get(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static float Float(JsonElement? e, float fallback)
    {
        if (e is not { ValueKind: JsonValueKind.Number } n || !n.TryGetDouble(out double v)) return fallback;
        float f = (float)v;
        return float.IsFinite(f) ? f : fallback;
    }

    private static int Int(JsonElement? e, int fallback) =>
        e is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out int v) ? v : fallback;

    private static bool Bool(JsonElement? e) => e is { ValueKind: JsonValueKind.True };

    private static DeadzoneShape Shape(JsonElement? e)
    {
        if (e is { ValueKind: JsonValueKind.String } s
            && s.GetString() is { } name
            && !name.Any(c => char.IsDigit(c) || c == '-' || c == ',')
            && Enum.TryParse(name, ignoreCase: true, out DeadzoneShape shape)
            && Enum.IsDefined(shape))
            return shape;
        if (e is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out int i) && Enum.IsDefined((DeadzoneShape)i))
            return (DeadzoneShape)i;
        return DeadzoneShape.Radial;
    }

    public override void Write(Utf8JsonWriter writer, InputTuning value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(nameof(InputTuning.LeftStick));
        WriteStick(writer, value.LeftStick ?? new StickTuning());
        writer.WritePropertyName(nameof(InputTuning.RightStick));
        WriteStick(writer, value.RightStick ?? new StickTuning());
        writer.WritePropertyName(nameof(InputTuning.L2));
        WriteTrigger(writer, value.L2 ?? new TriggerTuning());
        writer.WritePropertyName(nameof(InputTuning.R2));
        WriteTrigger(writer, value.R2 ?? new TriggerTuning());
        writer.WriteNumber(nameof(InputTuning.BluetoothPollIntervalMs), value.BluetoothPollIntervalMs);
        writer.WriteNumber(nameof(InputTuning.OutputRateHz), value.OutputRateHz);
        if (value.Remap is { IsIdentity: false } remap)
        {
            writer.WritePropertyName(nameof(InputTuning.Remap));
            WriteRemap(writer, remap);
        }
        writer.WriteEndObject();
    }

    private static void WriteRemap(Utf8JsonWriter w, ButtonRemap remap)
    {
        w.WriteStartObject();
        if (remap.SwapSticks) w.WriteBoolean(nameof(ButtonRemap.SwapSticks), true);
        w.WriteStartObject("Map");
        for (int i = 0; i < ButtonRemap.Count; i++)
        {
            var src = (PadControl)i;
            var target = remap[src];
            if (target.Kind == RemapKind.Same) continue;
            w.WritePropertyName(src.ToString());
            switch (target.Kind)
            {
                case RemapKind.Pad:
                    w.WriteStartObject();
                    w.WriteString("Pad", target.Output.ToString());
                    w.WriteEndObject();
                    break;
                case RemapKind.Key:
                    w.WriteStartObject();
                    w.WriteNumber("Key", target.ScanCode);
                    w.WriteEndObject();
                    break;
                case RemapKind.None:
                    w.WriteStringValue("None");
                    break;
            }
        }
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteStick(Utf8JsonWriter w, StickTuning t)
    {
        var d = new StickTuning();
        w.WriteStartObject();
        w.WriteString(nameof(StickTuning.Shape), Enum.IsDefined(t.Shape) ? t.Shape.ToString() : nameof(DeadzoneShape.Radial));
        WriteFloat(w, nameof(StickTuning.InnerDeadzone), t.InnerDeadzone, d.InnerDeadzone);
        WriteFloat(w, nameof(StickTuning.OuterDeadzone), t.OuterDeadzone, d.OuterDeadzone);
        WriteFloat(w, nameof(StickTuning.AntiDeadzone), t.AntiDeadzone, d.AntiDeadzone);
        WriteFloat(w, nameof(StickTuning.Curve), t.Curve, d.Curve);
        WriteFloat(w, nameof(StickTuning.Sensitivity), t.Sensitivity, d.Sensitivity);
        w.WriteBoolean(nameof(StickTuning.SquareOutput), t.SquareOutput);
        w.WriteBoolean(nameof(StickTuning.InvertX), t.InvertX);
        w.WriteBoolean(nameof(StickTuning.InvertY), t.InvertY);
        w.WriteEndObject();
    }

    private static void WriteTrigger(Utf8JsonWriter w, TriggerTuning t)
    {
        var d = new TriggerTuning();
        w.WriteStartObject();
        WriteFloat(w, nameof(TriggerTuning.InnerDeadzone), t.InnerDeadzone, d.InnerDeadzone);
        WriteFloat(w, nameof(TriggerTuning.OuterDeadzone), t.OuterDeadzone, d.OuterDeadzone);
        WriteFloat(w, nameof(TriggerTuning.AntiDeadzone), t.AntiDeadzone, d.AntiDeadzone);
        WriteFloat(w, nameof(TriggerTuning.Curve), t.Curve, d.Curve);
        w.WriteEndObject();
    }

    // JSON has no NaN / infinity: write the default instead of failing the whole save.
    private static void WriteFloat(Utf8JsonWriter w, string name, float value, float fallback) =>
        w.WriteNumber(name, float.IsFinite(value) ? value : fallback);
}
