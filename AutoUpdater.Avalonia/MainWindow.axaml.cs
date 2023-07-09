using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CarinaStudio.AutoUpdater;

/// <summary>
/// Main window.
/// </summary>
class MainWindow : Window
{
	// Fields.
	readonly ILogger logger;
	readonly SynchronizationContext synchronizationContext = SynchronizationContext.Current.AsNonNull();


	/// <summary>
	/// Initialize new <see cref="MainWindow"/> instance.
	/// </summary>
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
	protected override void OnOpened(EventArgs e)
	{
		// call base
		base.OnOpened(e);

		// start updating
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
		if (e.PropertyName == nameof(ViewModels.UpdatingSession.IsUpdatingCompleted))
		{
			this.synchronizationContext.Post(() =>
			{
				if (this.DataContext is ViewModels.UpdatingSession session 
					&& session.IsUpdatingSucceeded
					&& ((App)App.Current).IsAppExecutableSpecified)
				{
					this.logger.LogWarning("Updating completed, close window to start application");
					this.Close();
				}
			});
		}
	}
}
