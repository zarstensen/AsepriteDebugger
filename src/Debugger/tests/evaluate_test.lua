ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

local value = 1

local breakpoint_line

ASEDEB.stopTest()
ASEDEB.waitForServerStop()
ASEDEB.Debugger.deinit()