--- Static class holding helper methods for mapping source file paths to installed source file paths, and vice versa.
---@class SourceMapper
local P = { }

--- Maps the passed root directory of the given path, to the passed target root directory.
--- if the path does not live in the passed root directory, nil is returned.
---
--- Example:
---     path = foo/bar/file.txt
---     root_dir = foo/bar/
---     target_root_dir = foobar/
---
---     > P.map(path, root_dir, target_root_dir)
---       foobar/file.txt 
---
---@param path string
---@param root_dir string
--- @param target_root_dir string
---@return string | nil
function P.map(path, root_dir, target_root_dir)
    root_dir = app.fs.normalizePath(root_dir)
    target_root_dir = app.fs.normalizePath(target_root_dir)
    
    local _, end_src_dir_index = path:find(root_dir, 1, true)

    if end_src_dir_index == nil then
        return nil
    end

    local mapped_src = target_root_dir

    -- if the source is a script, then the end_src_dir_index will be equal to the length of src,
    -- which means the substring returned will be empty.
    -- If the second argument of app.fs.joinPath is empty, the path passed as the first argument is converted to a folder,
    -- however we want to retain its file status, as it is a script path, which points directly to the file,
    -- instead of a folder containing all of the source code.
    -- therefore we perform this check here.
    if end_src_dir_index < #path then
        mapped_src = app.fs.joinPath(target_root_dir, path:sub(end_src_dir_index + 1))
    end

    return app.fs.normalizePath(mapped_src)
end

return P
