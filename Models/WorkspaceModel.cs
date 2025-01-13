namespace ListenLense.Models
{
    public class WorkspaceModel
    {
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public List<WorkspaceFileModel> Files { get; set; } = new List<WorkspaceFileModel>();
        public DateTime LastAccessed { get; set; }
    }

    public class WorkspaceFileModel
    {
        public string FileName { get; set; } = string.Empty;
        public string BaseName { get; set; } = string.Empty;
        public string TextPath { get; set; } = string.Empty;
        public string AudioPath { get; set; } = string.Empty;
        public string JsonPath { get; set; } = string.Empty;
        public string WorkspaceName { get; set; } = string.Empty;

        public FileStateModel FileState { get; set; } = new FileStateModel();
    }
}
