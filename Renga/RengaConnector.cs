using System.Runtime.InteropServices;
using Renga;

namespace RengaMcpServer.Renga;

public sealed class RengaConnector : IDisposable
{
    private const string ProgId = "Renga.Application.1";
    private IApplication? _application;
    private bool _disposed;

    public RengaConnector()
    {
        Visible = !string.Equals(
            Environment.GetEnvironmentVariable("RENGA_MCP_VISIBLE"),
            "0",
            StringComparison.OrdinalIgnoreCase);
    }

    public bool Visible { get; }

    public bool IsConnected => _application is not null;

    public IApplication Connect()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RengaConnector));
        }

        if (_application is not null)
        {
            return _application;
        }

        _application = ConnectCore();
        _application.Visible = Visible;
        Log($"Connected to {_application.ProductName} {_application.VersionS}");
        return _application;
    }

    public IProject RequireProject()
    {
        var app = Connect();
        if (!app.HasProject())
        {
            throw new RengaException("No project is open. Call renga_new_project first.");
        }

        return app.Project;
    }

    public void NewProject()
    {
        var app = Connect();
        if (app.HasProject())
        {
            Check(app.CloseProject(true), "CloseProject");
        }

        Check(app.CreateProject(), "CreateProject");
        app.Visible = Visible;
        Log("Created a new Renga project");
    }

    public string LastError
    {
        get
        {
            try
            {
                return _application is not null && _application.HasLastError
                    ? _application.LastError
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public void Check(int result, string operation)
    {
        if (result == 0)
        {
            return;
        }

        var details = LastError;
        throw new RengaException(
            string.IsNullOrWhiteSpace(details)
                ? $"{operation} failed with code {result}."
                : $"{operation} failed with code {result}. Renga: {details}");
    }

    private static IApplication ConnectCore()
    {
        object? instance = null;
        try
        {
            var clsid = NativeMethods.ClsidFromProgId(ProgId);
            instance = NativeMethods.GetActiveObject(clsid);
            Log("Connected to a running Renga instance through the ROT");
        }
        catch (COMException)
        {
            Log("No running Renga instance was found in the ROT");
        }

        if (instance is null)
        {
            var type = Type.GetTypeFromProgID(ProgId, throwOnError: true)
                ?? throw new RengaException("Renga.Application.1 is not registered.");
            instance = Activator.CreateInstance(type)
                ?? throw new RengaException("Could not create the Renga COM application object.");
            Log("Started a new Renga instance");
        }

        return instance as IApplication
            ?? throw new RengaException("The Renga COM object does not expose IApplication.");
    }

    private static void Log(string message) =>
        Console.Error.WriteLine($"[renga-mcp] {DateTime.Now:HH:mm:ss} {message}");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_application is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_application);
            }
            catch
            {
                // ignored
            }

            _application = null;
        }
    }

    private static class NativeMethods
    {
        [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void CLSIDFromProgID(string progId, out Guid clsid);

        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(
            ref Guid rclsid,
            IntPtr reserved,
            [MarshalAs(UnmanagedType.Interface)] out object ppunk);

        public static Guid ClsidFromProgId(string progId)
        {
            CLSIDFromProgID(progId, out var clsid);
            return clsid;
        }

        public static object GetActiveObject(Guid clsid)
        {
            GetActiveObject(ref clsid, IntPtr.Zero, out var instance);
            return instance;
        }
    }
}

public sealed class RengaException : Exception
{
    public RengaException(string message) : base(message)
    {
    }

    public RengaException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
