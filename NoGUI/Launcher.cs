using ByteSizeLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UpdateLib;

namespace NoGUI
{
    class Launcher
    {
        private const string ProgressFormat = "{0} of {1} downloaded";

        private readonly LauncherSetup _setup;
        bool errorOcurred = false;

        public Launcher(LauncherSetup setup)
        {
            _setup = setup;
        }

        public void Initialize()
        {
            var changeLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog.txt");
            string patchNotesText = "You're using a BETA version of our custom launcher. Please report all issues on the forum at http://onemoreblock.com/.";

            if (File.Exists(changeLogFile))
                patchNotesText += "\n\n" + File.ReadAllText(changeLogFile);

            Task updateTask = Task.Run(() => CheckAndUpdate());

            Console.ReadKey();
            
        }

        private async void CheckAndUpdate()
        {
            await UpdateProject(new UpdaterClient(_setup.RemoteEndpoint, _setup.LauncherProject), "");
            await UpdateProject(new UpdaterClient(_setup.RemoteEndpoint, _setup.GameProject), _setup.GameFolder).ContinueWith(t=> {
                if(!errorOcurred)
                    Program.StartGame(_setup);
            });
        }

        private async Task UpdateProject(UpdaterClient updater, string projectRoot)
        {
            var targetPath = "";

            if (Program.IsUnix)
                targetPath = Path.Combine(Directory.GetParent(Assembly.GetEntryAssembly().Location).Parent.Parent.ToString(), projectRoot);
            else
                targetPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), projectRoot);

            Console.WriteLine("Checking for updates...");

            try
            {
                var cache = ChangeCache.FromFile(Path.Combine(targetPath, "version.json"));
                var version = await updater.FindLatestVersion();
                Console.WriteLine("Local version: {0}, Latest version: {1}", cache.Version, version);
                if (cache.Version >= version)
                {
                    Console.WriteLine("No updates available.");
                    return;
                }

                Console.WriteLine("Getting version v{0} from the server...", version);

                var changes = await updater.GetChanges(cache.Version, version);

                Console.WriteLine("Preparing to update...");

                string progressFile = Path.Combine(targetPath, "updateProgress.json");

                if (!Directory.Exists(targetPath))
                    Directory.CreateDirectory(targetPath);

                string curProgress = "";

                if (File.Exists(progressFile))
                    curProgress = File.ReadAllText(progressFile);

                if (curProgress == "")
                    curProgress = "{}";

                UpdateProgress progress = JsonConvert.DeserializeObject<UpdateProgress>(curProgress);

                if (progress == null)
                {
                    progress = new UpdateProgress();
                    progress.setVersion(version);
                }

                if (progress.Downloaded == 0)
                    progress.setVersion(version);

                if (progress.TargetVersion != version)
                {
                    UpdateLib.Version oldV = progress.TargetVersion;
                    Console.WriteLine("NOTICE: Your previous download progress was for v{0}, but the target version is v{1}. As a result, your download progress was reset.", oldV, version);

                    progress.setVersion(version);
                    progress.DownloadedFiles = new List<string>();
                }

                List<KeyValuePair<string, int>> changesLeft = changes.NewSizes.Where(c => !progress.DownloadedFiles.Contains(c.Key)).ToList();

                var totalSize = ByteSize.FromBytes(changesLeft.Sum(kvp => kvp.Value));
                long currentDownloaded = 0;
                foreach (var change in changesLeft)
                {
                    var relativePath = change.Key;

                    if (Program.IsUnix)
                        relativePath = relativePath.Replace('\\', '/');

                    var targetFile = Path.Combine(targetPath, relativePath);

                    if (File.Exists(targetFile))
                        File.Delete(targetFile);

                    await updater.Download(relativePath, targetFile, version);

                    currentDownloaded += change.Value;

                    UpdateDownloadProgress(relativePath, ByteSize.FromBytes(currentDownloaded), totalSize);

                    progress.DownloadedFiles.Add(change.Key);
                    File.WriteAllText(progressFile, JsonConvert.SerializeObject(progress));
                }
                cache.SetVersion(version);

                if (File.Exists(progressFile))
                    File.Delete(progressFile);

                Console.WriteLine("Finished Updating!");

                if (Program.IsUnix)
                {
                    // Update fix for Mac
                    string executeScript = "";

                    if (updater.GetProjectName() == _setup.LauncherProject)
                        executeScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "atmolauncher");
                    else if (updater.GetProjectName() == _setup.GameProject)
                        executeScript = Path.Combine(targetPath, _setup.GameExecutable, "Contents", "MacOS", "Atmosphir");

                    Program.macChangePerm(executeScript);
                }

                if (updater.GetProjectName() == _setup.LauncherProject)
                    Program.RebootOrig();
            }
            catch (Exception e)
            {
                if (e is System.Net.WebException || e is System.Net.Http.HttpRequestException || e is System.Net.Sockets.SocketException)
                {
                    Console.WriteLine("ERROR: Couldn't connect to update server. Please check your internet connection or try again later.");
                    errorOcurred = true;
                }
                else
                {
                    Console.WriteLine("An error ocurred, please report this at {0}:\n{1}", _setup.SupportSite, e);
                    errorOcurred = true;
                }
            }
        }

        private void UpdateDownloadProgress(string fileName, ByteSize current, ByteSize total)
        {
            Console.WriteLine(string.Format(ProgressFormat, current, total));
        }
    }
}
