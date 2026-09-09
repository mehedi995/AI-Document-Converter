using Microsoft.AspNetCore.Mvc;

namespace AI.Document.Converter.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    // FR-031: the customer-facing error page shows nothing technical - no
    // stack trace, no database detail, no engine paths.
    [Route("/error")]
    public IActionResult Error() => View();
}
