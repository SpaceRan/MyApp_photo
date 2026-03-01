using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
#endif

namespace MyApp
{
    public partial class MainPage : ContentPage
    {
        private string BaseFolderPath => Path.Combine(FileSystem.AppDataDirectory, "MyCancerData");
        private const string AlbumName = "MyCancerData";
        private const string FileName_Tasks = "tasks.txt";
        private const string FileName_Search = "search_data.txt";
        private const string PrefKey_TaskIndex = "last_task_index_v1";
        
        private List<string> _taskList = new List<string>();
        private List<string> _searchList = new List<string>();
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

        private async void InitializeAppAsync()
        {
            try
            {
                if (!Directory.Exists(BaseFolderPath))
                    Directory.CreateDirectory(BaseFolderPath);

                await CopySingleFileAsync(FileName_Tasks);
                await CopySingleFileAsync(FileName_Search);

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

                _currentTaskIndex = Preferences.Default.Get(PrefKey_TaskIndex, 0);
                if (_currentTaskIndex >= _taskList.Count && _taskList.Count > 0)
                {
                    _currentTaskIndex = 0;
                    Preferences.Default.Set(PrefKey_TaskIndex, 0);
                }

                UpdateTaskDisplay();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Init Error", ex.Message, "OK");
            }
        }

        private async Task CopySingleFileAsync(string fileName)
        {
            string destPath = Path.Combine(BaseFolderPath, fileName);
            using var inputStream = await FileSystem.OpenAppPackageFileAsync(fileName);
            using var outputStream = File.Create(destPath);
            await inputStream.CopyToAsync(outputStream);
        }

        private async Task ShowWelcomeAlert()
        {
            await DisplayAlert("Welcome", $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nDir: {BaseFolderPath}", "Start");
        }

        private void ResetUI()
        {
            FrameModeB.IsVisible = false;
            FrameModeC.IsVisible = false;
            EntrySearch.Text = string.Empty;
            LabelSearchResult.Text = string.Empty;
            LabelSearchResult.TextColor = Colors.Black;
        }

        private async void OnModeA_Clicked(object sender, EventArgs e)
        {
            ResetUI();
            _isInContinuousMode = true;
            await DisplayAlert("Mode A", "Continuous mode started", "OK");
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

        private async Task StartContinuousCameraLoop()
        {
            while (_isInContinuousMode)
            {
                bool taken = await CapturePhotoAsync();
                if (!taken || !_isInContinuousMode) break;
                await Task.Delay(300);
            }
        }

        private async void OnSearchExecute_Clicked(object sender, EventArgs e)
        {
            string keyword = EntrySearch.Text?.Trim();
            
            if (string.IsNullOrEmpty(keyword))
            {
                LabelSearchResult.Text = "Warning: Empty input";
                LabelSearchResult.TextColor = Colors.Orange;
                return;
            }

            bool exists = _searchList.Contains(keyword);

            if (exists)
            {
                LabelSearchResult.Text = "Warning: Already exists!";
                LabelSearchResult.TextColor = Colors.Red;
                EntrySearch.Text = string.Empty;
                EntrySearch.Focus();
            }
            else
            {
                LabelSearchResult.Text = "Success: Not found";
                LabelSearchResult.TextColor = Colors.Green;
                
                await Task.Delay(500);
                await CapturePhotoAsync();
                
                EntrySearch.Text = string.Empty;
                EntrySearch.Focus();
            }
        }

        private void UpdateTaskDisplay()
        {
            if (_taskList.Count == 0)
            {
                LabelTaskText.Text = "Task list is empty.";
                LabelTaskText.TextColor = Colors.Red;
                return;
            }

            if (_currentTaskIndex >= _taskList.Count)
            {
                _currentTaskIndex = 0;
                Preferences.Default.Set(PrefKey_TaskIndex, 0);
                LabelTaskText.Text = "All tasks completed! Reset to 1.";
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

        private async Task<bool> CapturePhotoAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                    if (status != PermissionStatus.Granted)
                    {
                        await DisplayAlert("Permission Error", "Camera permission is required.", "OK");
                        return false;
                    }
                }

                var photo = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions { Title = "CaseCapture" });
                if (photo == null)
                    return false;

                await SavePhotoToGalleryAsync(photo);
                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Camera Error", ex.Message, "OK");
                return false;
            }
        }

        private async Task SavePhotoToGalleryAsync(FileResult photo)
        {
            try
            {
#if ANDROID
                var context = Platform.CurrentActivity ?? Android.App.Application.Context;
                var resolver = context.ContentResolver;

                string fileName = $"case_{DateTime.Now:yyyyMMddHHmmss_fff}.jpg";

                var contentValues = new ContentValues();
                contentValues.Put(MediaStore.Images.Media.InterfaceConsts.DisplayName, fileName);
                contentValues.Put(MediaStore.Images.Media.InterfaceConsts.MimeType, "image/jpeg");
                contentValues.Put(MediaStore.Images.Media.InterfaceConsts.RelativePath, $"Pictures/{AlbumName}");
                contentValues.Put(MediaStore.Images.Media.InterfaceConsts.IsPending, 1);

                var uri = resolver.Insert(MediaStore.Images.Media.ExternalContentUri, contentValues);
                if (uri == null)
                    throw new Exception("Failed to create MediaStore entry");

                using (var sourceStream = await photo.OpenReadAsync())
                using (var outputStream = resolver.OpenOutputStream(uri))
                {
                    await sourceStream.CopyToAsync(outputStream);
                    await outputStream.FlushAsync();
                }

                contentValues.Clear();
                contentValues.Put(MediaStore.Images.Media.InterfaceConsts.IsPending, 0);
                resolver.Update(uri, contentValues, null, null);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (LabelPhotoCount != null)
                        LabelPhotoCount.Text = "Photo saved";
                });
#else
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (LabelPhotoCount != null)
                        LabelPhotoCount.Text = "Supported on Android 10+ only";
                });
#endif
            }
            catch (Exception ex)
            {
                await DisplayAlert("Save Error", ex.Message, "OK");
            }
        }

        private async void OnTakePhoto_Clicked(object sender, EventArgs e)
        {
            await CapturePhotoAsync();
        }
    }
}
