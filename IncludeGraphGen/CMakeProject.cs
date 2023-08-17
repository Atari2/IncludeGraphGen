using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;

namespace IncludeGraphGen
{
    class CMakeProjectCreationException : Exception
    {
        public CMakeProjectCreationException(string message) : base(message) { }
    }
    internal class CMakeProject
    {
        private const string BoilerPlate =
@"cmake_minimum_required(VERSION 3.14)
project(IncludeGraphGenBoilerplate)
add_subdirectory({0})
get_target_property(MAYBE_SOURCE_FILES {1} SOURCES)
get_target_property(MAYBE_INCLUDE_DIRECTORY {1} INCLUDE_DIRECTORIES)
message(STATUS ""BEGIN SOURCES OUTPUT"")
foreach(fil ${{MAYBE_SOURCE_FILES}})
    message(STATUS ${{fil}})
endforeach()
message(STATUS ""END SOURCES OUTPUT"")
message(STATUS ""BEGIN INCLUDE_DIRECTORIES OUTPUT"")
foreach (dir ${{MAYBE_INCLUDE_DIRECTORY}})
    message(STATUS ${{dir}})
endforeach()
message(STATUS ""END INCLUDE_DIRECTORIES OUTPUT"")
";
        public string OriginalDirectory;
        public string DestinationDir;
        string FormattedBoilerPlate;
        string ProjectName;
        public List<string> Sources;
        public List<string> IncludePaths;
        public CMakeProject()
        {
            OriginalDirectory = "";
            DestinationDir = "";
            FormattedBoilerPlate = "";
            ProjectName = "";
            Sources = new List<string>();
            IncludePaths = new List<string>();
        }

        public async Task Init(string cmakefilepath)
        {
            string content = File.ReadAllText(cmakefilepath);
            var re = new Regex(@"(add_executable\(\s*(\S+))|(add_library\(\s*(\S+))", RegexOptions.Compiled);
            var matches = re.Matches(content);
            if (matches.Count == 0)
                throw new CMakeProjectCreationException("Project declaration not found in CMake file");
            if (matches[0].Groups[2].Success)
                ProjectName = matches[0].Groups[2].Value;
            else
                ProjectName = matches[0].Groups[4].Value;
            var dirName = Path.GetDirectoryName(cmakefilepath);
            if (dirName == null)
                throw new CMakeProjectCreationException("Invalid path");
            string endingDirName = dirName[(dirName.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
            FormattedBoilerPlate = string.Format(BoilerPlate, endingDirName, ProjectName);
            OriginalDirectory = dirName;
            var tmp_dir = Path.Combine(Directory.GetCurrentDirectory(), "_tmp_gen_proj");
            DestinationDir = Path.Combine(tmp_dir, endingDirName);
            if (Directory.Exists(tmp_dir))
                Directory.Delete(tmp_dir, true);
            await CopyDirectory(OriginalDirectory, DestinationDir);
            await CreateCMakeLists();
            await RunCMakeLists();
        }

        async Task RunCMakeLists()
        {
            var dir = Directory.GetParent(DestinationDir);
            if (dir == null)
                throw new CMakeProjectCreationException($"Invalid path {DestinationDir}");
            var cmd = new ProcessStartInfo() {
                WorkingDirectory = dir.FullName,
                FileName = "cmake",
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = "."
            }; 
            var proc = new Process() { StartInfo = cmd };
            var output = new StringBuilder();
            var error = new StringBuilder();
            using (var outputWaitHandle = new AutoResetEvent(false))
            using (var errorWaitHandle = new AutoResetEvent(false))
            {
                proc.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        outputWaitHandle.Set();
                    }
                    else
                    {
                        output.Append(e.Data + '\n');
                    }
                };
                proc.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        errorWaitHandle.Set();
                    }
                    else
                    {
                        error.Append(e.Data + '\n');
                    }
                };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // 2 minutes timeout
                var timeout = 60 * 2 * 1000;

                await proc.WaitForExitAsync();

                if (outputWaitHandle.WaitOne(timeout) && errorWaitHandle.WaitOne(timeout))
                {
                    var status = proc.ExitCode;
                    if (status != 0)
                    {
                        throw new CMakeProjectCreationException($"CMake failed to execute correctly\n{error}");
                    }
                } else
                {
                    throw new CMakeProjectCreationException("CMake process timed out");
                }
            }


            var real_output = output.ToString();

            bool in_source_output = false;
            bool in_include_directories_output = false;

            foreach (var line in real_output.Split('\n'))
            {
                if (line.Contains("END SOURCES OUTPUT"))
                {
                    in_source_output = false;
                }
                if (in_source_output)
                {
                    Sources.Add(line.Split(' ').Last());
                }
                if (line.Contains("BEGIN SOURCES OUTPUT"))
                {
                    in_source_output = true;
                }
                if (line.Contains("END INCLUDE_DIRECTORIES OUTPUT"))
                {
                    in_include_directories_output = false;
                }
                if (in_include_directories_output)
                {
                    IncludePaths.Add(line.Split(' ').Last());
                }
                if (line.Contains("BEGIN INCLUDE_DIRECTORIES OUTPUT"))
                {
                    in_include_directories_output = true;
                }
            }
        }

        async Task CreateCMakeLists()
        {
            var dir = Directory.GetParent(DestinationDir);
            if (dir == null) return;
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "CMakeLists.txt"), FormattedBoilerPlate);
        }
        static async Task CopyFileAsync(FileInfo info, string destinationPath)
        {
            using Stream source = File.OpenRead(info.FullName);
            using Stream destination = File.Create(destinationPath);
            await source.CopyToAsync(destination);
        }

        static async Task CopyDirectory(string original_dir, string destdir)
        {
            var dir = new DirectoryInfo(original_dir);
            if (!dir.Exists)
                return;
            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destdir);
            foreach (var file in dir.GetFiles())
            {
                string targetPath = Path.Combine(destdir, file.Name);
                await CopyFileAsync(file, targetPath);
            }

            foreach (var dirInfo in dirs)
            {
                if (dirInfo.Name[0] != '.')
                {
                    string newDestDir = Path.Combine(destdir, dirInfo.Name);
                    await CopyDirectory(dirInfo.FullName, newDestDir);
                }
            }
            
        }
    }
}
