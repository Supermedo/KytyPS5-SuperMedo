// Kyty Launcher (version SuperMedo): a game library and per-game settings for the KytyPS5 emulator. It sits in the emulator
// folder, keeps its settings in launcher-settings.json next to it, and starts kyty_emulator.exe with the
// switches and command line the selected game's settings ask for.
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KytyLauncher;

static class Program {
	[STAThread]
	static void Main() {
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new MainForm());
	}
}

public sealed class GameOptions {
	public int Width { get; set; } = 2560;
	public int Height { get; set; } = 1440;
	public bool Fullscreen { get; set; }
	public int Language { get; set; } = 1;
	public bool FpsCounter { get; set; } = true;
	public string PresentMode { get; set; } = "Immediate";
	public bool AspectFit { get; set; }
	public string UserName { get; set; } = "Kyty";
	public string GamePatch { get; set; } = "";
	public string Build { get; set; } = "pgo";
	public string Profile { get; set; } = "recommended";
	public int FrameGen { get; set; } = 1;
	public bool Vrr { get; set; } = true;
	public bool ShaderWarmup { get; set; } = true;
	public bool RedZone { get; set; } = true;
	public bool GpuBufferPages { get; set; } = true;
	public bool SkipGpuCulling { get; set; } = true;
	public string Preset { get; set; } = "more";
	public Dictionary<string, bool> Effects { get; set; } = new();
	public string ExtraEnvironment { get; set; } = "";
	public string ExtraArguments { get; set; } = "";
}

public sealed class GameEntry {
	public string Folder { get; set; } = "";
	public string TitleId { get; set; } = "";
	public string Name { get; set; } = "";
	public string Version { get; set; } = "";
	public GameOptions Options { get; set; } = new();
}

public sealed class LauncherSettings {
	public string EmulatorRoot { get; set; } = "";
	public string LastGame { get; set; } = "";
	public List<GameEntry> Games { get; set; } = new();
}

// What the emulator knows about one game: Demon's Souls is the only title it is tuned for.
static class Titles {
	public const string DemonsSouls = "PPSA01341";
	public const string DemonsSoulsVersion = "01.007.000";

	public static bool IsDemonsSouls(GameEntry game) => game.TitleId == DemonsSouls;

	public record Effect(string Convar, string Label, string Note);

	// Demon's Souls' own render switches (tools/local/demons-souls-layers.py); the notes are the author's
	// measurements in docs/EXPERIMENTS.md.
	public static readonly Effect[] Effects = {
		new("fog_enable", "Volumetric fog", "about +4.6% FPS when off"),
		new("r_enableSpotShadows", "Torch and spot-light shadows", ""),
		new("r_contactShadowsEnabled", "Contact shadows", "with torch shadows about +3% when off"),
		new("r_enableCascadeShadows", "Sun shadows", "about +8.5% when off, looks flatter"),
		new("r_dofEnable", "Depth of field", ""),
		new("r_bloomEnable", "Bloom", ""),
		new("filmGrain_enable", "Film grain", ""),
	};

	static readonly string[] MoreFpsOff = { "fog_enable", "r_enableSpotShadows", "r_contactShadowsEnabled" };

	public static readonly (string Id, string Name)[] Presets = {
		("full", "Full quality"),
		("more", "More FPS (fog and small shadows off)"),
		("max", "Max FPS (also sun shadows off)"),
		("custom", "Custom"),
	};

	public static Dictionary<string, bool> PresetEffects(string preset) {
		var effects = Effects.ToDictionary(e => e.Convar, _ => true);
		if (preset is "more" or "max")
			foreach (var name in MoreFpsOff) effects[name] = false;
		if (preset == "max") effects["r_enableCascadeShadows"] = false;
		return effects;
	}

	public static string MatchPreset(Dictionary<string, bool> effects) {
		foreach (var (id, _) in Presets) {
			if (id == "custom") continue;
			var expected = PresetEffects(id);
			if (expected.All(pair => effects.TryGetValue(pair.Key, out var on) ? on == pair.Value : pair.Value))
				return id;
		}
		return "custom";
	}

	// The author's release switches (run-windows.ps1 and its launch.json), each keeping the emulator's
	// original behaviour when unset.
	public static readonly string[] RecommendedSwitches = {
		"KYTY_SRT_NATIVE=1", "KYTY_SRT_PREDICATES=1", "KYTY_PREPARATION_SCRATCH=1", "KYTY_PREPARATION_LOOKUP=1",
		"KYTY_PREPARATION_TRIM=1", "KYTY_SPECIALIZATION_GUARD=1", "KYTY_PIPELINE_INDEX=1", "KYTY_BINDING_SCRATCH=1",
		"KYTY_DRAW_RUN_RANGES=1", "KYTY_BUFFER_RESIDENCY=1", "KYTY_COPY_FEEDBACK=1", "KYTY_ASYNC_LOD_STATS=1",
		"KYTY_BACKING_READ=1", "KYTY_STREAM_UPLOAD=1", "KYTY_FRAME_PIPELINE=1", "KYTY_WRITE_WINDOW_HANDOFF=1",
		"KYTY_READBACK_DETACH=1", "KYTY_ASYNC_WRITE_READBACK=1", "KYTY_READBACK_SLOTS=1", "KYTY_ASYNC_UPLOAD=2",
		"KYTY_BUFFER_RECLAIM=1", "KYTY_UNMAP_PROTECT_SKIP=1", "KYTY_BDA_DIRTY_REGIONS=1",
		"KYTY_GLOBAL_BARRIER_DEDUPE=1", "KYTY_RANGE_SET_FAST=1", "KYTY_IMAGE_GRANULES=2",
		"KYTY_TEXTURE_RESOLVE_PAGES=1", "KYTY_PARTIAL_IMAGE_DIRTY=1", "KYTY_PARTIAL_ROW_BANDS=1",
		"KYTY_ASYNC_REPROTECT=1", "KYTY_READBACK_NARROW=1", "KYTY_READBACK_QUEUE=1", "KYTY_IMAGE_BARRIER_DEDUPE=1",
		"KYTY_IMAGE_POOL=1", "KYTY_PENDING_DRAIN=1", "KYTY_VULKAN_RECORDING=1", "KYTY_DEFERRED_SUBMIT=1",
		"KYTY_NATIVE_XPR=1", "KYTY_DRAW_PACKETS=1", "KYTY_NATIVE_XPR_PREDICT=1", "KYTY_NATIVE_IMAGE_PROOF=1",
		"KYTY_ASYNC_XPR_PIPELINES=1", "KYTY_NATIVE_XPR_RELOCATE=1", "KYTY_NATIVE_XPR_STORE_BUDGET=256",
		"KYTY_NATIVE_XPR_KEEP_FRAMES=600", "KYTY_NATIVE_XPR_INSTANCES=1", "KYTY_DISPATCH_BATCH=128",
	};

	public static readonly (int Id, string Name)[] Languages = {
		(0, "Japanese"), (1, "English (US)"), (18, "English (UK)"), (2, "French"), (22, "French (Canada)"),
		(3, "Spanish"), (20, "Spanish (Latin America)"), (4, "German"), (5, "Italian"), (6, "Dutch"),
		(7, "Portuguese"), (17, "Portuguese (Brazil)"), (8, "Russian"), (9, "Korean"),
		(10, "Chinese (Traditional)"), (11, "Chinese (Simplified)"), (12, "Finnish"), (13, "Swedish"),
		(14, "Danish"), (15, "Norwegian"), (16, "Polish"), (19, "Turkish"), (21, "Arabic"), (23, "Czech"),
		(24, "Hungarian"), (25, "Greek"), (26, "Romanian"), (27, "Thai"), (28, "Vietnamese"), (29, "Indonesian"),
	};

	public static readonly (int W, int H)[] Resolutions = { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160) };
}

public sealed class MainForm : Form {
	static readonly Color Accent      = Color.FromArgb(0, 103, 192);
	static readonly Color AccentLight = Color.FromArgb(222, 236, 250);
	static readonly Color Muted       = Color.FromArgb(96, 96, 96);
	static readonly Color Good        = Color.FromArgb(16, 124, 16);
	static readonly Color Warn        = Color.FromArgb(196, 98, 0);
	const string DriverCacheKey       = "1f3c29b70e536c8f5a5e812e793b33ac2eb6858b4810ed98ca2a51b9fa5eed97";
	const string LogName              = "chen_last_run_log.txt";

	readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "launcher-settings.json");
	LauncherSettings settings = new();
	readonly Dictionary<string, Image> icons = new(StringComparer.OrdinalIgnoreCase);

	readonly ListBox gameList = new();
	readonly PictureBox headerIcon = new();
	readonly Label headerName = new(), headerInfo = new(), headerSupport = new(), headerPath = new();
	readonly TabControl tabs = new();

	readonly ComboBox resolution = new(), language = new(), presentMode = new();
	readonly CheckBox fullscreen = new(), fpsCounter = new(), aspectFit = new();
	readonly TextBox userName = new();

	readonly Label inputsStatus = new(), cacheStatus = new(), shadersNote = new();
	readonly Button precompile = new(), checkShaders = new();
	readonly FlowLayoutPanel shadersPanel = new();
	Process precompileProcess;

	readonly TextBox gamePatch = new();

	readonly ComboBox build = new(), profile = new(), frameGen = new();
	readonly Label profileNote = new(), frameGenNote = new();
	readonly CheckBox vrr = new(), warmup = new();

	readonly ComboBox preset = new();
	readonly CheckBox skipCulling = new();
	readonly List<CheckBox> effectBoxes = new();
	readonly TableLayoutPanel effectsTable = new();
	readonly Label noEffects = new();

	readonly CheckBox redZone = new(), gpuPages = new();
	readonly TextBox extraEnv = new(), extraArgs = new(), emulatorRoot = new();

	readonly Button play = new(), stop = new(), measure = new(), openLog = new();
	readonly Label status = new();

	Process running;
	bool loading, starting;

	GameEntry Current => gameList.SelectedItem as GameEntry;
	string Root => string.IsNullOrWhiteSpace(settings.EmulatorRoot) ? AppContext.BaseDirectory : settings.EmulatorRoot;
	string LogPath => Path.Combine(Root, LogName);

	public MainForm() {
		Text          = "Kyty Launcher \u2014 SuperMedo";
		Font          = new Font("Segoe UI", 9.75f);
		ClientSize    = new Size(1080, 720);
		MinimumSize   = new Size(900, 640);
		StartPosition = FormStartPosition.CenterScreen;
		BackColor     = Color.White;
		try {
			Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		} catch {
		}

		BuildLayout();
		LoadSettings();
		RefreshList(settings.LastGame);
		UpdateRunningState();
	}

	// ---- Layout ----

	void BuildLayout() {
		var split = new SplitContainer {
			Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 1, BackColor = Color.FromArgb(225, 225, 225),
		};
		Controls.Add(split);
		split.Panel1.BackColor = Color.FromArgb(246, 246, 246);
		split.Panel2.BackColor = Color.White;

		// Library
		var libraryTitle = new Label {
			Text = "Games", Dock = DockStyle.Top, Height = 44, Padding = new Padding(14, 12, 0, 0),
			Font = new Font(Font.FontFamily, 13f, FontStyle.Bold),
		};
		gameList.Dock           = DockStyle.Fill;
		gameList.BorderStyle    = BorderStyle.None;
		gameList.DrawMode       = DrawMode.OwnerDrawFixed;
		gameList.ItemHeight     = 68;
		gameList.BackColor      = split.Panel1.BackColor;
		gameList.IntegralHeight = false;
		gameList.DrawItem      += DrawGameItem;
		gameList.SelectedIndexChanged += (_, _) => ShowGame();
		var libraryButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10, 8, 0, 0) };
		var addGame        = MakeButton("Add game...", 110);
		var removeGame     = MakeButton("Remove", 90);
		addGame.Click     += (_, _) => AddGame();
		removeGame.Click  += (_, _) => RemoveGame();
		libraryButtons.Controls.AddRange(new Control[] { addGame, removeGame });
		split.Panel1.Controls.Add(gameList);
		split.Panel1.Controls.Add(libraryButtons);
		split.Panel1.Controls.Add(libraryTitle);

		// Header
		var header = new Panel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(20, 16, 20, 8) };
		headerIcon.SizeMode = PictureBoxSizeMode.Zoom;
		headerIcon.Size     = new Size(118, 118);
		headerIcon.Location = new Point(20, 16);
		headerName.Font     = new Font(Font.FontFamily, 18f, FontStyle.Bold);
		headerName.AutoSize = true;
		headerName.Location = new Point(152, 18);
		headerInfo.AutoSize = true;
		headerInfo.ForeColor = Muted;
		headerInfo.Location = new Point(154, 58);
		headerSupport.AutoSize = true;
		headerSupport.Location = new Point(154, 82);
		headerSupport.Font     = new Font(Font, FontStyle.Bold);
		headerPath.AutoSize    = false;
		headerPath.AutoEllipsis = true;
		headerPath.Size        = new Size(560, 20);
		headerPath.Anchor      = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
		headerPath.ForeColor   = Muted;
		headerPath.Font        = new Font(Font.FontFamily, 8.5f);
		headerPath.Location    = new Point(154, 108);
		header.Controls.AddRange(new Control[] { headerIcon, headerName, headerInfo, headerSupport, headerPath });
		header.Resize += (_, _) => headerPath.Width = Math.Max(100, header.ClientSize.Width - headerPath.Left - 20);

		// Settings
		tabs.Dock    = DockStyle.Fill;
		tabs.Padding = new Point(14, 5);
		tabs.TabPages.Add(DisplayTab());
		tabs.TabPages.Add(PerformanceTab());
		tabs.TabPages.Add(ShadersTab());
		tabs.TabPages.Add(EffectsTab());
		tabs.TabPages.Add(AdvancedTab());
		var tabsHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 8) };
		tabsHost.Controls.Add(tabs);

		// Bottom bar
		var bottom = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Color.FromArgb(246, 246, 246) };
		play.Text      = "Play";
		play.Font      = new Font(Font.FontFamily, 14f, FontStyle.Bold);
		play.Size      = new Size(170, 48);
		play.Location  = new Point(16, 14);
		play.FlatStyle = FlatStyle.Flat;
		play.FlatAppearance.BorderSize = 0;
		play.BackColor = Accent;
		play.ForeColor = Color.White;
		play.Cursor    = Cursors.Hand;
		play.Click    += (_, _) => Play();
		// The status line has the full width under the buttons (measurements are long).
		var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(238, 238, 238) };
		status.Dock      = DockStyle.Fill;
		status.TextAlign = ContentAlignment.MiddleLeft;
		status.Padding   = new Padding(14, 0, 8, 0);
		status.AutoEllipsis = true;
		statusBar.Controls.Add(status);
		var actions      = new FlowLayoutPanel {
			Dock = DockStyle.Right, Width = 420, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 20, 12, 0),
		};
		stop.Text     = "Force close";
		measure.Text  = "Measure FPS";
		openLog.Text  = "Open log";
		foreach (var button in new[] { stop, measure, openLog }) StyleButton(button, 120);
		stop.Click    += (_, _) => StopGame();
		measure.Click += async (_, _) => await MeasureFps();
		openLog.Click += (_, _) => OpenLog();
		actions.Controls.AddRange(new Control[] { stop, measure, openLog });
		bottom.Controls.Add(actions);
		bottom.Controls.Add(play);

		split.Panel2.Controls.Add(tabsHost);
		split.Panel2.Controls.Add(header);
		split.Panel2.Controls.Add(bottom);
		split.Panel2.Controls.Add(statusBar);
		split.SplitterDistance = 290;
	}

	TabPage DisplayTab() {
		var table = NewTable();
		foreach (var (w, h) in Titles.Resolutions) resolution.Items.Add($"{w} x {h}");
		resolution.DropDownStyle = ComboBoxStyle.DropDownList;
		AddRow(table, "Window size", resolution, "The game picture is scaled to the window.");
		fullscreen.Text = "Start in full screen (F11 or Alt+Enter switches in the game)";
		AddRow(table, "Full screen", fullscreen, null);
		foreach (var (id, name) in Titles.Languages) language.Items.Add(name);
		language.DropDownStyle = ComboBoxStyle.DropDownList;
		AddRow(table, "Console language", language, "The language the game sees as the console's.");
		fpsCounter.Text = "Show the FPS counter in the top right corner";
		AddRow(table, "FPS counter", fpsCounter, null);
		presentMode.DropDownStyle = ComboBoxStyle.DropDownList;
		presentMode.Items.AddRange(new object[] { "Off (the author's setting)", "On (no tearing)", "Mailbox (fast V-Sync)" });
		AddRow(table, "V-Sync", presentMode, "Off is smoothest on a G-Sync or FreeSync monitor; turn it on if you see the picture tear.");
		aspectFit.Text = "Keep the 16:9 picture in full screen (black bars on wider screens)";
		AddRow(table, "Aspect ratio", aspectFit, null);
		userName.Width     = 220;
		userName.MaxLength = 16;
		AddRow(table, "Player name", userName, "The name the game sees as the console's user (up to 16 letters).");
		foreach (var control in new Control[] { resolution, language, presentMode }) ((ComboBox)control).SelectedIndexChanged += (_, _) => OptionsChanged();
		foreach (var box in new[] { fullscreen, fpsCounter, aspectFit }) box.CheckedChanged += (_, _) => OptionsChanged();
		userName.TextChanged += (_, _) => OptionsChanged();
		return Page("Display", table);
	}

	TabPage PerformanceTab() {
		var table = NewTable();
		build.DropDownStyle = ComboBoxStyle.DropDownList;
		AddRow(table, "Emulator build", build, "\"Optimized\" is the build tuned with the profile of your own play (PGO).");
		profile.DropDownStyle = ComboBoxStyle.DropDownList;
		profile.Items.AddRange(new object[] { "Recommended (all speed-ups)", "Safe (emulator defaults)" });
		AddRow(table, "Speed-ups", profile, "", profileNote);
		frameGen.DropDownStyle = ComboBoxStyle.DropDownList;
		frameGen.Items.AddRange(new object[] { "Off", "2x (one generated frame per real frame)", "3x", "4x" });
		AddRow(table, "Frame Generation", frameGen, "", frameGenNote);
		vrr.Text = "Show each frame as soon as it is ready (smoother)";
		AddRow(table, "Presentation", vrr, null);
		warmup.Text = "Prepare this game's shaders at start-up (up to a minute)";
		AddRow(table, "Shader warmup", warmup, "Without it, effects stutter the first time they appear in each session.");
		foreach (var combo in new[] { build, profile, frameGen }) combo.SelectedIndexChanged += (_, _) => OptionsChanged();
		foreach (var box in new[] { vrr, warmup }) box.CheckedChanged += (_, _) => OptionsChanged();
		return Page("Performance", table);
	}

	// The two shader stores of run-windows.ps1 / precompile-windows.ps1: the background preparation's
	// inputs (made before a start when stale, e.g. after a driver update) and the full precompile.
	TabPage ShadersTab() {
		var table = NewTable();
		inputsStatus.AutoSize = true;
		AddRow(table, "Background preparation", inputsStatus,
		       "Translates the game's shaders in the background while you play. Made again by itself before a start after a graphics driver update (about a minute).");
		cacheStatus.AutoSize = true;
		AddRow(table, "Full precompile", cacheStatus,
		       "Every shader of the game compiled once: no more first-time stutters. Needed again after a graphics driver update.");
		precompile.Text = "Precompile all shaders...";
		StyleButton(precompile, 200);
		checkShaders.Text = "Check again";
		StyleButton(checkShaders, 120);
		shadersPanel.AutoSize = true;
		shadersPanel.Margin   = new Padding(0);
		shadersPanel.Controls.AddRange(new Control[] { precompile, checkShaders });
		AddRow(table, "", shadersPanel,
		       "The precompile opens its own window and takes about an hour with the CPU fully loaded; closing that window stops it, and the next run continues. Don't play meanwhile.");
		shadersNote.AutoSize    = true;
		shadersNote.ForeColor   = Muted;
		shadersNote.MaximumSize = new Size(560, 0);
		AddRow(table, "", shadersNote, null);
		precompile.Click   += (_, _) => StartPrecompile();
		checkShaders.Click += async (_, _) => await RefreshShaderStatus();
		return Page("Shaders", table);
	}

	TabPage EffectsTab() {
		var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
		effectsTable.Dock         = DockStyle.Top;
		effectsTable.AutoSize     = true;
		effectsTable.ColumnCount  = 2;
		effectsTable.Padding      = new Padding(8, 12, 8, 8);
		effectsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
		effectsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		preset.DropDownStyle = ComboBoxStyle.DropDownList;
		foreach (var (_, name) in Titles.Presets) preset.Items.Add(name);
		preset.SelectedIndexChanged += (_, _) => PresetChanged();
		AddRow(effectsTable, "Graphics preset", preset, "The game's own effect switches, applied when it starts.");
		skipCulling.Text = "Skip the game's GPU culling pass (about +6% FPS, same picture)";
		skipCulling.CheckedChanged += (_, _) => OptionsChanged();
		AddRow(effectsTable, "GPU culling", skipCulling, null);
		var first = true;
		foreach (var effect in Titles.Effects) {
			var box = new CheckBox { Text = effect.Label + (effect.Note.Length > 0 ? $"   ({effect.Note})" : ""), Tag = effect.Convar, AutoSize = true };
			box.CheckedChanged += (_, _) => EffectChanged();
			effectBoxes.Add(box);
			AddRow(effectsTable, first ? "Effects" : "", box, null);
			first = false;
		}
		noEffects.Text      = "This game has no game-specific settings: they exist only for Demon's Souls.";
		noEffects.ForeColor = Muted;
		noEffects.AutoSize  = true;
		noEffects.Padding   = new Padding(12, 16, 0, 0);
		noEffects.Dock      = DockStyle.Top;
		host.Controls.Add(effectsTable);
		host.Controls.Add(noEffects);
		var page = new TabPage("Game effects") { BackColor = Color.White };
		page.Controls.Add(host);
		return page;
	}

	TabPage AdvancedTab() {
		var table = NewTable();
		redZone.Text = "Protect the game's stack red zone (prevents rare crashes)";
		AddRow(table, "Stability", redZone,
		       "Keep it on for Demon's Souls. This build's version breaks other games (God of War Sons of Sparta crashes with it), so they start with it off.");
		gpuPages.Text = "Give the game's GPU buffers memory pages of their own";
		AddRow(table, "Demon's Souls patch", gpuPages, "From the author's release package. Switches itself off for other versions.");
		extraEnv.Multiline  = true;
		extraEnv.ScrollBars = ScrollBars.Vertical;
		extraEnv.Height     = 90;
		extraEnv.Width      = 520;
		extraEnv.Font       = new Font("Consolas", 9.5f);
		AddRow(table, "Extra switches", extraEnv, "One KEY=VALUE per line; they override the ones above. KEY= removes one.");
		extraArgs.Width = 520;
		AddRow(table, "Extra arguments", extraArgs, "Added to the emulator's command line, e.g. --gpu 1");
		gamePatch.Width = 380;
		var patchBrowse = MakeButton("Browse...", 90);
		var patchClear  = MakeButton("Clear", 70);
		var patchRow    = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
		patchRow.Controls.AddRange(new Control[] { gamePatch, patchBrowse, patchClear });
		AddRow(table, "Game patch", patchRow, "An etaHEN-style patch file (.json) applied to the game's code at start, e.g. a skip of the intro screens.");
		gamePatch.ReadOnly = true;
		patchBrowse.Click += (_, _) => {
			using var dialog = new OpenFileDialog { Filter = "Game patch (*.json)|*.json|All files|*.*", Title = "Choose a game patch" };
			if (dialog.ShowDialog(this) == DialogResult.OK) gamePatch.Text = dialog.FileName;
		};
		patchClear.Click += (_, _) => gamePatch.Text = "";
		gamePatch.TextChanged += (_, _) => OptionsChanged();
		emulatorRoot.Width = 380;
		var browse  = MakeButton("Browse...", 90);
		var rootRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
		rootRow.Controls.AddRange(new Control[] { emulatorRoot, browse });
		AddRow(table, "Emulator folder", rootRow, "Where the emulator and its shader caches are (normally this launcher's own folder).");
		browse.Click += (_, _) => BrowseRoot();
		emulatorRoot.Leave += (_, _) => {
			settings.EmulatorRoot = emulatorRoot.Text.Trim();
			SaveSettings();
			FillBuilds();
		};
		foreach (var box in new[] { redZone, gpuPages }) box.CheckedChanged += (_, _) => OptionsChanged();
		foreach (var text in new[] { extraEnv, extraArgs }) text.TextChanged += (_, _) => OptionsChanged();
		return Page("Advanced", table);
	}

	static TableLayoutPanel NewTable() {
		var table = new TableLayoutPanel {
			Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 12, 8, 8),
		};
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		return table;
	}

	TabPage Page(string title, Control content) {
		var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
		host.Controls.Add(content);
		var page = new TabPage(title) { BackColor = Color.White };
		page.Controls.Add(host);
		return page;
	}

	// A setting: its name, the control, and a grey note under it (noteLabel: one whose text changes).
	void AddRow(TableLayoutPanel table, string label, Control control, string note, Label noteLabel = null) {
		var row = table.RowCount++;
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		var name = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 4), Font = new Font(Font, FontStyle.Bold) };
		table.Controls.Add(name, 0, row);
		if (control is ComboBox combo && combo.Width < 300) combo.Width = 360;
		if (control is CheckBox box) box.AutoSize = true;
		control.Margin = new Padding(0, 5, 0, note == null ? 6 : 0);
		if (note == null) {
			table.Controls.Add(control, 1, row);
			return;
		}
		var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
		stack.Controls.Add(control);
		noteLabel ??= new Label { Text = note };
		noteLabel.AutoSize    = true;
		noteLabel.ForeColor   = Muted;
		noteLabel.Margin      = new Padding(0, 2, 0, 8);
		noteLabel.MaximumSize = new Size(560, 0);
		stack.Controls.Add(noteLabel);
		table.Controls.Add(stack, 1, row);
	}

	static Button MakeButton(string text, int width) {
		var button = new Button();
		button.Text = text;
		StyleButton(button, width);
		return button;
	}

	static void StyleButton(Button button, int width) {
		button.Size      = new Size(width, 32);
		button.FlatStyle = FlatStyle.System;
		button.Margin    = new Padding(0, 0, 8, 0);
	}

	void DrawGameItem(object sender, DrawItemEventArgs e) {
		if (e.Index < 0) return;
		var game     = (GameEntry)gameList.Items[e.Index];
		var selected = (e.State & DrawItemState.Selected) != 0;
		var g        = e.Graphics;
		g.SmoothingMode     = SmoothingMode.AntiAlias;
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		using (var back = new SolidBrush(selected ? AccentLight : gameList.BackColor)) g.FillRectangle(back, e.Bounds);
		if (selected) {
			using var bar = new SolidBrush(Accent);
			g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + 8, 4, e.Bounds.Height - 16);
		}
		var iconRect = new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 8, 52, 52);
		var icon     = IconFor(game);
		if (icon != null) g.DrawImage(icon, iconRect);
		else g.FillRectangle(Brushes.Gainsboro, iconRect);
		using var nameFont = new Font(Font, FontStyle.Bold);
		var textX = iconRect.Right + 12;
		var width = e.Bounds.Width - textX - 6;
		TextRenderer.DrawText(g, game.Name, nameFont, new Rectangle(textX, e.Bounds.Y + 12, width, 22), Color.Black,
		                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
		TextRenderer.DrawText(g, $"{game.TitleId}   v{game.Version}", Font, new Rectangle(textX, e.Bounds.Y + 36, width, 20), Muted,
		                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
	}

	// ---- Library ----

	void LoadSettings() {
		try {
			if (File.Exists(settingsPath))
				settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(settingsPath)) ?? new LauncherSettings();
		} catch (Exception ex) {
			MessageBox.Show(this, $"The settings file could not be read and starts over:\n{ex.Message}", Text);
			settings = new LauncherSettings();
		}
		emulatorRoot.Text = Root;
		FillBuilds();
	}

	void SaveSettings() {
		try {
			File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
		} catch (Exception ex) {
			status.Text = "Settings not saved: " + ex.Message;
		}
	}

	void RefreshList(string select) {
		gameList.BeginUpdate();
		gameList.Items.Clear();
		foreach (var game in settings.Games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)) gameList.Items.Add(game);
		gameList.EndUpdate();
		var index = settings.Games.Count == 0 ? -1 : 0;
		for (var i = 0; i < gameList.Items.Count; i++)
			if (string.Equals(((GameEntry)gameList.Items[i]).Folder, select, StringComparison.OrdinalIgnoreCase)) index = i;
		gameList.SelectedIndex = index;
		ShowGame();
	}

	// The folder that holds eboot.bin: the one chosen, or the only such folder up to two levels below it.
	static string FindGameFolder(string folder) {
		if (File.Exists(Path.Combine(folder, "eboot.bin"))) return folder;
		try {
			var found = Directory.EnumerateDirectories(folder)
			    .SelectMany(d => new[] { d }.Concat(SafeDirectories(d)))
			    .Where(d => File.Exists(Path.Combine(d, "eboot.bin")))
			    .Take(2)
			    .ToList();
			return found.Count == 1 ? found[0] : null;
		} catch {
			return null;
		}
	}

	static IEnumerable<string> SafeDirectories(string folder) {
		try {
			return Directory.GetDirectories(folder);
		} catch {
			return Array.Empty<string>();
		}
	}

	static GameEntry ReadGame(string folder) {
		var game = new GameEntry { Folder = folder, Name = Path.GetFileName(folder), TitleId = "UNKNOWN", Version = "?" };
		try {
			var param = Path.Combine(folder, "sce_sys", "param.json");
			if (File.Exists(param)) {
				var json = JsonNode.Parse(File.ReadAllText(param));
				game.TitleId = json?["titleId"]?.GetValue<string>() ?? game.TitleId;
				game.Version = json?["contentVersion"]?.GetValue<string>() ?? game.Version;
				var localized = json?["localizedParameters"];
				var language  = localized?["defaultLanguage"]?.GetValue<string>();
				var name      = (language != null ? localized?[language]?["titleName"] : null) ?? localized?["en-US"]?["titleName"];
				if (name == null && localized is JsonObject all)
					name = all.Select(pair => pair.Value?["titleName"]).FirstOrDefault(n => n != null);
				if (name != null) game.Name = name.GetValue<string>();
			}
		} catch {
		}
		if (!Titles.IsDemonsSouls(game)) {
			// Untested games start with the emulator's own behaviour; the speed-ups can be tried.
			game.Options.Profile  = "safe";
			game.Options.FrameGen = 0;
			// This build's red-zone patcher is tuned for Demon's Souls: God of War Sons of Sparta crashed
			// with it (a corrupted argument in its Unity code) and runs without it.
			game.Options.RedZone = false;
		}
		return game;
	}

	Image IconFor(GameEntry game) {
		if (icons.TryGetValue(game.Folder, out var cached)) return cached;
		Image image = null;
		try {
			var path = Path.Combine(game.Folder, "sce_sys", "icon0.png");
			if (File.Exists(path)) {
				using var original = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
				image = new Bitmap(original, new Size(160, 160));
			}
		} catch {
		}
		icons[game.Folder] = image;
		return image;
	}

	void AddGame() {
		using var dialog = new FolderBrowserDialog {
			Description = "Choose the game folder (the one with eboot.bin and sce_sys)", UseDescriptionForTitle = true,
		};
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		var folder = FindGameFolder(dialog.SelectedPath);
		if (folder == null) {
			MessageBox.Show(this, "No game found there: choose the folder that holds eboot.bin and the sce_sys folder.", Text,
			                MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		if (settings.Games.Any(g => string.Equals(g.Folder, folder, StringComparison.OrdinalIgnoreCase))) {
			RefreshList(folder);
			return;
		}
		settings.Games.Add(ReadGame(folder));
		SaveSettings();
		RefreshList(folder);
	}

	void RemoveGame() {
		if (Current is not { } game) return;
		if (MessageBox.Show(this, $"Remove {game.Name} from the list? Its files and saves stay where they are.", Text,
		                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			return;
		settings.Games.Remove(game);
		SaveSettings();
		RefreshList(null);
	}

	void BrowseRoot() {
		using var dialog = new FolderBrowserDialog { Description = "Choose the emulator folder", UseDescriptionForTitle = true, SelectedPath = Root };
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		settings.EmulatorRoot = dialog.SelectedPath;
		emulatorRoot.Text     = dialog.SelectedPath;
		SaveSettings();
		FillBuilds();
		LoadOptions();
	}

	// The emulator builds present in the emulator folder.
	List<(string Id, string Name, string Path)> Builds() {
		var builds = new List<(string, string, string)>();
		void Add(string id, string name, string relative) {
			var path = Path.Combine(Root, relative);
			if (File.Exists(path)) builds.Add((id, name, path));
		}
		Add("pgo", "Optimized for this PC (PGO)", @"_Build\windows-pgo\kyty_emulator.exe");
		Add("standard", "Standard build", @"_Build\windows\kyty_emulator.exe");
		Add("package", "Package build", "kyty_emulator.exe");
		return builds;
	}

	void FillBuilds() {
		var keep = loading;
		loading = true;
		build.Items.Clear();
		foreach (var (_, name, _) in Builds()) build.Items.Add(name);
		loading = keep;
	}

	// ---- Settings of the selected game ----

	void ShowGame() {
		var game = Current;
		tabs.Enabled = game != null;
		play.Enabled = game != null && running == null;
		if (game == null) {
			headerName.Text    = "No game yet";
			headerInfo.Text    = "Add a game folder with \"Add game...\"";
			headerSupport.Text = "";
			headerPath.Text    = "";
			headerIcon.Image   = null;
			return;
		}
		settings.LastGame = game.Folder;
		SaveSettings();
		headerIcon.Image = IconFor(game);
		headerName.Text  = game.Name;
		headerInfo.Text  = $"{game.TitleId}    version {game.Version}";
		headerPath.Text  = game.Folder;
		if (Titles.IsDemonsSouls(game) && game.Version == Titles.DemonsSoulsVersion) {
			headerSupport.Text      = "Tuned for this emulator (tested version)";
			headerSupport.ForeColor = Good;
		} else if (Titles.IsDemonsSouls(game)) {
			headerSupport.Text      = $"Different version: the emulator was tuned for {Titles.DemonsSoulsVersion}";
			headerSupport.ForeColor = Warn;
		} else {
			headerSupport.Text      = "Untested with this emulator: it may not start or may crash";
			headerSupport.ForeColor = Warn;
		}
		LoadOptions();
	}

	void LoadOptions() {
		if (Current is not { } game) return;
		var o   = game.Options;
		var des = Titles.IsDemonsSouls(game);
		loading = true;
		var res = Array.FindIndex(Titles.Resolutions, r => r.W == o.Width && r.H == o.Height);
		resolution.SelectedIndex = res >= 0 ? res : 3;
		fullscreen.Checked       = o.Fullscreen;
		var lang = Array.FindIndex(Titles.Languages, l => l.Id == o.Language);
		language.SelectedIndex = lang >= 0 ? lang : 1;
		fpsCounter.Checked     = o.FpsCounter;
		presentMode.SelectedIndex = o.PresentMode switch { "Fifo" => 1, "Mailbox" => 2, _ => 0 };
		aspectFit.Checked         = o.AspectFit;
		userName.Text             = o.UserName;
		gamePatch.Text            = o.GamePatch;

		var builds = Builds();
		var b      = builds.FindIndex(x => x.Id == o.Build);
		build.SelectedIndex = builds.Count == 0 ? -1 : (b >= 0 ? b : 0);
		profile.SelectedIndex  = o.Profile == "safe" ? 1 : 0;
		frameGen.Enabled       = des;
		frameGen.SelectedIndex = des ? Math.Clamp(o.FrameGen, 0, 3) : 0;
		vrr.Checked            = o.Vrr;
		warmup.Checked         = o.ShaderWarmup;

		effectsTable.Visible = des;
		noEffects.Visible    = !des;
		skipCulling.Checked  = o.SkipGpuCulling;
		var effects = o.Effects.Count == 0 ? Titles.PresetEffects(o.Preset) : o.Effects;
		foreach (var box in effectBoxes) box.Checked = !effects.TryGetValue((string)box.Tag, out var on) || on;
		var p = Array.FindIndex(Titles.Presets, x => x.Id == Titles.MatchPreset(CurrentEffects()));
		preset.SelectedIndex = p >= 0 ? p : 1;

		redZone.Checked  = o.RedZone;
		gpuPages.Enabled = des;
		gpuPages.Checked = des && o.GpuBufferPages;
		extraEnv.Text    = o.ExtraEnvironment.Replace("\r\n", "\n").Replace("\n", "\r\n");
		extraArgs.Text   = o.ExtraArguments;
		loading = false;
		UpdateNotes();
		_ = RefreshShaderStatus();
	}

	Dictionary<string, bool> CurrentEffects() => effectBoxes.ToDictionary(b => (string)b.Tag, b => b.Checked);

	void OptionsChanged() {
		if (loading || Current is not { } game) return;
		var o = game.Options;
		if (resolution.SelectedIndex >= 0) (o.Width, o.Height) = Titles.Resolutions[resolution.SelectedIndex];
		o.Fullscreen = fullscreen.Checked;
		if (language.SelectedIndex >= 0) o.Language = Titles.Languages[language.SelectedIndex].Id;
		o.FpsCounter  = fpsCounter.Checked;
		o.PresentMode = presentMode.SelectedIndex switch { 1 => "Fifo", 2 => "Mailbox", _ => "Immediate" };
		o.AspectFit   = aspectFit.Checked;
		o.UserName    = userName.Text.Trim();
		o.GamePatch   = gamePatch.Text.Trim();
		var builds = Builds();
		if (build.SelectedIndex >= 0 && build.SelectedIndex < builds.Count) o.Build = builds[build.SelectedIndex].Id;
		o.Profile = profile.SelectedIndex == 1 ? "safe" : "recommended";
		if (frameGen.Enabled) o.FrameGen = Math.Max(0, frameGen.SelectedIndex);
		o.Vrr          = vrr.Checked;
		o.ShaderWarmup = warmup.Checked;
		if (Titles.IsDemonsSouls(game)) {
			o.SkipGpuCulling = skipCulling.Checked;
			o.Effects        = CurrentEffects();
			o.Preset         = Titles.MatchPreset(o.Effects);
			o.GpuBufferPages = gpuPages.Checked;
		}
		o.RedZone          = redZone.Checked;
		o.ExtraEnvironment = extraEnv.Text;
		o.ExtraArguments   = extraArgs.Text.Trim();
		SaveSettings();
		UpdateNotes();
	}

	void PresetChanged() {
		if (loading || preset.SelectedIndex < 0) return;
		var id = Titles.Presets[preset.SelectedIndex].Id;
		if (id == "custom") return;
		loading = true;
		var effects = Titles.PresetEffects(id);
		foreach (var box in effectBoxes) box.Checked = effects[(string)box.Tag];
		loading = false;
		OptionsChanged();
	}

	void EffectChanged() {
		if (loading) return;
		loading = true;
		var p = Array.FindIndex(Titles.Presets, x => x.Id == Titles.MatchPreset(CurrentEffects()));
		preset.SelectedIndex = p;
		loading = false;
		OptionsChanged();
	}

	void UpdateNotes() {
		var game = Current;
		profileNote.Text = profile.SelectedIndex == 1
		    ? "The emulator's own behaviour: slower. Try it when a game misbehaves with the speed-ups."
		    : "Every speed-up of the author's release setup (tested on Demon's Souls).";
		if (game != null && !Titles.IsDemonsSouls(game))
			frameGenNote.Text = "Only for Demon's Souls: Frame Generation reads that game's own camera data.";
		else
			frameGenNote.Text = frameGen.SelectedIndex switch {
				0 => "Off: what you see is the real frame rate.",
				1 => "Doubles the frames shown. Needs an NVIDIA RTX 40 or 50 card; controls respond at the real frame rate.",
				_ => "More generated frames: smoother on a high refresh rate monitor, more artifacts in fast motion.",
			};
	}

	// ---- Shaders (what run-windows.ps1 checks before a start, and precompile-windows.ps1) ----

	string ShaderTool() {
		foreach (var relative in new[] { @"_Build\windows-pgo", @"_Build\windows", "" }) {
			var path = Path.Combine(Root, relative, "kyty_shader_precompile.exe");
			if (File.Exists(path)) return path;
		}
		return null;
	}

	// The list of every shader the game ships (Demon's Souls only).
	string SeedsPath() {
		foreach (var relative in new[] { "seeds.seeds", @"_Build\static-precompile\seeds.seeds" }) {
			var path = Path.Combine(Root, relative);
			if (File.Exists(path)) return path;
		}
		return null;
	}

	bool ShadersSupported(GameEntry game) =>
	    game != null && Titles.IsDemonsSouls(game) && ShaderTool() != null && SeedsPath() != null;

	async Task<(int Code, string Output)> RunTool(string arguments) {
		var start = new ProcessStartInfo(ShaderTool(), arguments) {
			WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
			RedirectStandardOutput = true, RedirectStandardError = true,
		};
		using var process = Process.Start(start);
		var output = process.StandardOutput.ReadToEndAsync();
		var errors = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		await errors;
		return (process.ExitCode, await output);
	}

	bool PrecompileRunning => precompileProcess is { HasExited: false };

	// The status of both stores for this graphics card and driver ("inputs current|stale", "static cache current|stale").
	async Task<(bool Inputs, bool Cache)?> ShaderStatus(GameEntry game) {
		try {
			var (code, output) = await RunTool($"--game {Quote(game.Folder)} --seeds {Quote(SeedsPath())} --status");
			if (code != 0) return null;
			// A precompile stopped before its last merge keeps its shards (small ones stay behind after a merge).
			var shards = Directory.Exists(Path.Combine(Root, "_PipelineCache", "static")) &&
			             Directory.EnumerateFiles(Path.Combine(Root, "_PipelineCache", "static"), "*.shard*")
			                 .Any(f => new FileInfo(f).Length > (1 << 20));
			return (output.Contains("inputs current"), output.Contains("static cache current") && !shards);
		} catch {
			return null;
		}
	}

	async Task RefreshShaderStatus() {
		var game = Current;
		var supported = ShadersSupported(game);
		shadersPanel.Enabled = supported && running == null && !PrecompileRunning;
		inputsStatus.ForeColor = cacheStatus.ForeColor = Muted;
		if (!supported) {
			inputsStatus.Text = cacheStatus.Text = "Not available";
			shadersNote.Text = game != null && !Titles.IsDemonsSouls(game)
			    ? "These need the game's list of shaders, which exists only for Demon's Souls."
			    : "The precompile program or the shader list is missing in the emulator folder.";
			return;
		}
		shadersNote.Text = "";
		if (PrecompileRunning) {
			inputsStatus.Text = cacheStatus.Text = "The precompile is running (see its window)...";
			return;
		}
		if (running != null) return; // the check needs the graphics card: not while the game runs
		inputsStatus.Text = cacheStatus.Text = "Checking...";
		var status = await ShaderStatus(game);
		if (Current != game) return;
		if (status is not { } s) {
			inputsStatus.Text = cacheStatus.Text = "Could not be checked";
			return;
		}
		Mark(inputsStatus, s.Inputs, "Ready for this graphics card and driver", "Will be made at the next start (about a minute)");
		Mark(cacheStatus, s.Cache, "Done for this graphics card and driver", "Not done for this graphics card and driver");
	}

	static void Mark(Label label, bool good, string yes, string no) {
		label.Text      = (good ? "✔ " : "✖ ") + (good ? yes : no);
		label.ForeColor = good ? Good : Warn;
	}

	void StartPrecompile() {
		if (Current is not { } game || !ShadersSupported(game)) return;
		if (running != null) {
			MessageBox.Show(this, "Close the game first: the precompile needs the whole CPU.", Text);
			return;
		}
		if (PrecompileRunning) return;
		var script = Path.Combine(Root, "precompile-windows.ps1");
		if (!File.Exists(script)) {
			MessageBox.Show(this, "precompile-windows.ps1 is missing in the emulator folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		// The author's estimate: 44 minutes on 22 threads.
		var minutes = (int)Math.Ceiling(44.0 * 22 / Environment.ProcessorCount / 10) * 10;
		if (MessageBox.Show(this,
		                    $"Precompile every shader of {game.Name}?\n\nIt takes about {minutes} minutes on this PC with the CPU fully loaded; its own window shows the progress. " +
		                    "Closing that window stops it, and the next run continues where it stopped. Don't play meanwhile.",
		                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			return;
		// Every CPU (the script otherwise reads a launch.json this folder may not have).
		var mask = Environment.ProcessorCount >= 63 ? long.MaxValue : (1L << Environment.ProcessorCount) - 1;
		// An error stays on screen until Enter; a finished run closes its window after 10 seconds.
		var command = "try { & " + PsQuote(script) + " -Game " + PsQuote(game.Folder) + " -Affinity " + mask +
		              "; Write-Host ''; Write-Host 'Done. This window closes in 10 seconds.'; Start-Sleep 10 } " +
		              "catch { Write-Host $_; Write-Host ''; Read-Host 'The precompile stopped with an error. Press Enter to close' }";
		var start = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"") {
			WorkingDirectory = Root, UseShellExecute = true,
		};
		try {
			precompileProcess = Process.Start(start);
			precompileProcess.EnableRaisingEvents = true;
			precompileProcess.Exited += (_, _) => {
				if (!IsDisposed && IsHandleCreated) BeginInvoke(async () => {
					UpdateRunningState();
					await RefreshShaderStatus();
				});
			};
		} catch (Exception ex) {
			MessageBox.Show(this, "The precompile could not be started:\n" + ex.Message, Text);
			return;
		}
		UpdateRunningState();
		_ = RefreshShaderStatus();
	}

	static string PsQuote(string text) => "'" + text.Replace("'", "''") + "'";

	// Windows ends a program that asks for more than RAM and the page file can hold: the emulator commits
	// about 34 GB (run-windows.ps1's check).
	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	struct MemoryStatus {
		public uint  Length, Load;
		public ulong TotalPhysical, AvailablePhysical, TotalCommit, AvailableCommit, TotalVirtual, AvailableVirtual, Extended;
	}

	[System.Runtime.InteropServices.DllImport("kernel32.dll")]
	static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

	static string MemoryWarning() {
		var status = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatus>() };
		if (!GlobalMemoryStatusEx(ref status)) return null;
		const double GB = 1024.0 * 1024 * 1024;
		if (status.AvailableCommit / GB >= 34) return null;
		return $"Low memory: Windows can give programs {status.AvailableCommit / GB:0} GB more (RAM + page file) and the emulator needs about 34 GB. Close other programs if the game exits by itself.";
	}

	// Each run's log is kept in _Build\run-logs (the newest 20).
	void ArchiveLog() {
		try {
			if (!File.Exists(LogPath)) return;
			var folder = Path.Combine(Root, "_Build", "run-logs");
			Directory.CreateDirectory(folder);
			var name = (Current?.TitleId ?? "game") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";
			File.Copy(LogPath, Path.Combine(folder, name), true);
			foreach (var old in new DirectoryInfo(folder).GetFiles("*-????????-??????.log").OrderByDescending(f => f.LastWriteTime).Skip(20))
				old.Delete();
		} catch {
		}
	}

	// ---- Running the game ----

	(string Exe, string Arguments, Dictionary<string, string> Environment) BuildLaunch(GameEntry game) {
		var o      = game.Options;
		var des    = Titles.IsDemonsSouls(game);
		var builds = Builds();
		var exe    = (builds.FirstOrDefault(b => b.Id == o.Build).Path) ?? builds.FirstOrDefault().Path;
		var env    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		void Set(string pair) {
			var at = pair.IndexOf('=');
			if (at > 0) env[pair[..at].Trim()] = pair[(at + 1)..].Trim();
		}

		var live = Path.Combine(Root, "_Build", "pgo", "live-commands.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(live));
		env["KYTY_LIVE_FILE"]        = live;
		env["KYTY_HITCH_LOG_MS"]     = "100";
		env["KYTY_DRIVER_CACHE_KEY"] = DriverCacheKey;
		if (o.ShaderWarmup) {
			env["KYTY_SHADER_WARMUP"]         = "1";
			env["KYTY_SHADER_WARMUP_SECONDS"] = "60";
			env["KYTY_SHADER_WARMUP_THREADS"] = Environment.ProcessorCount.ToString();
		}
		if (o.Vrr) env["KYTY_VRR"] = "1";
		if (o.FpsCounter) env["KYTY_FPS_HUD"] = "1";
		if (o.AspectFit) env["KYTY_PRESENT_ASPECT"] = "fit";
		if (o.Profile != "safe") {
			foreach (var pair in Titles.RecommendedSwitches) Set(pair);
			// The render thread on the physical cores except core 0 (Windows interrupts land on CPU 0).
			if (Environment.ProcessorCount >= 4)
				env["KYTY_RENDER_CPUS"] = string.Join(",", Enumerable.Range(1, Environment.ProcessorCount / 2 - 1).Select(i => i * 2));
		}
		if (des) {
			var convars = new List<string>();
			if (o.SkipGpuCulling) convars.Add("doIndexBufferCulling=false");
			foreach (var (name, on) in CurrentEffectsOf(o))
				if (!on) convars.Add(name + "=false");
			if (convars.Count > 0) env["KYTY_GAME_CONVARS"] = string.Join(",", convars);
			if (o.GpuBufferPages) env["KYTY_GPU_BUFFER_PAGES"] = "1";
			if (o.FrameGen > 0) {
				env["KYTY_FRAMEGEN"] = o.FrameGen.ToString();
				// A release folder: NVIDIA's Streamline DLLs (not redistributable) unpacked by the user into
				// streamline\bin\x64 next to the emulator; a source tree finds them under _Build\deps by itself.
				var streamline = Path.Combine(Root, "streamline", "bin", "x64");
				if (File.Exists(Path.Combine(streamline, "sl.interposer.dll"))) env["KYTY_STREAMLINE_DIR"] = streamline;
			}
			var aot = Path.Combine(Root, @"_Build\srt-aot\windows-libraries\train-20261004\srt-aot.dll");
			if (o.Profile != "safe" && File.Exists(aot)) env["KYTY_SRT_AOT_LIBRARY"] = aot;
		}
		foreach (var line in o.ExtraEnvironment.Split('\n')) {
			var pair = line.Trim();
			var at   = pair.IndexOf('=');
			if (at <= 0) continue;
			var key = pair[..at].Trim();
			if (pair[(at + 1)..].Trim().Length == 0) env.Remove(key);
			else Set(pair);
		}

		var args = new List<string> {
			"--game", Quote(game.Folder), "--screen-width", o.Width.ToString(), "--screen-height", o.Height.ToString(),
			"--console-language", o.Language.ToString(),
		};
		args.AddRange(new[] { "--present-mode", o.PresentMode });
		if (o.Fullscreen) args.Add("--fullscreen");
		if (o.RedZone) args.Add("--redzone");
		if (o.UserName.Length > 0 && o.UserName != "Kyty") args.AddRange(new[] { "--user-name", Quote(o.UserName) });
		if (o.GamePatch.Length > 0 && File.Exists(o.GamePatch)) args.AddRange(new[] { "--game-patch", Quote(o.GamePatch) });
		if (o.ExtraArguments.Length > 0) args.Add(o.ExtraArguments);
		return (exe, string.Join(" ", args), env);
	}

	static IEnumerable<(string, bool)> CurrentEffectsOf(GameOptions o) {
		var effects = o.Effects.Count == 0 ? Titles.PresetEffects(o.Preset) : o.Effects;
		return effects.Select(pair => (pair.Key, pair.Value));
	}

	static string Quote(string text) => "\"" + text + "\"";

	async void Play() {
		if (Current is not { } game || running != null || starting) return;
		if (PrecompileRunning) {
			MessageBox.Show(this, "The shader precompile is still running: wait for its window to finish, or close it to stop it.", Text);
			return;
		}
		starting = true;
		UpdateRunningState();
		try {
			await PrepareShaders(game);
		} finally {
			starting = false;
		}
		Launch(game);
	}

	// As run-windows.ps1 before every start: the background shader preparation's inputs, made again when
	// they are not this graphics card's and driver's (the first start, a driver update).
	async Task PrepareShaders(GameEntry game) {
		if (!ShadersSupported(game) || !game.Options.ShaderWarmup) return;
		status.ForeColor = Color.Black;
		status.Text      = "Checking the shaders...";
		var state = await ShaderStatus(game);
		if (state is not { Inputs: false }) return;
		status.Text = "Preparing the shaders for this graphics card (after a driver update; about a minute)...";
		var (code, _) = await RunTool($"--game {Quote(game.Folder)} --seeds {Quote(SeedsPath())} --no-pipelines --static-inputs --threads {Environment.ProcessorCount}");
		if (code != 0) status.Text = "Preparing the shaders failed; starting without the background preparation.";
	}

	void Launch(GameEntry game) {
		if (!Directory.Exists(game.Folder)) {
			MessageBox.Show(this, $"The game folder is missing:\n{game.Folder}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		var (exe, arguments, environment) = BuildLaunch(game);
		if (exe == null || !File.Exists(exe)) {
			MessageBox.Show(this, $"No emulator found in\n{Root}\n\nSet the emulator folder in the Advanced tab.", Text,
			                MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		if (Process.GetProcessesByName("kyty_emulator").Length > 0) {
			MessageBox.Show(this, "The emulator is already running. Close the game window first.", Text,
			                MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		// cmd keeps the game's output in the log file; /s takes the quoted line as it is.
		var start = new ProcessStartInfo("cmd.exe") {
			Arguments        = $"/s /c \"{Quote(exe)} {arguments} > {Quote(LogPath)} 2>&1\"",
			WorkingDirectory = Root,
			UseShellExecute  = false,
			CreateNoWindow   = true,
		};
		foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("KYTY_", StringComparison.OrdinalIgnoreCase)).ToList())
			start.Environment.Remove(key);
		foreach (var (key, value) in environment) start.Environment[key] = value;
		try {
			running = Process.Start(start);
		} catch (Exception ex) {
			MessageBox.Show(this, "The emulator could not be started:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			return;
		}
		running.EnableRaisingEvents = true;
		running.Exited += (_, _) => {
			if (!IsDisposed && IsHandleCreated) BeginInvoke(GameExited);
		};
		var fg = Titles.IsDemonsSouls(game) && game.Options.FrameGen > 0 ? $", Frame Generation {game.Options.FrameGen + 1}x" : "";
		var memory = MemoryWarning();
		status.ForeColor = memory == null ? Color.Black : Warn;
		status.Text      = memory ?? $"{game.Name} is running{fg}. Close the game window to quit.";
		UpdateRunningState();
	}

	void GameExited() {
		if (running == null) return;
		var code = 0;
		try {
			code = running.ExitCode;
		} catch {
		}
		running.Dispose();
		running = null;
		ArchiveLog();
		status.ForeColor = code == 0 ? Color.Black : Warn;
		status.Text      = code == 0 ? "The game was closed." : $"The game stopped with error code {code}. \"Open log\" shows what happened.";
		UpdateRunningState();
		_ = RefreshShaderStatus();
	}

	void UpdateRunningState() {
		var active = running != null || starting;
		play.Enabled      = !active && Current != null;
		play.BackColor    = play.Enabled ? Accent : Color.FromArgb(160, 180, 200);
		stop.Enabled      = running != null;
		measure.Enabled   = running != null;
		shadersPanel.Enabled = !active && !PrecompileRunning && ShadersSupported(Current);
		gameList.Enabled  = !active;
		if (!active && string.IsNullOrEmpty(status.Text)) status.Text = "Ready.";
	}

	void StopGame() {
		if (running == null) return;
		if (MessageBox.Show(this,
		                    "Force-close the game?\n\nClosing the game window normally is better: it saves the shader cache, so the next start is smoother.",
		                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			return;
		try {
			running.Kill(entireProcessTree: true);
		} catch {
		}
	}

	void OpenLog() {
		if (!File.Exists(LogPath)) {
			MessageBox.Show(this, "There is no log yet: it is written while a game runs.", Text);
			return;
		}
		Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
	}

	// The emulator's live `measure` command: real frames per second over 8 seconds, without generated frames.
	async Task MeasureFps() {
		if (running == null) return;
		measure.Enabled = false;
		status.ForeColor = Color.Black;
		status.Text     = "Measuring for 8 seconds... keep the camera still for a fair number.";
		var id   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var live = Path.Combine(Root, "_Build", "pgo", "live-commands.txt");
		string result = null;
		try {
			await File.WriteAllTextAsync(live, $"id {id}\nmeasure 8 launcher\n");
			for (var i = 0; i < 40 && result == null && running != null; i++) {
				await Task.Delay(500);
				result = ReadMeasurement(id);
			}
		} catch (Exception ex) {
			result = "error: " + ex.Message;
		}
		if (running != null) measure.Enabled = true;
		if (result == null) status.Text = "No measurement came back (is the game past the title screen?).";
		else status.Text = result;
	}

	string ReadMeasurement(long id) {
		string text;
		using (var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
		using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
		var line = text.Split('\n').FirstOrDefault(l => l.StartsWith($"LIVE_MEASURE id={id} "));
		if (line == null) return null;
		string Field(string name) {
			var at = line.IndexOf(" " + name + "=", StringComparison.Ordinal);
			if (at < 0) return "?";
			var start = at + name.Length + 2;
			var end   = line.IndexOf(' ', start);
			return (end < 0 ? line[start..] : line[start..end]).Trim();
		}
		if (Field("frames") == "0") return "No game frames to measure yet: measure while playing, past the title screen.";
		return $"Real frame rate: {Field("fps")} fps   (1% low {Field("low1")}, slowest frame {Field("max_ms")} ms)";
	}
}
