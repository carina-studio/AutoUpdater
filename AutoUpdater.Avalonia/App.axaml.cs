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
using CarinaStudio.MacOS.AppKit;
using CarinaStudio.MacOS.CoreGraphics;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using SkiaSharp;
using System.Runtime.InteropServices;
using ILogger = Microsoft.Extensions.Logging.ILogger;

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
		bool bypassCertificateValidation;
		bool darkMode;
		string? httpUserAgent;
		bool isStartAppCalled;
#if DEBUG
		bool isDebugMode = true;
#else
		bool isDebugMode;
#endif
		readonly ManualResetEventSlim isLoggerReadyEvent = new(false);
		readonly ILogger logger;
		NSDockTile? macOSAppDockTile;
		SKBitmap? macOSAppDockTileOverlayBitmap;
		byte[]? macOSAppDockTileOverlayBitmapBuffer;
		GCHandle macOSAppDockTileOverlayBitmapBufferHandle;
		CGDataProvider? macOSAppDockTileOverlayBitmapBufferProvider;
		CGImage? macOSAppDockTileOverlayCGImage;
		NSImageView? macOSAppDockTileOverlayImageView;
		NSImage? macOSAppDockTileOverlayNSImage;
		string? packageManifestRequestHttpReferer;
		Uri? packageManifestUri;
		string? packageRequestHttpReferer;
		int? processIdToWaitFor;
		bool selfContainedPackageOnly;
		double taskBarProgress;
		TaskbarIconProgressState taskBarProgressState = TaskbarIconProgressState.None;
		ScheduledAction? updateMacOSAppDockTileProgressAction;
		UpdatingSession? updatingSession;
		Win32.ITaskbarList3? windowsTaskbarList;


		/// <summary>
		/// Initialize new <see cref="App"/> instance.
		/// </summary>
		public App()
		{
			// get running directory
			this.RootPrivateDirectoryPath = Global.Run(() =>
			{
				// get path from main module
				if (Platform.IsWindows)
				{
					var fileNameBuffer = new StringBuilder(256);
					var size = Win32.GetModuleFileName(default, fileNameBuffer, (uint)fileNameBuffer.Capacity);
					if (size <= fileNameBuffer.Capacity)
					{
						var fileName = fileNameBuffer.ToString();
						if (Path.GetFileNameWithoutExtension(fileName) != "dotnet")
							return Path.GetDirectoryName(fileName) ?? "";
					}
				}
				var mainModule = Process.GetCurrentProcess().MainModule;
				if (mainModule is not null && Path.GetFileNameWithoutExtension(mainModule.FileName) != "dotnet")
					return Path.GetDirectoryName(mainModule.FileName) ?? "";
				
				// get path from assembly
#pragma warning disable SYSLIB0044
				try
				{
					var codeBase = System.Reflection.Assembly.GetEntryAssembly()?.GetName().CodeBase;
					if (codeBase is not null && codeBase.StartsWith("file://") && codeBase.Length > 7)
					{
						if (Platform.IsWindows)
							return Path.GetDirectoryName(codeBase[8..].Replace('/', '\\')) ?? Environment.CurrentDirectory;
						return Path.GetDirectoryName(codeBase[7..]) ?? Environment.CurrentDirectory;
					}
				}
				// ReSharper disable EmptyGeneralCatchClause
				catch
				{ }
				// ReSharper restore EmptyGeneralCatchClause
#pragma warning restore SYSLIB0044
				return Environment.CurrentDirectory;
			});
			
			// setup logger
			LogManager.Configuration = new NLog.Config.LoggingConfiguration().Also(it =>
			{
				ThreadPool.QueueUserWorkItem(s =>
				{
					try
					{
						var config = (NLog.Config.LoggingConfiguration)s!;
						var fileTarget = new NLog.Targets.FileTarget("file")
						{
							ArchiveAboveSize = 10L << 20, // 10 MB per log file
							ArchiveFileKind = NLog.Targets.FilePathKind.Absolute,
							ArchiveFileName = Path.Combine(this.RootPrivateDirectoryPath, "Log", "log.txt"),
							ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.Sequence,
							FileName = Path.Combine(this.RootPrivateDirectoryPath, "Log", "log.txt"),
							// ReSharper disable StringLiteralTypo
							Layout = "${longdate} ${pad:padding=-5:inner=${processid}} ${pad:padding=-4:inner=${threadid}} ${pad:padding=-5:inner=${level:uppercase=true}} ${logger:shortName=true}: ${message} ${exception:format=tostring}",
							// ReSharper restore StringLiteralTypo
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
						config.AddTarget(fileTarget);
						config.LoggingRules.Add(rule);
						LogManager.ReconfigExistingLoggers();
					}
					finally
					{
						this.isLoggerReadyEvent.Set();
					}
				}, it);
			});

			// create logger
			// ReSharper disable VirtualMemberCallInConstructor
			this.logger = this.LoggerFactory.CreateLogger("App");
			// ReSharper restore VirtualMemberCallInConstructor
			this.logger.LogWarning("Create");

			// setup application name
			var cultureName = cultureInfo.Name;
			if (cultureName.StartsWith("zh-"))
			{
				this.Name = cultureName.EndsWith("TW") 
					? "Carina Studio 應用程式更新"
					: "Carina Studio 应用程序更新";
			}
			else
				this.Name = "Carina Studio Application Update";
		}


		// Apply given screen scale factor for Linux.
		static void ApplyScreenScaleFactor(double factor)
		{
			// check state
			if (!Platform.IsLinux || Platform.LinuxDistribution == LinuxDistribution.Ubuntu || !double.IsFinite(factor) || factor < 1)
				return;
			if (Math.Abs(factor - 1) < 0.01)
				return;

			// set environment variable
			Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR", factor.ToString(CultureInfo.InvariantCulture));
		}


		// Initialize.
		public override void Initialize() => AvaloniaXamlLoader.Load(this);


		/// <summary>
		/// Check whether executable of application has been specified or not.
		/// </summary>
		public bool IsAppExecutableSpecified => !string.IsNullOrWhiteSpace(this.appExePath);
		
		
		/// <summary>
		/// Check whether main loop of application has been exited or not.
		/// </summary>
		public bool IsExited { get; private set; }


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
			var cjkUnicodeRanges = new UnicodeRange(new UnicodeRangeSegment[]
			{
				// ReSharper disable CommentTypo
				new(0x2e80, 0x2eff), // CJKRadicalsSupplement
				new(0x3000, 0x303f), // CJKSymbolsandPunctuation
				new(0x3200, 0x4dbf), // EnclosedCJKLettersandMonths, CJKCompatibility, CJKUnifiedIdeographsExtensionA
				new(0x4e00, 0x9fff), // CJKUnifiedIdeographs
				new(0xf900, 0xfaff), // CJKCompatibilityIdeographs
				new(0xfe30, 0xfe4f), // CJKCompatibilityForms
				// ReSharper restore CommentTypo
			});
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
						it.With(new FontManagerOptions
						{
							// ReSharper disable StringLiteralTypo
							FontFallbacks = new FontFallback[]
							{
								new()
								{
									FontFamily = new("Microsoft JhengHei UI"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("Microsoft YaHei UI"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("PMingLiU"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("MingLiU"),
									UnicodeRange = cjkUnicodeRanges,
								}
							},
							// ReSharper restore StringLiteralTypo
						});
					}
					else if (Platform.IsLinux)
					{
						it.With(new FontManagerOptions
						{
							DefaultFamilyName = $"avares://AutoUpdater.Avalonia/Fonts/#Inter",
							// ReSharper disable StringLiteralTypo
							FontFallbacks = new FontFallback[]
							{
								new()
								{
									FontFamily = new("Noto Sans CJK TC"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("Noto Sans CJK SC"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("Noto Sans Mono CJK TC"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("Noto Sans Mono CJK SC"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("Noto Serif CJK TC"),
									UnicodeRange = cjkUnicodeRanges,
								},
								new()
								{
									FontFamily = new("Noto Serif CJK SC"),
									UnicodeRange = cjkUnicodeRanges,
								}
							},
							// ReSharper restore StringLiteralTypo
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
			
			// update state
			var app = (App)Current;
			app.IsExited = true;
			
			// start application
			app.StartApplication();
			
			// remove app icon from dock
			if (Platform.IsMacOS)
				NSApplication.Current?.SetActivationPolicy(NSApplication.ActivationPolicy.Accessory);
			
			// complete
			app.logger.LogWarning("Complete");
		}


		// Called when Avalonia initialized.
		public override void OnFrameworkInitializationCompleted()
		{
			// call base
			base.OnFrameworkInitializationCompleted();

			// parse arguments
			var desktopLifetime = (IClassicDesktopStyleApplicationLifetime?)this.ApplicationLifetime;
			if (desktopLifetime is null)
				return;
			if (!this.ParseArgs(desktopLifetime.Args ?? Array.Empty<string>()))
			{
				this.SynchronizationContext.Post(() => desktopLifetime.Shutdown(EXIT_CODE_INVALID_ARGUMENT));
				return;
			}
			
			// setup actions
			this.updateMacOSAppDockTileProgressAction = new(this.UpdateMacOSAppDockTileProgress);

			// load strings
			var cultureName = cultureInfo.Name;
			if (!cultureName.StartsWith("en-") && cultureName.StartsWith("zh"))
			{
				try
				{
					cultureName = cultureName.EndsWith("TW") 
						? "zh-TW"
						: "zh-CN";
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
			this.Resources["Brush/Window.Background"] = new SolidColorBrush(this.darkMode ? Color.Parse("#202020") : Color.Parse("#f0f0f0"));
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
				HttpUserAgent = this.httpUserAgent,
				PackageManifestRequestHttpReferer = this.packageManifestRequestHttpReferer,
				PackageManifestUri = this.packageManifestUri,
				PackageRequestHttpReferer = this.packageRequestHttpReferer,
				ProcessExecutableToWaitFor = this.appExePath,
				ProcessIdToWaitFor = this.processIdToWaitFor,
				SelfContainedPackageOnly = this.selfContainedPackageOnly,
			};
			if (this.bypassCertificateValidation)
			{
				this.logger.LogWarning("SSL certificate validation will be bypassed");
				UpdatingSession.BypassCertificateValidation = true;
			}

			// show main window
			this.SynchronizationContext.Post(() =>
			{
				new MainWindow { DataContext = this.updatingSession }.Show();
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
					case "-bypass-cert-validation":
						this.bypassCertificateValidation = true;
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
							if (this.appDirectoryPath is null)
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
							if (this.appExePath is null)
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
							if (this.appExeArgs is null)
								this.appExeArgs = args[++i];
							else
							{
								this.logger.LogError("Duplicate arguments of executable specified");
								return false;
							}
						}
						break;
					case "-http-user-agent":
						if (i < argCount - 1)
						{
							if (this.httpUserAgent is null)
								this.httpUserAgent = args[++i];
							else
							{
								this.logger.LogError("Duplicate HTTP user agent specified");
								return false;
							}
						}
						else
						{
							this.logger.LogError("No HTTP user agent specified");
							return false;
						}
						break;
					case "-name":
						if (i < argCount - 1)
						{
							if (this.appName is null)
								this.appName = args[++i];
							else
							{
								this.logger.LogError("Duplicate application name specified");
								return false;
							}
						}
						break;
					case "-package-http-referer":
						if (i < argCount - 1)
						{
							if (this.packageRequestHttpReferer is null)
								this.packageRequestHttpReferer = args[++i];
							else
							{
								this.logger.LogError("Duplicate HTTP referer for package request specified");
								return false;
							}
						}
						else
						{
							this.logger.LogError("No HTTP referer for package request specified");
							return false;
						}
						break;
					case "-package-manifest":
						if (i < argCount - 1)
						{
							if (this.packageManifestUri is not null)
							{
								this.logger.LogError("Duplicate package manifest URI specified");
								return false;
							}
							if (Uri.TryCreate(args[++i], UriKind.Absolute, out var uri))
								this.packageManifestUri = uri;
							else
							{
								this.logger.LogError("Invalid package manifest URI: {arg}", args[i]);
								return false;
							}
						}
						break;
					case "-package-manifest-http-referer":
						if (i < argCount - 1)
						{
							if (this.packageManifestRequestHttpReferer is null)
								this.packageManifestRequestHttpReferer = args[++i];
							else
							{
								this.logger.LogError("Duplicate HTTP referer for package manifest request specified");
								return false;
							}
						}
						else
						{
							this.logger.LogError("No HTTP referer for package manifest request specified");
							return false;
						}
						break;
					case "-screen-scale-factor":
						if (i >= argCount)
							this.logger.LogWarning("No screen scale factor specified");
						break;
					case "-self-contained-only":
						this.selfContainedPackageOnly = true;
						break;
					case "-updater-name":
						if (i < argCount - 1)
							this.Name = args[++i];
						break;
					case "-wait-for-process":
						if (i < argCount - 1)
						{
							if (this.processIdToWaitFor is not null)
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
			if (this.packageManifestUri is null)
			{
				this.logger.LogError("No package manifest URI specified");
				return false;
			}
			if (this.appExePath is not null)
			{
				this.appExePath = Path.DirectorySeparatorChar switch
				{
					'\\' => this.appExePath.Replace('/', '\\'),
					'/' => this.appExePath.Replace('\\', '/'),
					_ => this.appExePath,
				};
			}
			if (this.packageManifestRequestHttpReferer is not null)
				this.logger.LogDebug("HTTP referer for package manifest request set");
			if (this.packageRequestHttpReferer is not null)
				this.logger.LogDebug("HTTP referer for package request set");
			return true;
		}
		
		
		// Perform necessary setup for dock tile on macOS.
		void SetupMacOSAppDockTile()
		{
			// check state
			if (Platform.IsNotMacOS || this.macOSAppDockTile is not null)
				return;

			// get application
			var app = NSApplication.Shared;

			// create NSView for dock tile
			var dockTileSize = default(Size);
			this.macOSAppDockTile = app.DockTile.Also(dockTile =>
			{
				// prepare icon
				var iconImage = app.ApplicationIconImage;
				if (Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName) == "dotnet")
				{
					using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CarinaStudio.AppSuite.Resources.AppIcon_macOS_256.png");
					if (stream is not null)
						iconImage = NSImage.FromStream(stream);
				}
				
				// setup dock tile
				dockTileSize = dockTile.Size.Let(it => new Size(it.Width, it.Height));
				dockTile.ContentView = new NSImageView(new(0, 0, dockTileSize.Width, dockTileSize.Height)).Also(imageView =>
				{
					imageView.Image = iconImage;
					imageView.ImageAlignment = NSImageAlignment.Bottom;
					imageView.ImageScaling = NSImageScaling.ProportionallyUpOrDown;
					this.macOSAppDockTileOverlayImageView = new(new(0, 0, dockTileSize.Width, dockTileSize.Height));
					imageView.AddSubView(this.macOSAppDockTileOverlayImageView);
				});
				dockTile.Display();
			});

			// create overlay bitmap
			this.macOSAppDockTileOverlayBitmap = new(
				(int)dockTileSize.Width,
				(int)dockTileSize.Height
			);
			this.macOSAppDockTileOverlayBitmapBuffer = new byte[this.macOSAppDockTileOverlayBitmap.ByteCount];
			this.macOSAppDockTileOverlayBitmapBufferHandle = GCHandle.Alloc(this.macOSAppDockTileOverlayBitmapBuffer, GCHandleType.Pinned);
			this.macOSAppDockTileOverlayBitmap.InstallPixels(new(
				this.macOSAppDockTileOverlayBitmap.Width,
				this.macOSAppDockTileOverlayBitmap.Height,
				SKColorType.Rgba8888,
				SKAlphaType.Unpremul,
				SKColorSpace.CreateSrgb()
			), this.macOSAppDockTileOverlayBitmapBufferHandle.AddrOfPinnedObject());
			this.macOSAppDockTileOverlayBitmapBufferProvider = new(this.macOSAppDockTileOverlayBitmapBuffer);
		}


		// Setup related objects for taskbar.
		[MemberNotNullWhen(true, nameof(windowsTaskbarList))]
		bool SetupWindowsTaskbarList()
		{
			if (this.windowsTaskbarList is not null)
				return true;
			Win32.CoInitialize();
			var result = Win32.CoCreateInstance(in Win32.CLSID_TaskBarList, null, Win32.CLSCTX.INPROC_SERVER, in Win32.IID_TaskBarList3, out var obj);
			if (obj is null)
			{
				this.logger.LogError("Unable to create ITaskBarList3 object, result: {result}", result);
				return false;
			}
			this.windowsTaskbarList = obj as Win32.ITaskbarList3;
			if (this.windowsTaskbarList is null)
			{
				this.logger.LogError("Unable to get implementation of ITaskBarList3");
				return false;
			}
			this.windowsTaskbarList.HrInit();
			return true;
		}


		/// <summary>
		/// Start application if available.
		/// </summary>
		public bool StartApplication()
		{
			// check state
			if (this.updatingSession?.IsUpdatingSucceeded != true 
			    || !this.IsAppExecutableSpecified
			    || this.isStartAppCalled)
			{
				return false;
			}

			// start application
			this.isStartAppCalled = true;
			try
			{
				// mark file as executable
				if (Platform.IsLinux)
				{
					try
					{
						var appExePath = this.appExePath.AsNonNull();
						if (File.Exists(appExePath))
						{
#pragma warning disable CA1416
							File.SetUnixFileMode(appExePath, File.GetUnixFileMode(appExePath) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
#pragma warning restore CA1416
						}
						else
							this.logger.LogError("Cannot find executable '{appExePath}'", this.appExePath);
					}
					catch (Exception ex)
					{
						this.logger.LogError(ex, "Unable to mark '{appExePath}' as executable", this.appExePath);
					}
				}

				// start application
				this.logger.LogDebug("Start application '{appExePath}'", this.appExePath);
				Process.Start(new ProcessStartInfo
				{
					Arguments = this.appExeArgs ?? "",
					FileName = this.appExePath.AsNonNull(),
					UseShellExecute = Platform.IsMacOS && Path.GetExtension(this.appExePath).ToLower() == ".app",
				});
				return true;
			}
			catch (Exception ex)
			{
				this.logger.LogError(ex, "Unable to start application '{appExePath}'", this.appExePath);
				return false;
			}
		}
		
		
		// Update dock tile on macOS.
		void UpdateMacOSAppDockTileProgress()
		{
			this.SetupMacOSAppDockTile();
			switch (this.taskBarProgressState)
			{
				case TaskbarIconProgressState.Indeterminate:
					// Unsupported
					goto default;
				case TaskbarIconProgressState.Normal:
				case TaskbarIconProgressState.Error:
				case TaskbarIconProgressState.Paused:
					// update overlay bitmap
					new SKCanvas(this.macOSAppDockTileOverlayBitmap).Use(canvas =>
					{
						// get info of dock tile
						var dockTileWidth = this.macOSAppDockTileOverlayBitmap!.Width;
						var dockTileHeight = this.macOSAppDockTileOverlayBitmap.Height;
						var progressBackgroundColor = Colors.Black;
						var progressForegroundColor = this.taskBarProgressState switch
						{
							TaskbarIconProgressState.Error => Colors.Red,
							TaskbarIconProgressState.Paused => Colors.Yellow,
							_ => Colors.LightGray,
						};

						// prepare progress background
						using var progressBackgroundPaint = new SKPaint().Setup(it =>
						{
							it.Color = new(progressBackgroundColor.R, progressBackgroundColor.G, progressBackgroundColor.B, progressBackgroundColor.A);
							it.IsAntialias = true;
							it.Style = SKPaintStyle.Fill;
						});
						var progressBackgroundWidth = (int)(dockTileWidth * 0.65 + 0.5);
						var progressBackgroundHeight = (int)(dockTileHeight * 0.1 + 0.5);
						var progressBackgroundLeft = (dockTileWidth - progressBackgroundWidth) >> 1;
						var progressBackgroundTop = (int)(dockTileHeight * 0.7 + 0.5);
						var progressBackgroundRect = new SKRect(progressBackgroundLeft, progressBackgroundTop, progressBackgroundLeft + progressBackgroundWidth, progressBackgroundTop + progressBackgroundHeight);

						// prepare progress foreground
						using var progressForegroundPaint = new SKPaint().Setup(it =>
						{
							it.Color = new(progressForegroundColor.R, progressForegroundColor.G, progressForegroundColor.B, progressForegroundColor.A);
							it.IsAntialias = true;
							it.Style = SKPaintStyle.Fill;
						});
						var progressBorderWidth = (int)(progressBackgroundHeight * 0.15 + 0.5);
						var progressForegroundWidth = (int)((progressBackgroundWidth - progressBorderWidth - progressBorderWidth) * this.taskBarProgress + 0.5);
						var progressForegroundHeight = progressBackgroundHeight - progressBorderWidth - progressBorderWidth;
						var progressForegroundLeft = progressBackgroundLeft + progressBorderWidth;
						var progressForegroundTop = progressBackgroundTop + progressBorderWidth;
						var progressForegroundRect = new SKRect(progressForegroundLeft, progressForegroundTop, progressForegroundLeft + progressForegroundWidth, progressForegroundTop + progressForegroundHeight);

						// clear buffer
						canvas.Clear(new(0, 0, 0, 0));

						// draw progress
						if (this.taskBarProgress >= 0.001)
						{
							canvas.DrawRoundRect(new(progressBackgroundRect, progressBackgroundHeight / 2f), progressBackgroundPaint);
							canvas.DrawRoundRect(new(progressForegroundRect, progressForegroundHeight / 2f), progressForegroundPaint);
						}

						// draw dot on top-right
						if (this.taskBarProgressState != TaskbarIconProgressState.Normal)
						{
							var centerX = (int)(dockTileWidth * 0.87 + 0.5);
							var centerY = (int)(dockTileHeight * 0.13 + 0.5);
							var radius = (int)(dockTileWidth * 0.1 + 0.5);
							var borderWidth = (int)(dockTileWidth * 0.015 + 0.5);
							canvas.DrawCircle(centerX, centerY, radius + borderWidth, progressBackgroundPaint);
							canvas.DrawCircle(centerX, centerY, radius, progressForegroundPaint);
						}
					});

					// create new image for overlay
					this.macOSAppDockTileOverlayImageView!.Image = null;
					this.macOSAppDockTileOverlayNSImage?.Release();
					this.macOSAppDockTileOverlayCGImage?.Release();
					this.macOSAppDockTileOverlayCGImage = new CGImage(
						this.macOSAppDockTileOverlayBitmap!.Width,
						this.macOSAppDockTileOverlayBitmap.Height,
						CGImagePixelFormatInfo.Packed,
						8,
						CGImageByteOrderInfo.ByteOrderDefault,
						this.macOSAppDockTileOverlayBitmap.RowBytes,
						CGImageAlphaInfo.AlphaLast,
						this.macOSAppDockTileOverlayBitmapBufferProvider!,
						CGColorSpace.SRGB
					);

					// show overlay image
					this.macOSAppDockTileOverlayNSImage = NSImage.FromCGImage(this.macOSAppDockTileOverlayCGImage);
					this.macOSAppDockTileOverlayImageView!.Image = this.macOSAppDockTileOverlayNSImage;
					break;
				default:
					if (this.macOSAppDockTileOverlayNSImage is not null)
					{
						this.macOSAppDockTileOverlayImageView!.Image = null;
						this.macOSAppDockTileOverlayNSImage.Release();
						this.macOSAppDockTileOverlayNSImage = null;
					}
					if (this.macOSAppDockTileOverlayCGImage is not null)
					{
						this.macOSAppDockTileOverlayCGImage.Release();
						this.macOSAppDockTileOverlayCGImage = null;
					}
					this.macOSAppDockTile?.Let(it =>
						it.BadgeLabel = null);
					break;
			}
			this.macOSAppDockTile?.Display();
			this.SynchronizationContext.PostDelayed(() => // [Workaround] Make sure that dock tile redraws as expected
				this.macOSAppDockTile?.Display(), 100);
		}


		// Update progress and state of task bar icon.
		public void UpdateTaskBarProgress(Avalonia.Controls.Window window, TaskbarIconProgressState state, double progress)
		{
			this.taskBarProgress = progress;
			this.taskBarProgressState = state;
			if (Platform.IsWindows)
			{
				if (!this.SetupWindowsTaskbarList())
					return;
				var hWnd = (window.TryGetPlatformHandle()?.Handle).GetValueOrDefault();
				if (hWnd == default)
					return;
				this.windowsTaskbarList.SetProgressValue(hWnd, (ulong)(this.taskBarProgress * 1000 + 0.5), 1000UL);
				this.windowsTaskbarList.SetProgressState(hWnd, this.taskBarProgressState switch
				{
					TaskbarIconProgressState.Error => Win32.TBPF.ERROR,
					TaskbarIconProgressState.Indeterminate => Win32.TBPF.INDETERMINATE,
					TaskbarIconProgressState.Normal => Win32.TBPF.NORMAL,
					TaskbarIconProgressState.Paused => Win32.TBPF.PAUSED,
					_ => Win32.TBPF.NOPROGRESS,
				});
			}
			else if (Platform.IsMacOS)
				this.updateMacOSAppDockTileProgressAction?.Schedule();
		}


		// Wait for logger configuration ready.
		internal Task WaitForLoggerReadyAsync(CancellationToken cancellationToken = default)
		{
			if (this.isLoggerReadyEvent.Wait(0))
				return Task.CompletedTask;
			return Task.Run(() =>
			{
				while (true)
				{
					if (cancellationToken.IsCancellationRequested)
						throw new TaskCanceledException();
					if (this.isLoggerReadyEvent.Wait(1000))
						break;
				}
			}, cancellationToken);
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
