using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace ShoppingSysem.Components
{
    public partial class App
    {
        [CascadingParameter]
        private HttpContext HttpContext { get; set; } = default!;

        private IComponentRenderMode? PageRenderMode =>
            HttpContext.AcceptsInteractiveRouting() ? RenderMode.InteractiveServer : null;
    }
}
