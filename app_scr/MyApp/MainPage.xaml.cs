using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

namespace MyApp
{
    public partial class MainPage : ContentPage
    {
        // ================= 配置区域 =================
        private const string BaseFolderPath = "/storage/emulated/0/MyCancerData";
        
        // 文件名 (与 Resources/Raw 下的文件名一致)
        private const string FileName_Tasks = "tasks.txt";
        private const string FileName_Search = "search_data.txt";
        
        // 进度保存 Key
        private const string PrefKey_TaskIndex = "last_task_index_v1"; //注意每次安装时都要+1更新否则肯定出问题
        private List<string> _taskList = new();
        private List<string> _searchList = new();
        private int _currentTaskIndex = 0;
        private bool _isInContinuousMode = false;

        public MainPage()
        {
            InitializeComponent();
            InitializeAppAsync();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _isInContinuousMode = false; // 防止后台死循环
            await ShowWelcomeAlert();
        }

        #region 1. 核心初始化：权限 + 强制覆盖复制 + 读取

        private async void InitializeAppAsync()
        {
            try
            {
                await RequestStoragePermissionsAsync();
                await ForceCopyFromRawAsync();
                await LoadDataFromLocalFileAsync();
                _currentTaskIndex = Preferences.Default.Get(PrefKey_TaskIndex, 0);
                
                if (_currentTaskIndex >= _taskList.Count)
                {
                    _currentTaskIndex = 0;
                    Preferences.Default.Set(PrefKey_TaskIndex, 0);
                }

                UpdateTaskDisplay();
            }
            catch (Exception ex)
            {
                await DisplayAlert("启动错误", $"初始化失败：{ex.Message}", "确定");
            }
        }

        /// <summary>
        /// </summary>
        private async Task RequestStoragePermissionsAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.StorageWrite>();
            }

            if (status != PermissionStatus.Granted)
            {
                await DisplayAlert("权限警告", "必须授予存储权限才能同步任务文件！\n请在设置中手动开启。", "确定");
            }
        }

        /// <summary>
        /// 【强制覆盖逻辑】
        /// 无论手机里有没有文件，都从 APK 包内的 Raw 资源复制出来，覆盖旧文件。
        /// 确保每次安装/启动都是最新的 txt 内容。
        /// </summary>
        private async Task ForceCopyFromRawAsync()
        {
            // 1. 确保目标文件夹存在
            if (!Directory.Exists(BaseFolderPath))
            {
                Directory.CreateDirectory(BaseFolderPath);
            }

            // 2. 复制任务文件
            await CopySingleFileAsync(FileName_Tasks);

            // 3. 复制搜索文件
            await CopySingleFileAsync(FileName_Search);
            
            Console.WriteLine($"文件已强制同步至：{BaseFolderPath}");
        }

        /// <summary>
        /// 单个文件的复制实现
        /// </summary>
        private async Task CopySingleFileAsync(string fileName)
        {
            string destPath = Path.Combine(BaseFolderPath, fileName);

            try
            {
                using var inputStream = await FileSystem.OpenAppPackageFileAsync(fileName);
                
                // 创建/覆盖 手机上的目标文件
                using var outputStream = File.Create(destPath);
                
                // 执行流拷贝
                await inputStream.CopyToAsync(outputStream);
            }
            catch (Exception ex)
            {
                throw new Exception($"无法复制文件 {fileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// 从手机本地文件读取内容到内存列表
        /// </summary>
        private async Task LoadDataFromLocalFileAsync()
        {
            _taskList.Clear();
            _searchList.Clear();

            string tasksPath = Path.Combine(BaseFolderPath, FileName_Tasks);
            string searchPath = Path.Combine(BaseFolderPath, FileName_Search);

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
            string msg = $"你会成为最好的开发者\n日期：{dateStr}\n\n任务已同步自：/MyCancerData/";
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

        // 模式 A：连续拍照
        private async Task StartContinuousCameraLoop()
        {
            while (_isInContinuousMode)
            {
                await Task.Delay(500); // 稍微延时防卡
                bool taken = await CapturePhotoAsync();
                if (!taken) continue; // 用户取消则继续循环
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
                        BtnBackFromB.IsVisible = true; // 拍完显示返回按钮
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
            // 保存进度到手机存储 (断点续传)
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