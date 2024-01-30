using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TinyYaz0;

internal static class ReadUtility {
    public static ref T Read<T>(Memory<byte> data, int start) where T : unmanaged {
        return ref MemoryMarshal.AsRef<T>(data.Span[start..]);
    }

    public static ref T Read<T>(Span<byte> span, int start) where T : unmanaged {
        return ref MemoryMarshal.AsRef<T>(span[start..]);
    }

    public static ReadOnlySpan<T> Cast<T>(Memory<byte> data, int start, int count) where T : unmanaged {
        return MemoryMarshal.Cast<byte, T>((ReadOnlySpan<byte>)data.Span[start..(start + Unsafe.SizeOf<T>() * count)]);
    }
}
