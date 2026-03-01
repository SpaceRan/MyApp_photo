using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace MyApp
{
    public partial class MainPage : ContentPage
    {
        private string BaseFolderPath => Path.Combine(FileSystem.AppDataDirectory, "MyCancerData");
        private const string FileName_Tasks = "tasks.txt";
        private const string FileName_Search = "search_data.txt";
        private const string PrefKey_TaskIndex = "last_task_index_v1";
        
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
            await ShowWelcomeAlert();
        }

        protected override bool OnBackButtonPressed()
        {
            if (_isInContinuousMode)
            {
                _isInContinuousMode = false;
                MainThread.BeginInvokeOnMainThread(() => ResetUI());
                return true;
            }
            return base.OnBackButtonPressed();
        }

        #region 1. 初始化

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

                UpdateTaskDisplay();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("启动错误", $"初始化失败：{ex.Message}", "确定");
            }
        }

        private async Task ForceCopyFromRawAsync()
        {
            if (!Directory.Exists(BaseFolderPath))
                Directory.CreateDirectory(BaseFolderPath);

            await CopySingleFileAsync(FileName_Tasks);
            await CopySingleFileAsync(FileName_Search);
        }

        private async Task CopySingleFileAsync(string fileName)
        {
            string destPath = Path.Combine(BaseFolderPath, fileName);
            using var inputStream = await FileSystem.OpenAppPackageFileAsync(fileName);
            using var outputStream = File.Create(destPath);
            await inputStream.CopyToAsync(outputStream);
        }

        private async Task LoadDataFromLocalFileAsync()
        {
            _taskList.Clear();
            _searchList.Clear();
            
            string tasksPath = Path.Combine(BaseFolderPath, FileName_Tasks);
            string searchPath = Path.Combine(BaseFolderPath, FileName_Search);

            if (File.Exists(tasksPath))
            {
                var lines = await File.ReadAllLinesAsync(tasksPath);
                foreach (var line in lines)
                    if (!string.IsNullOrWhiteSpace(line))
                        _taskList.Add(line.Trim());
            }

            if (File.Exists(searchPath))
            {
                var lines = await File.ReadAllLinesAsync(searchPath);
                foreach (var line in lines)
                    if (!string.IsNullOrWhiteSpace(line))
                        _searchList.Add(line.Trim());
            }
        }

        #endregion

        #region 2. 欢迎弹窗

        private async Task ShowWelcomeAlert()
        {
            string msg = $"你会成为最好的开发者\n日期：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n数据目录：{BaseFolderPath}";
            await DisplayAlertAsync("欢迎", msg, "开始工作");
        }

        #endregion

        #region 3. 界面控制

        private void ResetUI()
        {
            FrameModeB.IsVisible = false;
            FrameModeC.IsVisible = false;
            EntrySearch.Text = "";
            LabelSearchResult.Text = "";
            LabelSearchResult.TextColor = Colors.Black;
        }

        private async void OnModeA_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            _isInContinuousMode = true;
            await DisplayAlertAsync("连续拍照模式", "进入后可自由拍照\n按物理返回键退出", "确定");
            await StartContinuousCameraLoop();
        }

        private void OnModeB_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            FrameModeB.IsVisible = true;
            EntrySearch.Focus();
        }

        private void OnModeC_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            FrameModeC.IsVisible = true;
            UpdateTaskDisplay();
        }

        #endregion

        #region 4. 业务逻辑 

        // 模式 A：连续拍照（自由模式，按返回键退出）
        private async Task StartContinuousCameraLoop()
        {
            while (_isInContinuousMode)
            {
                bool taken = await CapturePhotoAsync();
                if (!taken || !_isInContinuousMode) break;
                await Task.Delay(300);
            }
        }

        // 模式 B：搜索逻辑
        private async void OnSearchExecute_Clicked(object sender, EventArgs e)
        {
            string keyword = EntrySearch.Text?.Trim();
            
            if (string.IsNullOrEmpty(keyword))
            {
                LabelSearchResult.Text = "⚠ 请输入搜索内容";
                LabelSearchResult.TextColor = Colors.Orange;
                return;
            }

            bool exists = _searchList.Contains(keyword);

            if (exists)
            {
                LabelSearchResult.Text = "⚠ 已经有了！";
                LabelSearchResult.TextColor = Colors.Red;
                EntrySearch.Text = "";
                EntrySearch.Focus();
            }
            else
            {
                LabelSearchResult.Text = "✅ 没有！已拍照记录";
                LabelSearchResult.TextColor = Colors.Green;
                
                await Task.Delay(500);
                await CapturePhotoAsync();
                
                EntrySearch.Text = "";
                EntrySearch.Focus();
            }
        }

        // 模式 C：任务显示
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
            UpdateTaskDisplay();
        }

        #endregion

        #region 5. 相机逻辑
        private async Task<bool> CapturePhotoAsync()
        {
            try
            {
                // 1. 权限检查
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                    if (status != PermissionStatus.Granted)
                    {
                        await DisplayAlertAsync("权限错误", "需要相机权限才能拍照", "确定");
                        return false;
                    }
                }

                // 2. 拍照
                var photo = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions { Title = "CaseCapture" });
                if (photo == null)
                    return false;

                // ✅ 3. 关键：保存到应用目录（您原来缺少这行！）
                await SavePhotoToAppFolderAsync(photo);

                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("相机错误", ex.Message, "确定");
                return false;
            }
        }

        private async Task SavePhotoToAppFolderAsync(FileResult photo)
        {
            try
            {
                if (!Directory.Exists(BaseFolderPath))
                    Directory.CreateDirectory(BaseFolderPath);
                string fileName = $"case_{DateTime.Now:yyyyMMddHHmmss}.jpg";
                string destPath = Path.Combine(BaseFolderPath, fileName);
                using var sourceStream = await photo.OpenReadAsync();
                using var destStream = File.Create(destPath);
                await sourceStream.CopyToAsync(destStream);
                await DisplayAlertAsync("保存成功", $"照片已保存到：\n{destPath}", "确定");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("保存错误", $"无法保存照片：{ex.Message}", "确定");
            }
        }

        private async void OnTakePhoto_Clicked(object sender, EventArgs e)
        {
            await CapturePhotoAsync();
        }

        #endregion
    }
}