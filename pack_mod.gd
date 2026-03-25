extends SceneTree

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		push_error("Usage: godot --headless --script pack_mod.gd -- <pack_root> <output_pck>")
		quit(1)
		return

	var pack_root := args[0]
	var output_pck := args[1]
	var packer := PCKPacker.new()
	var err := packer.pck_start(output_pck)
	if err != OK:
		push_error("pck_start failed: %s" % err)
		quit(err)
		return

	_add_directory(packer, pack_root, pack_root)

	err = packer.flush(false)
	if err != OK:
		push_error("flush failed: %s" % err)
	quit(err)


func _add_directory(packer: PCKPacker, root: String, current: String) -> void:
	var dir := DirAccess.open(current)
	if dir == null:
		push_error("Failed to open directory: %s" % current)
		return

	dir.include_hidden = true
	dir.include_navigational = false
	dir.list_dir_begin()
	while true:
		var entry := dir.get_next()
		if entry.is_empty():
			break

		var full_path := current.path_join(entry)
		if dir.current_is_dir():
			_add_directory(packer, root, full_path)
			continue

		var relative_path := full_path.substr(root.length()).trim_prefix("/").trim_prefix("\\")
		relative_path = relative_path.replace("\\", "/")
		var err := packer.add_file("res://%s" % relative_path, full_path)
		if err != OK:
			push_error("Failed to add file %s: %s" % [full_path, err])
	dir.list_dir_end()
