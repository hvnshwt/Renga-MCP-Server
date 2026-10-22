using System.Globalization;
using RengaMcpServer.Mcp;
using RengaMcpServer.Renga;

namespace RengaMcpServer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        if (args.Length > 0 &&
            string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("RengaMcpServer 1.0.0");
            return 0;
        }

        using var connector = new RengaConnector();
        var service = new RengaModelService(connector);
        var server = new McpServer(service);
        server.Run();
        return 0;
    }
}
