using System;
using System.Diagnostics;
using System.Text;

// Запускает Python-скрипты проекта и передаёт им JSON через stdin.
public class PythonProcessService
{
    private readonly AppPaths _paths;

    public PythonProcessService(AppPaths paths)
    {
        _paths = paths;
    }

    // Запускает Python-файл, передаёт входной JSON и возвращает stdout/stderr.
    public ProcessRunResult RunScript(string scriptPath, string inputJson)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = _paths.PythonPath,
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using (Process? process = Process.Start(startInfo))
        {
            if (process == null)
            {
                return new ProcessRunResult
                {
                    Output = "",
                    Error = $"Не удалось запустить Python-скрипт: {scriptPath}",
                    ExitCode = -1
                };
            }

            process.StandardInput.WriteLine(inputJson);
            process.StandardInput.Flush();
            process.StandardInput.Close();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            return new ProcessRunResult
            {
                Output = output,
                Error = error,
                ExitCode = process.ExitCode
            };
        }
    }
}
