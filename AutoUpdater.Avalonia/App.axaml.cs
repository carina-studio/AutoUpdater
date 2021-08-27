using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Themes.Fluent;
using CarinaStudio.AutoUpdater.ViewModels;
using CarinaStudio.Configuration;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace CarinaStudio.AutoUpdater
{
	/// <summary>
	/// Application.
	/// </summary>
	class App : Application, IApplication
	{
		// Constants.
		const int EXIT_CODE_INVALID_ARGUMENT = 1;


		// Fields.
		string? appDirectoryPath;
		string? appExePath;
		string? appName;
		bool darkMode;
		readonly ILogger logger;
		Uri? packageManifestUri;
		int? processIdToWaitFor;
		volatile SynchronizationContext? synchronizationContext;
		UpdatingSession? updatingSession;


		/// <summary>
		/// Initialize new <see cref="App"/> instance.
		/// </summary>
		public App()
		{
			this.logger = this.LoggerFactory.CreateLogger("App");
			this.RootPrivateDirectoryPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? throw new InvalidOperationException();
		}

		
		// Initialize.
		public override void Initialize() => AvaloniaXamlLoader.Load(this);


		// Program entry.
		public static void Main(string[] args)
		{
			// start updating
			AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.LogToTrace().Also(it =>
				{
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
						it.With(new X11PlatformOptions());
				}).StartWithClassicDesktopLifetime(args);

			// start application
			var app = (App)App.Current;
			if (app.updatingSession?.IsUpdatingCompleted == true && !string.IsNullOrWhiteSpace(app.appExePath))
			{
				try
				{
					// mark file as executable
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
					{
						try
						{
							using var chmodeProcess = Process.Start(new ProcessStartInfo()
							{
								Arguments = $"-c chmod +x \"{app.appExePath}\"",
								CreateNoWindow = true,
								FileName = "/bin/bash",
								RedirectStandardOutput = true,
								UseShellExecute = false,
								WindowStyle = ProcessWindowStyle.Hidden,
							});
							if (chmodeProcess != null)
								chmodeProcess.WaitForExit(5000);
							else
								app.logger.LogError($"Unable to mark '{app.appExePath}' as executable");
						}
						catch (Exception ex)
						{
							app.logger.LogError(ex, $"Unable to mark '{app.appExePath}' as executable");
						}
					}

					// start application
					using var process = Process.Start(app.appExePath.AsNonNull());
				}
				catch (Exception ex)
				{
					app.logger.LogError(ex, $"Unable to start application '{app.appExePath}'");
				}
			}
		}


		// Called when Avalonia initialized.
		public override void OnFrameworkInitializationCompleted()
		{
			// setup thread
			this.synchronizationContext = SynchronizationContext.Current;

			// call base
			base.OnFrameworkInitializationCompleted();

			// parse arguments
			var desktopLifetime = (IClassicDesktopStyleApplicationLifetime)this.ApplicationLifetime;
			if (!this.ParseArgs(desktopLifetime.Args))
			{
				this.SynchronizationContext.Post(() => desktopLifetime.Shutdown(EXIT_CODE_INVALID_ARGUMENT));
				return;
			}

			// load strings
			if (this.CultureInfo.Name != "en-US")
			{
				var stringResources = (ResourceInclude?)null;
				try
				{
					stringResources = new ResourceInclude()
					{
						Source = new Uri($"avares://AutoUpdater.Avalonia/Strings/{this.CultureInfo.Name}.axaml")
					};
					_ = stringResources.Loaded; // trigger error if resource not found
					this.logger.LogInformation($"Load strings for {this.CultureInfo.Name}");
				}
				catch
				{
					stringResources = null;
					this.logger.LogWarning($"No strings for {this.CultureInfo.Name}");
					return;
				}
				this.Resources.MergedDictionaries.Add(stringResources);
			}

			// load styles
			this.Styles.Add(new FluentTheme(new Uri("avares://AutoUpdater.Avalonia"))
			{
				Mode = this.darkMode ? FluentThemeMode.Dark : FluentThemeMode.Light
			});

			// create updating session
			this.updatingSession = new UpdatingSession(this)
			{
				ApplicationDirectoryPath = this.appDirectoryPath,
				ApplicationName = this.appName,
				PackageManifestUri = this.packageManifestUri,
				ProcessIdToWaitFor = this.processIdToWaitFor,
			};

			// show main window
			new MainWindow() { DataContext = this.updatingSession }.Show();
		}


		// Parse arguments.
		bool ParseArgs(string[] args)
		{
			var argCount = args.Length;
			for (var i = 0; i < argCount; ++i)
			{
				switch (args[i])
				{
					case "-culture":
						if (i < argCount - 1)
						{
							try
							{
								this.CultureInfo = CultureInfo.GetCultureInfo(args[++i]);
							}
							catch 
							{
								this.logger.LogWarning($"Invalid culture name: {args[i]}");
							}
						}
						else
							this.logger.LogError("No culture name specified");
						break;
					case "-dark-mode":
						this.darkMode = true;
						break;
					case "-directory":
						if (i < argCount - 1)
						{
							if (this.appDirectoryPath == null)
								this.appDirectoryPath = args[++i];
							else
							{
								this.logger.LogError("Duplicate application directory specified");
								return false;
							}
						}
						break;
					case "-executable":
						if (i < argCount - 1)
						{
							if (this.appExePath == null)
								this.appExePath = args[++i];
							else
							{
								this.logger.LogError("Duplicate application executable specified");
								return false;
							}
						}
						break;
					case "-name":
						if (i < argCount - 1)
						{
							if (this.appName == null)
								this.appName = args[++i];
							else
							{
								this.logger.LogError("Duplicate application name specified");
								return false;
							}
						}
						break;
					case "-package-manifest":
						if (i < argCount - 1)
						{
							if (this.packageManifestUri != null)
							{
								this.logger.LogError("Duplicate package manifest URI specified");
								return false;
							}
							else if (Uri.TryCreate(args[++i], UriKind.Absolute, out var uri))
								this.packageManifestUri = uri;
							else
							{
								this.logger.LogError($"Invalid package manifest URI: {args[i]}");
								return false;
							}
						}
						break;
					case "-wait-for-process":
						if (i < argCount - 1)
						{
							if (this.processIdToWaitFor != null)
							{
								this.logger.LogError("Duplicate process ID specified");
								return false;
							}
							else if (int.TryParse(args[++i], out var pid))
								this.processIdToWaitFor = pid;
							else
							{
								this.logger.LogError($"Invalid process ID: {args[i]}");
								return false;
							}
						}
						break;
				}
			}
			if (string.IsNullOrWhiteSpace(this.appDirectoryPath))
			{
				this.logger.LogError("No application directory specified");
				return false;
			}
			if (this.packageManifestUri == null)
			{
				this.logger.LogError("No package manifest URI specified");
				return false;
			}
			return true;
		}


		// Implementations.
		public Assembly Assembly { get; } = Assembly.GetExecutingAssembly();
		public CultureInfo CultureInfo { get; private set; } = CultureInfo.CurrentCulture;
		public string? GetString(string key, string? defaultValue = null)
		{
			if (this.Resources.TryGetResource($"String.{key}", out var value) && value is string str)
				return str;
			return defaultValue;
		}
		public bool IsShutdownStarted { get; private set; }
		public ILoggerFactory LoggerFactory { get; } = new LoggerFactory();
		public ISettings PersistentState { get; } = new MemorySettings();
		public string RootPrivateDirectoryPath { get; }
		public ISettings Settings { get; } = new MemorySettings();
		public event EventHandler? StringsUpdated;
		public SynchronizationContext SynchronizationContext { get => this.synchronizationContext ?? throw new InvalidOperationException(); }
	}
}
