using ListenLense.Models;
using ListenLense.Services;
using Microsoft.AspNetCore.Mvc;

namespace ListenLense.Controllers
{
    public class WorkspaceController : Controller
    {
        private readonly WorkspaceService _workspaceService;
        private readonly GoogleTTSService _googleService;

        public WorkspaceController(
            WorkspaceService workspaceService,
            GoogleTTSService googleService
        )
        {
            _workspaceService = workspaceService;
            _googleService = googleService;
        }

        // GET: /Workspace/Index?name=MyFirstWorkspace
        public IActionResult Index(string name)
        {
            // name = directory name
            var folderPath = _workspaceService.CreateWorkspace(name); // ensures it exists
            var workspace = _workspaceService.LoadWorkspaceModel(folderPath);
            if (workspace == null)
                return NotFound();

            // Update last accessed if desired
            workspace.LastAccessed = DateTime.Now;
            // could store in “workspace.json” or do nothing for now

            return View(workspace); // -> Views/Workspace/Index.cshtml
        }

        [HttpPost]
        public async Task<IActionResult> UploadFile(string name, IFormFile txtFile)
        {
            var folderPath = _workspaceService.CreateWorkspace(name);
            if (txtFile != null && txtFile.Length > 0)
            {
                // Save the .txt in workspace folder
                var filePath = Path.Combine(folderPath, Path.GetFileName(txtFile.FileName));
                using (var fs = System.IO.File.Create(filePath))
                {
                    await txtFile.CopyToAsync(fs);
                }
                await _googleService.ProcessTextFileAsync(filePath, folderPath);
            }

            return RedirectToAction("Index", new { name });
        }

        // GET: /Workspace/Reader?name=MyFirstWorkspace&file=ethics
        // This page displays the audio + highlight text in an HTML
        public IActionResult Reader(string name, string file)
        {
            var folderPath = _workspaceService.CreateWorkspace(name);
            var workspace = _workspaceService.LoadWorkspaceModel(folderPath);
            if (workspace == null)
                return NotFound();

            var matchingFile = workspace.Files.FirstOrDefault(f => f.BaseName == file);
            if (matchingFile == null)
                return NotFound();

            matchingFile.WorkspaceName = name;

            // Update the model to include the last progress
            var progress = _workspaceService.LoadFileState(folderPath, file);
            if (progress != null)
            {
                matchingFile.FileState = progress;
            }

            return View(matchingFile);
        }

        // POST: /Workspace/SaveFileState
        [HttpPost]
        public IActionResult SaveFileState([FromBody] ProgressDto data)
        {
            var name = data.WorkspaceName;
            var file = data.File;
            var currentTime = data.CurrentTime;
            var autoscroll = data.Autoscroll;
            var darkMode = data.DarkMode;
            var folderPath = _workspaceService.CreateWorkspace(name);
            // load existing progress
            var fileState = _workspaceService.LoadFileState(folderPath, file);
            fileState.LastAudioPosition = currentTime;
            fileState.LastOpened = DateTime.Now;
            fileState.Autoscroll = autoscroll;
            fileState.DarkMode = darkMode;

            _workspaceService.SaveFileState(folderPath, file, fileState);
            return Ok();
        }
    }

    public class ProgressDto
    {
        public string WorkspaceName { get; set; }
        public string File { get; set; }
        public double CurrentTime { get; set; }
        public bool Autoscroll { get; set; }
        public bool DarkMode { get; set; }
    }
}
