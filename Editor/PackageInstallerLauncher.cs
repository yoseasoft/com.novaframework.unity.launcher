/// -------------------------------------------------------------------------------
/// NovaEngine Framework
///
/// Copyright (C) 2025 - 2026, Hainan Yuanyou Information Technology Co., Ltd. Guangzhou Branch
///
/// Permission is hereby granted, free of charge, to any person obtaining a copy
/// of this software and associated documentation files (the "Software"), to deal
/// in the Software without restriction, including without limitation the rights
/// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
/// copies of the Software, and to permit persons to whom the Software is
/// furnished to do so, subject to the following conditions:
///
/// The above copyright notice and this permission notice shall be included in
/// all copies or substantial portions of the Software.
///
/// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
/// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
/// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
/// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
/// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
/// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
/// THE SOFTWARE.
/// -------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace NovaFramework.Editor.Launcher
{
    public class PackageInstallerLauncher
    {
        private static readonly Dictionary<string, string> _gitUrlDic = new Dictionary<string, string>()
        {
            {
                "com.novaframework.unity.core.common",
                "https://github.com/yoseasoft/com.novaframework.unity.core.common.git"
            },
            {
                "com.novaframework.unity.installer",
                "https://github.com/yoseasoft/com.novaframework.unity.installer.git"
            },
        };

        private static string _launcherPackageName = "com.novaframework.unity.launcher";

        [InitializeOnLoadMethod]
        static void ExecuteInstallation()
        {
            // 检查Nova.Installer.Editor程序集是否存在
            if (IsAssemblyExists("Nova.Installer.Editor") && IsAssemblyExists("Nova.Common.Editor"))
            {
                
                Debug.Log("Nova.Installer.Editor assembly already exists. Skipping installation.");
                Debug.Log("Nova.Common.Editor assembly already exists. Skipping installation.");
                return; // 如果程序集已存在，则跳过安装
            }
            
            Debug.Log("ExecuteInstallation");

            try
            {
                //创建目录结构
                string projectPath = Path.GetDirectoryName(Application.dataPath);
                string novaFrameworkDataPath = Path.Combine(projectPath, "NovaFrameworkData");
                string frameworkRepoPath = Path.Combine(novaFrameworkDataPath, "framework_repo");

                if (!Directory.Exists(novaFrameworkDataPath))
                {
                    Directory.CreateDirectory(novaFrameworkDataPath);
                    Debug.Log($"Created directory: {novaFrameworkDataPath}");
                }

                if (!Directory.Exists(frameworkRepoPath))
                {
                    Directory.CreateDirectory(frameworkRepoPath);
                    Debug.Log($"Created directory: {frameworkRepoPath}");
                }

                foreach (var gitVar in _gitUrlDic)
                {
                    DownloadPackageFromGit(gitVar.Key, gitVar.Value, frameworkRepoPath);
                }
                
                RemoveSelf();
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during installation: {e.Message}\n{e.StackTrace}");
            }
        }

        static void DownloadPackageFromGit(string packageName, string gitUrl, string targetPath)
        {
            try
            {
                // 使用 Git 命令行下载包
                string command = $"git clone \"{gitUrl}\"";
                string workingDir = targetPath;

                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo))
                {
                    process.WaitForExit();
                    int exitCode = process.ExitCode;

                    if (exitCode == 0)
                    {
                        Debug.Log($"Successfully downloaded package from {gitUrl}");

                        // 修改 manifest.json
                        ModifyManifestJson(packageName);
                    }
                    else
                    {
                        string error = process.StandardError.ReadToEnd();
                        Debug.LogError($"Failed to download package: {error}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Exception during package download: {e.Message}");
            }
        }

        static void ModifyManifestJson(string packageName)
        {
            try
            {
                string manifestPath = Path.Combine(Directory.GetParent(Application.dataPath).ToString(), "Packages", "manifest.json");

                if (!File.Exists(manifestPath))
                {
                    Debug.LogError($"manifest.json not found at: {manifestPath}");
                    return;
                }

                // 读取现有的 manifest.json
                string jsonContent = File.ReadAllText(manifestPath);

                // 简单的字符串操作来添加依赖项
                // 找到 "dependencies" 部分并添加新的条目
                int dependenciesStart = jsonContent.IndexOf("\"dependencies\"");
                if (dependenciesStart != -1)
                {
                    int openingBrace = jsonContent.IndexOf('{', dependenciesStart);
                    if (openingBrace != -1)
                    {
                        // 检查是否已存在相同的依赖项，避免重复添加
                        if (jsonContent.Substring(dependenciesStart, openingBrace - dependenciesStart + 200).Contains(packageName))
                        {
                            Debug.Log("Package dependency already exists in manifest.json");
                            return;
                        }

                        int insertPosition = jsonContent.IndexOf('\n', openingBrace + 1);
                        if (insertPosition == -1) insertPosition = openingBrace + 1;

                        string newEntry = $"\n    \"{packageName}\": \"file:./../NovaFrameworkData/framework_repo/{packageName}\",";
                        string updatedJson = jsonContent.Insert(insertPosition, newEntry);

                        File.WriteAllText(manifestPath, updatedJson);
                        Debug.Log("Successfully updated manifest.json with new package dependency");

                        // 刷新 Unity 包管理器
                        //AssetDatabase.Refresh();
                        EditorApplication.delayCall += AssetDatabase.Refresh;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to modify manifest.json: {e.Message}");
            }
        }

        private static void RemoveSelf()
        {
            try
            {
                // 使用 PackageManager 移除自身
                Client.Remove(_launcherPackageName);
                Debug.Log($"Successfully removed self: {_launcherPackageName}");
                // 显示安装完成的消息框
                EditorUtility.DisplayDialog("安装完成", "NovaFramework Installer 已成功安装！\n等待刷新完成后，按【 F8 】键完成后续安装。", "确定");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to remove self: {e.Message}");
            }
        }
        
        public static bool IsAssemblyExists(string assemblyName)
        {
            // 获取当前应用程序域中的所有已加载程序集
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            // 检查是否存在指定名称的程序集
            return assemblies.Any(assembly => 
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
