

using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Text;
using TinyYaz0;


(IMemoryOwner<byte> owner, int size) = YazUtility.Compress(File.ReadAllBytes(@"C:\Users\Sanae\Projects\Switch\v1.2.0.nsp"));
File.WriteAllBytes(@"E:\v1.2.0.nsp.szs", owner.Memory[..size].ToArray());
(owner, size) = YazUtility.Decompress(owner.Memory[..size]);
File.WriteAllBytes(@"E:\v1.2.0.nsp.nsp", owner.Memory[..size].ToArray());
// Console.WriteLine(Encoding.UTF8.GetString(owner.Memory.Span[..size]));
