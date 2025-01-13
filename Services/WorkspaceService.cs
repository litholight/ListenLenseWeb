using System.Text.Json;
using ListenLense.Models;

namespace ListenLense.Services
{
    public class WorkspaceService
    {
        private readonly string _rootPath;

        public WorkspaceService(IHostEnvironment env)
        {
            // e.g. “~/App_Data/Workspaces” in production
            // For simplicity let's store in a subfolder of ContentRoot
            _rootPath = Path.Combine(env.ContentRootPath, "App_Data", "Workspaces");
            Directory.CreateDirectory(_rootPath);
        }

        public List<WorkspaceModel> GetAllWorkspaces()
        {
            var result = new List<WorkspaceModel>();
            var dirs = Directory.GetDirectories(_rootPath);

            foreach (var d in dirs)
            {
                var wm = LoadWorkspaceModel(d);
                if (wm != null)
                {
                    result.Add(wm);
                }
            }
            return result.OrderByDescending(w => w.LastAccessed).ToList();
        }

        public WorkspaceModel? LoadWorkspaceModel(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return null;

            var wm = new WorkspaceModel
            {
                FolderPath = folderPath,
                Name = Path.GetFileName(folderPath),
            };

            // Gather .txt files in the folder
            var txtFiles = Directory.GetFiles(folderPath, "*.txt");
            foreach (var txt in txtFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(txt);
                var mp3 = Path.Combine(folderPath, baseName + ".mp3");
                var json = Path.Combine(folderPath, baseName + ".json");

                // load or create progress
                var progressData = LoadFileProgress(folderPath, baseName);

                wm.Files.Add(
                    new WorkspaceFileModel
                    {
                        FileName = Path.GetFileName(txt),
                        BaseName = baseName,
                        TextPath = txt,
                        AudioPath = mp3,
                        JsonPath = json,
                        Progress = progressData
                    }
                );
            }

            // For LastAccessed, you might store it in a “workspace.json” or simply compute from the newest file
            wm.LastAccessed = (DateTime)
                wm
                    .Files.Select(f => f.Progress?.LastOpened ?? DateTime.MinValue)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();

            return wm;
        }

        public FileProgressModel LoadFileProgress(string folderPath, string baseName)
        {
            // We'll have a single “progress.json” or 1 file per text. Up to you.
            // For demonstration, let's keep a single “progress.json” in the workspace
            // that maps file “baseName” => progress object.

            var progressFile = Path.Combine(folderPath, "progress.json");
            var dict = new Dictionary<string, FileProgressModel>();

            if (File.Exists(progressFile))
            {
                var json = File.ReadAllText(progressFile);
                dict =
                    JsonSerializer.Deserialize<Dictionary<string, FileProgressModel>>(json)
                    ?? new Dictionary<string, FileProgressModel>();
            }

            if (!dict.ContainsKey(baseName))
            {
                dict[baseName] = new FileProgressModel
                {
                    LastAudioPosition = 0,
                    LastOpened = DateTime.MinValue
                };
            }

            return dict[baseName];
        }

        public void SaveFileProgress(string folderPath, string baseName, FileProgressModel progress)
        {
            var progressFile = Path.Combine(folderPath, "progress.json");
            var dict = new Dictionary<string, FileProgressModel>();

            if (File.Exists(progressFile))
            {
                var json = File.ReadAllText(progressFile);
                dict =
                    JsonSerializer.Deserialize<Dictionary<string, FileProgressModel>>(json)
                    ?? new Dictionary<string, FileProgressModel>();
            }

            dict[baseName] = progress;
            File.WriteAllText(
                progressFile,
                JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true })
            );
        }

        public string CreateWorkspace(string workspaceName)
        {
            // e.g. ~/App_Data/Workspaces/MyNewWorkspace
            var folder = Path.Combine(_rootPath, workspaceName);
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
