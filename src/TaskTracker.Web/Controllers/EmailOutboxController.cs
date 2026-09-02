using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize(Roles = "Admin")]
public class EmailOutboxController(IEmailQueue emailQueue) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["ActiveNav"] = "Master";
        return View(await emailQueue.GetRecentAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Replay(long id)
    {
        if (await emailQueue.ReplayAsync(id))
            TempData["Success"] = "Dead-letter message queued for replay.";
        else
            TempData["Error"] = "Only a dead-letter message can be replayed.";
        return RedirectToAction(nameof(Index));
    }
}
