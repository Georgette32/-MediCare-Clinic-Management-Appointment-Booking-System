using Microsoft.AspNetCore.Mvc.Filters;

namespace MediCare.Web.Filters;

/// <summary>Stops browsers and proxies from caching any page that shows medical content.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MedicalNoStoreAttribute : ActionFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        headers["Pragma"] = "no-cache";
        headers["Expires"] = "0";
        base.OnResultExecuting(context);
    }
}
