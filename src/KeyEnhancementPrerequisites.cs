using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PBZKeyForge
{
    internal static class KeyEnhancementPrerequisites
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", ExactSpelling = true)]
        static extern IntPtr GetProcAddressOrdinal(IntPtr module, IntPtr ordinal);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetThreadErrorMode(uint mode, out uint oldMode);

        internal static void EnsureAvailable(string gameBinaryDirectory, params byte[][] runtimeLibraries)
        {
            var streams = runtimeLibraries.Select(bytes => (Stream)new MemoryStream(bytes, false)).ToArray();
            try { EnsureAvailable(gameBinaryDirectory, streams); }
            finally { foreach (var stream in streams) stream.Dispose(); }
        }

        internal static void EnsureAvailable(string gameBinaryDirectory, params Stream[] runtimeLibraries)
        {
            if (IntPtr.Size != 8) throw new IOException(I18n.Str("ue4ss.requires64"));
            var imports = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Stream library in runtimeLibraries) ReadImports(library, imports);
            var failures = new List<string>();
            var modules = new List<IntPtr>();
            uint previousMode;
            bool changedMode = SetThreadErrorMode(0x0001 | 0x8000, out previousMode);
            try
            {
                foreach (string name in imports.Keys.OrderBy(name => name.StartsWith("VCRUNTIME", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    // The game searches beside PBZ.exe before the system runtime.
                    string local = Path.Combine(gameBinaryDirectory, name);
                    string path = File.Exists(local) ? local : Path.Combine(Environment.SystemDirectory, name);
                    // Load only CRT libraries, never the game, proxy or UE4SS.
                    IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00000800);
                    if (module == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        failures.Add(path + " (" + error + ": " + new Win32Exception(error).Message + ")");
                    }
                    else
                    {
                        modules.Add(module);
                        foreach (string symbol in imports[name])
                        {
                            IntPtr address = symbol.StartsWith("#", StringComparison.Ordinal)
                                ? GetProcAddressOrdinal(module, new IntPtr(int.Parse(symbol.Substring(1)))) : GetProcAddress(module, symbol);
                            if (address == IntPtr.Zero) failures.Add(path + ": " + symbol);
                        }
                    }
                }
            }
            finally
            {
                for (int i = modules.Count - 1; i >= 0; i--) FreeLibrary(modules[i]);
                uint ignored;
                if (changedMode) SetThreadErrorMode(previousMode, out ignored);
            }
            if (failures.Count != 0)
                throw new IOException(I18n.Str("ue4ss.vcredist") + Environment.NewLine
                    + "https://aka.ms/vc14/vc_redist.x64.exe" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        // Inspect imports without executing UE4SS or its proxy in the editor process.
        static void ReadImports(Stream stream, Dictionary<string, HashSet<string>> imports)
        {
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw InvalidLibrary();
                stream.Seek(60, SeekOrigin.Begin);
                int pe = reader.ReadInt32();
                if (pe < 64 || pe > stream.Length - 24) throw InvalidLibrary();
                stream.Seek(pe, SeekOrigin.Begin);
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw InvalidLibrary();
                int sections = reader.ReadUInt16();
                stream.Seek(pe + 20, SeekOrigin.Begin);
                int optionalSize = reader.ReadUInt16(), optional = pe + 24;
                if (optionalSize < 128 || optional > stream.Length - optionalSize) throw InvalidLibrary();
                stream.Seek(optional, SeekOrigin.Begin);
                byte[] header = reader.ReadBytes(optionalSize);
                if (BitConverter.ToUInt16(header, 0) != 0x20b)
                    throw InvalidLibrary();
                int sectionTable = optional + optionalSize;
                if (sections > (stream.Length - sectionTable) / 40) throw InvalidLibrary();
                byte[] sectionHeaders = reader.ReadBytes(sections * 40);
                Func<uint, long> offset = rva => {
                    for (int i = 0; i < sections; i++)
                    {
                        int section = i * 40;
                        uint start = BitConverter.ToUInt32(sectionHeaders, section + 12), size = BitConverter.ToUInt32(sectionHeaders, section + 16);
                        if (rva < start || rva - start >= size) continue;
                        long position = (long)BitConverter.ToUInt32(sectionHeaders, section + 20) + rva - start;
                        if (position >= stream.Length) throw InvalidLibrary();
                        return position;
                    }
                    throw InvalidLibrary();
                };
                uint directory = BitConverter.ToUInt32(header, 120);
                if (directory == 0) return;
                for (long descriptor = offset(directory); ; descriptor += 20)
                {
                    if (descriptor > stream.Length - 20) throw InvalidLibrary();
                    stream.Seek(descriptor, SeekOrigin.Begin);
                    byte[] entry = reader.ReadBytes(20);
                    uint nameRva = BitConverter.ToUInt32(entry, 12);
                    if (nameRva == 0) break;
                    string name = ReadName(reader, offset(nameRva));
                    if (!name.StartsWith("MSVCP", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("VCRUNTIME", StringComparison.OrdinalIgnoreCase)) continue;
                    HashSet<string> symbols;
                    if (!imports.TryGetValue(name, out symbols)) imports.Add(name, symbols = new HashSet<string>(StringComparer.Ordinal));
                    uint thunk = BitConverter.ToUInt32(entry, 0);
                    if (thunk == 0) thunk = BitConverter.ToUInt32(entry, 16);
                    var values = new List<ulong>();
                    stream.Seek(offset(thunk), SeekOrigin.Begin);
                    while (true)
                    {
                        if (stream.Position > stream.Length - 8) throw InvalidLibrary();
                        ulong value = reader.ReadUInt64();
                        if (value == 0) break;
                        values.Add(value);
                    }
                    foreach (ulong value in values)
                        symbols.Add((value & 0x8000000000000000UL) != 0
                            ? "#" + (value & 0xffff) : ReadName(reader, offset(checked((uint)value)) + 2));
                }
            }
        }

        static string ReadName(BinaryReader reader, long offset)
        {
            var stream = reader.BaseStream;
            if (offset < 0 || offset >= stream.Length) throw InvalidLibrary();
            stream.Seek(offset, SeekOrigin.Begin);
            var text = new StringBuilder();
            while (stream.Position < stream.Length)
            {
                byte value = reader.ReadByte();
                if (value == 0) return text.ToString();
                text.Append((char)value);
            }
            throw InvalidLibrary();
        }

        static InvalidDataException InvalidLibrary()
        { return new InvalidDataException(I18n.Str("ue4ss.invalid")); }
    }
}
