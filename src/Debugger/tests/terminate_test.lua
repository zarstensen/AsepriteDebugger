-- not much to do here other than set up the debugger and then shut it down.
ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

-- simulate app shutdown.
ASEDEB.Debugger.deinit()

ASEDEB.stopTest()
