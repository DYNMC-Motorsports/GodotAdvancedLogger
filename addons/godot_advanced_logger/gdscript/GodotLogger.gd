class_name GodotLogger
extends RefCounted

## Static GDScript facade for Godot Advanced Logger.
##
## Forwards to the C# [code]LogManager[/code] autoload, so GDScript projects get the same
## channels, filters, writers and Seq streaming as C# ones.
##
## [codeblock]
## GodotLogger.info("Player damaged", {"damage": 12, "enemy": "Slime"}, "Combat")
## GodotLogger.error("Save failed", {"path": path}, "IO")
##
## if GodotLogger.is_enabled(GodotLogger.Level.DEBUG, "Physics"):
##     GodotLogger.debug("Collision", {"velocity": velocity})
## [/codeblock]
##
## [b]Note:[/b] this does not remove the .NET requirement. The plugin core is C#, so the
## project must still be a Godot .NET (Mono) build with the C# solution compiled.

## Severity levels, mirroring the C# [code]LogLevel[/code] enum.
enum Level {
	DEBUG = 0,
	INFO = 1,
	WARNING = 2,
	ERROR = 3,
}

const _DEFAULT_CHANNEL := "General"
const _MANAGER_PATH := "/root/LogManager"
const _SCREEN_PATH := "/root/DebugScreen"

# Methods are invoked through Object.call() rather than directly. GDScript's static
# analyser only knows these nodes as Node, and cannot see methods added by a C# script -
# a direct manager.GdWrite(...) would fail to resolve at parse time.
const _M_WRITE := &"GdWrite"
const _M_IS_ENABLED := &"GdIsEnabled"
const _M_GET_MIN_LEVEL := &"GdGetMinLevel"
const _M_SET_MIN_LEVEL := &"GdSetMinLevel"
const _M_MUTE := &"MuteChannel"
const _M_UNMUTE := &"UnmuteChannel"
const _M_IS_MUTED := &"IsChannelMuted"
const _M_REGISTER_COMMAND := &"GdRegisterCommand"
const _M_WATCH := &"GdWatch"
const _M_UNWATCH := &"Unwatch"

# Important! Please change the path below if you move the plugin to a different location!
const _BRIDGE_SCRIPT := preload("res://addons/godot_advanced_logger/gdscript/GodotLoggerBridge.gd")

const _B_SET_WATCH := &"set_watch"
const _B_REMOVE_WATCH := &"remove_watch"
const _B_SET_COMMAND := &"set_command"
const _B_GET_WATCH := &"_get_watch"
const _B_DISPATCH_COMMAND := &"_dispatch_command"

static var _manager_node: Node = null
static var _screen_node: Node = null
static var _bridge: RefCounted = null
static var _warned_missing := false
static var _warned_stale := false


# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------

## Logs at [code]Level.DEBUG[/code].
##
## Returns immediately in release builds. The C# API strips [code]Debug()[/code] calls at
## compile time via [code][Conditional("DEBUG")][/code]; GDScript cannot be stripped, so
## this check is the closest equivalent. Use [method info] for entries that must survive
## into a release build.
static func debug(message: String, context: Dictionary = {}, channel: String = _DEFAULT_CHANNEL) -> void:
	if not OS.is_debug_build():
		return
	_write(Level.DEBUG, message, context, channel)


## Logs at [code]Level.INFO[/code].
static func info(message: String, context: Dictionary = {}, channel: String = _DEFAULT_CHANNEL) -> void:
	_write(Level.INFO, message, context, channel)


## Logs at [code]Level.WARNING[/code].
static func warning(message: String, context: Dictionary = {}, channel: String = _DEFAULT_CHANNEL) -> void:
	_write(Level.WARNING, message, context, channel)


## Logs at [code]Level.ERROR[/code].
static func error(message: String, context: Dictionary = {}, channel: String = _DEFAULT_CHANNEL) -> void:
	_write(Level.ERROR, message, context, channel)


## Returns whether an entry at [param level] on [param channel] would actually be written.
##
## Worth calling before building an expensive context dictionary in a hot path:
## [codeblock]
## if GodotLogger.is_enabled(GodotLogger.Level.DEBUG, "Physics"):
##     GodotLogger.debug("Contacts", {"bodies": _collect_bodies()})
## [/codeblock]
static func is_enabled(level: Level, channel: String = _DEFAULT_CHANNEL) -> bool:
	var manager := _get_manager()
	if manager == null:
		return false
	return bool(manager.call(_M_IS_ENABLED, level, channel))


## Whether the C# logger is reachable. False if the plugin is disabled or not yet built.
static func is_available() -> bool:
	return _get_manager() != null


# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

## The minimum level currently being written, as a [enum Level] value.
static func get_min_level() -> int:
	var manager := _get_manager()
	if manager == null:
		return Level.DEBUG
	return int(manager.call(_M_GET_MIN_LEVEL))


## Raises or lowers the minimum level at runtime.
static func set_min_level(level: Level) -> void:
	var manager := _get_manager()
	if manager != null:
		manager.call(_M_SET_MIN_LEVEL, level)


## Silences a channel until [method unmute_channel] is called.
static func mute_channel(channel: String) -> void:
	var manager := _get_manager()
	if manager != null:
		manager.call(_M_MUTE, channel)


## Re-enables a previously muted channel.
static func unmute_channel(channel: String) -> void:
	var manager := _get_manager()
	if manager != null:
		manager.call(_M_UNMUTE, channel)


## Whether [param channel] is currently muted.
static func is_channel_muted(channel: String) -> bool:
	var manager := _get_manager()
	if manager == null:
		return false
	return bool(manager.call(_M_IS_MUTED, channel))


# ---------------------------------------------------------------------------
# Debug screen
# ---------------------------------------------------------------------------

## Registers a command for the in-game console (F12 -> Console).
##
## [param handler] receives one [Array] of string arguments and returns the text to print;
## return nothing to print nothing.
## [codeblock]
## GodotLogger.register_command("give_gold", func(args):
##     if args.is_empty():
##         return "usage: give_gold <amount>"
##     Player.gold += int(args[0])
##     return "Gave %s gold." % args[0],
##     "Adds gold to the player", "give_gold <amount>")
## [/codeblock]
static func register_command(name: String, handler: Callable, description: String = "", usage: String = "") -> void:
	if not handler.is_valid():
		push_error("GodotLogger.register_command: '%s' was given an invalid Callable." % name)
		return

	var screen := _get_screen()
	if screen == null:
		return

	# The handler stays on this side; C# gets a plain method callable on the relay and
	# passes the command name back to it. See GodotLoggerBridge.gd.
	var bridge := _get_bridge()
	bridge.call(_B_SET_COMMAND, name, handler)

	screen.call(_M_REGISTER_COMMAND, name, Callable(bridge, _B_DISPATCH_COMMAND), description, usage)


## Displays a live value in the Watch panel. [param provider] takes no arguments.
##
## Providers are polled roughly ten times a second while the panel is visible, so keep
## them cheap.
## [codeblock]
## GodotLogger.watch("Player HP", func(): return _player.health)
## [/codeblock]
static func watch(label: String, provider: Callable) -> void:
	if not provider.is_valid():
		push_error("GodotLogger.watch: '%s' was given an invalid Callable." % label)
		return

	var screen := _get_screen()
	if screen == null:
		return

	# The provider stays on this side; C# gets a plain method callable on the relay and
	# passes the label back to it. See GodotLoggerBridge.gd.
	var bridge := _get_bridge()
	bridge.call(_B_SET_WATCH, label, provider)

	screen.call(_M_WATCH, label, Callable(bridge, _B_GET_WATCH))


## Removes a watch added with [method watch].
static func unwatch(label: String) -> void:
	if _bridge != null:
		_bridge.call(_B_REMOVE_WATCH, label)

	var screen := _get_screen()
	if screen != null:
		screen.call(_M_UNWATCH, label)


# ---------------------------------------------------------------------------
# Internals
# ---------------------------------------------------------------------------


static func _write(level: Level, message: String, context: Dictionary, channel: String) -> void:
	var manager := _get_manager()
	if manager == null:
		return

	# Gate before walking the stack: get_stack() is not free, and an entry that will be
	# discarded should not pay for it.
	if not bool(manager.call(_M_IS_ENABLED, level, channel)):
		return

	var frame := _caller_frame()
	manager.call(_M_WRITE, level, channel, message, context, frame[0], frame[1], frame[2])


## Returns [source, function, line] of the first frame outside this script.
##
## Replaces the [code][CallerFilePath][/code] attributes the C# API relies on, which the
## compiler fills in at the call site and GDScript has no equivalent for.
## [method @GDScript.get_stack] returns an empty array outside a debug session, in which
## case the caller fields are left blank rather than guessed at.
static func _caller_frame() -> Array:
	for frame in get_stack():
		var source := str(frame.get("source", ""))

		# Skip our own frames by name rather than by a fixed index, so adding or removing
		# an internal helper cannot silently start reporting the wrong line.
		if source.ends_with("GodotLogger.gd"):
			continue

		return [source, str(frame.get("function", "")), int(frame.get("line", 0))]

	return ["", "", 0]


static func _get_manager() -> Node:
	if is_instance_valid(_manager_node):
		return _manager_node

	_manager_node = _find_autoload(_MANAGER_PATH)

	if _manager_node == null:
		if not _warned_missing:
			_warned_missing = true
			push_warning(
				"GodotLogger: '%s' not found. Enable the Godot Advanced Logger plugin and build " % _MANAGER_PATH
				+ "the C# solution (Project -> Tools -> C# -> Build). Log calls are ignored until then."
			)
		return null

	# The autoload exists but predates the GDScript bridge - almost always a stale build.
	if not _manager_node.has_method(_M_WRITE):
		if not _warned_stale:
			_warned_stale = true
			push_warning(
				"GodotLogger: LogManager is missing '%s'. Rebuild the C# solution " % _M_WRITE
				+ "(Project -> Tools -> C# -> Build); the compiled assembly predates GDScript support."
			)
		_manager_node = null
		return null

	return _manager_node


## The relay that keeps GDScript callables on this side of the language boundary.
##
## Created on first use, so projects that only log never allocate it. Methods are invoked
## through [method Object.call] because the relay has no [code]class_name[/code] - adding
## one would put a second global symbol in the user's namespace for an internal type.
static func _get_bridge() -> RefCounted:
	if _bridge == null:
		_bridge = _BRIDGE_SCRIPT.new()
	return _bridge


static func _get_screen() -> Node:
	if is_instance_valid(_screen_node):
		return _screen_node

	_screen_node = _find_autoload(_SCREEN_PATH)
	return _screen_node


static func _find_autoload(path: String) -> Node:
	var loop := Engine.get_main_loop()
	if loop is SceneTree:
		return (loop as SceneTree).root.get_node_or_null(path)
	return null
