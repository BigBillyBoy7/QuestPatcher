﻿using Avalonia.Controls;
using QuestPatcher.Models;
using QuestPatcher.Views;
using Serilog.Core;
using System;
using System.Diagnostics;
using System.IO;
using ReactiveUI;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;

namespace QuestPatcher.ViewModels
{
    public class ToolsViewModel : ViewModelBase
    {
        public Config Config { get; }

        public ProgressViewModel ProgressView { get; }

        public OperationLocker Locker { get; }

        public string AdbButtonText => _isAdbLogging ? "Stop ADB Log" : "Start ADB Log";

        public string VersionText => $"QuestPatcher v{VersionUtil.QuestPatcherVersion}";

        private bool _isAdbLogging;

        private readonly Window _mainWindow;
        private readonly SpecialFolders _specialFolders;
        private readonly Logger _logger;
        private readonly PatchingManager _patchingManager;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly InfoDumper _dumper;
        private readonly QuestPatcherService _qpService;
        
        internal OpenChangeAppMenuDelegate? OnChangeApp { get; set; }

        public ToolsViewModel(Config config, ProgressViewModel progressView, OperationLocker locker, MainWindow mainWindow, SpecialFolders specialFolders,
                            Logger logger, PatchingManager patchingManager, AndroidDebugBridge debugBridge, InfoDumper dumper, QuestPatcherService qpService)
        {
            Config = config;
            ProgressView = progressView;
            Locker = locker;

            _mainWindow = mainWindow;
            _specialFolders = specialFolders;
            _logger = logger;
            _patchingManager = patchingManager;
            _debugBridge = debugBridge;
            _dumper = dumper;
            _qpService = qpService;

            _debugBridge.StoppedLogging += (_, _) =>
            {
                _logger.Information("ADB log exited");
                _isAdbLogging = false;
                this.RaisePropertyChanged(nameof(AdbButtonText));
            };
        }

        public async void UninstallApp()
        {
            try
            {
                DialogBuilder builder = new()
                {
                    Title = "Are you sure?",
                    Text = "Uninstalling your app will exit QuestPatcher, as it requires your app to be installed. If you ever reinstall your app, reopen QuestPatcher and you can repatch"
                };
                builder.OkButton.Text = "Uninstall App";
                if (await builder.OpenDialogue(_mainWindow))
                {
                    Locker.StartOperation();
                    try
                    {
                        _logger.Information("Uninstalling app . . .");
                        await _patchingManager.UninstallCurrentApp();
                    }
                    finally
                    {
                        Locker.FinishOperation();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to uninstall app: {ex}");
            }
        }

        public void OpenLogsFolder()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _specialFolders.LogsFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        public async void QuickFix()
        {
            Locker.StartOperation(true); // ADB is not available during a quick fix, as we redownload platform-tools
            try
            {
                await _qpService.QuickFix();
                _logger.Information("Done!");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to clear cache: {ex}");
                DialogBuilder builder = new()
                {
                    Title = "Failed to clear cache",
                    Text = "Running the quick fix failed due to an unhandled error",
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }   finally
            {
                Locker.FinishOperation();
            }
        }

        public async void ToggleAdbLog()
        {
            if(_isAdbLogging)
            {
                _debugBridge.StopLogging();
            }
            else
            {
                _logger.Information("Starting ADB log");
                await _debugBridge.StartLogging(Path.Combine(_specialFolders.LogsFolder, "adb.log"));

                _isAdbLogging = true;
                this.RaisePropertyChanged(nameof(AdbButtonText));
            }
        }

        public async void CreateDump()
        {
            Locker.StartOperation();
            try
            {
                // Create the dump in the default location (the data directory)
                string dumpLocation = await _dumper.CreateInfoDump();

                string? dumpFolder = Path.GetDirectoryName(dumpLocation);
                if (dumpFolder != null)
                {
                    // Open the dump's directory for convenience
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dumpFolder,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                // Show a dialog with any errors
                _logger.Error($"Failed to create dump: {ex}");
                DialogBuilder builder = new()
                {
                    Title = "Failed to create dump",
                    Text = "Creating the dump failed due to an unhandled error",
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public async void ChangeApp()
        {
            if (OnChangeApp != null)
            {
                await OnChangeApp(false);
            }
        }
    }
}
