local SourceMapper = require 'SourceMapper'

--- Debugger message handler responsible for handling all breakpoint related requests, including generating stacktraces.
---@class BreakpointHandler
---@field breakpoints table<string, table<number, number>> table containing all breakpoints for all source files.
--- mapped as [normalized source file path] -> [linue number] -> [breakpoint id].
--- so in order to check if the source file 's' has a breakpoint at line number 'l',
--- one should check if breakpoints[s][l] is nil, where s is the normalized source file path (app.fs.normalizePath).
local P = {
    -- debug.getInfo -[1]-> onStop -[2]-> debugHook -[3]-> relevant code.
    DEPTH_OFFSET = 3,

    curr_breakpoint_id = 0,
    breakpoints = {},
}

--- Registers relevant message handles in the debugger.
---@param handles table<fun(request: table, response: table, args: table), boolean>
function P.register(handles)
    handles[P.setBreakpoints] = true
    handles[P.stackTrace] = true
end

---@param args table
---@param response Response
function P.setBreakpoints(args, response)
    -- all paths need to point to the actual installed location in the aseprite user config folder,
    -- as we use these files to check for breakpoint validity.
    local response_body = { breakpoints = { } }
    local mapped_source = SourceMapper.mapSource(args.source.path)

    if mapped_source == nil then
        response:send(response_body)
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

--- Simply convert the current stacktrace stored at Debugger.stacktrace to a valid debug adapter stacktrace response.
---@param args table
---@param response Response
function P.stackTrace(args, response)
    local stackframes = { }

    if args.levels <= 0 then
        args.levels = nil
    end

    local start_frame = args.startFrame or 0
    local frame_count = args.levels or 1000

    local tail_call_count = 0

    for i=1,math.min(start_frame + frame_count, #ASEDEB.Debugger.stacktrace) do
        local stackframe_id = i - 1
        local deb_stackframe = ASEDEB.Debugger.stacktrace[#ASEDEB.Debugger.stacktrace - stackframe_id]
        
        if i >= start_frame + 1 and i <= start_frame + frame_count then
            local stackframe = {
                name = deb_stackframe.name,
                source = deb_stackframe.source,
                line = deb_stackframe.line,
                column = deb_stackframe.column
            }
            
            stackframe.id = stackframe_id - tail_call_count
            table.insert(stackframes, stackframe)
        end
            
        if deb_stackframe.is_tail_call then
            tail_call_count = tail_call_count + 1
        end

    end

    response:send({
        stackFrames = stackframes,
        totalFrames = #ASEDEB.Debugger.stacktrace
    })
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
