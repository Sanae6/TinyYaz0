using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TinyYaz0;

public class YazUtility {
    public const uint YazMagic = 0x307A6159;

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private struct Header {
        public uint Magic;
        public int Size;
    }

    public static (IMemoryOwner<byte> Memory, int Size) Compress(ReadOnlySpan<byte> srcData, int chunkSize = 2048) {
        unsafe {
            IMemoryOwner<byte> outMemory =
                MemoryPool<byte>.Shared.Rent(srcData.Length + srcData.Length / 8 + Unsafe.SizeOf<Header>());

            MemoryMarshal.Write(outMemory.Memory.Span,
                new Header { Magic = YazMagic, Size = BinaryPrimitives.ReverseEndianness(srcData.Length) });

            int chunkCount = ((srcData.Length + chunkSize) & ~(chunkSize - 1)) / chunkSize;
            int offset = Unsafe.SizeOf<Header>();
            fixed (byte* tmp = srcData) {
                int srcLength = srcData.Length;
                byte* data = tmp;
                IEnumerable<(IMemoryOwner<byte> dest, int usedSize)> outputOwners = Enumerable.Range(0, chunkCount)
                    .AsParallel().Select(chunkIndex => {
                        IMemoryOwner<byte> destOwner = MemoryPool<byte>.Shared.Rent(chunkSize + chunkSize / 8);
                        Span<byte> dest = destOwner.Memory.Span;
                        int usedSize = 0;
                        ReadOnlySpan<byte> src = new ReadOnlySpan<byte>(data + chunkIndex * chunkSize,
                            int.Min(chunkSize, srcLength % chunkSize));
                        int lastGroupLoc = 0;
                        byte groupData = 0;
                        int groupDataShift = -1;
                        for (int i = 0; i < src.Length; i++) {
                            int matchLength = 0;
                            int matchOffset = 0;
                            int searchStart = Math.Max(0, i - 0xFFF),
                                searchEnd = Math.Min(src.Length, i + 0x111);
                            ReadOnlySpan<byte> dictionary = src[searchStart..i];
                            ReadOnlySpan<byte> input = src[i..searchEnd];
                            for (int j = 0; j < input.Length; j++) {
                                int length = 0;
                                while (j + length < dictionary.Length && length < input.Length &&
                                       dictionary[j + length] == input[length]) length++;
                                if (length > matchLength) {
                                    matchLength = length;
                                    matchOffset = searchStart + j;
                                }
                            }

                            if (groupDataShift == -1) {
                                groupData = 0;
                                groupDataShift = 7;
                                lastGroupLoc = usedSize++;
                            }

                            if (matchLength < 3) {
                                groupData |= (byte) (1 << groupDataShift);
                                dest[usedSize++] = src[i];
                            } else {
                                groupData |= 0;
                                matchOffset = Math.Abs(i - matchOffset) - 1;
                                byte nr = (byte) ((matchOffset & 0xF00) >> 8);
                                byte rr = (byte) (matchOffset & 0xFF);
                                i += matchLength - 1;


                                if (matchLength < 0x12) {
                                    nr |= (byte) (matchLength - 2 << 4);
                                    dest[usedSize++] = nr;
                                    dest[usedSize++] = rr;
                                } else {
                                    dest[usedSize++] = (byte) (nr & 0xF);
                                    dest[usedSize++] = rr;
                                    dest[usedSize++] = (byte) (matchLength - 0x12);
                                }
                            }

                            groupDataShift--;
                            if (groupDataShift == -1)
                                dest[lastGroupLoc] = groupData;
                        }

                        if (groupDataShift != -1)
                            dest[lastGroupLoc] = groupData;
                        return (destOwner, usedSize);
                    });

                foreach ((IMemoryOwner<byte> dest, int usedSize) in outputOwners) {
                    using IMemoryOwner<byte> output = dest;
                    output.Memory.Span[..usedSize].CopyTo(outMemory.Memory.Span[offset..(offset += usedSize)]);
                }
            }

            return (outMemory, offset);
        }
    }

    public static (IMemoryOwner<byte> Memory, int Size) Decompress(Memory<byte> srcBytes) {
        ref Header header = ref ReadUtility.Read<Header>(srcBytes, 0);

        if (header.Magic != YazMagic) throw new Exception("Buffer is not a Yaz0 compressed archive");

        header.Size = BinaryPrimitives.ReverseEndianness(header.Size);

        ReadOnlySpan<byte> srcData = srcBytes.Span[16..];
        IMemoryOwner<byte> destDataOwner = MemoryPool<byte>.Shared.Rent(header.Size);
        Span<byte> destData = destDataOwner.Memory.Span[..header.Size];
        int srcOffset = 0;
        int destOffset = 0;

        {
            byte groupHead = 0;
            int groupHeadLen = 0;
            while (srcOffset < srcData.Length && destOffset < destData.Length) {
                if (groupHeadLen == 0) {
                    groupHead = srcData[srcOffset++];
                    groupHeadLen = 8;
                }

                groupHeadLen--;

                if ((groupHead & 0x80) != 0) {
                    destData[destOffset++] = srcData[srcOffset++];
                } else {
                    byte b1 = srcData[srcOffset++];
                    byte b2 = srcData[srcOffset++];
                    int offset = (((b1 & 0xF) << 8) | b2) + 1;
                    int len = b1 >> 4;
                    if (len == 0) {
                        len = srcData[srcOffset++] + 0x12;
                    } else {
                        len = (len & 0xF) + 2;
                    }

                    Debug.Assert(len is >= 3 and <= 0x111);
                    if (offset < 0 || destOffset + len > destData.Length)
                        throw new Exception("Corrupted data!\n");

                    for (int n = 0; n < len; ++n) {
                        int copyOffset = destOffset - offset;
                        if (copyOffset < 0)
                            copyOffset = 0;

                        destData[destOffset++] = (destData[copyOffset]);
                    }
                }

                groupHead <<= 1;
            }
        }

        return (destDataOwner, destData.Length);
    }
}
