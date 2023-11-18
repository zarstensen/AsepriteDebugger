ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

Global = 'Global'

function funcCreator()
    local up_value = false

    local function func(arg_1, arg_2, ...)
        local local_number = 1
        local local_list_table = { 'a', 'b', a = 1, b = 2}
        local local_aseprite_rect = Rectangle(10, 20, 30, 40)
        up_value = true

        local breakpoint_line

    end

    return func
end

local func = funcCreator()
func('arg_1', 'arg_2', 'vararg')

ASEDEB.stopTest()
ASEDEB.waitForServerStop()
ASEDEB.Debugger.deinit()
