using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using CarinaStudio.AutoUpdater.ViewModels;
using CarinaStudio.Configuration;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
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


		// Static fields.
		static CultureInfo cultureInfo = CultureInfo.GetCultureInfo("en-US");


		// Fields.
		Color? accentColor;
		Version? appBaseVersion;
		string? appDirectoryPath;
		string? appExeArgs;
		string? appExePath;
		string? appName;
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
			// get running directory
			this.RootPrivateDirectoryPath = Global.Run(() =>
			{
				var mainModule = Process.GetCurrentProcess().MainModule;
				if (mainModule != null && Path.GetFileNameWithoutExtension(mainModule.FileName) != "dotnet")
					return Path.GetDirectoryName(mainModule.FileName) ?? "";
#pragma warning disable SYSLIB0044
				try
				{
					var codeBase = System.Reflection.Assembly.GetEntryAssembly()?.GetName().CodeBase;
					if (codeBase != null && codeBase.StartsWith("file://") && codeBase.Length > 7)
					{
						if (Platform.IsWindows)
							return Path.GetDirectoryName(codeBase[8..^0].Replace('/', '\\')) ?? Environment.CurrentDirectory;
						return Path.GetDirectoryName(codeBase[7..^0]) ?? Environment.CurrentDirectory;
					}
				}
				// ReSharper disable EmptyGeneralCatchClause
				catch
				{ }
				// ReSharper restore EmptyGeneralCatchClause
#pragma warning restore SYSLIB0044
				return Environment.CurrentDirectory;
			});

			// create logger
			// ReSharper disable VirtualMemberCallInConstructor
			this.logger = this.LoggerFactory.CreateLogger("App");
			// ReSharper restore VirtualMemberCallInConstructor

			// setup application name
			var cultureName = cultureInfo.Name;
			if (cultureName.StartsWith("zh-"))
			{
				if (cultureName.EndsWith("TW"))
					this.Name = "Carina Studio 應用程式更新";
				else
					this.Name = "Carina Studio 应用程序更新";
			}
			else
				this.Name = "Carina Studio Application Update";
		}


		// Apply given screen scale factor for Linux.
		static void ApplyScreenScaleFactor(double factor)
		{
			// check state
			if (!Platform.IsLinux || !double.IsFinite(factor) || factor < 1)
				return;
			if (Math.Abs(factor - 1) < 0.01)
				return;

			// set environment variable
			Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR", factor.ToString());
		}


		// Initialize.
		public override void Initialize() => AvaloniaXamlLoader.Load(this);


		/// <summary>
		/// Check whether executable of application has been specified or not.
		/// </summary>
		public bool IsAppExecutableSpecified => !string.IsNullOrWhiteSpace(this.appExePath);


		// Program entry.
		public static void Main(string[] args)
		{
			// parse arguments which are needed before creating application
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
								cultureInfo = CultureInfo.GetCultureInfo(args[++i]);
							}
							catch 
							{
								Console.Error.WriteLine($"Invalid culture name: {args[i]}");
							}
						}
						else
							Console.Error.WriteLine("No culture name specified");
						break;
					case "-screen-scale-factor":
						if (Platform.IsLinux && i < argCount - 1 && double.TryParse(args[++i], out var factor))
							ApplyScreenScaleFactor(factor);
						break;
				}
			}

			// start updating
			AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.LogToTrace().Also(it =>
				{
					if (Platform.IsWindows)
					{
						it.ConfigureFonts(fontManager =>
						{
							fontManager.AddFontCollection(new EmbeddedFontCollection(
								new Uri("fonts:Inter", UriKind.Absolute),
								new Uri($"avares://AutoUpdater.Avalonia/Fonts", UriKind.Absolute)));
						});
					}
					else if (Platform.IsLinux)
					{
						it.With(new FontManagerOptions
						{
							DefaultFamilyName = $"avares://AutoUpdater.Avalonia/Fonts/#Inter"
						});
						it.With(new X11PlatformOptions());
					}
					else if (Platform.IsMacOS)
					{
						it.ConfigureFonts(fontManager =>
						{
							fontManager.AddFontCollection(new EmbeddedFontCollection(
								new Uri("fonts:Inter", UriKind.Absolute),
								new Uri($"avares://AutoUpdater.Avalonia/Fonts", UriKind.Absolute)));
						});
						it.With(new MacOSPlatformOptions
						{
							DisableDefaultApplicationMenuItems = true,
							DisableNativeMenus = true,
						});
					}
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
							var appExePath = app.appExePath.AsNonNull();
							if (File.Exists(appExePath))
								File.SetUnixFileMode(appExePath, File.GetUnixFileMode(appExePath) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
							else
								app.logger.LogError("Cannot find executable '{appExePath}'", app.appExePath);
						}
						catch (Exception ex)
						{
							app.logger.LogError(ex, "Unable to mark '{appExePath}' as executable", app.appExePath);
						}
					}

					// start application
					using var process = Process.Start(new ProcessStartInfo()
					{
						Arguments = app.appExeArgs ?? "",
						FileName = app.appExePath.AsNonNull(),
						UseShellExecute = Platform.IsMacOS && Path.GetExtension(app.appExePath).ToLower() == ".app",
					});
				}
				catch (Exception ex)
				{
					app.logger.LogError(ex, "Unable to start application '{appExePath}'", app.appExePath);
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
			if (!this.ParseArgs(desktopLifetime.Args ?? Array.Empty<string>()))
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
					Layout = "${longdate} ${pad:padding=-5:inner=${processid}} ${pad:padding=-4:inner=${threadid}} ${pad:padding=-5:inner=${level:uppercase=true}} ${logger:shortName=true}: ${message} ${exception:format=tostring}",
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
			var cultureName = cultureInfo.Name;
			if (!cultureName.StartsWith("en-") && cultureName.StartsWith("zh"))
			{
				try
				{
					if (cultureName.EndsWith("TW"))
						cultureName = "zh-TW";
					else
						cultureName = "zh-CN";
					var stringResources = new ResourceInclude(new Uri("avares://AutoUpdater.Avalonia/"))
					{
						Source = new Uri($"/Strings/{cultureName}.axaml", UriKind.Relative)
					};
					_ = stringResources.Loaded; // trigger error if resource not found
					this.logger.LogInformation("Load strings for {name}", cultureName);
					this.Resources.MergedDictionaries.Add(stringResources);
				}
				catch
				{
					this.logger.LogWarning("No strings for {name}", cultureName);
				}
			}

			// load styles
			this.Styles.Add(new FluentTheme());
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
			this.RequestedThemeVariant = this.darkMode ? ThemeVariant.Dark : ThemeVariant.Light;

			// apply accent color
			this.accentColor?.Let(accentColor =>
			{
				static Color GammaTransform(Color color, double gamma)
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
				ApplicationBaseVersion = this.appBaseVersion,
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
								this.logger.LogWarning("Invalid accent color: {arg}", args[i]);
						}
						else
							this.logger.LogWarning("No accent color specified");
						break;
					case "-base-version":
						if (i < argCount - 1)
						{
							if (Version.TryParse(args[++i], out var version))
								this.appBaseVersion = version;
							else
								this.logger.LogWarning("Invalid base application version: {arg}", args[i]);
						}
						else
							this.logger.LogError("No base application version specified");
						break;
					case "-culture":
						++i;
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
								this.logger.LogError("Invalid package manifest URI: {arg}", args[i]);
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
								this.logger.LogError("Invalid process ID: {arg}", args[i]);
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
		public override CultureInfo CultureInfo => cultureInfo;
		public override IObservable<string?> GetObservableString(string key) => new FixedObservableValue<string?>(null);
		public override string? GetString(string key, string? defaultValue = null) =>
			this.FindResourceOrDefault($"String.{key}", defaultValue);
		// ReSharper disable UnassignedGetOnlyAutoProperty
		public override bool IsShutdownStarted { get; }
		// ReSharper restore UnassignedGetOnlyAutoProperty
		public override ILoggerFactory LoggerFactory { get; } = new LoggerFactory(new ILoggerProvider[] { new NLog.Extensions.Logging.NLogLoggerProvider() });
		public override ISettings PersistentState { get; } = new MemorySettings();
		public override string RootPrivateDirectoryPath { get; }
		public override ISettings Settings { get; } = new MemorySettings();
	}
}
