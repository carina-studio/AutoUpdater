using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using CarinaStudio.AutoUpdater.ViewModels;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
using Mono.Unix;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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


		// Static fields.
		static readonly Regex X11MonitorLineRegex = new Regex("^[\\s]*[\\d]+[\\s]*\\:[\\s]*\\+\\*(?<Name>[^\\s]+)");


		// Fields.
		Color? accentColor;
		string? appDirectoryPath;
		string? appExeArgs;
		string? appExePath;
		string? appName;
		CultureInfo cultureInfo = CultureInfo.GetCultureInfo("en-US");
		bool darkMode;
#if DEBUG
		bool isDebugMode = true;
#else
		bool isDebugMode;
#endif
		readonly ILogger logger;
		Uri? packageManifestUri;
		int? processIdToWaitFor;
		bool selfContainedPackageOnly;
		UpdatingSession? updatingSession;


		/// <summary>
		/// Initialize new <see cref="App"/> instance.
		/// </summary>
		public App()
		{
			this.RootPrivateDirectoryPath = Global.Run(() =>
			{
				var mainModule = Process.GetCurrentProcess().MainModule;
				if (mainModule != null && Path.GetFileNameWithoutExtension(mainModule.FileName) != "dotnet")
					return Path.GetDirectoryName(mainModule.FileName) ?? "";
				var codeBase = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.CodeBase;
				if (codeBase != null && codeBase.StartsWith("file://") && codeBase.Length > 7)
				{
					if (Platform.IsWindows)
						return Path.GetDirectoryName(codeBase.Substring(8).Replace('/', '\\')) ?? Environment.CurrentDirectory;
					return Path.GetDirectoryName(codeBase.Substring(7)) ?? Environment.CurrentDirectory;
				}
				return Environment.CurrentDirectory;
			});
			this.logger = this.LoggerFactory.CreateLogger("App");
		}


		// Apply given screen scale factor for Linux.
		static void ApplyScreenScaleFactor(double factor)
		{
			// check state
			if (!Platform.IsLinux || !double.IsFinite(factor) || factor < 1)
				return;
			if (Math.Abs(factor - 1) < 0.01)
				return;

			// get all screens
			var screenNames = new List<string>();
			try
			{
				using var process = Process.Start(new ProcessStartInfo()
				{
					Arguments = "--listactivemonitors",
					CreateNoWindow = true,
					FileName = "xrandr",
					RedirectStandardOutput = true,
					UseShellExecute = false,
				});
				if (process == null)
					return;
				using var reader = process.StandardOutput;
				var line = reader.ReadLine();
				while (line != null)
				{
					var match = X11MonitorLineRegex.Match(line);
					if (match.Success)
						screenNames.Add(match.Groups["Name"].Value);
					line = reader.ReadLine();
				}
			}
			catch
			{ }
			if (screenNames.IsEmpty())
				return;

			// set environment variable
			var valueBuilder = new StringBuilder();
			foreach (var screenName in screenNames)
			{
				if (valueBuilder.Length > 0)
					valueBuilder.Append(';');
				valueBuilder.Append(screenName);
				valueBuilder.Append('=');
				valueBuilder.AppendFormat("{0:F1}", factor);
			}
			Environment.SetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS", valueBuilder.ToString());
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
			// apply screen scale factor
			if (Platform.IsLinux)
			{
				for (var i = 0; i < args.Length; ++i)
				{
					if (args[i] == "-screen-scale-factor")
					{
						++i;
						if (i < args.Length && double.TryParse(args[i], out var factor))
							ApplyScreenScaleFactor(factor);
						break;
					}
				}
			}

			// start updating
			AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.LogToTrace().Also(it =>
				{
					if (Platform.IsLinux)
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
						UseShellExecute = Platform.IsMacOS && Path.GetExtension(app.appExePath)?.ToLower() == ".app",
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
			var desktopLifetime = (IClassicDesktopStyleApplicationLifetime?)this.ApplicationLifetime;
			if (desktopLifetime == null)
				return;
			if (!this.ParseArgs(desktopLifetime.Args))
			{
				this.SynchronizationContext.Post(() => desktopLifetime.Shutdown(EXIT_CODE_INVALID_ARGUMENT));
				return;
			}

			// setup logger
			NLog.LogManager.Configuration = new NLog.Config.LoggingConfiguration().Also(it =>
			{
				var fileTarget = new NLog.Targets.FileTarget("file")
				{
					ArchiveAboveSize = 10L << 20, // 10 MB per log file
					ArchiveFileKind = NLog.Targets.FilePathKind.Absolute,
					ArchiveFileName = Path.Combine(this.RootPrivateDirectoryPath, "Log", "log.txt"),
					ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.Sequence,
					FileName = Path.Combine(this.RootPrivateDirectoryPath, "Log", "log.txt"),
					Layout = "${longdate} ${pad:padding=-5:inner=${processid}} ${pad:padding=-4:inner=${threadid}} ${pad:padding=-5:inner=${level:uppercase=true}} ${logger:shortName=true}: ${message} ${all-event-properties} ${exception:format=tostring}",
					MaxArchiveFiles = 10,
				};
				var rule = new NLog.Config.LoggingRule("logToFile").Also(rule =>
				{
					rule.LoggerNamePattern = "*";
					rule.SetLoggingLevels(
						this.isDebugMode ? NLog.LogLevel.Trace : NLog.LogLevel.Debug,
						NLog.LogLevel.Error
					);
					rule.Targets.Add(fileTarget);
				});
				it.AddTarget(fileTarget);
				it.LoggingRules.Add(rule);
			});
			NLog.LogManager.ReconfigExistingLoggers();

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
			this.Resources["Brush/Window.Background"] = new SolidColorBrush(this.darkMode ? Color.Parse("#1e1e1e") : Color.Parse("#f0f0f0"));
			if (this.darkMode)
            {
				var borderBrush = new LinearGradientBrush().Also(it =>
				{
					it.EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative);
					it.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative);
					it.GradientStops.Add(new GradientStop(Color.Parse("#22ffffff"), 0));
					it.GradientStops.Add(new GradientStop(Color.Parse("#11ffffff"), 1));
				});
				var borderBrushPressed = new LinearGradientBrush().Also(it =>
				{
					it.EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative);
					it.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative);
					it.GradientStops.Add(new GradientStop(Color.Parse("#11ffffff"), 0));
					it.GradientStops.Add(new GradientStop(Color.Parse("#22ffffff"), 1));
				});
				this.Resources["ButtonBackground"] = new SolidColorBrush(Color.Parse("#2d2d2d"));
				this.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Color.Parse("#373737"));
				this.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Color.Parse("#404040"));
				this.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Color.Parse("#202020"));
				this.Resources["ButtonBorderBrush"] = borderBrush;
				this.Resources["ButtonBorderBrushDisabled"] = borderBrush;
				this.Resources["ButtonBorderBrushPointerOver"] = borderBrush;
				this.Resources["ButtonBorderBrushPressed"] = borderBrushPressed;
			}
			else
            {
				var borderBrush = new LinearGradientBrush().Also(it =>
				{
					it.EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative);
					it.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative);
					it.GradientStops.Add(new GradientStop(Color.Parse("#20000000"), 0));
					it.GradientStops.Add(new GradientStop(Color.Parse("#50000000"), 1));
				});
				var borderBrushPressed = new LinearGradientBrush().Also(it =>
				{
					it.EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative);
					it.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative);
					it.GradientStops.Add(new GradientStop(Color.Parse("#50000000"), 0));
					it.GradientStops.Add(new GradientStop(Color.Parse("#20000000"), 1));
				});
				this.Resources["ButtonBackground"] = new SolidColorBrush(Color.Parse("#fbfbfb"));
				this.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Color.Parse("#e7e7e7"));
				this.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Color.Parse("#f0f0f0"));
				this.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Color.Parse("#e0e0e0"));
				this.Resources["ButtonBorderBrush"] = borderBrush;
				this.Resources["ButtonBorderBrushDisabled"] = borderBrush;
				this.Resources["ButtonBorderBrushPointerOver"] = borderBrush;
				this.Resources["ButtonBorderBrushPressed"] = borderBrushPressed;
			}

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
				SelfContainedPackageOnly = this.selfContainedPackageOnly,
			};

			// show main window
			this.SynchronizationContext.Post(() =>
			{
				new MainWindow() { DataContext = this.updatingSession }.Show();
			});
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
					case "-debug-mode":
						this.isDebugMode = true;
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
					case "-screen-scale-factor":
						if (i >= argCount)
							this.logger.LogWarning("No screen scale factor specified");
						break;
					case "-self-contained-only":
						this.selfContainedPackageOnly = true;
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
		public override ILoggerFactory LoggerFactory { get; } = new LoggerFactory(new ILoggerProvider[] { new NLog.Extensions.Logging.NLogLoggerProvider() });
		public override ISettings PersistentState { get; } = new MemorySettings();
		public override string RootPrivateDirectoryPath { get; }
		public override ISettings Settings { get; } = new MemorySettings();
	}
}
