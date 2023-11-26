ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

pcall(error, "pcall error")
xpcall(error, debug.traceback, "xpcall error")
error("error")
