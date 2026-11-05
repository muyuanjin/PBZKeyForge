using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using System.Text.Json;


if (args[0] == "--container")
{
    var versions = new VersionContainer(EGame.GAME_UE5_7);
    using var reader = new IoStoreReader(args[1], versions: versions);
    reader.Mount(StringComparer.OrdinalIgnoreCase);
    Directory.CreateDirectory(args[2]);
    var files = new List<object>(); int fileIndex = 0;
    foreach (var entry in reader.Files)
    {
        byte[] bytes = entry.Value.Read(); string name = (fileIndex++).ToString("D2") + ".uasset";
        File.WriteAllBytes(Path.Combine(args[2], name), bytes);
        files.Add(new { Path = entry.Key, Bytes = bytes.Length, Extracted = name });
    }
    if (reader.ContainerHeader!.PackageIds.Length != reader.Files.Count) throw new InvalidDataException("Container package directory mismatch.");
    using var pak = new PakFileReader(Path.ChangeExtension(args[1], ".pak"), versions);
    pak.Mount(StringComparer.OrdinalIgnoreCase);
    if (pak.Files.Count != 0) throw new InvalidDataException("Companion PAK must be empty.");
    Console.WriteLine(JsonSerializer.Serialize(new { Reader = "CUE4Parse 1.2.2.202611", Files = files, RuntimeVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

var data = File.ReadAllBytes(args[0]);
var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[1]))!;
using var stream = new FByteArchive("class-extension", data, new VersionContainer(EGame.GAME_UE5_7));
using var archive = new NamesArchive(stream, names);
archive.Position = Convert.ToInt32(args[2], 16);
int count = archive.Read<int>();
var result = new List<object>();
string lastName = "";
for (int i = 0; i < count; i++)
{
    long start = archive.Position;
    var type = archive.ReadFName();
    var field = FField.Construct(type);
    field.Deserialize(archive);
    lastName = field.Name.Text;
    result.Add(new { Type = type.Text, Name = field.Name.Text, Bytes = archive.Position - start });
}
if (count != int.Parse(args.Length > 3 ? args[3] : "22")) throw new InvalidDataException();
if (lastName != (args.Length > 4 ? args[4] : "Current Perfect Window Counter_1000")) throw new InvalidDataException("Added property identity changed.");
int memory = archive.Read<int>(), stored = archive.Read<int>();
if (memory < 0 || stored < 0 || archive.Position + stored > archive.Length) throw new InvalidDataException("Script boundary moved incorrectly.");
var instructions = new List<object>();
var functions = new List<object>();
if (memory == 0 && stored == 0)
{
    int entries = archive.Read<int>();
    if (entries < 0 || entries > 1000) throw new InvalidDataException("Invalid class function map.");
    var identities = new HashSet<string>();
    for (int i = 0; i < entries; i++)
    {
        string name = archive.ReadFName().Text; int index = archive.Read<int>();
        if (!identities.Add(name) || index <= 0) throw new InvalidDataException("Invalid class function identity.");
        functions.Add(new { Name = name, Export = index - 1 });
    }
}
if (stored > 0)
{
    using var script = new FKismetArchive("deflect-script", archive.ReadBytes(stored), new DummyPackage(), new VersionContainer(EGame.GAME_UE5_7), names.Select(n => new FNameEntrySerialized(n)).ToArray());
    while (script.Position < script.Length)
    {
        int offset = script.Index;
        var expression = script.ReadExpression();
        instructions.Add(new { Offset = offset, Token = expression.Token.ToString() });
    }
    if (script.Index != memory) throw new InvalidDataException("Independent VM length mismatch.");
}
Console.WriteLine(JsonSerializer.Serialize(new { Reader = "CUE4Parse 1.2.2.202611", Count = count, Properties = result, MemoryBytes = memory, StorageBytes = stored, Instructions = instructions, Functions = functions }, new JsonSerializerOptions { WriteIndented = true }));

sealed class NamesArchive : FAssetArchive
{
    readonly string[] names;
    public NamesArchive(FArchive archive, string[] names) : base(archive, null) { this.names = names; }
    public override FName ReadFName()
    {
        int index = Read<int>(), number = Read<int>();
        return new FName(names[index], index, number);
    }
}

sealed class DummyPackage : CUE4Parse.UE4.Assets.IPackage
{
    public string Name { get; set; } = "DeflectAudit";
    public CUE4Parse.FileProvider.IFileProvider? Provider => null;
    public string? ArchiveName => null;
    public CUE4Parse.MappingsProvider.TypeMappings? Mappings => null;
    public FPackageFileSummary Summary => null!;
    public FNameEntrySerialized[] NameMap => [];
    public int ImportMapLength => 0;
    public int ExportMapLength => 0;
    public Lazy<CUE4Parse.UE4.Assets.Exports.UObject>[] ExportsLazy => [];
    public bool IsFullyLoaded => true;
    public bool CanDeserialize => true;
    public bool HasFlags(EPackageFlags flags) => false;
    public int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => -1;
    public ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => null;
}
