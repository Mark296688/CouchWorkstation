namespace CouchWorkstation.Wii;

/// <summary>Core buttons as laid out in the two Wii Remote button bytes (wiibrew), byte1 &lt;&lt; 8 | byte2.</summary>
[Flags]
public enum WiiButtons : ushort
{
    None = 0,
    Two = 0x0001,
    One = 0x0002,
    B = 0x0004,
    A = 0x0008,
    Minus = 0x0010,
    Home = 0x0080,
    Left = 0x0100,
    Right = 0x0200,
    Down = 0x0400,
    Up = 0x0800,
    Plus = 0x1000,
}

/// <summary>One IR blob seen by the Wii Remote camera (1024x768 grid).</summary>
public readonly record struct IrDot(int X, int Y, int Size);

/// <summary>One parsed input report. Timestamp is Stopwatch ticks at the moment the report was read.</summary>
public sealed record WiiReport(long Timestamp, WiiButtons Buttons, IReadOnlyList<IrDot> Dots, byte AccX, byte AccY, byte AccZ);
