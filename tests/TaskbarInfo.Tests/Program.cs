using System.Buffers.Binary;
using System.Text.Json;
using TaskbarInfo;

var tests = new (string Name, Action Test)[]
{
    ("Clone copies app filter list independently", CloneCopiesAppFilterListIndependently),
    ("No track uses a playback prompt instead of a loading prompt", NoTrackUsesPlaybackPrompt),
    ("Lyric synchronization avoids redundant high-frequency rendering", LyricSynchronizationAvoidsRedundantHighFrequencyRendering),
    ("Settings path is user-local and build-independent", SettingsPathIsUserLocalAndBuildIndependent),
    ("Settings host preserves runtime component positions", SettingsHostPreservesRuntimeComponentPositions),
    ("Settings host opens at most one window", SettingsHostOpensAtMostOneWindow),
    ("Settings host is prewarmed and reused", SettingsHostIsPrewarmedAndReused),
    ("Taskbar performance defaults are compact", TaskbarPerformanceDefaultsAreCompact),
    ("Enhanced temperature mode defaults off and rejects invalid tokens", EnhancedTemperatureModeDefaultsOffAndRejectsInvalidTokens),
    ("Taskbar performance selection removes unknown and duplicate metrics", TaskbarPerformanceSelectionIsNormalized),
    ("Taskbar performance formatter follows selected metrics", TaskbarPerformanceFormatterFollowsSelection),
    ("Taskbar performance formatter splits detail labels and values", TaskbarPerformanceFormatterSplitsDetailValues),
    ("Taskbar performance formatter shows temperatures", TaskbarPerformanceFormatterShowsTemperatures),
    ("Windows storage temperature parser handles descriptor readings", WindowsStorageTemperatureParserHandlesDescriptorReadings),
    ("Temperature sources merge in precedence order", TemperatureSourcesMergeInPrecedenceOrder),
    ("Taskbar performance formatter supports two lines", TaskbarPerformanceFormatterSupportsTwoLines),
    ("Taskbar performance details detect outside clicks", TaskbarPerformanceDetailsDetectOutsideClicks),
    ("Taskbar performance layout supports an independent drag offset", TaskbarPerformanceLayoutSupportsIndependentDragOffset),
    ("Taskbar component drag handles share visual metrics", TaskbarComponentDragHandlesShareVisualMetrics),
    ("Taskbar performance layout adapts to font metrics and DPI", TaskbarPerformanceLayoutAdaptsToFontMetricsAndDpi),
    ("Taskbar performance collector caches network interfaces", TaskbarPerformanceCollectorCachesNetworkInterfaces),
    ("Taskbar performance collector caches temperature readings", TaskbarPerformanceCollectorCachesTemperatureReadings),
    ("Taskbar performance collector emits native snapshot", TaskbarPerformanceCollectorEmitsNativeSnapshot),
    ("Desktop widget defaults to dark theme", DesktopWidgetDefaultsToDarkTheme),
    ("Floating lyric shadow defaults to off", FloatingLyricShadowDefaultsToOff),
    ("Desktop widget palettes differ by theme", DesktopWidgetPalettesDifferByTheme),
    ("Desktop widget applies selected theme", DesktopWidgetAppliesSelectedTheme),
    ("Desktop widget formats playback time", DesktopWidgetFormatsPlaybackTime),
    ("Desktop widget scales to monitor DPI", DesktopWidgetScalesToMonitorDpi),
    ("Desktop widget clamps inside offset monitor", DesktopWidgetClampsInsideOffsetMonitor),
    ("Settings window track limits scale to current DPI", SettingsWindowTrackLimitsScaleToCurrentDpi),
    ("Settings window material defaults and applies from about page", SettingsWindowMaterialDefaultsAndAppliesFromAboutPage),
    ("Desktop host locates Explorer desktop view", DesktopHostLocatesExplorerDesktopView),
    ("Floating marquee keeps two complete lyric copies", FloatingMarqueeKeepsTwoCompleteLyricCopies),
    ("Floating bubble width is explicit and includes padding", FloatingBubbleWidthIsExplicitAndIncludesPadding),
    ("Floating bubble native width matches logical width", FloatingBubbleNativeWidthMatchesLogicalWidth),
    ("Floating marquee panel is not layout clipped", FloatingMarqueePanelIsNotLayoutClipped),
    ("Floating marquee defers updates during native width resize", FloatingMarqueeDefersUpdatesDuringNativeWidthResize),
    ("Untimed floating lyric disables active color", UntimedFloatingLyricDisablesActiveColor),
    ("Taskbar monitor selection uses configured display", TaskbarMonitorSelectionUsesConfiguredDisplay),
    ("Taskbar monitor selection falls back to primary taskbar", TaskbarMonitorSelectionFallsBackToPrimaryTaskbar),
    ("Taskbar component monitor assignments inherit legacy lyric display", TaskbarComponentMonitorAssignmentsInheritLegacyLyricDisplay),
    ("Taskbar components resolve their dedicated display", TaskbarComponentsResolveDedicatedDisplay),
    ("Taskbar component settings expose independent display selectors", TaskbarComponentSettingsExposeIndependentDisplaySelectors),
    ("Taskbar application is PerMonitorV2 aware", TaskbarApplicationUsesPerMonitorV2),
    ("Baidu translation response combines text segments", BaiduTranslationResponseCombinesTextSegments),
    ("Translation services parse built-in cloud responses", TranslationServicesParseBuiltInCloudResponses),
    ("Quick translate opens above a bottom taskbar", QuickTranslateOpensAboveBottomTaskbar),
    ("Quick translate opens below a top taskbar", QuickTranslateOpensBelowTopTaskbar),
    ("Quick translate placement clamps on an offset monitor", QuickTranslatePlacementClampsOnOffsetMonitor),
    ("Taskbar translate layout is independent from lyrics", TaskbarTranslateLayoutIsIndependentFromLyrics),
    ("Taskbar translate button defaults to visible", TaskbarTranslateButtonDefaultsToVisible),
    ("Taskbar translate button window initializes", TaskbarTranslateButtonWindowInitializes),
    ("Taskbar translate button exposes a settings-only menu", TaskbarTranslateButtonExposesSettingsMenu),
    ("Taskbar component menus exclude application commands", TaskbarComponentMenusExcludeApplicationCommands),
    ("Settings host opens quick translate page from taskbar menu", SettingsHostOpensQuickTranslatePageFromTaskbarMenu),
    ("Performance details reveal after final positioning", PerformanceDetailsRevealAfterFinalPositioning),
    ("Settings navigation groups lyric component pages", SettingsNavigationGroupsLyricComponentPages),
    ("Quick translate launch arguments preserve monitor coordinates", QuickTranslateLaunchArgumentsPreserveMonitorCoordinates),
    ("Translation configuration keeps provider credentials", TranslationConfigurationKeepsProviderCredentials),
    ("Translation domains retain General and custom choices", TranslationDomainsRetainGeneralAndCustomChoices),
    ("Quick translate target language restores a supported selection", QuickTranslateTargetLanguageRestoresSupportedSelection),
    ("AI phonetic option persists and extends AI prompts", AiPhoneticOptionPersistsAndExtendsAiPrompts),
    ("Translation provider profiles migrate legacy credentials", TranslationProviderProfilesMigrateLegacyCredentials),
    ("Translation provider catalog exposes supported built-ins", TranslationProviderCatalogExposesSupportedBuiltIns),
    ("AI translation providers expose compatible model routes", AiTranslationProvidersExposeCompatibleModelRoutes),
    ("AI model suggestions close when the editor loses focus", AiModelSuggestionsCloseWhenEditorLosesFocus),
    ("AI translation providers preserve prompts and build SiliconFlow requests", AiTranslationProvidersPreservePromptsAndBuildSiliconFlowRequests),
    ("New translation provider profile is an empty draft", NewTranslationProviderProfileIsAnEmptyDraft),
    ("Quick translate settings page scrolls as a whole", QuickTranslateSettingsPageScrollsAsAWhole),
    ("Quick translate settings use compact heading hierarchy", QuickTranslateSettingsUseCompactHeadingHierarchy),
    ("Quick translate host launcher emits mode and geometry", QuickTranslateHostLauncherEmitsModeAndGeometry),
    ("Quick translate popup collapses its inactive status area", QuickTranslatePopupCollapsesInactiveStatusArea),
    ("Quick translate popup balances idle and result layouts", QuickTranslatePopupBalancesIdleAndResultLayouts),
    ("Quick translate popup caches its target language", QuickTranslatePopupCachesTargetLanguage),
    ("Quick translate popup shows cancellable translation progress", QuickTranslatePopupShowsCancellableTranslationProgress),
    ("Quick translate popup uses responsive transitions", QuickTranslatePopupUsesResponsiveTransitions),
    ("Quick translate reveals domains for AI providers", QuickTranslateRevealsDomainsForAiProviders),
    ("Quick translate popup uses a borderless WPF frame", QuickTranslatePopupUsesBorderlessWpfFrame),
    ("Quick translate button closes an existing popup", QuickTranslateButtonClosesExistingPopup),
    ("Quick translate popup provides an always-on-top control", QuickTranslatePopupProvidesAlwaysOnTopControl),
    ("Quick translate hotkey parses supported gestures", QuickTranslateHotkeyParsesSupportedGestures),
    ("Quick translate material normalizes configured values", QuickTranslateMaterialNormalizesConfiguredValues),
    ("Quick translate uses an in-process WPF acrylic popup", QuickTranslateUsesInProcessWpfPopup),
    ("Quick translate acrylic reapplies after popup rendering", QuickTranslateAcrylicReappliesAfterPopupRendering),
    ("Tray menu excludes lyric component controls", TrayMenuExcludesLyricComponentControls),
    ("Update dialog uses a compact centered layout and settings material", UpdateDialogUsesCompactCenteredLayoutAndSettingsMaterial),
    ("Quick translate font setting is independent", QuickTranslateFontSettingIsIndependent),
};

var failures = new List<string>();

foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

static void SettingsPathIsUserLocalAndBuildIndependent()
{
    string localApplicationData = @"C:\Users\TestUser\AppData\Local";
    string expected = System.IO.Path.Combine(localApplicationData, "TaskbarInfo", "settings.json");

    AssertEqual(expected, AppSettings.GetSettingsPath(localApplicationData),
        "settings path should be based on LocalAppData, not the executable directory");

    if (AppSettings.SettingsPath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("settings path must not be stored beside a development build executable");
    }
}

static void NoTrackUsesPlaybackPrompt()
{
    AssertEqual("开始播放音乐吧", new MediaTrackInfo().DisplayText,
        "no media session should use the playback prompt");

    foreach (string markupFile in new[] { "MainWindow.xaml", "FloatingLyricsWindow.xaml" })
    {
        string markup = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, markupFile));
        AssertEqual(false, markup.Contains("歌词加载中", StringComparison.Ordinal),
            $"{markupFile} should not show a lyric-loading prompt without a track");
        AssertEqual(true, markup.Contains("开始播放音乐吧", StringComparison.Ordinal),
            $"{markupFile} should provide a playback prompt without a track");
    }
}

static void LyricSynchronizationAvoidsRedundantHighFrequencyRendering()
{
    string code = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));

    AssertEqual(true, code.Contains("Interval = TimeSpan.FromMilliseconds(100)", StringComparison.Ordinal),
        "lyric synchronization should poll line changes at no more than 10 FPS");
    AssertEqual(false, code.Contains("Dispatcher.Invoke(() => UpdateLyricsUI(position));", StringComparison.Ordinal),
        "playback timeline events should update the time anchor without duplicating lyric rendering");
    AssertEqual(true, code.Contains("else if (current.HasSyllables)", StringComparison.Ordinal),
        "plain LRC lines should skip repeated progress rendering");
    AssertEqual(false, code.Contains("BeginAnimation(GradientStop.OffsetProperty", StringComparison.Ordinal),
        "taskbar lyric progress should avoid a continuously rendered gradient animation");
}

static void SettingsHostPreservesRuntimeComponentPositions()
{
    string code = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));

    AssertEqual(true, code.Contains("PreserveRuntimeComponentPositions();", StringComparison.Ordinal),
        "settings saves should merge current runtime component positions before writing");
    AssertEqual(true, code.Contains(
        "_settings.TaskbarPerformanceOffsetX = currentSettings.TaskbarPerformanceOffsetX;",
        StringComparison.Ordinal),
        "ordinary settings saves must retain the latest performance position");
    AssertEqual(true, code.Contains("_resetTaskbarPerformancePosition = true;", StringComparison.Ordinal),
        "the performance reset command must retain the ability to clear its position");
    AssertEqual(true, code.Contains("_settings.OffsetX = currentSettings.OffsetX;", StringComparison.Ordinal),
        "ordinary settings saves must retain the latest taskbar lyric position");
    AssertEqual(true, code.Contains("_changedTaskbarLyricOffset = true;", StringComparison.Ordinal),
        "editing the taskbar lyric offset in settings must retain precedence");

    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));
    AssertEqual(true, mainWindowCode.Contains("Interlocked.Exchange(ref settingsApplied, 1);", StringComparison.Ordinal),
        "a settings apply event must mark the settings session as reloaded");
    AssertEqual(true, mainWindowCode.Contains(
        "process.ExitCode == 0 && Interlocked.CompareExchange(ref settingsApplied, 0, 0) == 0",
        StringComparison.Ordinal),
        "closing after an apply must not reload settings a second time");
}

static void SettingsHostOpensAtMostOneWindow()
{
    string code = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));

    AssertEqual(true, code.Contains("private System.Diagnostics.Process? _settingsProcess;", StringComparison.Ordinal),
        "main window should retain the settings host process");
    AssertEqual(true, code.Contains("if (TryActivateSettingsHost(initialNavIndex)) return;", StringComparison.Ordinal),
        "opening settings should activate an existing settings window first");
    AssertEqual(true, code.Contains("UnmanagedMethods.SetForegroundWindow", StringComparison.Ordinal),
        "an existing settings window should be brought to the foreground");
}

static void SettingsHostIsPrewarmedAndReused()
{
    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "MainWindow.xaml.cs"));
    string settingsAppCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "SettingsHost", "App.xaml.cs"));
    string settingsWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml.cs"));

    AssertEqual(true, mainWindowCode.Contains("PrewarmSettingsHost", StringComparison.Ordinal),
        "main app should prewarm the settings host after startup");
    AssertEqual(true, mainWindowCode.Contains("--keep-alive --hidden", StringComparison.Ordinal),
        "prewarmed settings host should start hidden and remain reusable");
    AssertEqual(true, mainWindowCode.Contains("FindTopLevelWindowForProcess", StringComparison.Ordinal),
        "reusing a hidden settings host must not rely on Process.MainWindowHandle");
    AssertEqual(true, settingsAppCode.Contains("StartParentProcessMonitor", StringComparison.Ordinal),
        "reusable settings host should exit when its parent exits");
    AssertEqual(true, settingsWindowCode.Contains("AppWindow.Closing += AppWindow_Closing", StringComparison.Ordinal),
        "closing a reusable settings window should hide it instead of restarting next time");
}

static void BaiduTranslationResponseCombinesTextSegments()
{
    const string response = "{\"trans_result\":[{\"dst\":\"你好\"},{\"dst\":\"，世界\"}]}";

    AssertEqual("你好，世界", TranslationService.ExtractBaiduTranslatedText(response),
        "translated segments must be concatenated in order");
}

static void TranslationServicesParseBuiltInCloudResponses()
{
    AssertEqual("你好", TranslationService.ExtractGoogleTranslatedText(
        "{\"data\":{\"translations\":[{\"translatedText\":\"你好\"}]}}"), "Google response");
    AssertEqual("你好", TranslationService.ExtractDeepLTranslatedText(
        "{\"translations\":[{\"text\":\"你好\"}]}"), "DeepL response");
    AssertEqual("你好", TranslationService.ExtractAzureTranslatedText(
        "[{\"translations\":[{\"text\":\"你好\",\"to\":\"zh-Hans\"}]}]"), "Azure response");
    AssertEqual("你好", TranslationService.ExtractTencentTranslatedText(
        "{\"Response\":{\"TargetText\":\"你好\"}}"), "Tencent response");
    AssertEqual("你好", TranslationService.ExtractAlibabaTranslatedText(
        "{\"Data\":{\"Translated\":\"你好\"}}"), "Alibaba response");
    AssertEqual("你好", TranslationService.ExtractVolcengineTranslatedText(
        "{\"TranslationList\":[{\"Translation\":\"你好\"}]}"), "Volcengine response");
    AssertEqual("你好", TranslationService.ExtractHuaweiTranslatedText(
        "{\"translated_text\":\"你好\"}"), "Huawei response");
    AssertEqual("你好", TranslationService.ExtractIFlytekTranslatedText(
        "{\"code\":0,\"data\":{\"result\":{\"trans_result\":{\"dst\":\"你好\"}}}}"), "iFlytek response");

    string translationServiceCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "TranslationService.cs"));
    AssertEqual(true, translationServiceCode.Contains(
        "request.Headers.TryAddWithoutValidation(\"Authorization\", CreateTencentAuthorization",
        StringComparison.Ordinal),
        "Tencent TC3 authorization must bypass the restrictive managed header parser");

    foreach (string provider in TranslationProviderProfiles.BuiltInProviders)
    {
        AssertEqual(true, TranslationService.IsSupportedProvider(provider),
            $"translation service must route {provider}");
    }
}

static void QuickTranslateOpensAboveBottomTaskbar()
{
    var placement = QuickTranslateLayout.GetPlacement(
        new System.Drawing.Rectangle(900, 1040, 32, 40),
        new System.Drawing.Rectangle(0, 1040, 1920, 40),
        new System.Drawing.Rectangle(0, 0, 1920, 1080),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        420,
        260);

    AssertEqual(706, placement.Left, "popup should remain centered on its taskbar button");
    AssertEqual(772, placement.Top, "bottom taskbar should open the popup above itself");
}

static void QuickTranslateOpensBelowTopTaskbar()
{
    var placement = QuickTranslateLayout.GetPlacement(
        new System.Drawing.Rectangle(900, 0, 32, 40),
        new System.Drawing.Rectangle(0, 0, 1920, 40),
        new System.Drawing.Rectangle(0, 0, 1920, 1080),
        new System.Drawing.Rectangle(0, 40, 1920, 1040),
        420,
        260);

    AssertEqual(706, placement.Left, "popup should remain centered on its taskbar button");
    AssertEqual(48, placement.Top, "top taskbar should open the popup below itself");
}

static void QuickTranslatePlacementClampsOnOffsetMonitor()
{
    var placement = QuickTranslateLayout.GetPlacement(
        new System.Drawing.Rectangle(-2380, 700, 32, 40),
        new System.Drawing.Rectangle(-2400, 0, 40, 1440),
        new System.Drawing.Rectangle(-2400, 0, 1920, 1440),
        new System.Drawing.Rectangle(-2360, 0, 1880, 1440),
        420,
        260);

    AssertEqual(-2352, placement.Left, "left taskbar should open toward the monitor work area");
    AssertEqual(590, placement.Top, "vertical placement should remain centered on the button");
}

static void TaskbarTranslateLayoutIsIndependentFromLyrics()
{
    AssertEqual(860, TaskbarTranslateButtonLayout.GetLeftFromTray(1000, 900, null),
        "default translate position should use tray edge, not lyric position");
    AssertEqual(740, TaskbarTranslateButtonLayout.GetLeftFromTray(1000, 900, 128),
        "saved translate offset should move the button left from the tray");
    AssertEqual(128, TaskbarTranslateButtonLayout.GetOffsetForLeft(900, 740),
        "dragged left position should round-trip to its saved offset");
}

static void TaskbarTranslateButtonDefaultsToVisible()
{
    var settings = new AppSettings();

    AssertEqual(true, settings.EnableTaskbarTranslateButton,
        "existing users should keep the translate button visible after upgrade");
    AssertEqual<int?>(null, settings.TaskbarTranslateButtonOffsetX,
        "unset translate offset should select the independent default position");
}

static void TaskbarTranslateButtonWindowInitializes()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarTranslateButtonWindow.xaml"));
    AssertEqual(true, windowXaml.Contains(
        "PreviewMouseLeftButtonDown=\"TranslateButton_PreviewMouseLeftButtonDown\"",
        StringComparison.Ordinal),
        "translate button should begin drag tracking on pointer down");
    AssertEqual(true, windowXaml.Contains(
        "PreviewMouseMove=\"TranslateButton_PreviewMouseMove\"",
        StringComparison.Ordinal),
        "translate button should track horizontal drag movement");
    AssertEqual(true, windowXaml.Contains(
        "PreviewMouseLeftButtonUp=\"TranslateButton_PreviewMouseLeftButtonUp\"",
        StringComparison.Ordinal),
        "translate button should persist the completed drag");

    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarTranslateButtonWindow.xaml.cs"));
    AssertEqual(false, windowCode.Contains("_suppressNextClick", StringComparison.Ordinal),
        "translate button should not suppress ordinary click events after dragging");

    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var window = new TaskbarTranslateButtonWindow();
            var button = window.FindName("TranslateButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("translate button was not found");
            button.ApplyTemplate();
            if (button.Template.FindName("ButtonSurface", button) is not System.Windows.Controls.Border)
            {
                throw new InvalidOperationException("translate button must use the custom hover surface");
            }
            window.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(5)))
    {
        throw new TimeoutException("taskbar translate button window initialization timed out");
    }
    if (failure != null)
    {
        throw new InvalidOperationException("taskbar translate button window failed to initialize", failure);
    }
}

static void SettingsNavigationGroupsLyricComponentPages()
{
    string markupPath = System.IO.Path.Combine(Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml");
    string markup = System.IO.File.ReadAllText(markupPath);
    string expected = """
                <NavigationViewItem Content="歌词组件" SelectsOnInvoked="False" IsExpanded="True">
                    <NavigationViewItem.Icon><SymbolIcon Symbol="MusicInfo" /></NavigationViewItem.Icon>
                    <NavigationViewItem.MenuItems>
                        <NavigationViewItem Content="布局与显示" Tag="Typography" />
                        <NavigationViewItem Content="其他效果" Tag="Visual" />
                        <NavigationViewItem Content="悬浮歌词" Tag="Floating" />
                        <NavigationViewItem Content="桌面歌词" Tag="DesktopWidget" />
                        <NavigationViewItem Content="应用筛选" Tag="Applications" />
                    </NavigationViewItem.MenuItems>
                </NavigationViewItem>
""";

    if (!markup.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("lyric component navigation hierarchy does not match the product structure");
    }

    foreach (string topLevelItem in new[]
    {
        "<NavigationViewItem Content=\"性能监控\" Tag=\"TaskbarPerformance\"",
        "<NavigationViewItem Content=\"快捷翻译\" Tag=\"QuickTranslate\"",
        "<NavigationViewItem Content=\"关于\" Tag=\"About\""
    })
    {
        if (!markup.Contains(topLevelItem, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"missing top-level settings item: {topLevelItem}");
        }
    }

    string codePath = System.IO.Path.Combine(Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml.cs");
    string code = System.IO.File.ReadAllText(codePath);
    foreach (string heading in new[]
    {
        "NewPanel(\"其他效果\"",
        "NewPanel(\"应用筛选\"",
        "NewPanel(\"桌面歌词\""
    })
    {
        if (!code.Contains(heading, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"renamed page heading is missing: {heading}");
        }
    }

    AssertEqual(true, code.Contains("显示任务栏翻译按钮", StringComparison.Ordinal),
        "quick translate settings should provide a taskbar button visibility switch");
}

static void QuickTranslateLaunchArgumentsPreserveMonitorCoordinates()
{
    bool parsed = QuickTranslateLaunchOptions.TryParse([
        "--quick-translate",
        "--anchor-left=-120", "--anchor-top=1040", "--anchor-width=32", "--anchor-height=40",
        "--taskbar-left=-1920", "--taskbar-top=1040", "--taskbar-width=1920", "--taskbar-height=40",
        "--screen-left=-1920", "--screen-top=0", "--screen-width=1920", "--screen-height=1080",
        "--work-left=-1920", "--work-top=0", "--work-width=1920", "--work-height=1040"
    ], out QuickTranslateLaunchOptions options);

    AssertEqual(true, parsed, "quick translate launch arguments should parse");
    AssertEqual(-120, options.ButtonBounds.Left, "button bounds should preserve signed monitor coordinates");
    AssertEqual(-1920, options.TaskbarBounds.Left, "taskbar bounds should preserve signed monitor coordinates");
    AssertEqual(1040, options.WorkArea.Height, "work area height should be retained");
}

static void TranslationConfigurationKeepsProviderCredentials()
{
    var configuration = new TranslationConfiguration(
        "work_youdao",
        "Youdao",
        "youdao-key",
        "youdao-secret",
        "https://openapi.youdao.com/api");

    AssertEqual("work_youdao", configuration.ProviderId, "provider profile ID");
    AssertEqual("Youdao", configuration.Provider, "translation provider");
    AssertEqual("youdao-key", configuration.AppId, "provider app key");
    AssertEqual("youdao-secret", configuration.AppSecret, "provider secret");
    AssertEqual("https://openapi.youdao.com/api", configuration.ApiBaseUrl, "provider endpoint");
}

static void TranslationDomainsRetainGeneralAndCustomChoices()
{
    string[] domains = TranslationDomainCatalog.Normalize(["医学", "通用领域", "医学", " "]).ToArray();
    AssertEqual("通用领域|医学", string.Join('|', domains),
        "domain normalization should retain General and unique custom names");
    AssertEqual("通用领域", TranslationDomainCatalog.ResolveSelected(domains, "法律"),
        "unknown selected domains should fall back to General");

    var configuration = new TranslationConfiguration(
        "provider", "OpenAI", "key", "", "https://api.example.com/v1/chat/completions", "model", "", "医学");
    AssertEqual("医学", configuration.Domain, "translation configuration should carry the selected domain");

    string settingsHostCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml.cs"));
    AssertEqual(true, settingsHostCode.Contains("QuickTranslateDomains", StringComparison.Ordinal) &&
        settingsHostCode.Contains("SelectedQuickTranslateDomain", StringComparison.Ordinal),
        "the settings host must preserve quick translate domain settings");
}

static void QuickTranslateTargetLanguageRestoresSupportedSelection()
{
    AssertEqual("zh-CN", QuickTranslateTargetLanguages.Normalize(null),
        "a missing cached target language should use Simplified Chinese");
    AssertEqual("ja", QuickTranslateTargetLanguages.Normalize("ja"),
        "a supported cached target language should be retained");
    AssertEqual("zh-CN", QuickTranslateTargetLanguages.Normalize("invalid"),
        "an obsolete cached target language should fall back safely");
    AssertEqual("zh-CN", new AppSettings().QuickTranslateTargetLanguage,
        "new settings should default to Simplified Chinese");
}

static void AiPhoneticOptionPersistsAndExtendsAiPrompts()
{
    AssertEqual(false, new AppSettings().EnableQuickTranslateAiPhonetic,
        "AI phonetic generation should remain opt-in by default");

    var configuration = new TranslationConfiguration(
        "provider",
        "OpenAI",
        "key",
        "",
        "https://api.example.com/v1/chat/completions",
        "model",
        GeneratePhonetic: true);
    using JsonDocument document = JsonDocument.Parse(
        TranslationService.CreateOpenAiCompatibleRequestBody("hello", "zh-CN", configuration));
    string prompt = document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
    AssertEqual(true, prompt.Contains("IPA: /.../", StringComparison.Ordinal),
        "enabled AI phonetic generation should request a slash-delimited IPA line");
    AssertEqual("I am a big crab.\nIPA: /aɪ æm ə bɪɡ kræb/",
        TranslationService.NormalizeAiPhoneticFormat("I am a big crab.\nIPA: aɪ æm ə bɪɡ kræb"),
        "bare IPA returned by an AI model should be normalized with slash delimiters");
    AssertEqual("Hello\nIPA: /həˈloʊ/",
        TranslationService.NormalizeAiPhoneticFormat("Hello\n音标： [həˈloʊ]"),
        "a localized bracketed phonetic label should normalize to the same IPA format");

    string settingsHostCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));
    string popupCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));
    AssertEqual(true, settingsHostCode.Contains("AI 生成音标", StringComparison.Ordinal) &&
        settingsHostCode.Contains("EnableQuickTranslateAiPhonetic", StringComparison.Ordinal) &&
        popupCode.Contains("_settings.EnableQuickTranslateAiPhonetic", StringComparison.Ordinal),
        "the settings toggle should persist and reach the quick translate AI request");
}

static void TranslationProviderProfilesMigrateLegacyCredentials()
{
    var profiles = TranslationProviderProfiles.Normalize(
        [],
        "Youdao",
        "",
        "",
        "legacy-key",
        "legacy-secret");

    AssertEqual(1, profiles.Count, "legacy credentials should migrate to one provider profile");
    AssertEqual("Youdao", profiles[0].Provider, "legacy provider kind");
    AssertEqual("legacy-key", profiles[0].AppId, "legacy provider credentials");
    AssertEqual("https://openapi.youdao.com/api", profiles[0].ApiBaseUrl,
        "legacy Youdao profile should receive the official endpoint");
    AssertEqual("https://fanyi-api.baidu.com/api/trans/vip/translate",
        TranslationProviderProfiles.GetDefaultApiBaseUrl("Baidu"),
        "Baidu default endpoint");
    AssertEqual(true, TranslationProviderProfiles.IsValidId("work_youdao-1"),
        "custom IDs should accept letters, digits, dashes, and underscores");
    AssertEqual(false, TranslationProviderProfiles.IsValidId("work provider"),
        "custom IDs should reject whitespace");

    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));
    AssertEqual(true, settingsCode.Contains("添加翻译服务商", StringComparison.Ordinal),
        "settings should add provider configurations instead of switching one global provider");
    AssertEqual(true, settingsCode.Contains("服务商 ID", StringComparison.Ordinal),
        "settings should expose the custom provider ID field");
    AssertEqual(true, settingsCode.Contains("翻译服务商", StringComparison.Ordinal),
        "settings should expose a dedicated provider list");
    AssertEqual(true, settingsCode.Contains("API Base URL", StringComparison.Ordinal),
        "selected provider should expose an API endpoint editor");
    AssertEqual(true, settingsCode.Contains("providerList.SelectionChanged", StringComparison.Ordinal),
        "selecting the provider list should load its detail form");
}

static void TranslationProviderCatalogExposesSupportedBuiltIns()
{
    foreach (string provider in new[]
    {
        "Google", "DeepL", "Azure", "Tencent", "Alibaba", "Volcengine", "Huawei", "iFlytek", "Baidu", "Youdao"
    })
    {
        AssertEqual(provider, TranslationProviderProfiles.NormalizeProvider(provider),
            $"{provider} must be a selectable built-in provider");
        AssertEqual(true, TranslationProviderProfiles.IsValidApiBaseUrl(
            TranslationProviderProfiles.GetDefaultApiBaseUrl(provider)),
            $"{provider} must have a valid default endpoint");
    }

    TranslationProviderProfile xfyun = TranslationProviderProfiles.CreateNew([], "iFlytek");
    AssertEqual("讯飞翻译", xfyun.DisplayName, "iFlytek default display name");
    AssertEqual("", xfyun.ExtraCredential, "new providers must not invent credentials");
    AssertEqual("API Key", TranslationProviderProfiles.GetExtraCredentialLabel("iFlytek"),
        "iFlytek needs an API key in addition to App ID and API Secret");

    TranslationProviderProfile tencent = TranslationProviderProfiles.CreateNew([], "Tencent");
    AssertEqual("腾讯云机器翻译", tencent.DisplayName,
        "Tencent should be labelled as the active Tencent Cloud translation service");
}

static void AiTranslationProvidersExposeCompatibleModelRoutes()
{
    foreach (string provider in new[] { "OpenAI", "DeepSeek", "Qwen", "SiliconFlow", "OpenAICompatible", "Ollama" })
    {
        AssertEqual(provider, TranslationProviderProfiles.NormalizeProvider(provider),
            $"{provider} must be selectable");
        AssertEqual(true, TranslationService.IsSupportedProvider(provider),
            $"translation service must route {provider}");
    }

    AssertEqual("模型 ID", TranslationProviderProfiles.GetExtraCredentialLabel("OpenAI"),
        "AI profiles must expose a model field");
    AssertEqual("你好", TranslationService.ExtractOpenAICompatibleTranslatedText(
        "{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}"), "chat-completions response");
    AssertEqual("你好", TranslationService.ExtractOllamaTranslatedText(
        "{\"message\":{\"content\":\"你好\"}}"), "Ollama response");
    AssertEqual("https://api.example.com/v1/models",
        TranslationService.GetModelListEndpoint("https://api.example.com/v1/chat/completions", false),
        "OpenAI-compatible model endpoint");
    AssertEqual("http://localhost:11434/api/tags",
        TranslationService.GetModelListEndpoint("http://localhost:11434/api/chat", true),
        "Ollama model endpoint");
    AssertEqual("model-a", TranslationService.ExtractOpenAICompatibleModelIds(
        "{\"data\":[{\"id\":\"model-a\"}]} ")[0], "OpenAI model ID");
    AssertEqual("qwen2.5:7b", TranslationService.ExtractOllamaModelIds(
        "{\"models\":[{\"name\":\"qwen2.5:7b\"}]} ")[0], "Ollama model ID");

    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml.cs"));
    AssertEqual(true, settingsCode.Contains("new AutoSuggestBox", StringComparison.Ordinal),
        "model editor must offer searchable candidates");
    AssertEqual(true, settingsCode.Contains("GetAvailableModelsAsync", StringComparison.Ordinal),
        "model candidates must be fetched on demand");
}

static void AiModelSuggestionsCloseWhenEditorLosesFocus()
{
    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml.cs"));
    AssertEqual(true, settingsCode.Contains(
        "extraCredentialBox.LostFocus += (_, _) => extraCredentialBox.IsSuggestionListOpen = false;",
        StringComparison.Ordinal),
        "AI model suggestions must close when the editor loses focus");
}

static void AiTranslationProvidersPreservePromptsAndBuildSiliconFlowRequests()
{
    const string prompt = "Translate into {target_language}. Return only the result.";
    var profile = new TranslationProviderProfile
    {
        Id = "siliconflow",
        DisplayName = "硅基流动",
        Provider = "SiliconFlow",
        AppId = "token",
        ExtraCredential = "Qwen/Qwen2.5-7B-Instruct",
        ApiBaseUrl = "https://api.siliconflow.cn/v1/chat/completions",
        SystemPrompt = prompt
    };

    TranslationProviderProfile normalized = TranslationProviderProfiles.Normalize([profile], "Baidu", "", "", "", "")[0];
    AssertEqual(prompt, normalized.SystemPrompt, "provider normalization must preserve the AI prompt");

    var configuration = new TranslationConfiguration(
        profile.Id,
        profile.Provider,
        profile.AppId,
        profile.AppSecret,
        profile.ApiBaseUrl,
        profile.ExtraCredential,
        profile.SystemPrompt);
    string payload = TranslationService.CreateOpenAiCompatibleRequestBody("hello", "zh-CN", configuration);
    using JsonDocument document = JsonDocument.Parse(payload);

    AssertEqual(false, document.RootElement.GetProperty("stream").GetBoolean(),
        "SiliconFlow translations must request a non-streaming response");
    AssertEqual("Qwen/Qwen2.5-7B-Instruct", document.RootElement.GetProperty("model").GetString(),
        "SiliconFlow must send the selected model ID");
    AssertEqual("Translate into Simplified Chinese. Return only the result.",
        document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString(),
        "the system prompt must substitute the target language");
    AssertEqual("hello", document.RootElement.GetProperty("messages")[1].GetProperty("content").GetString(),
        "the source text must remain a separate user message");

    TranslationConfiguration domainConfiguration = configuration with
    {
        SystemPrompt = "Translate into {target_language}. Domain: {domain}.",
        Domain = "医学"
    };
    using JsonDocument domainDocument = JsonDocument.Parse(
        TranslationService.CreateOpenAiCompatibleRequestBody("hello", "zh-CN", domainConfiguration));
    AssertEqual("Translate into Simplified Chinese. Domain: 医学.",
        domainDocument.RootElement.GetProperty("messages")[0].GetProperty("content").GetString(),
        "the system prompt must substitute the selected domain");

    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));
    AssertEqual(true, settingsCode.Contains("系统提示词", StringComparison.Ordinal) &&
        settingsCode.Contains("{target_language}", StringComparison.Ordinal),
        "AI provider settings should expose a target-language-aware system prompt editor");
}

static void NewTranslationProviderProfileIsAnEmptyDraft()
{
    TranslationProviderProfile profile = TranslationProviderProfiles.CreateNew([]);

    AssertEqual("", profile.Id, "new provider ID should be empty");
    AssertEqual("", profile.DisplayName, "new provider name should be empty");
    AssertEqual("", profile.Provider, "new provider type should be unselected");
    AssertEqual("", profile.AppId, "new provider app ID should be empty");
    AssertEqual("", profile.AppSecret, "new provider secret should be empty");
    AssertEqual("", profile.ApiBaseUrl, "new provider endpoint should be empty");
    AssertEqual("", TranslationProviderProfiles.NormalizeProvider(null),
        "an unselected provider type should remain empty");

    TranslationProviderProfile baidu = TranslationProviderProfiles.CreateNew([], "Baidu");
    AssertEqual("Baidu", baidu.Provider, "built-in Baidu provider type");
    AssertEqual("百度翻译", baidu.DisplayName, "built-in Baidu provider name");
    AssertEqual("https://fanyi-api.baidu.com/api/trans/vip/translate", baidu.ApiBaseUrl,
        "built-in Baidu provider endpoint");

    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));
    AssertEqual(false, settingsCode.Contains("providerTypeBox", StringComparison.Ordinal),
        "settings should not allow changing the provider type after creation");
    AssertEqual(true, settingsCode.Contains("new MenuFlyout()", StringComparison.Ordinal),
        "add provider should open a candidate menu");
    AssertEqual(true, settingsCode.Contains("自定义服务商", StringComparison.Ordinal),
        "candidate menu should offer a custom provider");
    AssertEqual(true, settingsCode.Contains("未配置", StringComparison.Ordinal),
        "empty custom provider fields should have a list placeholder");
    AssertEqual(true, settingsCode.Contains("Grid.SetColumn(removeProviderButton, 2);", StringComparison.Ordinal),
        "remove should be placed in the provider list header");
    AssertEqual(false, settingsCode.Contains("detailPanel.Children.Add(removeProviderButton);", StringComparison.Ordinal),
        "remove should not remain at the bottom of the provider detail panel");
    AssertEqual(true, settingsCode.Contains("new ContentDialog", StringComparison.Ordinal),
        "removing a provider should request confirmation");
    AssertEqual(true, settingsCode.Contains("PrimaryButtonText = \"删除\"", StringComparison.Ordinal),
        "provider removal confirmation should require an explicit delete action");
}

static void QuickTranslateSettingsPageScrollsAsAWhole()
{
    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));

    AssertEqual(true, settingsCode.Contains("var root = new StackPanel", StringComparison.Ordinal),
        "quick translate settings should use content-sized vertical layout");
    AssertEqual(true, settingsCode.Contains("VerticalScrollMode = ScrollMode.Auto", StringComparison.Ordinal),
        "quick translate settings should allow whole-page vertical scrolling");
    AssertEqual(true, settingsCode.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", StringComparison.Ordinal),
        "quick translate settings should show a whole-page scrollbar when needed");
    AssertEqual(false, settingsCode.Contains("var detailViewer = new ScrollViewer", StringComparison.Ordinal),
        "provider details should not have a competing inner scrollbar");
}

static void QuickTranslateSettingsUseCompactHeadingHierarchy()
{
    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));

    AssertEqual(true, settingsCode.Contains(
        "NewPanel(\"快捷翻译\", \"配置任务栏翻译入口与窗口行为。\", titleFontSize: 24)",
        StringComparison.Ordinal),
        "quick translate should not use the oversized default page heading");
    AssertEqual(true, settingsCode.Contains("Text = \"翻译服务商\",\n            FontSize = 16,",
        StringComparison.Ordinal),
        "provider list heading should use a compact section size");
    AssertEqual(true, settingsCode.Contains("var detailTitle = new TextBlock\n        {\n            FontSize = 20,",
        StringComparison.Ordinal),
        "provider detail title should remain distinct without competing with the page heading");
    AssertEqual(true, settingsCode.Contains("SectionHeader(\"任务栏入口\")", StringComparison.Ordinal) &&
        settingsCode.Contains("SectionHeader(\"快捷操作\")", StringComparison.Ordinal) &&
        settingsCode.Contains("var providerSection = new Border", StringComparison.Ordinal),
        "quick translate should separate entry, interaction, and provider configuration groups");
}

static void QuickTranslateHostLauncherEmitsModeAndGeometry()
{
    var options = new QuickTranslateLaunchOptions(
        new System.Drawing.Rectangle(-120, 1040, 32, 40),
        new System.Drawing.Rectangle(-1920, 1040, 1920, 40),
        new System.Drawing.Rectangle(-1920, 0, 1920, 1080),
        new System.Drawing.Rectangle(-1920, 0, 1920, 1040));

    string arguments = QuickTranslateHostLauncher.BuildArguments(
        "E:\\build output\\settings.json",
        options);

    AssertEqual(true, arguments.Contains("--quick-translate", StringComparison.Ordinal),
        "launcher must request WinUI quick translate mode");
    AssertEqual(true, arguments.Contains("--anchor-left=-120", StringComparison.Ordinal),
        "launcher must preserve signed button coordinates");
    AssertEqual(true, arguments.Contains("\"E:\\build output\\settings.json\"", StringComparison.Ordinal),
        "launcher must quote the shared settings path");
}

static void QuickTranslatePopupCollapsesInactiveStatusArea()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));
    int statusStart = windowXaml.IndexOf("x:Name=\"StatusPanel\"", StringComparison.Ordinal);
    if (statusStart < 0)
    {
        throw new InvalidOperationException("quick translate popup must expose a status panel");
    }

    string statusPanel = windowXaml[statusStart..Math.Min(windowXaml.Length, statusStart + 480)];
    AssertEqual(true, statusPanel.Contains("Visibility=\"Collapsed\"", StringComparison.Ordinal),
        "inactive status panel must not reserve vertical space");
}

static void QuickTranslatePopupBalancesIdleAndResultLayouts()
{
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));

    AssertEqual(true, windowXaml.Contains("Height=\"294\"", StringComparison.Ordinal),
        "the empty quick translate popup should use a compact height");
    AssertEqual(true, windowXaml.Contains("Height=\"64\"", StringComparison.Ordinal) &&
        windowXaml.Contains("VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal) &&
        windowCode.Contains("CalculateResultTextBoxHeight", StringComparison.Ordinal) &&
        windowCode.Contains("ResultTextBox.Height", StringComparison.Ordinal) &&
        windowCode.Contains("ResizeForContent(!string.IsNullOrWhiteSpace(ResultTextBox.Text))", StringComparison.Ordinal),
        "a completed translation should size the result area to its content instead of filling spare space");
    AssertEqual(true, windowXaml.Contains("BorderBrush=\"{TemplateBinding BorderBrush}\"", StringComparison.Ordinal),
        "the always-on-top control should render as a framed icon button");
}

static void QuickTranslatePopupCachesTargetLanguage()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));

    AssertEqual(true, windowXaml.Contains(
        "SelectionChanged=\"TargetLanguageBox_SelectionChanged\"",
        StringComparison.Ordinal),
        "target language selection should notify the popup");
    AssertEqual(true, windowCode.Contains("PopulateTargetLanguageBox();", StringComparison.Ordinal) &&
        windowCode.Contains("_settings.QuickTranslateTargetLanguage", StringComparison.Ordinal),
        "the popup should restore and persist its target language");
}

static void QuickTranslatePopupShowsCancellableTranslationProgress()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));

    AssertEqual(true, windowXaml.Contains(
        "<Grid x:Name=\"ProgressPanel\"\n                      Grid.Row=\"7\"",
        StringComparison.Ordinal) &&
        windowXaml.Contains("IsIndeterminate=\"True\"", StringComparison.Ordinal) &&
        windowXaml.Contains("x:Name=\"ResultLabel\"", StringComparison.Ordinal),
        "translation progress should replace the result label without reserving a status row");
    AssertEqual(true, windowCode.Contains("SetTranslationState(true, provider);", StringComparison.Ordinal) &&
        windowCode.Contains("_translationElapsedTimer", StringComparison.Ordinal) &&
        windowCode.Contains("TranslateButton.Content = translating ? \"取消\" : \"翻译\";", StringComparison.Ordinal) &&
        windowCode.Contains("CancelActiveTranslation", StringComparison.Ordinal),
        "the primary action should become cancellation while translation is in progress");
}

static void QuickTranslatePopupUsesResponsiveTransitions()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));

    AssertEqual(true, windowXaml.Contains("x:Name=\"PopupTranslation\"", StringComparison.Ordinal),
        "the popup should have a content transform for its entrance motion");
    AssertEqual(true, windowCode.Contains("BeginOpenAnimation", StringComparison.Ordinal) &&
        windowCode.Contains("AnimatePopupSize", StringComparison.Ordinal) &&
        windowCode.Contains("PopupTransitionDuration", StringComparison.Ordinal),
        "the popup should animate opening and compact-to-result size transitions");
}

static void QuickTranslateRevealsDomainsForAiProviders()
{
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));

    AssertEqual(true, windowXaml.Contains("x:Name=\"DomainBox\"", StringComparison.Ordinal) &&
        windowXaml.Contains("x:Name=\"AddDomainButton\"", StringComparison.Ordinal),
        "quick translate should expose domain selection and custom-domain commands");
    AssertEqual(true, windowCode.Contains("TranslationService.IsAiProvider", StringComparison.Ordinal) &&
        windowCode.Contains("UpdateDomainControls", StringComparison.Ordinal),
        "domain controls should only be visible for AI providers");
    AssertEqual(true, windowCode.Contains("QuickTranslateDomainDialog", StringComparison.Ordinal),
        "custom domains should be added through a focused dialog");
    AssertEqual(true, windowCode.Contains("_isShowingDomainDialog", StringComparison.Ordinal) &&
        windowCode.Contains("!_isAlwaysOnTop && !_isShowingDomainDialog", StringComparison.Ordinal),
        "the parent popup must stay open while the custom-domain dialog is active");
}

static void QuickTranslatePopupUsesBorderlessWpfFrame()
{
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));

    AssertEqual(true, windowXaml.Contains("WindowStyle=\"None\"", StringComparison.Ordinal),
        "taskbar popup should not retain a system title bar");
    AssertEqual(false, windowXaml.Contains("Click=\"Close_Click\"", StringComparison.Ordinal),
        "borderless popup should not retain a manual close command");
    AssertEqual(true, windowCode.Contains("Window_Deactivated", StringComparison.Ordinal),
        "taskbar popup should close after an outside click deactivates it");
    AssertEqual(true, windowCode.Contains("if (!_isAlwaysOnTop && !_isShowingDomainDialog) Close();", StringComparison.Ordinal),
        "a pinned taskbar popup should remain open after deactivation");
    AssertEqual(true, windowXaml.Contains("x:Name=\"ProviderBox\"", StringComparison.Ordinal),
        "quick translate popup should allow provider selection");

    int initialPositioning = windowCode.IndexOf("PositionBeforeShow(options);", StringComparison.Ordinal);
    int showCall = windowCode.IndexOf("Show();", StringComparison.Ordinal);
    if (initialPositioning < 0 || showCall < 0 || initialPositioning > showCall)
    {
        throw new InvalidOperationException(
            "quick translate popup must be positioned before it is activated to avoid a top-left flash");
    }
}

static void QuickTranslateButtonClosesExistingPopup()
{
    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));

    AssertEqual(true, mainWindowCode.Contains("if (_quickTranslatePopup?.IsVisible == true)", StringComparison.Ordinal) &&
        mainWindowCode.Contains("CloseQuickTranslatePopup();", StringComparison.Ordinal),
        "repeated taskbar-button invocation should close an existing popup");
}

static void QuickTranslatePopupProvidesAlwaysOnTopControl()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));

    AssertEqual(true, windowXaml.Contains("x:Name=\"AlwaysOnTopButton\"", StringComparison.Ordinal),
        "popup must expose an always-on-top control");
    AssertEqual(true, windowCode.Contains("HwndTopmost", StringComparison.Ordinal),
        "always-on-top control must use the native topmost z-order");
    AssertEqual(true, windowCode.Contains("GetTargetDpiScale", StringComparison.Ordinal),
        "popup size must scale with DPI so the pin control is not clipped");
}

static void QuickTranslateHotkeyParsesSupportedGestures()
{
    AssertEqual(true, QuickTranslateHotkey.TryParse("Ctrl+Alt+T", out QuickTranslateHotkey hotkey),
        "default quick translate hotkey should parse");
    AssertEqual((uint)3, hotkey.Modifiers, "Ctrl+Alt modifiers");
    AssertEqual((uint)'T', hotkey.VirtualKey, "letter virtual key");
    AssertEqual(false, QuickTranslateHotkey.TryParse("Ctrl+Alt+Unknown", out _),
        "unknown key should be rejected");
    AssertEqual(false, QuickTranslateHotkey.TryParse("Ctrl+T+U", out _),
        "multiple primary keys should be rejected");
    AssertEqual(true, QuickTranslateHotkey.TryCreate(QuickTranslateHotkey.Control | QuickTranslateHotkey.Shift, (uint)'F', out QuickTranslateHotkey captured),
        "captured modifier and key should create a supported hotkey");
    AssertEqual("Ctrl+Shift+F", captured.ToDisplayString(), "captured hotkey display value");
}

static void QuickTranslateMaterialNormalizesConfiguredValues()
{
    AssertEqual(QuickTranslateWindowMaterial.Acrylic,
        QuickTranslateWindowMaterialParser.Parse("Acrylic"), "Acrylic material");
    AssertEqual(QuickTranslateWindowMaterial.Solid,
        QuickTranslateWindowMaterialParser.Parse("solid"), "case-insensitive solid material");
    AssertEqual(QuickTranslateWindowMaterial.Mica,
        QuickTranslateWindowMaterialParser.Parse("unexpected"), "unknown material should fall back to Mica");
}

static void QuickTranslateUsesInProcessWpfPopup()
{
    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));
    string popupPath = System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs");
    string settingsHostAppCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "App.xaml.cs"));

    AssertEqual(true, System.IO.File.Exists(popupPath),
        "the in-process quick translate popup source should exist");
    if (!System.IO.File.Exists(popupPath)) return;

    string popupCode = System.IO.File.ReadAllText(popupPath);
    AssertEqual(true, mainWindowCode.Contains("QuickTranslatePopupWindow? _quickTranslatePopup", StringComparison.Ordinal) &&
        mainWindowCode.Contains("new QuickTranslatePopupWindow", StringComparison.Ordinal),
        "the main process should own a single WPF quick translate popup");
    AssertEqual(false, mainWindowCode.Contains("_quickTranslateProcess", StringComparison.Ordinal) ||
        mainWindowCode.Contains("QuickTranslateHostLauncher.BuildArguments", StringComparison.Ordinal),
        "quick translate should no longer launch a SettingsHost process");
    AssertEqual(false, settingsHostAppCode.Contains("new QuickTranslateWindow", StringComparison.Ordinal),
        "SettingsHost should remain responsible for settings pages only");
    AssertEqual(true, popupCode.Contains("ApplyAcrylicBackdrop", StringComparison.Ordinal) &&
        popupCode.Contains("SetWindowCompositionAttribute", StringComparison.Ordinal),
        "the WPF popup should apply the native acrylic composition effect");
}

static void QuickTranslateAcrylicReappliesAfterPopupRendering()
{
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml"));

    AssertEqual(true, windowXaml.Contains("x:Name=\"RootCard\"", StringComparison.Ordinal),
        "quick translate popup needs a root surface for the selected material");
    AssertEqual(true, windowCode.Contains("ApplyAcrylicBackdrop", StringComparison.Ordinal) &&
        windowCode.Contains("SetWindowCompositionAttribute", StringComparison.Ordinal),
        "Acrylic mode should use the native window acrylic composition effect");
    AssertEqual(true, windowCode.Contains(
        "ContentRendered += Window_ContentRendered", StringComparison.Ordinal) &&
        windowCode.Contains("Dispatcher.BeginInvoke", StringComparison.Ordinal),
        "native acrylic must be reapplied after the WPF popup finishes rendering");
}

static void TrayMenuExcludesLyricComponentControls()
{
    string markup = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml"));
    string code = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));

    const string trayMenuStart = "<ContextMenu x:Key=\"TrayContextMenu\"";
    int start = markup.IndexOf(trayMenuStart, StringComparison.Ordinal);
    int end = start < 0 ? -1 : markup.IndexOf("</ContextMenu>", start, StringComparison.Ordinal);
    if (start < 0 || end < 0)
    {
        throw new InvalidOperationException("tray icon must use its own context menu");
    }

    string trayMenu = markup[start..(end + "</ContextMenu>".Length)];
    AssertEqual(false, trayMenu.Contains("PreviousTrack_Click", StringComparison.Ordinal),
        "tray menu should not include lyric media controls");
    AssertEqual(false, trayMenu.Contains("FloatingLyrics_Checked", StringComparison.Ordinal),
        "tray menu should not include the floating-lyrics switch");
    AssertEqual(false, trayMenu.Contains("FloatingLyricsCtx_Checked", StringComparison.Ordinal),
        "tray menu should not include the floating-lyrics click-through switch");
    AssertEqual(true, trayMenu.Contains("Header=\"设置\"", StringComparison.Ordinal) &&
        trayMenu.Contains("Header=\"检查更新...\"", StringComparison.Ordinal) &&
        trayMenu.Contains("Header=\"重启\"", StringComparison.Ordinal) &&
        trayMenu.Contains("Header=\"退出\"", StringComparison.Ordinal),
        "tray menu should retain application-level commands");
    AssertEqual(true, code.Contains("FindResource(\"TrayContextMenu\")", StringComparison.Ordinal),
        "tray icon should open its dedicated context menu");
    AssertEqual(true, markup.Contains("<Grid Background=\"Transparent\" Margin=\"12\">", StringComparison.Ordinal),
        "the tray menu should reserve a transparent margin so its rounded shadow is not clipped by the popup bounds");
}

static void UpdateDialogUsesCompactCenteredLayoutAndSettingsMaterial()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "UpdateDialogWindow.xaml"));
    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "UpdateDialogWindow.xaml.cs"));
    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));

    AssertEqual(true, windowXaml.Contains("SizeToContent=\"Height\"", StringComparison.Ordinal) &&
        windowXaml.Contains("x:Name=\"ResultHeader\"", StringComparison.Ordinal) &&
        windowXaml.Contains("HorizontalAlignment=\"Center\"", StringComparison.Ordinal) &&
        windowXaml.Contains("x:Name=\"NotesPanel\"", StringComparison.Ordinal),
        "the update dialog should center its result header and only occupy the height its visible sections need");
    AssertEqual(false, windowXaml.Contains("Content=\"关闭\"", StringComparison.Ordinal),
        "the update dialog should use the title-bar close control instead of a redundant bottom close button");
    AssertEqual(true, windowCode.Contains("NotesPanel.Visibility = Visibility.Collapsed;", StringComparison.Ordinal) &&
        windowCode.Contains("ApplyWindowMaterial", StringComparison.Ordinal) &&
        windowCode.Contains("QuickTranslateWindowMaterialParser.Parse(_settingsWindowMaterial)", StringComparison.Ordinal),
        "the dialog should hide unused notes and apply the configured settings-window material");
    AssertEqual(true, mainWindowCode.Contains(
        "UpdateDialogWindow.ShowForResult(this, result, _settings.SettingsWindowMaterial)",
        StringComparison.Ordinal) &&
        mainWindowCode.Contains(
            "UpdateDialogWindow.ShowForError(this, result.ErrorMessage ?? \"发生了未知错误。\", _settings.SettingsWindowMaterial)",
            StringComparison.Ordinal),
        "update checks should pass the active settings-window material to the dialog");
}

static void QuickTranslateFontSettingIsIndependent()
{
    string appSettings = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "AppSettings.cs"));
    string settingsDocument = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));

    AssertEqual(true, appSettings.Contains(
        "public string QuickTranslateFontFamily { get; set; } = \"Microsoft YaHei UI\";",
        StringComparison.Ordinal),
        "the main app must persist an independent quick-translate font family");
    AssertEqual(true, settingsDocument.Contains(
        "public string QuickTranslateFontFamily { get; set; } = \"Microsoft YaHei UI\";",
        StringComparison.Ordinal),
        "the settings host must preserve the same quick-translate font family field");
    AssertEqual(true, settingsDocument.Contains(
        "\"翻译窗口字体\",\n            _settings.QuickTranslateFontFamily,",
        StringComparison.Ordinal) &&
        settingsDocument.Contains(
            "value => _settings.QuickTranslateFontFamily = value",
            StringComparison.Ordinal),
        "quick translate settings should use the shared searchable font-picker pattern");

    string popupCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "QuickTranslatePopupWindow.xaml.cs"));
    AssertEqual(true, popupCode.Contains(
        "ApplyConfiguredFontFamily();",
        StringComparison.Ordinal) &&
        popupCode.Contains(
            "new FontFamily(_settings.QuickTranslateFontFamily)",
            StringComparison.Ordinal),
        "the quick-translate popup should apply only its dedicated font setting");
    AssertEqual(false, popupCode.Contains(
        "TaskbarPerformanceFontFamily",
        StringComparison.Ordinal) ||
        popupCode.Contains("_settings.FontFamily", StringComparison.Ordinal),
        "quick-translate typography must remain independent from performance and lyric fonts");
}

static void TaskbarMonitorSelectionUsesConfiguredDisplay()
{
    var primary = new TaskbarMonitor(new IntPtr(1), "\\\\.\\DISPLAY1");
    var secondary = new TaskbarMonitor(new IntPtr(2), "\\\\.\\DISPLAY2");

    TaskbarMonitor selected = TaskbarMonitorLocator.Select(
        [primary, secondary],
        "\\\\.\\DISPLAY2");

    AssertEqual(secondary, selected, "configured display must select its secondary taskbar");
}

static void TaskbarMonitorSelectionFallsBackToPrimaryTaskbar()
{
    var primary = new TaskbarMonitor(new IntPtr(1), "\\\\.\\DISPLAY1");
    var secondary = new TaskbarMonitor(new IntPtr(2), "\\\\.\\DISPLAY2");

    AssertEqual(primary, TaskbarMonitorLocator.Select([primary, secondary], ""),
        "an empty setting must keep the primary taskbar behavior");
    AssertEqual(primary, TaskbarMonitorLocator.Select([primary, secondary], "\\\\.\\MISSING"),
        "a disconnected display must fall back to the primary taskbar");
}

static void TaskbarComponentMonitorAssignmentsInheritLegacyLyricDisplay()
{
    AssertEqual("\\\\.\\DISPLAY2",
        TaskbarComponentMonitorSelection.Resolve("", "\\\\.\\DISPLAY2"),
        "blank component monitor should inherit the legacy lyric monitor");
    AssertEqual("\\\\.\\DISPLAY3",
        TaskbarComponentMonitorSelection.Resolve("\\\\.\\DISPLAY3", "\\\\.\\DISPLAY2"),
        "an explicit component monitor should take precedence");
    AssertEqual("", TaskbarComponentMonitorSelection.Resolve(null, null),
        "a fresh configuration should retain primary-taskbar fallback behavior");
}

static void TaskbarComponentsResolveDedicatedDisplay()
{
    string performanceCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarPerformanceWindow.xaml.cs"));
    string translateButtonCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarTranslateButtonWindow.xaml.cs"));
    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));

    AssertEqual(true, performanceCode.Contains(
        "FindTaskbarWindow(_settings.TaskbarPerformanceMonitorDeviceName)", StringComparison.Ordinal),
        "performance monitor should resolve its own selected taskbar");
    AssertEqual(true, translateButtonCode.Contains(
        "FindTaskbarWindow(_settings.TaskbarTranslateButtonMonitorDeviceName)", StringComparison.Ordinal),
        "translation button should resolve its own selected taskbar");
    AssertEqual(true, mainWindowCode.Contains(
        "FindTaskbarWindow(_settings.TaskbarTranslateButtonMonitorDeviceName)", StringComparison.Ordinal),
        "translation popup placement should follow the translation button display");
}

static void TaskbarComponentSettingsExposeIndependentDisplaySelectors()
{
    string settingsCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "SettingsHost",
        "MainWindow.xaml.cs"));

    AssertEqual(true, settingsCode.Contains(
        "LabeledDisplaySelector(_settings.TaskbarPerformanceMonitorDeviceName",
        StringComparison.Ordinal),
        "performance page should expose a display selector");
    AssertEqual(true, settingsCode.Contains(
        "LabeledDisplaySelector(_settings.TaskbarTranslateButtonMonitorDeviceName",
        StringComparison.Ordinal),
        "quick translate page should expose a display selector");
}

static void TaskbarApplicationUsesPerMonitorV2()
{
    string projectPath = System.IO.Path.Combine(Environment.CurrentDirectory, "TaskbarInfo.csproj");
    string manifestPath = System.IO.Path.Combine(Environment.CurrentDirectory, "app.manifest");
    string projectCode = System.IO.File.ReadAllText(projectPath);
    string manifestCode = System.IO.File.Exists(manifestPath)
        ? System.IO.File.ReadAllText(manifestPath)
        : string.Empty;

    AssertEqual(true, projectCode.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", StringComparison.Ordinal) &&
        manifestCode.Contains("PerMonitorV2", StringComparison.Ordinal),
        "taskbar windows must use per-monitor DPI awareness before they are hosted by another display's taskbar");
}

static void CloneCopiesAppFilterListIndependently()
{
    var original = new AppSettings();
    original.IncludedAppIds.Add("Spotify");
    original.TranslationProviders.Add(new TranslationProviderProfile { Id = "work", DisplayName = "Work" });

    var clone = original.Clone();
    original.IncludedAppIds.Add("QQMusic");
    original.TranslationProviders[0].DisplayName = "Changed";

    AssertEqual(1, clone.IncludedAppIds.Count, "clone should keep the original app filter snapshot");
    AssertEqual("Spotify", clone.IncludedAppIds[0], "clone should preserve existing app id");
    AssertEqual("Work", clone.TranslationProviders[0].DisplayName,
        "clone should keep an independent provider profile snapshot");
}

static void TaskbarPerformanceDefaultsAreCompact()
{
    var settings = new AppSettings();

    AssertEqual(false, settings.EnableTaskbarPerformanceMonitor, "performance monitor must be opt-in");
    AssertEqual(5, settings.TaskbarPerformanceMetrics.Count, "the default metric list should include ordinary performance data");
    AssertEqual(TaskbarPerformanceMetricCatalog.Cpu, settings.TaskbarPerformanceMetrics[0], "CPU should be the first default metric");
    AssertEqual(1, settings.TaskbarPerformanceRefreshSeconds, "default refresh interval");
    AssertEqual(5, settings.TaskbarPerformanceSummaryMetricCount, "default summary should show non-temperature metrics");
}

static void TaskbarPerformanceSelectionIsNormalized()
{
    var normalized = TaskbarPerformanceMetricCatalog.Normalize([
        "memory", "CPU", "memory", "not-a-metric", " GPU "
    ]);

    AssertEqual(3, normalized.Count, "known metrics should be kept once");
    AssertEqual(TaskbarPerformanceMetricCatalog.Memory, normalized[0], "selection order should be preserved");
    AssertEqual(TaskbarPerformanceMetricCatalog.Cpu, normalized[1], "case-insensitive CPU id");
    AssertEqual(TaskbarPerformanceMetricCatalog.Gpu, normalized[2], "GPU id should be normalized");
}

static void TaskbarPerformanceFormatterFollowsSelection()
{
    var snapshot = new TaskbarPerformanceSnapshot(12.4, 54.1, 0.4, 4.2 * 1024 * 1024, 512, null, null, null);
    string text = TaskbarPerformanceFormatter.Format(snapshot, [
        TaskbarPerformanceMetricCatalog.Memory,
        TaskbarPerformanceMetricCatalog.Download,
        TaskbarPerformanceMetricCatalog.Upload
    ]);

    AssertEqual("内存 54%  ↓ 4.20 MB/s  ↑ 512 B/s", text, "formatter should only include selected metrics");

    string compact = TaskbarPerformanceFormatter.Format(
        snapshot,
        TaskbarPerformanceMetricCatalog.DefaultSelection,
        maxCharacters: 12);
    AssertEqual("CPU 12%", compact, "compact formatter should drop lower-priority metrics");
}

static void TaskbarPerformanceFormatterSplitsDetailValues()
{
    var snapshot = new TaskbarPerformanceSnapshot(12.4, null, null, 4.2 * 1024 * 1024, 0, null, null, null);

    TaskbarPerformanceMetricDisplay? cpu = TaskbarPerformanceFormatter.FormatMetric(
        snapshot, TaskbarPerformanceMetricCatalog.Cpu);
    AssertEqual("CPU", cpu?.Label, "CPU detail label");
    AssertEqual("12%", cpu?.Value, "CPU detail value");

    TaskbarPerformanceMetricDisplay? download = TaskbarPerformanceFormatter.FormatMetric(
        snapshot, TaskbarPerformanceMetricCatalog.Download);
    AssertEqual("↓", download?.Label, "download detail label");
    AssertEqual("4.20 MB/s", download?.Value, "download detail value");

    TaskbarPerformanceMetricDisplay? unavailableTemperature = TaskbarPerformanceFormatter.FormatMetric(
        snapshot, TaskbarPerformanceMetricCatalog.CpuTemperature);
    AssertEqual("CPU", unavailableTemperature?.Label, "temperature detail label");
    AssertEqual("--", unavailableTemperature?.Value, "unavailable temperature detail value");
}

static void TaskbarPerformanceFormatterSupportsTwoLines()
{
    var snapshot = new TaskbarPerformanceSnapshot(12.4, 54.1, 0.4, 4.2 * 1024 * 1024, 512, null, null, null);
    var lines = TaskbarPerformanceFormatter.FormatLines(snapshot, [
        TaskbarPerformanceMetricCatalog.Cpu,
        TaskbarPerformanceMetricCatalog.Memory,
        TaskbarPerformanceMetricCatalog.Download,
        TaskbarPerformanceMetricCatalog.Upload
    ], doubleLine: true);

    AssertEqual("CPU 12%  内存 54%", lines.First, "first performance line");
    AssertEqual("↓ 4.20 MB/s  ↑ 512 B/s", lines.Second, "second performance line");
}

static void TaskbarPerformanceDetailsDetectOutsideClicks()
{
    TaskbarPerformanceDetailsPlacement placement = TaskbarPerformanceDetailsLayout.GetPlacement(
        anchorLeft: 100,
        anchorTop: 1000,
        anchorWidth: 160,
        cardWidth: 128,
        cardHeight: 180,
        spacing: 8);
    AssertEqual(116, placement.Left, "details card should center over its taskbar anchor");
    AssertEqual(812, placement.Top, "details card should open above its taskbar anchor");

    var bounds = new UnmanagedMethods.RECT { Left = 100, Top = 200, Right = 228, Bottom = 360 };

    AssertEqual(true, TaskbarPerformanceDetailsLayout.ContainsScreenPoint(
        bounds, new UnmanagedMethods.POINT { X = 100, Y = 200 }),
        "top-left card pixel should remain inside");
    AssertEqual(true, TaskbarPerformanceDetailsLayout.ContainsScreenPoint(
        bounds, new UnmanagedMethods.POINT { X = 227, Y = 359 }),
        "bottom-right card pixel should remain inside");
    AssertEqual(false, TaskbarPerformanceDetailsLayout.ContainsScreenPoint(
        bounds, new UnmanagedMethods.POINT { X = 228, Y = 250 }),
        "right card edge should be outside");
    AssertEqual(false, TaskbarPerformanceDetailsLayout.ContainsScreenPoint(
        bounds, new UnmanagedMethods.POINT { X = 150, Y = 360 }),
        "bottom card edge should be outside");
}

static void TaskbarPerformanceLayoutSupportsIndependentDragOffset()
{
    var metrics = new[]
    {
        TaskbarPerformanceMetricCatalog.Cpu,
        TaskbarPerformanceMetricCatalog.Memory
    };

    AssertEqual(159, TaskbarPerformanceLayout.GetWidth(metrics),
        "performance width should be fixed from the selected metrics");
    AssertEqual(94, TaskbarPerformanceLayout.GetWidth(metrics, doubleLine: true),
        "two-line metrics should size to the widest line");

    AssertEqual(199, TaskbarPerformanceLayout.GetWidth([
        TaskbarPerformanceMetricCatalog.Cpu,
        TaskbarPerformanceMetricCatalog.Memory,
        TaskbarPerformanceMetricCatalog.Download,
        TaskbarPerformanceMetricCatalog.Upload
    ], doubleLine: true), "two-line metrics should size to the widest line");

    int defaultLeft = TaskbarPerformanceLayout.GetLeftBesideLyrics(
        taskbarWidth: 1200,
        lyricLeft: 700,
        metricIds: metrics);
    AssertEqual(535, defaultLeft, "default performance position should start to the left of lyrics");

    int defaultOffset = TaskbarPerformanceLayout.GetOffsetForLeft(1000, 153, defaultLeft);
    var position = TaskbarPerformanceLayout.GetPosition(
        taskbarWidth: 1200,
        taskbarHeight: 40,
        trayLeft: 1000,
        offsetX: defaultOffset,
        metricIds: metrics);
    AssertEqual(529, position.Left, "derived default offset should preserve the initial position");
    AssertEqual(40, position.Height, "performance component should fill the taskbar height");

    AssertEqual(821, TaskbarPerformanceLayout.GetLeftFromTray(
        taskbarWidth: 1200,
        trayLeft: 1000,
        metricIds: metrics,
        offsetX: 20), "saved performance offset should position the component independently from lyrics");
    AssertEqual(0, TaskbarPerformanceLayout.GetLeftFromTray(
        taskbarWidth: 120,
        trayLeft: 10,
        metricIds: metrics,
        offsetX: 20), "performance component should not leave the taskbar bounds");
}

static void TaskbarPerformanceLayoutAdaptsToFontMetricsAndDpi()
{
    var metrics = new[]
    {
        TaskbarPerformanceMetricCatalog.Cpu,
        TaskbarPerformanceMetricCatalog.Gpu,
        TaskbarPerformanceMetricCatalog.Memory,
        TaskbarPerformanceMetricCatalog.Download,
        TaskbarPerformanceMetricCatalog.Upload
    };

    int compact = TaskbarPerformanceLayout.GetWidth(metrics, doubleLine: true, "Segoe UI", 10, "Normal", 1);
    int large = TaskbarPerformanceLayout.GetWidth(metrics, doubleLine: true, "Segoe UI", 16, "Bold", 1);
    int scaled = TaskbarPerformanceLayout.GetWidth(metrics, doubleLine: true, "Segoe UI", 10, "Normal", 1.5);

    if (large <= compact)
    {
        throw new InvalidOperationException("larger font settings should require more width.");
    }
    if (scaled <= compact)
    {
        throw new InvalidOperationException("high-DPI rendering should scale the physical width.");
    }
}

static void TaskbarComponentDragHandlesShareVisualMetrics()
{
    string lyricMarkup = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, "MainWindow.xaml"));
    string performanceMarkup = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarPerformanceWindow.xaml"));
    string performanceCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarPerformanceWindow.xaml.cs"));

    AssertEqual(true, lyricMarkup.Contains("Width=\"12\" Height=\"24\"", StringComparison.Ordinal) &&
        lyricMarkup.Contains("Margin=\"0,0,4,0\"", StringComparison.Ordinal) &&
        lyricMarkup.Contains("Width=\"4\" Height=\"14\" CornerRadius=\"2\"", StringComparison.Ordinal),
        "lyric drag handle should define the shared visual metrics");
    AssertEqual(true, performanceMarkup.Contains("Width=\"12\"", StringComparison.Ordinal) &&
        performanceMarkup.Contains("Height=\"24\"", StringComparison.Ordinal) &&
        performanceMarkup.Contains("Margin=\"0,0,4,0\"", StringComparison.Ordinal) &&
        performanceMarkup.Contains("Width=\"4\"", StringComparison.Ordinal) &&
        performanceMarkup.Contains("Height=\"14\"", StringComparison.Ordinal) &&
        performanceMarkup.Contains("CornerRadius=\"2\"", StringComparison.Ordinal),
        "performance drag handle should use the lyric handle's size and spacing");
    AssertEqual(true, performanceMarkup.Contains(
        "DataTrigger Binding=\"{Binding IsMouseOver, ElementName=DragHandle}\"",
        StringComparison.Ordinal),
        "performance drag handle should match lyric hover feedback");
    int indicatorStart = performanceMarkup.IndexOf("<Border x:Name=\"DragIndicator\"", StringComparison.Ordinal);
    int indicatorTagEnd = indicatorStart < 0 ? -1 : performanceMarkup.IndexOf('>', indicatorStart);
    if (indicatorStart < 0 || indicatorTagEnd < 0)
    {
        throw new InvalidOperationException("performance drag indicator should be present");
    }
    string indicatorTag = performanceMarkup[indicatorStart..(indicatorTagEnd + 1)];
    AssertEqual(false, indicatorTag.Contains("Background=", StringComparison.Ordinal),
        "performance drag indicator must not use a local background that overrides the hover trigger");
    AssertEqual(false, performanceCode.Contains("DragIndicator.Height =", StringComparison.Ordinal),
        "performance drag indicator height should not be overwritten at runtime for double-line mode");
}

static void TaskbarTranslateButtonExposesSettingsMenu()
{
    string windowXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, "TaskbarTranslateButtonWindow.xaml"));
    AssertEqual(true, windowXaml.Contains("<Button.ContextMenu>", StringComparison.Ordinal),
        "translate button should define a right-click menu");
    AssertEqual(true, windowXaml.Contains("Header=\"设置\"", StringComparison.Ordinal),
        "translate button menu should expose settings");
    AssertEqual(false, windowXaml.Contains("Header=\"重启\"", StringComparison.Ordinal),
        "translate button menu should leave restart to the tray menu");
    AssertEqual(false, windowXaml.Contains("Header=\"退出\"", StringComparison.Ordinal),
        "translate button menu should leave exit to the tray menu");
    AssertEqual(true, windowXaml.Contains("TaskbarTranslateContextMenuStyle", StringComparison.Ordinal),
        "translate button should use the taskbar context menu surface");
    AssertEqual(true, windowXaml.Contains("CornerRadius=\"8\"", StringComparison.Ordinal),
        "translate button menu should use the shared rounded menu treatment");
    AssertEqual(true, windowXaml.Contains("&#xf013;", StringComparison.Ordinal),
        "translate button menu settings command should include a familiar icon");

    string windowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, "TaskbarTranslateButtonWindow.xaml.cs"));
    AssertEqual(true, windowCode.Contains("SettingsRequested", StringComparison.Ordinal),
        "translate button should publish settings command");
    AssertEqual(false, windowCode.Contains("RestartRequested", StringComparison.Ordinal),
        "translate button should not publish restart command");
    AssertEqual(false, windowCode.Contains("ExitRequested", StringComparison.Ordinal),
        "translate button should not publish exit command");
}

static void TaskbarComponentMenusExcludeApplicationCommands()
{
    foreach (var menuTarget in new[]
    {
        ("MainWindow.xaml", "<Border.ContextMenu>", "</Border.ContextMenu>"),
        ("TaskbarPerformanceWindow.xaml", "<Window.ContextMenu>", "</Window.ContextMenu>"),
        ("TaskbarTranslateButtonWindow.xaml", "<Button.ContextMenu>", "</Button.ContextMenu>")
    })
    {
        string markup = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, menuTarget.Item1));
        int start = markup.IndexOf(menuTarget.Item2, StringComparison.Ordinal);
        int end = start < 0 ? -1 : markup.IndexOf(menuTarget.Item3, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException($"{menuTarget.Item1} should define a taskbar component menu");
        }

        string menu = markup[start..(end + menuTarget.Item3.Length)];
        AssertEqual(false, menu.Contains("Header=\"重启\"", StringComparison.Ordinal),
            $"{menuTarget.Item1} should leave restart to the tray menu");
        AssertEqual(false, menu.Contains("Header=\"退出\"", StringComparison.Ordinal),
            $"{menuTarget.Item1} should leave exit to the tray menu");
        AssertEqual(false, menu.Contains("Header=\"检查更新...\"", StringComparison.Ordinal),
            $"{menuTarget.Item1} should leave update checks to the tray menu");
    }

    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "MainWindow.xaml.cs"));
    AssertEqual(false, mainWindowCode.Contains("_taskbarPerformanceWindow.RestartRequested", StringComparison.Ordinal) ||
        mainWindowCode.Contains("_taskbarTranslateButtonWindow.RestartRequested", StringComparison.Ordinal),
        "taskbar components should not forward restart commands");
    AssertEqual(false, mainWindowCode.Contains("_taskbarPerformanceWindow.ExitRequested", StringComparison.Ordinal) ||
        mainWindowCode.Contains("_taskbarTranslateButtonWindow.ExitRequested", StringComparison.Ordinal),
        "taskbar components should not forward exit commands");
    AssertEqual(false, mainWindowCode.Contains("_taskbarPerformanceWindow.CheckForUpdatesRequested", StringComparison.Ordinal),
        "taskbar components should not forward update commands");
}

static void SettingsHostOpensQuickTranslatePageFromTaskbarMenu()
{
    string mainWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, "MainWindow.xaml.cs"));
    AssertEqual(true, mainWindowCode.Contains("OpenSettings(QuickTranslateSettingsPage)", StringComparison.Ordinal),
        "translate button settings command should open its own page");
    AssertEqual(true, mainWindowCode.Contains("SettingsNavigateMessage", StringComparison.Ordinal),
        "main window should navigate a prewarmed settings host");

    string settingsHostCode = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml.cs"));
    AssertEqual(true, settingsHostCode.Contains("\"6\" => \"QuickTranslate\"", StringComparison.Ordinal),
        "settings host should map quick translate page requests");
    AssertEqual(true, settingsHostCode.Contains("SettingsNavigateMessage", StringComparison.Ordinal),
        "settings host should receive navigation requests");
}

static void PerformanceDetailsRevealAfterFinalPositioning()
{
    string detailWindowCode = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, "TaskbarPerformanceDetailsWindow.cs"));
    int prepareInitialPlacement = detailWindowCode.IndexOf("PrepareInitialPlacement(anchor)", StringComparison.Ordinal);
    int hideUntilPositioned = detailWindowCode.IndexOf("_window.Opacity = 0", StringComparison.Ordinal);
    int showWindow = detailWindowCode.IndexOf("_window.Show()", StringComparison.Ordinal);
    int positionWindow = detailWindowCode.IndexOf("PositionAbove(anchor)", StringComparison.Ordinal);
    int reveal = detailWindowCode.IndexOf("_window.Opacity = 1", StringComparison.Ordinal);

    AssertEqual(true, prepareInitialPlacement >= 0 && hideUntilPositioned > prepareInitialPlacement &&
        showWindow > hideUntilPositioned &&
        positionWindow > showWindow && reveal > positionWindow,
        "details card should receive an initial placement before its first frame is shown");
}

static void EnhancedTemperatureModeDefaultsOffAndRejectsInvalidTokens()
{
    AssertEqual(false, new AppSettings().EnableEnhancedTemperatureSensors,
        "enhanced temperature reads must require an explicit opt-in");
    AssertEqual(true, TemperatureHelperProtocol.HasValidToken("dG9rZW4tYQ==", "dG9rZW4tYQ=="),
        "matching helper token should be accepted");
    AssertEqual(false, TemperatureHelperProtocol.HasValidToken("dG9rZW4tYQ==", "dG9rZW4tYg=="),
        "mismatched helper token must be rejected");
}

static void TaskbarPerformanceFormatterShowsTemperatures()
{
    var snapshot = new TaskbarPerformanceSnapshot(null, null, null, 0, 0, 62.4, 51.2, 39.8);
    string text = TaskbarPerformanceFormatter.Format(snapshot, [
        TaskbarPerformanceMetricCatalog.CpuTemperature,
        TaskbarPerformanceMetricCatalog.GpuTemperature,
        TaskbarPerformanceMetricCatalog.DiskTemperature
    ]);

    AssertEqual("CPU 62°C  GPU 51°C  磁盘 40°C", text, "temperature metric formatting");

    var unavailable = TaskbarPerformanceSnapshot.Empty;
    AssertEqual("CPU --", TaskbarPerformanceFormatter.Format(unavailable,
        [TaskbarPerformanceMetricCatalog.CpuTemperature]),
        "an unavailable sensor should remain visible as an unavailable temperature");
}

static void WindowsStorageTemperatureParserHandlesDescriptorReadings()
{
    byte[] descriptor = new byte[56];
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(12), 2);
    BinaryPrimitives.WriteInt16LittleEndian(descriptor.AsSpan(26), 41);
    BinaryPrimitives.WriteInt16LittleEndian(descriptor.AsSpan(42), 49);

    AssertEqual(49d, WindowsStorageTemperatureParser.GetHighestTemperature(descriptor),
        "Windows storage descriptor chooses the hottest valid disk sensor");
}

static void TemperatureSourcesMergeInPrecedenceOrder()
{
    var merged = TaskbarTemperatureSnapshot.Merge(
        new(72, null, 44),
        new(68, 60, null),
        new(null, null, 42));

    AssertEqual(72d, merged.CpuTemperatureCelsius, "HWiNFO wins for CPU");
    AssertEqual(60d, merged.GpuTemperatureCelsius, "LibreHardwareMonitor fills GPU");
    AssertEqual(44d, merged.DiskTemperatureCelsius, "HWiNFO wins for disk");
}

static void TaskbarPerformanceCollectorCachesNetworkInterfaces()
{
    string code = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarPerformanceCollector.cs"));

    AssertEqual(true, code.Contains("NetworkChange.NetworkAddressChanged += _networkAddressChangedHandler", StringComparison.Ordinal),
        "network changes should invalidate the cached interface list");
    AssertEqual(true, code.Contains("NetworkInterfaceRefreshInterval", StringComparison.Ordinal),
        "network interface enumeration should have a bounded refresh interval");
    AssertEqual(true, code.Contains("GetNetworkInterfaces()", StringComparison.Ordinal),
        "network rate sampling should use the cached interface list");
}

static void TaskbarPerformanceCollectorCachesTemperatureReadings()
{
    string code = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "TaskbarPerformanceCollector.cs"));

    AssertEqual(true, code.Contains("TemperatureRefreshInterval = TimeSpan.FromSeconds(2)", StringComparison.Ordinal),
        "temperature reads should have a two-second refresh interval");
    AssertEqual(true, code.Contains("_temperatureReader.Dispose();", StringComparison.Ordinal),
        "the temperature reader should be disposed with the collector");
}

static void TaskbarPerformanceCollectorEmitsNativeSnapshot()
{
    if (!OperatingSystem.IsWindows()) return;

    using var signal = new ManualResetEventSlim();
    TaskbarPerformanceSnapshot? snapshot = null;
    using var collector = new TaskbarPerformanceCollector();
    collector.SnapshotUpdated += (_, value) =>
    {
        snapshot = value;
        signal.Set();
    };
    collector.Start(1);

    if (!signal.Wait(TimeSpan.FromSeconds(4)))
    {
        throw new TimeoutException("performance collector did not emit a snapshot");
    }

    if (snapshot == null || !snapshot.MemoryUsagePercent.HasValue)
    {
        throw new InvalidOperationException("native memory usage was not collected");
    }

    collector.Stop();
    if (collector.IsRunning) throw new InvalidOperationException("collector did not stop");
}

static void DesktopWidgetDefaultsToDarkTheme()
{
    var settings = new AppSettings();
    AssertEqual(false, settings.EnableDesktopWidget, "desktop widget must be opt-in");
    AssertEqual(DesktopWidgetTheme.Dark, settings.DesktopWidgetTheme, "default theme");
}

static void FloatingLyricShadowDefaultsToOff()
{
    var settings = new AppSettings();

    AssertEqual(false, settings.FloatingLyricsEnableShadow,
        "floating lyric text shadow must be disabled by default");
}

static void DesktopWidgetPalettesDifferByTheme()
{
    var dark = DesktopWidgetThemePalette.Get(DesktopWidgetTheme.Dark);
    var light = DesktopWidgetThemePalette.Get(DesktopWidgetTheme.Light);

    AssertEqual("#FF081025", dark.WindowBackground, "dark window background");
    AssertEqual("#FFF4F6FB", light.WindowBackground, "light window background");
    if (dark.PrimaryText == light.PrimaryText)
    {
        throw new InvalidOperationException("theme primary text colors must differ");
    }
}

static void DesktopWidgetAppliesSelectedTheme()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        DesktopWidgetWindow? widget = null;
        try
        {
            widget = new DesktopWidgetWindow(new AppSettings
            {
                DesktopWidgetTheme = DesktopWidgetTheme.Dark
            });
            var card = widget.FindName("CardBorder") as System.Windows.Controls.Border
                ?? throw new InvalidOperationException("desktop widget card was not created");
            var darkBackground = card.Background as System.Windows.Media.SolidColorBrush
                ?? throw new InvalidOperationException("desktop widget card background is not a solid brush");
            AssertEqual("#E6081025", darkBackground.Color.ToString(), "dark theme card background");

            widget.ApplySettings(new AppSettings
            {
                DesktopWidgetTheme = DesktopWidgetTheme.Light
            });

            var background = card.Background as System.Windows.Media.SolidColorBrush
                ?? throw new InvalidOperationException("desktop widget card background is not a solid brush");
            AssertEqual("#E6FFFFFF", background.Color.ToString(),
                $"light theme card background ({background.Color})");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            widget?.Dispose();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(5)))
    {
        throw new TimeoutException("desktop widget theme test timed out");
    }
    if (failure != null)
    {
        throw new InvalidOperationException("desktop widget theme was not applied: " + failure.Message, failure);
    }
}

static void DesktopWidgetFormatsPlaybackTime()
{
    AssertEqual("0:18", DesktopWidgetFormatting.FormatTime(TimeSpan.FromSeconds(18)), "elapsed time");
    AssertEqual("1:03:14", DesktopWidgetFormatting.FormatTime(TimeSpan.FromSeconds(3794)), "long duration");
}

static void DesktopWidgetScalesToMonitorDpi()
{
    var size = DesktopWidgetLayout.GetPixelSize(1.25, 1.25);
    AssertEqual(500, size.Width, "125% widget width");
    AssertEqual(185, size.Height, "125% widget height");
    AssertEqual(3, DesktopWidgetLayout.LyricMaxLines, "visible lyric line count");
}

static void DesktopWidgetClampsInsideOffsetMonitor()
{
    var position = DesktopWidgetLayout.ClampToWorkArea(
        -100,
        20,
        500,
        205,
        -2400,
        140,
        0,
        1430);

    AssertEqual(-500, position.X, "right edge should remain on the left monitor");
    AssertEqual(140, position.Y, "top should respect the monitor's vertical offset");
}

static void SettingsWindowTrackLimitsScaleToCurrentDpi()
{
    var bounds = WindowSizeConstraints.GetTrackSizeBounds(700, 540, 1120, 760, 144);

    AssertEqual(1050, bounds.MinimumWidth, "minimum width should scale at 150% DPI");
    AssertEqual(810, bounds.MinimumHeight, "minimum height should scale at 150% DPI");
    AssertEqual(1680, bounds.MaximumWidth, "maximum width should scale at 150% DPI");
    AssertEqual(1140, bounds.MaximumHeight, "maximum height should scale at 150% DPI");
}

static void SettingsWindowMaterialDefaultsAndAppliesFromAboutPage()
{
    AssertEqual("Mica", new AppSettings().SettingsWindowMaterial,
        "settings window should default to Mica");

    string settingsHostCode = System.IO.File.ReadAllText(System.IO.Path.Combine(
        Environment.CurrentDirectory, "SettingsHost", "MainWindow.xaml.cs"));
    AssertEqual(true, settingsHostCode.Contains("设置窗口材质", StringComparison.Ordinal),
        "about page should expose settings window material");
    AssertEqual(true, settingsHostCode.Contains("ApplyWindowMaterial()", StringComparison.Ordinal),
        "settings host should apply the selected material");
    AssertEqual(true, settingsHostCode.Contains("DesktopAcrylicBackdrop", StringComparison.Ordinal),
        "settings host should support acrylic material");
}

static void DesktopHostLocatesExplorerDesktopView()
{
    if (!OperatingSystem.IsWindows()) return;

    IntPtr desktopView = DesktopHostService.FindDesktopView();
    if (desktopView == IntPtr.Zero)
    {
        throw new InvalidOperationException("Explorer SHELLDLL_DefView was not found.");
    }

    AssertEqual(
        "SHELLDLL_DefView",
        DesktopHostService.GetWindowClassName(desktopView),
        "desktop view class");

    IntPtr desktopHost = DesktopHostService.FindDesktopHost();
    AssertEqual(
        UnmanagedMethods.GetParent(desktopView),
        desktopHost,
        "desktop widget host must be the desktop view parent");

    IntPtr inputHost = DesktopHostService.FindDesktopInputHost();
    string inputHostClass = DesktopHostService.GetWindowClassName(inputHost);
    if (inputHostClass != "SysListView32" && inputHostClass != "SHELLDLL_DefView")
    {
        throw new InvalidOperationException($"Unexpected desktop input host class: {inputHostClass}");
    }
}

static void FloatingMarqueeKeepsTwoCompleteLyricCopies()
{
    double textWidth = FloatingLyricsLayout.GetTextRenderWidth(900.4);
    AssertEqual(903d, textWidth, "text width should include layout rounding and glyph overhang guard");
    AssertEqual(1856d, FloatingLyricsLayout.GetMarqueePanelWidth(textWidth),
        "marquee panel should contain both full copies plus the gap");
}

static void FloatingBubbleWidthIsExplicitAndIncludesPadding()
{
    AssertEqual(225d, FloatingLyricsLayout.GetBubbleWidth(198.2, 12.8, 13.1, 180, 900),
        "automatic bubble width should include both padding edges in logical units");
    AssertEqual(180d, FloatingLyricsLayout.GetBubbleWidth(100, 8, 8, 180, 900),
        "automatic bubble width should respect the responsive minimum");
}

static void FloatingBubbleNativeWidthMatchesLogicalWidth()
{
    RunFloatingWindowLayoutTest(new AppSettings
    {
        FloatingLyricsWidth = null,
        FloatingLyricsFontSize = 18,
        FloatingLyricsUseAcrylic = false,
        FloatingLyricsEnableShadow = false
    }, "你在心上", window =>
    {
        if (window.SizeToContent != System.Windows.SizeToContent.Height)
        {
            throw new InvalidOperationException("floating bubble width must be explicitly managed");
        }

        var background = window.FindName("FloatingBackground") as System.Windows.FrameworkElement
            ?? throw new InvalidOperationException("floating background was not created");
        if (Math.Abs(background.ActualWidth - window.ActualWidth) > 0.01)
        {
            throw new InvalidOperationException("floating background does not fill the HWND width");
        }

        var origin = window.PointToScreen(new System.Windows.Point(0, 0));
        var right = window.PointToScreen(new System.Windows.Point(window.ActualWidth, 0));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        double expectedPixels = Math.Round(window.Width * dpi.DpiScaleX);
        if (Math.Abs((right.X - origin.X) - expectedPixels) > 1)
        {
            throw new InvalidOperationException("native HWND width is not synchronized with WPF DIPs");
        }
    });
}

static void FloatingMarqueePanelIsNotLayoutClipped()
{
    RunFloatingWindowLayoutTest(new AppSettings
    {
        FloatingLyricsWidth = 216,
        FloatingLyricsUseAcrylic = false,
        FloatingLyricsEnableShadow = false
    }, "要穿碎花洋裙和你一起看海边晚霞", window =>
    {
        var panel = window.FindName("MarqueePanel") as System.Windows.Media.Visual
            ?? throw new InvalidOperationException("marquee panel was not created");
        if (System.Windows.Media.VisualTreeHelper.GetClip(panel) != null)
        {
            throw new InvalidOperationException(
                "marquee panel must not retain a viewport-sized layout clip");
        }
    });
}

static void FloatingMarqueeDefersUpdatesDuringNativeWidthResize()
{
    var resize = new FloatingLyricsResizeCoordinator();
    int beforeResize = resize.ScheduleMarqueeUpdate();

    resize.BeginNativeWidthResize();
    int duringResize = resize.ScheduleMarqueeUpdate();

    AssertEqual(false, resize.CanApplyMarqueeUpdate(beforeResize),
        "a queued update before resizing must be invalidated");
    AssertEqual(false, resize.CanApplyMarqueeUpdate(duringResize),
        "a queued update must not re-layout while Windows owns the resize loop");

    resize.EndNativeWidthResize();
    int afterResize = resize.ScheduleMarqueeUpdate();

    AssertEqual(false, resize.CanApplyMarqueeUpdate(duringResize),
        "the deferred resize update must not replay after resizing ends");
    AssertEqual(true, resize.CanApplyMarqueeUpdate(afterResize),
        "only the final post-resize update may re-layout the marquee");
}

static void RunFloatingWindowLayoutTest(
    AppSettings settings,
    string lyric,
    Action<FloatingLyricsWindow> assertion)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        FloatingLyricsWindow? window = null;
        try
        {
            window = new FloatingLyricsWindow(settings);
            window.Show();
            window.UpdateLyrics(lyric);

            var frame = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    window.UpdateLayout();
                    assertion(window);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    window.Close();
                    frame.Continue = false;
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        catch (Exception ex)
        {
            failure = ex;
            window?.Close();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(5)))
    {
        throw new TimeoutException("floating marquee layout test timed out");
    }
    if (failure != null)
    {
        throw new InvalidOperationException("floating marquee layout regression", failure);
    }
}

static void UntimedFloatingLyricDisablesActiveColor()
{
    AssertEqual(false, FloatingLyricsLayout.ShouldUseActiveColor(false),
        "plain LRC lyrics must not paint a synthetic active-color prefix");
    AssertEqual(true, FloatingLyricsLayout.ShouldUseActiveColor(true),
        "word-timed lyrics should retain progress highlighting");
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected {expected}, got {actual}.");
    }
}
