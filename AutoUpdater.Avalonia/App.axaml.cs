using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using CarinaStudio.AutoUpdater.ViewModels;
using CarinaStudio.Configuration;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
using Mono.Unix;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

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
		Color? accentColor;
		string? appDirectoryPath;
		string? appExeArgs;
		string? appExePath;
		string? appName;
		CultureInfo cultureInfo = CultureInfo.CurrentCulture;
		bool darkMode;
		readonly ILogger logger;
		Uri? packageManifestUri;
		int? processIdToWaitFor;
		UpdatingSession? updatingSession;


		/// <summary>
		/// Initialize new <see cref="App"/> instance.
		/// </summary>
		public App()
		{
			this.logger = this.LoggerFactory.CreateLogger("App");
		}

		
		// Initialize.
		public override void Initialize() => AvaloniaXamlLoader.Load(this);


		/// <summary>
		/// Check whether executable of application has been specified or not.
		/// </summary>
		public bool IsAppExecutableSpecified { get => !string.IsNullOrWhiteSpace(this.appExePath); }


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
			if (app.updatingSession?.IsUpdatingCompleted == true && app.IsAppExecutableSpecified)
			{
				try
				{
					// mark file as executable
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
					{
						try
						{
							var fileInfo = new UnixFileInfo(app.appExePath.AsNonNull());
							if (fileInfo.Exists)
								fileInfo.FileAccessPermissions |= (FileAccessPermissions.UserExecute | FileAccessPermissions.GroupExecute);
							else
								app.logger.LogError($"Cannot find executable '{app.appExePath}'");
						}
						catch (Exception ex)
						{
							app.logger.LogError(ex, $"Unable to mark '{app.appExePath}' as executable");
						}
					}

					// start application
					using var process = Process.Start(new ProcessStartInfo()
					{
						Arguments = app.appExeArgs ?? "",
						FileName = app.appExePath.AsNonNull(),
					});
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

			// apply accent color
			this.accentColor?.Let(accentColor =>
			{
				Color GammaTransform(Color color, double gamma)
				{
					double r = (color.R / 255.0);
					double g = (color.G / 255.0);
					double b = (color.B / 255.0);
					return Color.FromArgb(color.A, (byte)(Math.Pow(r, gamma) * 255 + 0.5), (byte)(Math.Pow(g, gamma) * 255 + 0.5), (byte)(Math.Pow(b, gamma) * 255 + 0.5));
				}
				var sysAccentColorDark1 = GammaTransform(accentColor, 2.8);
				var sysAccentColorLight1 = GammaTransform(accentColor, 0.682);
				this.Resources["SystemAccentColor"] = accentColor;
				this.Resources["SystemAccentColorDark1"] = sysAccentColorDark1;
				this.Resources["SystemAccentColorDark2"] = GammaTransform(accentColor, 4.56);
				this.Resources["SystemAccentColorDark3"] = GammaTransform(accentColor, 5.365);
				this.Resources["SystemAccentColorLight1"] = sysAccentColorLight1;
				this.Resources["SystemAccentColorLight2"] = GammaTransform(accentColor, 0.431);
				this.Resources["SystemAccentColorLight3"] = GammaTransform(accentColor, 0.006);
			});

			// create updating session
			this.updatingSession = new UpdatingSession(this)
			{
				ApplicationDirectoryPath = this.appDirectoryPath,
				ApplicationName = this.appName,
				PackageManifestUri = this.packageManifestUri,
				ProcessExecutableToWaitFor = this.appExePath,
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
					case "-accent-color":
						if (i < argCount - 1)
						{
							if (Color.TryParse(args[++i], out var color))
								this.accentColor = color;
							else
								this.logger.LogWarning($"Invalid accent color: {args[i]}");
						}
						else
							this.logger.LogWarning("No accent color specified");
						break;
					case "-culture":
						if (i < argCount - 1)
						{
							try
							{
								this.cultureInfo = CultureInfo.GetCultureInfo(args[++i]);
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
					case "-executable-args":
						if (i < argCount - 1)
						{
							if (this.appExeArgs == null)
								this.appExeArgs = args[++i];
							else
							{
								this.logger.LogError("Duplicate arguments of executable specified");
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
			if (this.appExePath != null)
			{
				this.appExePath = Path.DirectorySeparatorChar switch
				{
					'\\' => this.appExePath.Replace('/', '\\'),
					'/' => this.appExePath.Replace('\\', '/'),
					_ => this.appExePath,
				};
			}
			return true;
		}


		// Implementations.
		public override CultureInfo CultureInfo { get => this.cultureInfo; }
		public override string? GetString(string key, string? defaultValue = null)
		{
			if (this.Resources.TryGetResource($"String.{key}", out var value) && value is string str)
				return str;
			return defaultValue;
		}
		public override bool IsShutdownStarted { get; }
		public override ILoggerFactory LoggerFactory { get; } = new LoggerFactory();
		public override ISettings PersistentState { get; } = new MemorySettings();
		public override ISettings Settings { get; } = new MemorySettings();
	}
}
