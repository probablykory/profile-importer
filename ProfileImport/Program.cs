using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using ConsoleToolkit;
using ConsoleToolkit.ApplicationStyles;
using ConsoleToolkit.CommandLineInterpretation.ConfigurationAttributes;
using ConsoleToolkit.ConsoleIO;
using System.Diagnostics;
using ConsoleToolkit.ApplicationStyles.Internals;

namespace ProfileImport
{
    [Command]
    [Description("Import an r2modman profile into a Valheim directory.")]
    class Options
    {
        public const string DefaultProfile = "Default";

        [Positional(DefaultValue = DefaultProfile)]
        [Description("The name of the profile to be imported.")]
        public string Name { get; set; }

        [Option("d")]
        [Description("Attempt to import using default values.")]
        public bool UseDefaults { get; set; } = false;

        [Option("r")]
        [Description("Specifies the path of r2modman.  Defaults to %USERPROFILE%\\AppData\\Roaming\\r2modmanPlus-local")]
        public string R2modmanPath { get; set; } = string.Empty;

        [Option("v")]
        
        [Description("Specifies the path of Valheim.  Defaults to current directory or %ProgramFiles%\\Steam\\steamapps\\common\\Valheim")]
        public string ValheimPath { get; set; } = ".";

        [Option("p")]
        [Description(@"Preserves your current Valheim\BepInEx\config directory")]
        public bool PreserveConfigs { get; set; } = false;

        //[Option("save", "s")]
        //[Description("Saves R2ModMan and Valheim paths to a .importer file.")]
        //public bool Save { get; set; } 

        [Option("help", "h", ShortCircuit = true)]
        [Description("Display this help text.")]
        public bool Help { get; set; }

        [CommandHandler]
        public void Handler(IConsoleAdapter console, IErrorAdapter error, string[] args)
        {
            // UseDefaults is actually unused.  Just needed an option to prevent `import.exe` with no args from performing an unintentional import.
            if (args.Length == 0)
            {
                error.WrapLine("No options specified.  Please use -d to attempt import with defaults, or -h for help.");
                Environment.Exit(-1);
            }

            #if DEBUG
            console.WrapLine("R2modmanPath option is set to \"{0}\".", R2modmanPath);
            console.WrapLine("ValheimPath option is set to \"{0}\".", ValheimPath);
            //console.WrapLine("Save option is set to \"{0}\".", Save);
            #endif

            // If no R2ModMan path is set, test various options for a R2ModMan dir.  If found continue, if not, error out.

            string r2path = EnsureValidPath(R2modmanPath, GetDefaultR2ModManLocations, ValidateR2ModManPath);
            if (string.IsNullOrEmpty(r2path))
            {
                error.WrapLine("Error.  Please specify a valid r2modman path with option -r.");
                Environment.Exit(-1);
                return;
            }
            else
            {
#if DEBUG
                console.WrapLine("Found path for R2modman.  " + r2path);
#endif

            }

            // If no Valheim path is set, test various options for a valheim dir.  If found continue, if not, error out.
            string valPath = EnsureValidPath(ValheimPath, GetDefaultValheimLocations, ValidateValheimPath);
            if (string.IsNullOrEmpty(valPath))
            {
                error.WrapLine("Error.  Please specify a valid Valheim path with option -v.");
                Environment.Exit(-1);
                return;
            }
            else
            {
#if DEBUG
                console.WrapLine("Found path for Valheim.  " + valPath);
#endif
            }

            string profilePath = r2path + $"{PathSep}Valheim{PathSep}profiles{PathSep}{Name}";
            if (!ValidateProfilePath(profilePath))
            {
                error.WrapLine("Error, profile path is invalid." + profilePath);
                Environment.Exit(-1);
                return;
            }
            else
            {
#if DEBUG
                console.WrapLine("Profile path validated.  " + profilePath);
#endif
            }

            if (Process.GetProcessesByName("valheim.exe").Length > 0)
            {
                error.WrapLine("Error, Valheim is currently running.  Please exit the game and rerun.");
                Environment.Exit(-1);
                return;
            }

            //debug with empty valPath
            //string valPath = ValheimPath;
            //if (string.IsNullOrEmpty(valPath))
            //{
            //    valPath = ".";
            //}
            //string profilePath = r2path + $"{PathSep}Valheim{PathSep}profiles{PathSep}{Name}";


            // After path validations, the rest of this code essentially executes robocopy like so:
            //robocopy %USERPROFILE%\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default target /xd BepInEx doorstop_libs unstripped_corlib
            //robocopy %USERPROFILE%\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx target\BepInEx /MIR /xd {...exclusions}
            //robocopy %USERPROFILE%\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\doorstop_libs target\doorstop_libs /MIR
            //robocopy %USERPROFILE%\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\unstripped_corlib target\unstripped_corlib /MIR

            Action<int, string> outputResultMsg = (r, path) =>
            {
                if (r > 6)
                {
                    error.WrapLine(getExitMessage(r, path));
                }
                else
                {
                    console.WrapLine(getExitMessage(r, path));
                }
            };

            
            int result = 0;
            result = runProcess("robocopy.exe", $"{profilePath} {valPath} /xd BepInEx doorstop_libs unstripped_corlib");
            if (result > 2) outputResultMsg(result, valPath); // exit code 2 is expected, valheim won't be present in the profile directory
            if (result > 6) Environment.Exit(-1);
            //maxResult = maxResult >= result ? maxResult : result;

            string checkForRemoval = string.Empty;
            string exclusions = string.Empty;
            if (PreserveConfigs)
            {
                exclusions = "config ";
            }

            // Get exclusions the list of disabled mods in mods.yml
            checkForRemoval = GetExclusionList(GetYamlPackages(profilePath + PathSep + "mods.yml"));
            exclusions = exclusions + checkForRemoval;
            if (!string.IsNullOrEmpty(exclusions))
            {
                exclusions = "/xd " + exclusions;
            }
            int maxResult = 0;
            result = runProcess("robocopy.exe", $"{profilePath}{PathSep}BepInEx {valPath}{PathSep}BepInEx /MIR {exclusions}");
            if (result > 1) outputResultMsg(result, $"{valPath}{PathSep}BepInEx");
            if (result > 6) Environment.Exit(-1);
            maxResult = maxResult >= result ? maxResult : result;

            result = runProcess("robocopy.exe", $"{profilePath}{PathSep}doorstop_libs {valPath}{PathSep}doorstop_libs /MIR");
            if (result > 1) outputResultMsg(result, $"{valPath}{PathSep}doorstop_libs");
            if (result > 6) Environment.Exit(-1);
            maxResult = maxResult >= result ? maxResult : result;

            result = runProcess("robocopy.exe", $"{profilePath}{PathSep}unstripped_corlib {valPath}{PathSep}unstripped_corlib /MIR");
            if (result > 1) outputResultMsg(result, $"{valPath}{PathSep}unstripped_corlib");
            if (result > 6) Environment.Exit(-1);
            maxResult = maxResult >= result ? maxResult : result;

            if (!string.IsNullOrEmpty(checkForRemoval))
            {
                foreach(string item in checkForRemoval.Split(' '))
                {
                    string path = $"{valPath}{PathSep}BepInEx{PathSep}plugins{PathSep}{item}";
                    if (Directory.Exists(path))
                    {
                        console.WrapLine($"removing disabled plugin {path}");
                        Directory.Delete(path, true);
                    }
                }
            }

            if (maxResult == 0)
            {
                console.WrapLine($"The files and folders already exist in \"{valPath}\", the import was skipped.");
            }
            else if (maxResult == 1)
            { 
                console.WrapLine($"Imported {Name} profile successfully.");
            }
        }

        private static int runProcess(string name, string args)
        {
            Process process = new Process();
            process.StartInfo.FileName = name;
            process.StartInfo.Arguments = args;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.Start();
            process.WaitForExit();

            //#if DEBUG
            //Console.WriteLine(
            //    $"D: executing            : {name} {args}\n" +
            //    $"D: process exit code    : {process.ExitCode}\n" +
            //    $"D: process elapsed time : {Math.Round((process.ExitTime - process.StartTime).TotalMilliseconds)}");
            //#endif

            return process.ExitCode;
        }

        private static string getExitMessage(int code, string path = "")
        {
            if (string.IsNullOrEmpty(path))
            {
                path = "the destination directory";
            }
            else
            {
                path = $"\"{path}\"";
            }
            switch (code)
            {
                case 0:
                    return $"The files already exist in {path}, the copy operation was skipped.";
                case 1:
                    return "All files were copied successfully.";
                case 2:
                    return $"There are some additional files in {path} that aren't present in the source directory. No files were copied.";
                case 3:
                    return $"Some files were copied. Additional files were present in {path}. No failure was met.";
                case 5:
                    return $"Some files were copied. Some files were mismatched in {path}. No failure was met.";
                case 6:
                    return $"Additional files and mismatched files exist. No files were copied and no failures were met. Which means that the files already exist in {path}.";
                case 7:
                    return $"Files were copied, a file mismatch was present, and additional files were present in ${path}.";
                case 8:
                    return $"Several files didn't copy in {path}";
            }

            return $"At least one failure occurred during the copy operation in {path}";
        }


        public static string PathSep = Path.DirectorySeparatorChar.ToString();

        // get packages from yaml
        public static List<Package> GetYamlPackages(string modsFile)
        {

            return ReadYamlFile<List<Package>>(modsFile);
        }

        // Deserialize yaml
        public static T ReadYamlFile<T>(string filePath)
        {
            using (var input = File.OpenText(filePath))
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var result = deserializer.Deserialize<T>(input);
                return result;
            }
        }

        // transform packages list to a string of disabled plugin names
        public static string GetExclusionList(List<Package> packages)
        {
            string result = "";

            foreach(var package in packages)
            {
                if (package != null && package.Enabled == false)
                {
                    result = result + package.Name + " ";
                }
            }

            return result.TrimEnd(' ');
        }

        // This static method attempts to find a valid path amongst the input or default paths, testing against a validate function.
        public static string EnsureValidPath(string inputPath, Func<List<string>> defaultPaths, Func<string, bool> validate)
        {
            string result = string.Empty;
            List<string> testPaths;

            if (string.IsNullOrEmpty(inputPath))
            {
                testPaths = defaultPaths();
            }
            else
            {
                testPaths = new List<string>() {
                    inputPath
                };
            }
            foreach (string path in testPaths)
            {
                if (validate(path) == true)
                {
                    result = path;
                    break;
                }
            }

            return result;
        }

        // provide a list of common locations for valheim
        public static List<string> GetDefaultValheimLocations()
        {
            var steam = @"Steam\steamapps\common\Valheim";
            var PF = System.Environment.GetEnvironmentVariable("ProgramFiles");
            var PFx86 = System.Environment.GetEnvironmentVariable("ProgramFiles(x86)");

            var results = new List<string>() {
                Environment.CurrentDirectory,
                PF + PathSep + steam,
                PFx86 + PathSep + steam
            };

            return results;
        }

        // provide a list of common locations for r2modman
        public static List<string> GetDefaultR2ModManLocations()
        {
            var UP = System.Environment.GetEnvironmentVariable("USERPROFILE");

            var results = new List<string>() {
                UP + PathSep + @"AppData\Roaming\r2modmanPlus-local"
            };

            return results;
        }

        // check if the input path is valid and contains a valheim game profile subdir.
        public static bool ValidateR2ModManPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!Directory.Exists(path)) return false;

            if (Directory.Exists(path + PathSep + "Valheim") &&
                Directory.EnumerateFileSystemEntries(path + PathSep + "Valheim").Any())
                return true;

            return false;
        }

        // check if the input path is valid and contains a mods file
        public static bool ValidateProfilePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!Directory.Exists(path)) return false;

            if (File.Exists(path + PathSep + "mods.yml"))
                return true;

            return false;
        }

        // check if the input path is valid and contains valheim .exe + data
        public static bool ValidateValheimPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!Directory.Exists(path)) return false;

            if (File.Exists(path + PathSep + "valheim.exe") &&
                Directory.Exists(path + PathSep + "valheim_Data") &&
                Directory.EnumerateFileSystemEntries(path + PathSep + "valheim_Data").Any())
                return true;

            return false;
        }
    }

    // used for parsing mods.yml
    class Package
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
    }

    // main
    internal class Program: ConsoleApplication
    {
        public static string[] Arguments { get; set; }

        static void Main(string[] args)
        {
            Arguments = args;
            Toolkit.Execute<Program>(args);
        }

        protected override void Initialise()
        {
            base.RegisterInjectionInstance<string[]>(Arguments);
            base.HelpOption<Options>(o => o.Help);
            base.Initialise();
        }
    }
}
