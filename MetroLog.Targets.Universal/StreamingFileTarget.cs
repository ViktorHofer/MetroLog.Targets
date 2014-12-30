using MetroLog.Layouts;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace MetroLog.Targets
{
    public class StreamingFileTarget : FileTargetBase
    {
        private readonly IStorageFolder _appFolder;
        private readonly string _dirName;
        private IStorageFolder _folder;

        public StreamingFileTarget(IStorageFolder appFolder, string dirName = "MetroLogs", Layout layout = null)
            : base(layout ?? new SingleLineLayout())
        {
            _appFolder = appFolder;
            _dirName = dirName;
        }

        protected override async Task<Stream> GetCompressedLogsInternal()
        {
            var ms = new MemoryStream();

            using (var a = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var file in await _folder.GetFilesAsync())
                {
                    var zipFile = a.CreateEntry(file.Name);
                    using (var writer = new StreamWriter(zipFile.Open()))
                    {
                        await writer.WriteAsync(await FileIO.ReadTextAsync(file));
                    }
                }
            }

            ms.Position = 0;
            return ms;
        }

        protected override async Task EnsureInitialized()
        {
            // Skip if folder already existing
            if (_folder != null) return;

            _folder = await _appFolder.CreateFolderAsync(_dirName, CreationCollisionOption.OpenIfExists);
        }

        protected sealed override async Task<LogWriteOperation> DoWriteAsync(string fileName, string contents, LogEventInfo entry)
        {
            var file = await _folder.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            await FileIO.AppendTextAsync(file, contents + Environment.NewLine);
            return new LogWriteOperation(this, entry, true);
        }

        sealed protected override async Task DoCleanup(Regex pattern, DateTime threshold)
        {
            await EnsureInitialized();

            foreach (var file in await _folder.GetFilesAsync())
            {
                // Skip if wrong pattern, filename not valid or treshold date not reached
                if (!pattern.Match(file.Name).Success ||
                    !Regex.Match(file.Name, @"[0-9]{8}").Success ||
                    file.DateCreated.DateTime >= threshold) continue;

                try
                {
                    await file.DeleteAsync();
                }
                catch (Exception ex)
                {
                    InternalLogger.Current.Warn(string.Format("Failed to delete '{0}'.", file.Name), ex);
                }
            }
        }
    }
}
