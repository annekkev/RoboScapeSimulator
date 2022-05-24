using System.Text;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.Utilities;
using EmbedIO.WebApi;

namespace RoboScapeSimulator.API;

/// <summary>
/// API module providing information about rooms available on this server 
/// </summary>
public class RoomsModule : WebApiModule
{
    public RoomsModule(string baseRoute) : base(baseRoute)
    {
        AddHandler(HttpVerbs.Get, RouteMatcher.Parse("/hello", false), GetHello);
    }

    private Task GetHello(IHttpContext context, RouteMatch route)
    {
        var queryData = context.GetRequestQueryData();

        if (queryData.ContainsKey("username"))
        {
            return context.SendStringAsync("Hello " + queryData["username"] + "!", "text/plain", Encoding.Default);
        }
        else
        {
            return context.SendStringAsync("Hello world!", "text/plain", Encoding.Default);
        }
    }
}