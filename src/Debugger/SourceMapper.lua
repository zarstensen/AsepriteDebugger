--- Static class holding helper methods for mapping source file paths to installed source file paths, and vice versa.
---@class SourceMapper
local P = { }

--- Maps the passed source file, to its actual installed location in the aseprite user cofig directory.
--- If the file is not part of the installed source code, nil is returned instead.
---@param src any
---@return string | nil
function P.mapSource(src)
    local source_dir = app.fs.normalizePath(ASEDEB.config.source_dir)
    local install_dir = app.fs.normalizePath(ASEDEB.config.install_dir)
    
    local _, end_src_dir_index = src:find(source_dir, 1, true)

    if end_src_dir_index == nil then
        return nil
    end

    return app.fs.normalizePath(app.fs.joinPath(install_dir, src:sub(end_src_dir_index + 1)))
end

--- Maps the passed installed source file to its original source code location.
---@param isntall_src any
---@return string | nil
function P.mapInstalled(isntall_src)
    local source_dir = app.fs.normalizePath(ASEDEB.config.source_dir)
    local install_dir = app.fs.normalizePath(ASEDEB.config.install_dir)
    
    local _, end_install_dir_index = isntall_src:find(install_dir, 1, true)

    if end_install_dir_index == nil then
        return nil
    end

    return app.fs.normalizePath(app.fs.joinPath(source_dir, isntall_src:sub(end_install_dir_index + 1)))
end

return P
