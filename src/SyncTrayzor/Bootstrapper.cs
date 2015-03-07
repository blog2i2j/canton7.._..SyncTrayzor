﻿using CefSharp;
using NLog;
using Stylet;
using StyletIoC;
using SyncTrayzor.NotifyIcon;
using SyncTrayzor.Pages;
using SyncTrayzor.Properties;
using SyncTrayzor.Services;
using SyncTrayzor.Services.UpdateChecker;
using SyncTrayzor.SyncThing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SyncTrayzor
{
    public class Bootstrapper : Bootstrapper<ShellViewModel>
    {
        protected override void ConfigureIoC(IStyletIoCBuilder builder)
        {
            builder.Bind<IApplicationState>().ToInstance(new ApplicationState(this.Application));
            builder.Bind<IConfigurationProvider>().To<ConfigurationProvider>().InSingletonScope();
            builder.Bind<IAutostartProvider>().To<AutostartProvider>().InSingletonScope();
            builder.Bind<ConfigurationApplicator>().ToSelf().InSingletonScope();
            builder.Bind<ISyncThingApiClient>().To<SyncThingApiClient>().InSingletonScope();
            builder.Bind<ISyncThingEventWatcher>().To<SyncThingEventWatcher>().InSingletonScope();
            builder.Bind<ISyncThingProcessRunner>().To<SyncThingProcessRunner>().InSingletonScope();
            builder.Bind<ISyncThingManager>().To<SyncThingManager>().InSingletonScope();
            builder.Bind<ISyncThingConnectionsWatcher>().To<SyncThingConnectionsWatcher>().InSingletonScope();
            builder.Bind<INotifyIconManager>().To<NotifyIconManager>().InSingletonScope();
            builder.Bind<IWatchedFolderMonitor>().To<WatchedFolderMonitor>().InSingletonScope();
            builder.Bind<IGithubApiClient>().To<GithubApiClient>().InSingletonScope();
            builder.Bind<IUpdateChecker>().To<UpdateChecker>().InSingletonScope();
        }

        protected override void Configure()
        {
            var configurationProvider = this.Container.Get<IConfigurationProvider>();
            // Debug builds are always 'portable'
#if DEBUG
            configurationProvider.IsPortableMode = true;
#else
            configurationProvider.IsPortableMode = Settings.Default.PortableMode;
#endif
            configurationProvider.EnsureEnvironmentConsistency();

            GlobalDiagnosticsContext.Set("LogFilePath", configurationProvider.LogFilePath);

            var autostartProvider = this.Container.Get<IAutostartProvider>();
#if DEBUG
            autostartProvider.IsEnabled = false;
#endif

            if (autostartProvider.CanWrite)
            {
                // If it's not in portable mode, and if we had to create config (i.e. it's the first start ever), then enable autostart
                // Else, keep the config as it was, but update the path to us (if we're not in debug)
                if (!configurationProvider.IsPortableMode && configurationProvider.HadToCreateConfiguration)
                    autostartProvider.SetAutoStart(new AutostartConfiguration() { AutoStart = true, StartMinimized = true });
                else
                    autostartProvider.UpdatePathToSelf();
            }

            var notifyIconManager = this.Container.Get<INotifyIconManager>();
            notifyIconManager.Setup((INotifyIconDelegate)this.RootViewModel);
            this.Container.Get<ConfigurationApplicator>().ApplyConfiguration();

            // Horrible workaround for a CefSharp crash on logout/shutdown
            // https://github.com/cefsharp/CefSharp/issues/800#issuecomment-75058534
            this.Application.SessionEnding += (o, e) => Process.GetCurrentProcess().Kill();
        }

        protected override void Launch()
        {
            if (this.Args.Contains("-minimized"))
                this.Container.Get<INotifyIconManager>().EnsureIconVisible();
            else
                base.Launch();
        }

        protected override void OnLaunch()
        {
            var config = this.Container.Get<IConfigurationProvider>().Load();
            if (config.StartSyncthingAutomatically && !this.Args.Contains("-noautostart"))
                ((ShellViewModel)this.RootViewModel).Start();

            // We don't care if this fails
            if (config.NotifyOfNewVersions)
                this.Container.Get<IUpdateChecker>().CheckForUpdatesAsync();
        }

        protected override void OnUnhandledException(DispatcherUnhandledExceptionEventArgs e)
        {
            var windowManager = this.Container.Get<IWindowManager>();
            var logger = LogManager.GetCurrentClassLogger();
            logger.Error("An unhandled exception occurred", e.Exception);

            var configurationException = e.Exception as ConfigurationException;
            if (configurationException != null)
            {
                windowManager.ShowMessageBox(String.Format("Configuration Error: {0}", configurationException.Message), "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            }
            else
            {
                var vm = this.Container.Get<UnhandledExceptionViewModel>();
                vm.Exception = e.Exception;
                windowManager.ShowDialog(vm);
            }
        }
    }
}
