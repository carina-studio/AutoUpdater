using CarinaStudio.AutoUpdate;
using CarinaStudio.AutoUpdate.Resolvers;
using CarinaStudio.IO;
using CarinaStudio.Net;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
		CancellationTokenSource? processWaitingCancellationTokenSource;


		/// <summary>
		/// Initialize new <see cref="UpdatingSession"/> instance.
		/// </summary>
		/// <param name="app">Application.</param>
		public UpdatingSession(IApplication app) : base(app)
		{ }


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
				return new XmlPackageResolver() { Source = source };
			return new JsonPackageResolver() { Source = source };
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
		/// Wait for given process to be completed before starting updating.
		/// </summary>
		/// <param name="processId">Process ID.</param>
		/// <returns>Task of waiting.</returns>
		public async Task WaitForProcess(int processId)
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();
			if (this.IsWaitingForProcess)
				throw new InvalidOperationException();

			// find process
			var process = Global.Run(() =>
			{
				try
				{
					return Process.GetProcessById(processId);
				}
				catch(Exception ex)
				{
					this.Logger.LogError(ex, "Unable to get process to wait for");
					return null;
				}
			});
			if (process == null)
			{
				this.Logger.LogWarning($"Process {processId} not found");
				return;
			}

			// wait for completion
			this.IsWaitingForProcess = true;
			this.OnPropertyChanged(nameof(IsWaitingForProcess));
			using (process)
			{
				this.processWaitingCancellationTokenSource = new CancellationTokenSource();
				try
				{
					this.Logger.LogDebug($"Start waiting for process {processId}");
					await process.WaitForExitAsync(this.processWaitingCancellationTokenSource.Token);
					this.Logger.LogDebug($"Complete waiting for process {processId}");
				}
				catch (TaskCanceledException)
				{
					this.Logger.LogWarning($"Waiting for process {processId} has been cancelled");
				}
				catch (Exception ex)
				{
					this.Logger.LogError(ex, $"Error occurred while waiting for process {processId}");
				}
			}
			this.IsWaitingForProcess = false;
			this.OnPropertyChanged(nameof(IsWaitingForProcess));
		}
	}
}
