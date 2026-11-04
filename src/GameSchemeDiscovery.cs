using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PBZKeyForge
{
    internal sealed class SchemeDiscoveryRequiredException : IOException
    {
        readonly string executable;
        readonly long length;
        readonly DateTime modified;

        public SchemeDiscoveryRequiredException(string executable, long length, DateTime modified)
            : base("PBZS reader discovery is required.")
        { this.executable = executable; this.length = length; this.modified = modified; }

        public bool SameExecutableVersion(SchemeDiscoveryRequiredException other)
        {
            return other != null && string.Equals(executable, other.executable, StringComparison.OrdinalIgnoreCase)
                && length == other.length && modified == other.modified;
        }
    }

    public sealed partial class GameSchemeSettings
    {
        // Four RIP-relative loads, two XORs, and both 16-byte stores identify the derivation.
        static readonly byte[] DerivationPattern = {
            0xf3,0x0f,0x6f,0x05,0,0,0,0, 0xf3,0x0f,0x6f,0x0d,0,0,0,0,
            0xf3,0x0f,0x6f,0x15,0,0,0,0, 0x0f,0x57,0xc8,0xf3,0x0f,0x6f,0x05,0,0,0,0,
            0xf3,0x0f,0x7f,0x0a,0x0f,0x57,0xd0,0xf3,0x0f,0x7f,0x52,0x10,0xb0,0x01,0xc3
        };
        static readonly int[] Displacements = { 4, 12, 20, 31 };
        static readonly object LocationLock = new object();
        static readonly Dictionary<string, LocationCache> Locations = new Dictionary<string, LocationCache>(StringComparer.OrdinalIgnoreCase);

        sealed class Location
        {
            public long Function;
            public long[] References;
        }
        sealed class LocationCache
        {
            public long Length;
            public DateTime Modified;
            public Location[] Candidates;
        }
        sealed class ResolvedReader : IDisposable
        {
            public ReaderPath Path;
            public string Build;
            public byte[] Plain, Key;
            public void Dispose() { Array.Clear(Key, 0, Key.Length); }
        }
        sealed class PeSection
        {
            public long Raw, Size, Rva;
            public bool Executable;
        }

        static ResolvedReader Resolve(byte[] encoded, string executablePath, bool allowDiscovery)
        {
            ValidateContainer(encoded);
            string exe = System.IO.Path.GetFullPath(executablePath);
            using (var stream = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess))
            {
                foreach (var known in VerifiedKeyProfiles)
                foreach (var path in known.Paths)
                {
                    byte[] key = ReadKey(stream, path.KeyOffsets);
                    if (key == null) continue;
                    try
                    {
                        string digest;
                        using (var sha = SHA256.Create()) digest = Hex(sha.ComputeHash(key));
                        if (digest != known.KeySha256) continue;
                        var result = Authenticate(encoded, key, path, known.Name + " (verified PBZS reader; exact build unknown)");
                        if (result != null) { key = null; return result; }
                    }
                    finally { if (key != null) Array.Clear(key, 0, key.Length); }
                }

                DateTime modified = File.GetLastWriteTimeUtc(exe);
                LocationCache cached;
                lock (LocationLock) Locations.TryGetValue(exe, out cached);
                List<PeSection> sections = null;
                bool validCache = cached != null && cached.Length == stream.Length && cached.Modified == modified;
                if (validCache)
                {
                    sections = ReadSections(stream);
                    foreach (var candidate in cached.Candidates)
                    {
                        Location current = ReadLocation(stream, sections, candidate.Function);
                        if (current == null || !SameOffsets(candidate.References, current.References)) { validCache = false; break; }
                    }
                }
                if (!validCache)
                {
                    if (cached != null) lock (LocationLock) Locations.Remove(exe);
                    if (!allowDiscovery) throw new SchemeDiscoveryRequiredException(exe, stream.Length, modified);
                    if (sections == null) sections = ReadSections(stream);
                    var found = Discover(stream, sections);
                    if (found.Count == 0)
                        throw new InvalidDataException("Unsupported PBZ executable: no PBZS key derivation was found. Switch the scheme in the game.");
                    cached = new LocationCache { Length = stream.Length, Modified = modified, Candidates = found.ToArray() };
                    lock (LocationLock) Locations[exe] = cached;
                }
                foreach (var candidate in cached.Candidates)
                {
                    // The last pair of machine loads is the reverse of the registered XOR order.
                    var path = new ReaderPath("PBZS v1/1, discovered derivation", Header, 8, 20, 36, candidate.References);
                    byte[] key = ReadKey(stream, path.KeyOffsets);
                    if (key == null) continue;
                    try
                    {
                        var result = Authenticate(encoded, key, path, "Authenticated PBZS derivation (exact build unknown)");
                        if (result != null) { key = null; return result; }
                    }
                    finally { if (key != null) Array.Clear(key, 0, key.Length); }
                }
                throw new InvalidDataException("No PBZS reader authenticated this settings file. The file was not changed.");
            }
        }

        static void ValidateContainer(byte[] encoded)
        {
            if (encoded.Length < 36 || encoded.Length > 1024 * 1024)
                throw new InvalidDataException("Unsupported PBZ settings container size.");
            for (int i = 0; i < Header.Length; i++)
                if (encoded[i] != Header[i]) throw new InvalidDataException("Unsupported PBZS header.");
        }

        static ResolvedReader Authenticate(byte[] encoded, byte[] key, ReaderPath path, string build)
        {
            byte[] decoded;
            try { decoded = Unseal(encoded, path, key); }
            catch (CryptographicException) { return null; }
            // Authenticated content errors are final, even when another function also matches.
            new GameSchemeSettings(null, null, encoded, decoded, build);
            return new ResolvedReader { Path = path, Plain = decoded, Key = key, Build = build };
        }

        static byte[] ReadKey(FileStream stream, long[] offsets)
        {
            var key = new byte[32];
            var part = new byte[16];
            try
            {
                for (int n = 0; n < 4; n++)
                {
                    if (offsets[n] < 0 || offsets[n] > stream.Length - 16) { Array.Clear(key, 0, key.Length); return null; }
                    stream.Position = offsets[n];
                    ReadExactly(stream, part, 16);
                    for (int i = 0; i < 16; i++) key[(n / 2) * 16 + i] ^= part[i];
                }
                return key;
            }
            finally { Array.Clear(part, 0, part.Length); }
        }

        static void ReadExactly(FileStream stream, byte[] bytes, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(bytes, read, count - read);
                if (n == 0) throw new InvalidDataException("Truncated PBZ executable.");
                read += n;
            }
        }
        static byte[] ReadAt(FileStream stream, long offset, int count)
        {
            if (offset < 0 || offset > stream.Length - count) throw new InvalidDataException("Invalid PBZ PE file range.");
            stream.Position = offset;
            byte[] bytes = new byte[count];
            ReadExactly(stream, bytes, count);
            return bytes;
        }
        static List<PeSection> ReadSections(FileStream stream)
        {
            var dos = ReadAt(stream, 0, 64);
            if (dos[0] != 'M' || dos[1] != 'Z') throw new InvalidDataException("PBZ executable is not a PE image.");
            long pe = BitConverter.ToUInt32(dos, 60);
            var coff = ReadAt(stream, pe, 24);
            if (BitConverter.ToUInt32(coff, 0) != 0x4550 || BitConverter.ToUInt16(coff, 4) != 0x8664)
                throw new InvalidDataException("PBZ executable must be an x64 PE image.");
            int count = BitConverter.ToUInt16(coff, 6), optionalSize = BitConverter.ToUInt16(coff, 20);
            if (optionalSize < 2 || BitConverter.ToUInt16(ReadAt(stream, pe + 24, 2), 0) != 0x20b)
                throw new InvalidDataException("Unsupported PBZ PE optional header.");
            var sections = new List<PeSection>();
            for (int i = 0; i < count; i++)
            {
                var bytes = ReadAt(stream, pe + 24 + optionalSize + i * 40L, 40);
                var section = new PeSection { Rva = BitConverter.ToUInt32(bytes, 12), Size = BitConverter.ToUInt32(bytes, 16),
                    Raw = BitConverter.ToUInt32(bytes, 20), Executable = (BitConverter.ToUInt32(bytes, 36) & 0x20000000) != 0 };
                if (section.Raw > stream.Length || section.Size > stream.Length - section.Raw)
                    throw new InvalidDataException("Truncated PBZ PE section.");
                sections.Add(section);
            }
            return sections;
        }
        static long MapRva(List<PeSection> sections, long rva, int size)
        {
            foreach (var section in sections)
                if (rva >= section.Rva && rva - section.Rva <= section.Size - size)
                    return section.Raw + rva - section.Rva;
            return -1;
        }
        static bool Matches(byte[] bytes, int offset)
        {
            for (int i = 0; i < DerivationPattern.Length; i++)
            {
                if ((i >= 4 && i <= 7) || (i >= 12 && i <= 15) || (i >= 20 && i <= 23) || (i >= 31 && i <= 34)) continue;
                if (bytes[offset + i] != DerivationPattern[i]) return false;
            }
            return true;
        }
        static Location ReadLocation(FileStream stream, List<PeSection> sections, long raw)
        {
            foreach (var section in sections)
            {
                if (!section.Executable || raw < section.Raw || raw - section.Raw > section.Size - DerivationPattern.Length) continue;
                var bytes = ReadAt(stream, raw, DerivationPattern.Length);
                if (!Matches(bytes, 0)) return null;
                var offsets = new long[4];
                for (int i = 0; i < offsets.Length; i++)
                {
                    int displacement = Displacements[i];
                    long rva = section.Rva + raw - section.Raw + displacement + 4 + BitConverter.ToInt32(bytes, displacement);
                    offsets[i] = MapRva(sections, rva, 16);
                    if (offsets[i] < 0) return null;
                }
                return new Location { Function = raw, References = offsets };
            }
            return null;
        }
        static bool SameOffsets(long[] a, long[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        static List<Location> Discover(FileStream stream, List<PeSection> sections)
        {
            var found = new List<Location>();
            byte[] block = new byte[65536 + 49];
            foreach (var section in sections)
            {
                if (!section.Executable) continue;
                long position = section.Raw, end = section.Raw + section.Size;
                while (position <= end - DerivationPattern.Length)
                {
                    int count = (int)Math.Min(block.Length, end - position);
                    stream.Position = position;
                    ReadExactly(stream, block, count);
                    int starts = count - DerivationPattern.Length + 1;
                    for (int i = 0; i < starts; i++)
                    {
                        if (!Matches(block, i)) continue;
                        var candidate = ReadLocation(stream, sections, position + i);
                        if (candidate != null) found.Add(candidate);
                    }
                    position += starts;
                }
            }
            return found;
        }
    }
}
