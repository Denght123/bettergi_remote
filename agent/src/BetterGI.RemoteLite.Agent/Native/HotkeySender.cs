using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.Native;

internal static class HotkeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;

    public static bool IsValid(string value) => TryParse(value, out _);

    public static bool Send(string value)
    {
        if (!TryParse(value, out var keys))
        {
            return false;
        }

        var inputs = new List<Input>();
        foreach (var key in keys)
        {
            inputs.Add(Create(key, false));
        }
        foreach (var key in keys.Reverse())
        {
            inputs.Add(Create(key, true));
        }
        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) == inputs.Count;
    }

    private static bool TryParse(string value, out ushort[] keys)
    {
        keys = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var parsed = new List<ushort>();
        foreach (var token in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ushort key = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 0x11,
                "SHIFT" => 0x10,
                "ALT" => 0x12,
                _ when token.Length == 1 && char.IsAsciiLetterOrDigit(token[0]) => char.ToUpperInvariant(token[0]),
                _ when token.StartsWith('F') && int.TryParse(token[1..], out var function) && function is >= 1 and <= 24 => (ushort)(0x70 + function - 1),
                _ => 0,
            };
            if (key == 0 || parsed.Contains(key))
            {
                return false;
            }
            parsed.Add(key);
        }
        if (parsed.Count < 2 || !parsed.Any(key => key is 0x10 or 0x11 or 0x12))
        {
            return false;
        }
        keys = parsed.ToArray();
        return true;
    }

    private static Input Create(ushort key, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = key,
                Flags = keyUp ? KeyUp : 0,
            },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}

