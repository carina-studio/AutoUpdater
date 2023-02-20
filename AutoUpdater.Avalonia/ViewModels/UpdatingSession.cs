using CarinaStudio.AutoUpdate;
using CarinaStudio.AutoUpdate.Resolvers;
using CarinaStudio.Collections;
using CarinaStudio.IO;
using CarinaStudio.Net;
using CarinaStudio.Threading;
using CarinaStudio.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CarinaStudio.AutoUpdater.ViewModels
{
	/// <summary>
	/// View-model of application updater.
	/// </summary>
	class UpdatingSession : AutoUpdate.ViewModels.UpdatingSession
	{
		// Fields.
		Uri? packageManifestUri;
		string? processExecutableToWaitFor;
		int? processIdToWaitFor;
		CancellationTokenSource? processWaitingCancellationTokenSource;
		readonly ScheduledAction updateMessageAction;


		/// <summary>
		/// Initialize new <see cref="UpdatingSession"/> instance.
		/// </summary>
		/// <param name="app">Application.</param>
		public UpdatingSession(IApplication app) : base(app)
		{
			this.updateMessageAction = new ScheduledAction(this.UpdateMessage);
			this.updateMessageAction.Execute();
		}


		/// <summary>
		/// Cancel process waiting.
		/// </summary>
		public void CancelWaitingForProcess()
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();
			if (!this.IsWaitingForProcess)
				return;

			// cancel
			this.processWaitingCancellationTokenSource?.Cancel();
		}


		// Create package resolver.
		protected override IPackageResolver CreatePackageResolver(IStreamProvider source)
		{
			if (source is not WebRequestStreamProvider wrStreamProvider)
				return base.CreatePackageResolver(source);
			var packageManifestName = Path.GetExtension(wrStreamProvider.RequestUri.LocalPath)?.ToLower();
			if (packageManifestName == ".xml")
				return new XmlPackageResolver(this.Application, this.ApplicationBaseVersion) { Source = source };
			return new JsonPackageResolver(this.Application, this.ApplicationBaseVersion) { Source = source };
		}


		// Dispose.
		protected override void Dispose(bool disposing)
		{
			this.processWaitingCancellationTokenSource?.Cancel();
			base.Dispose(disposing);
		}


		/// <summary>
		/// Check whether instance it waiting for process completion.
		/// </summary>
		public bool IsWaitingForProcess { get; private set; }


		// Property changed.
		protected override void OnPropertyChanged(ObservableProperty property, object? oldValue, object? newValue)
		{
			base.OnPropertyChanged(property, oldValue, newValue);
			if (property == DownloadedPackageSizeProperty
				|| property == IsUpdatingCancellingProperty
				|| property == PackageSizeProperty)
			{
				this.updateMessageAction.Schedule();
			}
		}


		// Called when state of updater changed.
		protected override void OnUpdaterStateChanged()
		{
			base.OnUpdaterStateChanged();
			this.updateMessageAction.Schedule();
		}


		/// <summary>
		/// Get or set URI of package manifest.
		/// </summary>
		public Uri? PackageManifestUri
		{
			get => this.packageManifestUri;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				this.packageManifestUri = value;
				this.PackageManifestSource = value != null
					? new WebRequestStreamProvider(value)
					: null;
			}
		}


		/// <summary>
		/// Get or set executable of process to wait for before updating.
		/// </summary>
		public string? ProcessExecutableToWaitFor
		{
			get => this.processExecutableToWaitFor;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				this.processExecutableToWaitFor = value;
			}
		}


		/// <summary>
		/// Get or set ID of process to wait for before updating.
		/// </summary>
		public int? ProcessIdToWaitFor
		{
			get => this.processIdToWaitFor;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				this.processIdToWaitFor = value;
			}
		}


		// Update message according to current state.
		void UpdateMessage()
		{
			if (this.IsDisposed)
				return;
			var appName = this.ApplicationName ?? this.Application.GetString("Common.Application");
			if (this.IsWaitingForProcess)
				this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.WaitingForProcess", appName));
			else if (this.IsUpdatingCompleted)
			{
				if (this.IsUpdatingCancelled)
					this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.UpdatingCancelled"));
				else if (this.IsUpdatingFailed)
					this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.UpdatingFailed", appName));
				else
					this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.UpdatingSucceeded", appName));
			}
			else
			{
				switch (this.UpdaterState)
				{
					case UpdaterState.BackingUpApplication:
						this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.BackingUpApplication", appName));
						break;
					case UpdaterState.DownloadingPackage:
						{
							var downloadSizeString = this.DownloadedPackageSize.ToFileSizeString();
							var packageSize = this.PackageSize.GetValueOrDefault();
							if (packageSize > 0)
								this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.DownloadingPackage", $"{downloadSizeString} / {packageSize.ToFileSizeString()}"));
							else
								this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.DownloadingPackage", downloadSizeString));
						}
						break;
					case UpdaterState.Initializing:
						this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.Initializing"));
						break;
					case UpdaterState.InstallingPackage:
						{
							var version = this.UpdatingVersion;
							if (version != null)
								this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.InstallingPackage.WithVersion", appName, version));
							else
								this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.InstallingPackage", appName));
						}
						break;
					case UpdaterState.ResolvingPackage:
						this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.Preparing"));
						break;
					case UpdaterState.RestoringApplication:
						this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.RestoringApplication", appName));
						break;
					case UpdaterState.VerifyingPackage:
						this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.VerifyingPackage"));
						break;
					default:
						this.SetValue(MessageProperty, " ");
						break;
				}
			}
		}


		/// <summary>
		/// Wait for process specified by <see cref="ProcessIdToWaitFor"/> to be completed before starting updating.
		/// </summary>
		/// <returns>Task of waiting.</returns>
		public async Task WaitForProcess()
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();
			if (this.processIdToWaitFor == null && string.IsNullOrWhiteSpace(this.processExecutableToWaitFor))
				return;
			if (this.IsWaitingForProcess)
				throw new InvalidOperationException();

			// wait for process
			this.IsWaitingForProcess = true;
			this.OnPropertyChanged(nameof(IsWaitingForProcess));
			this.updateMessageAction.Schedule();
			try
			{
				// find processes
				this.processWaitingCancellationTokenSource = new CancellationTokenSource();
				var processId = this.processIdToWaitFor.GetValueOrDefault();
				var processExecutable = this.processExecutableToWaitFor;
				var processes = await Task.Run(() =>
				{
					// get by PID
					var processes = new Dictionary<int, Process>();
					if (processId != 0)
					{
						try
						{
							Process.GetProcessById(processId).Let(it =>
							{
								processes[it.Id] = it;
							});
						}
						catch (Exception ex)
						{
							this.Logger.LogError(ex, "Unable to get process {processId} to wait for", processId);
						}
					}

					// get by executable
					if (!string.IsNullOrWhiteSpace(processExecutable))
					{
						try
						{
							var comparer = PathEqualityComparer.Default;
							var exeFileName = Path.GetFileNameWithoutExtension(processExecutable);
							foreach (var process in Process.GetProcesses())
							{
								try
								{
									if ((!Platform.IsWindows || comparer.Equals(exeFileName, process.ProcessName))
										&& comparer.Equals(processExecutable, process.MainModule?.FileName))
									{
										processes[process.Id] = process;
									}
								}
								catch
								{ }
								if (this.processWaitingCancellationTokenSource.IsCancellationRequested)
									break;
							}
						}
						catch (Exception ex)
						{
							this.Logger.LogError(ex, "Unable to get process '{processExecutable}' to wait for", processExecutable);
						}
					}
					return processes;
				});
				if (this.processWaitingCancellationTokenSource.IsCancellationRequested)
					throw new TaskCanceledException();
				if (processes.IsEmpty())
				{
					this.Logger.LogWarning($"No process to wait for");
					return;
				}

				// wait for completion
				this.Logger.LogDebug("Start waiting for {processCount} process(es)", processes.Count);
				foreach (var process in processes.Values)
				{
					try
					{
						using (process)
						{
							this.Logger.LogDebug("Start waiting for process {processId}", processId);
							await process.WaitForExitAsync(this.processWaitingCancellationTokenSource.Token);
							this.Logger.LogDebug("Complete waiting for process {processId}", processId);
						}
					}
					catch (Exception ex)
					{
						if (ex is TaskCanceledException)
						{
							this.Logger.LogWarning("Waiting for process {processId} has been cancelled", processId);
							throw;
						}
						this.Logger.LogError(ex, "Error occurred while waiting for process {processId}", processId);
					}
				}
			}
			finally
			{
				this.IsWaitingForProcess = false;
				this.OnPropertyChanged(nameof(IsWaitingForProcess));
				this.updateMessageAction.Schedule();
			}
		}
	}
}
