using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CarinaStudio.AutoUpdater.ViewModels;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace CarinaStudio.AutoUpdater;

/// <summary>
/// Main window.
/// </summary>
class MainWindow : Window
{
	// Fields.
	bool isAppIconRefreshed;
	readonly ILogger logger;
	readonly SynchronizationContext synchronizationContext = SynchronizationContext.Current.AsNonNull();


	/// <summary>
	/// Initialize new <see cref="MainWindow"/> instance.
	/// </summary>
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UpdatingSession))]
	public MainWindow()
	{
		AvaloniaXamlLoader.Load(this);
		this.logger = App.Current.LoggerFactory.CreateLogger(nameof(MainWindow));
	}


	/// <inheritdoc/>
	protected override void OnClosed(EventArgs e)
	{
		this.synchronizationContext.PostDelayed(() => // [Workaround] Prevent application hanging on macOS
		{
			var app = (App)App.Current;
			this.logger.LogWarning("Application didn't exit yet");
			app.StartApplication();
			this.logger.LogWarning("Stop process directly");
			this.synchronizationContext.PostDelayed(Process.GetCurrentProcess().Kill, 1000);
		}, 1000);
		base.OnClosed(e);
	}


	/// <inheritdoc/>
	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (this.DataContext is ViewModels.UpdatingSession session && session.IsUpdating)
		{
			this.logger.LogWarning("Cancel updating by closing window");
			e.Cancel = true;
			if (!session.IsUpdatingCancelling)
				session.CancelUpdatingCommand.TryExecute();
		}
		base.OnClosing(e);
	}


	// Window opened.
	protected override async void OnOpened(EventArgs e)
	{
		// call base
		base.OnOpened(e);

		// wait for logger configuration
		var app = (App)App.Current;
		await app.WaitForLoggerReadyAsync();

		// start updating
		app.UpdateTaskBarProgress(this, TaskbarIconProgressState.Indeterminate, 0);
		if (this.DataContext is ViewModels.UpdatingSession session)
		{
			this.synchronizationContext.PostDelayed(async () =>
			{
				// wait for process
				try
				{
					await session.WaitForProcess();
				}
				catch (TaskCanceledException)
				{
					this.synchronizationContext.Post(this.Close);
					return;
				}

				// start updating
				if (!session.StartUpdatingCommand.TryExecute())
					this.synchronizationContext.Post(this.Close);
			}, 500);
		}
		else
			this.synchronizationContext.Post(this.Close);
	}


	// Called when property changed.
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == DataContextProperty)
		{
			(change.OldValue as ViewModels.UpdatingSession)?.Let(it =>
			{
				it.PropertyChanged -= this.OnSessionPropertyChanged;
			});
			(change.NewValue as ViewModels.UpdatingSession)?.Let(it =>
			{
				it.PropertyChanged += this.OnSessionPropertyChanged;
			});
		}
	}


	// Called when property of session changed.
	void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (this.DataContext is not ViewModels.UpdatingSession session)
			return;
		var app = (App)App.Current;
		switch (e.PropertyName)
		{
			case nameof(ViewModels.UpdatingSession.IsDownloadingPackage):
				if (session.IsDownloadingPackage)
					app.UpdateTaskBarProgress(this, TaskbarIconProgressState.Normal, 0);
				break;
			
			case nameof(ViewModels.UpdatingSession.IsInstallingPackage):
				if (session.IsInstallingPackage)
					app.UpdateTaskBarProgress(this, TaskbarIconProgressState.Normal, 0.5);
				break;
			
			case nameof(ViewModels.UpdatingSession.IsRefreshingApplicationIcon):
				this.isAppIconRefreshed = true;
				break;
			
			case nameof(ViewModels.UpdatingSession.IsWaitingForProcess):
				if (session.IsWaitingForProcess)
					app.UpdateTaskBarProgress(this, TaskbarIconProgressState.Indeterminate, 0);
				break;
			
			case nameof(ViewModels.UpdatingSession.IsUpdatingCompleted):
				this.synchronizationContext.Post(() =>
				{
					if (session.IsUpdatingSucceeded)
					{
						app.UpdateTaskBarProgress(this, TaskbarIconProgressState.None, 0);
						if (app.IsAppExecutableSpecified && (!this.isAppIconRefreshed || Platform.IsNotMacOS))
						{
							this.logger.LogWarning("Updating completed, close window to start application");
							this.Close();
							return;
						}
					}
					else if (session.IsUpdatingCancelled)
						app.UpdateTaskBarProgress(this, TaskbarIconProgressState.None, 0);
					else
						app.UpdateTaskBarProgress(this, TaskbarIconProgressState.Error, 1);
					this.Activate();
				});
				break;
			
			case nameof(ViewModels.UpdatingSession.ProgressPercentage):
				if (session.IsProgressAvailable)
				{
					if (session.IsDownloadingPackage)
						app.UpdateTaskBarProgress(this, TaskbarIconProgressState.Normal, session.ProgressPercentage / 200);
					else if (session.IsInstallingPackage)
						app.UpdateTaskBarProgress(this, TaskbarIconProgressState.Normal, 0.5 + session.ProgressPercentage / 200);
				}
				break;
		}
	}
}
