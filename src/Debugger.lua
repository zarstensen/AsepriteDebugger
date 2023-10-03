Debugger = { breakpoints = { } }

function Debugger.init()
    debug.sethook(Debugger._debugHook, "clr")
end
 
function Debugger.deinit()
    debug.sethook(Debugger._debugHook)
end

function Debugger.setBreakpoints(breakpoints)
end

function Debugger.getBreakpoints()
end

function Debugger.getVariables(scope_id, function_id)
end

function Debugger._debugHook(event, line)
    print(event, line)
end
