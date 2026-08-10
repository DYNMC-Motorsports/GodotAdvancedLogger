extends RefCounted

## Internal relay between GDScript callables and the C# debug overlay. Not part of the
## public API, use [GodotLogger] instead.
##
## [b]Why this exists.[/b] A GDScript lambda ([code]func(): ...[/code]) is a "custom"
## callable. Godot's C# marshalling can only represent two kinds of callable: a plain
## object + method pair, or a callable created from a C# delegate. A custom one has
## neither, so it arrives on the C# side as an empty [code]null::null[/code] callable and
## errors the moment it is invoked:
##
##     Attempt to call callable 'null::null' on a null instance.
##
## [method Callable.bind] produces a custom callable too, so binding the label to the
## provider up front would fail the same way.
##
## The fix is to never hand a lambda across the boundary. The lambdas stay in this object,
## and C# receives [code]Callable(self, "_get_watch")[/code] - a plain method callable,
## which survives marshalling - and passes back the label or command name it wants.

var _watches: Dictionary = {}
var _commands: Dictionary = {}


func set_watch(label: String, provider: Callable) -> void:
	_watches[label] = provider


func remove_watch(label: String) -> void:
	_watches.erase(label)


func set_command(name: String, handler: Callable) -> void:
	_commands[name] = handler


## Invoked from C# on every Watch panel refresh.
func _get_watch(label: String) -> Variant:
	var provider: Callable = _watches.get(label, Callable())

	# A lambda capturing a node that has since been freed reports itself invalid. Drop it
	# rather than calling into freed memory once per refresh.
	if not provider.is_valid():
		_watches.erase(label)
		return "<provider unavailable>"

	return provider.call()


## Invoked from C# when a GDScript-registered console command runs.
func _dispatch_command(name: String, args: Array) -> Variant:
	var handler: Callable = _commands.get(name, Callable())

	if not handler.is_valid():
		_commands.erase(name)
		return "Command '%s' is no longer available." % name

	return handler.call(args)
