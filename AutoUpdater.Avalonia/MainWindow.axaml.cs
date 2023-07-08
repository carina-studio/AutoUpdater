using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace CarinaStudio.AutoUpdater;

/// <summary>
/// Main window.
/// </summary>
class MainWindow : Window
{
	// Fields.
	readonly SynchronizationContext synchronizationContext = SynchronizationContext.Current.AsNonNull();


	/// <summary>
	/// Initialize new <see cref="MainWindow"/> instance.
	/// </summary>
	public MainWindow()
	{
		InitializeComponent();
	}


	// Initialize.
	private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


	// Closing window.
	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (this.DataContext is ViewModels.UpdatingSession session && session.IsUpdating)
		{
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
					this.Close();
				}
			});
		}
	}
}
