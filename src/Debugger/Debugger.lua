print(debug.getinfo(1, "S").source)
Debugger = { breakpoints = { } }

local function _onWebsocketRecieve(message_type, message)
    print("RECIEVE")
    print(message_type, message)

    if message_type == WebSocketMessageType.OPEN then
        Debugger.ws:sendText("Established Connection")
    end
end

function Debugger.init(endpoint)
    print("set hook")
    -- setup lua debugger
    debug.sethook(Debugger._debugHook, "clr")
    
    print("get endpoint")

    -- connect to debug adapter
    if app.params then
        endpoint = endpoint or app.params.debugger_endpoint
    end

    print("create websocket")
    print(endpoint)
    print(_onWebsocketRecieve)

    Debugger.ws = WebSocket{
        url = endpoint,
        deflate = false,
        onreceive = _onWebsocketRecieve
    }

    print("attempt connect")


    Debugger.ws:connect()
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
end


