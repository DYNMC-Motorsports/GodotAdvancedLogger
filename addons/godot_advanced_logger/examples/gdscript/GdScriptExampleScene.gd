extends Node

## Example scene for the GDScript facade.
##
## Run this scene, then press F12 to open the debug overlay and watch the entries arrive.
## Press Space to emit a burst of logs from several channels.

var _hp := 100
var _wave := 1


func _ready() -> void:
	if not GodotLogger.is_available():
		push_error("LogManager autoload not found - enable the plugin and build the C# solution.")
		return

	print("--- GDScript Logger Example ---")
	print("Press F12 to open the debug overlay.")
	print("Press Space to emit logs, Enter to take damage.")
	print("In the Console tab, try: help, gd_heal 25, channels")
	print("-------------------------------")

	# Structured logging: the message stays stable, the data stays queryable.
	GodotLogger.info("Example scene loaded", {
		"scene": name,
		"wave": _wave,
	}, "Example")

	# A live value in the Watch tab.
	GodotLogger.watch("Player HP", func(): return _hp)
	GodotLogger.watch("Wave", func(): return _wave)

	# A console command backed by GDScript.
	GodotLogger.register_command("gd_heal", _cmd_heal,
		"Heals the example player", "gd_heal <amount>")


func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_SPACE:
			_emit_sample_logs()
		elif event.keycode == KEY_ENTER:
			_take_damage(17)


func _emit_sample_logs() -> void:
	GodotLogger.debug("Frame sampled", {"fps": Engine.get_frames_per_second()}, "Perf")
	GodotLogger.info("Wave started", {"wave": _wave, "enemies": _wave * 3}, "Combat")
	GodotLogger.warning("Pathfinding took too long", {"ms": 42.7}, "AI")

	_wave += 1

	# Context values may be any Variant; Vectors, Colors and the like are stored as text.
	GodotLogger.info("Spawn point chosen", {
		"position": Vector2(120, 48),
		"tint": Color.CRIMSON,
		"tags": ["ranged", "elite"],
	}, "Combat")


func _take_damage(amount: int) -> void:
	_hp -= amount

	if _hp <= 0:
		_hp = 100
		GodotLogger.error("Player died", {"wave": _wave}, "Combat")
		return

	# Guarding a hot path: nothing is built when the Combat channel is muted or the
	# minimum level is above Debug.
	if GodotLogger.is_enabled(GodotLogger.Level.DEBUG, "Combat"):
		GodotLogger.debug("Damage applied", {"amount": amount, "hp_left": _hp}, "Combat")


func _cmd_heal(args: Array) -> String:
	if args.is_empty() or not str(args[0]).is_valid_int():
		return "usage: gd_heal <amount>"

	var amount := int(args[0])
	_hp = mini(_hp + amount, 100)

	GodotLogger.info("Healed via console", {"amount": amount, "hp": _hp}, "Combat")
	return "Healed %d. HP is now %d." % [amount, _hp]
