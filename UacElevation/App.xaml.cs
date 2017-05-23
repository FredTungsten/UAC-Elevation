using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace UacElevation
{
    public partial class App : Application
    {
        public App()
        {

#if DEBUGELEVATED
            if (IsElevated())
            {
                if (Debugger.IsAttached)
                    Debugger.Break();
                else
                    Debugger.Launch();
            }
#endif

            string exeName = GetParameter("appname");
            string guidString = GetParameter("guid");

            if (string.IsNullOrWhiteSpace(exeName))
                exeName = Path.GetFileName(PermissionChecker.GetExe());

            Guid guid;

            if (!string.IsNullOrWhiteSpace(guidString))
                guid = Guid.Parse(guidString);
            else
                guid = Guid.NewGuid();


        Console.WriteLine(@"Is Admin: " + UserIsAdmin());
            
            var result = PermissionChecker.EnsureAppPermissionsSet(exeName, guid);

            if (result == PermissionCheckResult.ElevationRequired)
            {
                if (IsElevated() || UserIsAdmin())
                {
                    result = PermissionCheckResult.ElevationInsufficient;
                }
                else
                {
                    result = LaunchCurrentAppAsAdmin(exeName, guid);
                }
            }

#if DEBUG
            if(!IsElevated())
            {
                Console.WriteLine(result);
                Console.ReadKey();
            }
#endif

            Environment.Exit((int)result);
        }

        private string GetParameter(string name)
        {
            string flag = $"/{name}:";

            foreach (string parameter in Environment.GetCommandLineArgs())
            {
                if (parameter.StartsWith(flag))
                    return parameter.Substring(flag.Length).Trim('"');
            }

            return null;
        }

        /// <summary>
        /// Determine if the current process was started with the "/elevated" argument
        /// </summary>
        private bool IsElevated()
        {
            return Environment.GetCommandLineArgs().Contains("/elevated");
        }

        /// <summary>
        /// Determine if the current process was started with admin privileges
        /// </summary>
        /// <returns></returns>
        private bool UserIsAdmin()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Try to launch the program as admin, return result
        /// </summary>
        private PermissionCheckResult LaunchCurrentAppAsAdmin(string exeName, Guid guid)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = PermissionChecker.GetExe(),
                UseShellExecute = true,
                CreateNoWindow = true
            };

            //Required by UAC ;(
            info.UseShellExecute = true;

            // Provides Run as Administrator
            info.Verb = "runas"; 

            // Custom argument so we know we manually elevated the process
            info.Arguments = $"/elevated /appname:\"{exeName}\" /guid:\"{guid:N}\"";

            try
            {
                Process process = new Process { StartInfo = info };
                process.Start();

                int processId = process.Id;

                Debug.WriteLine("UAC Promt Accepted, waiting for exit ...");

               //process.HasExited and process.WaitForExit are not allowed while the process is elevated :(
                WaitForExit(processId);

                if (!process.HasExited)
                    process.WaitForExit();

                return (PermissionCheckResult)process.ExitCode;
            }
            catch(Exception e)
            {
                Debug.WriteLine(e.Message);
                return PermissionCheckResult.ElevationFailed;
            }
        }

        private void WaitForExit(int processId)
        {
            do
            {
                try
                {
                    if (Process.GetProcessById(processId).HasExited)
                        return;
                }
                catch (ArgumentException)
                {
                    // Process doesn't exist (has exited)
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Access denied
                    Thread.Sleep(100);   
                }
            } while (true);
        }
    }
}
