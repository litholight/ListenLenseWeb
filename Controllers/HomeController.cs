using ListenLense.Services;
using Microsoft.AspNetCore.Mvc;

namespace ListenLense.Controllers
{
    public class HomeController : Controller
    {
        private readonly WorkspaceService _workspaceService;

        public HomeController(WorkspaceService workspaceService)
        {
            _workspaceService = workspaceService;
        }

        public IActionResult Index()
        {
            var workspaces = _workspaceService.GetAllWorkspaces();
            return View(workspaces); // pass to Views/Home/Index.cshtml
        }

        [HttpPost]
        public IActionResult CreateWorkspace(string workspaceName)
        {
            _workspaceService.CreateWorkspace(workspaceName);
            return RedirectToAction("Index");
        }
    }
}
