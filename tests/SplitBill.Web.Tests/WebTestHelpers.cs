using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SplitBill.Web.Tests;

/// <summary>Dựng HttpContext/PageContext tối giản để test PageModel mà không cần chạy cả host.</summary>
public static class WebTestHelpers
{
    public static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddDataProtection();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        return httpContext;
    }

    public static void AttachPageContext(PageModel pageModel, DefaultHttpContext httpContext)
    {
        var pageContext = new PageContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
        };
        pageModel.PageContext = pageContext;
        pageModel.TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider());
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _data = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _data = values;
    }
}
