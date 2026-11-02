namespace Borealis.Core.Diagnostics;

/// <summary>Simple file log in <c>C:\ProgramData\Borealis\logs</c>, one file per day and program.
/// Einfaches Datei-Log in <c>C:\ProgramData\Borealis\logs</c>, eine Datei pro Tag und Programm.</summary>
public static class Log
{
    static readonly object Gate = new();
    static string program = "borealis";

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Borealis", "logs");

    /// <summary>Also write to the console (service in foreground, CLI).
    /// Auch auf die Konsole schreiben (Dienst im Vordergrund, CLI).</summary>
    public static bool ToConsole { get; set; }

    public static void Init(string programName) => program = programName;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("FEHLER", ex == null ? message : $"{message}: {ex}");

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-6} {message}";
        lock (Gate)
        {
            if (ToConsole) Console.WriteLine(line);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, $"{program}-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
