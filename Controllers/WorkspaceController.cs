using ListenLense.Models;
using ListenLense.Services;
using Microsoft.AspNetCore.Mvc;

namespace ListenLense.Controllers
{
    public class WorkspaceController : Controller
    {
        private readonly WorkspaceService _workspaceService;
        private readonly PollyService _pollyService;

        public WorkspaceController(WorkspaceService workspaceService, PollyService pollyService)
        {
            _workspaceService = workspaceService;
            _pollyService = pollyService;
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
                // Now process with Polly
                await _pollyService.ProcessTextFileAsync(filePath, folderPath);
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
            var progress = _workspaceService.LoadFileProgress(folderPath, file);
            if (progress != null)
            {
                matchingFile.Progress = progress;
            }

            return View(matchingFile);
        }

        // POST: /Workspace/SaveProgress
        [HttpPost]
        public IActionResult SaveProgress([FromBody] ProgressDto data)
        {
            var name = data.WorkspaceName;
            var file = data.File;
            var currentTime = data.CurrentTime;
            var folderPath = _workspaceService.CreateWorkspace(name);
            // load existing progress
            var progress = _workspaceService.LoadFileProgress(folderPath, file);
            progress.LastAudioPosition = currentTime;
            progress.LastOpened = DateTime.Now;

            _workspaceService.SaveFileProgress(folderPath, file, progress);
            return Ok();
        }
    }

    public class ProgressDto
    {
        public string WorkspaceName { get; set; }
        public string File { get; set; }
        public double CurrentTime { get; set; }
    }
}
