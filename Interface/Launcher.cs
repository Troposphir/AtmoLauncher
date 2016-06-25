using ByteSizeLib;
using Gtk;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UpdateLib;

namespace Interface
{
    internal class Launcher {
        private const string ProgressFormat = "{1} of {2} downloaded";

        private readonly LauncherSetup _setup;
        private readonly Builder _builder;

        public Window Window;
        public ProgressBar ProgressBar;
        public Button PlayButton;
        public TextView PatchNotes;
        public Image HeaderImage;

        public Launcher(LauncherSetup setup, Builder builder) {
            _setup = setup;
            _builder = builder;
        }

        public void Initialize() {
            Window = (Window) _builder.GetObject("LauncherWindow");
            Window.Title = _setup.Title;
            Window.Hidden += (sender, eventArgs) => Application.Quit();
            Window.Show();
            PatchNotes = (TextView)_builder.GetObject("PatchNotes");
            ProgressBar = (ProgressBar) _builder.GetObject("ProgressBar");
            PlayButton = (Button) _builder.GetObject("PlayButton");
            PlayButton.Clicked += (sender, args) => {
                Program.StartGame(_setup);
            };

            HeaderImage = (Image)_builder.GetObject("HeaderImage");
            var headerLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LaunchHeader.png");
            if (File.Exists(headerLocation))
                HeaderImage.Pixbuf = new Gdk.Pixbuf(headerLocation);

            var changeLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog.txt");
            string patchNotesText = "You're using a BETA version of our custom launcher. Please report all issues on the forum at http://onemoreblock.com/.";

            if (File.Exists(changeLogFile))
                patchNotesText += "\n\n" + File.ReadAllText(changeLogFile);

            PatchNotes.Buffer.Text = patchNotesText;

            Task.Run(() => CheckAndUpdate());
        }

        private async void CheckAndUpdate() {
            await UpdateProject(new UpdaterClient(_setup.RemoteEndpoint, _setup.LauncherProject), "");
            await UpdateProject(new UpdaterClient(_setup.RemoteEndpoint, _setup.GameProject), _setup.GameFolder);
        }

        private async Task UpdateProject(UpdaterClient updater, string projectRoot) {
            var targetPath = "";

            if (Program.IsUnix)
                targetPath = Path.Combine(Directory.GetParent(Assembly.GetEntryAssembly().Location).Parent.Parent.ToString(), projectRoot);  
            else
                targetPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), projectRoot);

            Application.Invoke((sender, args) => {
                PlayButton.Sensitive = false;
                PlayButton.Label = "Updating";
                ProgressBar.Text = "Checking for updates";
            });
            
            try {
                var cache = ChangeCache.FromFile(Path.Combine(targetPath, "version.json"));
                var version = await updater.FindLatestVersion();
                Console.WriteLine("Local version: {0}, Latest version: {1}", cache.Version, version);
                if (cache.Version >= version) {
                    Console.WriteLine("No updates available.");

                    Application.Invoke((sender, args)=> {
                        PlayButton.Sensitive = true;
                        PlayButton.Label = "Play";
                        ProgressBar.Text = "No updates available";
                    });
                    
                    return;
                }

                Application.Invoke((sender, args)=> {
                    ProgressBar.Text = "Getting version " + version + " from server...";
                    PlayButton.Sensitive = false;
                    PlayButton.Label = "Updating";
                });
                
                var changes = await updater.GetChanges(cache.Version, version);

                Application.Invoke((sender, args) =>
                {
                    ProgressBar.Text = "Preparing to update...";
                });               

                string progressFile = Path.Combine(targetPath, "updateProgress.json");

                if (!Directory.Exists(targetPath))
                    Directory.CreateDirectory(targetPath);

                string curProgress = "";

                if (File.Exists(progressFile))
                    curProgress = File.ReadAllText(progressFile);

                if (curProgress == "")
                    curProgress = "{}";

                UpdateProgress progress = JsonConvert.DeserializeObject<UpdateProgress>(curProgress);

                if (progress == null) {
                    progress = new UpdateProgress();
                    progress.setVersion(version);
                }

                if (progress.Downloaded == 0)
                    progress.setVersion(version);

                if (progress.TargetVersion != version) {
                    UpdateLib.Version oldV = progress.TargetVersion;
                    Application.Invoke((sender, args) => {
                        var dialog = new MessageDialog(Window, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok,
                            false, "Your previous download progress was for v{0}, but the target version is v{1}. As a result, your download progress was reset.",
                            oldV, version) { Title = "Progress Version Mismatch" };

                        dialog.Run();
                        dialog.Destroy();
                    });

                    progress.setVersion(version);
                    progress.DownloadedFiles = new List<string>();
                }
                    
                List<KeyValuePair<string, int>> changesLeft = changes.NewSizes.Where(c => !progress.DownloadedFiles.Contains(c.Key)).ToList();

                var totalSize = ByteSize.FromBytes(changesLeft.Sum(kvp => kvp.Value));
                long currentDownloaded = 0;
                foreach (var change in changesLeft) {
                    var relativePath = change.Key;

                    if(Program.IsUnix)
                        relativePath = relativePath.Replace('\\','/');

                    var targetFile = Path.Combine(targetPath, relativePath);

                    if (File.Exists(targetFile))
                        File.Delete(targetFile);

                    await updater.Download(relativePath, targetFile, version);

                    currentDownloaded += change.Value;

                    Application.Invoke((_, args) => {
                        UpdateDownloadProgress(relativePath, ByteSize.FromBytes(currentDownloaded), totalSize);
                    });

                    progress.DownloadedFiles.Add(change.Key);
                    File.WriteAllText(progressFile, JsonConvert.SerializeObject(progress));
                }
                cache.SetVersion(version);

                if (File.Exists(progressFile))
                    File.Delete(progressFile);

                Application.Invoke((sender, args) => {
                    PlayButton.Sensitive = true;
                    PlayButton.Label = "Play";
                    ProgressBar.Text = "Finished Updating";
                });

                if (Program.IsUnix) {
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
            catch (Exception e) {
                if (e is System.Net.WebException || e is System.Net.Http.HttpRequestException || e is System.Net.Sockets.SocketException) {
                    Application.Invoke((sender, args) => {
                        PlayButton.Sensitive = true;
                        PlayButton.Label = "Play";
                        ProgressBar.Text = "Couldn't connect to update server.";
                    });
                }
                else {
                    Application.Invoke((sender, args) => {
                        Console.WriteLine(e);
                        var dialog = new MessageDialog(Window, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok,
                                                        false, e.GetType() + "An error ocurred, please report this at {0}:\n{1}", _setup.SupportSite, e)
                        {
                            Title = "Update error"
                        };
                        dialog.Run();
                        dialog.Destroy();
                    });
                }
            }
        }

        private void UpdateDownloadProgress(string fileName, ByteSize current, ByteSize total) {
            ProgressBar.Fraction = current.Bytes / total.Bytes;
            ProgressBar.Text = string.Format(ProgressFormat, fileName, current, total);
        }
        
    }
}