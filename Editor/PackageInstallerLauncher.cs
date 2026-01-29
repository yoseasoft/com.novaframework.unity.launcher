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
using System.Threading;

namespace NovaFramework.Editor.Launcher
{
    public class PackageInstallerLauncher
    {
        private static readonly Dictionary<string, string> _gitUrlDic = new Dictionary<string, string>()
        {
            {
                "com.novaframework.unity.core.common",
                "https://github.com/phao5120/com.novaframework.unity.core.common.git"
            },
            {
                "com.novaframework.unity.installer",
                "https://github.com/phao5120/com.novaframework.unity.installer.git"
            },
        };

        private static string _launcherPackageName = "com.novaframework.unity.launcher";
        private static UnifiedInstallProgressWindow _progressWindow;

        [InitializeOnLoadMethod]
        static void ExecuteInstallation()
        {
            // 检查Nova.Installer.Editor程序集是否存在
            if (IsAssemblyExists("NovaEditor.Installer") || IsAssemblyExists("NovaEditor.Common"))
            {
                Debug.Log("Nova.Installer.Editor assembly already exists. Skipping installation.");
                Debug.Log("Nova.Common.Editor assembly already exists. Skipping installation.");
                return; // 如果程序集已存在，则跳过安装
            }
            
            Debug.Log("ExecuteInstallation - Starting unified installation process");

            // 显示统一安装进度窗口
            _progressWindow = UnifiedInstallProgressWindow.ShowWindow();
            
            // 立即设置初始步骤并强制刷新界面
            _progressWindow.SetStep(UnifiedInstallProgressWindow.InstallStep.CheckEnvironment, "检查安装环境...");
            
            // 强制刷新界面，确保立即显示内容
            _progressWindow.Repaint();
            
            // 延迟执行安装，确保UI已渲染
            EditorApplication.delayCall += DoExecuteInstallation;
        }

        static void DoExecuteInstallation()
        {
            try
            {
                _progressWindow?.SetStep(UnifiedInstallProgressWindow.InstallStep.DownloadLauncher, "准备下载框架包...");
                
                //创建目录结构
                string projectPath = Path.GetDirectoryName(Application.dataPath);
                string novaFrameworkDataPath = Path.Combine(projectPath, "NovaFrameworkData");
                string frameworkRepoPath = Path.Combine(novaFrameworkDataPath, "framework_repo");

                if (!Directory.Exists(novaFrameworkDataPath))
                {
                    Directory.CreateDirectory(novaFrameworkDataPath);
                    _progressWindow?.AddLog($"Created directory: {novaFrameworkDataPath}");
                }

                if (!Directory.Exists(frameworkRepoPath))
                {
                    Directory.CreateDirectory(frameworkRepoPath);
                    _progressWindow?.AddLog($"Created directory: {frameworkRepoPath}");
                }

                // 依次下载并安装包
                DownloadAndInstallPackagesSequentially(_gitUrlDic.ToList(), 0);
            }
            catch (Exception e)
            {
                _progressWindow?.SetError($"Error during installation: {e.Message}\n{e.StackTrace}");
                Debug.LogError($"Error during installation: {e.Message}\n{e.StackTrace}");
            }
        }

        // 顺序下载和安装包
        static void DownloadAndInstallPackagesSequentially(List<KeyValuePair<string, string>> gitUrls, int index)
        {
            if (index >= gitUrls.Count)
            {
                // 所有包都已安装完成，启动AutoInstallManager
                _progressWindow?.SetStep(UnifiedInstallProgressWindow.InstallStep.LaunchInstaller, "启动Installer...");
                EditorApplication.delayCall += StartAutoInstallManager;
                return;
            }

            var currentPair = gitUrls[index];
            string packageName = currentPair.Key;
            string gitUrl = currentPair.Value;

            _progressWindow?.SetStep(
                packageName.Contains("installer") ? UnifiedInstallProgressWindow.InstallStep.InstallInstaller : 
                packageName.Contains("common") ? UnifiedInstallProgressWindow.InstallStep.InstallCommon :
                UnifiedInstallProgressWindow.InstallStep.DownloadLauncher,
                $"正在安装 {packageName}..."
            );

            DownloadPackageFromGit(packageName, gitUrl, Path.Combine(Path.GetDirectoryName(Application.dataPath), "NovaFrameworkData", "framework_repo"), () =>
            {
                _progressWindow?.AddLog($"  完成: {packageName}");
                
                // 延迟执行下一个包的安装
                EditorApplication.delayCall += () =>
                {
                    DownloadAndInstallPackagesSequentially(gitUrls, index + 1);
                };
            });
        }

        static void DownloadPackageFromGit(string packageName, string gitUrl, string targetPath, System.Action onComplete)
        {
            try
            {
                // 使用 Git 命令行下载包
                string command = $"git clone \"{gitUrl}\" \"{Path.Combine(targetPath, packageName)}\"";
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
                        _progressWindow?.AddLog($"Successfully downloaded package from {gitUrl}");

                        // 修改 manifest.json
                        ModifyManifestJson(packageName, onComplete);
                    }
                    else
                    {
                        string error = process.StandardError.ReadToEnd();
                        string errorMsg = $"Failed to download package: {error}";
                        _progressWindow?.SetError(errorMsg);
                        Debug.LogError(errorMsg);
                        onComplete?.Invoke(); // 确保即使失败也能继续
                    }
                }
            }
            catch (Exception e)
            {
                string errorMsg = $"Exception during package download: {e.Message}";
                _progressWindow?.SetError(errorMsg);
                Debug.LogError(errorMsg);
                onComplete?.Invoke(); // 确保即使失败也能继续
            }
        }

        static void ModifyManifestJson(string packageName, System.Action onComplete)
        {
            try
            {
                string manifestPath = Path.Combine(Directory.GetParent(Application.dataPath).ToString(), "Packages", "manifest.json");

                if (!File.Exists(manifestPath))
                {
                    string errorMsg = $"manifest.json not found at: {manifestPath}";
                    _progressWindow?.SetError(errorMsg);
                    Debug.LogError(errorMsg);
                    onComplete?.Invoke();
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
                            _progressWindow?.AddLog("Package dependency already exists in manifest.json");
                            onComplete?.Invoke();
                            return;
                        }

                        int insertPosition = jsonContent.IndexOf('\n', openingBrace + 1);
                        if (insertPosition == -1) insertPosition = openingBrace + 1;

                        string newEntry = $"\n    \"{packageName}\": \"file:./../NovaFrameworkData/framework_repo/{packageName}\",";
                        string updatedJson = jsonContent.Insert(insertPosition, newEntry);

                        File.WriteAllText(manifestPath, updatedJson);
                        _progressWindow?.AddLog("Successfully updated manifest.json with new package dependency");

                        // 刷新 Unity 包管理器
                        AssetDatabase.Refresh();
                        
                        // 延迟一点时间确保刷新完成
                        EditorApplication.delayCall += () =>
                        {
                            Thread.Sleep(1000); // 等待1秒让Unity处理包更新
                            onComplete?.Invoke();
                        };
                    }
                }
                else
                {
                    onComplete?.Invoke();
                }
            }
            catch (Exception e)
            {
                string errorMsg = $"Failed to modify manifest.json: {e.Message}";
                _progressWindow?.SetError(errorMsg);
                Debug.LogError(errorMsg);
                onComplete?.Invoke();
            }
        }

        // 启动AutoInstallManager
        static void StartAutoInstallManager()
        {
            _progressWindow?.SetStep(UnifiedInstallProgressWindow.InstallStep.RunAutoInstall, "正在启动AutoInstallManager...");
            
            // 刷新AssetDatabase以确保新添加的包被识别
            AssetDatabase.Refresh();
            
            // 延迟执行，给Unity时间处理包的加载
            EditorApplication.delayCall += () =>
            {
                // 使用更可靠的方式等待包管理器完成更新
                ResolvePackageManagerAndLoadAssembly();
            };
        }

        // 等待包管理器完成更新并加载程序集
        static void ResolvePackageManagerAndLoadAssembly()
        {
            // 首先强制解析包管理器
            // 注意：Client.Resolve()是一个异步操作，它本身不返回请求对象
            Client.Resolve();
            
            // 由于Client.Resolve()是异步的，我们需要等待一段时间以确保包管理器完成更新
            // 使用计数器和EditorApplication.update来定期检查
            int checkCount = 0;
            int maxChecks = 60; // 最多检查60次 (30秒)
            
            EditorApplication.update += CheckResolveAndUpdate;
            
            void CheckResolveAndUpdate()
            {
                checkCount++;
                
                // 简单地等待一段时间，然后假设resolve已完成
                if (checkCount >= 10) // 等待大约10次（每次约50-100毫秒）
                {
                    // 停止监听更新事件
                    EditorApplication.update -= CheckResolveAndUpdate;
                    
                    _progressWindow?.AddLog("包管理器解析等待完成，准备启动AutoInstallManager...");
                    
                    // 延迟一段时间确保程序集完全加载
                    EditorApplication.delayCall += () =>
                    {
                        System.Threading.Thread.Sleep(2000); // 等待2秒确保程序集加载
                        TryLoadAndStartAutoInstallManager();
                    };
                }
                else if (checkCount >= maxChecks)
                {
                    // 即使resolve未完成，也停止等待并尝试启动AutoInstallManager
                    EditorApplication.update -= CheckResolveAndUpdate;
                    
                    _progressWindow?.AddLog("包管理器解析等待超时，但仍准备启动AutoInstallManager...");
                    
                    // 延迟一段时间确保程序集尽可能加载
                    EditorApplication.delayCall += () =>
                    {
                        System.Threading.Thread.Sleep(1000); // 等待1秒
                        TryLoadAndStartAutoInstallManager();
                    };
                }
            }
        }
        
        // 尝试加载并启动AutoInstallManager
        static void TryLoadAndStartAutoInstallManager()
        {
            _progressWindow?.AddLog("开始尝试加载并启动AutoInstallManager...");
            
            // 再次刷新确保所有变更都已应用
            AssetDatabase.Refresh();
            
            // 延迟一小段时间让Unity完成程序集编译
            System.Threading.Thread.Sleep(2000);
            
            // 尝试通过反射调用AutoInstallManager的StartAutoInstall方法
            Type autoInstallManagerType = null;
            
            // 首先尝试直接获取类型
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    // 排除系统程序集，只检查用户相关的程序集
                    if (assembly.FullName.StartsWith("Nova") || 
                        assembly.FullName.StartsWith("Assembly-CSharp") || 
                        assembly.FullName.Contains("Installer"))
                    {
                        var type = assembly.GetType("NovaFramework.Editor.Installer.AutoInstallManager");
                        if (type != null)
                        {
                            autoInstallManagerType = type;
                            _progressWindow?.AddLog($"找到AutoInstallManager类型在程序集: {assembly.FullName}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 忽略无法访问的程序集
                    continue;
                }
            }
            
            if (autoInstallManagerType != null)
            {
                // 首先设置外部进度回调
                var setExternalProgressCallbacksMethod = autoInstallManagerType.GetMethod("SetExternalProgressCallbacks", 
                    BindingFlags.Static | BindingFlags.Public);
                
                if (setExternalProgressCallbacksMethod != null)
                {
                    try
                    {
                        // 创建各种回调函数
                        var stepCallback = new System.Action<int, string>((stepVal, detail) => {
                            var unifiedStep = MapAutoInstallStepToUnifiedStep(stepVal);
                            _progressWindow.SetStep(unifiedStep, detail);
                        });
                        
                        var packageProgressCallback = new System.Action<int, int, string>(_progressWindow.SetPackageProgress);
                        var logCallback = new System.Action<string>(_progressWindow.AddLog);
                        var errorCallback = new System.Action<string>(_progressWindow.SetError);
                        
                        // 调用外部进度设置方法
                        setExternalProgressCallbacksMethod.Invoke(null, new object[] {
                            _progressWindow,  // externalProgressWindow
                            stepCallback,     // setStepCallback
                            packageProgressCallback, // setPackageProgressCallback
                            logCallback,      // addLogCallback
                            errorCallback     // setErrorCallback
                        });
                        
                        _progressWindow.AddLog("已连接到AutoInstallManager进度系统...");
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"Error setting external progress callbacks: {ex.Message}";
                        _progressWindow.SetError(errorMsg);
                        Debug.LogError(errorMsg);
                    }
                }
                
                // 现在调用StartAutoInstall方法
                var startAutoInstallMethod = autoInstallManagerType.GetMethod("StartAutoInstall", 
                    BindingFlags.Static | BindingFlags.Public);
                
                if (startAutoInstallMethod != null)
                {
                    try
                    {
                        _progressWindow.AddLog("正在调用AutoInstallManager.StartAutoInstall方法...");
                        
                        // 在调用StartAutoInstall之前，确保包管理器已完全解析
                        // 这里使用异步方式调用，避免阻塞
                        EditorApplication.delayCall += () =>
                        {
                            try
                            {
                                startAutoInstallMethod.Invoke(null, null);
                                _progressWindow.AddLog("AutoInstallManager已启动，进度将同步更新...");
                            }
                            catch (Exception invokeEx)
                            {
                                string errorMsg = $"Error invoking AutoInstallManager.StartAutoInstall: {invokeEx.Message}";
                                _progressWindow.SetError(errorMsg);
                                Debug.LogError(errorMsg);
                                Debug.LogError(invokeEx.StackTrace);
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"Error invoking AutoInstallManager.StartAutoInstall: {ex.Message}";
                        _progressWindow.SetError(errorMsg);
                        Debug.LogError(errorMsg);
                        Debug.LogError(ex.StackTrace);
                    }
                }
                else
                {
                    string errorMsg = "AutoInstallManager.StartAutoInstall method not found";
                    _progressWindow.SetError(errorMsg);
                    Debug.LogError(errorMsg);
                }
            }
            else
            {
                // 如果找不到类型，尝试强制刷新并再等待一会儿
                string errorMsg = "NovaFramework.Editor.Installer.AutoInstallManager type not found in loaded assemblies";
                _progressWindow.AddLog(errorMsg);
                
                // 强制刷新并重试
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                
                // 再次等待并尝试
                EditorApplication.delayCall += () =>
                {
                    System.Threading.Thread.Sleep(3000); // 等待3秒
                    _progressWindow?.AddLog("再次尝试加载AutoInstallManager...");
                    
                    // 最后再尝试一次
                    Type finalAutoInstallManagerType = null;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            if (assembly.FullName.StartsWith("Nova") || 
                                assembly.FullName.StartsWith("Assembly-CSharp") || 
                                assembly.FullName.Contains("Installer"))
                            {
                                var type = assembly.GetType("NovaFramework.Editor.Installer.AutoInstallManager");
                                if (type != null)
                                {
                                    finalAutoInstallManagerType = type;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            continue;
                        }
                    }
                    
                    if (finalAutoInstallManagerType != null)
                    {
                        var startAutoInstallMethod = finalAutoInstallManagerType.GetMethod("StartAutoInstall", 
                            BindingFlags.Static | BindingFlags.Public);
                        
                        if (startAutoInstallMethod != null)
                        {
                            try
                            {
                                _progressWindow.AddLog("最终找到了AutoInstallManager，正在调用StartAutoInstall方法...");
                                startAutoInstallMethod.Invoke(null, null);
                                _progressWindow.AddLog("AutoInstallManager已启动！");
                            }
                            catch (Exception ex)
                            {
                                string errorMsg = $"Final attempt - Error invoking AutoInstallManager.StartAutoInstall: {ex.Message}";
                                _progressWindow.SetError(errorMsg);
                                Debug.LogError(errorMsg);
                                Debug.LogError(ex.StackTrace);
                            }
                        }
                    }
                    else
                    {
                        // 如果最终还是找不到，尝试手动调用Client.Resolve()后等待
                        _progressWindow?.AddLog("仍然找不到AutoInstallManager，尝试再次调用Client.Resolve()...");
                        
                        EditorApplication.delayCall += () =>
                        {
                            Client.Resolve();
                            System.Threading.Thread.Sleep(3000);
                            AssetDatabase.Refresh();
                            
                            // 最终尝试
                            Type lastAttemptType = null;
                            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                            {
                                try
                                {
                                    if (assembly.FullName.StartsWith("Nova") || 
                                        assembly.FullName.StartsWith("Assembly-CSharp") || 
                                        assembly.FullName.Contains("Installer"))
                                    {
                                        var type = assembly.GetType("NovaFramework.Editor.Installer.AutoInstallManager");
                                        if (type != null)
                                        {
                                            lastAttemptType = type;
                                            break;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    continue;
                                }
                            }
                            
                            if (lastAttemptType != null)
                            {
                                var startAutoInstallMethod = lastAttemptType.GetMethod("StartAutoInstall", 
                                    BindingFlags.Static | BindingFlags.Public);
                                
                                if (startAutoInstallMethod != null)
                                {
                                    try
                                    {
                                        _progressWindow.AddLog("最后尝试成功！正在调用StartAutoInstall方法...");
                                        startAutoInstallMethod.Invoke(null, null);
                                        _progressWindow.AddLog("AutoInstallManager已启动！");
                                    }
                                    catch (Exception ex)
                                    {
                                        string errorMsg = $"Last attempt - Error invoking AutoInstallManager.StartAutoInstall: {ex.Message}";
                                        _progressWindow.SetError(errorMsg);
                                        Debug.LogError(errorMsg);
                                        Debug.LogError(ex.StackTrace);
                                    }
                                }
                            }
                            else
                            {
                                _progressWindow.SetError("无法找到AutoInstallManager，请检查安装过程是否成功完成。");
                            }
                        };
                    }
                };
            }
        }

        // 将AutoInstallProgressWindow.InstallStep的值映射到UnifiedInstallProgressWindow.InstallStep
        private static UnifiedInstallProgressWindow.InstallStep MapAutoInstallStepToUnifiedStep(int autoInstallStepValue)
        {
            // 我们需要根据AutoInstallProgressWindow.InstallStep的枚举值进行映射
            // 由于原进度窗口已移至launcher包中，我们使用数值映射
            // 假设值对应关系如下：
            // None=0, CheckEnvironment=1, LoadPackageInfo=2, InstallPackages=3, CreateDirectories=4, 
            // InstallBasePack=5, CopyAotLibraries=6, GenerateConfig=7, CopyResources=8, ExportConfig=9, 
            // OpenScene=10, Complete=11
            
            switch (autoInstallStepValue)
            {
                case 0: // None
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 1: // CheckEnvironment
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 2: // LoadPackageInfo
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 3: // InstallPackages
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 4: // CreateDirectories
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 5: // InstallBasePack
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 6: // CopyAotLibraries
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 7: // GenerateConfig
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 8: // CopyResources
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 9: // ExportConfig
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 10: // OpenScene
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall;
                case 11: // Complete
                    return UnifiedInstallProgressWindow.InstallStep.Complete;
                default:
                    return UnifiedInstallProgressWindow.InstallStep.RunAutoInstall; // 默认映射到运行自动安装阶段
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