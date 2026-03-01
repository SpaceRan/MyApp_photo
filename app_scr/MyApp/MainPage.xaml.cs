using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

namespace MyApp
{
    public partial class MainPage : ContentPage
    {
        // ✅ 修复1：使用 AppDataDirectory，无需外部存储权限
        private string BaseFolderPath => Path.Combine(FileSystem.AppDataDirectory, "MyCancerData");
        private const string FileName_Tasks = "tasks.txt";
        private const string FileName_Search = "search_data.txt";
        
        // 进度保存 Key
        private const string PrefKey_TaskIndex = "last_task_index_v1";
        private const string PrefKey_LastPhotoTime = "last_photo_time";
        
        private List<string> _taskList = new();
        private List<string> _searchList = new();
        private int _currentTaskIndex = 0;
        private bool _isInContinuousMode = false;
        
        // ✅ 修复2：添加取消令牌，用于退出连续拍照
        private CancellationTokenSource? _cts;

        public MainPage()
        {
            InitializeComponent();
            InitializeAppAsync();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _isInContinuousMode = false;
            await ShowWelcomeAlert();
        }

        // ✅ 修复2：拦截物理返回键，退出连续拍照模式
        protected override bool OnBackButtonPressed()
        {
            if (_isInContinuousMode)
            {
                _cts?.Cancel();
                _isInContinuousMode = false;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    ResetUI();
                    await DisplayAlert("已退出连续拍照模式", "确定");
                });
                return true;
            }
            return base.OnBackButtonPressed();
        }

        #region 1. 核心初始化：权限 + 强制覆盖复制 + 读取

        private async void InitializeAppAsync()
        {
            try
            {
                await ForceCopyFromRawAsync();
                await LoadDataFromLocalFileAsync();
                _currentTaskIndex = Preferences.Default.Get(PrefKey_TaskIndex, 0);
                
                if (_currentTaskIndex >= _taskList.Count)
                {
                    _currentTaskIndex = 0;
                    Preferences.Default.Set(PrefKey_TaskIndex, 0);
                }

                System.Diagnostics.Debug.WriteLine($"[DEBUG] BaseFolderPath = {BaseFolderPath}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 任务数量 = {_taskList.Count}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 搜索库数量 = {_searchList.Count}");

                UpdateTaskDisplay();
            }
            catch (Exception ex)
            {
                await DisplayAlert("启动错误", $"初始化失败：{ex.Message}", "确定");
            }
        }

        private async Task ForceCopyFromRawAsync()
        {
            // 1. 确保目标文件夹存在
            if (!Directory.Exists(BaseFolderPath))
            {
                Directory.CreateDirectory(BaseFolderPath);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 创建文件夹：{BaseFolderPath}");
            }

            await CopySingleFileAsync(FileName_Tasks);
            await CopySingleFileAsync(FileName_Search);
            
            System.Diagnostics.Debug.WriteLine($"[DEBUG] 文件已强制同步至：{BaseFolderPath}");
        }


        private async Task CopySingleFileAsync(string fileName)
        {
            string destPath = Path.Combine(BaseFolderPath, fileName);

            try
            {
                using var inputStream = await FileSystem.OpenAppPackageFileAsync(fileName);
                
                using var outputStream = File.Create(destPath);
                
                await inputStream.CopyToAsync(outputStream);
                
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 文件复制成功：{fileName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 文件复制失败：{fileName}, 错误：{ex.Message}");
                throw new Exception($"无法复制文件 {fileName}: {ex.Message}");
            }
        }

        private async Task LoadDataFromLocalFileAsync()
        {
            _taskList.Clear();
            _searchList.Clear();

            string tasksPath = Path.Combine(BaseFolderPath, FileName_Tasks);
            string searchPath = Path.Combine(BaseFolderPath, FileName_Search);

            System.Diagnostics.Debug.WriteLine($"[DEBUG] 任务文件路径：{tasksPath}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG] 任务文件存在：{File.Exists(tasksPath)}");

            // 读取任务
            if (File.Exists(tasksPath))
            {
                string[] lines = await File.ReadAllLinesAsync(tasksPath);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        _taskList.Add(line.Trim());
                }
            }

            // 读取搜索库
            if (File.Exists(searchPath))
            {
                string[] lines = await File.ReadAllLinesAsync(searchPath);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        _searchList.Add(line.Trim());
                }
            }
        }

        #endregion

        #region 2. 欢迎弹窗

        private async Task ShowWelcomeAlert()
        {
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string msg = $"你会成为最好的开发者\n日期：{dateStr}\n\n数据目录：{BaseFolderPath}";
            await DisplayAlert("欢迎", msg, "开始工作");
        }

        #endregion

        #region 3. 界面控制逻辑

        private void ResetUI()
        {
            FrameModeB.IsVisible = false;
            FrameModeC.IsVisible = false;
            EntrySearch.Text = "";
            LabelSearchResult.Text = "";
            LabelSearchResult.TextColor = Colors.Black;
            BtnBackFromB.IsVisible = false;
        }

        private async void OnModeA_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            _isInContinuousMode = true;
            await StartContinuousCameraLoop();
        }

        // ✅ 修复2：添加退出按钮事件
        private void OnExitContinuousMode_Clicked(object sender, EventArgs e)
        {
            _cts?.Cancel();
            _isInContinuousMode = false;
            ResetUI();
        }

        private void OnModeB_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            _isInContinuousMode = false;
            FrameModeB.IsVisible = true;
            EntrySearch.Focus();
        }

        private void OnModeC_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            _isInContinuousMode = false;
            FrameModeC.IsVisible = true;
            UpdateTaskDisplay();
        }

        private void OnBackToMain_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            _isInContinuousMode = false;
        }

        #endregion

        #region 4. 业务逻辑 (模式 A/B/C)

        // ✅ 修复2：模式 A 连续拍照 - 带退出机制
        private async Task StartContinuousCameraLoop()
        {
            _cts = new CancellationTokenSource();
            
            try
            {
                while (_isInContinuousMode && !_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(500, _cts.Token);
                    bool taken = await CapturePhotoAsync();
                    if (!taken)
                    {
                        // 用户取消拍照，退出循环
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
                System.Diagnostics.Debug.WriteLine("[DEBUG] 连续拍照已取消");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 连续拍照错误：{ex.Message}");
            }
            finally
            {
                _isInContinuousMode = false;
                _cts?.Dispose();
                _cts = null;
                MainThread.BeginInvokeOnMainThread(() => ResetUI());
            }
        }

        // 模式 B：搜索逻辑
        private void OnSearchExecute_Clicked(object sender, EventArgs e)
        {
            string keyword = EntrySearch.Text?.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                DisplayAlert("提示", "请输入搜索内容", "确定");
                return;
            }

            // 在内存列表中查找
            bool exists = _searchList.Contains(keyword);

            if (exists)
            {
                LabelSearchResult.Text = "已经有了！";
                LabelSearchResult.TextColor = Colors.Red;
                // 延迟后关闭程序
                Device.StartTimer(TimeSpan.FromMilliseconds(800), () =>
                {
                    Application.Current.Quit();
                    return false;
                });
            }
            else
            {
                LabelSearchResult.Text = "没有！正在打开相机...";
                LabelSearchResult.TextColor = Colors.Green;
                
                Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await CapturePhotoAsync();
                        BtnBackFromB.IsVisible = true;
                    });
                });
            }
        }

        // 模式 C：任务显示与断点
        private void UpdateTaskDisplay()
        {
            if (_taskList.Count == 0)
            {
                LabelTaskText.Text = "任务列表为空\n请检查 Resources/Raw/tasks.txt";
                LabelTaskText.TextColor = Colors.Red;
                return;
            }

            if (_currentTaskIndex >= _taskList.Count)
            {
                _currentTaskIndex = 0;
                Preferences.Default.Set(PrefKey_TaskIndex, 0);
                LabelTaskText.Text = "✅ 所有任务已完成！\n已自动重置到第 1 个。";
                LabelTaskText.TextColor = Colors.Green;
                return;
            }

            LabelTaskText.Text = $"[{_currentTaskIndex + 1} / {_taskList.Count}] \n{_taskList[_currentTaskIndex]}";
            LabelTaskText.TextColor = Colors.Black;
        }

        private void OnNextTask_Clicked(object sender, EventArgs e)
        {
            _currentTaskIndex++;

            Preferences.Default.Set(PrefKey_TaskIndex, _currentTaskIndex);
            Preferences.Default.Set(PrefKey_LastPhotoTime, DateTime.Now.ToString());
            
            UpdateTaskDisplay();
        }

        #endregion

        #region 5. 相机通用逻辑

        private async Task<bool> CapturePhotoAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                    if (status != PermissionStatus.Granted)
                        return false;
                }
                var photo = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions { Title = "CaseCapture" });
                return photo != null;
            }
            catch (Exception ex)
            {
                await DisplayAlert("相机错误", ex.Message, "确定");
                return false;
            }
        }

        private async void OnTakePhoto_Clicked(object sender, EventArgs e)
        {
            await CapturePhotoAsync();
        }

        #endregion
    }
}