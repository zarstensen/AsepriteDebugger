
ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

print("!<TEST LOG MESSAGE>!")

ASEDEB.stopTest()
ASEDEB.waitForServerStop()
ASEDEB.Debugger.deinit()
