namespace CommandHandlers;

using System;
using System.IO.Pipelines;
using System.IO.Enumeration;
using System.IO.Compression;
#if NET7_0
using System.Formats.Tar;
#endif
using System.Runtime.InteropServices;

sealed partial class DeployHandler
{
#if NET6_0
    // .NET 6: create a zip archive.
    private static Stream CreateApplicationArchive(string directory, Dictionary<string, string> buildEnvironment)
    {
        // TODO: finetune PipeOptions.
        return ZipStreamFromDirectory(directory, buildEnvironment);

        static Stream ZipStreamFromDirectory(string directory, Dictionary<string, string> buildEnvironment)
        {
            var pipe = new Pipe();

            CreateFromDirectoryAsync(directory, buildEnvironment, pipe.Writer);

            return pipe.Reader.AsStream();

            static async void CreateFromDirectoryAsync(string directory, Dictionary<string, string> buildEnvironment, PipeWriter writer)
            {
                Stream stream = writer.AsStream(leaveOpen: true);
                try
                {
                    await Task.Yield();
                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                    {
                        DirectoryInfo di = new DirectoryInfo(directory);

                        string basePath = di.FullName;

                        foreach (FileSystemInfo file in di.EnumerateFileSystemInfos("*",
                            new EnumerationOptions()
                            {
                                RecurseSubdirectories = true,
                                AttributesToSkip = (FileAttributes)0
                            }))
                        {
                            if (SkipEntry(file.FullName.AsSpan(basePath.Length), file is DirectoryInfo))
                            {
                                continue;
                            }

                            if (file is FileInfo)
                            {
                                // Create entry for file:
                                string entryName = EntryFromPath(file.FullName.AsSpan(basePath.Length));
                                archive.CreateEntryFromFile(file.FullName, entryName);
                            }
                            else
                            {
                                // Entry marking an empty dir:
                                if (file is DirectoryInfo possiblyEmpty && IsDirEmpty(possiblyEmpty))
                                {
                                    // FullName never returns a directory separator character on the end,
                                    // but Zip archives require it to specify an explicit directory:
                                    string entryName = EntryFromPath(file.FullName.AsSpan(basePath.Length), appendPathSeparator: true);
                                    archive.CreateEntry(entryName);
                                }
                            }
                        }
                        WriteBuildEnvironment(archive, directory, buildEnvironment);
                    }
                    writer.Complete();
                }
                catch (Exception ex)
                {
                    writer.Complete(ex);
                }                

                static bool SkipEntry(ReadOnlySpan<char> path, bool isDirectory)
                {
                    do
                    {
                        ReadOnlySpan<char> name = Path.GetFileName(path);
                        if (isDirectory && SkipDirectoryWithName(name))
                        {
                            return true;
                        }
                        path = Path.GetDirectoryName(path);
                        isDirectory = true;
                    } while (!path.IsEmpty);

                    return false;
                }

                static bool IsDirEmpty(DirectoryInfo possiblyEmptyDir)
                {
                    using (IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(possiblyEmptyDir.FullName).GetEnumerator())
                        return !enumerator.MoveNext();
                }

                static void WriteBuildEnvironment(ZipArchive archive, string directory, Dictionary<string, string> buildEnvironment)
                {
                    string entryName = ".s2i/environment";
                    string content = GenerateS2iEnvironmentContent(directory, buildEnvironment);

                    ZipArchiveEntry entry = archive.CreateEntry(entryName);
                    using var stream = entry.Open();
                    stream.Write(Encoding.UTF8.GetBytes(content));
                    stream.Flush();
                }
            }
        }
    }
#else
    // NET 7+: create a tar.gz archive.
    private static Stream CreateApplicationArchive(string directory, Dictionary<string, string> buildEnvironment)
    {
        // TODO: finetune PipeOptions.
        return Compress(TarStreamFromDirectory(directory, buildEnvironment));

        static Stream TarStreamFromDirectory(string directory, Dictionary<string, string> buildEnvironment)
        {
            var pipe = new Pipe();

            TarFileToPipeWriter(directory, buildEnvironment, pipe.Writer);

            return pipe.Reader.AsStream();

            static async void TarFileToPipeWriter(string directory, Dictionary<string, string> buildEnvironment, PipeWriter writer)
            {
                try
                {
                    await CreateFromDirectoryAsync(directory, buildEnvironment, writer.AsStream(leaveOpen: true));
                    writer.Complete();
                }
                catch (Exception ex)
                {
                    writer.Complete(ex);
                }
            }

            static async Task CreateFromDirectoryAsync(string directory, Dictionary<string, string> buildEnvironment, Stream destination)
            {
                TarWriter writer = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: false);
                await using (writer.ConfigureAwait(false))
                {
                    DirectoryInfo di = new(directory);
                    string basePath = di.FullName;

                    foreach (FileSystemInfo file in GetFileSystemEnumerationForCreation(directory))
                    {
                        await writer.WriteEntryAsync(file.FullName, GetEntryNameForFileSystemInfo(file, basePath.Length), cancellationToken: default).ConfigureAwait(false);
                    }

                    await WriteBuildEnvironmentAsync(writer, directory, buildEnvironment);
                }

                static async Task WriteBuildEnvironmentAsync(TarWriter writer, string directory, Dictionary<string, string> buildEnvironment)
                {
                    string entryName = ".s2i/environment";
                    string content = GenerateS2iEnvironmentContent(directory, buildEnvironment);

                    TarEntryType entryType = writer.Format is TarEntryFormat.V7 ? TarEntryType.V7RegularFile : TarEntryType.RegularFile;
                    TarEntry entry = writer.Format switch
                    {
                        TarEntryFormat.V7 => new V7TarEntry(entryType, entryName),
                        TarEntryFormat.Ustar => new UstarTarEntry(entryType, entryName),
                        TarEntryFormat.Pax => new PaxTarEntry(entryType, entryName),
                        TarEntryFormat.Gnu => new GnuTarEntry(entryType, entryName),
                        _ => throw new InvalidDataException(),
                    };
                    entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content.ToString()));

                    await writer.WriteEntryAsync(entry);
                }

                static IEnumerable<FileSystemInfo> GetFileSystemEnumerationForCreation(string directory)
                {
                    return new FileSystemEnumerable<FileSystemInfo>(
                        directory: directory,
                        transform: (ref FileSystemEntry entry) => entry.ToFileSystemInfo(),
                        options: new EnumerationOptions()
                        {
                            RecurseSubdirectories = true,
                            AttributesToSkip = (FileAttributes)0
                        })
                    {
                        ShouldRecursePredicate = ShouldRecurse,
                        ShouldIncludePredicate = ShouldInclude
                    };

                    static bool ShouldRecurse(ref FileSystemEntry entry)
                        => entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0
                           && !SkipDirectoryWithName(entry.FileName);

                    static bool ShouldInclude(ref FileSystemEntry entry)
                        => !entry.IsDirectory ||
                           !SkipDirectoryWithName(entry.FileName);
                }

                static string GetEntryNameForFileSystemInfo(FileSystemInfo file, int basePathLength)
                {
                    bool isDirectory = (file.Attributes & FileAttributes.Directory) != 0;
                    return EntryFromPath(file.FullName.AsSpan(basePathLength), appendPathSeparator: isDirectory);
                }
            }
        }

        static Stream Compress(Stream stream)
        {
            var pipe = new Pipe();

            StreamToPipeWriter(stream, pipe.Writer);

            return pipe.Reader.AsStream();

            static async void StreamToPipeWriter(Stream stream, PipeWriter writer)
            {
                using GZipStream gzStream = new GZipStream(writer.AsStream(leaveOpen: true), CompressionMode.Compress);
                try
                {
                    await stream.CopyToAsync(gzStream);
                    gzStream.Close();
                    writer.Complete();
                }
                catch (Exception ex)
                {
                    try
                    {
                        gzStream.Close();
                    }
                    catch
                    { }
                    writer.Complete(ex);
                }
            }
        }
    }
#endif

    private static bool SkipDirectoryWithName(ReadOnlySpan<char> directoryName)
    {
        return directoryName.SequenceEqual(".git") ||
               directoryName.SequenceEqual("bin") || directoryName.SequenceEqual("Bin") ||
               directoryName.SequenceEqual("obj") || directoryName.SequenceEqual("Obj");
    }

    private static string GenerateS2iEnvironmentContent(string directory, Dictionary<string, string> buildEnvironment)
    {
        StringBuilder content = new();
        string sourceS2iEnvironmentPath = Path.Combine(directory, ".s2i", "environment");
        if (File.Exists(sourceS2iEnvironmentPath))
        {
            content.Append(File.ReadAllText(sourceS2iEnvironmentPath));
            content.AppendLine();
        }
        foreach (var env in buildEnvironment)
        {
            content.AppendLine($"{env.Key}={env.Value}");
        }

        return content.ToString();
    }

    static string EntryFromPath(ReadOnlySpan<char> path, bool appendPathSeparator = false)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return EntryFromPathWindows(path, appendPathSeparator);
        }
        else
        {
            return EntryFromPathUnix(path, appendPathSeparator);
        }
    }

    static string EntryFromPathUnix(ReadOnlySpan<char> path, bool appendPathSeparator = false)
    {
        // Remove leading separators.
        int nonSlash = IndexOfAnyExcept(path, '/');
        if (nonSlash < 0)
        {
            nonSlash = path.Length;
        }
        path = path.Slice(nonSlash);

        // Append a separator if necessary.
        return (path.IsEmpty, appendPathSeparator) switch
        {
            (false, false) => path.ToString(),
            (false, true) => string.Concat(path, "/"),
            (true, false) => string.Empty,
            (true, true) => "/",
        };
    }

    static unsafe string EntryFromPathWindows(ReadOnlySpan<char> path, bool appendPathSeparator = false)
    {
        // Remove leading separators.
        int nonSlash = IndexOfAnyExcept(path, '/', '\\');
        if (nonSlash < 0)
        {
            nonSlash = path.Length;
        }
        path = path.Slice(nonSlash);

        // Replace \ with /, and append a separator if necessary.

        if (path.IsEmpty)
        {
            return appendPathSeparator ?
                "/" :
                string.Empty;
        }

        fixed (char* pathPtr = &MemoryMarshal.GetReference(path))
        {
            return string.Create(appendPathSeparator ? path.Length + 1 : path.Length, (appendPathSeparator, (IntPtr)pathPtr, path.Length), static (dest, state) =>
            {
                ReadOnlySpan<char> path = new ReadOnlySpan<char>((char*)state.Item2, state.Length);
                path.CopyTo(dest);
                if (state.appendPathSeparator)
                {
                    dest[^1] = '/';
                }

                // To ensure tar files remain compatible with Unix, and per the ZIP File Format Specification 4.4.17.1,
                // all slashes should be forward slashes.
                char oldValue = '\\';
                char newValue = '/';
                for (int i = 0; i < dest.Length; ++i)
                {
                    ref char val = ref dest[i];
                    if (oldValue.Equals(val))
                    {
                        val = newValue;
                    }
                }
            });
        }
    }

    static int IndexOfAnyExcept(ReadOnlySpan<char> path, char c1, char c2 = '\0')
    {
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (c != c1 && c != c2)
            {
                return i;
            }
        }
        return -1;
    }
}
