using Strata.Core.Errors;
using Strata.Core.Model;

namespace Strata.Core.Ingestion;

/// <summary>
/// Default <see cref="IBinaryLoader"/> — composes format detection, ELF parsing, string extraction, and
/// packing detection into a <see cref="ScanTarget"/> (FR-001..005).
/// </summary>
public sealed class BinaryLoader : IBinaryLoader
{
    public ScanTarget Load(Stream binary, string name, LoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(binary);
        ArgumentNullException.ThrowIfNull(options);

        byte[] data = ReadAll(binary, options.MaxInputBytes);

        BinaryFormat format = FormatDetector.DetectFormat(data);
        if (format == BinaryFormat.Unknown)
        {
            throw new UnsupportedFormatException(
                $"'{name}' is not a recognised native binary (ELF/PE/Mach-O). Refusing to guess (FR-004).");
        }

        var arch = Architecture.Unknown;
        var linkage = Linkage.Unknown;
        var sections = (IReadOnlyList<Section>)[];
        var entryPoints = (IReadOnlyList<ulong>)[];

        switch (format)
        {
            case BinaryFormat.Elf:
                {
                    ElfReader.ElfHeader header = ElfReader.ReadHeader(data);
                    arch = header.Architecture;
                    sections = ElfReader.ReadSections(data, header);
                    entryPoints = header.Entry != 0 ? [header.Entry] : [];
                    // A shared object or a .dynamic section implies dynamic linking; otherwise treat as static.
                    linkage = header.IsSharedObject || HasSection(sections, ".dynamic") ? Linkage.Dynamic : Linkage.Static;
                    break;
                }

            case BinaryFormat.Pe:
                {
                    PeReader.PeInfo pe = PeReader.Read(data);
                    arch = pe.Architecture;
                    sections = pe.Sections;
                    entryPoints = pe.Entry != 0 ? [pe.Entry] : [];
                    linkage = Linkage.Dynamic; // PE images resolve imports via the loader
                    break;
                }

            case BinaryFormat.MachO:
                {
                    MachOReader.MachOInfo macho = MachOReader.Read(data);
                    arch = macho.Architecture;
                    sections = macho.Sections;
                    linkage = Linkage.Dynamic;
                    break;
                }

            default:
                break;
        }

        IReadOnlyList<StringLiteral> strings = StringConstantExtractor.Extract(data, options.MinStringLength);
        PackingStatus packing = PackingDetector.Detect(data);

        return new ScanTarget
        {
            Name = name,
            SizeBytes = data.LongLength,
            Format = format,
            Architecture = arch,
            Linkage = linkage,
            PackingStatus = packing,
            Sections = sections,
            EntryPoints = entryPoints,
            Symbols = [], // symbol-table parsing lands with function recovery (T035); stripped is the default
            Strings = strings,
            Constants = [],
            Image = data,
        };
    }

    private static bool HasSection(IReadOnlyList<Section> sections, string name)
    {
        foreach (Section s in sections)
        {
            if (s.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] ReadAll(Stream stream, long maxBytes)
    {
        if (maxBytes > 0 && stream.CanSeek && stream.Length > maxBytes)
        {
            throw new OutOfEnvelopeException(
                $"Input is {stream.Length} bytes, exceeding the configured limit of {maxBytes}. Reported, not processed.");
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        if (maxBytes > 0 && ms.Length > maxBytes)
        {
            throw new OutOfEnvelopeException(
                $"Input is {ms.Length} bytes, exceeding the configured limit of {maxBytes}. Reported, not processed.");
        }

        return ms.ToArray();
    }
}
