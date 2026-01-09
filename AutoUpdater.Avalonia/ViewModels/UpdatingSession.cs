using CarinaStudio.AutoUpdate;
using CarinaStudio.AutoUpdate.Resolvers;
using CarinaStudio.Collections;
using CarinaStudio.IO;
using CarinaStudio.Logging;
using CarinaStudio.Net.Http;
using CarinaStudio.Threading;
using CarinaStudio.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace CarinaStudio.AutoUpdater.ViewModels
{
	/// <summary>
	/// View-model of application updater.
	/// </summary>
	class UpdatingSession : AutoUpdate.ViewModels.UpdatingSession
	{
		// Constants.
		const string HttpRefererHeader = "Referer";
		const string HttpUserAgentHeader = "User-Agent";
		
		
		// Static fields.
		static readonly HttpClientHandler BypassCertificateValidationHttpClientHandler = new()
		{
			ServerCertificateCustomValidationCallback = (_, _, _, _) => true
		};
		
		
		// Fields.
		bool isAppIconRefreshed;
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
			this.RefreshApplicationIconAutomatically = true;
			this.RefreshApplicationIconMessage = app.GetString("UpdatingSession.RefreshApplicationIcon");
		}
		
		
		/// <summary>
		/// Get or set whether certification validation can be bypassed or not.
		/// </summary>
		public bool BypassCertificateValidation
		{
			get;
			set
			{
				if (field == value)
					return;
				field = value;
				this.SetupPackageManifestSource();
			}
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
			if (this.PackageManifestUri?.LocalPath is not { } localPath)
				return base.CreatePackageResolver(source);
			var packageManifestName = Path.GetExtension(localPath).ToLower();
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
		/// Get or set value of HTTP User-Agent used for sending request.
		/// </summary>
		public string? HttpUserAgent
		{
			get;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				field = value;
				this.SetupPackageManifestSource();
				if (value is not null)
					this.PackageRequestHeaders[HttpUserAgentHeader] = value;
				else
					this.PackageRequestHeaders.Remove(HttpUserAgentHeader);
			}
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
			else if (property == IsRefreshingApplicationIconProperty)
			{
				if (this.IsRefreshingApplicationIcon)
					this.isAppIconRefreshed = true;
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
		/// Get or set HTTP referer used for sending request to get package manifest.
		/// </summary>
		public Uri? PackageManifestRequestHttpReferer
		{
			get;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				field = value;
				this.SetupPackageManifestSource();
			}
		}


		/// <summary>
		/// Get or set URI of package manifest.
		/// </summary>
		public Uri? PackageManifestUri
		{
			get;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				field = value;
				this.SetupPackageManifestSource();
			}
		}
		
		
		/// <summary>
		/// Get or set HTTP referer used for sending request to download package.
		/// </summary>
		public string? PackageRequestHttpReferer
		{
			get;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				field = value;
				if (value is not null)
					this.PackageRequestHeaders[HttpRefererHeader] = value;
				else
					this.PackageRequestHeaders.Remove(HttpRefererHeader);
			}
		}


		/// <summary>
		/// Get or set executable of process to wait for before updating.
		/// </summary>
		public string? ProcessExecutableToWaitFor
		{
			get;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				field = value;
			}
		}


		/// <summary>
		/// Get or set ID of process to wait for before updating.
		/// </summary>
		public int? ProcessIdToWaitFor
		{
			get;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.IsUpdating || this.IsUpdatingCompleted)
					throw new InvalidOperationException();
				field = value;
			}
		}
		
		
		// Setup source to get package manifest.
		void SetupPackageManifestSource()
		{
			if (this.PackageManifestUri is { } manifestUri)
			{
				var httpRequest = new HttpRequestMessage(HttpMethod.Get, manifestUri).Also(message =>
				{
					if (this.PackageManifestRequestHttpReferer is { } referer)
						message.Headers.Referrer = referer;
					if (this.HttpUserAgent is { } userAgent)
						message.Headers.UserAgent.Add(new ProductInfoHeaderValue(userAgent, null));
				});
				var messageHandler = this.BypassCertificateValidation
					? BypassCertificateValidationHttpClientHandler
					: null;
				this.PackageManifestSource = new HttpResponseStreamProvider(() => httpRequest, messageHandler: messageHandler);
			}
			else
				this.PackageManifestSource = null;
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
				else if (this.isAppIconRefreshed && Platform.IsMacOS)
					this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.UpdatingSucceeded.WithAppIconRefreshed.MacOS", appName));
				else
					this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.UpdatingSucceeded", appName));
			}
			else if (this.IsRefreshingApplicationIcon)
				this.SetValue(MessageProperty, this.Application.GetFormattedString("UpdatingSession.RefreshingApplicationIcon"));
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
							// ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
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
							var version = this.UpdatingInformationalVersion.Let(it =>
							{
								if (string.IsNullOrWhiteSpace(it))
									return this.UpdatingVersion?.ToString();
								return it;
							});
							// ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
							if (!string.IsNullOrWhiteSpace(version))
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
			if (this.ProcessIdToWaitFor is null && string.IsNullOrWhiteSpace(this.ProcessExecutableToWaitFor))
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
				var processId = this.ProcessIdToWaitFor.GetValueOrDefault();
				var processExecutable = this.ProcessExecutableToWaitFor;
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
								// ReSharper disable EmptyGeneralCatchClause
								catch
								{ }
								// ReSharper restore EmptyGeneralCatchClause
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
