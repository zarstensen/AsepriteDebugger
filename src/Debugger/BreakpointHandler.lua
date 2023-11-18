local SourceMapper = require 'SourceMapper'

--- Debugger message handler responsible for handling all breakpoint related requests, including generating stacktraces.
---@class BreakpointHandler
---@field breakpoints table<string, table<number, number>> table containing all breakpoints for all source files.
--- mapped as [normalized source file path] -> [linue number] -> [breakpoint id].
--- so in order to check if the source file 's' has a breakpoint at line number 'l',
--- one should check if breakpoints[s][l] is nil, where s is the normalized source file path (app.fs.normalizePath).
---@field stacktrace table[] current stacktrace if stopped, stored as a list of stackframes,
--- where the top stackframe is the last element in the list.
local P = {
    -- debug.getInfo -[1]-> onStop -[2]-> debugHook -[3]-> relevant code.
    DEPTH_OFFSET = 3,

    curr_breakpoint_id = 0,
    breakpoints = {},
    stacktrace = {}
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
        print("VAID LINE: ", valid_line)

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

--- Simply convert the current stacktrace stored at P.stacktrace to a valid debug adapter stacktrace response.
---@param args table
---@param response Response
function P.stackTrace(args, response)
    local stack_frames = { }

    local start_frame = args.startFrame or 0
    local frame_count = args.levels or 1000

    for i=1,frame_count + 1 do
        if #P.stacktrace < i then
            break;
        end

        table.insert(stack_frames, P.stacktrace[start_frame + i])
    end

    response:send({
        stackFrames = stack_frames,
        totalFrames = #stack_frames
    })
end

function P.onStop()
    -- generate stacktrace

    local stack_depth = 0
    local stack_info = debug.getinfo(stack_depth + P.DEPTH_OFFSET, "lnS")

    while stack_info do

        local stack_frame = {
            name = stack_info.name,
            -- id is simply the depth of the frame, as there should be no two stackframes with equal depth.
            -- here depth is how close it is to the entry point.
            id = stack_depth,
            source = {
                path = SourceMapper.mapInstalled(stack_info.short_src)
            },
            line = stack_info.currentline,
            -- column has to be specified here, but the debugger does not currently support detecting where in the line we are stopped,
            -- so we just say its always at the start of the line.
            column = 1
        }

        if not stack_frame.source.path then
            -- source was unable to map to a source location.
            stack_frame.source.path = stack_info.short_src
        end

        if not stack_frame.name then
            stack_frame.name = 'main'
        end
        
        table.insert(P.stacktrace, #P.stacktrace + 1, stack_frame)
        
        stack_depth = stack_depth + 1

        stack_info = debug.getinfo(stack_depth + P.DEPTH_OFFSET, "lnS")
    end

end

function P.onContinue()
    -- stacktrace is only needed when stopped,
    -- so it is cleared here to enable a potential garbage collect to free its memory.
    P.stacktrace = {}
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
