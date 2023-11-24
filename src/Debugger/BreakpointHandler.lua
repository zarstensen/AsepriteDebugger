local SourceMapper = require 'SourceMapper'

--- Debugger message handler responsible for handling all breakpoint related requests, including generating stacktraces.
---@class BreakpointHandler
---@field breakpoints table<string, table<number, number>> table containing all breakpoints for all source files.
--- mapped as [normalized source file path] -> [linue number] -> [breakpoint id].
--- so in order to check if the source file 's' has a breakpoint at line number 'l',
--- one should check if breakpoints[s][l] is nil, where s is the normalized source file path (app.fs.normalizePath).
local P = {
    curr_breakpoint_id = 0,
    breakpoints = {},
}

--- Registers relevant message handles in the debugger.
---@param handles table<fun(request: table, response: table, args: table), boolean>
function P.register(handles)
    handles[P.setBreakpoints] = true
end

function P.onDebugHook(event, line)
    -- check if file has breakpoints

    local file_info = debug.getinfo(ASEDEB.Debugger.HANDLER_DEPTH_OFFSET, "nS")
    
    local src_key = app.fs.normalizePath(file_info.source:sub(2))

    if file_info.source:sub(1, 1) ~= '@' or not P.breakpoints[src_key] then
        return
    end

    -- check if current line is a breakpoint
    if event == 'line' and P.breakpoints[src_key][line] then
        ASEDEB.Debugger.stop('breakpoint')
    end
end

---@param args table
---@param response Response
function P.setBreakpoints(args, response)
    -- all paths need to point to the actual installed location in the aseprite user config folder,
    -- as we use these files to check for breakpoint validity.
    local response_body = { breakpoints = { } }
    local mapped_source = SourceMapper.map(args.source.path, ASEDEB.config.install_dir, ASEDEB.config.source_dir)

    if mapped_source == nil then
        response:sendError(ASEDEB.Debugger.ERR_INVALID_SRC_FILE, 'Invalid Source',
            string.format("The path '%s' could not be mapped to a corresponding source file.", args.source.path))
        return
    end

    P.breakpoints[mapped_source] = { }

    for _, breakpoint in ipairs(args.breakpoints) do
        
        local valid_line = P.validBreakpointLine(breakpoint.line, args.source.path)

        if valid_line then
            P.curr_breakpoint_id = P.curr_breakpoint_id + 1
            P.breakpoints[mapped_source][valid_line] = P.curr_breakpoint_id

            table.insert(response_body.breakpoints, {
                verified = true,
                id = P.curr_breakpoint_id,
                line = valid_line,
                source = args.source
            })
        end
    end

    response:send(response_body)
end



--- Returns a line in the passed file, which contains lua code.
--- The line is guaranteed to be the closest valid breakpoint line, after or at the passed line.
---@param line number
---@param file string
function P.validBreakpointLine(line, file)
    local file_handle, err = io.open(file, 'r')

    local file_line

    local is_inside_comment = false

    local multi_line_comment_start = "^%s*%-%-%[%[.*$"
    local multi_line_comment_end = "^%.*%]%].*$"
    
    -- read lines up until target line, keeping track of multiline comments.
    for i=1,line do
        file_line = file_handle:read("l")
                
        if file_line:match(multi_line_comment_start) then
            is_inside_comment = true
        end

        if is_inside_comment and file_line:match(multi_line_comment_end) then
            is_inside_comment = false
        end

    end

    ---@type number | nil
    local valid_line = line

    -- loop over all lines in source file, and check if they contain code (not a comment or whitespace only)
    while true do
        if file_line == nil then
            valid_line = nil
            break
        end

        if file_line:match(multi_line_comment_start) then
            is_inside_comment = true
        end

        if is_inside_comment and file_line:match(multi_line_comment_end) then
            is_inside_comment = false

            -- remove end of multiline comment, so next step can check if this line contains any other code.
            file_line = file_line:gsub(".*%]%]", "")
        end

        -- match for non code lines.
        if not is_inside_comment and not file_line:match("^%s*%-%-.*$") and not file_line:match("^%s*$") then
            break
        end

        valid_line = valid_line + 1
        file_line = file_handle:read("l")
    end

    file_handle:close()

    return valid_line
end

return P
