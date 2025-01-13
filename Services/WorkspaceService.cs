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
                var fileStateData = LoadFileState(folderPath, baseName);

                wm.Files.Add(
                    new WorkspaceFileModel
                    {
                        FileName = Path.GetFileName(txt),
                        BaseName = baseName,
                        TextPath = txt,
                        AudioPath = mp3,
                        JsonPath = json,
                        FileState = fileStateData
                    }
                );
            }

            // For LastAccessed, you might store it in a “workspace.json” or simply compute from the newest file
            wm.LastAccessed = (DateTime)
                wm
                    .Files.Select(f => f.FileState?.LastOpened ?? DateTime.MinValue)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();

            return wm;
        }

        public FileStateModel LoadFileState(string folderPath, string baseName)
        {
            // We'll have a single “fileState.json” or 1 file per text. Up to you.
            // For demonstration, let's keep a single “fileState.json” in the workspace
            // that maps file “baseName” => progress object.

            var fileStateFile = Path.Combine(folderPath, "fileState.json");
            var dict = new Dictionary<string, FileStateModel>();

            if (File.Exists(fileStateFile))
            {
                var json = File.ReadAllText(fileStateFile);
                dict =
                    JsonSerializer.Deserialize<Dictionary<string, FileStateModel>>(json)
                    ?? new Dictionary<string, FileStateModel>();
            }

            if (!dict.ContainsKey(baseName))
            {
                dict[baseName] = new FileStateModel
                {
                    LastAudioPosition = 0,
                    LastOpened = DateTime.MinValue,
                    Autoscroll = true,
                    DarkMode = false
                };
            }

            return dict[baseName];
        }

        public void SaveFileState(string folderPath, string baseName, FileStateModel fileState)
        {
            var fileStateFile = Path.Combine(folderPath, "fileState.json");
            var dict = new Dictionary<string, FileStateModel>();

            if (File.Exists(fileStateFile))
            {
                var json = File.ReadAllText(fileStateFile);
                dict =
                    JsonSerializer.Deserialize<Dictionary<string, FileStateModel>>(json)
                    ?? new Dictionary<string, FileStateModel>();
            }

            dict[baseName] = fileState;
            File.WriteAllText(
                fileStateFile,
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
