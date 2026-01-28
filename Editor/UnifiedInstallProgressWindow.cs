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
using UnityEditor;
using UnityEngine;

namespace NovaFramework.Editor.Launcher
{
    /// <summary>
    /// 统一安装进度窗口 - 用于launcher安装installer和common包的进度
    /// </summary>
    internal class UnifiedInstallProgressWindow : EditorWindow
    {
        // 安装步骤定义
        public enum InstallStep
        {
            None,
            CheckEnvironment,        // 检查环境
            DownloadLauncher,        // 下载launcher包
            InstallInstaller,        // 安装installer包
            InstallCommon,           // 安装common包
            LaunchInstaller,         // 启动installer
            RunAutoInstall,          // 运行自动安装
            Complete                 // 完成
        }

        // 步骤描述
        private static readonly Dictionary<InstallStep, string> StepDescriptions = new Dictionary<InstallStep, string>
        {
            { InstallStep.None, "准备中..." },
            { InstallStep.CheckEnvironment, "正在检查环境..." },
            { InstallStep.DownloadLauncher, "正在下载Launcher包..." },
            { InstallStep.InstallInstaller, "正在安装Installer包..." },
            { InstallStep.InstallCommon, "正在安装Common包..." },
            { InstallStep.LaunchInstaller, "正在启动Installer..." },
            { InstallStep.RunAutoInstall, "正在运行自动安装..." },
            { InstallStep.Complete, "安装完成！" }
        };

        // 单例实例
        private static UnifiedInstallProgressWindow _instance;
        
        // 当前状态
        private InstallStep _currentStep = InstallStep.None;
        private string _currentDetail = "";
        private float _progress = 0f;
        private int _currentPackageIndex = 0;
        private int _totalPackageCount = 0;
        private List<string> _logs = new List<string>();
        private Vector2 _logScrollPosition;
        private bool _shouldScrollToBottom = true; // 是否应该滚动到底部
        private bool _isComplete = false;
        private bool _hasError = false;
        private string _errorMessage = "";

        // 动画相关
        private string _activityIndicator = "";

        // GUI样式
        private GUIStyle _titleStyle;
        private GUIStyle _stepStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _logStyle;
        private GUIStyle _successStyle;
        private GUIStyle _errorStyle;
        private bool _stylesInitialized = false;
        
        // 用于存储AutoInstallManager实例，以便传递进度更新
        private object _autoInstallManagerInstance;
        private System.Action<int, int, string> _onPackageProgressChanged;
        private System.Action<string> _onLogAdded;
        private System.Action<string> _onErrorSet;
        private System.Action<int, string> _onStepChanged;

        /// <summary>
        /// 显示进度窗口
        /// </summary>
        public static UnifiedInstallProgressWindow ShowWindow()
        {
            if (_instance != null)
            {
                _instance.Close();
            }

            _instance = GetWindow<UnifiedInstallProgressWindow>(true, "统一安装进度", true);
            
            // 设置窗口大小
            Vector2 windowSize = new Vector2(520, 420);
            _instance.minSize = windowSize;
            _instance.maxSize = new Vector2(600, 500);
            
            // 将窗口居中显示（稍微偏左上，避免和Unity包管理弹窗重叠）
            Vector2 screenCenter = new Vector2(Screen.currentResolution.width / 2f, Screen.currentResolution.height / 2f);
            float offsetX = -100f; // 偏左
            float offsetY = -50f;  // 偏上
            Rect windowRect = new Rect(
                screenCenter.x - windowSize.x / 2f + offsetX,
                screenCenter.y - windowSize.y / 2f + offsetY,
                windowSize.x,
                windowSize.y
            );
            _instance.position = windowRect;
            
            _instance.Reset();
            _instance.ShowUtility();
            
            // 强制重绘确保界面显示
            _instance.Repaint();
            
            return _instance;
        }

        /// <summary>
        /// 关闭进度窗口
        /// </summary>
        public static void CloseWindow()
        {
            if (_instance != null)
            {
                _instance.Close();
                _instance = null;
            }
        }

        /// <summary>
        /// 获取当前实例
        /// </summary>
        public static UnifiedInstallProgressWindow Instance => _instance;

        /// <summary>
        /// 重置状态
        /// </summary>
        private void Reset()
        {
            _currentStep = InstallStep.None;
            _currentDetail = "";
            _progress = 0f;
            _currentPackageIndex = 0;
            _totalPackageCount = 0;
            _logs.Clear();
            _isComplete = false;
            _hasError = false;
            _errorMessage = "";
        }

        /// <summary>
        /// 设置当前步骤
        /// </summary>
        public void SetStep(InstallStep step, string detail = "")
        {
            _currentStep = step;
            _currentDetail = detail;
            
            // 计算进度
            int stepIndex = (int)step;
            int totalSteps = Enum.GetValues(typeof(InstallStep)).Length - 1; // 减去None
            _progress = (float)stepIndex / totalSteps;

            // 添加日志
            string logMessage = StepDescriptions.ContainsKey(step) ? StepDescriptions[step] : step.ToString();
            if (!string.IsNullOrEmpty(detail))
            {
                logMessage += " " + detail;
            }
            AddLog(logMessage);

            if (step == InstallStep.Complete)
            {
                _isComplete = true;
                _progress = 1f;
            }

            Repaint();
        }

        /// <summary>
        /// 设置包安装进度
        /// </summary>
        public void SetPackageProgress(int currentIndex, int totalCount, string packageName)
        {
            _currentPackageIndex = currentIndex;
            _totalPackageCount = totalCount;
            _currentDetail = $"({currentIndex}/{totalCount}) {packageName}";
            
            // 计算包安装阶段的进度
            // 根据当前步骤调整进度计算
            float baseProgress = 0f;
            float stepRange = 0f;
            
            if (_currentStep == InstallStep.InstallInstaller || _currentStep == InstallStep.InstallCommon)
            {
                // 假设安装installer和common分别占不同进度段
                if (_currentStep == InstallStep.InstallInstaller)
                {
                    baseProgress = 0.2f;  // 20% 左右
                    stepRange = 0.1f;     // 10% 范围
                }
                else if (_currentStep == InstallStep.InstallCommon)
                {
                    baseProgress = 0.3f;  // 30% 左右
                    stepRange = 0.1f;     // 10% 范围
                }
            }
            else if (_currentStep == InstallStep.RunAutoInstall)
            {
                // 如果是在运行自动安装期间，则使用更复杂的进度计算
                baseProgress = 0.4f;  // 40% 开始
                stepRange = 0.5f;     // 50% 范围
            }
            
            float packageProgress = totalCount > 0 ? (float)currentIndex / totalCount : 0f;
            _progress = baseProgress + (stepRange * packageProgress);
            
            AddLog($"  正在配置: {packageName}");
            Repaint();
        }

        /// <summary>
        /// 添加日志
        /// </summary>
        public void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logs.Add($"[{timestamp}] {message}");
            
            // 限制日志数量
            if (_logs.Count > 100)
            {
                _logs.RemoveAt(0);
            }
            
            // 添加新日志时，应该自动滚动到底部
            _shouldScrollToBottom = true;
            
            Repaint();
        }

        /// <summary>
        /// 设置错误状态
        /// </summary>
        public void SetError(string errorMessage)
        {
            _hasError = true;
            _errorMessage = errorMessage;
            AddLog($"错误: {errorMessage}");
            Repaint();
        }

        /// <summary>
        /// 初始化GUI样式
        /// </summary>
        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            _stepStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft
            };

            _detailStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            _logStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                richText = true,
                wordWrap = true
            };

            _successStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            _successStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f);

            _errorStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            _errorStyle.normal.textColor = new Color(0.9f, 0.2f, 0.2f);

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.Space(10);

            // 标题
            EditorGUILayout.LabelField("Nova Framework 统一安装", _titleStyle);
            
            EditorGUILayout.Space(15);

            // 当前步骤
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                string stepText = StepDescriptions.ContainsKey(_currentStep) ? StepDescriptions[_currentStep] : "准备中...";
                
                // 添加步骤进度信息（例如 3/11）
                int stepIndex = (int)_currentStep;
                int totalSteps = Enum.GetValues(typeof(InstallStep)).Length - 1; // 减去None
                string stepProgress = $" ({stepIndex}/{totalSteps})";
                if (stepIndex <= 0) stepProgress = ""; // 不显示 0/11 或负数
                
                EditorGUILayout.LabelField(stepText + stepProgress, _stepStyle);
                GUILayout.FlexibleSpace();
            }

            // 仅在非包安装步骤时显示详细信息
            if (!string.IsNullOrEmpty(_currentDetail) && _currentStep != InstallStep.InstallInstaller && _currentStep != InstallStep.InstallCommon)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    string detailText = _currentDetail;
                    if (!_isComplete && !_hasError) // 只在安装进行时添加动画指示器
                    {
                        detailText += _activityIndicator; // 将点动画直接附加到文本末尾
                    }
                    EditorGUILayout.LabelField(detailText, _detailStyle);
                    GUILayout.FlexibleSpace();
                }
            }

            EditorGUILayout.Space(10);

            // 进度条
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(20);
                Rect progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(25));
                EditorGUI.ProgressBar(progressRect, _progress, $"{(_progress * 100):F0}%");
                GUILayout.Space(20);
            }

            // 包安装进度文本（放在进度条下方）
            if ((_currentStep == InstallStep.InstallInstaller || _currentStep == InstallStep.InstallCommon || _currentStep == InstallStep.RunAutoInstall) && _totalPackageCount > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    string packageProgressText = $"{_currentDetail}";
                    EditorGUILayout.LabelField(packageProgressText, _detailStyle);
                    GUILayout.FlexibleSpace();
                }
            }
            else if (!string.IsNullOrEmpty(_currentDetail) && _currentStep != InstallStep.InstallInstaller && _currentStep != InstallStep.InstallCommon)
            {
                // 只在非包安装步骤时显示详情文本
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    string detailText = _currentDetail;
                    if (!_isComplete && !_hasError) // 只在安装进行时添加动画指示器
                    {
                        detailText += _activityIndicator; // 将点动画直接附加到文本末尾
                    }
                    EditorGUILayout.LabelField(detailText, _detailStyle);
                    GUILayout.FlexibleSpace();
                }
            }

            EditorGUILayout.Space(15);

            // 日志区域
            EditorGUILayout.LabelField("安装日志", EditorStyles.boldLabel);
            
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
            {
                // 监听滚动变化，如果用户滚动则停止自动滚动到底部
                Vector2 newScrollPos = EditorGUILayout.BeginScrollView(_logScrollPosition);
                if (newScrollPos != _logScrollPosition)
                {
                    // 滚动位置改变，检查是否滚动到底部
                    float scrollThreshold = newScrollPos.y - (_logScrollPosition.y + 10f); // 10f为容差
                    _shouldScrollToBottom = scrollThreshold >= 0; // 如果是向下滚动，则继续自动滚动
                }
                _logScrollPosition = newScrollPos;
                
                foreach (string log in _logs)
                {
                    EditorGUILayout.LabelField(log, _logStyle);
                }
                
                // 仅在需要时自动滚动到底部
                if (Event.current.type == EventType.Repaint && _shouldScrollToBottom)
                {
                    _logScrollPosition.y = Mathf.Infinity;
                }
                
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(10);

            // 状态显示和按钮
            if (_hasError)
            {
                EditorGUILayout.LabelField("安装过程中出现错误", _errorStyle);
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                
                if (GUILayout.Button("关闭", GUILayout.Height(30)))
                {
                    Close();
                }
            }
            else if (_isComplete)
            {
                EditorGUILayout.LabelField("安装完成！", _successStyle);
                
                if (GUILayout.Button("关闭", GUILayout.Height(30)))
                {
                    Close();
                }
            }
            else
            {
                // 安装进行中，显示提示
                EditorGUILayout.HelpBox("安装进行中，请勿关闭此窗口...", MessageType.Info);
            }

            EditorGUILayout.Space(10);
            
            // 更新动画指示器
            UpdateActivityIndicator();
        }

        private void UpdateActivityIndicator()
        {
            float time = Time.realtimeSinceStartup;
            int dots = ((int)(time * 3)) % 4; // 每秒变化3次
            _activityIndicator = new string('.', dots);
        }
        
        private void OnDestroy()
        {
            _instance = null;
        }

        /// <summary>
        /// 设置AutoInstallManager的进度回调
        /// </summary>
        public void SetAutoInstallProgressCallbacks(System.Action<int, int, string> onPackageProgress, 
                                                   System.Action<string> onLogAdded, 
                                                   System.Action<string> onErrorSet,
                                                   System.Action<int, string> onStepChanged = null)
        {
            _onPackageProgressChanged = (currentIndex, totalCount, packageName) =>
            {
                // 当AutoInstallManager更新进度时，我们也更新统一进度窗口
                SetPackageProgress(currentIndex, totalCount, packageName);
                onPackageProgress?.Invoke(currentIndex, totalCount, packageName);
            };
            
            _onLogAdded = (message) =>
            {
                AddLog(message);
                onLogAdded?.Invoke(message);
            };
            
            _onErrorSet = (errorMessage) =>
            {
                SetError(errorMessage);
                onErrorSet?.Invoke(errorMessage);
            };
            
            _onStepChanged = (stepValue, detail) =>
            {
                // 将AutoInstallManager的步骤值转换为统一窗口的步骤
                var unifiedStep = MapAutoInstallStepToUnifiedStep(stepValue);
                SetStep(unifiedStep, detail);
                onStepChanged?.Invoke(stepValue, detail);
            };
        }

        /// <summary>
        /// 将AutoInstallProgressWindow.InstallStep的值映射到UnifiedInstallProgressWindow.InstallStep
        /// </summary>
        private UnifiedInstallProgressWindow.InstallStep MapAutoInstallStepToUnifiedStep(int autoInstallStepValue)
        {
            // 根据AutoInstallProgressWindow.InstallStep的枚举值进行映射
            // None=0, CheckEnvironment=1, LoadPackageInfo=2, InstallPackages=3, CreateDirectories=4, 
            // InstallBasePack=5, CopyAotLibraries=6, GenerateConfig=7, CopyResources=8, ExportConfig=9, 
            // OpenScene=10, Complete=11
            
            switch (autoInstallStepValue)
            {
                case 0: // None
                    return InstallStep.RunAutoInstall;
                case 1: // CheckEnvironment
                    return InstallStep.RunAutoInstall;
                case 2: // LoadPackageInfo
                    return InstallStep.RunAutoInstall;
                case 3: // InstallPackages
                    return InstallStep.RunAutoInstall;
                case 4: // CreateDirectories
                    return InstallStep.RunAutoInstall;
                case 5: // InstallBasePack
                    return InstallStep.RunAutoInstall;
                case 6: // CopyAotLibraries
                    return InstallStep.RunAutoInstall;
                case 7: // GenerateConfig
                    return InstallStep.RunAutoInstall;
                case 8: // CopyResources
                    return InstallStep.RunAutoInstall;
                case 9: // ExportConfig
                    return InstallStep.RunAutoInstall;
                case 10: // OpenScene
                    return InstallStep.RunAutoInstall;
                case 11: // Complete
                    return InstallStep.Complete;
                default:
                    return InstallStep.RunAutoInstall; // 默认映射到运行自动安装阶段
            }
        }

        /// <summary>
        /// 代理AutoInstallManager的进度更新
        /// </summary>
        public void ProxySetPackageProgress(int currentIndex, int totalCount, string packageName)
        {
            SetPackageProgress(currentIndex, totalCount, packageName);
            _onPackageProgressChanged?.Invoke(currentIndex, totalCount, packageName);
        }

        /// <summary>
        /// 代理AutoInstallManager的日志添加
        /// </summary>
        public void ProxyAddLog(string message)
        {
            AddLog(message);
            _onLogAdded?.Invoke(message);
        }

        /// <summary>
        /// 代理AutoInstallManager的错误设置
        /// </summary>
        public void ProxySetError(string errorMessage)
        {
            SetError(errorMessage);
            _onErrorSet?.Invoke(errorMessage);
        }

        /// <summary>
        /// 代理AutoInstallManager的步骤更改
        /// </summary>
        public void ProxySetStep(int stepValue, string detail = "")
        {
            var unifiedStep = MapAutoInstallStepToUnifiedStep(stepValue);
            SetStep(unifiedStep, detail);
            _onStepChanged?.Invoke(stepValue, detail);
        }
    }
}