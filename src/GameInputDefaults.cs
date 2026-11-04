using System;
using System.IO;

namespace PBZKeyForge
{
    public sealed partial class GameSchemeSettings
    {
        internal static InputDefaultSet InputDefaultsForSave(string inputPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
                return InputDefaultsForExecutable(FindExecutable(dir));
            }
            catch (IOException) { return InputDefaultSet.Legacy; }
            catch (UnauthorizedAccessException) { return InputDefaultSet.Legacy; }
        }

        internal static InputDefaultSet InputDefaultsForExecutable(string executable)
        {
            try
            {
                using (var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess))
                {
                    var sections = ReadSections(stream);
                    long pe = BitConverter.ToUInt32(ReadAt(stream, 60, 4), 0);
                    ulong imageBase = BitConverter.ToUInt64(ReadAt(stream, pe + 48, 8), 0);
                    if (MatchesInputImplementation(stream, sections, imageBase, 0x68bbb8d, 0xc21f208, 0x68cd250,
                        new byte[] { 0x40,0x55,0x57,0x41,0x55,0x48,0x8d,0xac,0x24,0,0xff,0xff,0xff,0x48,0x81,0xec,0,2,0,0 }, 0x488ade0))
                        return InputDefaultSet.Current;
                    if (MatchesInputImplementation(stream, sections, imageBase, 0x688139d, 0xc1ade08, 0x48606a0,
                        new byte[] { 0x4c,0x89,0x44,0x24,0x18,0x48,0x89,0x54,0x24,0x10,0x48,0x89,0x4c,0x24,8 }, 0))
                        return InputDefaultSet.Legacy;
                }
            }
            catch (IOException) { }
            catch (InvalidDataException) { }
            catch (UnauthorizedAccessException) { }
            return InputDefaultSet.Legacy;
        }

        static bool MatchesInputImplementation(FileStream stream, System.Collections.Generic.List<PeSection> sections,
            ulong imageBase, long constructorRva, long vtableRva, long functionRva, byte[] prologue, long baseFunctionRva)
        {
            long raw = MapRva(sections, constructorRva, 10);
            if (raw < 0) return false;
            byte[] constructor = ReadAt(stream, raw, 10);
            if (constructor[0] != 0x48 || constructor[1] != 0x8d || constructor[2] != 5
                || constructor[7] != 0x48 || constructor[8] != 0x89 || constructor[9] != 3
                || constructorRva + 7 + BitConverter.ToInt32(constructor, 3) != vtableRva) return false;
            raw = MapRva(sections, vtableRva + 0x378, 8);
            if (raw < 0 || BitConverter.ToUInt64(ReadAt(stream, raw, 8), 0) != imageBase + (ulong)functionRva) return false;
            raw = MapRva(sections, functionRva, baseFunctionRva == 0 ? prologue.Length : 0x32);
            if (raw < 0) return false;
            byte[] function = ReadAt(stream, raw, baseFunctionRva == 0 ? prologue.Length : 0x32);
            for (int i = 0; i < prologue.Length; i++) if (function[i] != prologue[i]) return false;
            return baseFunctionRva == 0 || function[0x2d] == 0xe8
                && functionRva + 0x32 + BitConverter.ToInt32(function, 0x2e) == baseFunctionRva;
        }
    }
}
